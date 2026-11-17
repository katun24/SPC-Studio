using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace SpcStudio.Theme;

// Asks Windows to draw a window's title bar in its dark palette.
public static class DarkTitleBar
{
    private const int DwmUseImmersiveDarkModeAttribute = 20;
    private const int DarkModeOn = 1;

    // Darkens a window's title bar on Windows, and does nothing elsewhere or where the window has no handle yet.
    public static void Apply(Window window)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        int darkModeOn = DarkModeOn;
        DwmSetWindowAttribute(handle, DwmUseImmersiveDarkModeAttribute, ref darkModeOn, sizeof(int));
    }

    // Sets one of the desktop window manager's attributes on a window.
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
