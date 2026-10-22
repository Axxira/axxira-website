using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RaidPilot.Overlay.Tests;

internal static class RawMouseInputTests
{
    internal const int TestCount = 1;

    internal static void RunAll(ICollection<string> failures)
    {
        try
        {
            var expectedPointerStructSize = IntPtr.Size == 8 ? 24 : 16;
            Require(
                Marshal.SizeOf<NativeMethods.RawInputHeader>() == expectedPointerStructSize,
                "la disposition RAWINPUTHEADER ne correspond pas à Windows");
            Require(
                Marshal.SizeOf<NativeMethods.RawMouse>() == 24,
                "la disposition RAWMOUSE ne correspond pas à Windows");
            Require(
                Marshal.SizeOf<NativeMethods.RawKeyboard>() == 16,
                "la disposition RAWKEYBOARD ne correspond pas à Windows");
            Require(
                Marshal.SizeOf<NativeMethods.RawInputDevice>() == (IntPtr.Size == 8 ? 16 : 12),
                "la disposition RAWINPUTDEVICE ne correspond pas à Windows");
            Require(
                RawMouseInput.NormalizeKeyboardKey((ushort)Keys.ShiftKey, 0x2a, 0) == Keys.LShiftKey
                && RawMouseInput.NormalizeKeyboardKey((ushort)Keys.ShiftKey, 0x36, 0) == Keys.RShiftKey,
                "les touches Shift gauche/droite ne sont pas distinguées");
            Require(
                RawMouseInput.NormalizeKeyboardKey(
                    (ushort)Keys.ControlKey,
                    0x1d,
                    NativeMethods.RiKeyE0) == Keys.RControlKey,
                "une touche étendue droite n'est pas normalisée");
            Console.WriteLine("OK  disposition mémoire Raw Input Windows");
        }
        catch (Exception exception)
        {
            failures.Add($"disposition mémoire Raw Input Windows — {exception.Message}");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
