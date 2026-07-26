using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Pos.App.Helpers;

/// <summary>
/// Paints the native title bar in the app's dark palette.
///
/// WPF can't style the title bar — it belongs to the window manager — so this goes through
/// DWM. Windows 11 (build 22000+) accepts explicit caption/text/border colours; older builds
/// only understand the immersive dark-mode flag and simply reject the rest, which is why
/// every call is best-effort. Colours are read from the app resources so the title bar
/// follows the palette instead of hard-coding it a second time.
/// </summary>
public static class WindowTheme
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void ApplyDarkTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        Set(hwnd, DwmwaUseImmersiveDarkMode, 1);

        // Blend the caption into the window body rather than leaving the system's dark grey.
        if (Brush("WindowBg") is { } caption) Set(hwnd, DwmwaCaptionColor, ColorRef(caption));
        if (Brush("TextPrimary") is { } text) Set(hwnd, DwmwaTextColor, ColorRef(text));
        if (Brush("Border") is { } border) Set(hwnd, DwmwaBorderColor, ColorRef(border));
    }

    private static Color? Brush(string key) =>
        (Application.Current?.TryFindResource(key) as SolidColorBrush)?.Color;

    /// <summary>DWM wants a COLORREF: 0x00BBGGRR, not WPF's ARGB.</summary>
    private static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    private static void Set(IntPtr hwnd, int attribute, int value)
    {
        try
        {
            DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
        }
        catch (DllNotFoundException) { /* no DWM — nothing to theme */ }
        catch (EntryPointNotFoundException) { }
    }
}
