using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace RaidPilot.Overlay;

internal sealed partial class OverlayForm : Form
{
    private readonly RaidPilotLocalMapHost _localMapHost = new();
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
    private const double SuperKeyframeSampleIntervalSeconds = 0.20;
    private const double SuperKeyframeMinimumUncertaintyMeters = 1.5;
    private const double SuperKeyframeAnchorConsistencyMeters = 4.0;
    private const double SuperKeyframeAnchorConsistencyVerticalMeters = 3.0;
    private const double SuperKeyframeAnchorConsistencyYawDegrees = 20.0;
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
    private readonly ToolStripMenuItem _mapCornerMenuItem = new("Position de la carte");
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
    private readonly ToolStripMenuItem _mobileExactAnchorMenuItem = new(
        "Recalage F8 pendant déplacement · 4 s (expérimental)")
    {
        Checked = false,
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
    private readonly SuperKeyframeRecognizer _superKeyframeRecognizer = new();
    private readonly VisualRelocalizationEngineV2 _visualRelocalization = new();
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
    private bool _mobileExactAnchorEnabled;
    private long _mobileExactAnchorRequests;
    private long _mobileExactAnchorConfirmed;
    private bool _visibleInScreenCaptures = true;
    private int? _mapScalePercent;
    private OverlayCorner _mapCorner = OverlayCorner.TopRight;
    private OverlayFreeLayout? _freeLayout;
    private double _effectiveMapScale = 1.0;
    private bool _applyingInteractiveFrame;
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
    private long _lastSuperKeyframeSample;
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
        _visualRelocalization.BeginSession(_learnedRouteClockOriginTimestamp);
        _controlSettings = EftControlSettings.Read();
        _visualMotionGate.ConfigureVerticalFieldOfView(
            EftGameViewSettings.Read().VerticalFieldOfViewDegrees);
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
        _superKeyframeRecognizer.ClearAllKeyframes();
        _ = SuperKeyframeStore.TryLoad(
            _superKeyframeRecognizer,
            SuperKeyframeStore.GetPath(_squadProfile.MemberId));
        _mobileExactAnchorEnabled = MobileExactAnchorPreference.Load();
        _mobileExactAnchorMenuItem.Checked = _mobileExactAnchorEnabled;
        _streamVisibilityMenuItem.Checked = OverlayPreferencesStore.LoadVisibleInCaptures();
        _mapScalePercent = OverlayPreferencesStore.LoadMapScalePercent();
        _mapCorner = OverlayPreferencesStore.LoadMapCorner();
        _freeLayout = OverlayPreferencesStore.LoadFreeLayout();
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
        InitializeCollectiveAtlas();
        InitializeQuestContext();
        InitializeAutomaticFloors();
        InitializeRaidClockUi();
        InitializePrecision();
        PositionOverlay();

        _realtimeTimer.Tick += OnRealtimeTimerTick;
        Shown += OnShown;
        Resize += OnInteractiveResize;
        ResizeEnd += OnInteractiveMoveOrResizeEnd;
        FormClosing += OnFormClosing;
    }

    protected override bool ShowWithoutActivation => _clickThrough;

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        PositionOverlay(e.DeviceDpiNew);
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
                    // Le bouton du milieu maintient le free-look EFT : la camÃƒÂ©ra
                    // tourne, mais pas l'axe du corps utilisÃƒÂ© par W/A/S/D.
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
            SetManualOverlayHidden(false);
            SetClickThrough(true, activateWhenInteractive: false);
            ShowNoActivate(force: true);
            ReassertTopMost();
        });
        menu.Items.Add("Interagir (Ctrl+Alt+I)", null, (_, _) =>
        {
            SetClickThrough(false, activateWhenInteractive: true);
        });
        menu.Items.Add(
            "Masquer (Ctrl+Alt+M)",
            null,
            (_, _) => SetManualOverlayHidden(true));
        _streamVisibilityMenuItem.Click += (_, _) =>
        {
            _ = TrySetStreamVisibility(
                !_visibleInScreenCaptures,
                persistPreference: true,
                showSuccess: true);
        };
        menu.Items.Add(_streamVisibilityMenuItem);
        menu.Items.Add(BuildMapSizeMenu());
        menu.Items.Add(BuildMapCornerMenu());
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
        _mobileExactAnchorMenuItem.Click += (_, _) =>
        {
            _mobileExactAnchorEnabled = _mobileExactAnchorMenuItem.Checked;
            var saved = MobileExactAnchorPreference.TrySave(_mobileExactAnchorEnabled);
            _trayIcon.ShowBalloonTip(
                4200,
                "RaidPilot — recalage en marche",
                _mobileExactAnchorEnabled
                    ? "Recalage F8 périodique actif pendant les déplacements, sous réserve de la touche Screenshot d'EFT (F6-F12 seule). Les captures peuvent afficher une notification en jeu."
                    : "Recalage pendant les déplacements arrêté. F8 manuel et recalage au repos restent disponibles."
                    + (saved ? "" : " Préférence non enregistrée."),
                saved ? ToolTipIcon.Info : ToolTipIcon.Warning);
        };
        menu.Items.Add(_mobileExactAnchorMenuItem);
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
        if (_freeLayout.HasValue)
        {
            _freeLayout = null;
            OverlayPreferencesStore.TrySaveFreeLayout(null);
        }
        PositionOverlay();
        RefreshMapSizeMenu();
        RefreshMapCornerMenu();

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

    private ToolStripMenuItem BuildMapCornerMenu()
    {
        _mapCornerMenuItem.DropDownOpening += (_, _) => RefreshMapCornerMenu();
        RefreshMapCornerMenu();
        return _mapCornerMenuItem;
    }

    private void RefreshMapCornerMenu()
    {
        _mapCornerMenuItem.DropDownItems.Clear();
        _mapCornerMenuItem.Text = _freeLayout.HasValue
            ? "Position de la carte (Libre)"
            : $"Position de la carte ({MapCornerLabel(_mapCorner)})";

        foreach (var (corner, label) in new[]
        {
            (OverlayCorner.TopLeft, "Haut gauche"),
            (OverlayCorner.TopRight, "Haut droite"),
            (OverlayCorner.BottomLeft, "Bas gauche"),
            (OverlayCorner.BottomRight, "Bas droite"),
        })
        {
            var item = new ToolStripMenuItem(label)
            {
                Checked = !_freeLayout.HasValue && _mapCorner == corner,
                CheckOnClick = false,
            };
            var capturedCorner = corner;
            item.Click += (_, _) => SetMapCorner(capturedCorner, showSuccess: true);
            _mapCornerMenuItem.DropDownItems.Add(item);
        }

        _mapCornerMenuItem.DropDownItems.Add(new ToolStripSeparator());
        var freeItem = new ToolStripMenuItem("Libre — déplacer et redimensionner")
        {
            Checked = _freeLayout.HasValue,
            CheckOnClick = false,
        };
        freeItem.Click += (_, _) => EnableFreeLayout();
        _mapCornerMenuItem.DropDownItems.Add(freeItem);
    }

    private void SetMapCorner(OverlayCorner corner, bool showSuccess)
    {
        if (!Enum.IsDefined(corner))
        {
            return;
        }

        _mapCorner = corner;
        _freeLayout = null;
        PositionOverlay();
        RefreshMapCornerMenu();

        var saved = OverlayPreferencesStore.TrySaveMapCorner(corner);
        if (!saved)
        {
            _trayIcon.ShowBalloonTip(
                4500,
                "RaidPilot — position de carte",
                "La position a changé pour cette session, mais RaidPilot n'a pas pu l'enregistrer.",
                ToolTipIcon.Warning);
            return;
        }

        if (showSuccess)
        {
            _trayIcon.ShowBalloonTip(
                2500,
                "RaidPilot — position de carte",
                $"Carte placée : {MapCornerLabel(corner)}.",
                ToolTipIcon.Info);
        }
    }

    private void EnableFreeLayout()
    {
        var workingArea = GetTargetWorkingArea();
        _freeLayout = OverlayLayoutCalculator.NormalizeFreeLayout(Bounds, workingArea);
        OverlayPreferencesStore.TrySaveFreeLayout(_freeLayout);
        RefreshMapCornerMenu();
        SetClickThrough(false, activateWhenInteractive: true);
        _trayIcon.ShowBalloonTip(
            4000,
            "RaidPilot — position libre",
            "Déplace ou redimensionne la fenêtre, puis refais Ctrl+Alt+I pour verrouiller la carte.",
            ToolTipIcon.Info);
    }

    private static string MapCornerLabel(OverlayCorner corner) => corner switch
    {
        OverlayCorner.TopLeft => "Haut gauche",
        OverlayCorner.TopRight => "Haut droite",
        OverlayCorner.BottomLeft => "Bas gauche",
        OverlayCorner.BottomRight => "Bas droite",
        _ => "Haut droite",
    };

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
        PositionOverlay();
        ApplyClickThroughStyle();
        ReassertTopMost();
        EnsureMapLogWatcherStarted();
        EnsureLocalScreenshotWatcherStarted();
        _lastRealtimeTick = Stopwatch.GetTimestamp();
        _realtimeTimer.Start();
        ApplyAutomaticOverlayVisibility();
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
        var tarkovCursorVisible =
            _tarkovIsForeground && NativeMethods.IsCursorVisible();
        UpdateAutomaticOverlayVisibility(
            _tarkovIsForeground,
            tarkovCursorVisible,
            now);
        var controlsActive = _liveTrackingEnabled
            && _tarkovIsForeground
            && !tarkovCursorVisible;
        if (!controlsActive)
        {
            if (_controlTrackingActive)
            {
                _precisionV2DriftAudit.MarkInterrupted();
                _realtimeEstimator.SuspendMotion();
                _precisionSegmentClean = false;
                _precisionGaps++;
                _visualMotionGate.Reset();
                _visualKeyframeRecognizer.CancelPendingMatch();
            _superKeyframeRecognizer.CancelPendingMatch();
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
            // Toute nouvelle commande invalide la sÃƒÂ©quence visuelle prÃƒÂ©cÃƒÂ©dente.
            // La capture doit de nouveau montrer un mouvement rÃƒÂ©el avant qu'une
            // image possiblement figÃƒÂ©e puisse autoriser un recalage de lieu.
            _visualMotionGate.InvalidateLiveness();
        }
        _movementInputWasActive = movementInputActive;

        var mouseDelta = Interlocked.Exchange(ref _pendingRawMouseX, 0);
        var rotationOccurred = Interlocked.Exchange(ref _precisionYawActivity, 0) != 0;
        var wheelDelta = Interlocked.Exchange(ref _pendingRawMouseWheel, 0);
        var duckWheelChord = Interlocked.Exchange(ref _pendingDuckWheelChord, 0) != 0;
        UpdateMovementSpeedScale(wheelDelta, duckWheelChord);

        // La relocalisation v2 peut confirmer un lieu pendant une marche
        // rectiligne. Seule une rotation significative invalide donc les
        // confirmations partielles des recognizers. L'apprentissage d'un
        // NOUVEAU repÃƒÂ¨re, lui, reste strictement immobile et est annulÃƒÂ© au
        // moindre dÃƒÂ©placement comme auparavant.
        if (rotationOccurred || Math.Abs(mouseDelta) > 3)
        {
            _visualKeyframeRecognizer.CancelPendingMatch();
            _superKeyframeRecognizer.CancelPendingMatch();
        }
        if (movementInputActive || rotationOccurred || mouseDelta != 0)
        {
            _pendingVisualKeyframeLearning = null;
        }

        // Une longue pause de la boucle UI ne doit jamais compter comme une
        // nouvelle preuve visuelle ni dÃƒÂ©clencher le cooldown d'un recalage qui
        // ne sera pas appliquÃƒÂ©.
        if (_realtimeEstimator.IsAnchored && elapsedSeconds > 2.0)
        {
            _precisionV2DriftAudit.MarkInterrupted();
            _realtimeEstimator.SuspendMotion(elapsedSeconds);
            _precisionSegmentClean = false;
            _precisionGaps++;
            _precisionYaw.Reset();
            _skipNextRealtimeCalibration = true;
            _visualMotionGate.Reset();
            _visualKeyframeRecognizer.CancelPendingMatch();
            _superKeyframeRecognizer.CancelPendingMatch();
            _pendingVisualKeyframeLearning = null;
            ResetRealtimePoseHistory(now);
            return;
        }

        VisualRelocalizationDecision? visualRelocalizationDecision = null;
        RealtimePose? visualRelocalizationPoseAtCapture = null;
        var visualDecisionFromCollectiveAtlas = false;
        // Visual motion cannot produce an absolute position before the first
        // exact anchor. Delay GDI capture/feature work until it can actually
        // protect or correct a live estimate.
        if (_realtimeEstimator.IsAnchored
            && (now - _lastVisualMotionSample) / (double)Stopwatch.Frequency >= 0.1)
        {
            var visualIntervalStart = _lastVisualMotionSample;
            _lastVisualMotionSample = now;
            if (_visualMotionGate.TryObserveWindow(
                    _lastForegroundWindow,
                    out var visualObservation,
                    // Le suivi de points 64Ãƒâ€”36 tourne ÃƒÂ  10 Hz. Il renforce
                    // l'hystÃƒÂ©rÃƒÂ©sis de mouvement mÃƒÂªme quand aucun repÃƒÂ¨re n'est
                    // recherchÃƒÂ©, tout en restant indÃƒÂ©pendant de la pose absolue.
                    estimateEgomotion: true))
            {
                TryCalibrateYawFromVision(
                    visualIntervalStart,
                    now,
                    visualObservation,
                    movementInputActive,
                    aiming);

                if (_realtimeEstimator.CurrentPose is { } estimatedVisualPose)
                {
                    _visualRelocalization.ObserveTracking(
                        estimatedVisualPose,
                        now);

                    var stationaryForLearning =
                        !movementInputActive
                        && !rotationOccurred
                        && mouseDelta == 0
                        && visualObservation.CaptureProvenLive
                        && visualObservation.Egomotion.State
                            == VisualEgomotionState.Stationary
                        && _visualMotionGate.State
                            == VisualMotionState.Stationary;

                    if (visualObservation.CaptureProvenLive
                        && _visualMotionGate.TryCopyLatestLuminance(
                            out var visualFrame))
                    {
                        var learnedPendingKeyframe =
                            stationaryForLearning
                            && TryLearnPendingVisualKeyframe(
                                visualFrame,
                                now);
                        if (learnedPendingKeyframe)
                        {
                            // Le nouveau repÃƒÂ¨re vient d'ÃƒÂªtre appris ÃƒÂ  une pose
                            // absolue. Une confirmation commencÃƒÂ©e avant cet
                            // apprentissage ne doit pas survivre ÃƒÂ  ce changement
                            // de base visuelle.
                            _visualKeyframeRecognizer.CancelPendingMatch();
                            _superKeyframeRecognizer.CancelPendingMatch();
                        }
                        else
                        {
                            var canMatch =
                                !rotationOccurred
                                && Math.Abs(mouseDelta) <= 3;
                            var estimatedKeyframePose =
                                new VisualKeyframePose(
                                    estimatedVisualPose.X,
                                    estimatedVisualPose.Y,
                                    estimatedVisualPose.Z,
                                    estimatedVisualPose.YawDegrees);

                            var lowRecognition =
                                VisualKeyframeRecognition.NoMatch();
                            var collectiveLowRecognition =
                                VisualKeyframeRecognition.NoMatch();
                            var lowAttempted = false;
                            if (canMatch
                                && _visualRelocalization
                                    .ShouldRunLowResolution(
                                        movementInputActive,
                                        now))
                            {
                                lowAttempted = true;
                                lowRecognition =
                                    _visualKeyframeRecognizer.Observe(
                                        _currentMap,
                                        visualFrame,
                                        _visualMotionGate.SampleWidth,
                                        _visualMotionGate.SampleHeight,
                                        estimatedKeyframePose,
                                        now,
                                        estimatedVisualPose
                                            .PositionUncertaintyMeters);

                                if (CollectiveAtlasHasLowResolution(
                                        _currentMap))
                                {
                                    collectiveLowRecognition =
                                        ObserveCollectiveLow(
                                            _currentMap,
                                            visualFrame,
                                            estimatedKeyframePose,
                                            now,
                                            estimatedVisualPose
                                                .PositionUncertaintyMeters);
                                }
                            }

                            var superAvailable =
                                _superKeyframeRecognizer
                                    .KeyframeCountForMap(
                                        _currentMap) > 0;
                            var collectiveSuperAvailable =
                                CollectiveAtlasHasSuperKeyframes(
                                    _currentMap);
                            var anySuperAvailable =
                                superAvailable
                                || collectiveSuperAvailable;
                            var adaptiveHighResolutionDue =
                                canMatch
                                && _visualRelocalization
                                    .ShouldRunHighResolution(
                                        estimatedVisualPose,
                                        anySuperAvailable,
                                        now);
                            var lowNeedsCorroboration =
                                canMatch
                                && anySuperAvailable
                                && (lowRecognition.IsConfirmed
                                    || collectiveLowRecognition.IsConfirmed)
                                && (_lastSuperKeyframeSample == 0
                                    || (now - _lastSuperKeyframeSample)
                                        / (double)Stopwatch.Frequency
                                        >= SuperKeyframeSampleIntervalSeconds);
                            var superDue =
                                anySuperAvailable
                                && (adaptiveHighResolutionDue
                                    || lowNeedsCorroboration);

                            var superRecognition =
                                SuperKeyframeRecognition.NoMatch();
                            var collectiveSuperRecognition =
                                SuperKeyframeRecognition.NoMatch();
                            var highResolutionAttempted = false;
                            if (superDue)
                            {
                                highResolutionAttempted = true;
                                _lastSuperKeyframeSample = now;
                                if (HighResolutionVisualSampler
                                    .TryCaptureWindow(
                                        _lastForegroundWindow,
                                        out var highResolutionFrame))
                                {
                                    if (superAvailable)
                                    {
                                        superRecognition =
                                            _superKeyframeRecognizer.Observe(
                                                _currentMap,
                                                highResolutionFrame,
                                                estimatedKeyframePose,
                                                now,
                                                estimatedVisualPose
                                                    .PositionUncertaintyMeters);
                                    }

                                    if (collectiveSuperAvailable)
                                    {
                                        collectiveSuperRecognition =
                                            ObserveCollectiveSuper(
                                                _currentMap,
                                                highResolutionFrame,
                                                estimatedKeyframePose,
                                                now,
                                                estimatedVisualPose
                                                    .PositionUncertaintyMeters);
                                    }
                                }
                                else
                                {
                                    _superKeyframeRecognizer
                                        .CancelPendingMatch();
                                    _collectiveSuperKeyframeRecognizer
                                        .CancelPendingMatch();
                                }
                            }

                            if (lowAttempted
                                || highResolutionAttempted)
                            {
                                var evaluatedRelocalization =
                                    _visualRelocalization.Evaluate(
                                        estimatedVisualPose,
                                        lowRecognition,
                                        superRecognition,
                                        superAvailable,
                                        now,
                                        commitImmediately: !_precisionV4Menu.Checked);
                                if (evaluatedRelocalization.ShouldCorrect)
                                {
                                    visualRelocalizationDecision =
                                        evaluatedRelocalization;
                                    visualRelocalizationPoseAtCapture =
                                        estimatedVisualPose;
                                }
                                else if (collectiveLowRecognition.IsConfirmed
                                    || collectiveSuperRecognition.IsConfirmed)
                                {
                                    var collectiveDecision =
                                        _collectiveAtlasRelocalization.Evaluate(
                                            estimatedVisualPose,
                                            collectiveLowRecognition,
                                            collectiveSuperRecognition,
                                            collectiveSuperAvailable,
                                            now,
                                            commitImmediately: !_precisionV4Menu.Checked);
                                    if (ShouldAcceptCollectiveCorrection(
                                            collectiveDecision,
                                            true))
                                    {
                                        visualRelocalizationDecision =
                                            collectiveDecision;
                                        visualRelocalizationPoseAtCapture =
                                            estimatedVisualPose;
                                        visualDecisionFromCollectiveAtlas = true;
                                    }
                                }
                            }

                            if (!canMatch)
                            {
                                _visualKeyframeRecognizer
                                    .CancelPendingMatch();
                                _superKeyframeRecognizer
                                    .CancelPendingMatch();
                                _collectiveVisualKeyframeRecognizer
                                    .CancelPendingMatch();
                                _collectiveSuperKeyframeRecognizer
                                    .CancelPendingMatch();
                            }
                        }
                    }
                    else if (!visualObservation.CaptureProvenLive)
                    {
                        _visualKeyframeRecognizer.CancelPendingMatch();
                        _superKeyframeRecognizer.CancelPendingMatch();
                        _collectiveVisualKeyframeRecognizer.CancelPendingMatch();
                        _collectiveSuperKeyframeRecognizer.CancelPendingMatch();
                    }
                }
            }
            else
            {
                _visualKeyframeRecognizer.CancelPendingMatch();
                _superKeyframeRecognizer.CancelPendingMatch();
                _collectiveVisualKeyframeRecognizer.CancelPendingMatch();
                _collectiveSuperKeyframeRecognizer.CancelPendingMatch();
            }
        }
        if (Math.Abs(mouseDelta) > 3
            && _visualMotionGate.State == VisualMotionState.Stationary)
        {
            // Une rotation rÃƒÂ©elle avec des pixels prÃƒÂ©tendument figÃƒÂ©s indique
            // que la surface GDI ne se rafraÃƒÂ®chit plus. Retirer sa confiance.
            _visualMotionGate.InvalidateLiveness();
        }
        var visualCaptureAvailable = _visualMotionGate.CaptureProvenLive;
        var visualMotionState = _visualMotionGate.State;
        if (movementInputActive
            && visualCaptureAvailable
            && visualMotionState == VisualMotionState.Stationary)
        {
            _commandedStationarySeconds += Math.Clamp(elapsedSeconds, 0.0, 0.25);
            if (_commandedStationarySeconds >= MaximumCommandedStationarySeconds)
            {
                // Une surface GDI peut se figer aprÃƒÂ¨s avoir dÃƒÂ©jÃƒÂ  montrÃƒÂ© du
                // mouvement. Comme un mur rÃƒÂ©el et une image gelÃƒÂ©e sont alors
                // indiscernables, Stationary expire au lieu de bloquer le point
                // indÃƒÂ©finiment pendant qu'une touche reste maintenue.
                _visualMotionGate.InvalidateLiveness();
                visualCaptureAvailable = false;
                visualMotionState = VisualMotionState.Unknown;
                _commandedStationarySeconds = 0.0;
            }
        }
        else
        {
            _commandedStationarySeconds = 0.0;
        }

        // Une courte pause UI conserve le dÃƒÂ©placement; pour une pause plus
        // longue, borner l'intÃƒÂ©gration ÃƒÂ©vite autant un saut qu'une perte totale.
        if (!_realtimeEstimator.IsAnchored
            || elapsedSeconds <= 0.0)
        {
            RecordCurrentRealtimePose(now);
            return;
        }
        var integrationSeconds = Math.Min(elapsedSeconds, 0.75);
        var motionEvidence = RealtimeMotionEvidence.Unknown;
        if (visualCaptureAvailable && !rotationOccurred && Math.Abs(mouseDelta) <= 3)
        {
            motionEvidence = visualMotionState switch
            {
                VisualMotionState.Moving => RealtimeMotionEvidence.Moving,
                VisualMotionState.Stationary => RealtimeMotionEvidence.Stationary,
                _ => RealtimeMotionEvidence.Unknown,
            };
        }

        motionEvidence = FilterAmbiguousStationaryEvidence(
            motionEvidence,
            movementInputActive,
            sprint);
        motionEvidence = FusePrecisionV2(
            now, movementInputActive, rotationOccurred, aiming, motionEvidence);

        var gaitSpeedScale = _slowWalkEnabled || sprint
            ? 1.0
            : _movementSpeedScale;
        var stanceSpeedScale = _prone
            ? ProneMovementScale
            : _crouched
                ? CrouchedMovementScale
                : 1.0;
        var movementSpeedScale = gaitSpeedScale * stanceSpeedScale;
        if (movementSpeedScale < 0.35)
        {
            // Ãƒâ‚¬ trÃƒÂ¨s basse vitesse, quelques centimÃƒÂ¨tres entre deux trames
            // peuvent ressembler ÃƒÂ  une image fixe aprÃƒÂ¨s rÃƒÂ©duction. Ne jamais
            // transformer ce doute en faux arrÃƒÂªt.
            motionEvidence = RealtimeMotionEvidence.Unknown;
        }
        if (elapsedSeconds > 0.75)
        {
            _realtimeEstimator.SuspendMotion(elapsedSeconds - 0.75);
            _precisionGaps++;
        }
        if (_prone || _crouched || movementSpeedScale < 0.99 || right != 0 || forward < 0
            || motionEvidence == RealtimeMotionEvidence.Stationary && movementInputActive
            || elapsedSeconds > 0.15)
            _precisionSegmentClean = false;
        var previousPose = _realtimeEstimator.CurrentPose!.Value;

        RealtimePose pose;
        double precisionPath = double.NaN;
        try
        {
            if (TryAdvancePrecision(inputIntervalStart, now, sprint, _slowWalkEnabled,
                movementSpeedScale, motionEvidence, mouseDelta, out var integrated))
            {
                pose = integrated.Pose;
                precisionPath = integrated.PathMeters;
            }
            else
            {
                _precisionSegmentClean = false;
                pose = _realtimeEstimator.Advance(integrationSeconds, forward, right, sprint,
                    _slowWalkEnabled, mouseDelta, aiming, movementSpeedScale, motionEvidence);
            }
        }
        catch (InvalidOperationException)
        {
            return;
        }
        var visualCorrectionApplied = false;
        var visualCorrectionSnap = false;
        var visualCorrectionAuthoritative = false;
        // V4 uses the V2 recognizer consensus and the existing odometry;
        // only the measured correction gain and rejection policy change.
        var fusedVisualDecision =
            visualRelocalizationDecision is { } proposed
            && visualRelocalizationPoseAtCapture is { } visualEstimate
                ? FilterPrecisionV4(proposed, visualEstimate, now)
                : null;
        if (fusedVisualDecision is { } decision
            && decision.ShouldCorrect
            && decision.TargetPose is { } visualAnchor
            && visualRelocalizationPoseAtCapture is { } visualPoseAtCapture)
        {
            var anchorObservation = new ScreenshotObservation(
                "raidpilot-visual-v2",
                visualAnchor.X,
                visualAnchor.Y,
                visualAnchor.Z,
                0,
                0,
                0,
                1,
                visualAnchor.YawDegrees);

            pose = _realtimeEstimator.ApplyVisualRelocalizationCorrection(
                visualAnchor,
                visualPoseAtCapture,
                decision.PositionGain,
                decision.YawGain,
                decision.MaximumTranslationStepMeters,
                decision.MaximumYawStepDegrees);

            if (_precisionV4Menu.Checked)
            {
                // Only an APPLIED correction may reset health/acceptance metrics.
                (visualDecisionFromCollectiveAtlas
                    ? _collectiveAtlasRelocalization
                    : _visualRelocalization).NoteAppliedCorrection(
                        decision, now, countAsAbsoluteConfirmation: false);
            }
            visualCorrectionApplied = true;
            visualCorrectionSnap =
                decision.Mode == VisualCorrectionMode.HardSnap;
            visualCorrectionAuthoritative =
                decision.Mode is VisualCorrectionMode.Medium
                    or VisualCorrectionMode.HardSnap;

            // Une correction moyenne ou un loop-closure change suffisamment
            // le repÃƒÂ¨re pour casser la continuitÃƒÂ© d'une route apprise. Une
            // micro-correction reste au contraire sous le bruit normal du
            // graphe et ne doit pas fragmenter le trajet.
            if (visualCorrectionAuthoritative)
            {
                _learnedRouteGraph.BreakContinuity();
                _lastLearnedRouteObservationAt = 0;
                _guidanceGeneration = unchecked(
                    _guidanceGeneration + 1);
                InvalidateLearnedRoutePlan();
            }

            if (!_precisionV4Menu.Checked)
            {
                // A bounded, partially fused visual observation is not a
                // native F8 anchor and must not replace the last exact one.
                _lastExactObservation = anchorObservation;
                RecordAutomaticFloorAnchor(
                    anchorObservation,
                    now,
                    decision.GlobalLoopClosure
                        ? "visual-v2-loop"
                        : "visual-v2");
                _lastExactAnchorAt = now;
            }
            _idleSeconds = 0.0;
            // Une correction visuelle crÃƒÂ©e une discontinuitÃƒÂ© par rapport au
            // segment utilisÃƒÂ© pour apprendre les vitesses. Le prochain F8 ne
            // doit donc pas calibrer une vitesse ÃƒÂ  travers cette correction.
            _skipNextRealtimeCalibration = true;
            _precisionSegmentClean = false;
            _realtimePoseHistory.Reset();
            ResetDistanceCalibrationCounters();
            Interlocked.Exchange(ref _hipMouseCountsSinceAnchor, 0);
            Interlocked.Exchange(ref _adsMouseCountsSinceAnchor, 0);
            Interlocked.Exchange(ref _hipMouseMagnitudeSinceAnchor, 0);
            Interlocked.Exchange(ref _adsMouseMagnitudeSinceAnchor, 0);
        }

        _visualRelocalization.ObserveTracking(pose, now);
        UpdateMineProximity(pose);
        UpdateBoundaryProximity(pose);
        _realtimePoseHistory.Add(now, pose);
        ObserveLearnedRoutePose(
            pose,
            visualCorrectionAuthoritative && !_precisionV4Menu.Checked
                ? LearnedPoseSource.ExactAnchor
                : LearnedPoseSource.LiveEstimate,
            pose.PositionUncertaintyMeters,
            now,
            force: visualCorrectionAuthoritative);
        // Le garde interne limite l'ÃƒÂ©criture ÃƒÂ  une fois toutes les 45 s. Le
        // solliciter ÃƒÂ  chaque tick ÃƒÂ©vite de perdre tout le trajet depuis le
        // dernier F8 si le jeu ou Windows s'arrÃƒÂªte brutalement.
        MaybeSaveLearnedRouteGraph(now);

        // Une correction de relocalisation n'est pas une distance parcourue
        // pendant ce tick et ne doit jamais alimenter l'apprentissage des
        // vitesses de marche.
        if (ShouldAccumulateCalibrationDistance(visualCorrectionApplied))
        {
            var estimatedDistance = double.IsFinite(precisionPath) ? precisionPath : Math.Sqrt(
                ((pose.X - previousPose.X) * (pose.X - previousPose.X))
                + ((pose.Z - previousPose.Z) * (pose.Z - previousPose.Z)));
            if (_slowWalkEnabled)
            {
                _slowDistanceSinceAnchor += estimatedDistance;
            }
            else if (sprint)
            {
                _sprintDistanceSinceAnchor += estimatedDistance;
            }
            else
            {
                _walkDistanceSinceAnchor += estimatedDistance;
            }
        }

        var observation = new ScreenshotObservation(
            "raidpilot-live",
            pose.X,
            pose.Y,
            pose.Z,
            0,
            0,
            0,
            1,
            pose.YawDegrees);
        _lastObservation = observation;
        _questContextPoseTimestamp = now;
        _floorPoseTimestamp = now;

        TryAutoReanchor(
            now,
            integrationSeconds,
            inputMagnitude,
            mouseDelta,
            aiming,
            pose.PositionUncertaintyMeters,
            rotationOccurred);

        if ((now - _lastLiveSocketUpdate) / (double)Stopwatch.Frequency >= LivePositionSendIntervalSeconds)
        {
            _lastLiveSocketUpdate = now;
            _ = UpdateLocalPlayerMarkerAsync(
                observation,
                pose.PositionUncertaintyMeters,
                snap: visualCorrectionSnap);
            MaybeSendSquadPose(
                observation,
                pose.PositionUncertaintyMeters);
            _ = SendLivePositionAsync(observation);
        }

        if ((_selectedExtraction is not null || _confirmedExtractionIds is not null)
            && (now - _lastLiveGuidanceUpdate) / (double)Stopwatch.Frequency >= 0.1)
        {
            _lastLiveGuidanceUpdate = now;
            var target = _selectedExtraction;
            if (_confirmedExtractionIds is not null
                && TryChooseNearestConfirmedExtraction(observation, out var automaticTarget))
            {
                target = automaticTarget;
                if (!IsSameExtraction(automaticTarget, _selectedExtraction))
                {
                    SelectExtractionTarget(automaticTarget);
                    _extractionMenuItem.Text = $"Extraction : {automaticTarget.Name} (auto)";
                }
            }

            if (target is not null)
            {
                _ = UpdateLiveGuidanceAsync(
                    observation,
                    target,
                    pose.PositionUncertaintyMeters,
                    _guidanceGeneration);
            }
        }
    }

    private void UpdateMovementSpeedScale(int wheelDelta, bool duckWheelChord)
    {
        if (wheelDelta == 0
            || duckWheelChord
            || IsKeyDown(_controlSettings.Duck)
            || IsKeyDown(Keys.R)
            || IsKeyDown(Keys.LControlKey)
            || IsKeyDown(Keys.RControlKey)
            || IsKeyDown(Keys.LMenu)
            || IsKeyDown(Keys.RMenu))
        {
            return;
        }

        // EFT rÃƒÂ¨gle la vitesse de marche sur une ÃƒÂ©chelle continue avec la
        // molette. Sept centiÃƒÂ¨mes par cran reproduisent les paliers usuels sans
        // laisser une molette haute rÃƒÂ©solution sortir des bornes.
        var wheelSteps = wheelDelta / 120.0;
        _movementSpeedScale = Math.Clamp(
            _movementSpeedScale + (wheelSteps * 0.07),
            0.25,
            1.0);
    }

    private void TryAutoReanchor(
        long now,
        double elapsedSeconds,
        double inputMagnitude,
        int mouseDelta,
        bool aiming,
        double uncertaintyMeters,
        bool rotationOccurred)
    {
        var playerActionInProgress = inputMagnitude > 0.0
            || rotationOccurred
            || Math.Abs(mouseDelta) > 1
            || aiming
            || IsKeyDown(Keys.LButton)
            || IsKeyDown(_controlSettings.Jump);
        var visualActionInProgress = _visualMotionGate.CaptureProvenLive
            && _visualMotionGate.State != VisualMotionState.Stationary;
        var actionInProgress = playerActionInProgress || visualActionInProgress;

        var pendingRequest = _automaticReanchorRequest;
        if (pendingRequest is not null)
        {
            if (playerActionInProgress)
            {
                pendingRequest.MovementOccurred = true;
            }
            if (pendingRequest.IsWithinWindow(now, seconds: 4.0))
            {
                return;
            }

            _automaticReanchorRequest = null;
            Interlocked.Exchange(ref _autoReanchorRequestedAt, 0);
        }

        var secondsSinceLastAttempt = _lastAutoReanchorAttempt == 0
            ? double.PositiveInfinity
            : (now - _lastAutoReanchorAttempt) / (double)Stopwatch.Frequency;
        var secondsSinceExactAnchor = _lastExactAnchorAt <= 0
            ? 0.0
            : (now - _lastExactAnchorAt) / (double)Stopwatch.Frequency;
        var mobileRequest = MobileExactAnchorPolicy.ShouldCapture(
            _mobileExactAnchorEnabled,
            _liveTrackingEnabled && _tarkovIsForeground && _controlTrackingActive && _raidActive,
            inputMagnitude > 0.15,
            aiming || _middleMouseDown || IsKeyDown(Keys.LButton)
                || IsKeyDown(Keys.RButton),
            ScreenshotHotkeySender.CanSendWhileMoving(_screenshotBinding),
            _screenshotWatcher is not null,
            _lastExactObservation is not null,
            secondsSinceExactAnchor,
            secondsSinceLastAttempt);

        if (actionInProgress)
        {
            _idleSeconds = 0.0;
        }
        else
        {
            _idleSeconds += elapsedSeconds;
        }

        var stationaryRecoveryReady = !actionInProgress && _autoReanchorEnabled
            && (LocalizationRecoveryPolicy.ShouldAttemptAutomaticReanchor(
                    uncertaintyMeters, _idleSeconds, secondsSinceLastAttempt)
                || LocalizationRecoveryPolicy.ShouldEscalateFromLocalizationHealth(
                    _visualRelocalization.Health == LocalizationHealthState.Red,
                    uncertaintyMeters, _idleSeconds, secondsSinceLastAttempt));
        if (!mobileRequest && !stationaryRecoveryReady)
            return;

        if (_lastExactObservation is null
            || !ScreenshotHotkeySender.CanSend(_screenshotBinding)
            || _screenshotWatcher is null
            || secondsSinceExactAnchor < 1.5)
            return;

        var existingFileNames = SnapshotScreenshotFileNames();
        var requestedAtTimestamp = Stopwatch.GetTimestamp();
        var requestedAtUtc = DateTimeOffset.UtcNow;
        _lastAutoReanchorAttempt = requestedAtTimestamp;
        var automaticRequest = new AutomaticReanchorRequest(
            requestedAtTimestamp,
            requestedAtUtc,
            _raidGeneration,
            existingFileNames,
            requestedWhileMoving: mobileRequest);
        _automaticReanchorRequest = automaticRequest;
        Interlocked.Exchange(ref _autoReanchorRequestedAt, requestedAtTimestamp);
        if (ScreenshotHotkeySender.TrySend(_screenshotBinding))
        {
            _idleSeconds = 0.0;
            if (mobileRequest) _mobileExactAnchorRequests++;
        }
        else
        {
            _automaticReanchorRequest = null;
            Interlocked.Exchange(ref _autoReanchorRequestedAt, 0);
        }
    }

    private IReadOnlySet<string> SnapshotScreenshotFileNames()
    {
        var watcher = _screenshotWatcher;
        if (watcher is null)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        return watcher.SnapshotKnownFileNames();
    }

    private IReadOnlySet<string> SnapshotScreenshotFileNamesFromDisk()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var watcher = _screenshotWatcher;
        if (watcher is null)
        {
            return names;
        }

        try
        {
            foreach (var path in Directory.EnumerateFiles(
                watcher.ScreenshotsPath,
                "*.png",
                SearchOption.TopDirectoryOnly))
            {
                names.Add(Path.GetFileName(path));
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or DirectoryNotFoundException)
        {
            // Un inventaire incomplet empÃƒÂªche seulement la suppression automatique.
        }

        return names;
    }

    private bool TryGetScreenshotTimestamp(
        string fileName,
        out string fullPath,
        out DateTimeOffset timestampUtc)
    {
        fullPath = string.Empty;
        timestampUtc = default;
        var watcher = _screenshotWatcher;
        if (watcher is null)
        {
            return false;
        }

        try
        {
            var root = Path.GetFullPath(watcher.ScreenshotsPath);
            var safeName = Path.GetFileName(fileName);
            if (!string.Equals(safeName, fileName, StringComparison.Ordinal))
            {
                return false;
            }

            fullPath = Path.GetFullPath(Path.Combine(root, safeName));
            if (!string.Equals(Path.GetDirectoryName(fullPath), root, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var file = new FileInfo(fullPath);
            if (!file.Exists)
            {
                return false;
            }

            var creation = new DateTimeOffset(file.CreationTimeUtc);
            var written = new DateTimeOffset(file.LastWriteTimeUtc);
            timestampUtc = creation > written ? creation : written;
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or DirectoryNotFoundException)
        {
            return false;
        }
    }

    private bool TryResolveScreenshotCaptureTimestamp(
        AutomaticReanchorRequest? automaticRequest,
        bool isAutomaticReanchor,
        bool hasFileTimestamp,
        DateTimeOffset fileTimestampUtc,
        long nowTimestamp,
        out long captureTimestamp)
    {
        captureTimestamp = 0;
        if (isAutomaticReanchor && automaticRequest is not null)
        {
            captureTimestamp = automaticRequest.RequestedAtTimestamp;
            _lastManualScreenshotPressAt = 0;
            return true;
        }

        var manualPress = _lastManualScreenshotPressAt;
        if (manualPress != 0 && nowTimestamp >= manualPress)
        {
            var ageSeconds = (nowTimestamp - manualPress) / (double)Stopwatch.Frequency;
            if (ageSeconds <= ScreenshotCaptureMatchWindowSeconds)
            {
                captureTimestamp = manualPress;
                _lastManualScreenshotPressAt = 0;
                return true;
            }
        }

        _lastManualScreenshotPressAt = 0;
        if (!hasFileTimestamp)
        {
            return false;
        }

        var fileAgeSeconds = (DateTimeOffset.UtcNow - fileTimestampUtc).TotalSeconds;
        if (fileAgeSeconds < -1.0 || fileAgeSeconds > ScreenshotCaptureMatchWindowSeconds)
        {
            return false;
        }

        fileAgeSeconds = Math.Clamp(fileAgeSeconds, 0.0, ScreenshotCaptureMatchWindowSeconds);
        captureTimestamp = nowTimestamp
            - (long)Math.Round(fileAgeSeconds * Stopwatch.Frequency);
        return true;
    }

    private async Task FinalizeAutomaticScreenshotAsync(AutomaticReanchorRequest request)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), _formLifetime.Token);
            if (!ReferenceEquals(_automaticReanchorRequest, request))
            {
                return;
            }

            var candidate = request.CandidateFileName;
            if (candidate is null || request.IsAmbiguous)
            {
                return;
            }

            var diskSnapshot = await Task.Run(
                SnapshotScreenshotFileNamesFromDisk,
                _formLifetime.Token);
            var newFiles = diskSnapshot
                .Where(name => !request.ExistingFileNames.Contains(name))
                .ToArray();
            if (newFiles.Length != 1
                || !string.Equals(newFiles[0], candidate, StringComparison.OrdinalIgnoreCase)
                || !TryGetScreenshotTimestamp(candidate, out var path, out var timestampUtc)
                || timestampUtc < request.RequestedAtUtc - TimeSpan.FromSeconds(1))
            {
                return;
            }

            File.Delete(path);
        }
        catch (OperationCanceledException) when (_formLifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or DirectoryNotFoundException)
        {
            // Le recalage est dÃƒÂ©jÃƒÂ  appliquÃƒÂ©; conserver le PNG en cas de verrou
            // temporaire est prÃƒÂ©fÃƒÂ©rable ÃƒÂ  gÃƒÂªner le suivi.
        }
        finally
        {
            if (ReferenceEquals(_automaticReanchorRequest, request))
            {
                _automaticReanchorRequest = null;
                Interlocked.Exchange(ref _autoReanchorRequestedAt, 0);
            }
        }
    }

    private Task SendLivePositionAsync(ScreenshotObservation observation)
    {
        return SendLivePositionAsync((observation, _currentMap, _raidGeneration));
    }

    private async Task SendLivePositionAsync(
        (ScreenshotObservation Observation, string Map, int RaidGeneration) update)
    {
        if (!_gpsEnabled
            || update.RaidGeneration != _raidGeneration
            || !string.Equals(update.Map, _currentMap, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        if (_livePositionSendInProgress)
        {
            // Ne jamais empiler les 20 mises ÃƒÂ  jour par seconde : conserver
            // uniquement la pose la plus rÃƒÂ©cente, avec sa carte et son raid,
            // pendant l'envoi courant.
            _pendingLivePosition = update;
            return;
        }

        var client = _gpsClient;
        if (client is null)
        {
            return;
        }

        _livePositionSendInProgress = true;
        try
        {
            await client.SendPositionAsync(update.Observation, update.Map, _formLifetime.Token);
        }
        catch (OperationCanceledException) when (_formLifetime.IsCancellationRequested || !_gpsEnabled)
        {
        }
        catch (Exception) when (!_gpsEnabled)
        {
        }
        catch
        {
            // La prochaine mise ÃƒÂ  jour ou le prochain F8 rÃƒÂ©essaiera.
        }
        finally
        {
            _livePositionSendInProgress = false;
            var pending = _pendingLivePosition;
            _pendingLivePosition = null;
            if (_gpsEnabled
                && pending is { } next
                && next.RaidGeneration == _raidGeneration
                && string.Equals(next.Map, _currentMap, StringComparison.OrdinalIgnoreCase))
            {
                _ = SendLivePositionAsync(next);
            }
        }
    }

    private async Task UpdateLiveGuidanceAsync(
        ScreenshotObservation observation,
        ExtractionCandidate target,
        double uncertaintyMeters,
        int guidanceGeneration)
    {
        if (_liveGuidanceUpdateInProgress)
        {
            return;
        }

        _liveGuidanceUpdateInProgress = true;
        try
        {
            await ShowExtractionGuidanceAsync(
                observation,
                target,
                guidanceGeneration,
                estimated: true,
                uncertaintyMeters);
        }
        finally
        {
            _liveGuidanceUpdateInProgress = false;
        }
    }

    private void SetLiveTrackingEnabled(bool enabled)
    {
        _liveTrackingEnabled = enabled;
        _learnedRouteGraph.BreakContinuity();
        _lastLearnedRouteObservationAt = 0;
        _liveTrackingMenuItem.Checked = enabled;
        Interlocked.Exchange(ref _pendingRawMouseX, 0);
        Interlocked.Exchange(ref _precisionYawActivity, 0);
        Interlocked.Exchange(ref _pendingRawMouseWheel, 0);
        Interlocked.Exchange(ref _pendingDuckWheelChord, 0);
        Interlocked.Exchange(ref _hipMouseCountsSinceAnchor, 0);
        Interlocked.Exchange(ref _adsMouseCountsSinceAnchor, 0);
        Interlocked.Exchange(ref _hipMouseMagnitudeSinceAnchor, 0);
        Interlocked.Exchange(ref _adsMouseMagnitudeSinceAnchor, 0);
        _automaticReanchorRequest = null;
        Interlocked.Exchange(ref _autoReanchorRequestedAt, 0);
        _skipNextRealtimeCalibration = false;
        _idleSeconds = 0.0;
        _visualMotionGate.Reset();
        ResetPrecisionV2();
        _visualKeyframeRecognizer.CancelPendingMatch();
        _superKeyframeRecognizer.CancelPendingMatch();
        _pendingVisualKeyframeLearning = null;
        ResetDistanceCalibrationCounters();
        ResetControlTrackingState();
        _lastRealtimeTick = Stopwatch.GetTimestamp();
        _realtimePoseHistory.Reset();

        if (enabled && _lastExactObservation is not null)
        {
            _realtimeEstimator.Anchor(_lastExactObservation);
            _realtimePoseHistory.Add(_lastRealtimeTick, _realtimeEstimator.CurrentPose!.Value);
            _lastObservation = _lastExactObservation;
        }
        else if (!enabled)
        {
            _realtimeEstimator.Reset();
            _lastObservation = _lastExactObservation;
            _guidanceGeneration = unchecked(_guidanceGeneration + 1);
            if (_lastExactObservation is { } exactObservation)
            {
                _ = SendObservationAsync(exactObservation);
                _ = UpdateExtractionGuidanceAsync(exactObservation);
            }
        }

        if (_lastObservation is { } markerObservation)
        {
            var uncertainty = _realtimeEstimator.CurrentPose is { } pose
                ? pose.PositionUncertaintyMeters
                : 0.0;
            _ = UpdateLocalPlayerMarkerAsync(
                markerObservation,
                uncertainty,
                snap: true);
        }

        _trayIcon.ShowBalloonTip(
            3500,
            "RaidPilot — suivi",
            enabled
                ? "Suivi fluide activé : la position bouge entre les recalibrages F8."
                : "Suivi fluide désactivé : la position change seulement avec F8.",
            ToolTipIcon.Info);
    }

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
            return _tarkovIsForeground;
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
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private bool CanAcceptRawRealtimeInput()
    {
        // RIDEV_INPUTSINK is required because the click-through overlay never
        // owns focus. Verify the exact EFT foreground HWND for every packet so
        // a transition from another application is never kept until the next
        // 50 ms timer tick.
        return _controlTrackingActive
            && _tarkovIsForeground
            && _lastForegroundWindow != nint.Zero
            && NativeMethods.GetForegroundWindow() == _lastForegroundWindow
            && !NativeMethods.IsCursorVisible();
    }

    private static HashSet<Keys> BuildTrackedKeyboardKeys(EftControlSettings settings)
    {
        var keys = new HashSet<Keys>();
        foreach (var key in new[]
        {
            settings.MoveForward,
            settings.MoveBackward,
            settings.MoveLeft,
            settings.MoveRight,
            settings.Sprint,
            settings.Walk,
            settings.Aim,
            settings.Duck,
            settings.Prone,
            settings.Jump,
        })
        {
            var normalized = key & Keys.KeyCode;
            if (IsKeyboardKey(normalized))
            {
                keys.Add(normalized);
            }
        }
        return keys;
    }

    private static bool IsKeyboardKey(Keys key)
    {
        return key is not (Keys.None
            or Keys.LButton
            or Keys.RButton
            or Keys.MButton
            or Keys.XButton1
            or Keys.XButton2);
    }

    private void SynchronizeRawKeyboardTimeline()
    {
        _rawKeyboardTimeline.Reset();
        _precisionYaw.Reset();
        if (!_rawKeyboardInputRegistered)
        {
            return;
        }

        foreach (var key in _trackedKeyboardKeys)
        {
            _rawKeyboardTimeline.Synchronize(key, IsKeyDown(key));
        }
    }

    private KeySample SampleControlKey(Keys key, long startTimestamp, long endTimestamp)
    {
        var polled = SampleKey(key);
        var normalized = key & Keys.KeyCode;
        if (!_rawKeyboardInputRegistered
            || !_trackedKeyboardKeys.Contains(normalized)
            || !_rawKeyboardTimeline.TrySample(
                normalized,
                startTimestamp,
                endTimestamp,
                out var rawSample))
        {
            return polled;
        }

        // GetAsyncKeyState remains a cheap end-state checksum. If Windows ever
        // drops a raw packet, synthesize only the missing final edge at this
        // tick; future intervals cannot stay stuck on a phantom held key.
        if (rawSample.IsDown != polled.IsDown)
        {
            _rawKeyboardTimeline.Record(normalized, polled.IsDown, endTimestamp);
            _ = _rawKeyboardTimeline.TrySample(
                normalized,
                startTimestamp,
                endTimestamp,
                out rawSample);
        }

        return new KeySample(
            rawSample.IsDown,
            rawSample.WasPressed,
            rawSample.HeldFraction,
            rawSample.WasReleased);
    }

    private static bool IsKeyDown(Keys key)
    {
        return key != Keys.None && (NativeMethods.GetAsyncKeyState((int)key) & 0x8000) != 0;
    }

    private void TrackScreenshotBindingPress(long nowTimestamp)
    {
        if (!_screenshotBinding.IsReady
            || _screenshotBinding.AxisName is not null
            || _screenshotBinding.KeyCodes.Count == 0)
        {
            _screenshotBindingWasDown = false;
            return;
        }

        var allDown = true;
        var anyPressedSinceLastPoll = false;
        foreach (var code in _screenshotBinding.KeyCodes)
        {
            if (!EftControlSettings.TryMapKeyCode(code, out var key) || key == Keys.None)
            {
                _screenshotBindingWasDown = false;
                return;
            }

            var state = NativeMethods.GetAsyncKeyState((int)key);
            allDown &= (state & 0x8000) != 0;
            anyPressedSinceLastPoll |= (state & 0x0001) != 0;
        }

        var pressed = (allDown && !_screenshotBindingWasDown)
            || (anyPressedSinceLastPoll
                && (allDown || _screenshotBinding.KeyCodes.Count == 1));
        if (_tarkovIsForeground && pressed)
        {
            _lastManualScreenshotPressAt = nowTimestamp;
        }

        _screenshotBindingWasDown = allDown;
    }

    private void RecordCurrentRealtimePose(long timestamp)
    {
        if (!_liveTrackingEnabled || _realtimeEstimator.CurrentPose is not { } pose)
        {
            return;
        }

        var newestTimestamp = _realtimePoseHistory.NewestTimestamp;
        if (newestTimestamp is null || timestamp > newestTimestamp.Value)
        {
            _realtimePoseHistory.Add(timestamp, pose);
        }
    }

    private void ResetRealtimePoseHistory(long timestamp)
    {
        _realtimePoseHistory.Reset();
        if (_liveTrackingEnabled && _realtimeEstimator.CurrentPose is { } pose)
        {
            _realtimePoseHistory.Add(timestamp, pose);
        }
    }

    private void ResetRealtimePoseHistoryAfterExactAnchor(
        ScreenshotObservation exactObservation,
        RealtimePose currentPose,
        long captureTimestamp,
        long currentTimestamp)
    {
        _realtimePoseHistory.Reset();
        if (!_liveTrackingEnabled)
        {
            return;
        }

        if (captureTimestamp > 0 && captureTimestamp < currentTimestamp)
        {
            var settings = _realtimeEstimator.Settings;
            var normalizedYaw = exactObservation.YawDegrees % 360.0;
            if (normalizedYaw < 0.0)
            {
                normalizedYaw += 360.0;
            }
            _realtimePoseHistory.Add(
                captureTimestamp,
                new RealtimePose(
                    exactObservation.X,
                    exactObservation.Y,
                    exactObservation.Z,
                    normalizedYaw,
                    settings.InitialPositionUncertaintyMeters,
                    settings.InitialYawUncertaintyDegrees));
        }

        _realtimePoseHistory.Add(currentTimestamp, currentPose);
    }

    private static KeySample SampleKey(Keys key)
    {
        if (key == Keys.None)
        {
            return default;
        }

        var state = NativeMethods.GetAsyncKeyState((int)key);
        return new KeySample(
            IsDown: (state & 0x8000) != 0,
            WasPressedSinceLastPoll: (state & 0x0001) != 0,
            HeldFraction: (state & 0x8000) != 0 ? 1.0 : 0.0,
            WasReleasedSinceLastPoll: false);
    }

    internal static bool UpdateActionState(
        bool current,
        bool isToggle,
        KeySample sample,
        bool wasDown,
        bool allowToggle = true)
    {
        if (!isToggle)
        {
            return sample.IsDown;
        }

        var pressed = sample.WasPressedSinceLastPoll || (sample.IsDown && !wasDown);
        return pressed && allowToggle ? !current : current;
    }

    internal static RealtimeMotionEvidence FilterAmbiguousStationaryEvidence(
        RealtimeMotionEvidence evidence,
        bool movementInputActive,
        bool sprint)
    {
        // At walking/slow-walking speed, distant scenery and low-resolution
        // GDI capture can look stationary even while the player is moving.
        // Sprint has enough expected visual flow for Stationary to remain a
        // useful collision/frozen-motion guard. Idle Stationary is also kept
        // for ZUPT and automatic re-anchoring.
        return movementInputActive
            && !sprint
            && evidence == RealtimeMotionEvidence.Stationary
                ? RealtimeMotionEvidence.Unknown
                : evidence;
    }

    internal static bool ShouldAccumulateCalibrationDistance(bool visuallyReanchored) =>
        !visuallyReanchored;

    internal static bool VisualAnchorsConsistent(
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
        var yaw = Math.Abs(NormalizeSigned180(
            lowResolution.YawDegrees
            - highResolution.YawDegrees));

        return horizontal <= SuperKeyframeAnchorConsistencyMeters
            && vertical <= SuperKeyframeAnchorConsistencyVerticalMeters
            && yaw <= SuperKeyframeAnchorConsistencyYawDegrees;
    }

    internal static bool IsScreenshotTimestampFromCurrentSession(
        DateTimeOffset fileTimestampUtc,
        DateTimeOffset sessionStartedAtUtc) =>
        fileTimestampUtc >= sessionStartedAtUtc - ScreenshotSessionClockTolerance;

    internal static bool ShouldScheduleVisualKeyframeLearning(
        bool isAutomaticReanchor,
        bool hasFileTimestamp,
        bool hasCaptureTimestamp) =>
        isAutomaticReanchor && hasFileTimestamp && hasCaptureTimestamp;

    private void ScheduleManualVisualKeyframeLearning(
        ScreenshotObservation observation,
        long now)
    {
        var minimumDelayTicks = (long)Math.Ceiling(
            ManualVisualKeyframeMinimumDelaySeconds * Stopwatch.Frequency);
        var maximumDelayTicks = (long)Math.Ceiling(
            ManualVisualKeyframeMaximumDelaySeconds * Stopwatch.Frequency);
        _pendingVisualKeyframeLearning = new PendingVisualKeyframeLearning(
            _currentMap,
            _raidGeneration,
            new VisualKeyframePose(
                observation.X,
                observation.Y,
                observation.Z,
                observation.YawDegrees),
            now + minimumDelayTicks,
            now + maximumDelayTicks);
    }

    private bool TryLearnPendingVisualKeyframe(
        ReadOnlySpan<byte> visualFrame,
        long now)
    {
        if (_pendingVisualKeyframeLearning is not { } pending)
        {
            return false;
        }

        if (now > pending.ExpiresAtTimestamp
            || pending.RaidGeneration != _raidGeneration
            || !string.Equals(
                pending.MapId,
                _currentMap,
                StringComparison.OrdinalIgnoreCase))
        {
            _pendingVisualKeyframeLearning = null;
            return false;
        }

        if (now < pending.EarliestAtTimestamp)
        {
            return false;
        }

        var lowResolutionLearned =
            _visualKeyframeRecognizer.AddExactKeyframe(
                pending.MapId,
                visualFrame,
                _visualMotionGate.SampleWidth,
                _visualMotionGate.SampleHeight,
                pending.Pose,
                now,
                out _);
        if (lowResolutionLearned)
        {
            _ = VisualKeyframeStore.TrySave(
                _visualKeyframeRecognizer,
                VisualKeyframeStore.GetPath(_squadProfile.MemberId));
        }

        var superLearned = false;
        if (_lastForegroundWindow != nint.Zero
            && HighResolutionVisualSampler.TryCaptureWindow(
                _lastForegroundWindow,
                out var superFrame)
            && _superKeyframeRecognizer.AddExactKeyframe(
                pending.MapId,
                superFrame,
                pending.Pose,
                now,
                out _))
        {
            superLearned = true;
            _ = SuperKeyframeStore.TrySave(
                _superKeyframeRecognizer,
                SuperKeyframeStore.GetPath(_squadProfile.MemberId));
        }

        if (!lowResolutionLearned && !superLearned)
        {
            return false;
        }

        CollectiveAtlasObserveLocalExact(
            pending.MapId,
            pending.Pose);
        _pendingVisualKeyframeLearning = null;
        _visualKeyframeRecognizer.CancelPendingMatch();
        _superKeyframeRecognizer.CancelPendingMatch();
        return true;
    }

    private readonly record struct PendingVisualKeyframeLearning(
        string MapId,
        int RaidGeneration,
        VisualKeyframePose Pose,
        long EarliestAtTimestamp,
        long ExpiresAtTimestamp);

    internal readonly record struct KeySample(
        bool IsDown,
        bool WasPressedSinceLastPoll,
        double HeldFraction = 0.0,
        bool WasReleasedSinceLastPoll = false);

    private async Task InitializeWebViewAsync()
    {
        try
        {
            // Keep the WebView profile beside the executable. This makes the portable
            // build work in restricted environments without writing to AppData.
            var userDataFolder = ResolveWritableUserDataFolder();
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await _webView.EnsureCoreWebView2Async(environment);
            ApplyMapScaleToWebView();

            var core = _webView.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping(
                OfflineMapHost,
                AppContext.BaseDirectory,
                CoreWebView2HostResourceAccessKind.Allow);
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.WebMessageReceived += OnMapZoomPreferenceMessage;
            core.NavigationStarting += OnNavigationStarting;
            core.NavigationCompleted += OnNavigationCompleted;
            core.SourceChanged += OnSourceChanged;
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, args) => args.Cancel = true;

            // The overlay is a dedicated kiosk view. Remembering the site's
            // standard first-party cookie choice prevents its consent panel from
            // covering half of the tiny map every time the portable profile moves.
            var consentCookie = core.CookieManager.CreateCookie(
                "CookieConsent",
                "true",
                "tarkov.dev",
                "/");
            consentCookie.Expires = DateTime.Now.AddYears(5);
            consentCookie.IsSecure = true;
            consentCookie.SameSite = CoreWebView2CookieSameSiteKind.Lax;
            core.CookieManager.AddOrUpdateCookie(consentCookie);

            var cssPath = Path.Combine(AppContext.BaseDirectory, "InjectedStyles.css");
            var behaviorPath = Path.Combine(AppContext.BaseDirectory, "InjectedBehavior.js");
            var css = await File.ReadAllTextAsync(cssPath, _formLifetime.Token);
            var behavior = await File.ReadAllTextAsync(behaviorPath, _formLifetime.Token);
            var floorModule = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "AutomaticFloors.js"), _formLifetime.Token);
            var serializedCss = JsonSerializer.Serialize(css);
            var injectionScript = $$"""
                {{floorModule}}
                {{behavior}}
                (() => {
                    const install = () => {
                        if (document.getElementById('raidpilot-local-style')) return;
                        const style = document.createElement('style');
                        style.id = 'raidpilot-local-style';
                        style.textContent = {{serializedCss}};
                        (document.head || document.documentElement).appendChild(style);
                    };
                    install();
                    document.addEventListener('DOMContentLoaded', install, { once: true });
                })();
                """;
            // User-selected zoom survives WebView profile and Launcher replacement.
            // The local renderer reads this before its initial image load.
            await core.AddScriptToExecuteOnDocumentCreatedAsync(
                "if (location.protocol === 'https:' && "
                + "['raidpilot-map.local','raidpilot.local','tarkov.dev'].includes(location.hostname)) { "
                + "window.__raidPilotHostZoomFactor = "
                + JsonSerializer.Serialize(_preferredMapZoomFactor) + "; }");
            await core.AddScriptToExecuteOnDocumentCreatedAsync("if (location.hostname !== 'raidpilot-map.local') {\n" + (injectionScript) + "\n}");
            // Map detection starts before WebView2 initialization. If EFT has
            // already announced the raid, skip the default Customs navigation
            // entirely instead of loading two maps back-to-back.
            var initialMap = _pendingDetectedMap;
            var remoteInitialUri =
                initialMap?.Url ?? new Uri(DefaultMapUrl);
            _offlineMapMode = await ShouldPreferOfflineMapAsync(
                remoteInitialUri,
                _formLifetime.Token);
            var initialUrl = _offlineMapMode
                ? BuildOfflineMapUri(
                    initialMap?.NormalizedName ?? "customs")
                    .AbsoluteUri
                : remoteInitialUri.AbsoluteUri;
            if (_offlineMapMode)
            {
                SetStatus(
                    "tarkov.dev bloqué · carte locale activée",
                    Color.FromArgb(217, 170, 78));
            }
            await _localMapHost.AttachAsync(_webView);
            BeginWebNavigation(core, initialUrl);
        }
        catch (OperationCanceledException) when (_formLifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SetStatus("Carte indisponible", Color.FromArgb(214, 93, 84));
            MessageBox.Show(
                $"Impossible d'initialiser WebView2.\n\n{exception.Message}",
                "RaidPilot",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (!WebNavigationPolicy.TryGetAllowedMapUri(args.Uri, out var uri)
            || uri is null)
        {
            args.Cancel = true;
            return;
        }

        _navigationSequence++;
        _navigationInProgress = true;
        _navigationStartPending = false;
        _activeNavigationId = args.NavigationId;
        _activeNavigationUri = uri;
        _webReady = false;
        _renderedLearnedRouteSignature = null;
        _clearedLearnedRouteGeneration = -1;

        if (_navigationWaiter is not null
            && _navigationWaiterUri is not null
            && Uri.Compare(
                uri,
                _navigationWaiterUri,
                UriComponents.SchemeAndServer | UriComponents.PathAndQuery,
                UriFormat.SafeUnescaped,
                StringComparison.OrdinalIgnoreCase) == 0)
        {
            _navigationWaiterId = args.NavigationId;
        }
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (_navigationWaiter is not null && _navigationWaiterId == args.NavigationId)
        {
            _navigationWaiter.TrySetResult(args.IsSuccess);
            _navigationWaiter = null;
            _navigationWaiterId = null;
            _navigationWaiterUri = null;
        }

        if (!WebNavigationPolicy.IsActiveCompletion(
                args.NavigationId,
                _activeNavigationId,
                _navigationStartPending))
        {
            return;
        }

        if (await TryFallbackToOfflineMapAsync(args))
        {
            return;
        }

        var completedNavigationSequence = _navigationSequence;
        _activeNavigationId = null;
        _activeNavigationUri = null;
        _navigationInProgress = false;
        _webReady = args.IsSuccess;

        if (!args.IsSuccess)
        {
            if (_pendingDetectedMap is not null)
            {
                SetStatus("Nouvelle tentative de carte…", Color.FromArgb(217, 170, 78));
                _ = RetryPendingDetectedMapAsync(_pendingDetectedMap);
            }
            else
            {
                SetStatus("Erreur de chargement", Color.FromArgb(214, 93, 84));
            }
            return;
        }

        UpdateCurrentMapFromSource();
        EnsureMapLogWatcherStarted();
        if (TryApplyPendingDetectedMap())
        {
            return;
        }
        SetStatus(_gpsEnabled ? "GPS actif" : "Carte prête");
        try
        {
            if (_raidQuestGameMode is { } gameMode)
            {
                var reloadingForGameMode = await EnsureWebMapGameModeAsync(gameMode, _raidGeneration);
                if (!IsNavigationContinuationCurrent(completedNavigationSequence)
                    || reloadingForGameMode)
                {
                    return;
                }
            }

            await Task.Delay(250, _formLifetime.Token);
            if (!IsNavigationContinuationCurrent(completedNavigationSequence))
            {
                return;
            }

            var core = _webView.CoreWebView2;
            if (core is null
                || !await EnsurePageAssetsInjectedAsync(core, completedNavigationSequence))
            {
                return;
            }

            // Refresh the preference from AppData: the first document-created
            // injection is immutable, while players can change zoom mid-session.
            await core.ExecuteScriptAsync(
                "window.__raidPilotHostZoomFactor = "
                + JsonSerializer.Serialize(_preferredMapZoomFactor)
                + "; window.dispatchEvent(new Event('raidpilot-zoom-sync')); void 0;");
            if (!IsNavigationContinuationCurrent(completedNavigationSequence))
                return;

            await ApplyWebInteractionStateAsync();
            if (!IsNavigationContinuationCurrent(completedNavigationSequence))
            {
                return;
            }

            await core.ExecuteScriptAsync("window.dispatchEvent(new Event('resize')); void 0;");
            if (!IsNavigationContinuationCurrent(completedNavigationSequence))
            {
                return;
            }

            await RestoreLocalPlayerMarkerAsync();
            if (!IsNavigationContinuationCurrent(completedNavigationSequence))
            {
                return;
            }

            await RestoreMineHazardOverlayAsync();
            if (!IsNavigationContinuationCurrent(completedNavigationSequence))
            {
                return;
            }

            await RestoreBoundaryOverlayAsync();
            if (!IsNavigationContinuationCurrent(completedNavigationSequence))
            {
                return;
            }

            await RestoreSquadMarkersAsync();
            if (!IsNavigationContinuationCurrent(completedNavigationSequence))
            {
                return;
            }

            await RestoreMapReceiverAfterNavigationAsync(completedNavigationSequence);
            if (!IsNavigationContinuationCurrent(completedNavigationSequence))
            {
                return;
            }

            await RestoreRaidCountdownAsync();
            if (!IsNavigationContinuationCurrent(completedNavigationSequence))
            {
                return;
            }

            await RestoreRaidExtractionMarkersAsync();
            if (!IsNavigationContinuationCurrent(completedNavigationSequence))
            {
                return;
            }

            await RestoreRaidQuestMarkersAsync();
            await FlushQuestLogEventsAsync();
            if (!IsNavigationContinuationCurrent(completedNavigationSequence))
            {
                return;
            }

            if (_lastObservation is { } observation)
            {
                await UpdateExtractionGuidanceAsync(observation);
            }
        }
        catch (OperationCanceledException) when (_formLifetime.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (!_formLifetime.IsCancellationRequested
                && IsNavigationContinuationCurrent(completedNavigationSequence))
            {
                SetStatus(_gpsEnabled ? "GPS actif" : "Carte prête");
            }
        }
    }

    private bool IsNavigationContinuationCurrent(long navigationSequence)
    {
        return !_formLifetime.IsCancellationRequested
            && WebNavigationPolicy.CanContinueAfterAwait(
                navigationSequence,
                _navigationSequence,
                _navigationInProgress);
    }

    private async Task<bool> EnsurePageAssetsInjectedAsync(
        CoreWebView2 core,
        long navigationSequence)
    {
        if (RaidPilotLocalMapHost.IsLocalMapUri(_webView.Source)) return true;

        try
        {
            var installed = await core.ExecuteScriptAsync(
                "Boolean(window.__raidPilotMapFilterInstalled === true "
                + "&& document.getElementById('raidpilot-local-style'));");
            if (!IsNavigationContinuationCurrent(navigationSequence))
            {
                return false;
            }

            if (string.Equals(installed, "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        catch when (_formLifetime.IsCancellationRequested)
        {
            return false;
        }
        catch
        {
            // A failed probe is treated as a missing document-created injection.
        }

        if (!IsNavigationContinuationCurrent(navigationSequence))
        {
            return false;
        }

        // AddScriptToExecuteOnDocumentCreated is the normal path. Disk I/O and
        // the larger script execution happen only if that early injection is absent.
        var cssPath = Path.Combine(AppContext.BaseDirectory, "InjectedStyles.css");
        var behaviorPath = Path.Combine(AppContext.BaseDirectory, "InjectedBehavior.js");
        var cssTask = File.ReadAllTextAsync(cssPath, _formLifetime.Token);
        var behaviorTask = File.ReadAllTextAsync(behaviorPath, _formLifetime.Token);
        await Task.WhenAll(cssTask, behaviorTask);
        if (!IsNavigationContinuationCurrent(navigationSequence))
        {
            return false;
        }

        var serializedCss = JsonSerializer.Serialize(await cssTask);
        await core.ExecuteScriptAsync($$"""
            (() => {
                let style = document.getElementById('raidpilot-local-style');
                if (!style) {
                    style = document.createElement('style');
                    style.id = 'raidpilot-local-style';
                    (document.head || document.documentElement).appendChild(style);
                }
                style.textContent = {{serializedCss}};
            })();
            """);
        if (!IsNavigationContinuationCurrent(navigationSequence))
        {
            return false;
        }

        await core.ExecuteScriptAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "AutomaticFloors.js"), _formLifetime.Token));
        await core.ExecuteScriptAsync(await behaviorTask);
        return IsNavigationContinuationCurrent(navigationSequence);
    }

    private void OnSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs args)
    {
        if (_webReady && !_navigationInProgress)
        {
            UpdateCurrentMapFromSource();
        }
    }

    private async void OnGpsButtonClick(object? sender, EventArgs e)
    {
        if (_gpsTransitionInProgress)
        {
            return;
        }

        _gpsTransitionInProgress = true;
        _gpsMenuItem.Enabled = false;
        try
        {
            await ToggleGpsAsync();
        }
        finally
        {
            _gpsTransitionInProgress = false;
            _gpsMenuItem.Enabled = true;
        }
    }

    private async Task ToggleGpsAsync()
    {
        if (_gpsEnabled)
        {
            await DisableGpsAsync();
            return;
        }

        if (!_webReady || _webView.CoreWebView2 is null)
        {
            MessageBox.Show(
                "Attendez que la carte tarkov.dev soit chargée.",
                "RaidPilot",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var consent = MessageBox.Show(
            "Afficher aussi ton point sur la carte tarkov.dev ?\n\n"
            + "La lecture locale des captures, la flèche et le clignotement des sorties fonctionnent déjà sans cette option. "
            + "Si tu acceptes, seuls la carte, x/y/z et la rotation mesurés ou estimés seront envoyés à wss://socket.tarkov.dev afin d'afficher ton point. "
            + "Les captures, les lignes OCR, les touches, la souris et les lignes du journal restent sur ce PC. "
            + "RaidPilot ne lit pas la mémoire ou le trafic réseau du jeu et ne simule aucune touche.",
            "Point tarkov.dev — RaidPilot",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (consent != DialogResult.Yes)
        {
            return;
        }

        _gpsMenuItem.Enabled = false;
        SetStatus("Connexion GPS…", Color.FromArgb(217, 170, 78));

        try
        {
            await _webView.CoreWebView2.ExecuteScriptAsync(
                "sessionStorage.setItem('__raidpilotGpsEnabled', '1'); void 0;");
            var generatedRemoteId = GenerateRemoteId();
            var remoteId = await ConnectMapReceiverAsync(generatedRemoteId, _formLifetime.Token);
            var client = new TarkovDevGpsClient(remoteId);
            try
            {
                using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(_formLifetime.Token);
                connectTimeout.CancelAfter(TimeSpan.FromSeconds(12));
                try
                {
                    await client.ConnectAsync(connectTimeout.Token);
                }
                catch (OperationCanceledException) when (!_formLifetime.IsCancellationRequested)
                {
                    throw new TimeoutException("Le service GPS tarkov.dev n'a pas répondu sous 12 secondes.");
                }

                _gpsClient = client;
                _gpsEnabled = true;
                _gpsMenuItem.Text = "Masquer mon point tarkov.dev (Ctrl+Alt+G)";
                RefreshPositionKeyMenu();
                SetStatus(
                    _screenshotBinding.IsReady
                        ? $"Point actif · suivi local · {_screenshotBinding.UserLabel}"
                        : "Point actif · touche à configurer",
                    Color.FromArgb(88, 168, 138));
                if (_lastObservation is { } observation)
                {
                    await SendObservationAsync(observation);
                }
            }
            catch
            {
                await client.DisposeAsync();
                throw;
            }
        }
        catch (OperationCanceledException) when (_formLifetime.IsCancellationRequested)
        {
            await ClearMapReceiverAsync(reload: false);
        }
        catch (Exception exception)
        {
            await ClearMapReceiverAsync(reload: true);
            SetStatus("GPS non connecté", Color.FromArgb(214, 93, 84));
            MessageBox.Show(
                $"La connexion GPS n'a pas pu être activée.\n\n{exception.Message}",
                "RaidPilot",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            _gpsMenuItem.Enabled = true;
        }
    }

    private async Task<string> ConnectMapReceiverAsync(string generatedRemoteId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var mapUrl = GetCurrentMapUrl();
            var connectionUrl = $"{mapUrl}?connection={Uri.EscapeDataString(generatedRemoteId)}";
            var navigationWaiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _navigationWaiter = navigationWaiter;
            _navigationWaiterId = null;
            _navigationWaiterUri = new Uri(connectionUrl, UriKind.Absolute);
            BeginWebNavigation(_webView.CoreWebView2, connectionUrl);

            bool navigationResult;
            try
            {
                navigationResult = await navigationWaiter.Task.WaitAsync(
                    TimeSpan.FromSeconds(15),
                    cancellationToken);
            }
            catch (TimeoutException)
            {
                if (ReferenceEquals(_navigationWaiter, navigationWaiter))
                {
                    _navigationWaiter = null;
                    _navigationWaiterId = null;
                    _navigationWaiterUri = null;
                }
                continue;
            }
            if (!navigationResult)
            {
                continue;
            }

            for (var poll = 0; poll < 20; poll++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var state = await ReadRemoteStateAsync();
                if (string.Equals(state.Id, generatedRemoteId, StringComparison.Ordinal)
                    && string.Equals(state.VisibleStatus, generatedRemoteId, StringComparison.Ordinal))
                {
                    await _webView.CoreWebView2.ExecuteScriptAsync(
                        "history.replaceState(null, '', location.pathname + location.hash); void 0;");
                    return state.Id!;
                }

                await Task.Delay(250, cancellationToken);
            }
        }

        throw new InvalidOperationException("tarkov.dev n'a pas confirmé la session distante.");
    }

    private string GetCurrentMapUrl()
    {
        var source = _webView.Source;
        if (source is null
            || source.Scheme != Uri.UriSchemeHttps
            || !string.Equals(source.Host, "tarkov.dev", StringComparison.OrdinalIgnoreCase)
            || !source.AbsolutePath.StartsWith("/map/", StringComparison.OrdinalIgnoreCase))
        {
            return DefaultMapUrl;
        }

        return source.GetLeftPart(UriPartial.Path);
    }

    private async Task<(string? Id, string? VisibleStatus)> ReadRemoteStateAsync()
    {
        const string script = """
            (() => {
                let id = null;
                try { id = JSON.parse(localStorage.getItem('sessionId')); } catch (_) {}
                const visibleStatus = document.querySelector('.session-id')?.textContent?.trim() ?? null;
                return { id, visibleStatus };
            })();
            """;
        var raw = await _webView.CoreWebView2.ExecuteScriptAsync(script);
        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;
        var id = root.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
            ? idElement.GetString()
            : null;
        var visibleStatus = root.TryGetProperty("visibleStatus", out var statusElement)
            && statusElement.ValueKind == JsonValueKind.String
            ? statusElement.GetString()
            : null;
        return (id, visibleStatus);
    }

    private async Task RestoreMapReceiverAfterNavigationAsync(long navigationSequence)
    {
        if (!_gpsEnabled
            || _webView.CoreWebView2 is null
            || !IsNavigationContinuationCurrent(navigationSequence))
        {
            return;
        }

        for (var poll = 0; poll < 20; poll++)
        {
            _formLifetime.Token.ThrowIfCancellationRequested();
            var state = await ReadRemoteStateAsync();
            if (!IsNavigationContinuationCurrent(navigationSequence))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(state.Id)
                && string.Equals(state.Id, state.VisibleStatus, StringComparison.Ordinal))
            {
                await _webView.CoreWebView2.ExecuteScriptAsync(
                    "history.replaceState(null, '', location.pathname + location.hash); void 0;");
                if (!IsNavigationContinuationCurrent(navigationSequence))
                {
                    return;
                }

                if (_lastObservation is { } observation)
                {
                    await SendObservationAsync(observation);
                }
                return;
            }

            if (poll < 19)
            {
                await Task.Delay(100, _formLifetime.Token);
                if (!IsNavigationContinuationCurrent(navigationSequence))
                {
                    return;
                }
            }
        }
    }

    private static string GenerateRemoteId()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var value = new char[16];
        for (var index = 0; index < value.Length; index++)
        {
            value[index] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }
        return new string(value);
    }

    private async Task DisableGpsAsync()
    {
        _gpsEnabled = false;
        _pendingLivePosition = null;
        _gpsMenuItem.Enabled = false;

        var client = _gpsClient;
        _gpsClient = null;
        if (client is not null)
        {
            await client.DisposeAsync();
        }

        _gpsMenuItem.Text = "Afficher mon point sur tarkov.dev (Ctrl+Alt+G)…";
        _gpsMenuItem.Enabled = true;
        await ClearMapReceiverAsync(reload: !_formLifetime.IsCancellationRequested);
        SetStatus("Point distant masqué · guidage local actif");
    }

    private async Task ClearMapReceiverAsync(bool reload)
    {
        var core = _webView.CoreWebView2;
        if (core is null)
        {
            return;
        }

        try
        {
            await core.ExecuteScriptAsync(
                "sessionStorage.removeItem('__raidpilotGpsEnabled'); localStorage.removeItem('sessionId'); history.replaceState(null, '', location.pathname + location.hash); void 0;");
            if (reload)
            {
                BeginWebNavigation(core, GetCurrentMapUrl());
            }
        }
        catch
        {
            // Renderer shutdown or navigation teardown: process exit clears access.
        }
    }

    private void OnWatcherStatusChanged(object? sender, string status)
    {
        RunOnUiThread(() =>
        {
            if (ReferenceEquals(sender, _screenshotWatcher))
            {
                SetStatus(status, Color.FromArgb(88, 168, 138));
            }
        });
    }

    private void OnObservationCreated(object? sender, ScreenshotObservation observation)
    {
        RunOnUiThread(() =>
        {
            if (!ReferenceEquals(sender, _screenshotWatcher))
            {
                return;
            }

            var now = Stopwatch.GetTimestamp();
            TrackScreenshotBindingPress(now);
            RecordCurrentRealtimePose(now);
            var hasFileTimestamp = TryGetScreenshotTimestamp(
                observation.FileName,
                out var screenshotPath,
                out var fileTimestampUtc);
            var automaticRequest = _automaticReanchorRequest;
            var requestAlreadyHadCandidate = automaticRequest?.CandidateFileName is not null;
            var matchesAutomaticRequest = automaticRequest is not null
                && hasFileTimestamp
                && automaticRequest.TryRegisterCandidate(
                    observation.FileName,
                    fileTimestampUtc,
                    _raidGeneration,
                    now);
            var isAutomaticReanchor = matchesAutomaticRequest && !requestAlreadyHadCandidate;
            if (isAutomaticReanchor && automaticRequest is { RequestedWhileMoving: true })
                _mobileExactAnchorConfirmed++;
            if (matchesAutomaticRequest && !automaticRequest!.CleanupScheduled)
            {
                automaticRequest.CleanupScheduled = true;
                _ = FinalizeAutomaticScreenshotAsync(automaticRequest);
            }

            if (!matchesAutomaticRequest
                && hasFileTimestamp
                && !IsScreenshotTimestampFromCurrentSession(
                    fileTimestampUtc,
                    _raidSessionStartedAtUtc))
            {
                // Une notification tardive de l'ancien raid ne doit ni ancrer
                // le joueur sur la nouvelle carte, ni y enregistrer un repÃƒÂ¨re.
                return;
            }

            RealtimePose? estimatedPoseAtCapture = null;
            var captureTimestamp = _replayingRaidStartObservation
                ? _raidStartReplayCaptureTimestamp
                : 0;
            var hasCaptureTimestamp = captureTimestamp > 0
                || TryResolveScreenshotCaptureTimestamp(
                    automaticRequest,
                    isAutomaticReanchor,
                    hasFileTimestamp,
                    fileTimestampUtc,
                    now,
                    out captureTimestamp);
            if (!_replayingRaidStartObservation
                && !matchesAutomaticRequest
                && hasFileTimestamp)
            {
                // The screenshot event can beat the EFT map/session line at the
                // very beginning of a raid. Do not apply that exact pose, OCR,
                // calibration, graph point or GPS packet to the previous raid.
                // Give the log watcher one reconciliation interval to claim it;
                // otherwise commit it once to the current stable session.
                var bufferedSequence = _raidStartObservationBuffer.Remember(
                    observation,
                    fileTimestampUtc,
                    hasCaptureTimestamp ? captureTimestamp : now,
                    _raidGeneration,
                    now);
                if (bufferedSequence != 0)
                {
                    _ = CommitBufferedObservationAfterMapGracePeriodAsync(
                        (ScreenshotFolderWatcher)sender!,
                        bufferedSequence,
                        _raidGeneration);
                    return;
                }
            }
            if (hasCaptureTimestamp
                && _realtimePoseHistory.TryGetPoseAt(captureTimestamp, out var historicalPose))
            {
                estimatedPoseAtCapture = historicalPose;
            }

            var postCaptureIntervalContaminated = isAutomaticReanchor
                && automaticRequest!.MovementOccurred;
            if (estimatedPoseAtCapture is { } capturePose
                && _realtimeEstimator.CurrentPose is { } currentPose)
            {
                var postCaptureDistance = Math.Sqrt(
                    ((currentPose.X - capturePose.X) * (currentPose.X - capturePose.X))
                    + ((currentPose.Z - capturePose.Z) * (currentPose.Z - capturePose.Z)));
                var postCaptureYaw = Math.Abs(NormalizeSigned180(
                    currentPose.YawDegrees - capturePose.YawDegrees));
                postCaptureIntervalContaminated |=
                    postCaptureDistance > CalibrationPostCaptureMovementToleranceMeters
                    || postCaptureYaw > CalibrationPostCaptureYawToleranceDegrees;
            }

            // Les compteurs sont remis ÃƒÂ  zÃƒÂ©ro ÃƒÂ  la dÃƒÂ©tection du fichier, alors
            // que l'observation exacte appartient ÃƒÂ  l'instant de capture. Si un
            // trajet significatif sÃƒÂ©pare les deux, ni ce segment ni le suivant
            // ne doit apprendre une calibration avec deux intervalles dÃƒÂ©calÃƒÂ©s.
            var allowCalibration = !postCaptureIntervalContaminated
                && !_skipNextRealtimeCalibration
                && !_raidStartReplayCrossedTransition;
            if (estimatedPoseAtCapture is { } beforeCorrection)
                _precisionResiduals.Observe(beforeCorrection, observation);
            _precisionV2DriftAudit.ObserveExactAnchor(
                observation, estimatedPoseAtCapture,
                hasCaptureTimestamp ? captureTimestamp : 0,
                _precisionV2ToggleMenuItem.Checked,
                _precisionV4Menu.Checked);
            CalibrateRealtimeEstimator(observation, estimatedPoseAtCapture, allowCalibration);
            _skipNextRealtimeCalibration = postCaptureIntervalContaminated;
            _lastExactObservation = observation;
            _lastExactAnchorAt = now;
            _visualRelocalization.NoteAbsoluteAnchor(now);
            ResetPrecisionV2();
            MarkCollectiveAtlasLocallyValidated(
                _currentMap,
                new VisualKeyframePose(
                    observation.X,
                    observation.Y,
                    observation.Z,
                    observation.YawDegrees));
            if (ShouldScheduleVisualKeyframeLearning(
                    isAutomaticReanchor,
                    hasFileTimestamp,
                    hasCaptureTimestamp)
                && automaticRequest is { MovementOccurred: false })
            {
                // Une capture automatique dÃƒÂ©clenchÃƒÂ©e au repos est dÃƒÂ©jÃƒÂ  une vue
                // de jeu propre: elle peut ÃƒÂªtre transformÃƒÂ©e immÃƒÂ©diatement en
                // signature visuelle exacte.
                _ = LearnAutomaticVisualKeyframeAsync(
                    automaticRequest,
                    screenshotPath,
                    _currentMap,
                    _raidGeneration,
                    observation,
                    captureTimestamp);
            }
            else if (!isAutomaticReanchor
                && _liveTrackingEnabled
                && hasFileTimestamp
                && hasCaptureTimestamp
                && estimatedPoseAtCapture is not null
                && !postCaptureIntervalContaminated)
            {
                // Un F8 manuel peut afficher O ou TÃƒâ€šCHES. Ne jamais apprendre
                // le PNG lui-mÃƒÂªme. Attendre que l'ÃƒÂ©cran de jeu soit revenu,
                // exiger une scÃƒÂ¨ne vivante puis stationnaire, et annuler au
                // moindre dÃƒÂ©placement/virage pour conserver exactement la pose F8.
                ScheduleManualVisualKeyframeLearning(observation, now);
            }
            else if (!isAutomaticReanchor)
            {
                _pendingVisualKeyframeLearning = null;
            }
            _idleSeconds = 0.0;
            _guidanceGeneration = unchecked(_guidanceGeneration + 1);
            InvalidateLearnedRoutePlan();
            _learnedRouteGraph.BreakContinuity();
            _lastLearnedRouteObservationAt = 0;
            var effectiveObservation = observation;
            if (_liveTrackingEnabled)
            {
                RealtimePose anchoredPose;
                if (estimatedPoseAtCapture is { } historicalAnchorPose
                    && _realtimeEstimator.CurrentPose is not null)
                {
                    // La capture dÃƒÂ©crit l'ÃƒÂ©tat passÃƒÂ©. Corriger cet instant puis
                    // conserver le trajet dÃƒÂ©jÃƒÂ  parcouru ÃƒÂ©vite le saut arriÃƒÂ¨re
                    // perceptible lorsque le PNG arrive quelques trames plus tard.
                    anchoredPose = _realtimeEstimator.ReanchorFromHistoricalPose(
                        observation,
                        historicalAnchorPose);
                }
                else
                {
                    anchoredPose = _realtimeEstimator.Anchor(observation);
                    Interlocked.Exchange(ref _pendingRawMouseX, 0);
        Interlocked.Exchange(ref _precisionYawActivity, 0);
                }

                _lastRealtimeTick = now;
                _lastLiveSocketUpdate = _lastRealtimeTick;
                ResetRealtimePoseHistoryAfterExactAnchor(
                    observation,
                    anchoredPose,
                    hasCaptureTimestamp ? captureTimestamp : 0,
                    now);
                effectiveObservation = CreateLiveObservation(anchoredPose);
            }
            _lastObservation = effectiveObservation;
            RecordAutomaticFloorAnchor(observation, hasCaptureTimestamp ? captureTimestamp : now, "native");
            var learnedPose = _realtimeEstimator.CurrentPose ?? new RealtimePose(
                effectiveObservation.X,
                effectiveObservation.Y,
                effectiveObservation.Z,
                effectiveObservation.YawDegrees,
                0.0,
                0.0);
            UpdateMineProximity(learnedPose);
            UpdateBoundaryProximity(learnedPose);
            var learnedSource = !_liveTrackingEnabled || estimatedPoseAtCapture is null
                ? LearnedPoseSource.ExactAnchor
                : LearnedPoseSource.LiveEstimate;
            ObserveLearnedRoutePose(
                learnedPose,
                learnedSource,
                learnedPose.PositionUncertaintyMeters,
                now,
                force: true);
            MaybeSaveLearnedRouteGraph(now);
            // Drop a coalesced pre-anchor live pose. Otherwise the in-flight
            // sender could publish that stale estimate immediately after this
            // exact F8 packet and make the marker jump backwards for one tick.
            _pendingLivePosition = null;
            _ = UpdateLocalPlayerMarkerAsync(
                effectiveObservation,
                learnedPose.PositionUncertaintyMeters,
                snap: true);
            MaybeSendSquadPose(
                effectiveObservation,
                learnedPose.PositionUncertaintyMeters,
                force: true);
            _ = SendObservationAsync(effectiveObservation);
            _ = UpdateExtractionGuidanceAsync(effectiveObservation);
            if (!isAutomaticReanchor)
            {
                TryStartExtractionScan(
                    observation,
                    hasCaptureTimestamp ? captureTimestamp : now);
            }
        });
    }

    private async Task CommitBufferedObservationAfterMapGracePeriodAsync(
        ScreenshotFolderWatcher watcher,
        long sequence,
        int raidGeneration)
    {
        try
        {
            await Task.Delay(RaidStartMapDetectionGracePeriod, _formLifetime.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_formLifetime.IsCancellationRequested)
        {
            return;
        }

        RunOnUiThread(() =>
        {
            if (!ReferenceEquals(watcher, _screenshotWatcher)
                || !_raidStartObservationBuffer.TryTakeForStableSession(
                    sequence,
                    raidGeneration,
                    out var buffered))
            {
                return;
            }

            ReplayBufferedObservation(watcher, buffered, crossedRaidTransition: false);
        });
    }

    private void ReplayBufferedObservation(
        ScreenshotFolderWatcher watcher,
        RaidStartObservation buffered,
        bool crossedRaidTransition)
    {
        var previousReplayState = _replayingRaidStartObservation;
        var previousTransitionState = _raidStartReplayCrossedTransition;
        var previousCaptureTimestamp = _raidStartReplayCaptureTimestamp;
        _replayingRaidStartObservation = true;
        _raidStartReplayCrossedTransition = crossedRaidTransition;
        _raidStartReplayCaptureTimestamp = buffered.CaptureTimestamp;
        try
        {
            OnObservationCreated(watcher, buffered.Observation);
        }
        finally
        {
            _raidStartReplayCaptureTimestamp = previousCaptureTimestamp;
            _raidStartReplayCrossedTransition = previousTransitionState;
            _replayingRaidStartObservation = previousReplayState;
        }
    }

    private async Task LearnAutomaticVisualKeyframeAsync(
        AutomaticReanchorRequest request,
        string screenshotPath,
        string mapId,
        int raidGeneration,
        ScreenshotObservation observation,
        long captureTimestamp)
    {
        try
        {
            var lowResolutionTask =
                _visualMotionGate.CreateLuminanceFrameFromImageWithRetryAsync(
                    screenshotPath,
                    _formLifetime.Token);
            var highResolutionTask =
                HighResolutionVisualSampler.CreateLuminanceFrameFromImageWithRetryAsync(
                    screenshotPath,
                    _formLifetime.Token);
            await Task.WhenAll(lowResolutionTask, highResolutionTask);

            if (_formLifetime.IsCancellationRequested
                || request.IsAmbiguous
                || raidGeneration != _raidGeneration
                || !string.Equals(
                    mapId,
                    _currentMap,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var exactPose = new VisualKeyframePose(
                observation.X,
                observation.Y,
                observation.Z,
                observation.YawDegrees);

            var frame = await lowResolutionTask;
            if (frame is not null
                && _visualKeyframeRecognizer.AddExactKeyframe(
                    mapId,
                    frame,
                    _visualMotionGate.SampleWidth,
                    _visualMotionGate.SampleHeight,
                    exactPose,
                    captureTimestamp,
                    out _))
            {
                _ = VisualKeyframeStore.TrySave(
                    _visualKeyframeRecognizer,
                    VisualKeyframeStore.GetPath(_squadProfile.MemberId));
            }

            var superFrame = await highResolutionTask;
            if (superFrame is not null
                && _superKeyframeRecognizer.AddExactKeyframe(
                    mapId,
                    superFrame,
                    exactPose,
                    captureTimestamp,
                    out _))
            {
                _ = SuperKeyframeStore.TrySave(
                    _superKeyframeRecognizer,
                    SuperKeyframeStore.GetPath(_squadProfile.MemberId));
            }
        }
        catch (OperationCanceledException) when (_formLifetime.IsCancellationRequested)
        {
        }
    }

    private void TryStartExtractionScan(
        ScreenshotObservation observation,
        long captureTimestamp)
    {
        var watcher = _screenshotWatcher;
        if (watcher is null)
        {
            return;
        }

        var screenshotsRoot = Path.GetFullPath(watcher.ScreenshotsPath);
        var fullPath = Path.GetFullPath(Path.Combine(screenshotsRoot, observation.FileName));
        if (!string.Equals(
                Path.GetDirectoryName(fullPath),
                screenshotsRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var raidTimerSnapshot = _raidCountdown.GetSnapshot(captureTimestamp);
        var raidTimerGeneration = raidTimerSnapshot.IsActive
            ? raidTimerSnapshot.Generation
            : _raidCountdown.StartSession();
        if (_extractionScanInProgress)
        {
            _pendingExtractionScan = (
                fullPath,
                _raidGeneration,
                _currentMap,
                captureTimestamp,
                raidTimerGeneration);
            return;
        }

        _ = RecognizeRaidExtractionsAsync(
            fullPath,
            _raidGeneration,
            _currentMap,
            captureTimestamp,
            raidTimerGeneration);
    }

    private async Task RecognizeRaidExtractionsAsync(
        string screenshotPath,
        int raidGeneration,
        string mapName,
        long captureTimestamp,
        int raidTimerGeneration)
    {
        if (_extractionScanInProgress)
        {
            return;
        }

        _extractionScanInProgress = true;
        using var scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(_formLifetime.Token);
        _extractionScanCancellation = scanCancellation;
        try
        {
            // Read the user-supplied panel again even after EXFIL is complete.
            // A previous successful extraction read is NOT evidence that the clock synchronized.
            var lines = await _screenshotTextReader.ReadExtractionPanelLinesAsync(
                screenshotPath, scanCancellation.Token);
            if (scanCancellation.IsCancellationRequested || raidGeneration != _raidGeneration
                || !string.Equals(mapName, _currentMap, StringComparison.OrdinalIgnoreCase)) return;

            var plan = RaidPanelReadPlan.FromLines(lines, _extractionPanelComplete);
            if (plan.ReadTimer)
            {
                // Independent from catalog fetches, connectivity and extraction completeness.
                // The existing shared OCR gate still serializes the work; no new screenshot is requested.
                _ = RecognizeRaidTimerAsync(screenshotPath, raidGeneration, mapName,
                    captureTimestamp, raidTimerGeneration);
            }
            if (plan.ReadQuests)
            {
                TryStartQuestScan(screenshotPath, raidGeneration, mapName);
                return;
            }
            if (!plan.UpdateExtractions) return;

            var compatible = (await GetCurrentExtractionCandidatesAsync(scanCancellation.Token))
                .Where(IsExtractionCompatibleWithCurrentSide).ToArray();
            if (scanCancellation.IsCancellationRequested || raidGeneration != _raidGeneration
                || !string.Equals(mapName, _currentMap, StringComparison.OrdinalIgnoreCase)) return;
            var panel = ExtractionAdvisor.ResolveOcrExtractionPanel(compatible, lines);
            if (panel.SlotCount < 2)
            {
                SetStatus("Sorties non lues · O deux fois puis F8", Color.FromArgb(217, 170, 78));
                return;
            }
            // ResolveOcrExtractionPanel ne conserve que les rangs EXFIL valides;
            // les lignes TRANSIT et EXFIL?? restent donc exclues ici. Garder
            // toutes les EXFIL confirmÃƒÂ©es est important sur Reserve PMC, oÃƒÂ¹
            // aucune sortie disponible n'est entiÃƒÂ¨rement sans condition.
            var newlyRecognized = compatible
                .Where(candidate => panel.ConfirmedIds.Contains(candidate.Id))
                .ToArray();
            if (newlyRecognized.Length == 0)
            {
                SetStatus(
                    panel.IsComplete
                        ? "Aucune sortie EXFIL reconnue · O deux fois puis F8"
                        : $"Lecture incomplète {panel.ConfirmedIds.Count}/{panel.SlotCount} · laisse O ouvert puis refais F8",
                    Color.FromArgb(217, 170, 78));
                return;
            }

            var confirmedIds = panel.IsComplete || _confirmedExtractionIds is null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(_confirmedExtractionIds, StringComparer.OrdinalIgnoreCase);
            confirmedIds.UnionWith(newlyRecognized.Select(candidate => candidate.Id));
            _confirmedExtractionIds = confirmedIds;
            _extractionPanelComplete = panel.IsComplete;
            var confirmedExtractions = compatible
                .Where(candidate => confirmedIds.Contains(candidate.Id))
                .ToArray();
            await ShowRaidExtractionMarkersAsync(confirmedExtractions, raidGeneration);

            var observation = _lastObservation;
            if (observation is not null)
            {
                await UpdateExtractionGuidanceAsync(observation);
            }

            if (!panel.IsComplete)
            {
                SetStatus(
                    $"Guidage actif · lecture {panel.ConfirmedIds.Count}/{panel.SlotCount} · F8 pour compléter",
                    Color.FromArgb(88, 168, 138));
            }
        }
        catch (OperationCanceledException) when (scanCancellation.IsCancellationRequested)
        {
        }
        catch (Exception) when (raidGeneration != _raidGeneration)
        {
        }
        catch (Exception)
        {
            SetStatus("Lecture des sorties à reprendre · O deux fois puis F8", Color.FromArgb(217, 170, 78));
        }
        finally
        {
            (
                string ScreenshotPath,
                int RaidGeneration,
                string MapName,
                long CaptureTimestamp,
                int RaidTimerGeneration)? pending = null;
            if (ReferenceEquals(_extractionScanCancellation, scanCancellation))
            {
                _extractionScanCancellation = null;
                _extractionScanInProgress = false;
                pending = _pendingExtractionScan;
                _pendingExtractionScan = null;
            }

            if (pending is { } next
                && next.RaidGeneration == _raidGeneration
                && string.Equals(next.MapName, _currentMap, StringComparison.OrdinalIgnoreCase))
            {
                _ = RecognizeRaidExtractionsAsync(
                    next.ScreenshotPath,
                    next.RaidGeneration,
                    next.MapName,
                    next.CaptureTimestamp,
                    next.RaidTimerGeneration);
            }
        }
    }

    internal static async Task<T> AwaitRequiredScanAsync<T>(
        Task<T> requiredScan,
        Task optionalEnrichment)
    {
        ArgumentNullException.ThrowIfNull(requiredScan);
        ArgumentNullException.ThrowIfNull(optionalEnrichment);
        _ = ObserveOptionalScanAsync(optionalEnrichment);
        return await requiredScan;
    }

    private static async Task ObserveOptionalScanAsync(Task optionalEnrichment)
    {
        try
        {
            await optionalEnrichment;
        }
        catch
        {
            // Un enrichissement facultatif ne doit jamais devenir une exception
            // non observÃƒÂ©e ni remonter dans le chemin critique des extractions.
        }
    }

    private async Task RecognizeRaidTimerAsync(
        string screenshotPath,
        int raidGeneration,
        string mapName,
        long captureTimestamp,
        int raidTimerGeneration)
    {
        if (_raidTimerScanInProgress)
        {
            // Keep only the newest screenshot. Serializing OCR is useful, but
            // retaining every obsolete timer request would create a CPU queue.
            _pendingRaidTimerScan = (
                screenshotPath,
                raidGeneration,
                mapName,
                captureTimestamp,
                raidTimerGeneration);
            return;
        }

        _raidTimerScanInProgress = true;
        using var scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(_formLifetime.Token);
        _raidTimerScanCancellation = scanCancellation;
        try
        {
            _raidTimerReadAttempts++;
            SetRaidTimerReadState(RaidTimerReadState.Reading);
            var raidTimerLines = await ReadRaidTimerLinesSafelyAsync(
                screenshotPath, scanCancellation.Token);
            if (scanCancellation.IsCancellationRequested || raidGeneration != _raidGeneration
                || !string.Equals(mapName, _currentMap, StringComparison.OrdinalIgnoreCase)) return;
            if (!RaidTimerParser.TryParseRemainingSeconds(raidTimerLines, out var capturedRemainingSeconds))
            {
                SetRaidTimerReadState(RaidTimerReadState.Unreadable);
                return;
            }

            var now = Stopwatch.GetTimestamp();
            if (_raidCountdown.TrySynchronize(
                raidTimerGeneration,
                capturedRemainingSeconds,
                captureTimestamp,
                now))
            {
                SetRaidTimerReadState(RaidTimerReadState.Synchronized);
                await ShowRaidCountdownAsync(_raidCountdown.GetSnapshot(now));
            }
            else SetRaidTimerReadState(RaidTimerReadState.ConfirmationRequired);
        }
        catch (OperationCanceledException) when (scanCancellation.IsCancellationRequested) { }
        catch
        {
            if (raidGeneration == _raidGeneration)
                SetRaidTimerReadState(RaidTimerReadState.Failed);
            // A failed optional read must not discard the last synchronized clock or EXFIL list.
        }
        finally
        {
            (
                string ScreenshotPath,
                int RaidGeneration,
                string MapName,
                long CaptureTimestamp,
                int RaidTimerGeneration)? pending = null;
            if (ReferenceEquals(_raidTimerScanCancellation, scanCancellation))
            {
                _raidTimerScanCancellation = null;
                _raidTimerScanInProgress = false;
                pending = _pendingRaidTimerScan;
                _pendingRaidTimerScan = null;
            }

            if (pending is { } next
                && next.RaidGeneration == _raidGeneration
                && string.Equals(next.MapName, _currentMap, StringComparison.OrdinalIgnoreCase))
            {
                _ = RecognizeRaidTimerAsync(
                    next.ScreenshotPath,
                    next.RaidGeneration,
                    next.MapName,
                    next.CaptureTimestamp,
                    next.RaidTimerGeneration);
            }
        }
    }

    private async Task<IReadOnlyList<string>> ReadRaidTimerLinesSafelyAsync(
        string screenshotPath,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _screenshotTextReader.ReadRaidTimerLinesAsync(
                screenshotPath,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Le chrono est un enrichissement indÃƒÂ©pendant. Une lecture ratÃƒÂ©e
            // ne doit jamais empÃƒÂªcher la reconnaissance des extractions.
            return Array.Empty<string>();
        }
    }

    private void TryStartQuestScan(
        string screenshotPath,
        int raidGeneration,
        string mapName)
    {
        var watcher = _screenshotWatcher;
        if (watcher is null
            || raidGeneration != _raidGeneration
            || !string.Equals(mapName, _currentMap, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var screenshotsRoot = Path.GetFullPath(watcher.ScreenshotsPath);
        var fullPath = Path.GetFullPath(screenshotPath);
        if (!string.Equals(
                Path.GetDirectoryName(fullPath),
                screenshotsRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_questScanInProgress)
        {
            _pendingQuestScan = (fullPath, raidGeneration, mapName);
            return;
        }

        _ = RecognizeRaidQuestsAsync(fullPath, raidGeneration, mapName);
    }

    private async Task RecognizeRaidQuestsAsync(
        string screenshotPath,
        int raidGeneration,
        string mapName)
    {
        if (_questScanInProgress)
        {
            _pendingQuestScan = (screenshotPath, raidGeneration, mapName);
            return;
        }

        _questScanInProgress = true;
        using var scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(_formLifetime.Token);
        _questScanCancellation = scanCancellation;
        try
        {
            var linesTask = _screenshotTextReader.ReadQuestPanelLinesAsync(
                screenshotPath,
                scanCancellation.Token);
            var lines = await linesTask;
            if (scanCancellation.IsCancellationRequested
                || raidGeneration != _raidGeneration
                || !string.Equals(mapName, _currentMap, StringComparison.OrdinalIgnoreCase)
                || !QuestAdvisor.IsQuestScreen(lines))
            {
                return;
            }

            var gameMode = QuestAdvisor.DetectGameMode(lines, _raidQuestGameMode);
            var candidates = await GetCurrentQuestCandidatesAsync(gameMode, scanCancellation.Token);

            if (scanCancellation.IsCancellationRequested
                || raidGeneration != _raidGeneration
                || !string.Equals(mapName, _currentMap, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            _raidQuestGameMode = gameMode;

            var recognized = QuestAdvisor.ResolveFromOcrLines(candidates, lines);
            if (recognized.Count == 0)
            {
                return;
            }

            var confirmed = _confirmedQuestIds is null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(_confirmedQuestIds, StringComparer.OrdinalIgnoreCase);
            var previousCount = confirmed.Count;
            confirmed.UnionWith(recognized);
            _confirmedQuestIds = confirmed;

            var selected = candidates
                .Where(candidate => confirmed.Contains(candidate.Id))
                .ToArray();

            _questMenuItem.Text = confirmed.Count == 1
                ? "Quêtes : 1 repérée sur cette carte"
                : $"Quêtes : {confirmed.Count} repérées sur cette carte";

            // Les identifiants de certaines quÃƒÂªtes diffÃƒÂ¨rent entre PvE et PvP.
            // tarkov.dev construit ses couches depuis le mode Redux lu au
            // chargement; persister le bon mode puis recharger une seule fois
            // garantit que les questId OCR existent rÃƒÂ©ellement sur la carte.
            if (await EnsureWebMapGameModeAsync(gameMode, raidGeneration))
            {
                return;
            }

            await ShowRaidQuestMarkersAsync(selected, raidGeneration);
            if (confirmed.Count > previousCount)
            {
                SetStatus(
                    confirmed.Count == 1
                        ? "1 quête placée sur la carte"
                        : $"{confirmed.Count} quêtes placées sur la carte",
                    Color.FromArgb(196, 113, 237));
            }
        }
        catch (OperationCanceledException) when (scanCancellation.IsCancellationRequested)
        {
        }
        catch (Exception) when (raidGeneration != _raidGeneration)
        {
        }
        catch
        {
            // La capture reste une ancre de position valide. Une prochaine F8
            // rÃƒÂ©essaiera le catalogue ou l'OCR des quÃƒÂªtes sans gÃƒÂªner le guidage.
        }
        finally
        {
            (string ScreenshotPath, int RaidGeneration, string MapName)? pending = null;
            if (ReferenceEquals(_questScanCancellation, scanCancellation))
            {
                _questScanCancellation = null;
                _questScanInProgress = false;
                pending = _pendingQuestScan;
                _pendingQuestScan = null;
            }

            if (pending is { } next
                && next.RaidGeneration == _raidGeneration
                && string.Equals(next.MapName, _currentMap, StringComparison.OrdinalIgnoreCase))
            {
                _ = RecognizeRaidQuestsAsync(
                    next.ScreenshotPath,
                    next.RaidGeneration,
                    next.MapName);
            }
        }
    }

    private void CalibrateRealtimeEstimator(
        ScreenshotObservation observation,
        RealtimePose? estimatedPoseAtCapture,
        bool allowCalibration)
    {
        var hipCounts = Interlocked.Exchange(ref _hipMouseCountsSinceAnchor, 0);
        var adsCounts = Interlocked.Exchange(ref _adsMouseCountsSinceAnchor, 0);
        var hipMagnitude = Interlocked.Exchange(ref _hipMouseMagnitudeSinceAnchor, 0);
        var adsMagnitude = Interlocked.Exchange(ref _adsMouseMagnitudeSinceAnchor, 0);
        var walkDistance = _walkDistanceSinceAnchor;
        var sprintDistance = _sprintDistanceSinceAnchor;
        var slowDistance = _slowDistanceSinceAnchor;
        var cleanSegment = _precisionSegmentClean;
        ResetDistanceCalibrationCounters();
        var previousExact = _lastExactObservation;
        if (!_liveTrackingEnabled || previousExact is null || !allowCalibration
            || previousExact.FileName.StartsWith("raidpilot-", StringComparison.Ordinal)
            || !CanLearnPrecisionV4())
        {
            return;
        }

        var settings = _realtimeEstimator.Settings;
        var nextSettings = _precisionCalibration.Learn(settings, previousExact, observation,
            estimatedPoseAtCapture ?? _realtimeEstimator.CurrentPose,
            hipCounts, adsCounts, hipMagnitude, adsMagnitude,
            walkDistance, sprintDistance, slowDistance, cleanSegment);

        if (nextSettings != settings)
        {
            _realtimeEstimator.ApplySettings(nextSettings);
            _ = RealtimeCalibrationStore.TrySave(
                GetRealtimeCalibrationPath(),
                nextSettings,
                new RealtimeCalibrationSource(
                    _controlSettings.MouseSensitivity,
                    _controlSettings.MouseAimingSensitivity));
        }
    }

    private static string GetRealtimeCalibrationPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RaidPilot",
            "realtime-calibration.json");
    }

    private static string GetLearnedRouteGraphPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RaidPilot",
            "learned-routes-v1.json");
    }

    private static LearnedRouteGraph LoadLearnedRouteGraph()
    {
        return LearnedRouteGraphStore.TryLoad(
                GetLearnedRouteGraphPath(),
                out var restored)
            && restored is not null
            ? restored
            : new LearnedRouteGraph();
    }

    private void StartLearnedRouteSession(
        string mapId,
        DateTimeOffset? sessionStartedAtUtc = null)
    {
        MaybeSaveLearnedRouteGraph(Stopwatch.GetTimestamp(), force: true);
        StartRaidCountdownSession();
        _raidSessionStartedAtUtc = sessionStartedAtUtc ?? DateTimeOffset.UtcNow;
        _learnedRouteClockOriginUtc = _raidSessionStartedAtUtc;
        _learnedRouteClockOriginTimestamp = Stopwatch.GetTimestamp();
        _lastLearnedRouteObservationAt = 0;
        _learnedRouteGraph.BeginSession(mapId);
        _precisionV2DriftAudit.Reset();
        _precisionV4Fusion.Reset();
        ResetPrecisionV2();
        _visualKeyframeRecognizer.ResetSession();
        _superKeyframeRecognizer.ResetSession();
        _lastSuperKeyframeSample = 0;
        _pendingVisualKeyframeLearning = null;
        InvalidateLearnedRoutePlan();
    }

    private void ObserveLearnedRoutePose(
        RealtimePose pose,
        LearnedPoseSource source,
        double uncertaintyMeters,
        long timestamp,
        bool force = false)
    {
        if (!force
            && source == LearnedPoseSource.LiveEstimate
            && _lastLearnedRouteObservationAt != 0
            && (timestamp - _lastLearnedRouteObservationAt) / (double)Stopwatch.Frequency < 0.35)
        {
            return;
        }

        _lastLearnedRouteObservationAt = timestamp;
        var result = _learnedRouteGraph.Observe(new LearnedRoutePose(
            _currentMap,
            GetLearnedRouteTimestamp(timestamp),
            pose.X,
            pose.Y,
            pose.Z,
            source,
            Math.Max(0.0, uncertaintyMeters)));
        if (result.Accepted)
        {
            _learnedRouteGraphDirty = true;
            if (result.Status == LearnedRouteObservationStatus.AddedNode
                || result.AddedTraversedEdge)
            {
                _learnedRouteGraphRevision = unchecked(_learnedRouteGraphRevision + 1);
            }
            if (source == LearnedPoseSource.ExactAnchor)
            {
                InvalidateLearnedRoutePlan();
            }
        }
    }

    private DateTimeOffset GetLearnedRouteTimestamp(long timestamp)
    {
        var elapsedTicks = Math.Max(0, timestamp - _learnedRouteClockOriginTimestamp);
        return _learnedRouteClockOriginUtc.AddSeconds(
            elapsedTicks / (double)Stopwatch.Frequency);
    }

    private void MaybeSaveLearnedRouteGraph(long timestamp, bool force = false)
    {
        if (!_learnedRouteGraphDirty)
        {
            return;
        }

        if (!force
            && _lastLearnedRouteSaveAt != 0
            && (timestamp - _lastLearnedRouteSaveAt) / (double)Stopwatch.Frequency < 45.0)
        {
            return;
        }

        if (_learnedRouteSaveTask is { IsCompleted: false })
        {
            return;
        }

        var savedRevision = _learnedRouteGraphRevision;
        var saveTask = LearnedRouteGraphStore.TrySaveAsync(
            GetLearnedRouteGraphPath(),
            _learnedRouteGraph);
        _learnedRouteSaveTask = saveTask;
        _lastLearnedRouteSaveAt = timestamp;
        _ = CompleteLearnedRouteSaveAsync(saveTask, savedRevision);
    }

    private async Task CompleteLearnedRouteSaveAsync(Task<bool> saveTask, long savedRevision)
    {
        var saved = false;
        try
        {
            saved = await saveTask;
        }
        catch
        {
            // Le prochain intervalle ou la fermeture rÃƒÂ©essaiera.
        }

        if (!ReferenceEquals(_learnedRouteSaveTask, saveTask))
        {
            return;
        }

        _learnedRouteSaveTask = null;
        if (saved && savedRevision == _learnedRouteGraphRevision)
        {
            _learnedRouteGraphDirty = false;
        }
    }

    private void SaveLearnedRouteGraphForShutdown()
    {
        if (!_learnedRouteGraphDirty)
        {
            return;
        }

        if (LearnedRouteGraphStore.TrySave(GetLearnedRouteGraphPath(), _learnedRouteGraph))
        {
            _learnedRouteGraphDirty = false;
            _lastLearnedRouteSaveAt = Stopwatch.GetTimestamp();
        }
    }

    private void InvalidateLearnedRoutePlan()
    {
        _cachedLearnedRoutePlan = null;
        _cachedLearnedRouteTargetId = null;
        _cachedLearnedRouteGeneration = -1;
        _cachedLearnedRouteRevision = -1;
        _lastLearnedRoutePlanAt = 0;
        _activeLearnedRouteProgressSignature = null;
        _activeLearnedRouteNextIndex = 0;
    }

    private static ScreenshotObservation CreateLiveObservation(RealtimePose pose)
    {
        return new ScreenshotObservation(
            "raidpilot-live",
            pose.X,
            pose.Y,
            pose.Z,
            0,
            0,
            0,
            1,
            pose.YawDegrees);
    }

    private void ResetDistanceCalibrationCounters()
    {
        _precisionSegmentClean = true;
        _walkDistanceSinceAnchor = 0.0;
        _sprintDistanceSinceAnchor = 0.0;
        _slowDistanceSinceAnchor = 0.0;
    }

    private void ResetControlTrackingState()
    {
        _controlTrackingActive = false;
        _rawKeyboardTimeline.Reset();
        _precisionYaw.Reset();
        _walkKeyWasDown = false;
        _sprintKeyWasDown = false;
        _aimKeyWasDown = false;
        _duckKeyWasDown = false;
        _proneKeyWasDown = false;
        _jumpKeyWasDown = false;
        _duckChordUsed = false;
        _movementInputWasActive = false;
        _middleMouseDown = false;
        _crouched = false;
        _prone = false;
        _slowWalkEnabled = false;
        _sprintEnabled = false;
        _aimingEnabled = false;
        _commandedStationarySeconds = 0.0;
    }

    private static double Blend(double current, double candidate, double amount)
    {
        return current + ((candidate - current) * amount);
    }

    private static double NormalizeSigned180(double angleDegrees)
    {
        var normalized = (angleDegrees + 180.0) % 360.0;
        if (normalized < 0.0)
        {
            normalized += 360.0;
        }
        return normalized - 180.0;
    }

    private async Task SendObservationAsync(ScreenshotObservation observation)
    {
        var client = _gpsClient;
        if (!_gpsEnabled || client is null)
        {
            return;
        }

        try
        {
            var map = _currentMap;
            await client.SendPositionAsync(observation, map, _formLifetime.Token);
            RunOnUiThread(() =>
            {
                ShowNoActivate();
                if (_selectedExtraction is null)
                {
                    SetStatus(
                        $"Position mise à jour · {DateTime.Now:HH:mm:ss}",
                        Color.FromArgb(88, 168, 138));
                }
            });
        }
        catch (OperationCanceledException) when (_formLifetime.IsCancellationRequested || !_gpsEnabled)
        {
        }
        catch (Exception) when (!_gpsEnabled)
        {
            // A disable can close the socket while a filename event is in flight.
        }
        catch (Exception)
        {
            RunOnUiThread(() => SetStatus("GPS en attente de reconnexion", Color.FromArgb(217, 170, 78)));
        }
    }

    private async Task CycleExtractionAsync()
    {
        if (_lastObservation is null)
        {
            _trayIcon.ShowBalloonTip(
                5000,
                "RaidPilot — extraction",
                "Appuie sur F8 en raid avant de choisir une extraction.",
                ToolTipIcon.Info);
            return;
        }

        if (_extractionCycleInProgress)
        {
            return;
        }

        if (_confirmedExtractionIds is not null)
        {
            await UpdateExtractionGuidanceAsync(_lastObservation);
            return;
        }

        _extractionCycleInProgress = true;
        _extractionMenuItem.Enabled = false;
        var guidanceGeneration = _guidanceGeneration;

        try
        {
            var candidates = await GetCurrentExtractionCandidatesAsync();
            var observation = _lastObservation;
            if (observation is null
                || guidanceGeneration != _guidanceGeneration)
            {
                return;
            }

            var compatible = candidates
                .Where(candidate => IsExtractionCompatibleWithCurrentSide(candidate))
                .Select(candidate => new
                {
                    Candidate = candidate,
                    Distance = HorizontalDistance(observation, candidate),
                    Conditional = ExtractionAdvisor.EffectiveRequirements(candidate) != ExtractionRequirement.None,
                })
                .OrderBy(item => item.Conditional)
                .ThenBy(item => item.Distance)
                .ThenBy(item => item.Candidate.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(item => item.Candidate)
                .ToArray();

            if (compatible.Length == 0)
            {
                _trayIcon.ShowBalloonTip(
                    5000,
                    "RaidPilot — extraction",
                    $"Aucune extraction {_raidSide} n’a été trouvée pour {_currentMap}.",
                    ToolTipIcon.Warning);
                return;
            }

            var currentIndex = Array.FindIndex(
                compatible,
                candidate => IsSameExtraction(candidate, _selectedExtraction));
            var next = compatible[(currentIndex + 1) % compatible.Length];
            var selectedGeneration = SelectExtractionTarget(next);
            await ShowExtractionGuidanceAsync(
                observation,
                next,
                selectedGeneration,
                estimated: _liveTrackingEnabled,
                GetCurrentGuidanceUncertainty());
        }
        catch (OperationCanceledException) when (_formLifetime.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            SetStatus("Extractions indisponibles", Color.FromArgb(217, 170, 78));
            _trayIcon.ShowBalloonTip(
                5000,
                "RaidPilot — extraction",
                "Les données d’extraction tarkov.dev sont momentanément indisponibles.",
                ToolTipIcon.Warning);
        }
        finally
        {
            _extractionCycleInProgress = false;
            _extractionMenuItem.Enabled = true;
        }
    }

    private async Task UpdateExtractionGuidanceAsync(ScreenshotObservation observation)
    {
        var guidanceGeneration = _guidanceGeneration;
        if (_confirmedExtractionIds is not null)
        {
            try
            {
                await GetCurrentExtractionCandidatesAsync();
                if (guidanceGeneration != _guidanceGeneration
                    || !TryChooseNearestConfirmedExtraction(
                        _lastObservation ?? observation,
                        out var automaticTarget))
                {
                    return;
                }

                var selectedGeneration = SelectExtractionTarget(automaticTarget);
                await ShowExtractionGuidanceAsync(
                    _lastObservation ?? observation,
                    automaticTarget,
                    selectedGeneration,
                    estimated: _liveTrackingEnabled,
                    GetCurrentGuidanceUncertainty());
            }
            catch (OperationCanceledException) when (_formLifetime.IsCancellationRequested)
            {
            }
            catch
            {
                // Le prochain mouvement ou F8 rÃƒÂ©essaiera avec le catalogue gardÃƒÂ©
                // en mÃƒÂ©moire.
            }
            return;
        }

        var selected = _selectedExtraction;
        if (selected is null)
        {
            return;
        }

        try
        {
            var candidates = await GetCurrentExtractionCandidatesAsync();
            if (guidanceGeneration != _guidanceGeneration
                || _selectedExtraction is null
                || !IsSameExtraction(_selectedExtraction, selected))
            {
                return;
            }

            var target = candidates.FirstOrDefault(candidate =>
                IsSameExtraction(candidate, selected));
            if (target is null)
            {
                await ClearExtractionGuidanceAsync();
                return;
            }

            await ShowExtractionGuidanceAsync(
                _lastObservation ?? observation,
                target,
                guidanceGeneration,
                estimated: _liveTrackingEnabled,
                GetCurrentGuidanceUncertainty());
        }
        catch (OperationCanceledException) when (_formLifetime.IsCancellationRequested)
        {
        }
        catch
        {
            // The GPS update remains useful even when the public extraction
            // catalogue is temporarily unreachable.
        }
    }

    private bool TryChooseNearestConfirmedExtraction(
        ScreenshotObservation observation,
        out ExtractionCandidate target)
    {
        target = null!;
        if (_confirmedExtractionIds is null
            || !string.Equals(_extractionsMap, _currentMap, StringComparison.OrdinalIgnoreCase)
            || !ExtractionAdvisor.TryChooseConfirmedWithConditionalFallback(
                observation,
                _raidSide,
                _extractions,
                _confirmedExtractionIds,
                out var recommendation)
            || recommendation is null)
        {
            return false;
        }

        var confirmed = _extractions
            .Where(candidate => _confirmedExtractionIds.Contains(candidate.Id))
            .Where(IsExtractionCompatibleWithCurrentSide)
            .ToArray();
        var ordinary = confirmed
            .Where(candidate => ExtractionAdvisor.EffectiveRequirements(candidate) == ExtractionRequirement.None)
            .ToArray();
        var routeCandidates = ordinary.Length > 0 ? ordinary : confirmed;
        var uncertainty = GetCurrentGuidanceUncertainty();
        var routeNow = Stopwatch.GetTimestamp();
        var routeCacheFresh = IsLearnedRouteCacheFresh(
            observation,
            uncertainty,
            routeNow);
        if (routeCacheFresh && _cachedLearnedRouteGeneration == _guidanceGeneration)
        {
            if (LearnedRoutePolicy.IsTrusted(_cachedLearnedRoutePlan, uncertainty))
            {
                var cachedTarget = routeCandidates.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.Id,
                        _cachedLearnedRoutePlan!.Target.Id,
                        StringComparison.OrdinalIgnoreCase));
                if (cachedTarget is not null)
                {
                    target = cachedTarget;
                    return true;
                }
            }
            else if (_cachedLearnedRoutePlan is null)
            {
                // Un ÃƒÂ©chec est aussi un rÃƒÂ©sultat de cache. Sans cela, un
                // graphe dÃƒÂ©connectÃƒÂ© relancerait A* dix fois par seconde.
                target = recommendation.Target;
                return true;
            }
        }

        if (TryPlanLearnedRoute(
                observation,
                routeCandidates,
                uncertainty,
                out var learnedPlan)
            && learnedPlan is not null
            && LearnedRoutePolicy.IsTrusted(learnedPlan, uncertainty))
        {
            var learnedTarget = routeCandidates.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, learnedPlan.Target.Id, StringComparison.OrdinalIgnoreCase));
            if (learnedTarget is not null)
            {
                CacheLearnedRoutePlan(
                    learnedTarget.Id,
                    learnedPlan,
                    observation,
                    uncertainty);
                target = learnedTarget;
                return true;
            }
        }

        CacheLearnedRoutePlan(
            recommendation.Target.Id,
            plan: null,
            observation,
            uncertainty);

        target = recommendation.Target;
        return true;
    }

    private async Task<IReadOnlyList<ExtractionCandidate>> GetCurrentExtractionCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(_extractionsMap, _currentMap, StringComparison.OrdinalIgnoreCase))
        {
            return _extractions;
        }

        var map = _currentMap;
        var effectiveToken = cancellationToken.CanBeCanceled
            ? cancellationToken
            : _formLifetime.Token;
        var candidates = await _extractionProvider.GetForMapAsync(map, effectiveToken);
        effectiveToken.ThrowIfCancellationRequested();
        if (!string.Equals(map, _currentMap, StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<ExtractionCandidate>();
        }

        _extractions = candidates;
        _extractionsMap = map;
        return _extractions;
    }

    private async Task<IReadOnlyList<QuestCandidate>> GetCurrentQuestCandidatesAsync(
        string gameMode,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(_questsMap, _currentMap, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_questsGameMode, gameMode, StringComparison.Ordinal))
        {
            return _quests;
        }

        var map = _currentMap;
        var effectiveToken = cancellationToken.CanBeCanceled
            ? cancellationToken
            : _formLifetime.Token;
        var candidates = await _questProvider.GetForMapAsync(map, gameMode, effectiveToken);
        effectiveToken.ThrowIfCancellationRequested();
        if (!string.Equals(map, _currentMap, StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<QuestCandidate>();
        }

        _quests = candidates;
        _questsMap = map;
        _questsGameMode = gameMode;
        return _quests;
    }

    private void StartRaidCountdownSession()
    {
        var generation = _raidCountdown.StartSession();
        ResetRaidTimerUi();
        _ = ClearRaidCountdownAsync(generation, showPending: true);
    }

    private async Task ShowRaidCountdownAsync(RaidCountdownSnapshot snapshot)
    {
        if (!snapshot.IsActive
            || !snapshot.IsSynchronized
            || _webView.CoreWebView2 is null)
        {
            return;
        }

        var current = _raidCountdown.GetSnapshot(Stopwatch.GetTimestamp());
        if (current.Generation != snapshot.Generation
            || !current.IsSynchronized)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            generation = current.Generation,
            remainingSeconds = current.RemainingSeconds,
            sequence = ++_raidTimerUiSequence,
        });
        try
        {
            var accepted = await _webView.CoreWebView2.ExecuteScriptAsync(
                $"window.__raidPilotSetRaidTimer?.({payload}) ?? false;");
            _raidTimerLastRenderAccepted = accepted == "true";
        }
        catch
        {
            // Une navigation peut remplacer le document entre la lecture OCR
            // et l'injection. RestoreRaidCountdownAsync rÃƒÂ©applique l'ancre.
        }
    }

    private async Task ClearRaidCountdownAsync(int generation, bool showPending)
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            var pending = showPending ? "true" : "false";
            var sequence = ++_raidTimerUiSequence;
            var state = JsonSerializer.Serialize(_raidTimerReadState.ToString().ToLowerInvariant());
            var accepted = await _webView.CoreWebView2.ExecuteScriptAsync(
                $"window.__raidPilotClearRaidTimer?.({generation}, {pending}, {sequence}, {state}) ?? false;");
            _raidTimerLastRenderAccepted = accepted == "true";
        }
        catch
        {
            // Le document en cours de remplacement dÃƒÂ©truit dÃƒÂ©jÃƒÂ  son compteur.
        }
    }

    private async Task RestoreRaidCountdownAsync()
    {
        var snapshot = _raidCountdown.GetSnapshot(Stopwatch.GetTimestamp());
        if (!snapshot.IsActive)
        {
            return;
        }

        if (snapshot.IsSynchronized)
        {
            await ShowRaidCountdownAsync(snapshot);
        }
        else
        {
            await ClearRaidCountdownAsync(snapshot.Generation, showPending: true);
        }
    }

    private async Task ShowRaidExtractionMarkersAsync(
        IEnumerable<ExtractionCandidate> extractions,
        int raidGeneration)
    {
        if (raidGeneration != _raidGeneration
            || _webView.CoreWebView2 is null)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            generation = raidGeneration,
            extractions = extractions.Select(extraction => new
            {
                id = extraction.Id,
                labels = (extraction.Aliases ?? Array.Empty<string>())
                    .Append(extraction.Name)
                    .Where(label => !string.IsNullOrWhiteSpace(label))
                    .Distinct(StringComparer.CurrentCultureIgnoreCase),
            }),
        });
        try
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                if (raidGeneration != _raidGeneration || _webView.CoreWebView2 is null)
                {
                    return;
                }

                var script = attempt == 0
                    ? $"window.__raidPilotSetRaidExtractions?.({payload}) ?? 0;"
                    : "window.__raidPilotApplyRaidExtractions?.() ?? 0;";
                var rawCount = await _webView.CoreWebView2.ExecuteScriptAsync(script);
                if (int.TryParse(rawCount, out var matchedCount) && matchedCount > 0)
                {
                    return;
                }

                if (attempt < 3)
                {
                    await Task.Delay(180, _formLifetime.Token);
                }
            }
        }
        catch (OperationCanceledException) when (_formLifetime.IsCancellationRequested)
        {
        }
        catch
        {
            // Une reconstruction Leaflet ou la prochaine navigation rÃƒÂ©appliquera
            // la liste gardÃƒÂ©e en mÃƒÂ©moire.
        }
    }

    private async Task ClearRaidExtractionMarkersAsync(int raidGeneration)
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            await _webView.CoreWebView2.ExecuteScriptAsync(
                $"window.__raidPilotClearRaidExtractions?.({raidGeneration}); void 0;");
        }
        catch
        {
            // Navigation ou fermeture : le document dÃƒÂ©truit dÃƒÂ©jÃƒÂ  les marqueurs.
        }
    }

    private async Task RestoreRaidExtractionMarkersAsync()
    {
        var confirmed = _confirmedExtractionIds;
        if (confirmed is null)
        {
            return;
        }

        var confirmedExtractions = _extractions
            .Where(candidate => confirmed.Contains(candidate.Id))
            .ToArray();
        await ShowRaidExtractionMarkersAsync(confirmedExtractions, _raidGeneration);
    }

    private async Task ShowRaidQuestMarkersAsync(
        IEnumerable<QuestCandidate> quests,
        int raidGeneration)
    {
        if (raidGeneration != _raidGeneration
            || _webView.CoreWebView2 is null)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            generation = raidGeneration,
            gameMode = _raidQuestGameMode ?? "regular",
            map = _currentMap,
            quests = quests.Select(quest => new
            {
                id = quest.Id,
                name = quest.Name,
            }),
        });
        try
        {
            for (var attempt = 0; attempt < 8; attempt++)
            {
                if (raidGeneration != _raidGeneration || _webView.CoreWebView2 is null)
                {
                    return;
                }

                var script = attempt == 0
                    ? $"window.__raidPilotSetRaidQuests?.({payload}) ?? 0;"
                    : "window.__raidPilotApplyRaidQuests?.() ?? 0;";
                var rawCount = await _webView.CoreWebView2.ExecuteScriptAsync(script);
                if (int.TryParse(rawCount, out var matchedCount) && matchedCount > 0)
                {
                    return;
                }

                if (attempt < 7)
                {
                    await Task.Delay(300, _formLifetime.Token);
                }
            }
        }
        catch (OperationCanceledException) when (_formLifetime.IsCancellationRequested)
        {
        }
        catch
        {
            // Le script garde les IDs et les rÃƒÂ©applique lorsque tarkov.dev finit
            // de construire ses couches de quÃƒÂªtes.
        }
    }

    private async Task<bool> EnsureWebMapGameModeAsync(
        string gameMode,
        int raidGeneration)
    {
        if (_offlineMapMode
            || gameMode is not ("regular" or "pve" or "pvp-season")
            || raidGeneration != _raidGeneration
            || _webView.CoreWebView2 is null
            || !_webReady
            || _navigationInProgress)
        {
            return false;
        }

        var serializedMode = JsonSerializer.Serialize(gameMode);
        try
        {
            var changed = await _webView.CoreWebView2.ExecuteScriptAsync($$"""
                (() => {
                    const expected = {{serializedMode}};
                    let current = "regular";
                    try {
                        current = JSON.parse(localStorage.getItem("gameMode") ?? '"regular"');
                    } catch {
                        current = "regular";
                    }
                    if (current === expected) {
                        return false;
                    }
                    localStorage.setItem("gameMode", JSON.stringify(expected));
                    return true;
                })();
                """);
            if (!bool.TryParse(changed, out var mustReload)
                || !mustReload
                || raidGeneration != _raidGeneration)
            {
                return false;
            }

            _webView.CoreWebView2.Reload();
            return true;
        }
        catch
        {
            // La navigation en cours rÃƒÂ©essaiera la synchronisation avant de
            // restaurer les marqueurs. La capture reste une ancre valide.
            return false;
        }
    }

    private async Task ClearRaidQuestMarkersAsync(int raidGeneration)
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            await _webView.CoreWebView2.ExecuteScriptAsync(
                $"window.__raidPilotClearRaidQuests?.({raidGeneration}); void 0;");
        }
        catch
        {
            // Une navigation dÃƒÂ©truit dÃƒÂ©jÃƒÂ  les marqueurs du document prÃƒÂ©cÃƒÂ©dent.
        }
    }

    private async Task RestoreRaidQuestMarkersAsync()
    {
        var confirmed = _confirmedQuestIds;
        if (confirmed is null)
        {
            return;
        }

        var selected = _quests
            .Where(candidate => confirmed.Contains(candidate.Id))
            .ToArray();
        await ShowRaidQuestMarkersAsync(selected, _raidGeneration);
    }

    private bool TryGetLearnedRoutePlan(
        ScreenshotObservation observation,
        ExtractionCandidate target,
        double uncertaintyMeters,
        out LearnedRoutePlan? plan)
    {
        if (!double.IsFinite(uncertaintyMeters)
            || uncertaintyMeters < 0.0
            || uncertaintyMeters > LearnedRoutePolicy.MaximumLiveUncertaintyMeters)
        {
            InvalidateLearnedRoutePlan();
            plan = null;
            return false;
        }

        var now = Stopwatch.GetTimestamp();
        if (_cachedLearnedRouteGeneration == _guidanceGeneration
            && string.Equals(
                _cachedLearnedRouteTargetId,
                target.Id,
                StringComparison.OrdinalIgnoreCase)
            && IsLearnedRouteCacheFresh(observation, uncertaintyMeters, now))
        {
            plan = _cachedLearnedRoutePlan;
            return LearnedRoutePolicy.IsTrusted(plan, uncertaintyMeters);
        }

        var success = TryPlanLearnedRoute(
            observation,
            new[] { target },
            uncertaintyMeters,
            out plan);
        CacheLearnedRoutePlan(
            target.Id,
            success ? plan : null,
            observation,
            uncertaintyMeters);
        return success;
    }

    private bool TryPlanLearnedRoute(
        ScreenshotObservation observation,
        IEnumerable<ExtractionCandidate> targets,
        double uncertaintyMeters,
        out LearnedRoutePlan? plan)
    {
        plan = null;
        if (!double.IsFinite(uncertaintyMeters)
            || uncertaintyMeters < 0.0
            || uncertaintyMeters > LearnedRoutePolicy.MaximumLiveUncertaintyMeters)
        {
            return false;
        }

        var routeTargets = targets
            .Where(candidate => candidate is not null)
            .Select(candidate => new LearnedRouteTarget(
                candidate.Id,
                candidate.Name,
                candidate.X,
                candidate.Y,
                candidate.Z))
            .ToArray();
        if (routeTargets.Length == 0)
        {
            return false;
        }

        var pose = new LearnedRoutePose(
            _currentMap,
            GetLearnedRouteTimestamp(Stopwatch.GetTimestamp()),
            observation.X,
            observation.Y,
            observation.Z,
            LearnedPoseSource.LiveEstimate,
            uncertaintyMeters);
        if (!_learnedRouteGraph.TryPlanRoute(
                pose,
                routeTargets,
                out plan,
                allowDirectFallback: false)
            || !LearnedRoutePolicy.IsTrusted(plan, uncertaintyMeters))
        {
            plan = null;
            return false;
        }

        return true;
    }

    private bool IsLearnedRouteCacheFresh(
        ScreenshotObservation observation,
        double uncertaintyMeters,
        long now)
    {
        if (_lastLearnedRoutePlanAt == 0
            || _cachedLearnedRouteRevision != _learnedRouteGraphRevision
            || !double.IsFinite(uncertaintyMeters)
            || uncertaintyMeters < 0.0
            || uncertaintyMeters > LearnedRoutePolicy.MaximumLiveUncertaintyMeters
            || Math.Abs(uncertaintyMeters - _cachedLearnedRouteUncertainty)
                > LearnedRoutePolicy.CacheUncertaintyDeltaMeters
            || (now - _lastLearnedRoutePlanAt) / (double)Stopwatch.Frequency
                >= LearnedRoutePolicy.CacheLifetimeSeconds)
        {
            return false;
        }

        var deltaX = observation.X - _cachedLearnedRouteX;
        var deltaZ = observation.Z - _cachedLearnedRouteZ;
        var horizontal = Math.Sqrt((deltaX * deltaX) + (deltaZ * deltaZ));
        return double.IsFinite(horizontal)
            && horizontal <= LearnedRoutePolicy.CacheHorizontalMovementMeters
            && Math.Abs(observation.Y - _cachedLearnedRouteY)
                <= LearnedRoutePolicy.CacheVerticalMovementMeters;
    }

    private void CacheLearnedRoutePlan(
        string targetId,
        LearnedRoutePlan? plan,
        ScreenshotObservation observation,
        double uncertaintyMeters)
    {
        _cachedLearnedRouteGeneration = _guidanceGeneration;
        _cachedLearnedRouteRevision = _learnedRouteGraphRevision;
        _cachedLearnedRouteTargetId = targetId;
        _cachedLearnedRoutePlan = plan;
        _cachedLearnedRouteX = observation.X;
        _cachedLearnedRouteY = observation.Y;
        _cachedLearnedRouteZ = observation.Z;
        _cachedLearnedRouteUncertainty = uncertaintyMeters;
        _lastLearnedRoutePlanAt = Stopwatch.GetTimestamp();
    }

    private int SelectExtractionTarget(ExtractionCandidate target)
    {
        if (!IsSameExtraction(target, _selectedExtraction))
        {
            _guidanceGeneration = unchecked(_guidanceGeneration + 1);
            InvalidateLearnedRoutePlan();
            _ = ClearLearnedRouteOverlayAsync(_guidanceGeneration);
            _ = ClearGuidanceOverlayAsync(_guidanceGeneration);
        }

        _selectedExtraction = target;
        return _guidanceGeneration;
    }

    private double GetCurrentGuidanceUncertainty()
    {
        if (!_liveTrackingEnabled)
        {
            return 0.0;
        }

        var uncertainty = _realtimeEstimator.CurrentPose?.PositionUncertaintyMeters ?? 0.0;
        return double.IsFinite(uncertainty) && uncertainty >= 0.0
            ? uncertainty
            : double.PositiveInfinity;
    }

    private int FindNextLearnedWaypointIndex(
        ScreenshotObservation observation,
        LearnedRoutePlan plan)
    {
        var signature = $"{_guidanceGeneration}:{plan.Target.Id}:"
            + string.Join(',', plan.Waypoints.Select(point => point.NodeId));
        if (!string.Equals(
                signature,
                _activeLearnedRouteProgressSignature,
                StringComparison.Ordinal))
        {
            _activeLearnedRouteProgressSignature = signature;
            _activeLearnedRouteNextIndex = 0;
        }

        _activeLearnedRouteNextIndex = LearnedRouteProgress.Advance(
            observation,
            plan.Waypoints,
            _activeLearnedRouteNextIndex);
        return _activeLearnedRouteNextIndex;
    }

    private async Task ShowLearnedRouteOverlayAsync(
        LearnedRoutePlan plan,
        int nextIndex,
        int guidanceGeneration)
    {
        if (_webView.CoreWebView2 is null
            || guidanceGeneration != _guidanceGeneration
            || plan.Kind != LearnedRouteKind.LearnedGraph
            || !plan.UsesOnlyTraversedEdges)
        {
            return;
        }
        if (plan.Waypoints.Count < 2
            || nextIndex < 0
            || nextIndex >= plan.Waypoints.Count)
        {
            await ClearLearnedRouteOverlayAsync(guidanceGeneration);
            return;
        }

        var signature = $"{guidanceGeneration}:{plan.Target.Id}:{nextIndex}:"
            + string.Join(',', plan.Waypoints.Select(point => point.NodeId));
        if (string.Equals(signature, _renderedLearnedRouteSignature, StringComparison.Ordinal))
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            generation = guidanceGeneration,
            kind = "learned",
            confidence = plan.Confidence.ToString().ToLowerInvariant(),
            nextIndex,
            points = plan.Waypoints.Select(point => new
            {
                x = point.X,
                y = point.Y,
                z = point.Z,
            }),
        });
        await _webView.CoreWebView2.ExecuteScriptAsync(
            $"window.__raidPilotShowLearnedRoute?.({payload}); void 0;");
        if (guidanceGeneration == _guidanceGeneration)
        {
            _renderedLearnedRouteSignature = signature;
        }
        else
        {
            await ClearLearnedRouteOverlayAsync(_guidanceGeneration);
        }
    }

    private async Task ClearLearnedRouteOverlayAsync(int guidanceGeneration)
    {
        if (_webView.CoreWebView2 is null
            || (_renderedLearnedRouteSignature is null
                && guidanceGeneration <= _clearedLearnedRouteGeneration))
        {
            return;
        }

        try
        {
            await _webView.CoreWebView2.ExecuteScriptAsync(
                $"window.__raidPilotClearLearnedRoute?.({guidanceGeneration}); void 0;");
            _clearedLearnedRouteGeneration = Math.Max(
                _clearedLearnedRouteGeneration,
                guidanceGeneration);
        }
        catch
        {
            // Une navigation dÃƒÂ©truit aussi le SVG et ses ÃƒÂ©couteurs.
        }
        finally
        {
            _renderedLearnedRouteSignature = null;
        }
    }

    private async Task ShowExtractionGuidanceAsync(
        ScreenshotObservation observation,
        ExtractionCandidate target,
        int guidanceGeneration,
        bool estimated,
        double uncertaintyMeters)
    {
        if (guidanceGeneration != _guidanceGeneration
            || _webView.CoreWebView2 is null
            || !RouteGuidanceCalculator.TryCalculate(
                observation.X,
                observation.Y,
                observation.Z,
                observation.YawDegrees,
                target.X,
                target.Y,
                target.Z,
                out var destinationGuidance))
        {
            return;
        }

        var pointerGuidance = destinationGuidance;
        var learnedRoute = false;
        var learnedConfidence = LearnedRouteConfidence.Unknown;
        var displayedDistance = destinationGuidance.DistanceMeters;
        if (!destinationGuidance.HasArrived
            && TryGetLearnedRoutePlan(
                observation,
                target,
                uncertaintyMeters,
                out var plan)
            && plan is not null
            && LearnedRoutePolicy.IsTrusted(plan, uncertaintyMeters))
        {
            var nextIndex = FindNextLearnedWaypointIndex(observation, plan);
            if (nextIndex < plan.Waypoints.Count)
            {
                var next = plan.Waypoints[nextIndex];
                if (RouteGuidanceCalculator.TryCalculate(
                    observation.X,
                    observation.Y,
                    observation.Z,
                    observation.YawDegrees,
                    next.X,
                    next.Y,
                    next.Z,
                    out var nextGuidance,
                    arrivalRadiusMeters: LearnedWaypointArrivalRadiusMeters,
                    arrivalVerticalToleranceMeters: LearnedWaypointVerticalToleranceMeters))
                {
                    pointerGuidance = nextGuidance;
                    learnedRoute = true;
                    learnedConfidence = plan.Confidence;
                    displayedDistance = plan.StartSnapDistanceMeters
                        + plan.GraphDistanceMeters
                        + plan.TargetSnapDistanceMeters;
                }
            }

            await ShowLearnedRouteOverlayAsync(plan, nextIndex, guidanceGeneration);
        }
        else
        {
            await ClearLearnedRouteOverlayAsync(guidanceGeneration);
        }

        if (guidanceGeneration != _guidanceGeneration)
        {
            return;
        }

        var conditional = ExtractionAdvisor.EffectiveRequirements(target) != ExtractionRequirement.None;
        var payload = JsonSerializer.Serialize(new
        {
            id = target.Id,
            name = target.Name,
            aliases = (target.Aliases ?? Array.Empty<string>())
                .Append(target.Name)
                .Where(alias => !string.IsNullOrWhiteSpace(alias))
                .Distinct(StringComparer.CurrentCultureIgnoreCase),
            distance = Math.Round(displayedDistance),
            vertical = Math.Round(destinationGuidance.VerticalDeltaMeters),
            angle = Math.Round(pointerGuidance.ArrowRotationDegrees, 1),
            arrived = destinationGuidance.HasArrived,
            conditional,
            generation = guidanceGeneration,
            estimated,
            route = learnedRoute ? "learned" : "direct",
            routeConfidence = learnedConfidence.ToString().ToLowerInvariant(),
            provisional = _confirmedExtractionIds is not null && !_extractionPanelComplete,
            uncertainty = Math.Round(Math.Max(0.0, uncertaintyMeters)),
            needsReanchor = estimated
                && uncertaintyMeters > LearnedRoutePolicy.MaximumLiveUncertaintyMeters,
        });
        await _webView.CoreWebView2.ExecuteScriptAsync(
            $"window.__raidPilotShowGuidance?.({payload}); void 0;");

        if (guidanceGeneration != _guidanceGeneration)
        {
            return;
        }

        if (!estimated)
        {
            _extractionMenuItem.Text = _confirmedExtractionIds is null
                ? $"Extraction : {target.Name} (Ctrl+Alt+E)"
                : $"Extraction auto : {target.Name}";
            SetStatus(
                destinationGuidance.HasArrived
                    ? $"Arrivé · {target.Name}"
                    : $"{target.Name} · {displayedDistance:F0} m",
                Color.FromArgb(88, 168, 138));
            ShowNoActivate();
        }
    }

    private async Task ClearExtractionGuidanceAsync()
    {
        _guidanceGeneration = unchecked(_guidanceGeneration + 1);
        InvalidateLearnedRoutePlan();
        var clearGeneration = _guidanceGeneration;
        _selectedExtraction = null;
        _extractionMenuItem.Text = "Extraction auto : O×2 puis F8 (Ctrl+Alt+E secours)";
        await ClearLearnedRouteOverlayAsync(clearGeneration);
        await ClearGuidanceOverlayAsync(clearGeneration);
    }

    private async Task ClearGuidanceOverlayAsync(int guidanceGeneration)
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            await _webView.CoreWebView2.ExecuteScriptAsync(
                $"window.__raidPilotClearGuidance?.({guidanceGeneration}); void 0;");
        }
        catch
        {
            // Navigation or renderer teardown already removes the in-page HUD.
        }
    }

    private void ResetExtractionGuidance(
        bool clearObservation,
        bool preserveRealtimePoseForImmediateReplay = false)
    {
        _guidanceGeneration = unchecked(_guidanceGeneration + 1);
        _raidGeneration = unchecked(_raidGeneration + 1);
        ResetQuestContext();
        ResetAutomaticFloors();
        ResetPrecisionSession();
        InvalidateLearnedRoutePlan();
        _pendingLivePosition = null;
        _pendingLocalPlayerMarker = null;
        _ = ClearLocalPlayerMarkerAsync(_raidGeneration);
        var scanCancellation = _extractionScanCancellation;
        _extractionScanCancellation = null;
        var questScanCancellation = _questScanCancellation;
        _questScanCancellation = null;
        var raidTimerScanCancellation = _raidTimerScanCancellation;
        _raidTimerScanCancellation = null;
        _pendingExtractionScan = null;
        _pendingQuestScan = null;
        _pendingRaidTimerScan = null;
        _extractionScanInProgress = false;
        _questScanInProgress = false;
        _raidTimerScanInProgress = false;
        _extractionPanelComplete = false;
        scanCancellation?.Cancel();
        questScanCancellation?.Cancel();
        raidTimerScanCancellation?.Cancel();
        _selectedExtraction = null;
        _confirmedExtractionIds = null;
        _confirmedQuestIds = null;
        _raidQuestGameMode = null;
        if (clearObservation)
        {
            ResetMineAlertForRaid();
            ResetBoundaryAlertForRaid();
            _raidStartObservationBuffer.Clear();
            _learnedRouteGraph.BreakContinuity();
            _lastLearnedRouteObservationAt = 0;
            _lastObservation = null;
            if (!preserveRealtimePoseForImmediateReplay)
            {
                _lastExactObservation = null;
                _realtimeEstimator.Reset();
                _realtimePoseHistory.Reset();
            }
            Interlocked.Exchange(ref _pendingRawMouseX, 0);
        Interlocked.Exchange(ref _precisionYawActivity, 0);
            Interlocked.Exchange(ref _pendingRawMouseWheel, 0);
            Interlocked.Exchange(ref _pendingDuckWheelChord, 0);
            Interlocked.Exchange(ref _hipMouseCountsSinceAnchor, 0);
            Interlocked.Exchange(ref _adsMouseCountsSinceAnchor, 0);
            Interlocked.Exchange(ref _hipMouseMagnitudeSinceAnchor, 0);
            Interlocked.Exchange(ref _adsMouseMagnitudeSinceAnchor, 0);
            _automaticReanchorRequest = null;
            Interlocked.Exchange(ref _autoReanchorRequestedAt, 0);
            _lastExactAnchorAt = 0;
            _lastAutoReanchorAttempt = 0;
            _lastManualScreenshotPressAt = 0;
            _screenshotBindingWasDown = false;
            _skipNextRealtimeCalibration = false;
            _movementSpeedScale = 1.0;
            _idleSeconds = 0.0;
            _visualMotionGate.Reset();
            _visualKeyframeRecognizer.ResetSession();
            _superKeyframeRecognizer.ResetSession();
            _visualRelocalization.BeginSession(Stopwatch.GetTimestamp());
            _precisionV4Fusion.Reset();
            _lastSuperKeyframeSample = 0;
            _pendingVisualKeyframeLearning = null;
            ResetDistanceCalibrationCounters();
            ResetControlTrackingState();
        }
        _extractionMenuItem.Text = "Extraction auto : O×2 puis F8 (Ctrl+Alt+E secours)";
        _questMenuItem.Text = "Quêtes : ouvre TÂCHES puis F8";
        _ = ClearExtractionGuidanceAsync();
        _ = ClearRaidExtractionMarkersAsync(_raidGeneration);
        _ = ClearRaidQuestMarkersAsync(_raidGeneration);
    }

    private void SetRaidSide(RaidSide side)
    {
        if (_raidSide == side)
        {
            return;
        }

        _raidSide = side;
        ResetExtractionGuidance(clearObservation: false);
        _trayIcon.ShowBalloonTip(
            3500,
            "RaidPilot — extraction",
            side == RaidSide.Pmc ? "Guidage réglé sur PMC." : "Guidage réglé sur Scav.",
            ToolTipIcon.Info);
    }

    private bool IsExtractionCompatibleWithCurrentSide(ExtractionCandidate candidate)
    {
        var expected = _raidSide == RaidSide.Pmc ? "pmc" : "scav";
        // Some official variant maps (night-factory and ground-zero-21)
        // publish valid extracts without a faction. The player confirms the
        // displayed name with Tarkov's O panel, so keep those candidates.
        return string.IsNullOrWhiteSpace(candidate.Faction)
            || string.Equals(candidate.Faction, expected, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.Faction, "shared", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameExtraction(
        ExtractionCandidate candidate,
        ExtractionCandidate? selected)
    {
        return selected is not null
            && string.Equals(candidate.Id, selected.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.Faction, selected.Faction, StringComparison.OrdinalIgnoreCase)
            && Math.Abs(candidate.X - selected.X) < 0.001
            && Math.Abs(candidate.Y - selected.Y) < 0.001
            && Math.Abs(candidate.Z - selected.Z) < 0.001;
    }

    private static double HorizontalDistance(
        ScreenshotObservation observation,
        ExtractionCandidate candidate)
    {
        var deltaX = candidate.X - observation.X;
        var deltaZ = candidate.Z - observation.Z;
        return Math.Sqrt((deltaX * deltaX) + (deltaZ * deltaZ));
    }

    private void UpdateCurrentMapFromSource()
    {
        if (RaidPilotLocalMapHost.IsLocalMapUri(_webView.Source) && RaidPilotLocalMapHost.TryExtractMapKey(_webView.Source?.AbsoluteUri, out var localFirstMap))
        {
            if (!string.Equals(_currentMap, localFirstMap, StringComparison.OrdinalIgnoreCase))
            {
                _currentMap = localFirstMap;
                SwitchCollectiveAtlasMap(_currentMap);
                StartLearnedRouteSession(_currentMap);
                ResetExtractionGuidance(clearObservation: true);
            }
            return;
        }

        var source = _webView.Source;
        if (source is null)
        {
            return;
        }

        var segments = source.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2
            && string.Equals(segments[0], "map", StringComparison.OrdinalIgnoreCase)
            && segments[1].All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
            && EftMapLogParser.TryNormalizeMapToken(segments[1], out var map)
            && map is not null)
        {
            if (!string.Equals(_currentMap, map.NormalizedName, StringComparison.OrdinalIgnoreCase))
            {
                _currentMap = map.NormalizedName;
                SwitchCollectiveAtlasMap(_currentMap);
                StartLearnedRouteSession(_currentMap);
                ResetExtractionGuidance(clearObservation: true);
            }
        }
    }

    private void EnsureMapLogWatcherStarted()
    {
        if (_mapLogWatcher is not null)
        {
            EnsureQuestLogWatcherStarted();
            return;
        }

        EftMapLogWatcher? watcher = null;
        try
        {
            watcher = new EftMapLogWatcher();
            watcher.MapDetected += OnMapDetected;
            watcher.GameModeDetected += OnEftGameModeDetected;
            watcher.RaidEnded += OnRaidEnded;
            _mapLogWatcher = watcher;
            watcher.Start();
            EnsureQuestLogWatcherStarted();
        }
        catch
        {
            if (watcher is not null)
            {
                watcher.MapDetected -= OnMapDetected;
                watcher.GameModeDetected -= OnEftGameModeDetected;
                watcher.RaidEnded -= OnRaidEnded;
                watcher.Dispose();
            }
            _mapLogWatcher = null;
            // Missing or inaccessible logs are a supported manual-map fallback.
        }
    }

    private void EnsureLocalScreenshotWatcherStarted()
    {
        if (_screenshotWatcher is not null || _formLifetime.IsCancellationRequested)
        {
            return;
        }

        ScreenshotFolderWatcher? watcher = null;
        try
        {
            watcher = new ScreenshotFolderWatcher();
            watcher.StatusChanged += OnWatcherStatusChanged;
            watcher.ObservationCreated += OnObservationCreated;
            _screenshotWatcher = watcher;
            watcher.Start();
        }
        catch
        {
            if (watcher is not null)
            {
                watcher.StatusChanged -= OnWatcherStatusChanged;
                watcher.ObservationCreated -= OnObservationCreated;
                watcher.Dispose();
            }
            _screenshotWatcher = null;
            SetStatus("Captures locales indisponibles", Color.FromArgb(214, 93, 84));
        }
    }

    private void OnMapDetected(object? sender, EftMapDetectedEventArgs args)
    {
        RunOnUiThread(() =>
        {
            if (!ReferenceEquals(sender, _mapLogWatcher))
            {
                return;
            }

            SetRaidActive(true);
            // Never reuse a PvE/PvP mode verified in a previous raid.
            // A fresh Session mode line is mandatory before classifying
            // any new notification from this session.
            _logVerifiedGameMode = null;
            _unscopedQuestLogEvents.Clear();

            // Un nouveau fichier de session reprÃƒÂ©sente un nouveau raid mÃƒÂªme si
            // le joueur relance exactement la mÃƒÂªme carte.
            var transitionTimestamp = Stopwatch.GetTimestamp();
            var previousRaidGeneration = _raidGeneration;
            var carriedObservations = new List<RaidStartObservation>(capacity: 2);
            while (_raidStartObservationBuffer.TryTakeForRaidTransition(
                previousRaidGeneration,
                transitionTimestamp,
                out var carriedObservation))
            {
                carriedObservations.Add(carriedObservation);
            }
            var carryFirstObservation = carriedObservations.Count > 0;
            var sessionStartedAtUtc = DateTimeOffset.UtcNow;
            foreach (var carriedObservation in carriedObservations)
            {
                if (carriedObservation.FileTimestampUtc < sessionStartedAtUtc)
                {
                    sessionStartedAtUtc = carriedObservation.FileTimestampUtc;
                }
            }

            _currentMap = args.Map.NormalizedName;
            StartLearnedRouteSession(_currentMap, sessionStartedAtUtc);
            ResetExtractionGuidance(
                clearObservation: true,
                preserveRealtimePoseForImmediateReplay: carryFirstObservation);
            RefreshPositionKeyMenu();
            EnsureLocalScreenshotWatcherStarted();
            SetStatus($"Raid détecté · {_currentMap} · position prête", Color.FromArgb(88, 168, 138));
            NavigateToDetectedMap(args.Map);

            if (carryFirstObservation && _screenshotWatcher is { } screenshotWatcher)
            {
                // Apply buffered F8 captures exactly once and in arrival order,
                // now that they belong to the new map. The estimator/history
                // survived the reset, so historical reanchoring retains every
                // command after each capture.
                foreach (var carriedObservation in carriedObservations)
                {
                    ReplayBufferedObservation(
                        screenshotWatcher,
                        carriedObservation,
                        crossedRaidTransition: true);
                }
            }
        });
    }

    private void OnRaidEnded(object? sender, EventArgs args)
    {
        RunOnUiThread(() =>
        {
            if (!ReferenceEquals(sender, _mapLogWatcher))
            {
                return;
            }

            SetRaidActive(false);
            _logVerifiedGameMode = null;
            _unscopedQuestLogEvents.Clear();
            StopCollectiveAtlasRelay();
            _compassOverlay.ClearHeading();
            _learnedRouteGraph.BreakContinuity();
            _lastLearnedRouteObservationAt = 0;
            SetStatus(
                "Hors raid · overlay masqué automatiquement",
                Color.FromArgb(145, 153, 160));
        });
    }

    private void NavigateToDetectedMap(EftMapRoute map, bool startsNewRaid = false)
    {
        if (startsNewRaid)
        {
            SetRaidActive(true);
            _currentMap = map.NormalizedName;
            SwitchCollectiveAtlasMap(_currentMap);
            StartLearnedRouteSession(_currentMap);
            ResetExtractionGuidance(clearObservation: true);
        }

        _pendingDetectedMap = map;
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        if (_navigationInProgress
            && IsCurrentSourceForMap(_activeNavigationUri, map))
        {
            // The correct map is already loading. The pending marker is kept so
            // completion restores the current raid state exactly once.
            return;
        }

        var source = _webView.Source;
        if (_webReady
            && IsCurrentSourceForMap(source, map))
        {
            _pendingDetectedMap = null;
            UpdateCurrentMapFromSource();
            return;
        }

        if (_offlineMapMode)
        {
            NavigateToOfflineMap(map, "mode offline");
            return;
        }

        SetStatus($"Carte détectée · {map.NormalizedName}", Color.FromArgb(88, 168, 138));
        BeginWebNavigation(_webView.CoreWebView2, map.Url.AbsoluteUri);
    }

    private void BeginWebNavigation(CoreWebView2 core, string url)
    {
        if (_localMapHost.IsAttached && RaidPilotLocalMapHost.TryExtractMapKey(url, out var localFirstMap))
            url = RaidPilotLocalMapHost.GetMapUrl(localFirstMap);

        if (!WebNavigationPolicy.TryGetAllowedMapUri(url, out _))
        {
            throw new ArgumentException("Only tarkov.dev map URLs can be opened.", nameof(url));
        }

        var previousStartPending = _navigationStartPending;
        var previousInProgress = _navigationInProgress;
        var previousWebReady = _webReady;

        _navigationStartPending = true;
        _navigationInProgress = true;
        _webReady = false;
        try
        {
            core.Navigate(url);
        }
        catch
        {
            _navigationStartPending = previousStartPending;
            _navigationInProgress = previousInProgress;
            _webReady = previousWebReady;
            throw;
        }
    }

    private bool TryApplyPendingDetectedMap()
    {
        var pendingMap = _pendingDetectedMap;
        if (pendingMap is null)
        {
            return false;
        }

        var source = _webView.Source;
        if (_webReady
            && IsCurrentSourceForMap(source, pendingMap))
        {
            _pendingDetectedMap = null;
            return false;
        }

        if (!_navigationInProgress)
        {
            NavigateToDetectedMap(pendingMap);
        }
        return true;
    }

    private async Task RetryPendingDetectedMapAsync(EftMapRoute expectedMap)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1500), _formLifetime.Token);
        }
        catch (OperationCanceledException) when (_formLifetime.IsCancellationRequested)
        {
            return;
        }

        RunOnUiThread(() =>
        {
            if (_pendingDetectedMap == expectedMap && !_navigationInProgress)
            {
                NavigateToDetectedMap(expectedMap);
            }
        });
    }

    private void ToggleOverlayVisibility()
    {
        if (Visible)
        {
            SetManualOverlayHidden(true);
        }
        else
        {
            SetManualOverlayHidden(false);
        }
    }

    private bool TrySetStreamVisibility(
        bool visibleInScreenCaptures,
        bool persistPreference,
        bool showSuccess)
    {
        if (!IsHandleCreated)
        {
            return false;
        }

        var affinity = OverlayCaptureVisibility.GetDisplayAffinity(visibleInScreenCaptures);
        if (!NativeMethods.SetWindowDisplayAffinity(Handle, affinity))
        {
            var error = new Win32Exception();
            _streamVisibilityMenuItem.Checked = _visibleInScreenCaptures;
            _trayIcon.ShowBalloonTip(
                5000,
                "RaidPilot — partage Discord",
                $"Windows n'a pas pu changer la visibilité de la carte (erreur {error.NativeErrorCode}). Le réglage précédent est conservé.",
                ToolTipIcon.Warning);
            return false;
        }

        _visibleInScreenCaptures = visibleInScreenCaptures;
        _streamVisibilityMenuItem.Checked = visibleInScreenCaptures;

        // Discard frames straddling the compositor-affinity transition. The
        // visual tracker captures EFT directly, but this keeps future capture
        // backends from ever comparing one frame from each visibility mode.
        _visualMotionGate.Reset();
        _visualKeyframeRecognizer.CancelPendingMatch();
        _superKeyframeRecognizer.CancelPendingMatch();
        _pendingVisualKeyframeLearning = null;

        if (persistPreference
            && !OverlayPreferencesStore.TrySaveVisibleInCaptures(visibleInScreenCaptures))
        {
            _trayIcon.ShowBalloonTip(
                5000,
                "RaidPilot — partage Discord",
                "Le changement fonctionne pour cette session, mais RaidPilot n'a pas pu l'enregistrer pour le prochain démarrage.",
                ToolTipIcon.Warning);
            return true;
        }

        if (showSuccess)
        {
            _trayIcon.ShowBalloonTip(
                3500,
                "RaidPilot — partage Discord",
                visibleInScreenCaptures
                    ? "La carte est maintenant visible dans ton partage d'écran Discord."
                    : "La carte est maintenant masquée dans ton partage d'écran Discord.",
                ToolTipIcon.Info);
        }

        return true;
    }

    private void SetClickThrough(bool enabled, bool activateWhenInteractive)
    {
        if (enabled && _freeLayout.HasValue && !_applyingInteractiveFrame)
        {
            PersistCurrentFreeLayout();
        }

        _clickThrough = enabled;
        if (!enabled)
        {
            _manualOverlayHidden = false;
        }
        SetInteractiveWindowFrame(!enabled);
        ApplyClickThroughStyle();
        _ = ApplyWebInteractionStateAsync();

        if (enabled)
        {
            if (Visible)
            {
                Hide();
            }
            ApplyAutomaticOverlayVisibility();
            return;
        }

        if (!Visible)
        {
            Show();
        }
        _compassOverlay.SetCompassVisible(false);
        if (activateWhenInteractive)
        {
            Activate();
            _webView.Focus();
        }
    }

    private void SetInteractiveWindowFrame(bool interactive)
    {
        var desired = interactive
            ? FormBorderStyle.SizableToolWindow
            : FormBorderStyle.None;
        if (FormBorderStyle == desired)
        {
            return;
        }

        var currentBounds = Bounds;
        _applyingInteractiveFrame = true;
        try
        {
            FormBorderStyle = desired;
            var dpi = DeviceDpi > 0 ? DeviceDpi : OverlayLayoutCalculator.LogicalDpi;
            MinimumSize = interactive
                ? new Size(
                    (int)Math.Round(
                        OverlayLayoutCalculator.MinimumFreeWidthDip * dpi
                            / (double)OverlayLayoutCalculator.LogicalDpi),
                    (int)Math.Round(
                        OverlayLayoutCalculator.MinimumFreeHeightDip * dpi
                            / (double)OverlayLayoutCalculator.LogicalDpi))
                : Size.Empty;
            Bounds = currentBounds;
        }
        finally
        {
            _applyingInteractiveFrame = false;
        }
    }

    private void OnInteractiveResize(object? sender, EventArgs e)
    {
        if (_clickThrough || _applyingInteractiveFrame || !Visible)
        {
            return;
        }

        UpdateInteractiveContentScale();
    }

    private void OnInteractiveMoveOrResizeEnd(object? sender, EventArgs e)
    {
        if (_clickThrough || _applyingInteractiveFrame || !Visible)
        {
            return;
        }

        PersistCurrentFreeLayout();
        RefreshMapCornerMenu();
    }

    private void UpdateInteractiveContentScale()
    {
        var gameScreen = FindGameScreen(out var gameWindowDpi);
        var targetScreen = gameScreen ?? Screen.PrimaryScreen;
        var workingArea = gameScreen?.WorkingArea
            ?? targetScreen?.WorkingArea
            ?? SystemInformation.WorkingArea;
        var effectiveDpi = ResolveEffectiveDpi(targetScreen, gameWindowDpi);
        var referenceBounds = OverlayLayoutCalculator.CalculateBounds(
            workingArea,
            effectiveDpi,
            _questContextVisible,
            requestedScale: 1.0,
            corner: OverlayCorner.TopLeft);

        _effectiveMapScale = OverlayLayoutCalculator.CalculateContentScale(
            Bounds,
            referenceBounds);
        ApplyMapScaleToWebView();
    }

    private void PersistCurrentFreeLayout()
    {
        var workingArea = GetTargetWorkingArea();
        _freeLayout = OverlayLayoutCalculator.NormalizeFreeLayout(Bounds, workingArea);
        OverlayPreferencesStore.TrySaveFreeLayout(_freeLayout);
        PositionOverlay();
    }

    private async Task ApplyWebInteractionStateAsync()
    {
        var core = _webView.CoreWebView2;
        if (core is null || !_webReady || _formLifetime.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var interactive = _clickThrough ? "false" : "true";
            await core.ExecuteScriptAsync(
                $"document.documentElement.classList.toggle('raidpilot-interactive', {interactive}); void 0;");
        }
        catch
        {
            // A navigation can replace the document between the hotkey and this
            // call. NavigationCompleted reapplies the authoritative state.
        }
    }

    private void ApplyClickThroughStyle()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        var current = NativeMethods.GetWindowLongPtr(Handle, NativeMethods.GwlExStyle).ToInt64();
        current |= NativeMethods.WsExToolWindow;
        if (_clickThrough)
        {
            current |= NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate;
        }
        else
        {
            current &= ~(NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate);
        }

        NativeMethods.SetWindowLongPtr(Handle, NativeMethods.GwlExStyle, new nint(current));
        NativeMethods.SetWindowPos(
            Handle,
            NativeMethods.HwndTopMost,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove
            | NativeMethods.SwpNoSize
            | NativeMethods.SwpNoActivate
            | NativeMethods.SwpFrameChanged);
    }

    private void ShowNoActivate(bool force = false)
    {
        if (!force
            && _clickThrough
            && (_manualOverlayHidden || !_raidActive || _lootUiVisible))
        {
            return;
        }

        // Reissuing SW_SHOW/SWP_SHOWWINDOW against a visible WebView2 window can
        // briefly force a black composition frame over a fullscreen game.
        if (Visible)
        {
            return;
        }

        PositionOverlay();
        Show();
        NativeMethods.ShowWindow(Handle, NativeMethods.SwShowNoActivate);
        ReassertTopMost(showWindow: true);
    }

    private void ReassertTopMost(bool showWindow = false)
    {
        if (!IsHandleCreated || (!Visible && !showWindow))
        {
            return;
        }

        var flags = NativeMethods.SwpNoMove
            | NativeMethods.SwpNoSize
            | NativeMethods.SwpNoActivate;
        if (showWindow)
        {
            flags |= NativeMethods.SwpShowWindow;
        }

        NativeMethods.SetWindowPos(
            Handle,
            NativeMethods.HwndTopMost,
            0,
            0,
            0,
            0,
            flags);
    }

    private void PositionOverlay(int? effectiveDpiOverride = null)
    {
        var gameScreen = FindGameScreen(out var gameWindowDpi);
        var targetScreen = gameScreen ?? Screen.PrimaryScreen;
        var workingArea = gameScreen?.WorkingArea
            ?? targetScreen?.WorkingArea
            ?? SystemInformation.WorkingArea;
        var effectiveDpi = effectiveDpiOverride
            ?? ResolveEffectiveDpi(targetScreen, gameWindowDpi);

        if (_freeLayout is { } freeLayout)
        {
            var freeBounds = OverlayLayoutCalculator.CalculateFreeBounds(
                workingArea,
                effectiveDpi,
                freeLayout);
            var referenceBounds = OverlayLayoutCalculator.CalculateBounds(
                workingArea,
                effectiveDpi,
                _questContextVisible,
                requestedScale: 1.0,
                corner: OverlayCorner.TopLeft);

            _effectiveMapScale = OverlayLayoutCalculator.CalculateContentScale(
                freeBounds,
                referenceBounds);
            Bounds = freeBounds;
        }
        else
        {
            var requestedScale = _mapScalePercent is { } manualPercent
                ? manualPercent / 100.0
                : OverlayLayoutCalculator.CalculateAutomaticScale(
                    workingArea,
                    effectiveDpi,
                    _questContextVisible);

            _effectiveMapScale = OverlayLayoutCalculator.ResolveScale(
                workingArea,
                effectiveDpi,
                _questContextVisible,
                requestedScale);

            Bounds = OverlayLayoutCalculator.CalculateBounds(
                workingArea,
                effectiveDpi,
                _questContextVisible,
                requestedScale,
                _mapCorner);
        }

        ApplyMapScaleToWebView();
    }

    private static Rectangle GetTargetWorkingArea()
    {
        var gameScreen = FindGameScreen(out _);
        var targetScreen = gameScreen ?? Screen.PrimaryScreen;
        return gameScreen?.WorkingArea
            ?? targetScreen?.WorkingArea
            ?? SystemInformation.WorkingArea;
    }

    private void ApplyMapScaleToWebView()
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            _webView.ZoomFactor = Math.Clamp(_effectiveMapScale, 0.25, 5.0);
        }
        catch (InvalidOperationException)
        {
            // WebView2 can briefly reject zoom changes while its controller is
            // being recreated for a DPI transition. PositionOverlay will be
            // called again by the normal DPI/window lifecycle.
        }
    }

    private int ResolveEffectiveDpi(Screen? targetScreen, uint gameWindowDpi)
    {
        if (IsHandleCreated
            && targetScreen is not null
            && string.Equals(
                Screen.FromHandle(Handle).DeviceName,
                targetScreen.DeviceName,
                StringComparison.OrdinalIgnoreCase)
            && DeviceDpi > 0)
        {
            return DeviceDpi;
        }

        if (gameWindowDpi > 0)
        {
            return checked((int)gameWindowDpi);
        }

        var systemDpi = NativeMethods.GetDpiForSystem();
        return systemDpi > 0
            ? checked((int)systemDpi)
            : OverlayLayoutCalculator.LogicalDpi;
    }

    private static Screen? FindGameScreen(out uint effectiveDpi)
    {
        effectiveDpi = 0;
        foreach (var processName in new[] { "EscapeFromTarkov", "EscapeFromTarkov_BE" })
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(processName);
            }
            catch
            {
                continue;
            }

            foreach (var process in processes)
            {
                using (process)
                {
                    try
                    {
                        if (process.MainWindowHandle != nint.Zero)
                        {
                            effectiveDpi = NativeMethods.GetDpiForWindow(process.MainWindowHandle);
                            return Screen.FromHandle(process.MainWindowHandle);
                        }
                    }
                    catch
                    {
                        // Process may exit between enumeration and handle lookup.
                    }
                }
            }
        }

        return null;
    }

    private static string ResolveWritableUserDataFolder()
    {
        var portableFolder = Path.Combine(AppContext.BaseDirectory, "UserData");
        if (CanWriteDirectory(portableFolder))
        {
            return portableFolder;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RaidPilot",
            "WebView2");
    }

    private static bool CanWriteDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probePath = Path.Combine(path, $".raidpilot-write-{Guid.NewGuid():N}.tmp");
            using var probe = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private void SetStatus(string text, Color? color = null)
    {
        const int notifyIconTextLimit = 63;
        var tooltip = $"RaidPilot — {text}";
        _trayIcon.Text = tooltip.Length <= notifyIconTextLimit
            ? tooltip
            : tooltip[..notifyIconTextLimit];
    }

    private void RefreshPositionKeyMenu()
    {
        _screenshotBinding = EftScreenshotBindingReader.Read();
        _positionKeyMenuItem.Text = _screenshotBinding.Status switch
        {
            EftScreenshotBindingStatus.Ready => $"Touche de position : {_screenshotBinding.UserLabel}",
            EftScreenshotBindingStatus.NeedsRebind => "Configurer la touche de position (F8 conseillé)…",
            EftScreenshotBindingStatus.Unbound => "Configurer la touche de position…",
            _ => "Touche de position : réglage introuvable…",
        };
    }

    private void ShowPositionKeyHelp()
    {
        RefreshPositionKeyMenu();

        var message = _screenshotBinding.IsReady
            ? $"Ta touche de position est {_screenshotBinding.UserLabel}.\n\n"
                + $"Au début du raid, appuie deux fois sur O puis une fois sur {_screenshotBinding.UserLabel} pendant que la liste reste visible. RaidPilot recalera la position et lira localement les sorties normales."
            : "Réglage unique dans Tarkov :\n\n"
                + "Paramètres → Commandes → Capture d’écran → attribue F8.\n\n"
                + "Au début du raid : O deux fois, puis F8 pendant que la liste est visible. RaidPilot ne simule aucune touche; le panneau est lu localement et jamais envoyé.";

        MessageBox.Show(
            message,
            "Touche de position RaidPilot",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void ShowQuestScanHelp()
    {
        RefreshPositionKeyMenu();
        var key = _screenshotBinding.IsReady ? _screenshotBinding.UserLabel : "F8";
        MessageBox.Show(
            $"Dans le raid, ouvre l'onglet TÂCHES afin que les noms de quêtes soient visibles, puis appuie sur {key}.\n\n"
            + "RaidPilot lit les noms localement et ajoute seulement leurs objectifs géolocalisés sur la carte. "
            + "Si la liste défile, prends une deuxième capture après l'avoir descendue. Les quêtes reconnues s'additionnent pour ce raid.",
            "Quêtes sur la carte — RaidPilot",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void AnnouncePositionKey()
    {
        var message = _screenshotBinding.IsReady
            ? $"Suivi fluide prêt : {_screenshotBinding.UserLabel} donne une ancre exacte et corrige la dérive."
            : "Dans Tarkov, règle Commandes → Capture d’écran sur F8. Ensuite F8 recalera ta position.";

        _trayIcon.ShowBalloonTip(
            7000,
            "RaidPilot — localisation",
            message,
            _screenshotBinding.IsReady ? ToolTipIcon.Info : ToolTipIcon.Warning);
    }

    private void RunOnUiThread(Action action)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }
        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
                // The native handle can disappear between the checks during exit.
            }
        }
        else
        {
            action();
        }
    }

    private async Task ExitAsync()
    {
        _allowClose = true;
        await LeaveSquadAsync(clearSavedRoom: false);
        _formLifetime.Cancel();
        if (_gpsEnabled)
        {
            await DisableGpsAsync();
        }
        Close();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _squadMaintenanceTimer.Stop();
        _squadMaintenanceTimer.Dispose();
        if (_squadClient is not null)
        {
            _ = _squadClient.DisposeAsync();
            _squadClient = null;
        }
        if (_webView.CoreWebView2 is { } zoomCore)
            zoomCore.WebMessageReceived -= OnMapZoomPreferenceMessage;
        DisposeCollectiveAtlas();
        DisposePrecision();
        DisposeRaidClockUi();
        DisposeAutomaticFloors();
        DisposeQuestContext();
        DisposeQuestLog();
        _realtimeTimer.Stop();
        _realtimeTimer.Dispose();
        SaveLearnedRouteGraphForShutdown();
        _learnedRouteGraph.EndSession();
        _visualMotionGate.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _formLifetime.Cancel();
        _extractionScanCancellation?.Cancel();
        _questScanCancellation?.Cancel();
        _raidTimerScanCancellation?.Cancel();
        _screenshotWatcher?.Dispose();
        _gpsClient?.Abort();
        _gpsClient = null;
        if (_mapLogWatcher is not null)
        {
            _mapLogWatcher.MapDetected -= OnMapDetected;
            _mapLogWatcher.GameModeDetected -= OnEftGameModeDetected;
            _mapLogWatcher.RaidEnded -= OnRaidEnded;
            _mapLogWatcher.Dispose();
        }
        _compassOverlay.Close();
        _compassOverlay.Dispose();
        _formLifetime.Dispose();
    }
}
