using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RaidPilot.Overlay;

/// <summary>
/// Envoie uniquement le raccourci de capture que l'utilisateur a lui-même
/// configuré dans EFT. L'appel est utilisé au repos pour obtenir un point exact
/// sans conserver le PNG généré automatiquement.
/// </summary>
internal static class ScreenshotHotkeySender
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;

    internal static bool CanSend(EftScreenshotBindingInfo binding)
    {
        return binding.IsReady
            && binding.AxisName is null
            && binding.KeyCodes.Count is > 0 and <= 4
            && binding.KeyCodes.All(code => EftControlSettings.TryMapKeyCode(code, out var key)
                && IsKeyboardKey(key));
    }

    /// <summary>
    /// Injecting a shortcut that includes a held modifier could release
    /// the player's sprint/lean/fire key. Allow mobile capture only for a
    /// single dedicated function key already bound to EFT's Screenshot.
    /// </summary>
    internal static bool CanSendWhileMoving(EftScreenshotBindingInfo binding) =>
        CanSend(binding)
        && binding.KeyCodes.Count == 1
        && EftControlSettings.TryMapKeyCode(binding.KeyCodes[0], out var key)
        && key >= Keys.F6 && key <= Keys.F12;

    internal static bool TrySend(EftScreenshotBindingInfo binding)
    {
        if (!CanSend(binding))
        {
            return false;
        }

        var keys = binding.KeyCodes
            .Select(code => EftControlSettings.TryMapKeyCode(code, out var key) ? key : Keys.None)
            .Where(key => key != Keys.None)
            .Distinct()
            .OrderByDescending(IsModifier)
            .ToArray();
        if (keys.Length == 0)
        {
            return false;
        }

        var inputs = new Input[keys.Length * 2];
        for (var index = 0; index < keys.Length; index++)
        {
            inputs[index] = CreateKeyInput(keys[index], keyUp: false);
            inputs[inputs.Length - 1 - index] = CreateKeyInput(keys[index], keyUp: true);
        }

        try
        {
            return SendInput(
                (uint)inputs.Length,
                inputs,
                Marshal.SizeOf<Input>()) == inputs.Length;
        }
        catch (Exception exception) when (exception is DllNotFoundException
            or EntryPointNotFoundException
            or TypeLoadException)
        {
            return false;
        }
    }

    private static bool IsModifier(Keys key) => key is Keys.LControlKey
        or Keys.RControlKey
        or Keys.ControlKey
        or Keys.LShiftKey
        or Keys.RShiftKey
        or Keys.ShiftKey
        or Keys.LMenu
        or Keys.RMenu
        or Keys.Menu
        or Keys.LWin
        or Keys.RWin;

    private static bool IsKeyboardKey(Keys key)
    {
        var keyCode = key & Keys.KeyCode;
        return keyCode != Keys.None
            && keyCode is not Keys.LButton
                and not Keys.RButton
                and not Keys.MButton
                and not Keys.XButton1
                and not Keys.XButton2;
    }

    private static Input CreateKeyInput(Keys key, bool keyUp)
    {
        return new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = (ushort)key,
                    Flags = keyUp ? KeyEventKeyUp : 0,
                },
            },
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint numberOfInputs,
        [In] Input[] inputs,
        int inputSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        internal uint Type;
        internal InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        internal KeyboardInput Keyboard;

        [FieldOffset(0)]
        internal MouseInput Mouse;

        [FieldOffset(0)]
        internal HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        internal ushort VirtualKey;
        internal ushort ScanCode;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        internal int X;
        internal int Y;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        internal uint Message;
        internal ushort ParameterLow;
        internal ushort ParameterHigh;
    }
}
