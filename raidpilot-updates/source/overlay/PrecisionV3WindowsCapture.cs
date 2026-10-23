using System.Runtime.InteropServices;

namespace RaidPilot.Overlay;

/// <summary>
/// User-enabled Windows.Graphics.Capture adapter. All captured pixels stay
/// local and are used only for RaidPilot's 160x90 motion observations.
/// The native bridge is loaded only after explicit selection in the tray.
/// </summary>
internal enum WgcFrameStatus { Ready, Pending, Unavailable }

internal readonly record struct WgcCapturedFrame(
    byte[] Luminance, int ContentWidth, int ContentHeight);

internal sealed class PrecisionV3WindowsCapture : IDisposable
{
    private const string Library = "RaidPilot.Capture.Native.dll";
    internal const int Width = 160;
    internal const int Height = 90;
    internal const int PixelCount = Width * Height;

    [DllImport(Library, EntryPoint = "rp_wgc_version",
        ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern int Version();

    [DllImport(Library, EntryPoint = "rp_wgc_start",
        ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint Start(nint window);

    [DllImport(Library, EntryPoint = "rp_wgc_poll",
        ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern int Poll(
        nint handle, [Out] byte[] destination, int capacity,
        out ulong sequence);

    [DllImport(Library, EntryPoint = "rp_wgc_poll_geometry",
        ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern int PollWithGeometry(
        nint handle, [Out] byte[] destination, int capacity,
        out ulong sequence, out uint contentWidth, out uint contentHeight);

    [DllImport(Library, EntryPoint = "rp_wgc_stop",
        ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern void Stop(nint handle);

    private readonly object _gate = new();
    private nint _handle;
    private nint _window;
    private bool _disposed;
    private bool _unavailable;
    private string _status = "désactivé";

    internal string Status
    {
        get { lock (_gate) return _status; }
    }

    internal WgcFrameStatus Read(nint gameWindow, out byte[] luminance)
    {
        var status = ReadWithGeometry(gameWindow, out var frame);
        luminance = status == WgcFrameStatus.Ready ? frame.Luminance : [];
        return status;
    }

    internal WgcFrameStatus ReadWithGeometry(
        nint gameWindow, out WgcCapturedFrame captured)
    {
        captured = default;
        if (gameWindow == nint.Zero) return WgcFrameStatus.Unavailable;
        lock (_gate)
        {
            if (_disposed || _unavailable) return WgcFrameStatus.Unavailable;
            try
            {
                if (_handle != nint.Zero && _window != gameWindow)
                    StopCurrent();

                if (_handle == nint.Zero)
                {
                    if (Version() != 2)
                    {
                        _unavailable = true;
                        _status = "protocole du module graphique incompatible";
                        return WgcFrameStatus.Unavailable;
                    }
                    _handle = Start(gameWindow);
                    if (_handle == nint.Zero)
                    {
                        _unavailable = true;
                        _status = "Windows ne permet pas la capture de cette fenêtre";
                        return WgcFrameStatus.Unavailable;
                    }
                    _window = gameWindow;
                    _status = "capture démarrée — recherche de trames";
                }

                var buffer = new byte[PixelCount];
                var response = PollWithGeometry(
                    _handle, buffer, buffer.Length, out _,
                    out var contentWidth, out var contentHeight);
                if (response == 1)
                {
                    if (contentWidth < Width || contentHeight < Height
                        || contentWidth > 16384 || contentHeight > 16384)
                    {
                        _unavailable = true;
                        _status = "dimensions WGC non valides";
                        StopCurrent();
                        return WgcFrameStatus.Unavailable;
                    }
                    captured = new WgcCapturedFrame(
                        buffer, (int)contentWidth, (int)contentHeight);
                    _status = "WGC/Direct3D11 actif · "
                        + contentWidth + "x" + contentHeight;
                    return WgcFrameStatus.Ready;
                }
                if (response == 0) return WgcFrameStatus.Pending;

                _unavailable = true;
                _status = "la capture WGC n'a pas renvoyé de trame valide";
                StopCurrent();
                return WgcFrameStatus.Unavailable;
            }
            catch (Exception error) when (error is DllNotFoundException
                or EntryPointNotFoundException
                or BadImageFormatException
                or SEHException
                or TypeLoadException)
            {
                _unavailable = true;
                _status = "module de capture indisponible : "
                    + error.GetType().Name;
                StopCurrent();
                return WgcFrameStatus.Unavailable;
            }
        }
    }

    internal void Reset(bool allowRetry = false)
    {
        lock (_gate)
        {
            StopCurrent();
            if (allowRetry)
            {
                _unavailable = false;
                _status = "nouvelle tentative autorisée";
            }
        }
    }

    private void StopCurrent()
    {
        var handle = _handle;
        _handle = nint.Zero;
        _window = nint.Zero;
        if (handle == nint.Zero) return;
        try { Stop(handle); }
        catch (Exception error) when (error is DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException or SEHException)
        {
            _status = "fermeture de la capture : " + error.GetType().Name;
            _unavailable = true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            StopCurrent();
        }
    }
}
