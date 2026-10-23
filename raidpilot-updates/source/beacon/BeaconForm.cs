using System.Diagnostics;
using RaidPilot.Overlay;

namespace RaidPilot.Beacon;

internal sealed class BeaconForm : Form
{
    private const double SendIntervalSeconds = 0.10;
    private const double PendingCaptureMaximumAgeSeconds = 3.0;
    private const double MaximumCommandedStationarySeconds = 1.25;
    private const double SuperKeyframeSampleIntervalSeconds = 0.20;
    private const double SuperKeyframeMinimumUncertaintyMeters = 1.5;
    private const double SuperKeyframeAnchorConsistencyMeters = 4.0;
    private const double SuperKeyframeAnchorConsistencyVerticalMeters = 3.0;
    private const double SuperKeyframeAnchorConsistencyYawDegrees = 20.0;
    private readonly TextBox _roomTextBox = new();
    private readonly TextBox _nameTextBox = new();
    private readonly Label _statusLabel = new();
    private readonly Label _mapLabel = new();
    private readonly Label _poseLabel = new();
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();
    private readonly NotifyIcon _trayIcon = new();
    private readonly System.Windows.Forms.Timer _tickTimer = new() { Interval = 25 };
    private readonly System.Windows.Forms.Timer _folderProbeTimer = new() { Interval = 3000 };
    private readonly EftControlSettings _controls;
    private readonly RealtimePoseEstimator _estimator;
    private readonly RealtimeCalibrationSource _calibrationSource;
    private BeaconAdaptiveCalibration _adaptiveCalibration;
    private EftScreenshotBindingInfo _screenshotBinding;
    private readonly string _settingsPath;
    private readonly HashSet<string> _knownScreenshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly VisualMotionGate _visualMotionGate = new();
    private readonly PrecisionV2VisualSampler _precisionV2Vision = new();
    private readonly PrecisionV2MotionFusion _precisionV2Fusion = new();
    private readonly ToolStripMenuItem _precisionV2Toggle = new("Precision V2 — flux optique 160×90 (expérimental)") { Checked = true, CheckOnClick = true };
    private long _precisionV2LastTurnAt;
    private bool _precisionV2TrackingActive;
    private readonly VisualKeyframeRecognizer _visualKeyframeRecognizer = new();
    private readonly SuperKeyframeRecognizer _superKeyframeRecognizer = new();
    private readonly VisualYawCalibration _visualYawCalibration = new();
    private readonly RawKeyboardTimeline _rawKeyboardTimeline = new();
    private readonly PrecisionYawTimeline _precisionYaw = new();
    private readonly PrecisionMotionIntegrator _precisionMotionIntegrator = new();
    private readonly HashSet<Keys> _rawKeysDown = new();
    private readonly HashSet<Keys> _trackedMovementKeys;
    private RealtimeTrackingPosture _posture;
    private bool _previousDuckKey;
    private bool _previousProneKey;
    private bool _previousJumpKey;
    private bool _duckChordUsed;

    private SquadProfile _profile;
    private SquadRelayClient? _client;
    private EftMapLogWatcher? _mapWatcher;
    private FileSystemWatcher? _screenshotWatcher;
    private string? _screenshotsPath;
    private string? _currentMap;
    private long _lastTick;
    private long _lastSend;
    private long _pendingMouseX;
    private int _pendingRawMouseWheel;
    private int _pendingDuckWheelChord;
    private int _rawRotationActivity;
    private bool _sendInProgress;
    private bool _profileRefreshInProgress;
    private bool _allowClose;
    private bool _rawInputRegistered;
    private bool _controlTrackingActive;
    private bool _middleMouseDown;
    private bool _sprintToggle;
    private bool _walkToggle;
    private bool _aimToggle;
    private bool _previousSprintKey;
    private bool _previousWalkKey;
    private bool _previousAimKey;
    private nint _lastForegroundWindow;
    private bool _tarkovForeground;
    private bool _screenshotBindingWasDown;
    private bool _rawScreenshotBindingDown;
    private bool _playerCalibrationLoaded;
    private long _lastVisualSample;
    private long _lastSuperKeyframeSample;
    private long _lastAutomaticReanchor;
    private double _movementSpeedScale = 1.0;
    private double _commandedStationarySeconds;
    private double _reanchorIdleSeconds;
    private RealtimeMotionEvidence _visualMotionEvidence = RealtimeMotionEvidence.Unknown;
    private bool _previousMovementCommanded;
    private VisualMotionObservation? _lastVisualObservation;
    private PendingSuperKeyframeLearning? _pendingSuperKeyframeLearning;
    private (
        long Timestamp,
        RealtimePose Pose,
        BeaconCalibrationSegment Calibration,
        string? Map,
        bool Automatic)? _pendingCapture;

    internal BeaconForm()
    {
        _settingsPath = SquadProfileStore.DefaultPath;
        _profile = SquadProfileStore.LoadOrCreate(_settingsPath);
        _controls = EftControlSettings.Read();
        var gameFieldOfView = EftGameViewSettings.Read().VerticalFieldOfViewDegrees;
        _visualMotionGate.ConfigureVerticalFieldOfView(gameFieldOfView);
        _precisionV2Vision.ConfigureVerticalFieldOfView(gameFieldOfView);
        _trackedMovementKeys = BuildTrackedMovementKeys(_controls);
        _calibrationSource = new RealtimeCalibrationSource(
            _controls.MouseSensitivity,
            _controls.MouseAimingSensitivity);
        var estimatorSettings = CreateBaseEstimatorSettings();
        var calibrationPath = BeaconAdaptiveCalibration.GetCalibrationPath(_profile.MemberId);
        _playerCalibrationLoaded = RealtimeCalibrationStore.TryLoad(
            calibrationPath,
            estimatorSettings,
            _calibrationSource,
            out var learnedSettings);
        if (_playerCalibrationLoaded)
        {
            estimatorSettings = learnedSettings;
        }
        _estimator = new RealtimePoseEstimator(estimatorSettings);
        _adaptiveCalibration = new BeaconAdaptiveCalibration(
            calibrationPath,
            _calibrationSource);
        _visualKeyframeRecognizer.ClearAllKeyframes();
        _ = VisualKeyframeStore.TryLoad(
            _visualKeyframeRecognizer,
            VisualKeyframeStore.GetPath(_profile.MemberId));
        _superKeyframeRecognizer.ClearAllKeyframes();
        _ = SuperKeyframeStore.TryLoad(
            _superKeyframeRecognizer,
            SuperKeyframeStore.GetPath(_profile.MemberId));
        _screenshotBinding = EftScreenshotBindingReader.Read();

        BuildUi();
        BuildTray();
        ResolveScreenshotsPath();
        InitializeMapWatcher();

        _tickTimer.Tick += OnTick;
        _folderProbeTimer.Tick += async (_, _) =>
        {
            EnsureScreenshotWatcher();
            _screenshotBinding = EftScreenshotBindingReader.Read();
            await RefreshSharedSquadProfileAsync();
        };
        Shown += OnShown;
        FormClosing += OnFormClosing;
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
            }
        };
    }

    private RealtimePoseEstimatorSettings CreateBaseEstimatorSettings() => new()
    {
        HipYawDegreesPerMouseCount = Math.Clamp(_controls.MouseSensitivity * 0.1, 0.005, 0.5),
        AdsYawDegreesPerMouseCount = Math.Clamp(_controls.MouseAimingSensitivity * 0.1, 0.005, 0.5),
    };

    private void ReloadPlayerCalibration(string memberId)
    {
        var settings = CreateBaseEstimatorSettings();
        var path = BeaconAdaptiveCalibration.GetCalibrationPath(memberId);
        _playerCalibrationLoaded = RealtimeCalibrationStore.TryLoad(
            path,
            settings,
            _calibrationSource,
            out var learnedSettings);
        if (_playerCalibrationLoaded)
        {
            settings = learnedSettings;
        }

        _estimator.ApplySettings(settings);
        _adaptiveCalibration = new BeaconAdaptiveCalibration(path, _calibrationSource);
        _visualKeyframeRecognizer.ClearAllKeyframes();
        _ = VisualKeyframeStore.TryLoad(
            _visualKeyframeRecognizer,
            VisualKeyframeStore.GetPath(memberId));
        _superKeyframeRecognizer.ClearAllKeyframes();
        _ = SuperKeyframeStore.TryLoad(
            _superKeyframeRecognizer,
            SuperKeyframeStore.GetPath(memberId));
        _lastSuperKeyframeSample = 0;
        _visualYawCalibration.Reset();
        _pendingSuperKeyframeLearning = null;
        _pendingCapture = null;
        _screenshotBindingWasDown = false;
        _controlTrackingActive = false;
        _rawKeyboardTimeline.Reset();
        _precisionYaw.Reset();
        Interlocked.Exchange(ref _pendingMouseX, 0);
        Interlocked.Exchange(ref _pendingRawMouseWheel, 0);
        Interlocked.Exchange(ref _rawRotationActivity, 0);
    }

    private void BuildUi()
    {
        Text = "RaidPilot Beacon";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(540, 350);
        BackColor = Color.FromArgb(15, 20, 23);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Segoe UI", 9F);

        var title = new Label
        {
            Text = "RAIDPILOT  BEACON",
            Location = new Point(24, 20),
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold),
            ForeColor = Color.FromArgb(112, 232, 255),
        };
        var subtitle = new Label
        {
            Text = "Partage uniquement ta position à ton escouade — aucune carte, aucun overlay.",
            Location = new Point(26, 58),
            Size = new Size(485, 36),
            ForeColor = Color.Gainsboro,
        };

        Controls.Add(title);
        Controls.Add(subtitle);
        Controls.Add(MakeLabel("Code d’escouade", 26, 105));
        _roomTextBox.Location = new Point(26, 126);
        _roomTextBox.Size = new Size(292, 27);
        _roomTextBox.CharacterCasing = CharacterCasing.Upper;
        _roomTextBox.Text = _profile.RoomCode ?? string.Empty;
        Controls.Add(_roomTextBox);

        Controls.Add(MakeLabel("Ton nom", 334, 105));
        _nameTextBox.Location = new Point(334, 126);
        _nameTextBox.Size = new Size(176, 27);
        _nameTextBox.MaxLength = 24;
        _nameTextBox.Text = _profile.Name;
        Controls.Add(_nameTextBox);

        _startButton.Text = "CONNECTER LE BEACON";
        _startButton.Location = new Point(26, 171);
        _startButton.Size = new Size(220, 38);
        StylePrimaryButton(_startButton);
        _startButton.Click += async (_, _) => await StartBeaconFromUiAsync();
        Controls.Add(_startButton);

        _stopButton.Text = "ARRÊTER";
        _stopButton.Location = new Point(256, 171);
        _stopButton.Size = new Size(112, 38);
        _stopButton.Enabled = false;
        StyleSecondaryButton(_stopButton);
        _stopButton.Click += async (_, _) => await StopBeaconAsync(clearAutoJoin: true);
        Controls.Add(_stopButton);

        var minimize = new Button
        {
            Text = "Réduire",
            Location = new Point(378, 171),
            Size = new Size(132, 38),
        };
        StyleSecondaryButton(minimize);
        minimize.Click += (_, _) => Hide();
        Controls.Add(minimize);

        var panel = new Panel
        {
            Location = new Point(26, 226),
            Size = new Size(484, 96),
            BackColor = Color.FromArgb(23, 29, 33),
        };
        _statusLabel.Text = "Déconnecté";
        _statusLabel.Location = new Point(14, 10);
        _statusLabel.Size = new Size(454, 22);
        _statusLabel.ForeColor = Color.FromArgb(255, 202, 90);
        _mapLabel.Text = "Carte : en attente de Tarkov";
        _mapLabel.Location = new Point(14, 37);
        _mapLabel.Size = new Size(454, 20);
        _poseLabel.Text = "Position : appuie sur F8 une fois en raid";
        _poseLabel.Location = new Point(14, 63);
        _poseLabel.Size = new Size(454, 20);
        _poseLabel.ForeColor = Color.Gainsboro;
        panel.Controls.AddRange([_statusLabel, _mapLabel, _poseLabel]);
        Controls.Add(panel);
    }

    private static Label MakeLabel(string text, int x, int y) => new()
    {
        Text = text,
        Location = new Point(x, y),
        AutoSize = true,
        ForeColor = Color.Gainsboro,
    };

    private static void StylePrimaryButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Color.FromArgb(24, 100, 118);
        button.ForeColor = Color.White;
        button.FlatAppearance.BorderColor = Color.FromArgb(112, 232, 255);
    }

    private static void StyleSecondaryButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Color.FromArgb(38, 44, 48);
        button.ForeColor = Color.White;
        button.FlatAppearance.BorderColor = Color.FromArgb(76, 88, 94);
    }

    private void BuildTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Ouvrir RaidPilot Beacon", null, (_, _) =>
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        });
        menu.Items.Add("Arrêter le partage", null, async (_, _) =>
            await StopBeaconAsync(clearAutoJoin: true));
        menu.Items.Add(_precisionV2Toggle);
        _precisionV2Toggle.Click += (_, _) => ResetPrecisionV2();
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quitter", null, async (_, _) =>
        {
            _allowClose = true;
            await StopBeaconAsync(clearAutoJoin: false);
            Close();
        });

        _trayIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)
            ?? SystemIcons.Information;
        _trayIcon.Text = "RaidPilot Beacon";
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.Visible = true;
        _trayIcon.DoubleClick += (_, _) =>
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _rawInputRegistered = RawMouseInput.Register(Handle);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (_rawInputRegistered)
        {
            RawMouseInput.Unregister();
            _rawInputRegistered = false;
        }
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMethods.WmInput
            && RawMouseInput.TryReadPacket(message.LParam, out var packet))
        {
            if (packet.Kind == RawInputPacketKind.Mouse)
            {
                if (CanTrackGameInput())
                {
                    if ((packet.ButtonFlags & NativeMethods.RiMouseMiddleButtonDown) != 0)
                    {
                        _middleMouseDown = true;
                    }
                    if ((packet.ButtonFlags & NativeMethods.RiMouseMiddleButtonUp) != 0)
                    {
                        _middleMouseDown = false;
                    }

                    Interlocked.Add(ref _pendingRawMouseWheel, packet.WheelDelta);
                    if (packet.WheelDelta != 0 && IsKeyDown(_controls.Duck))
                    {
                        Interlocked.Exchange(ref _pendingDuckWheelChord, 1);
                    }

                    // Le bouton du milieu est le free-look EFT: la caméra tourne
                    // sans changer l'axe du corps utilisé pour W/A/S/D. Compter
                    // ces mouvements comme du lacet faisait dériver Beacon.
                    if (!_middleMouseDown)
                    {
                        Interlocked.Add(ref _pendingMouseX, packet.DeltaX);
                        if (packet.DeltaX != 0)
                        {
                            Interlocked.Exchange(ref _rawRotationActivity, 1);
                            var aimingForSample = _controls.AimIsToggle
                                ? _aimToggle
                                : IsKeyDown(_controls.Aim);
                            _precisionYaw.Record(
                                Math.Max(
                                    Stopwatch.GetTimestamp(),
                                    Volatile.Read(ref _lastTick) + 1),
                                packet.DeltaX,
                                aimingForSample);
                        }
                    }
                }
                else
                {
                    _middleMouseDown = false;
                }
            }
            else if (packet.Kind == RawInputPacketKind.Keyboard)
            {
                HandleRawKeyboardPacket(packet);
            }
        }
        base.WndProc(ref message);
    }

    private void HandleRawKeyboardPacket(RawInputPacket packet)
    {
        if (!CanTrackGameInput())
        {
            _rawKeysDown.Clear();
            _rawScreenshotBindingDown = false;
            return;
        }

        var key = packet.Key & Keys.KeyCode;
        if (packet.IsKeyDown)
        {
            _rawKeysDown.Add(key);
        }
        else
        {
            _rawKeysDown.Remove(key);
        }

        if (RawMouseInput.KeyboardRegistered
            && _trackedMovementKeys.Contains(key))
        {
            _rawKeyboardTimeline.Record(
                key,
                packet.IsKeyDown,
                Math.Max(
                    Stopwatch.GetTimestamp(),
                    Volatile.Read(ref _lastTick) + 1));
        }

        var screenshotDown = IsRawScreenshotBindingDown();
        if (screenshotDown
            && !_rawScreenshotBindingDown
            && _pendingCapture is null
            && _estimator.CurrentPose is { } pose
            && !string.IsNullOrWhiteSpace(_currentMap))
        {
            var now = Stopwatch.GetTimestamp();
            _pendingCapture = (
                now,
                pose,
                _adaptiveCalibration.CaptureSegment(pose),
                _currentMap,
                false);
        }

        _rawScreenshotBindingDown = screenshotDown;
    }

    private bool IsRawScreenshotBindingDown()
    {
        if (!_screenshotBinding.IsReady
            || _screenshotBinding.AxisName is not null
            || _screenshotBinding.KeyCodes.Count == 0)
        {
            return false;
        }

        foreach (var keyCode in _screenshotBinding.KeyCodes)
        {
            if (!EftControlSettings.TryMapKeyCode(keyCode, out var mapped)
                || mapped == Keys.None
                || !_rawKeysDown.Contains(mapped & Keys.KeyCode))
            {
                return false;
            }
        }

        return true;
    }

    private async void OnShown(object? sender, EventArgs e)
    {
        EnsureScreenshotWatcher();
        _folderProbeTimer.Start();
        _lastTick = Stopwatch.GetTimestamp();
        _tickTimer.Start();

        if (_profile.AutoJoin && _profile.HasRoom)
        {
            try
            {
                await StartBeaconAsync(_profile.RoomCode!, _profile.Name, showSuccess: false);
            }
            catch
            {
                SetStatus("Connexion automatique impossible", warning: true);
            }
        }
    }

    private async Task RefreshSharedSquadProfileAsync()
    {
        if (_profileRefreshInProgress || _lifetime.IsCancellationRequested)
        {
            return;
        }

        _profileRefreshInProgress = true;
        try
        {
            var stored = SquadProfileStore.LoadOrCreate(_settingsPath);
            var roomChanged = !string.Equals(
                stored.RoomCode,
                _profile.RoomCode,
                StringComparison.OrdinalIgnoreCase);
            var nameChanged = !string.Equals(
                stored.Name,
                _profile.Name,
                StringComparison.Ordinal);
            var autoJoinChanged = stored.AutoJoin != _profile.AutoJoin;
            var identityChanged = !string.Equals(
                stored.MemberId,
                _profile.MemberId,
                StringComparison.Ordinal);

            if (!roomChanged && !nameChanged && !autoJoinChanged && !identityChanged)
            {
                return;
            }

            _profile = stored;
            _roomTextBox.Text = _profile.RoomCode ?? string.Empty;
            _nameTextBox.Text = _profile.Name;
            if (identityChanged)
            {
                ReloadPlayerCalibration(_profile.MemberId);
            }

            if (!_profile.AutoJoin || !_profile.HasRoom)
            {
                if (_client is not null)
                {
                    await StopBeaconAsync(clearAutoJoin: false);
                    _profile = stored;
                }
                SetStatus("Déconnecté · profil partagé mis à jour", warning: true);
                return;
            }

            var mustReconnect = _client is null
                || roomChanged
                || nameChanged
                || identityChanged
                || !string.Equals(
                    _client.RoomCode,
                    _profile.RoomCode,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    _client.Name,
                    _profile.Name,
                    StringComparison.Ordinal);

            if (mustReconnect)
            {
                await StartBeaconAsync(
                    _profile.RoomCode!,
                    _profile.Name,
                    showSuccess: false);
                SetStatus(
                    $"Beacon actif · escouade {_profile.RoomCode}",
                    warning: false);
            }
        }
        catch
        {
            if (!_lifetime.IsCancellationRequested)
            {
                SetStatus("Synchronisation du profil en reprise…", warning: true);
            }
        }
        finally
        {
            _profileRefreshInProgress = false;
        }
    }

    private void ResolveScreenshotsPath()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(docs))
        {
            _screenshotsPath = Path.Combine(docs, "Escape from Tarkov", "Screenshots");
        }
    }

    private void EnsureScreenshotWatcher()
    {
        if (_screenshotWatcher is not null
            || string.IsNullOrWhiteSpace(_screenshotsPath)
            || !Directory.Exists(_screenshotsPath))
        {
            return;
        }

        lock (_knownScreenshots)
        {
            _knownScreenshots.Clear();
            foreach (var path in Directory.EnumerateFiles(_screenshotsPath, "*.png", SearchOption.TopDirectoryOnly))
            {
                _knownScreenshots.Add(Path.GetFileName(path));
            }
        }

        try
        {
            var watcher = new FileSystemWatcher(_screenshotsPath, "*.png")
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = false,
            };
            watcher.Created += OnScreenshotAppeared;
            watcher.Renamed += OnScreenshotAppeared;
            watcher.Error += (_, _) =>
            {
                BeginInvoke(() =>
                {
                    _screenshotWatcher?.Dispose();
                    _screenshotWatcher = null;
                });
            };
            watcher.EnableRaisingEvents = true;
            _screenshotWatcher = watcher;
        }
        catch
        {
        }
    }

    private void OnScreenshotAppeared(object sender, FileSystemEventArgs args)
    {
        var name = Path.GetFileName(args.FullPath);
        lock (_knownScreenshots)
        {
            if (!_knownScreenshots.Add(name))
            {
                return;
            }
        }

        if (!ScreenshotNameParser.TryParse(name, out var observation) || observation is null)
        {
            return;
        }

        BeginInvoke(() =>
        {
            var now = Stopwatch.GetTimestamp();
            var automaticCapture = _pendingCapture is { Automatic: true } pending
                && (now - pending.Timestamp) / (double)Stopwatch.Frequency
                    <= PendingCaptureMaximumAgeSeconds;
            AnchorFromScreenshot(observation);
            _ = RememberVisualKeyframeFromScreenshotAsync(
                args.FullPath,
                observation,
                _currentMap,
                automaticCapture);

            if (!automaticCapture
                && !string.IsNullOrWhiteSpace(_currentMap))
            {
                _pendingSuperKeyframeLearning = new PendingSuperKeyframeLearning(
                    _currentMap!,
                    new VisualKeyframePose(
                        observation.X,
                        observation.Y,
                        observation.Z,
                        observation.YawDegrees),
                    now + (long)Math.Ceiling(1.8 * Stopwatch.Frequency),
                    now + (long)Math.Ceiling(6.0 * Stopwatch.Frequency));
            }
        });
    }

    private void AnchorFromScreenshot(ScreenshotObservation observation)
    {
        var now = Stopwatch.GetTimestamp();
        var hasHistoricalCapture = false;
        RealtimePose? poseAtCapture = null;
        BeaconCalibrationSegment calibrationSegment;

        if (_pendingCapture is { } pending
            && (now - pending.Timestamp) / (double)Stopwatch.Frequency
                <= PendingCaptureMaximumAgeSeconds
            && (pending.Map is null
                || _currentMap is null
                || string.Equals(
                    pending.Map,
                    _currentMap,
                    StringComparison.OrdinalIgnoreCase)))
        {
            hasHistoricalCapture = true;
            poseAtCapture = pending.Pose;
            calibrationSegment = pending.Calibration;
        }
        else
        {
            // The file arrived without a reliably observed screenshot-key
            // transition. Keep the absolute anchor, but do not learn from a
            // segment whose true capture instant is unknown.
            _adaptiveCalibration.MarkTrackingGap();
            calibrationSegment = _adaptiveCalibration.CaptureSegment(
                _estimator.CurrentPose);
        }

        _pendingCapture = null;
        ResetPrecisionV2();

        var learned = _adaptiveCalibration.ObserveExact(
            observation,
            calibrationSegment,
            _estimator);
        if (learned)
        {
            _playerCalibrationLoaded = true;
        }

        if (hasHistoricalCapture
            && poseAtCapture is { } historicalPose
            && _estimator.IsAnchored)
        {
            try
            {
                _estimator.ReanchorFromHistoricalPose(
                    observation,
                    historicalPose);
            }
            catch (InvalidOperationException)
            {
                _estimator.Anchor(observation);
            }
            catch (ArgumentException)
            {
                _estimator.Anchor(observation);
            }
        }
        else
        {
            _estimator.Anchor(observation);
        }

        Interlocked.Exchange(ref _pendingMouseX, 0);
        _reanchorIdleSeconds = 0.0;
        _lastTick = now;

        var residual = _adaptiveCalibration.LastResidualMeters is { } meters
            ? $" · écart corrigé {meters:F1} m"
            : string.Empty;
        var learnedLabel = _playerCalibrationLoaded
            ? " · profil joueur adaptatif actif"
            : string.Empty;
        _poseLabel.Text =
            $"Position F8 recalée : {observation.X:F1}, {observation.Z:F1} · cap {observation.YawDegrees:F0}°"
            + residual
            + learnedLabel;
        _poseLabel.ForeColor = Color.FromArgb(141, 255, 203);

        if (_client is not null)
        {
            _ = SendCurrentPoseAsync(force: true);
        }
    }

    private async Task RememberVisualKeyframeFromScreenshotAsync(
        string screenshotPath,
        ScreenshotObservation observation,
        string? map,
        bool allowSuperKeyframeFromScreenshot)
    {
        if (string.IsNullOrWhiteSpace(map)
            || _lifetime.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var lowResolutionTask =
                _visualMotionGate.CreateLuminanceFrameFromImageWithRetryAsync(
                    screenshotPath,
                    _lifetime.Token);
            Task<byte[]?>? highResolutionTask = allowSuperKeyframeFromScreenshot
                ? HighResolutionVisualSampler.CreateLuminanceFrameFromImageWithRetryAsync(
                    screenshotPath,
                    _lifetime.Token)
                : null;

            var luminance = await lowResolutionTask;
            var exactPose = new VisualKeyframePose(
                observation.X,
                observation.Y,
                observation.Z,
                observation.YawDegrees);
            if (luminance is not null
                && _visualKeyframeRecognizer.AddExactKeyframe(
                    map,
                    luminance,
                    _visualMotionGate.SampleWidth,
                    _visualMotionGate.SampleHeight,
                    exactPose,
                    Stopwatch.GetTimestamp(),
                    out _))
            {
                _ = VisualKeyframeStore.TrySave(
                    _visualKeyframeRecognizer,
                    VisualKeyframeStore.GetPath(_profile.MemberId));
            }

            if (highResolutionTask is not null)
            {
                var superFrame = await highResolutionTask;
                if (superFrame is not null
                    && _superKeyframeRecognizer.AddExactKeyframe(
                        map,
                        superFrame,
                        exactPose,
                        Stopwatch.GetTimestamp(),
                        out _))
                {
                    _ = SuperKeyframeStore.TrySave(
                        _superKeyframeRecognizer,
                        SuperKeyframeStore.GetPath(_profile.MemberId));
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch
        {
            // Visual landmarks are an optional accuracy layer; a failed image
            // read must never affect the authoritative F8 anchor.
        }
    }

    private void InitializeMapWatcher()
    {
        try
        {
            var watcher = new EftMapLogWatcher();
            watcher.MapDetected += (_, args) => BeginInvoke(() =>
            {
                _currentMap = args.NormalizedName;
                _mapLabel.Text = $"Carte : {_currentMap}";
                // Every map event is treated as a raid boundary. The log watcher
                // deliberately emits a same-map event when EFT starts another
                // raid on the same location, so an old pose can never leak into it.
                _estimator.Reset();
                _adaptiveCalibration.ResetRaid();
                _visualMotionGate.Reset();
                _visualKeyframeRecognizer.ResetSession();
                _superKeyframeRecognizer.ResetSession();
                _visualYawCalibration.Reset();
                _visualMotionEvidence = RealtimeMotionEvidence.Unknown;
                _lastVisualObservation = null;
                _lastVisualSample = 0;
                _lastSuperKeyframeSample = 0;
                _lastAutomaticReanchor = 0;
                ResetPrecisionV2();
                _pendingSuperKeyframeLearning = null;
                _previousMovementCommanded = false;
                _pendingCapture = null;
                _screenshotBindingWasDown = false;
                _controlTrackingActive = false;
                _middleMouseDown = false;
                _movementSpeedScale = 1.0;
                _posture = default;
                _previousDuckKey = _previousProneKey = _previousJumpKey = false;
                _duckChordUsed = false;
                _sprintToggle = _walkToggle = _aimToggle = false;
                _previousSprintKey = _previousWalkKey = _previousAimKey = false;
                _commandedStationarySeconds = 0.0;
                _reanchorIdleSeconds = 0.0;
                _rawKeyboardTimeline.Reset();
                _precisionYaw.Reset();
                _rawKeysDown.Clear();
                Interlocked.Exchange(ref _pendingMouseX, 0);
                Interlocked.Exchange(ref _pendingRawMouseWheel, 0);
                Interlocked.Exchange(ref _pendingDuckWheelChord, 0);
                Interlocked.Exchange(ref _rawRotationActivity, 0);
                _poseLabel.Text = "Position : raid détecté · appuie sur F8";
                _poseLabel.ForeColor = Color.Gainsboro;
            });
            watcher.Start();
            _mapWatcher = watcher;
        }
        catch
        {
            _mapLabel.Text = "Carte : journal EFT non détecté";
        }
    }

    private async Task StartBeaconFromUiAsync()
    {
        if (!SquadRoomCode.TryNormalize(_roomTextBox.Text, out var normalized))
        {
            MessageBox.Show(
                this,
                "Entre un code du type RP-ABCD-EFGH-JKLM-NPQR.",
                "RaidPilot Beacon",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        await StartBeaconAsync(
            SquadRoomCode.Format(normalized),
            SquadRelayClient.NormalizeName(_nameTextBox.Text),
            showSuccess: true);
    }

    private async Task StartBeaconAsync(string roomCode, string name, bool showSuccess)
    {
        await StopBeaconAsync(clearAutoJoin: false);
        var client = new SquadRelayClient(roomCode, _profile.MemberId, name);
        _client = client;
        _profile = _profile with
        {
            RoomCode = client.RoomCode,
            Name = client.Name,
            AutoJoin = true,
        };
        _ = SquadProfileStore.TrySave(_profile, _settingsPath);
        _roomTextBox.Text = client.RoomCode;
        _nameTextBox.Text = client.Name;
        _startButton.Enabled = false;
        _stopButton.Enabled = true;
        SetStatus(
            _playerCalibrationLoaded
                ? "Beacon actif · profil joueur adaptatif chargé"
                : "Beacon actif · apprentissage du joueur en cours",
            warning: false);

        if (_estimator.IsAnchored)
        {
            await SendCurrentPoseAsync(force: true);
        }

        if (showSuccess)
        {
            _trayIcon.ShowBalloonTip(
                3500,
                "RaidPilot Beacon",
                "Connecté. Appuie sur F8 en raid pour donner une position exacte, puis laisse Beacon ouvert.",
                ToolTipIcon.Info);
        }
    }

    private async Task StopBeaconAsync(bool clearAutoJoin)
    {
        var client = _client;
        _client = null;
        if (client is not null)
        {
            await client.DisposeAsync();
        }
        if (clearAutoJoin)
        {
            _profile = _profile with { AutoJoin = false };
            _ = SquadProfileStore.TrySave(_profile, _settingsPath);
        }
        _startButton.Enabled = true;
        _stopButton.Enabled = false;
        SetStatus("Déconnecté", warning: true);
    }

    private static HashSet<Keys> BuildTrackedMovementKeys(EftControlSettings controls)
    {
        var keys = new HashSet<Keys>();
        foreach (var candidate in new[]
        {
            controls.MoveForward,
            controls.MoveBackward,
            controls.MoveLeft,
            controls.MoveRight,
            controls.Duck,
            controls.Prone,
            controls.Jump,
        })
        {
            var key = candidate & Keys.KeyCode;
            if (key is not (Keys.None
                or Keys.LButton
                or Keys.RButton
                or Keys.MButton
                or Keys.XButton1
                or Keys.XButton2))
            {
                keys.Add(key);
            }
        }
        return keys;
    }

    private void UpdateControlTrackingState(bool canTrack)
    {
        if (canTrack)
        {
            if (_controlTrackingActive)
            {
                return;
            }

            _rawKeyboardTimeline.Reset();
            _precisionYaw.Reset();
            if (RawMouseInput.KeyboardRegistered)
            {
                foreach (var key in _trackedMovementKeys)
                {
                    _rawKeyboardTimeline.Synchronize(key, IsKeyDown(key));
                }
            }

            Interlocked.Exchange(ref _pendingMouseX, 0);
            Interlocked.Exchange(ref _pendingRawMouseWheel, 0);
            Interlocked.Exchange(ref _pendingDuckWheelChord, 0);
            Interlocked.Exchange(ref _rawRotationActivity, 0);
            _middleMouseDown = false;
            _controlTrackingActive = true;
            return;
        }

        if (_controlTrackingActive)
        {
            _adaptiveCalibration.MarkTrackingGap();
            _visualMotionGate.Reset();
            ResetPrecisionV2();
            _visualKeyframeRecognizer.CancelPendingMatch();
            _visualMotionEvidence = RealtimeMotionEvidence.Unknown;
            _lastVisualObservation = null;
            _previousMovementCommanded = false;
            _commandedStationarySeconds = 0.0;
            _reanchorIdleSeconds = 0.0;
        }

        _controlTrackingActive = false;
        _rawKeyboardTimeline.Reset();
        _precisionYaw.Reset();
        _rawKeysDown.Clear();
        _rawScreenshotBindingDown = false;
        _middleMouseDown = false;
        Interlocked.Exchange(ref _pendingMouseX, 0);
        Interlocked.Exchange(ref _pendingRawMouseWheel, 0);
        Interlocked.Exchange(ref _pendingDuckWheelChord, 0);
        Interlocked.Exchange(ref _rawRotationActivity, 0);
    }

    private bool CanUsePrecisionInput() =>
        RawMouseInput.MouseRegistered
        && RawMouseInput.KeyboardRegistered
        && _trackedMovementKeys.Contains(_controls.MoveForward & Keys.KeyCode)
        && _trackedMovementKeys.Contains(_controls.MoveBackward & Keys.KeyCode)
        && _trackedMovementKeys.Contains(_controls.MoveLeft & Keys.KeyCode)
        && _trackedMovementKeys.Contains(_controls.MoveRight & Keys.KeyCode);

    private bool TryGetMovementActivity(
        long startTimestamp,
        long endTimestamp,
        out double activity)
    {
        activity = 0.0;
        if (!RawMouseInput.KeyboardRegistered
            || !_rawKeyboardTimeline.TrySample(
                _controls.MoveForward & Keys.KeyCode,
                startTimestamp,
                endTimestamp,
                out var forward)
            || !_rawKeyboardTimeline.TrySample(
                _controls.MoveBackward & Keys.KeyCode,
                startTimestamp,
                endTimestamp,
                out var backward)
            || !_rawKeyboardTimeline.TrySample(
                _controls.MoveLeft & Keys.KeyCode,
                startTimestamp,
                endTimestamp,
                out var left)
            || !_rawKeyboardTimeline.TrySample(
                _controls.MoveRight & Keys.KeyCode,
                startTimestamp,
                endTimestamp,
                out var right))
        {
            return false;
        }

        activity = PrecisionMotionIntegrator.MovementActivity(
            forward.HeldFraction,
            backward.HeldFraction,
            left.HeldFraction,
            right.HeldFraction);
        return true;
    }

    private void UpdateMovementSpeedScale(int wheelDelta, bool duckWheelChord)
    {
        if (wheelDelta == 0
            || duckWheelChord
            || IsKeyDown(_controls.Duck)
            || IsKeyDown(Keys.R)
            || IsKeyDown(Keys.LControlKey)
            || IsKeyDown(Keys.RControlKey)
            || IsKeyDown(Keys.LMenu)
            || IsKeyDown(Keys.RMenu))
        {
            return;
        }

        // EFT utilise la molette pour une vitesse de marche continue. Sans ce
        // multiplicateur, Beacon avançait trop vite dès qu'un teammate avait
        // réduit sa vitesse sous 100 %.
        var wheelSteps = wheelDelta / 120.0;
        _movementSpeedScale = Math.Clamp(
            _movementSpeedScale + (wheelSteps * 0.07),
            0.25,
            1.0);
    }

    private void ResetPrecisionV2()
    {
        _precisionV2Vision.Reset();
        _precisionV2Fusion.Reset();
        _precisionV2LastTurnAt = 0;
        _precisionV2TrackingActive = false;
    }

    private RealtimeMotionEvidence FusePrecisionV2(
        long now, bool canTrack, bool movementCommanded,
        bool aiming, RealtimeMotionEvidence baseline)
    {
        if (!_precisionV2Toggle.Checked || !canTrack || !_estimator.IsAnchored
            || _lastForegroundWindow == nint.Zero)
        {
            if (_precisionV2TrackingActive) ResetPrecisionV2();
            return baseline;
        }
        _precisionV2TrackingActive = true;
        var directionStable = !_middleMouseDown && !aiming
            && (_precisionV2LastTurnAt == 0
                || (now - _precisionV2LastTurnAt) / (double)Stopwatch.Frequency >= 0.45);
        var frame = _precisionV2Vision.GetFresh(now, _lastForegroundWindow);
        var merged = _precisionV2Fusion.Fuse(
            frame, now, _lastForegroundWindow, movementCommanded, directionStable, baseline);
        _precisionV2Vision.Request(_lastForegroundWindow, now);
        return merged;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        var previous = _lastTick;
        _lastTick = now;
        var elapsedSinceTick = previous == 0
            ? 0.0
            : Math.Max(0.0, (now - previous) / (double)Stopwatch.Frequency);
        var elapsed = RealtimeTrackingPolicy.IntegratedSeconds(elapsedSinceTick);

        _tarkovForeground = IsTarkovForeground();
        var canTrack = CanTrackGameInput();
        var precisionIntervalReady = canTrack
            && _controlTrackingActive
            && previous > 0;
        UpdateControlTrackingState(canTrack);

        if (!_estimator.IsAnchored || elapsed <= 0)
        {
            return;
        }

        // A stalled UI timer cannot invent a seamless 2-second route. Record
        // unobserved time as uncertainty, reset velocity and relock raw input.
        if (RealtimeTrackingPolicy.HasUnreliableGap(elapsedSinceTick))
        {
            _estimator.SuspendMotion(elapsedSinceTick);
            _adaptiveCalibration.MarkTrackingGap();
            _rawKeyboardTimeline.Reset();
            _precisionYaw.Reset();
            _visualMotionGate.Reset();
            _visualMotionEvidence = RealtimeMotionEvidence.Unknown;
            _lastVisualObservation = null;
            _controlTrackingActive = false;
            return;
        }
        if (elapsedSinceTick > elapsed)
        {
            _estimator.SuspendMotion(
                RealtimeTrackingPolicy.UnobservedSeconds(elapsedSinceTick));
            _adaptiveCalibration.MarkTrackingGap();
            _visualMotionGate.Reset();
        }

        var forward = canTrack
            ? (IsKeyDown(_controls.MoveForward) ? 1.0 : 0.0)
                - (IsKeyDown(_controls.MoveBackward) ? 1.0 : 0.0)
            : 0.0;
        var right = canTrack
            ? (IsKeyDown(_controls.MoveRight) ? 1.0 : 0.0)
                - (IsKeyDown(_controls.MoveLeft) ? 1.0 : 0.0)
            : 0.0;

        var sprintKey = canTrack && IsKeyDown(_controls.Sprint);
        var walkKey = canTrack && IsKeyDown(_controls.Walk);
        var aimKey = canTrack && IsKeyDown(_controls.Aim);
        UpdateToggle(sprintKey, ref _previousSprintKey, ref _sprintToggle, _controls.SprintIsToggle);
        UpdateToggle(walkKey, ref _previousWalkKey, ref _walkToggle, _controls.WalkIsToggle);
        UpdateToggle(aimKey, ref _previousAimKey, ref _aimToggle, _controls.AimIsToggle);
        var sprint = _controls.SprintIsToggle ? _sprintToggle : sprintKey;
        var slow = _controls.WalkIsToggle ? _walkToggle : walkKey;
        var aiming = _controls.AimIsToggle ? _aimToggle : aimKey;
        var modes = RealtimeTrackingPolicy.ResolveModes(
            forward, sprint, slow, aiming, _sprintToggle, _walkToggle, _aimToggle);
        sprint = modes.Sprint;
        slow = modes.Slow;
        aiming = modes.Aiming;
        _sprintToggle = modes.SprintLatched;
        _walkToggle = modes.WalkLatched;
        _aimToggle = modes.AimLatched;

        var wheelDelta = canTrack
            ? Interlocked.Exchange(ref _pendingRawMouseWheel, 0)
            : 0;
        var duckWheelChord = Interlocked.Exchange(ref _pendingDuckWheelChord, 0) != 0;
        if (canTrack)
        {
            UpdateMovementSpeedScale(wheelDelta, duckWheelChord);
        }

        var duckDown = canTrack && IsKeyDown(_controls.Duck);
        var proneDown = canTrack && IsKeyDown(_controls.Prone);
        var jumpDown = canTrack && IsKeyDown(_controls.Jump);
        var duckPressed = duckDown && !_previousDuckKey;
        var duckReleased = !duckDown && _previousDuckKey;
        var pronePressed = proneDown && !_previousProneKey;
        var jumpPressed = jumpDown && !_previousJumpKey;
        if (precisionIntervalReady && RawMouseInput.KeyboardRegistered)
        {
            // Recognize a full key tap even when pressed and released between
            // two 25-ms ticks (also used by the Overlay's raw input path).
            if (_rawKeyboardTimeline.TrySample(_controls.Duck, previous, now, out var duckSample))
            {
                duckPressed |= duckSample.WasPressed;
                duckReleased |= duckSample.WasReleased;
            }
            if (_rawKeyboardTimeline.TrySample(_controls.Prone, previous, now, out var proneSample))
                pronePressed |= proneSample.WasPressed;
            if (_rawKeyboardTimeline.TrySample(_controls.Jump, previous, now, out var jumpSample))
                jumpPressed |= jumpSample.WasPressed;
        }
        if (duckPressed) _duckChordUsed = false;
        if (duckWheelChord) _duckChordUsed = true;
        _posture = RealtimeTrackingPolicy.UpdatePosture(
            _posture,
            duckReleased: canTrack && precisionIntervalReady && duckReleased,
            duckWheelChord: _duckChordUsed,
            pronePressed: canTrack && precisionIntervalReady && pronePressed,
            jumpPressed: canTrack && precisionIntervalReady && jumpPressed,
            sprinting: sprint);
        if (duckReleased) _duckChordUsed = false;
        _previousDuckKey = duckDown;
        _previousProneKey = proneDown;
        _previousJumpKey = jumpDown;

        var mouseX = canTrack ? Interlocked.Exchange(ref _pendingMouseX, 0) : 0;
        var rotationOccurred = Interlocked.Exchange(ref _rawRotationActivity, 0) != 0;
        if (rotationOccurred || Math.Abs(mouseX) > 1 || _middleMouseDown)
            _precisionV2LastTurnAt = now;
        if (!canTrack)
        {
            Interlocked.Exchange(ref _pendingMouseX, 0);
            Interlocked.Exchange(ref _pendingRawMouseWheel, 0);
            _adaptiveCalibration.MarkTrackingGap();
        }

        var movementCommanded = canTrack
            && (Math.Abs(forward) > 0.01 || Math.Abs(right) > 0.01);
        if (precisionIntervalReady
            && TryGetMovementActivity(previous, now, out var rawMovementActivity))
        {
            movementCommanded |= rawMovementActivity > 0.0;
        }

        if (movementCommanded || rotationOccurred)
        {
            _visualKeyframeRecognizer.CancelPendingMatch();
        }

        var visualEvidence = UpdateVisualMotionEvidence(
            now,
            canTrack,
            movementCommanded);

        if (Math.Abs(mouseX) > 3
            && _visualMotionGate.State == VisualMotionState.Stationary)
        {
            // Une rotation du corps avec une image prétendument fixe signifie
            // que la capture GDI s'est probablement figée. Elle ne doit pas
            // bloquer le pion indéfiniment.
            _visualMotionGate.InvalidateLiveness();
            _visualMotionEvidence = RealtimeMotionEvidence.Unknown;
            _lastVisualObservation = null;
            visualEvidence = RealtimeMotionEvidence.Unknown;
        }

        if (movementCommanded
            && _visualMotionGate.CaptureProvenLive
            && _visualMotionGate.State == VisualMotionState.Stationary)
        {
            _commandedStationarySeconds += Math.Clamp(elapsed, 0.0, 0.25);
            if (_commandedStationarySeconds >= MaximumCommandedStationarySeconds)
            {
                _visualMotionGate.InvalidateLiveness();
                _visualMotionEvidence = RealtimeMotionEvidence.Unknown;
                _lastVisualObservation = null;
                visualEvidence = RealtimeMotionEvidence.Unknown;
                _commandedStationarySeconds = 0.0;
            }
        }
        else
        {
            _commandedStationarySeconds = 0.0;
        }

        visualEvidence = RealtimeTrackingPolicy.FilterAmbiguousStationaryEvidence(
            visualEvidence, movementCommanded, sprint);
        visualEvidence = FusePrecisionV2(
            now, canTrack, movementCommanded, aiming, visualEvidence);

        if (!movementCommanded
            && !rotationOccurred
            && visualEvidence == RealtimeMotionEvidence.Stationary)
        {
            _reanchorIdleSeconds += Math.Clamp(elapsed, 0.0, 0.25);
        }
        else
        {
            _reanchorIdleSeconds = 0.0;
        }

        var movementSpeedScale = (slow || sprint ? 1.0 : _movementSpeedScale)
            * RealtimeTrackingPolicy.PostureSpeedScale(_posture);
        if (movementSpeedScale < 0.35)
        {
            // À très basse vitesse, 64×36 peut ressembler à une scène fixe.
            // Conserver Unknown évite un faux arrêt pendant une marche lente.
            visualEvidence = RealtimeMotionEvidence.Unknown;
        }

        var beforePose = _estimator.CurrentPose!.Value;
        _adaptiveCalibration.RecordMouse(mouseX, aiming, canTrack);
        if (_posture.Crouched || _posture.Prone)
            _adaptiveCalibration.MarkTrackingGap();
        if (movementCommanded
            && !sprint
            && !slow
            && movementSpeedScale < 0.99)
        {
            // La molette change le multiplicateur courant, pas la vitesse de
            // marche de base du joueur. Ne pas apprendre ce segment comme une
            // nouvelle vitesse personnelle.
            _adaptiveCalibration.MarkTrackingGap();
        }

        RealtimePose pose;
        PrecisionMotionResult integrated = default;
        var usedPrecision = precisionIntervalReady
            && mouseX >= int.MinValue
            && mouseX <= int.MaxValue
            && CanUsePrecisionInput()
            && _precisionMotionIntegrator.TryAdvance(
                _estimator,
                _rawKeyboardTimeline,
                _precisionYaw,
                previous,
                now,
                _controls.MoveForward & Keys.KeyCode,
                _controls.MoveBackward & Keys.KeyCode,
                _controls.MoveLeft & Keys.KeyCode,
                _controls.MoveRight & Keys.KeyCode,
                sprint,
                slow,
                movementSpeedScale,
                visualEvidence,
                (int)mouseX,
                out integrated);

        if (usedPrecision)
        {
            pose = integrated.Pose;
        }
        else
        {
            pose = _estimator.Advance(
                elapsed,
                forward,
                right,
                sprint,
                slow,
                mouseX,
                aiming,
                movementSpeedScale,
                visualEvidence);
        }

        _adaptiveCalibration.RecordMotion(
            beforePose,
            pose,
            forward,
            right,
            sprint,
            slow,
            canTrack,
            elapsed);
        DetectScreenshotCapturePoint(now, pose);
        TryAutomaticReanchor(
            now,
            pose,
            movementCommanded,
            visualEvidence);

        var calibrationSuffix = _playerCalibrationLoaded
            ? " · profil adaptatif"
            : string.Empty;
        var visionSuffix = _lastVisualObservation is { CaptureProvenLive: true } visual
            ? visual.State switch
            {
                VisualMotionState.Stationary => " · vision: arrêt",
                VisualMotionState.Moving => " · vision: mouvement",
                _ => " · vision: incertaine",
            }
            : string.Empty;
        _poseLabel.Text =
            $"Suivi : ±{pose.PositionUncertaintyMeters:F1} m · {pose.X:F1}, {pose.Z:F1} · cap {pose.YawDegrees:F0}°"
            + calibrationSuffix
            + visionSuffix;
        _poseLabel.ForeColor = pose.PositionUncertaintyMeters <= 3
            ? Color.FromArgb(141, 255, 203)
            : pose.PositionUncertaintyMeters <= 8
                ? Color.FromArgb(246, 214, 122)
                : Color.FromArgb(255, 155, 103);

        if (_client is not null
            && (now - _lastSend) / (double)Stopwatch.Frequency >= SendIntervalSeconds)
        {
            _lastSend = now;
            _ = SendCurrentPoseAsync(force: false);
        }
    }

    private bool TryLearnPendingSuperKeyframe(long now)
    {
        if (_pendingSuperKeyframeLearning is not { } pending)
        {
            return false;
        }

        if (now > pending.ExpiresAtTimestamp
            || !string.Equals(
                pending.MapId,
                _currentMap,
                StringComparison.OrdinalIgnoreCase))
        {
            _pendingSuperKeyframeLearning = null;
            return false;
        }

        if (now < pending.EarliestAtTimestamp)
        {
            return false;
        }

        if (_lastForegroundWindow == nint.Zero
            || !HighResolutionVisualSampler.TryCaptureWindow(
                _lastForegroundWindow,
                out var frame)
            || !_superKeyframeRecognizer.AddExactKeyframe(
                pending.MapId,
                frame,
                pending.Pose,
                now,
                out _))
        {
            return false;
        }

        _ = SuperKeyframeStore.TrySave(
            _superKeyframeRecognizer,
            SuperKeyframeStore.GetPath(_profile.MemberId));
        _pendingSuperKeyframeLearning = null;
        _superKeyframeRecognizer.CancelPendingMatch();
        return true;
    }

    private bool TryApplyVisualKeyframe(long now)
    {
        if (string.IsNullOrWhiteSpace(_currentMap)
            || _estimator.CurrentPose is not { } estimatedPose
            || !_visualMotionGate.TryCopyLatestLuminance(out var luminance)
            || luminance.Length == 0)
        {
            return false;
        }

        VisualKeyframePose? lowResolutionPose = null;
        try
        {
            var recognition = _visualKeyframeRecognizer.Observe(
                _currentMap,
                luminance,
                _visualMotionGate.SampleWidth,
                _visualMotionGate.SampleHeight,
                new VisualKeyframePose(
                    estimatedPose.X,
                    estimatedPose.Y,
                    estimatedPose.Z,
                    estimatedPose.YawDegrees),
                now,
                estimatedPose.PositionUncertaintyMeters);
            if (recognition.IsConfirmed)
            {
                lowResolutionPose = recognition.ExactPose;
            }
        }
        catch
        {
            return false;
        }

        var superAvailable =
            _superKeyframeRecognizer.KeyframeCountForMap(_currentMap) > 0;
        SuperKeyframeRecognition superRecognition =
            SuperKeyframeRecognition.NoMatch();
        var superDue = superAvailable
            && estimatedPose.PositionUncertaintyMeters
                >= SuperKeyframeMinimumUncertaintyMeters
            && (_lastSuperKeyframeSample == 0
                || (now - _lastSuperKeyframeSample)
                    / (double)Stopwatch.Frequency
                    >= SuperKeyframeSampleIntervalSeconds);

        if (superDue)
        {
            _lastSuperKeyframeSample = now;
            if (HighResolutionVisualSampler.TryCaptureWindow(
                    _lastForegroundWindow,
                    out var highResolutionFrame))
            {
                try
                {
                    superRecognition = _superKeyframeRecognizer.Observe(
                        _currentMap,
                        highResolutionFrame,
                        new VisualKeyframePose(
                            estimatedPose.X,
                            estimatedPose.Y,
                            estimatedPose.Z,
                            estimatedPose.YawDegrees),
                        now,
                        estimatedPose.PositionUncertaintyMeters);
                }
                catch
                {
                    _superKeyframeRecognizer.CancelPendingMatch();
                }
            }
            else
            {
                _superKeyframeRecognizer.CancelPendingMatch();
            }
        }

        VisualKeyframePose? exact = null;
        if (superRecognition.IsConfirmed
            && superRecognition.ExactPose is { } superPose)
        {
            if (lowResolutionPose is null
                || VisualAnchorsConsistent(
                    lowResolutionPose.Value,
                    superPose))
            {
                exact = superPose;
            }
            else
            {
                _visualKeyframeRecognizer.CancelPendingMatch();
                _superKeyframeRecognizer.CancelPendingMatch();
                return false;
            }
        }
        else if (!superAvailable
            && lowResolutionPose is { } legacyPose)
        {
            exact = legacyPose;
        }

        if (exact is not { } anchor)
        {
            return false;
        }

        var correction = Math.Sqrt(
            ((anchor.X - estimatedPose.X) * (anchor.X - estimatedPose.X))
            + ((anchor.Z - estimatedPose.Z) * (anchor.Z - estimatedPose.Z)));

        _estimator.Anchor(new ScreenshotObservation(
            "raidpilot-super-keyframe",
            anchor.X,
            anchor.Y,
            anchor.Z,
            0,
            0,
            0,
            1,
            anchor.YawDegrees));
        _adaptiveCalibration.MarkTrackingGap();

        _poseLabel.Text =
            $"Super-repère reconnu · correction {correction:F1} m · {anchor.X:F1}, {anchor.Z:F1}";
        _poseLabel.ForeColor = Color.FromArgb(112, 232, 255);

        if (_client is not null)
        {
            _ = SendCurrentPoseAsync(force: true);
        }

        return true;
    }

    private static bool VisualAnchorsConsistent(
        VisualKeyframePose lowResolution,
        VisualKeyframePose highResolution)
    {
        var horizontal = Math.Sqrt(
            ((lowResolution.X - highResolution.X)
                * (lowResolution.X - highResolution.X))
            + ((lowResolution.Z - highResolution.Z)
                * (lowResolution.Z - highResolution.Z)));
        var vertical = Math.Abs(
            lowResolution.Y - highResolution.Y);
        var yaw = AngleDistance(
            lowResolution.YawDegrees,
            highResolution.YawDegrees);

        return horizontal <= SuperKeyframeAnchorConsistencyMeters
            && vertical <= SuperKeyframeAnchorConsistencyVerticalMeters
            && yaw <= SuperKeyframeAnchorConsistencyYawDegrees;
    }

    private static double AngleDistance(double a, double b)
    {
        var delta = (a - b) % 360.0;
        if (delta <= -180)
        {
            delta += 360;
        }
        else if (delta > 180)
        {
            delta -= 360;
        }
        return Math.Abs(delta);
    }

    private readonly record struct PendingSuperKeyframeLearning(
        string MapId,
        VisualKeyframePose Pose,
        long EarliestAtTimestamp,
        long ExpiresAtTimestamp);

    private void TryAutomaticReanchor(
        long now,
        RealtimePose pose,
        bool movementCommanded,
        RealtimeMotionEvidence visualEvidence)
    {
        if (_pendingCapture is { } stalePending
            && (now - stalePending.Timestamp) / (double)Stopwatch.Frequency
                > PendingCaptureMaximumAgeSeconds)
        {
            // A lost/blocked screenshot must never freeze automatic recovery.
            _pendingCapture = null;
            _adaptiveCalibration.MarkTrackingGap();
        }

        var secondsSinceLastAttempt = _lastAutomaticReanchor == 0
            ? double.PositiveInfinity
            : (now - _lastAutomaticReanchor) / (double)Stopwatch.Frequency;
        var recoveryReady = LocalizationRecoveryPolicy.ShouldAttemptAutomaticReanchor(
            pose.PositionUncertaintyMeters,
            _reanchorIdleSeconds,
            secondsSinceLastAttempt);

        if (_pendingCapture is not null
            || movementCommanded
            || visualEvidence != RealtimeMotionEvidence.Stationary
            || !recoveryReady
            || string.IsNullOrWhiteSpace(_currentMap)
            || !_tarkovForeground
            || !CanTrackGameInput()
            || !ScreenshotHotkeySender.CanSend(_screenshotBinding))
        {
            return;
        }

        if (!ScreenshotHotkeySender.TrySend(_screenshotBinding))
        {
            return;
        }

        _pendingCapture = (
            now,
            pose,
            _adaptiveCalibration.CaptureSegment(pose),
            _currentMap,
            true);
        _lastAutomaticReanchor = now;
        _reanchorIdleSeconds = 0.0;

        SetStatus(
            $"Beacon actif · recalage automatique demandé (±{pose.PositionUncertaintyMeters:F1} m)",
            warning: false);
    }

    private RealtimeMotionEvidence UpdateVisualMotionEvidence(
        long now,
        bool canTrack,
        bool movementCommanded)
    {
        if (!canTrack || _lastForegroundWindow == nint.Zero)
        {
            _visualMotionEvidence = RealtimeMotionEvidence.Unknown;
            _lastVisualObservation = null;
            _previousMovementCommanded = movementCommanded;
            _pendingSuperKeyframeLearning = null;
            _visualKeyframeRecognizer.CancelPendingMatch();
            _superKeyframeRecognizer.CancelPendingMatch();
            return _visualMotionEvidence;
        }

        if (movementCommanded && !_previousMovementCommanded)
        {
            _visualMotionGate.ReleaseStationary();
            _pendingSuperKeyframeLearning = null;
            _visualKeyframeRecognizer.CancelPendingMatch();
            _superKeyframeRecognizer.CancelPendingMatch();
        }
        _previousMovementCommanded = movementCommanded;

        if (_lastVisualSample != 0
            && (now - _lastVisualSample) / (double)Stopwatch.Frequency < 0.10)
        {
            return _visualMotionEvidence;
        }

        var visualIntervalStart = _lastVisualSample;
        _lastVisualSample = now;
        if (!_visualMotionGate.TryObserveWindow(
                _lastForegroundWindow,
                out var observation,
                estimateEgomotion: true))
        {
            _visualMotionEvidence = RealtimeMotionEvidence.Unknown;
            _lastVisualObservation = observation;
            _pendingSuperKeyframeLearning = null;
            _visualKeyframeRecognizer.CancelPendingMatch();
            _superKeyframeRecognizer.CancelPendingMatch();
            return _visualMotionEvidence;
        }

        _lastVisualObservation = observation;
        TryCalibrateYawFromVision(
            visualIntervalStart,
            now,
            observation,
            movementCommanded);
        if (!observation.CaptureProvenLive)
        {
            _visualMotionEvidence = RealtimeMotionEvidence.Unknown;
            _pendingSuperKeyframeLearning = null;
            _visualKeyframeRecognizer.CancelPendingMatch();
            _superKeyframeRecognizer.CancelPendingMatch();
            return _visualMotionEvidence;
        }

        _visualMotionEvidence = observation.State switch
        {
            VisualMotionState.Stationary => RealtimeMotionEvidence.Stationary,
            VisualMotionState.Moving => RealtimeMotionEvidence.Moving,
            _ => RealtimeMotionEvidence.Unknown,
        };

        // A keyboard command with a visually confirmed stationary scene means
        // the character is probably blocked, exhausted or otherwise not
        // translating as requested. Keep the adaptive speed learner away from
        // that segment; it is not evidence that the player's base speed is low.
        if (movementCommanded
            && _visualMotionEvidence == RealtimeMotionEvidence.Stationary)
        {
            _adaptiveCalibration.MarkTrackingGap();
        }

        if (!movementCommanded
            && observation.Egomotion.State == VisualEgomotionState.Stationary
            && observation.Egomotion.IsReliable)
        {
            _ = TryLearnPendingSuperKeyframe(now);
            _ = TryApplyVisualKeyframe(now);
        }
        else
        {
            _pendingSuperKeyframeLearning = null;
            _visualKeyframeRecognizer.CancelPendingMatch();
            _superKeyframeRecognizer.CancelPendingMatch();
        }
        return _visualMotionEvidence;
    }

    private void TryCalibrateYawFromVision(
        long intervalStart,
        long intervalEnd,
        VisualMotionObservation observation,
        bool movementCommanded)
    {
        if (!observation.CaptureProvenLive
            || intervalStart <= 0
            || intervalEnd <= intervalStart
            || !_precisionYaw.TrySummarizeInterval(
                intervalStart,
                intervalEnd,
                out var yawSummary))
        {
            return;
        }

        var aiming = _controls.AimIsToggle
            ? _aimToggle
            : IsKeyDown(_controls.Aim);
        var settings = _estimator.Settings;
        var visualContextAiming = aiming || yawSummary.Aiming || yawSummary.MixedAiming;
        var realtimeCorrection = _visualYawCalibration.TryGetRealtimeCorrection(
            observation.Egomotion,
            yawSummary.Counts,
            yawSummary.Magnitude,
            movementCommanded,
            visualContextAiming,
            _middleMouseDown,
            settings.HipYawDegreesPerMouseCount);
        if (realtimeCorrection is { } correction
            && _estimator.IsAnchored)
        {
            _estimator.ApplyYawCorrection(correction);
        }

        var proposed = _visualYawCalibration.Observe(
            observation.Egomotion,
            yawSummary.Counts,
            yawSummary.Magnitude,
            movementCommanded,
            visualContextAiming,
            _middleMouseDown,
            settings.HipYawDegreesPerMouseCount);
        if (proposed is not { } hipYaw
            || Math.Abs(hipYaw - settings.HipYawDegreesPerMouseCount) < 1e-12)
        {
            return;
        }

        var updated = settings with { HipYawDegreesPerMouseCount = hipYaw };
        _estimator.ApplySettings(updated);
        _playerCalibrationLoaded = true;
        _ = RealtimeCalibrationStore.TrySave(
            BeaconAdaptiveCalibration.GetCalibrationPath(_profile.MemberId),
            updated,
            _calibrationSource);
    }

    private void DetectScreenshotCapturePoint(long now, RealtimePose pose)
    {
        var keyDown = IsScreenshotBindingDown();
        if (keyDown
            && !_screenshotBindingWasDown
            && _pendingCapture is null)
        {
            _pendingCapture = (
                now,
                pose,
                _adaptiveCalibration.CaptureSegment(pose),
                _currentMap,
                false);
        }

        _screenshotBindingWasDown = keyDown;
    }

    private bool IsScreenshotBindingDown()
    {
        if (!_screenshotBinding.IsReady
            || _screenshotBinding.AxisName is not null
            || _screenshotBinding.KeyCodes.Count == 0)
        {
            return false;
        }

        foreach (var keyCode in _screenshotBinding.KeyCodes)
        {
            if (!EftControlSettings.TryMapKeyCode(keyCode, out var key)
                || key == Keys.None
                || !IsKeyDown(key))
            {
                return false;
            }
        }

        return true;
    }

    private async Task SendCurrentPoseAsync(bool force)
    {
        var client = _client;
        if (client is null
            || _sendInProgress
            || _estimator.CurrentPose is not { } pose
            || string.IsNullOrWhiteSpace(_currentMap))
        {
            return;
        }

        _sendInProgress = true;
        try
        {
            var message = new SquadMemberPose(
                _profile.MemberId,
                _profile.Name,
                _currentMap,
                pose.X,
                pose.Y,
                pose.Z,
                pose.YawDegrees,
                pose.PositionUncertaintyMeters,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            await client.SendPoseAsync(message, _lifetime.Token);
            if (force)
            {
                SetStatus("Beacon actif · position F8 envoyée", warning: false);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch
        {
            SetStatus("Beacon actif · reconnexion à l’envoi…", warning: true);
        }
        finally
        {
            _sendInProgress = false;
        }
    }

    private static void UpdateToggle(
        bool keyDown,
        ref bool previousDown,
        ref bool toggled,
        bool toggleMode)
    {
        if (toggleMode && keyDown && !previousDown)
        {
            toggled = !toggled;
        }
        previousDown = keyDown;
    }

    private static bool IsKeyDown(Keys key) =>
        (NativeMethods.GetAsyncKeyState((int)(key & Keys.KeyCode)) & 0x8000) != 0;

    private bool CanTrackGameInput() =>
        _tarkovForeground && !NativeMethods.IsCursorVisible();

    private bool IsTarkovForeground()
    {
        var window = NativeMethods.GetForegroundWindow();
        if (window == nint.Zero)
        {
            _lastForegroundWindow = nint.Zero;
            return false;
        }

        if (window == _lastForegroundWindow)
        {
            return _tarkovForeground;
        }
        _lastForegroundWindow = window;

        try
        {
            NativeMethods.GetWindowThreadProcessId(window, out var processId);
            if (processId == 0)
            {
                return false;
            }
            using var process = Process.GetProcessById((int)processId);
            return string.Equals(
                process.ProcessName,
                "EscapeFromTarkov",
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void SetStatus(string text, bool warning)
    {
        _statusLabel.Text = text;
        _statusLabel.ForeColor = warning
            ? Color.FromArgb(255, 202, 90)
            : Color.FromArgb(112, 232, 255);
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _tickTimer.Stop();
        _folderProbeTimer.Stop();
        _lifetime.Cancel();
        _screenshotWatcher?.Dispose();
        _mapWatcher?.Dispose();
        if (_client is not null)
        {
            _ = _client.DisposeAsync();
            _client = null;
        }
        if (_rawInputRegistered)
        {
            RawMouseInput.Unregister();
            _rawInputRegistered = false;
        }
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _visualMotionGate.Dispose();
        _precisionV2Vision.Dispose();
        _tickTimer.Dispose();
        _folderProbeTimer.Dispose();
        _lifetime.Dispose();
    }
}