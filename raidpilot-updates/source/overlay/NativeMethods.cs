using System.Runtime.InteropServices;
using System.Text;

namespace RaidPilot.Overlay;

internal static class NativeMethods
{
    internal delegate bool EnumWindowsCallback(nint window, nint parameter);

    internal const int WmHotkey = 0x0312;
    internal const int WmInput = 0x00FF;
    internal const int WmNcHitTest = 0x0084;
    internal const int WmMouseActivate = 0x0021;
    internal const int GwlExStyle = -20;

    internal const long WsExTransparent = 0x00000020L;
    internal const long WsExToolWindow = 0x00000080L;
    internal const long WsExNoActivate = 0x08000000L;

    internal const int HtTransparent = -1;
    internal const int MaNoActivate = 3;

    internal const uint ModNone = 0x0000;
    internal const uint ModAlt = 0x0001;
    internal const uint ModControl = 0x0002;
    internal const uint ModNoRepeat = 0x4000;

    internal const int SwShowNoActivate = 4;
    internal const uint WdaNone = 0x00000000;
    internal const uint WdaExcludeFromCapture = 0x00000011;

    internal const uint RidInput = 0x10000003;
    internal const uint RidevRemove = 0x00000001;
    internal const uint RidevInputSink = 0x00000100;
    internal const uint RimTypeMouse = 0;
    internal const uint RimTypeKeyboard = 1;
    internal const ushort HidUsagePageGeneric = 0x01;
    internal const ushort HidUsageGenericMouse = 0x02;
    internal const ushort HidUsageGenericKeyboard = 0x06;
    internal const ushort MouseMoveAbsolute = 0x0001;
    internal const ushort RiKeyBreak = 0x0001;
    internal const ushort RiKeyE0 = 0x0002;
    internal const ushort RiMouseWheel = 0x0400;
    internal const ushort RiMouseMiddleButtonDown = 0x0010;
    internal const ushort RiMouseMiddleButtonUp = 0x0020;
    private const int CursorShowing = 0x00000001;

    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpFrameChanged = 0x0020;
    internal const uint SwpShowWindow = 0x0040;

    internal static readonly nint HwndTopMost = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RawInputDevice
    {
        internal ushort UsagePage;
        internal ushort Usage;
        internal uint Flags;
        internal nint Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RawInputHeader
    {
        internal uint Type;
        internal uint Size;
        internal nint Device;
        internal nint Parameter;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RawMouse
    {
        internal ushort Flags;
        private ushort _padding;
        private uint _buttons;
        private uint _rawButtons;
        internal int LastX;
        internal int LastY;
        private uint _extraInformation;

        internal readonly ushort ButtonFlags => (ushort)(_buttons & 0xffff);

        internal readonly short ButtonData => unchecked((short)(_buttons >> 16));
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RawKeyboard
    {
        internal ushort MakeCode;
        internal ushort Flags;
        private ushort _reserved;
        internal ushort VirtualKey;
        internal uint Message;
        private uint _extraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        internal int Size;
        internal int Flags;
        internal nint Cursor;
        internal Point ScreenPosition;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static extern nint GetWindowLongPtr(nint hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static extern nint SetWindowLongPtr(nint hWnd, int index, nint newLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        nint hWnd,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint hWnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(nint hWnd, StringBuilder text, int maximumCount);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForSystem();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterRawInputDevices(
        [In] RawInputDevice[] devices,
        uint numberOfDevices,
        uint size);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetRawInputData(
        nint rawInput,
        uint command,
        nint data,
        ref uint size,
        uint headerSize);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowDisplayAffinity(nint hWnd, uint affinity);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorInfo(ref CursorInfo cursorInfo);

    internal static bool IsCursorVisible()
    {
        var cursorInfo = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        return GetCursorInfo(ref cursorInfo) && (cursorInfo.Flags & CursorShowing) != 0;
    }
}
