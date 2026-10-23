using System.Diagnostics;
using System.Text.Json;

namespace RaidPilot.Overlay;

internal sealed partial class OverlayForm
{
    private const double SquadSendIntervalSeconds = 0.10;
    private static readonly TimeSpan SquadMemberRetention = TimeSpan.FromSeconds(9);

    private readonly ToolStripMenuItem _squadMenuItem = new("Escouade");
    private readonly ToolStripMenuItem _squadStatusMenuItem = new("Non connectée") { Enabled = false };
    private readonly ToolStripMenuItem _squadCopyCodeMenuItem = new("Copier le code") { Enabled = false };
    private readonly ToolStripMenuItem _squadLeaveMenuItem = new("Quitter l’escouade") { Enabled = false };
    private readonly System.Windows.Forms.Timer _squadMaintenanceTimer = new() { Interval = 1000 };
    private readonly object _squadGate = new();
    private readonly Dictionary<string, (SquadMemberPose Pose, DateTimeOffset ReceivedAt)> _squadMembers =
        new(StringComparer.Ordinal);

    private SquadProfile _squadProfile = SquadProfileStore.LoadOrCreate();
    private SquadRelayClient? _squadClient;
    private long _lastSquadSendAt;
    private bool _squadSendInProgress;
    private bool _squadProfileRefreshInProgress;
    private string _squadConnectionState = "Non connectée";

    private void InitializeSquad()
    {
        _squadMaintenanceTimer.Tick += async (_, _) =>
        {
            PruneSquadMembers();
            await RefreshSquadProfileFromDiskAsync();
        };
        _squadMaintenanceTimer.Start();
        RefreshSquadMenu();
    }

    private async Task RefreshSquadProfileFromDiskAsync()
    {
        if (_squadProfileRefreshInProgress || _formLifetime.IsCancellationRequested)
        {
            return;
        }

        _squadProfileRefreshInProgress = true;
        try
        {
            var stored = SquadProfileStore.LoadOrCreate();
            var roomChanged = !string.Equals(
                stored.RoomCode,
                _squadProfile.RoomCode,
                StringComparison.OrdinalIgnoreCase);
            var nameChanged = !string.Equals(
                stored.Name,
                _squadProfile.Name,
                StringComparison.Ordinal);
            var autoJoinChanged = stored.AutoJoin != _squadProfile.AutoJoin;
            var identityChanged = !string.Equals(
                stored.MemberId,
                _squadProfile.MemberId,
                StringComparison.Ordinal);

            if (!roomChanged && !nameChanged && !autoJoinChanged && !identityChanged)
            {
                return;
            }

            _squadProfile = stored;
            if (identityChanged)
            {
                _visualKeyframeRecognizer.ClearAllKeyframes();
                _ = VisualKeyframeStore.TryLoad(
                    _visualKeyframeRecognizer,
                    VisualKeyframeStore.GetPath(_squadProfile.MemberId));
                _superKeyframeRecognizer.ClearAllKeyframes();
                _ = SuperKeyframeStore.TryLoad(
                    _superKeyframeRecognizer,
                    SuperKeyframeStore.GetPath(_squadProfile.MemberId));
                _lastSuperKeyframeSample = 0;
            }

            if (!_squadProfile.AutoJoin || !_squadProfile.HasRoom)
            {
                if (_squadClient is not null)
                {
                    await LeaveSquadAsync(clearSavedRoom: false);
                    _squadProfile = stored;
                }
                _squadConnectionState = "Non connectée";
                RefreshSquadMenu();
                return;
            }

            var mustReconnect = _squadClient is null
                || roomChanged
                || nameChanged
                || identityChanged;

            if (mustReconnect)
            {
                await ConnectSquadAsync(
                    _squadProfile.RoomCode!,
                    _squadProfile.Name,
                    persist: false,
                    showSuccess: false);
                _squadConnectionState = "Connectée · profil synchronisé";
                RefreshSquadMenu();
            }
        }
        catch
        {
            if (!_formLifetime.IsCancellationRequested)
            {
                _squadConnectionState = "Escouade · synchronisation en reprise";
                RefreshSquadMenu();
            }
        }
        finally
        {
            _squadProfileRefreshInProgress = false;
        }
    }

    private ToolStripMenuItem BuildSquadMenu()
    {
        _squadMenuItem.DropDownItems.Clear();
        _squadMenuItem.DropDownItems.Add(_squadStatusMenuItem);
        _squadMenuItem.DropDownItems.Add(new ToolStripSeparator());
        _squadMenuItem.DropDownItems.Add(
            "Créer une escouade…",
            null,
            async (_, _) => await CreateSquadAsync());
        _squadMenuItem.DropDownItems.Add(
            "Rejoindre une escouade…",
            null,
            async (_, _) => await JoinSquadAsync());

        _squadCopyCodeMenuItem.Click += (_, _) =>
        {
            if (_squadProfile.RoomCode is { } code)
            {
                try
                {
                    Clipboard.SetText(code);
                    _trayIcon.ShowBalloonTip(
                        2500,
                        "RaidPilot — escouade",
                        $"Code copié : {code}",
                        ToolTipIcon.Info);
                }
                catch
                {
                }
            }
        };
        _squadMenuItem.DropDownItems.Add(_squadCopyCodeMenuItem);

        _squadLeaveMenuItem.Click += async (_, _) => await LeaveSquadAsync(clearSavedRoom: true);
        _squadMenuItem.DropDownItems.Add(_squadLeaveMenuItem);
        _squadMenuItem.DropDownOpening += (_, _) => RefreshSquadMenu();
        return _squadMenuItem;
    }

    private async Task TryAutoJoinSquadAsync()
    {
        if (!_squadProfile.AutoJoin || !_squadProfile.HasRoom || _squadClient is not null)
        {
            return;
        }

        try
        {
            await ConnectSquadAsync(
                _squadProfile.RoomCode!,
                _squadProfile.Name,
                persist: false,
                showSuccess: false);
        }
        catch
        {
            _squadConnectionState = "Escouade hors ligne · reconnexion manuelle";
            RefreshSquadMenu();
        }
    }

    private async Task CreateSquadAsync()
    {
        var generated = SquadRoomCode.Generate();
        if (!SquadSetupDialog.TryShow(
                this,
                "Créer une escouade RaidPilot",
                generated,
                _squadProfile.Name,
                codeReadOnly: true,
                out var roomCode,
                out var name))
        {
            return;
        }

        await ConnectSquadWithUiFeedbackAsync(roomCode, name);
    }

    private async Task JoinSquadAsync()
    {
        if (!SquadSetupDialog.TryShow(
                this,
                "Rejoindre une escouade RaidPilot",
                _squadProfile.RoomCode ?? string.Empty,
                _squadProfile.Name,
                codeReadOnly: false,
                out var roomCode,
                out var name))
        {
            return;
        }

        await ConnectSquadWithUiFeedbackAsync(roomCode, name);
    }

    private async Task ConnectSquadWithUiFeedbackAsync(string roomCode, string name)
    {
        var consent = MessageBox.Show(
            this,
            "Activer RaidPilot Squad Sync ?\n\n"
            + "Ton nom choisi, la carte, X/Y/Z et le cap seront relayés via notre serveur Cloudflare RaidPilot aux membres qui possèdent le même code d’escouade. "
            + "Les captures, touches et mouvements de souris restent sur ce PC.\n\n"
            + "Garde le code d’escouade privé.",
            "RaidPilot — escouade",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (consent != DialogResult.Yes)
        {
            return;
        }

        try
        {
            await ConnectSquadAsync(roomCode, name, persist: true, showSuccess: true);
        }
        catch (Exception exception)
        {
            _squadConnectionState = "Connexion impossible";
            RefreshSquadMenu();
            MessageBox.Show(
                $"La connexion à l’escouade a échoué.\n\n{exception.Message}",
                "RaidPilot — escouade",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private async Task ConnectSquadAsync(
        string roomCode,
        string name,
        bool persist,
        bool showSuccess)
    {
        if (!SquadRoomCode.TryNormalize(roomCode, out var normalized))
        {
            throw new ArgumentException("Le code d’escouade n’est pas valide.");
        }

        var formatted = SquadRoomCode.Format(normalized);
        var normalizedName = SquadRelayClient.NormalizeName(name);
        await LeaveSquadAsync(clearSavedRoom: false);

        var client = new SquadRelayClient(
            formatted,
            _squadProfile.MemberId,
            normalizedName);
        client.PoseReceived += OnSquadPoseReceived;
        client.ReceiverConnectionChanged += OnSquadReceiverConnectionChanged;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_formLifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(7));
        try
        {
            await client.StartReceivingAsync(timeout.Token);
        }
        catch
        {
            client.PoseReceived -= OnSquadPoseReceived;
            client.ReceiverConnectionChanged -= OnSquadReceiverConnectionChanged;
            await client.DisposeAsync();
            throw;
        }

        _squadClient = client;
        _squadProfile = _squadProfile with
        {
            Name = normalizedName,
            RoomCode = formatted,
            AutoJoin = true,
        };
        if (persist)
        {
            _ = SquadProfileStore.TrySave(_squadProfile);
        }

        _squadConnectionState = "Connectée";
        _lastSquadSendAt = 0;
        _squadMaintenanceTimer.Start();
        RefreshSquadMenu();

        if (_lastObservation is { } observation)
        {
            var uncertainty = _realtimeEstimator.CurrentPose?.PositionUncertaintyMeters ?? 0.5;
            MaybeSendSquadPose(observation, uncertainty, force: true);
        }

        if (showSuccess)
        {
            _trayIcon.ShowBalloonTip(
                4000,
                "RaidPilot — escouade",
                $"Escouade active · {_squadProfile.RoomCode}\nPartage ce code aux RaidPilot Beacon de tes teammates.",
                ToolTipIcon.Info);
        }
    }

    private async Task LeaveSquadAsync(bool clearSavedRoom)
    {
        var client = _squadClient;
        _squadClient = null;
        if (client is not null)
        {
            client.PoseReceived -= OnSquadPoseReceived;
            client.ReceiverConnectionChanged -= OnSquadReceiverConnectionChanged;
            await client.DisposeAsync();
        }

        lock (_squadGate)
        {
            _squadMembers.Clear();
        }
        await ClearSquadMarkersAsync();

        if (clearSavedRoom)
        {
            _squadProfile = _squadProfile with { RoomCode = null, AutoJoin = false };
            _ = SquadProfileStore.TrySave(_squadProfile);
        }

        _squadConnectionState = "Non connectée";
        RefreshSquadMenu();
    }

    private void WriteSquadDiagnosticSnapshot(
        string eventName,
        SquadMemberPose? pose = null)
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("RAIDPILOT_SQUAD_DIAGNOSTICS"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RaidPilot");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "squad-live-diagnostics.json");
            var json = JsonSerializer.Serialize(
                new
                {
                    eventName,
                    timestampUtc = DateTimeOffset.UtcNow,
                    roomCode = _squadProfile.RoomCode,
                    localMemberId = _squadProfile.MemberId,
                    localName = _squadProfile.Name,
                    currentMap = _currentMap,
                    connected = _squadClient is not null,
                    remote = pose is null
                        ? null
                        : new
                        {
                            pose.MemberId,
                            pose.Name,
                            pose.Map,
                            pose.X,
                            pose.Y,
                            pose.Z,
                            pose.YawDegrees,
                            pose.UncertaintyMeters,
                            pose.SentAtUnixMilliseconds,
                        },
                },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
            // Diagnostic mode must never affect the overlay.
        }
    }

    private void OnSquadReceiverConnectionChanged(bool connected)
    {
        WriteSquadDiagnosticSnapshot(
            connected ? "receiver-connected" : "receiver-disconnected");
        RunOnUiThread(() =>
        {
            if (_squadClient is null)
            {
                return;
            }
            _squadConnectionState = connected ? "Connectée" : "Reconnexion…";
            RefreshSquadMenu();
        });
    }

    internal static bool ShouldRenderSquadPoseForMap(string poseMap, string activeMap) =>
        string.Equals(
            RaidPilotLocalMapHost.NormalizeMapKey(poseMap),
            RaidPilotLocalMapHost.NormalizeMapKey(activeMap),
            StringComparison.OrdinalIgnoreCase);

    private void OnSquadPoseReceived(SquadMemberPose pose)
    {
        WriteSquadDiagnosticSnapshot("pose-received", pose);
        if (string.Equals(pose.MemberId, _squadProfile.MemberId, StringComparison.Ordinal))
        {
            return;
        }

        lock (_squadGate)
        {
            if (_squadMembers.TryGetValue(pose.MemberId, out var previous)
                && pose.SentAtUnixMilliseconds <= previous.Pose.SentAtUnixMilliseconds)
            {
                // Les appels WebView sont asynchrones et une reconnexion peut
                // remettre en circulation un paquet ancien. Ne jamais faire
                // reculer visuellement un teammate vers une pose plus vieille.
                return;
            }

            if (!_squadMembers.ContainsKey(pose.MemberId) && _squadMembers.Count >= 12)
            {
                var oldest = _squadMembers
                    .OrderBy(pair => pair.Value.ReceivedAt)
                    .FirstOrDefault();
                if (!string.IsNullOrEmpty(oldest.Key))
                {
                    _squadMembers.Remove(oldest.Key);
                }
            }
            _squadMembers[pose.MemberId] = (pose, DateTimeOffset.UtcNow);
        }

        RunOnUiThread(() =>
        {
            if (ShouldRenderSquadPoseForMap(pose.Map, _currentMap))
            {
                _ = RenderSquadPoseAsync(pose);
            }
            else
            {
                // A teammate who moved to another map must not remain a ghost
                // on this map just because their foreign-map poses keep arriving.
                _ = RemoveSquadMemberMarkerAsync(pose.MemberId, pose.SentAtUnixMilliseconds);
            }
        });
    }

    private void MaybeSendSquadPose(
        ScreenshotObservation observation,
        double uncertaintyMeters,
        bool force = false)
    {
        var client = _squadClient;
        if (client is null || _squadSendInProgress)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        if (!force
            && _lastSquadSendAt != 0
            && (now - _lastSquadSendAt) / (double)Stopwatch.Frequency < SquadSendIntervalSeconds)
        {
            return;
        }

        _lastSquadSendAt = now;
        var pose = new SquadMemberPose(
            _squadProfile.MemberId,
            _squadProfile.Name,
            _currentMap,
            observation.X,
            observation.Y,
            observation.Z,
            observation.YawDegrees,
            double.IsFinite(uncertaintyMeters)
                ? Math.Clamp(uncertaintyMeters, 0, 1000)
                : 1000,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _ = SendSquadPoseAsync(client, pose);
    }

    private async Task SendSquadPoseAsync(SquadRelayClient client, SquadMemberPose pose)
    {
        _squadSendInProgress = true;
        try
        {
            await client.SendPoseAsync(pose, _formLifetime.Token);
        }
        catch (OperationCanceledException) when (_formLifetime.IsCancellationRequested)
        {
        }
        catch
        {
            RunOnUiThread(() =>
            {
                if (ReferenceEquals(client, _squadClient))
                {
                    _squadConnectionState = "Réception active · envoi en reprise";
                    RefreshSquadMenu();
                }
            });
        }
        finally
        {
            _squadSendInProgress = false;
        }
    }

    private async Task RenderSquadPoseAsync(SquadMemberPose pose)
    {
        if (!_webReady
            || _webView.CoreWebView2 is null
            || !ShouldRenderSquadPoseForMap(pose.Map, _currentMap))
        {
            return;
        }

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                memberId = pose.MemberId,
                name = pose.Name,
                map = pose.Map,
                x = pose.X,
                y = pose.Y,
                z = pose.Z,
                yaw = pose.YawDegrees,
                uncertainty = pose.UncertaintyMeters >= 1000 ? (double?)null : pose.UncertaintyMeters,
                sentAtUnixMs = pose.SentAtUnixMilliseconds,
            });
            await _webView.CoreWebView2.ExecuteScriptAsync(
                $"window.__raidPilotSetSquadPose?.({payload}); void 0;");
        }
        catch
        {
        }
    }

    private async Task RestoreSquadMarkersAsync()
    {
        SquadMemberPose[] poses;
        lock (_squadGate)
        {
            var cutoff = DateTimeOffset.UtcNow - SquadMemberRetention;
            poses = _squadMembers.Values
                .Where(value => value.ReceivedAt >= cutoff
                    && ShouldRenderSquadPoseForMap(value.Pose.Map, _currentMap))
                .Select(value => value.Pose)
                .ToArray();
        }

        foreach (var pose in poses)
        {
            await RenderSquadPoseAsync(pose);
        }
    }

    private async Task ClearSquadMarkersAsync()
    {
        var core = _webView.CoreWebView2;
        if (core is null)
        {
            return;
        }
        try
        {
            await core.ExecuteScriptAsync("window.__raidPilotClearSquad?.(); void 0;");
        }
        catch
        {
        }
    }

    private async Task RemoveSquadMemberMarkerAsync(string memberId, long sentAtUnixMilliseconds)
    {
        var core = _webView.CoreWebView2;
        if (core is null) return;
        try
        {
            var serialized = JsonSerializer.Serialize(memberId);
            await core.ExecuteScriptAsync(
                $"window.__raidPilotRemoveSquadMember?.({serialized}, {sentAtUnixMilliseconds}); void 0;");
        }
        catch
        {
            // A navigation or temporary renderer failure must not interrupt squad tracking.
        }
    }

    private void PruneSquadMembers()
    {
        var cutoff = DateTimeOffset.UtcNow - SquadMemberRetention;
        (string MemberId, long SentAtUnixMilliseconds)[] removed;
        lock (_squadGate)
        {
            removed = _squadMembers
                .Where(pair => pair.Value.ReceivedAt < cutoff)
                .Select(pair => (
                    MemberId: pair.Key,
                    SentAtUnixMilliseconds: pair.Value.Pose.SentAtUnixMilliseconds))
                .ToArray();
            foreach (var member in removed)
            {
                _squadMembers.Remove(member.MemberId);
            }
        }

        foreach (var member in removed)
        {
            _ = RemoveSquadMemberMarkerAsync(
                member.MemberId,
                member.SentAtUnixMilliseconds);
        }
        RefreshSquadMenu();
    }

    private void RefreshSquadMenu()
    {
        var active = _squadClient is not null && _squadProfile.HasRoom;
        var memberCount = 0;
        lock (_squadGate)
        {
            memberCount = _squadMembers.Count(pair =>
                pair.Value.ReceivedAt >= DateTimeOffset.UtcNow - SquadMemberRetention);
        }

        _squadStatusMenuItem.Text = active
            ? $"{_squadConnectionState} · {memberCount} teammate{(memberCount == 1 ? "" : "s")}"
            : _squadConnectionState;
        _squadCopyCodeMenuItem.Enabled = active;
        _squadLeaveMenuItem.Enabled = active;
        _squadMenuItem.Text = active
            ? $"Escouade · {memberCount + 1}"
            : "Escouade";
    }
}

internal sealed class SquadSetupDialog : Form
{
    private readonly TextBox _codeTextBox = new();
    private readonly TextBox _nameTextBox = new();

    private SquadSetupDialog(
        string title,
        string roomCode,
        string playerName,
        bool codeReadOnly)
    {
        Text = title;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(430, 224);
        BackColor = Color.FromArgb(18, 23, 26);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Segoe UI", 9F);

        var heading = new Label
        {
            Text = "RaidPilot Squad Sync",
            Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold),
            ForeColor = Color.FromArgb(112, 232, 255),
            AutoSize = true,
            Location = new Point(20, 16),
        };
        var info = new Label
        {
            Text = codeReadOnly
                ? "Partage ce code aux teammates qui utilisent RaidPilot Beacon."
                : "Entre le code reçu du joueur qui a créé l’escouade.",
            AutoSize = false,
            Location = new Point(22, 51),
            Size = new Size(386, 34),
            ForeColor = Color.Gainsboro,
        };

        var codeLabel = new Label
        {
            Text = "Code d’escouade",
            AutoSize = true,
            Location = new Point(22, 89),
        };
        _codeTextBox.Location = new Point(22, 110);
        _codeTextBox.Size = new Size(250, 26);
        _codeTextBox.Text = roomCode;
        _codeTextBox.ReadOnly = codeReadOnly;
        _codeTextBox.CharacterCasing = CharacterCasing.Upper;

        var nameLabel = new Label
        {
            Text = "Ton nom",
            AutoSize = true,
            Location = new Point(286, 89),
        };
        _nameTextBox.Location = new Point(286, 110);
        _nameTextBox.Size = new Size(122, 26);
        _nameTextBox.MaxLength = 24;
        _nameTextBox.Text = playerName;

        var ok = new Button
        {
            Text = codeReadOnly ? "Créer" : "Rejoindre",
            DialogResult = DialogResult.OK,
            Location = new Point(222, 168),
            Size = new Size(90, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(27, 98, 113),
            ForeColor = Color.White,
        };
        ok.FlatAppearance.BorderColor = Color.FromArgb(112, 232, 255);
        var cancel = new Button
        {
            Text = "Annuler",
            DialogResult = DialogResult.Cancel,
            Location = new Point(318, 168),
            Size = new Size(90, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(40, 45, 48),
            ForeColor = Color.White,
        };

        Controls.AddRange([
            heading,
            info,
            codeLabel,
            _codeTextBox,
            nameLabel,
            _nameTextBox,
            ok,
            cancel,
        ]);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    internal static bool TryShow(
        IWin32Window owner,
        string title,
        string roomCode,
        string playerName,
        bool codeReadOnly,
        out string normalizedRoomCode,
        out string normalizedName)
    {
        normalizedRoomCode = string.Empty;
        normalizedName = string.Empty;
        using var dialog = new SquadSetupDialog(
            title,
            roomCode,
            playerName,
            codeReadOnly);

        while (dialog.ShowDialog(owner) == DialogResult.OK)
        {
            if (!SquadRoomCode.TryNormalize(dialog._codeTextBox.Text, out var normalized))
            {
                MessageBox.Show(
                    dialog,
                    "Le code doit ressembler à RP-ABCD-EFGH-JKLM-NPQR.",
                    "RaidPilot — escouade",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                continue;
            }

            normalizedRoomCode = SquadRoomCode.Format(normalized);
            normalizedName = SquadRelayClient.NormalizeName(dialog._nameTextBox.Text);
            return true;
        }

        return false;
    }
}
