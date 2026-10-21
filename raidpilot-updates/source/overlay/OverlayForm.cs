using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace RaidPilot.Overlay;

internal sealed partial class OverlayForm : Form
{
    private const string DefaultMapUrl = "https://tarkov.dev/map/customs";
    private const int ToggleVisibilityHotkeyId = 0x5250;
    private const int ToggleInteractionHotkeyId = 0x5251;
    private const int ToggleGpsHotkeyId = 0x5252;
    private const int CycleExtractionHotkeyId = 0x5253;
    private const double OverlayOpacity = 0.86;
    private const double CrouchedMovementScale = 0.55;
    private const double ProneMovementScale = 0.20;
    // Ten updates per second are visually smooth with the marker's CSS
    // interpolation and halve websocket traffic plus React/Leaflet work versus
    // the former 20 Hz stream. Input integration itself remains at 20 Hz.
    private const double LivePositionSendIntervalSeconds = 0.10;
    private const double ScreenshotCaptureMatchWindowSeconds = 4.5;
    private const double CalibrationPostCaptureMovementToleranceMeters = 0.35;
    private const double CalibrationPostCaptureYawToleranceDegrees = 2.0;
    private const double MaximumCommandedStationarySeconds = 1.25;
    private const double ManualVisualKeyframeMinimumDelaySeconds = 1.8;
    private const double ManualVisualKeyframeMaximumDelaySeconds = 6.0;
    private const double LearnedWaypointArrivalRadiusMeters = 0.25;
    private const double LearnedWaypointVerticalToleranceMeters = 0.75;
    private static readonly TimeSpan ScreenshotSessionClockTolerance = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RaidStartMapDetectionGracePeriod = TimeSpan.FromMilliseconds(1750);

    private readonly WebView2 _webView = new();
    private readonly ToolStripMenuItem _gpsMenuItem = new("Afficher mon point sur tarkov.dev (Ctrl+Alt+G)…");
    private readonly ToolStripMenuItem _positionKeyMenuItem = new("Touche de position…");
    private readonly ToolStripMenuItem _extractionMenuItem = new("Extraction auto : O×2 puis F8 (Ctrl+Alt+E secours)");
    private readonly ToolStripMenuItem _questMenuItem = new("Quêtes : ouvre TÂCHES puis F8");
    private readonly ToolStripMenuItem _streamVisibilityMenuItem = new("Visible dans le stream Discord")
    {
        Checked = true,
        CheckOnClick = false,
    };
    private readonly ToolStripMenuItem _mapSizeMenuItem = new("Taille de la carte");
    private readonly ToolStripMenuItem _liveTrackingMenuItem = new("Suivi fluide en direct")
    {
        Checked = true,
        CheckOnClick = true,
    };
    private readonly ToolStripMenuItem _autoReanchorMenuItem = new("Recalage exact automatique au repos")
    {
        Checked = true,
        CheckOnClick = true,
    };
    private readonly NotifyIcon _trayIcon = new();
    private readonly CancellationTokenSource _formLifetime = new();
    private readonly System.Windows.Forms.Timer _realtimeTimer = new() { Interval = 50 };
    private readonly TarkovDevExtractionProvider _extractionProvider = new();
    private readonly TarkovDevQuestProvider _questProvider = new();
    private readonly WindowsScreenshotTextReader _screenshotTextReader = new();
    private readonly VisualMotionGate _visualMotionGate = new();
    private readonly VisualKeyframeRecognizer _visualKeyframeRecognizer = new();
    private readonly RealtimePoseHistory _realtimePoseHistory = new(capacity: 256);
    private readonly RawKeyboardTimeline _rawKeyboardTimeline = new();
    private readonly RaidCountdown _raidCountdown = new();
    private readonly RaidStartObservationBuffer _raidStartObservationBuffer = new();
    private readonly LearnedRouteGraph _learnedRouteGraph;
    private readonly HashSet<Keys> _trackedKeyboardKeys;
    private readonly Dictionary<Keys, KeySample> _controlSamples = new(capacity: 10);
    private readonly EftControlSettings _controlSettings;
    private RealtimePoseEstimator _realtimeEstimator;

    private ScreenshotFolderWatcher? _screenshotWatcher;
    private TarkovDevGpsClient? _gpsClient;
    private EftMapLogWatcher? _mapLogWatcher;
    private TaskCompletionSource<bool>? _navigationWaiter;
    private ulong? _navigationWaiterId;
    private Uri? _navigationWaiterUri;
    private ulong? _activeNavigationId;
    private Uri? _activeNavigationUri;
    private long _navigationSequence;
    private EftMapRoute? _pendingDetectedMap;
    private bool _webReady;
    private bool _navigationInProgress;
    private bool _navigationStartPending;
    private bool _gpsEnabled;
    private bool _clickThrough = true;
    private bool _allowClose;
    private bool _visibilityHotkeyRegistered;
    private bool _interactionHotkeyRegistered;
    private bool _gpsHotkeyRegistered;
    private bool _extractionHotkeyRegistered;
    private bool _rawMouseInputRegistered;
    private bool _rawKeyboardInputRegistered;
    private bool _gpsTransitionInProgress;
    private bool _extractionCycleInProgress;
    private bool _extractionScanInProgress;
    private bool _extractionPanelComplete;
    private bool _questScanInProgress;
    private bool _raidTimerScanInProgress;
    private bool _liveTrackingEnabled = true;
    private bool _autoReanchorEnabled = true;
    private bool _visibleInScreenCaptures = true;
    private int? _mapScalePercent;
    private double _effectiveMapScale = 1.0;
    private bool _windowHandleWasCreated;
    private bool _tarkovIsForeground;
    private bool _livePositionSendInProgress;
    private bool _liveGuidanceUpdateInProgress;
    private bool _learnedRouteGraphDirty;
    private bool _controlTrackingActive;
    private bool _foregroundStateInitialized;
    private bool _walkKeyWasDown;
    private bool _sprintKeyWasDown;
    private bool _aimKeyWasDown;
    private bool _duckKeyWasDown;
    private bool _proneKeyWasDown;
    private bool _jumpKeyWasDown;
    private bool _duckChordUsed;
    private bool _movementInputWasActive;
    private bool _screenshotBindingWasDown;
    private bool _skipNextRealtimeCalibration;
    private bool _replayingRaidStartObservation;
    private bool _raidStartReplayCrossedTransition;
    private bool _middleMouseDown;
    private bool _crouched;
    private bool _prone;
    private bool _slowWalkEnabled;
    private bool _sprintEnabled;
    private bool _aimingEnabled;
    private int _guidanceGeneration;
    private int _raidGeneration;
    private int _pendingRawMouseX;
    private int _pendingRawMouseWheel;
    private int _pendingDuckWheelChord;
    private long _hipMouseCountsSinceAnchor;
    private long _adsMouseCountsSinceAnchor;
    private long _hipMouseMagnitudeSinceAnchor;
    private long _adsMouseMagnitudeSinceAnchor;
    private long _lastRealtimeTick;
    private long _lastLiveSocketUpdate;
    private long _lastLiveGuidanceUpdate;
    private long _lastVisualMotionSample;
    private long _lastExactAnchorAt;
    private long _lastAutoReanchorAttempt;
    private long _lastManualScreenshotPressAt;
    private long _raidStartReplayCaptureTimestamp;
    private long _autoReanchorRequestedAt;
    private long _lastLearnedRouteObservationAt;
    private long _lastLearnedRoutePlanAt;
    private long _lastLearnedRouteSaveAt;
    private AutomaticReanchorRequest? _automaticReanchorRequest;
    private PendingVisualKeyframeLearning? _pendingVisualKeyframeLearning;
    private nint _lastForegroundWindow;
    private double _walkDistanceSinceAnchor;
    private double _sprintDistanceSinceAnchor;
    private double _slowDistanceSinceAnchor;
    private double _movementSpeedScale = 1.0;
    private double _idleSeconds;
    private double _commandedStationarySeconds;
    private string _currentMap = "customs";
    private string? _extractionsMap;
    private string? _questsMap;
    private string? _questsGameMode;
    private string? _raidQuestGameMode;
    private ExtractionCandidate? _selectedExtraction;
    private IReadOnlyList<ExtractionCandidate> _extractions = Array.Empty<ExtractionCandidate>();
    private IReadOnlySet<string>? _confirmedExtractionIds;
    private IReadOnlyList<QuestCandidate> _quests = Array.Empty<QuestCandidate>();
    private IReadOnlySet<string>? _confirmedQuestIds;
    private CancellationTokenSource? _extractionScanCancellation;
    private CancellationTokenSource? _questScanCancellation;
    private CancellationTokenSource? _raidTimerScanCancellation;
    private (
        string ScreenshotPath,
        int RaidGeneration,
        string MapName,
        long CaptureTimestamp,
        int RaidTimerGeneration)? _pendingExtractionScan;
    private (string ScreenshotPath, int RaidGeneration, string MapName)? _pendingQuestScan;
    private (
        string ScreenshotPath,
        int RaidGeneration,
        string MapName,
        long CaptureTimestamp,
        int RaidTimerGeneration)? _pendingRaidTimerScan;
    private ScreenshotObservation? _lastObservation;
    private ScreenshotObservation? _lastExactObservation;
    private (ScreenshotObservation Observation, string Map, int RaidGeneration)? _pendingLivePosition;
    private LearnedRoutePlan? _cachedLearnedRoutePlan;
    private string? _cachedLearnedRouteTargetId;
    private int _cachedLearnedRouteGeneration = -1;
    private long _cachedLearnedRouteRevision = -1;
    private double _cachedLearnedRouteX;
    private double _cachedLearnedRouteY;
    private double _cachedLearnedRouteZ;
    private double _cachedLearnedRouteUncertainty;
    private string? _activeLearnedRouteProgressSignature;
    private int _activeLearnedRouteNextIndex;
    private int _clearedLearnedRouteGeneration = -1;
    private string? _renderedLearnedRouteSignature;
    private Task<bool>? _learnedRouteSaveTask;
    private long _learnedRouteGraphRevision;
    private DateTimeOffset _learnedRouteClockOriginUtc;
    private long _learnedRouteClockOriginTimestamp;
    private DateTimeOffset _raidSessionStartedAtUtc;
    private RaidSide _raidSide = RaidSide.Pmc;
    private EftScreenshotBindingInfo _screenshotBinding = EftScreenshotBindingReader.Read();

    internal OverlayForm()
    {
        _learnedRouteGraph = LoadLearnedRouteGraph();
        _raidSessionStartedAtUtc = DateTimeOffset.UtcNow;
        _learnedRouteClockOriginUtc = _raidSessionStartedAtUtc;
        _learnedRouteClockOriginTimestamp = Stopwatch.GetTimestamp();
        _learnedRouteGraph.BeginSession(_currentMap);
        _controlSettings = EftControlSettings.Read();
        _trackedKeyboardKeys = BuildTrackedKeyboardKeys(_controlSettings);
        var estimatorSettings = new RealtimePoseEstimatorSettings
        {
            HipYawDegreesPerMouseCount = Math.Clamp(_controlSettings.MouseSensitivity * 0.1, 0.005, 0.5),
            AdsYawDegreesPerMouseCount = Math.Clamp(_controlSettings.MouseAimingSensitivity * 0.1, 0.005, 0.5),
        };
        var calibrationSource = new RealtimeCalibrationSource(
            _controlSettings.MouseSensitivity,
            _controlSettings.MouseAimingSensitivity);
        if (RealtimeCalibrationStore.TryLoad(
            GetRealtimeCalibrationPath(),
            estimatorSettings,
            calibrationSource,
            out var persistedSettings))
        {
            estimatorSettings = persistedSettings;
        }
        _realtimeEstimator = new RealtimePoseEstimator(estimatorSettings);
        _visualKeyframeRecognizer.ClearAllKeyframes();
        _ = VisualKeyframeStore.TryLoad(
            _visualKeyframeRecognizer,
            VisualKeyframeStore.GetPath(_squadProfile.MemberId));
        _streamVisibilityMenuItem.Checked = OverlayPreferencesStore.LoadVisibleInCaptures();
        _mapScalePercent = OverlayPreferencesStore.LoadMapScalePercent();
        Text = "RaidPilot Overlay";
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(
            OverlayLayoutCalculator.BaseWidthDip + OverlayLayoutCalculator.AdditionalWidthDip,
            OverlayLayoutCalculator.TotalHeightDip);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(11, 14, 13);
        Opacity = OverlayOpacity;
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;

        BuildWebView();
        BuildTrayMenu();
        InitializeSquad();
        InitializeQuestContext();
        InitializeAutomaticFloors();
        InitializeRaidClockUi();
        InitializePrecision();
        PositionAtTopRight();

        _realtimeTimer.Tick += OnRealtimeTimerTick;
        Shown += OnShown;
        FormClosing += OnFormClosing;
    }

    protected override bool ShowWithoutActivation => _clickThrough;

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        PositionAtTopRight(e.DeviceDpiNew);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= (int)NativeMethods.WsExToolWindow;
            if (_clickThrough)
            {
                parameters.ExStyle |= (int)(
                    NativeMethods.WsExTransparent
                    | NativeMethods.WsExNoActivate);
            }
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // Every new native handle starts at WDA_NONE. Reapply the user's saved
        // preference, including after an eventual handle recreation.
        var requestedStreamVisibility = _windowHandleWasCreated
            ? _visibleInScreenCaptures
            : _streamVisibilityMenuItem.Checked;
        _windowHandleWasCreated = true;
        _visibleInScreenCaptures = true;
        _streamVisibilityMenuItem.Checked = true;
        if (!requestedStreamVisibility)
        {
            _ = TrySetStreamVisibility(
                visibleInScreenCaptures: false,
                persistPreference: false,
                showSuccess: false);
        }
        _visibilityHotkeyRegistered = NativeMethods.RegisterHotKey(
            Handle,
            ToggleVisibilityHotkeyId,
            NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModNoRepeat,
            (uint)Keys.M);
        _interactionHotkeyRegistered = NativeMethods.RegisterHotKey(
            Handle,
            ToggleInteractionHotkeyId,
            NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModNoRepeat,
            (uint)Keys.I);
        _gpsHotkeyRegistered = NativeMethods.RegisterHotKey(
            Handle,
            ToggleGpsHotkeyId,
            NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModNoRepeat,
            (uint)Keys.G);
        _extractionHotkeyRegistered = NativeMethods.RegisterHotKey(
            Handle,
            CycleExtractionHotkeyId,
            NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModNoRepeat,
            (uint)Keys.E);
        _rawMouseInputRegistered = RawMouseInput.Register(Handle);
        _rawKeyboardInputRegistered = RawMouseInput.KeyboardRegistered;

        if (!_visibilityHotkeyRegistered
            || !_interactionHotkeyRegistered
            || !_gpsHotkeyRegistered
            || !_extractionHotkeyRegistered)
        {
            BeginInvoke(() => _trayIcon.ShowBalloonTip(
                5000,
                "RaidPilot",
                "Un raccourci global RaidPilot est déjà utilisé. Le menu de la zone de notification reste disponible.",
                ToolTipIcon.Warning));
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (_visibilityHotkeyRegistered)
        {
            NativeMethods.UnregisterHotKey(Handle, ToggleVisibilityHotkeyId);
        }
        if (_interactionHotkeyRegistered)
        {
            NativeMethods.UnregisterHotKey(Handle, ToggleInteractionHotkeyId);
        }
        if (_gpsHotkeyRegistered)
        {
            NativeMethods.UnregisterHotKey(Handle, ToggleGpsHotkeyId);
        }
        if (_extractionHotkeyRegistered)
        {
            NativeMethods.UnregisterHotKey(Handle, CycleExtractionHotkeyId);
        }
        if (_rawMouseInputRegistered)
        {
            RawMouseInput.Unregister();
            _rawMouseInputRegistered = false;
            _rawKeyboardInputRegistered = false;
        }
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMethods.WmInput)
        {
            if (RawMouseInput.TryReadPacket(message.LParam, out var packet)
                && CanAcceptRawRealtimeInput())
            {
                if (packet.Kind == RawInputPacketKind.Keyboard
                    && _rawKeyboardInputRegistered
                    && _trackedKeyboardKeys.Contains(packet.Key))
                {
                    _rawKeyboardTimeline.Record(
                        packet.Key,
                        packet.IsKeyDown,
                        Math.Max(
                            Stopwatch.GetTimestamp(),
                            Volatile.Read(ref _lastRealtimeTick) + 1));
                }
                else if (packet.Kind == RawInputPacketKind.Mouse)
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
                    if (packet.WheelDelta != 0 && IsKeyDown(_controlSettings.Duck))
                    {
                        _duckChordUsed = true;
                        Interlocked.Exchange(ref _pendingDuckWheelChord, 1);
                    }
                    // Le bouton du milieu maintient le free-look EFT : la caméra
                    // tourne, mais pas l'axe du corps utilisé par W/A/S/D.
                    if (!_middleMouseDown)
                    {
                        Interlocked.Add(ref _pendingRawMouseX, packet.DeltaX);
                        if (packet.DeltaX != 0) Interlocked.Exchange(ref _precisionYawActivity, 1);
                        _precisionYaw.Record(Math.Max(Stopwatch.GetTimestamp(),
                            Volatile.Read(ref _lastRealtimeTick) + 1), packet.DeltaX, _aimingEnabled);
                        if (_aimingEnabled)
                        {
                            Interlocked.Add(ref _adsMouseCountsSinceAnchor, packet.DeltaX);
                            Interlocked.Add(ref _adsMouseMagnitudeSinceAnchor, Math.Abs((long)packet.DeltaX));
                        }
                        else
                        {
                            Interlocked.Add(ref _hipMouseCountsSinceAnchor, packet.DeltaX);
                            Interlocked.Add(ref _hipMouseMagnitudeSinceAnchor, Math.Abs((long)packet.DeltaX));
                        }
                    }
                }
            }
            base.WndProc(ref message);
            return;
        }

        if (_clickThrough && message.Msg == NativeMethods.WmNcHitTest)
        {
            message.Result = new nint(NativeMethods.HtTransparent);
            return;
        }

        if (_clickThrough && message.Msg == NativeMethods.WmMouseActivate)
        {
            message.Result = new nint(NativeMethods.MaNoActivate);
            return;
        }

        if (message.Msg == NativeMethods.WmHotkey)
        {
            switch (message.WParam.ToInt32())
            {
                case ToggleVisibilityHotkeyId:
                    ToggleOverlayVisibility();
                    return;
                case ToggleInteractionHotkeyId:
                    SetClickThrough(!_clickThrough, activateWhenInteractive: true);
                    return;
                case ToggleGpsHotkeyId:
                    OnGpsButtonClick(this, EventArgs.Empty);
                    return;
                case CycleExtractionHotkeyId:
                    _ = CycleExtractionAsync();
                    return;
            }
        }

        base.WndProc(ref message);
    }

    private void BuildWebView()
    {
        _webView.Dock = DockStyle.Fill;
        _webView.DefaultBackgroundColor = Color.FromArgb(11, 14, 13);
        Controls.Add(_webView);
    }

    private void BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Ouvrir (sans focus)", null, (_, _) =>
        {
            SetClickThrough(true, activateWhenInteractive: false);
            ShowNoActivate();
            ReassertTopMost();
        });
        menu.Items.Add("Interagir (Ctrl+Alt+I)", null, (_, _) =>
        {
            SetClickThrough(false, activateWhenInteractive: true);
        });
        menu.Items.Add("Masquer (Ctrl+Alt+M)", null, (_, _) => Hide());
        _streamVisibilityMenuItem.Click += (_, _) =>
        {
            _ = TrySetStreamVisibility(
                !_visibleInScreenCaptures,
                persistPreference: true,
                showSuccess: true);
        };
        menu.Items.Add(_streamVisibilityMenuItem);
        menu.Items.Add(BuildMapSizeMenu());
        menu.Items.Add(BuildMapMenu());
        _positionKeyMenuItem.Click += (_, _) => ShowPositionKeyHelp();
        menu.Items.Add(_positionKeyMenuItem);
        _gpsMenuItem.Click += OnGpsButtonClick;
        menu.Items.Add(_gpsMenuItem);
        menu.Items.Add(BuildSquadMenu());
        _extractionMenuItem.Click += async (_, _) => await CycleExtractionAsync();
        menu.Items.Add(_extractionMenuItem);
        _questMenuItem.Click += (_, _) => ShowQuestScanHelp();
        menu.Items.Add(_questMenuItem);
        _liveTrackingMenuItem.Click += (_, _) => SetLiveTrackingEnabled(_liveTrackingMenuItem.Checked);
        menu.Items.Add(_liveTrackingMenuItem);
        _autoReanchorMenuItem.Click += (_, _) =>
        {
            _autoReanchorEnabled = _autoReanchorMenuItem.Checked;
            _idleSeconds = 0.0;
            _automaticReanchorRequest = null;
            Interlocked.Exchange(ref _autoReanchorRequestedAt, 0);
            _trayIcon.ShowBalloonTip(
                3000,
                "RaidPilot — recalage",
                _autoReanchorEnabled
                    ? "Recalage discret activé : RaidPilot corrige le point seulement quand tu es au repos."
                    : "Recalage automatique désactivé : F8 reste disponible manuellement.",
                ToolTipIcon.Info);
        };
        menu.Items.Add(_autoReanchorMenuItem);
        menu.Items.Add(BuildRaidSideMenu());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quitter", null, async (_, _) => await ExitAsync());

        _trayIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)
            ?? SystemIcons.Application;
        _trayIcon.Text = "RaidPilot Overlay";
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.Visible = true;
        _trayIcon.DoubleClick += (_, _) => SetClickThrough(false, activateWhenInteractive: true);
        menu.Opening += (_, _) => RefreshPositionKeyMenu();
        RefreshPositionKeyMenu();
    }

    private ToolStripMenuItem BuildRaidSideMenu()
    {
        var sideMenu = new ToolStripMenuItem("Type de raid pour les extractions");
        var pmcItem = new ToolStripMenuItem("PMC") { Checked = true, CheckOnClick = false };
        var scavItem = new ToolStripMenuItem("Scav") { CheckOnClick = false };

        pmcItem.Click += (_, _) =>
        {
            pmcItem.Checked = true;
            scavItem.Checked = false;
            SetRaidSide(RaidSide.Pmc);
        };
        scavItem.Click += (_, _) =>
        {
            pmcItem.Checked = false;
            scavItem.Checked = true;
            SetRaidSide(RaidSide.Scav);
        };

        sideMenu.DropDownItems.Add(pmcItem);
        sideMenu.DropDownItems.Add(scavItem);
        return sideMenu;
    }

    private ToolStripMenuItem BuildMapSizeMenu()
    {
        _mapSizeMenuItem.DropDownOpening += (_, _) => RefreshMapSizeMenu();
        RefreshMapSizeMenu();
        return _mapSizeMenuItem;
    }

    private void RefreshMapSizeMenu()
    {
        _mapSizeMenuItem.DropDownItems.Clear();

        var effectivePercent = (int)Math.Round(
            _effectiveMapScale * 100.0,
            MidpointRounding.AwayFromZero);
        _mapSizeMenuItem.Text = _mapScalePercent is { } requested
            ? $"Taille de la carte ({requested}% → {effectivePercent}%)"
            : $"Taille de la carte (Auto · {effectivePercent}%)";

        var autoItem = new ToolStripMenuItem("Auto — recommandé")
        {
            Checked = !_mapScalePercent.HasValue,
            CheckOnClick = false,
        };
        autoItem.Click += (_, _) => SetMapScalePercent(null, showSuccess: true);
        _mapSizeMenuItem.DropDownItems.Add(autoItem);
        _mapSizeMenuItem.DropDownItems.Add(new ToolStripSeparator());

        foreach (var percent in new[] { 50, 60, 70, 80, 90, 100, 110, 120 })
        {
            var item = new ToolStripMenuItem($"{percent}%")
            {
                Checked = _mapScalePercent == percent,
                CheckOnClick = false,
            };
            var capturedPercent = percent;
            item.Click += (_, _) => SetMapScalePercent(
                capturedPercent,
                showSuccess: true);
            _mapSizeMenuItem.DropDownItems.Add(item);
        }

        _mapSizeMenuItem.DropDownItems.Add(new ToolStripSeparator());

        var reduceItem = new ToolStripMenuItem("− Réduire de 5%");
        reduceItem.Click += (_, _) => AdjustMapScalePercent(-5);
        _mapSizeMenuItem.DropDownItems.Add(reduceItem);

        var enlargeItem = new ToolStripMenuItem("+ Agrandir de 5%");
        enlargeItem.Click += (_, _) => AdjustMapScalePercent(5);
        _mapSizeMenuItem.DropDownItems.Add(enlargeItem);

        _mapSizeMenuItem.DropDownItems.Add(new ToolStripSeparator());

        var resetItem = new ToolStripMenuItem("Réinitialiser en Auto");
        resetItem.Click += (_, _) => SetMapScalePercent(null, showSuccess: true);
        _mapSizeMenuItem.DropDownItems.Add(resetItem);
    }

    private void AdjustMapScalePercent(int delta)
    {
        var currentPercent = _mapScalePercent
            ?? (int)Math.Round(
                _effectiveMapScale * 100.0,
                MidpointRounding.AwayFromZero);
        var nextPercent = Math.Clamp(
            currentPercent + delta,
            OverlayLayoutCalculator.MinimumManualScalePercent,
            OverlayLayoutCalculator.MaximumManualScalePercent);
        SetMapScalePercent(nextPercent, showSuccess: true);
    }

    private void SetMapScalePercent(
        int? percent,
        bool showSuccess)
    {
        if (percent is { } rawPercent)
        {
            percent = Math.Clamp(
                rawPercent,
                OverlayLayoutCalculator.MinimumManualScalePercent,
                OverlayLayoutCalculator.MaximumManualScalePercent);
        }

        _mapScalePercent = percent;
        PositionAtTopRight();
        RefreshMapSizeMenu();

        var saved = OverlayPreferencesStore.TrySaveMapScalePercent(percent);
        if (!saved)
        {
            _trayIcon.ShowBalloonTip(
                4500,
                "RaidPilot — taille de carte",
                "La taille a changé pour cette session, mais RaidPilot n'a pas pu l'enregistrer.",
                ToolTipIcon.Warning);
            return;
        }

        if (showSuccess)
        {
            var effectivePercent = (int)Math.Round(
                _effectiveMapScale * 100.0,
                MidpointRounding.AwayFromZero);
            var message = percent is null
                ? $"Mode Auto activé · taille actuelle {effectivePercent}%."
                : effectivePercent == percent.Value
                    ? $"Taille de carte réglée à {percent.Value}%."
                    : $"Taille demandée {percent.Value}% · limitée à {effectivePercent}% pour rester dans l'écran.";

            _trayIcon.ShowBalloonTip(
                3000,
                "RaidPilot — taille de carte",
                message,
                ToolTipIcon.Info);
        }
    }

    private ToolStripMenuItem BuildMapMenu()
    {
        var mapMenu = new ToolStripMenuItem("Choisir la carte");
        (string Label, string Token)[] maps =
        [
            ("Customs", "customs"),
            ("Factory", "factory"),
            ("Ground Zero", "ground-zero"),
            ("Streets of Tarkov", "streets-of-tarkov"),
            ("Interchange", "interchange"),
            ("The Lab", "the-lab"),
            ("Labyrinth", "labyrinth"),
            ("Lighthouse", "lighthouse"),
            ("Reserve", "reserve"),
            ("Shoreline", "shoreline"),
            ("Woods", "woods"),
            ("Terminal", "terminal"),
            ("Icebreaker", "icebreaker")
        ];

        foreach (var (label, token) in maps)
        {
            var item = new ToolStripMenuItem(label);
            item.Click += (_, _) =>
            {
                if (EftMapLogParser.TryNormalizeMapToken(token, out var map) && map is not null)
                {
                    NavigateToDetectedMap(map, startsNewRaid: true);
                }
            };
            mapMenu.DropDownItems.Add(item);
        }

        return mapMenu;
    }

    private async void OnShown(object? sender, EventArgs e)
    {
        PositionAtTopRight();
        ApplyClickThroughStyle();
        ReassertTopMost();
        EnsureMapLogWatcherStarted();
        EnsureLocalScreenshotWatcherStarted();
        _lastRealtimeTick = Stopwatch.GetTimestamp();
        _realtimeTimer.Start();
        await InitializeWebViewAsync();
        await TryAutoJoinSquadAsync();
    }

    private void OnRealtimeTimerTick(object? sender, EventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        var previous = _lastRealtimeTick;
        _lastRealtimeTick = now;
        var elapsedSeconds = previous == 0
            ? 0.0
            : (now - previous) / (double)Stopwatch.Frequency;

        var wasTarkovForeground = _tarkovIsForeground;
        var previousForegroundWindow = _lastForegroundWindow;
        _tarkovIsForeground = IsTarkovForeground();
        if (_foregroundStateInitialized
            && _tarkovIsForeground
            && (!wasTarkovForeground || _lastForegroundWindow != previousForegroundWindow)
            && Visible)
        {
            // One event-driven repair when the game regains focus replaces the
            // old 1 Hz z-order watchdog that made WebView2 flash.
            ReassertTopMost();
        }
        _foregroundStateInitialized = true;
        TrackScreenshotBindingPress(now);
        var controlsActive = _liveTrackingEnabled
            && _tarkovIsForeground
            && !NativeMethods.IsCursorVisible();
        if (!controlsActive)
        {
            if (_controlTrackingActive)
            {
                _realtimeEstimator.SuspendMotion();
                _precisionSegmentClean = false;
                _precisionGaps++;
                _visualMotionGate.Reset();
                _visualKeyframeRecognizer.CancelPendingMatch();
                _pendingVisualKeyframeLearning = null;
            }
            _controlTrackingActive = false;
            _movementInputWasActive = false;
            _middleMouseDown = false;
            _rawKeyboardTimeline.Reset();
        _precisionYaw.Reset();
            Interlocked.Exchange(ref _pendingRawMouseX, 0);
        Interlocked.Exchange(ref _precisionYawActivity, 0);
            Interlocked.Exchange(ref _pendingRawMouseWheel, 0);
            Interlocked.Exchange(ref _pendingDuckWheelChord, 0);
            _idleSeconds = 0.0;
            _commandedStationarySeconds = 0.0;
            RecordCurrentRealtimePose(now);
            return;
        }

        var firstControlTick = !_controlTrackingActive;
        if (firstControlTick)
        {
            SynchronizeRawKeyboardTimeline();
            // The preceding interval may belong to another foreground app.
            // Synchronize physical state now, but never integrate that time.
            elapsedSeconds = 0.0;
        }
        var inputIntervalStart = firstControlTick ? now : previous;
        // Reuse the tiny lookup table instead of allocating a Dictionary on
        // every 50 ms input tick. All accesses happen on the WinForms UI
        // thread, so clearing it here is deterministic and thread-safe.
        var samples = _controlSamples;
        samples.Clear();
        KeySample GetControlSample(Keys key)
        {
            var normalized = key & Keys.KeyCode;
            if (!samples.TryGetValue(normalized, out var sample))
            {
                sample = SampleControlKey(normalized, inputIntervalStart, now);
                samples[normalized] = sample;
            }
            return sample;
        }

        var forwardSample = GetControlSample(_controlSettings.MoveForward);
        var backwardSample = GetControlSample(_controlSettings.MoveBackward);
        var leftSample = GetControlSample(_controlSettings.MoveLeft);
        var rightSample = GetControlSample(_controlSettings.MoveRight);
        var forward = forwardSample.HeldFraction - backwardSample.HeldFraction;
        var right = rightSample.HeldFraction - leftSample.HeldFraction;

        var walkSample = GetControlSample(_controlSettings.Walk);
        var sprintSample = _controlSettings.Sprint == _controlSettings.Walk
            ? walkSample
            : GetControlSample(_controlSettings.Sprint);
        var aimSample = _controlSettings.Aim == _controlSettings.Walk
            ? walkSample
            : _controlSettings.Aim == _controlSettings.Sprint
                ? sprintSample
                : GetControlSample(_controlSettings.Aim);
        var duckSample = GetControlSample(_controlSettings.Duck);
        var proneSample = GetControlSample(_controlSettings.Prone);
        var jumpSample = GetControlSample(_controlSettings.Jump);

        if (!_controlTrackingActive)
        {
            // The first foreground tick consumes stale edge bits accumulated
            // while another application had focus. Continuous binds still
            // synchronize immediately from their current physical state.
            _walkKeyWasDown = walkSample.IsDown;
            _sprintKeyWasDown = sprintSample.IsDown;
            _aimKeyWasDown = aimSample.IsDown;
            _duckKeyWasDown = duckSample.IsDown;
            _proneKeyWasDown = proneSample.IsDown;
            _jumpKeyWasDown = jumpSample.IsDown;
            _middleMouseDown = IsKeyDown(Keys.MButton);
            if (!_controlSettings.WalkIsToggle)
            {
                _slowWalkEnabled = walkSample.IsDown;
            }
            if (!_controlSettings.SprintIsToggle)
            {
                _sprintEnabled = sprintSample.IsDown;
            }
            if (!_controlSettings.AimIsToggle)
            {
                _aimingEnabled = aimSample.IsDown;
            }
            _controlTrackingActive = true;
        }
        else
        {
            _slowWalkEnabled = UpdateActionState(
                _slowWalkEnabled,
                _controlSettings.WalkIsToggle,
                walkSample,
                _walkKeyWasDown);
            _sprintEnabled = UpdateActionState(
                _sprintEnabled,
                _controlSettings.SprintIsToggle,
                sprintSample,
                _sprintKeyWasDown,
                allowToggle: forward > 0.0);
            _aimingEnabled = UpdateActionState(
                _aimingEnabled,
                _controlSettings.AimIsToggle,
                aimSample,
                _aimKeyWasDown);
            _walkKeyWasDown = walkSample.IsDown;
            _sprintKeyWasDown = sprintSample.IsDown;
            _aimKeyWasDown = aimSample.IsDown;

            if (duckSample.IsDown
                && !_duckKeyWasDown
                && Volatile.Read(ref _pendingDuckWheelChord) == 0)
            {
                _duckChordUsed = false;
            }
            var duckReleased = !duckSample.IsDown && _duckKeyWasDown;
            var quickDuckTap = !duckSample.IsDown
                && !_duckKeyWasDown
                && duckSample.WasPressedSinceLastPoll;
            var duckActionCompleted = duckReleased || quickDuckTap;
            if (duckActionCompleted && !_duckChordUsed)
            {
                if (_prone)
                {
                    _prone = false;
                    _crouched = true;
                }
                else
                {
                    _crouched = !_crouched;
                }
            }
            if (duckActionCompleted)
            {
                _duckChordUsed = false;
            }

            var pronePressed = proneSample.WasPressedSinceLastPoll
                || (proneSample.IsDown && !_proneKeyWasDown);
            if (pronePressed)
            {
                _prone = !_prone;
                _crouched = false;
            }

            var jumpPressed = jumpSample.WasPressedSinceLastPoll
                || (jumpSample.IsDown && !_jumpKeyWasDown);
            if (jumpPressed)
            {
                _prone = false;
                _crouched = false;
            }

            _duckKeyWasDown = duckSample.IsDown;
            _proneKeyWasDown = proneSample.IsDown;
            _jumpKeyWasDown = jumpSample.IsDown;
        }

        // Releasing forward interrupts Tarkov's sprint. Requiring a fresh
        // sprint edge avoids silently resuming at sprint speed on the next W.
        if (forward <= 0.0)
        {
            _sprintEnabled = false;
        }
        var sprint = _sprintEnabled && forward > 0.0;
        if (sprint)
        {
            // Tarkov lowers the weapon when sprint starts, including toggle ADS.
            _aimingEnabled = false;
            _slowWalkEnabled = false;
            _prone = false;
            _crouched = false;
        }
        var aiming = _aimingEnabled;
        var inputMagnitude = Math.Min(1.0, Math.Sqrt((forward * forward) + (right * right)));
        // Opposing taps may cancel their average vector, but they are not an idle interval.
        inputMagnitude = Math.Max(inputMagnitude, PrecisionMotionIntegrator.MovementActivity(
            forwardSample.HeldFraction, backwardSample.HeldFraction, leftSample.HeldFraction, rightSample.HeldFraction));
        var movementInputActive = inputMagnitude > 0.0;
        if (movementInputActive && !_movementInputWasActive)
        {
            // Toute nouvelle commande invalide la séquence visuelle précédente.
            // La capture doit de nouveau montrer un mouvement réel avant qu'une
            // image possiblement figée puisse autoriser un recalage de lieu.
            _visualMotionGate.InvalidateLiveness();
        }
        _movementInputWasActive = movementInputActive;

        var mouseDelta = Interlocked.Exchange(ref _pendingRawMouseX, 0);
        var rotationOccurred = Interlocked.Exchange(ref _precisionYawActivity, 0) != 0;
        var wheelDelta = Interlocked.Exchange(ref _pendingRawMouseWheel, 0);
        var duckWheelChord = Interlocked.Exchange(ref _pendingDuckWheelChord, 0) != 0;
        UpdateMovementSpeedScale(wheelDelta, duckWheelChord);

        if (movementInputActive || rotationOccurred || mouseDelta != 0)
        {
            _visualKeyframeRecognizer.CancelPendingMatch();
            // Un repère différé doit rester exactement à la pose F8. Dès que
            // le joueur bouge ou tourne, la vue courante n'est plus une ancre
            // absolue sûre et l'apprentissage est abandonné.
            _pendingVisualKeyframeLearning = null;
        }

        // Une longue pause de la boucle UI ne doit jamais compter comme une
        // nouvelle preuve visuelle ni déclencher le cooldown d'un recalage qui
        // ne sera pas appliqué.
        if (_realtimeEstimator.IsAnchored && elapsedSeconds > 2.0)
        {
            _realtimeEstimator.SuspendMotion(elapsedSeconds);
            _precisionSegmentClean = false;
            _precisionGaps++;
            _precisionYaw.Reset();
            _skipNextRealtimeCalibration = true;
            _visualMotionGate.Reset();
            _visualKeyframeRecognizer.CancelPendingMatch();
            _pendingVisualKeyframeLearning = null;
            ResetRealtimePoseHistory(now);
            return;
        }

        VisualKeyframePose? recognizedVisualPose = null;
        // Visual motion cannot produce an absolute position before the first
        // exact anchor. Delay GDI capture/feature work until it can actually
        // protect or correct a live estimate.
        if (_realtimeEstimator.IsAnchored
            && (now - _lastVisualMotionSample) / (double)Stopwatch.Frequency >= 0.1)
        {
            _lastVisualMotionSample = now;
            var needsVisualKeyframeCheck = !movementInputActive && !rotationOccurred
                && mouseDelta == 0
                && _visualMotionGate.State == VisualMotionState.Stationary;
            if (_visualMotionGate.TryObserveWindow(
                    _lastForegroundWindow,
                    out var visualObservation,
                    // Le suivi de points 64×36 tourne à 10 Hz. Il renforce
