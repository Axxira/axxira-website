using System.Runtime.InteropServices;

namespace RaidPilot.Overlay;

/// <summary>
/// Reçoit les paquets Raw Input de souris et de clavier. Les paquets sont
/// décodés en mémoire puis consommés immédiatement par OverlayForm; aucune
/// entrée n'est enregistrée sur disque.
/// </summary>
internal static class RawMouseInput
{
    // RAWINPUT souris/clavier tient largement dans ce tampon. Le conserver
    // évite deux allocations par paquet sur les souris à haut polling rate.
    private const int BufferCapacity = 256;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmSysKeyDown = 0x0104;
    private const uint WmSysKeyUp = 0x0105;

    private static nint _buffer;

    internal static bool MouseRegistered { get; private set; }

    internal static bool KeyboardRegistered { get; private set; }

    internal static bool Register(nint window)
    {
        if (_buffer == nint.Zero)
        {
            _buffer = Marshal.AllocHGlobal(BufferCapacity);
        }

        var mouse = CreateDevice(
            NativeMethods.HidUsageGenericMouse,
            NativeMethods.RidevInputSink,
            window);
        var keyboard = CreateDevice(
            NativeMethods.HidUsageGenericKeyboard,
            NativeMethods.RidevInputSink,
            window);
        var deviceSize = (uint)Marshal.SizeOf<NativeMethods.RawInputDevice>();

        if (NativeMethods.RegisterRawInputDevices([mouse, keyboard], 2, deviceSize))
        {
            MouseRegistered = true;
            KeyboardRegistered = true;
            return true;
        }

        // Une pile HID atypique peut refuser une des deux classes. Garder
        // l'autre active est plus sûr que de perdre à la fois le cap souris
        // et la chronologie clavier.
        MouseRegistered = NativeMethods.RegisterRawInputDevices([mouse], 1, deviceSize);
        KeyboardRegistered = NativeMethods.RegisterRawInputDevices([keyboard], 1, deviceSize);
        if (MouseRegistered || KeyboardRegistered)
        {
            return true;
        }

        Marshal.FreeHGlobal(_buffer);
        _buffer = nint.Zero;
        return false;
    }

    internal static void Unregister()
    {
        var devices = new List<NativeMethods.RawInputDevice>(2);
        if (MouseRegistered)
        {
            devices.Add(CreateDevice(
                NativeMethods.HidUsageGenericMouse,
                NativeMethods.RidevRemove,
                nint.Zero));
        }
        if (KeyboardRegistered)
        {
            devices.Add(CreateDevice(
                NativeMethods.HidUsageGenericKeyboard,
                NativeMethods.RidevRemove,
                nint.Zero));
        }

        try
        {
            if (devices.Count > 0)
            {
                _ = NativeMethods.RegisterRawInputDevices(
                    devices.ToArray(),
                    (uint)devices.Count,
                    (uint)Marshal.SizeOf<NativeMethods.RawInputDevice>());
            }
        }
        finally
        {
            MouseRegistered = false;
            KeyboardRegistered = false;
            var buffer = Interlocked.Exchange(ref _buffer, nint.Zero);
            if (buffer != nint.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    internal static bool TryReadPacket(nint rawInputHandle, out RawInputPacket packet)
    {
        packet = default;
        var buffer = _buffer;
        if (buffer == nint.Zero)
        {
            return false;
        }

        var headerSize = (uint)Marshal.SizeOf<NativeMethods.RawInputHeader>();
        uint dataSize = BufferCapacity;
        var read = NativeMethods.GetRawInputData(
            rawInputHandle,
            NativeMethods.RidInput,
            buffer,
            ref dataSize,
            headerSize);
        if (read == uint.MaxValue
            || read < headerSize
            || read > BufferCapacity)
        {
            return false;
        }

        var header = Marshal.PtrToStructure<NativeMethods.RawInputHeader>(buffer);
        if (header.Size > read || header.Size < headerSize)
        {
            return false;
        }

        var payload = nint.Add(buffer, Marshal.SizeOf<NativeMethods.RawInputHeader>());
        if (header.Type == NativeMethods.RimTypeMouse)
        {
            var mouseSize = (uint)Marshal.SizeOf<NativeMethods.RawMouse>();
            if (read < headerSize + mouseSize
                || header.Size < headerSize + mouseSize)
            {
                return false;
            }

            var mouse = Marshal.PtrToStructure<NativeMethods.RawMouse>(payload);
            if ((mouse.Flags & NativeMethods.MouseMoveAbsolute) != 0)
            {
                return false;
            }

            var wheelDelta = (mouse.ButtonFlags & NativeMethods.RiMouseWheel) != 0
                ? mouse.ButtonData
                : 0;
            packet = RawInputPacket.ForMouse(
                mouse.LastX,
                mouse.LastY,
                wheelDelta,
                mouse.ButtonFlags);
            return mouse.LastX != 0
                || mouse.LastY != 0
                || wheelDelta != 0
                || mouse.ButtonFlags != 0;
        }

        if (header.Type != NativeMethods.RimTypeKeyboard)
        {
            return false;
        }

        var keyboardSize = (uint)Marshal.SizeOf<NativeMethods.RawKeyboard>();
        if (read < headerSize + keyboardSize
            || header.Size < headerSize + keyboardSize)
        {
            return false;
        }

        var keyboard = Marshal.PtrToStructure<NativeMethods.RawKeyboard>(payload);
        if (keyboard.VirtualKey == byte.MaxValue
            || keyboard.Message is not (WmKeyDown or WmKeyUp or WmSysKeyDown or WmSysKeyUp))
        {
            return false;
        }

        var key = NormalizeKeyboardKey(
            keyboard.VirtualKey,
            keyboard.MakeCode,
            keyboard.Flags);
        if (key == Keys.None)
        {
            return false;
        }

        packet = RawInputPacket.ForKeyboard(
            key,
            (keyboard.Flags & NativeMethods.RiKeyBreak) == 0);
        return true;
    }

    internal static bool TryReadDelta(nint rawInputHandle, out int deltaX, out int deltaY)
    {
        return TryReadMotion(rawInputHandle, out deltaX, out deltaY, out _);
    }

    internal static bool TryReadMotion(
        nint rawInputHandle,
        out int deltaX,
        out int deltaY,
        out int wheelDelta)
    {
        return TryReadMotion(
            rawInputHandle,
            out deltaX,
            out deltaY,
            out wheelDelta,
            out _);
    }

    internal static bool TryReadMotion(
        nint rawInputHandle,
        out int deltaX,
        out int deltaY,
        out int wheelDelta,
        out ushort buttonFlags)
    {
        deltaX = 0;
        deltaY = 0;
        wheelDelta = 0;
        buttonFlags = 0;
        if (!TryReadPacket(rawInputHandle, out var packet)
            || packet.Kind != RawInputPacketKind.Mouse)
        {
            return false;
        }

        deltaX = packet.DeltaX;
        deltaY = packet.DeltaY;
        wheelDelta = packet.WheelDelta;
        buttonFlags = packet.ButtonFlags;
        return true;
    }

    private static NativeMethods.RawInputDevice CreateDevice(
        ushort usage,
        uint flags,
        nint target)
    {
        return new NativeMethods.RawInputDevice
        {
            UsagePage = NativeMethods.HidUsagePageGeneric,
            Usage = usage,
            Flags = flags,
            Target = target,
        };
    }

    internal static Keys NormalizeKeyboardKey(
        ushort virtualKey,
        ushort makeCode,
        ushort flags)
    {
        var key = (Keys)virtualKey;
        if (key == Keys.ShiftKey)
        {
            return makeCode == 0x36 ? Keys.RShiftKey : Keys.LShiftKey;
        }
        if (key == Keys.ControlKey)
        {
            return (flags & NativeMethods.RiKeyE0) != 0
                ? Keys.RControlKey
                : Keys.LControlKey;
        }
        if (key == Keys.Menu)
        {
            return (flags & NativeMethods.RiKeyE0) != 0
                ? Keys.RMenu
                : Keys.LMenu;
        }

        return key & Keys.KeyCode;
    }
}

internal enum RawInputPacketKind
{
    None,
    Mouse,
    Keyboard,
}

internal readonly record struct RawInputPacket(
    RawInputPacketKind Kind,
    int DeltaX,
    int DeltaY,
    int WheelDelta,
    ushort ButtonFlags,
    Keys Key,
    bool IsKeyDown)
{
    internal static RawInputPacket ForMouse(
        int deltaX,
        int deltaY,
        int wheelDelta,
        ushort buttonFlags) => new(
            RawInputPacketKind.Mouse,
            deltaX,
            deltaY,
            wheelDelta,
            buttonFlags,
            Keys.None,
            false);

    internal static RawInputPacket ForKeyboard(Keys key, bool isDown) => new(
        RawInputPacketKind.Keyboard,
        0,
        0,
        0,
        0,
        key,
        isDown);
}
