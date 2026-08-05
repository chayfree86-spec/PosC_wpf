using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pos.App.Services;

/// <summary>
/// Recolours the app's highlight (accent) from a single chosen colour.
///
/// Only the accent family moves — the green everything lights up with. The status colours a
/// bill or a table depends on to be read at a glance (a settled table's green, an occupied
/// one's blue, an error's red) are deliberately left alone: they carry meaning, not branding,
/// and repainting them from a theme picker would make the floor harder to read.
///
/// The accent brushes are <b>replaced</b> in the application resources, not mutated: WPF freezes
/// the brushes it loads from XAML, so their <c>Color</c> can't be changed in place. Every accent
/// consumer reaches them through <c>DynamicResource</c> (the ordinary status brushes stay
/// <c>StaticResource</c>), so swapping the entry here repaints all of them at once, with no
/// reload and no per-control wiring. The whole family is derived from the one base colour so a
/// blue accent gets a blue hover, a blue gradient and blue badge text, not a blue button sitting
/// in green trim.
/// </summary>
public static class ThemeService
{
    /// <summary>The green the app ships with, and the fallback whenever a stored or typed
    /// colour can't be parsed.</summary>
    public const string Default = "#00704A";

    /// <summary>
    /// Where the chosen colour is kept. A per-client setting, not a machine one: the highlight
    /// is part of a brand's identity, so Daal Roti keeps its green and Chay Chaupal its blue on
    /// the very same till, and the theme is re-applied whenever the counter changes hands.
    /// </summary>
    public const string SettingKey = "pos_accent_color";

    public sealed record Preset(string Name, string Hex);

    /// <summary>
    /// A handful of accents that read well on this dark theme and stay clear of the status
    /// palette — no red (that is error), no colour so dark it disappears on the near-black
    /// background. The first is the shipped green.
    /// </summary>
    public static readonly IReadOnlyList<Preset> Presets = new[]
    {
        new Preset("Emerald", "#00704A"),
        new Preset("Ocean",   "#2563EB"),
        new Preset("Violet",  "#7C3AED"),
        new Preset("Amber",   "#D97706"),
        new Preset("Teal",    "#0D9488"),
    };

    /// <summary>Uppercased, always with a leading '#', so a stored value compares equal to a
    /// preset's hex without worrying about case — the swatch's selected ring turns on a string
    /// match.</summary>
    public static string Normalize(string? hex)
    {
        var h = (hex ?? "").Trim();
        if (h.Length == 0) return Default;
        if (!h.StartsWith('#')) h = "#" + h;
        return h.ToUpperInvariant();
    }

    /// <summary>
    /// True when <paramref name="hex"/> is a colour WPF can parse (#RGB, #RRGGBB, #AARRGGBB, or a
    /// named colour), handing back the normalised form. Lets the custom-colour box reject a
    /// half-typed value instead of repainting the app to nothing.
    /// </summary>
    public static bool TryParse(string? hex, out string normalized)
    {
        normalized = Normalize(hex);
        try
        {
            _ = (Color)ColorConverter.ConvertFromString(normalized);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool _isDark = true;
    private static string? _activeAccentHex = Default;

    public static bool IsDark => _isDark;

    public static void SetThemeMode(bool isDark)
    {
        _isDark = isDark;
        var res = Application.Current?.Resources;
        if (res is null) return;

        if (isDark)
        {
            res["WindowBg"] = Brush(Color.FromRgb(0x0A, 0x0A, 0x0A));
            res["SidebarBg"] = Brush(Color.FromRgb(0x12, 0x12, 0x12));
            res["PanelBg"] = Brush(Color.FromRgb(0x12, 0x12, 0x12));
            res["CardBg"] = Brush(Color.FromRgb(0x1E, 0x1E, 0x1E));
            res["CardBg2"] = Brush(Color.FromRgb(0x17, 0x17, 0x17));
            res["Border"] = Brush(Color.FromRgb(0x2D, 0x2D, 0x2D));
            res["BorderSoft"] = Brush(Color.FromRgb(0x20, 0x20, 0x20));
            res["TextPrimary"] = Brush(Color.FromRgb(0xE6, 0xEA, 0xF2));
            res["TextMuted"] = Brush(Color.FromRgb(0x9C, 0xA3, 0xAF));
            res["TextFaint"] = Brush(Color.FromRgb(0x6B, 0x72, 0x80));
            res["ErrorRed"] = Brush(Color.FromRgb(0xEF, 0x44, 0x44));
        }
        else
        {
            res["WindowBg"] = Brush(Color.FromRgb(0xF3, 0xF4, 0xF6)); // Light grey (gray-100)
            res["SidebarBg"] = Brush(Color.FromRgb(0xFF, 0xFF, 0xFF)); // White
            res["PanelBg"] = Brush(Color.FromRgb(0xFF, 0xFF, 0xFF)); // White
            res["CardBg"] = Brush(Color.FromRgb(0xFF, 0xFF, 0xFF)); // White
            res["CardBg2"] = Brush(Color.FromRgb(0xF9, 0xFA, 0xFB)); // Very light grey (gray-50)
            res["Border"] = Brush(Color.FromRgb(0xE5, 0xE7, 0xEB)); // Gray border (gray-200)
            res["BorderSoft"] = Brush(Color.FromRgb(0xF3, 0xF4, 0xF6)); // Soft gray border (gray-100)
            res["TextPrimary"] = Brush(Color.FromRgb(0x11, 0x18, 0x27)); // Dark grey/black text (gray-900)
            res["TextMuted"] = Brush(Color.FromRgb(0x4B, 0x55, 0x63)); // Gray text (gray-600)
            res["TextFaint"] = Brush(Color.FromRgb(0x9C, 0xA3, 0xAF)); // Faint text (gray-400)
            res["ErrorRed"] = Brush(Color.FromRgb(0xD9, 0x30, 0x25)); // Darker red for light mode readability
        }

        // Set the AppIcon based on light/dark mode (using PNG for crisp taskbar scaling)
        try
        {
            string iconUri = isDark
                ? "pack://application:,,,/Pos.App;component/login_logo_dark.png"
                : "pack://application:,,,/Pos.App;component/login_logo_light.png";
            res["AppIcon"] = new BitmapImage(new Uri(iconUri));
        }
        catch
        {
            // Safe fallback
        }

        // Set the LoginLogo based on light/dark mode
        try
        {
            string logoUri = isDark
                ? "pack://application:,,,/Pos.App;component/login_logo_dark.png"
                : "pack://application:,,,/Pos.App;component/login_logo_light.png";
            res["LoginLogo"] = new BitmapImage(new Uri(logoUri));
        }
        catch
        {
            // Safe fallback
        }

        // Re-apply the accent using current mode logic
        Apply(_activeAccentHex);
    }

    /// <summary>
    /// Repaints the accent family from <paramref name="hex"/>. Silently no-ops if the colour
    /// can't be parsed or the resources aren't up yet — a bad theme value must never stop the
    /// app from starting.
    /// </summary>
    public static void Apply(string? hex)
    {
        var res = Application.Current?.Resources;
        if (res is null)
        {
            return;
        }

        Color baseColor;
        try
        {
            baseColor = (Color)ColorConverter.ConvertFromString(Normalize(hex));
        }
        catch
        {
            return;
        }

        _activeAccentHex = hex;

        // Selected table card border: white in dark mode, accent base color in light mode.
        res["SelectedTableBorder"] = _isDark ? Brush(Colors.White) : Brush(baseColor);

        // The main highlight, and its literal duplicate.
        res["Accent"] = Brush(baseColor);
        res["Green"] = Brush(baseColor);

        // A darker shade for pressed/secondary accents, and the 10%-alpha wash used behind
        // hovered rows and cards.
        res["AccentDim"] = Brush(_isDark ? Scale(baseColor, 0.75) : Scale(baseColor, 0.85));
        res["AccentFaint"] = Brush(WithAlpha(baseColor, _isDark ? (byte)0x1A : (byte)0x12));

        var (h, s, _) = ToHsl(baseColor);

        if (_isDark)
        {
            // Badge/label text: the base is too dark to read small on the near-black background, so
            // this is the same hue lifted to a bright, legible lightness. BlueLink is the same green
            // in the shipped theme, so it moves with it.
            var text = FromHsl(h, Math.Min(s, 0.75), 0.62);
            res["AccentText"] = Brush(text);
            res["BlueLink"] = Brush(text);

            // The solid tint behind a highlighted menu row, where a 10%-alpha wash would vanish.
            var tint = FromHsl(h, Math.Min(s, 0.55), 0.16);
            res["GreenBg"] = Brush(tint);
            res["MenuHighlight"] = Brush(tint);

            // The active sidebar item's gradient: a touch lighter into a touch darker than the base.
            res["AccentGrad"] = Gradient(Lighten(baseColor, 0.18), Scale(baseColor, 0.85));
        }
        else
        {
            // Badge/label text: darker for light theme readability.
            var text = FromHsl(h, Math.Min(s, 0.75), 0.35);
            res["AccentText"] = Brush(text);
            res["BlueLink"] = Brush(text);

            // The solid tint behind a highlighted menu row: very light green tint for light theme
            var tint = FromHsl(h, Math.Min(s, 0.55), 0.94);
            res["GreenBg"] = Brush(tint);
            res["MenuHighlight"] = Brush(tint);

            // Gradient: start is baseColor, end is slightly darker
            res["AccentGrad"] = Gradient(baseColor, Scale(baseColor, 0.88));
        }
    }


    private static SolidColorBrush Brush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();   // frozen brushes are cheaper and thread-safe; we replace, never mutate.
        return b;
    }

    private static LinearGradientBrush Gradient(Color start, Color end)
    {
        var g = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop(start, 0),
                new GradientStop(end, 1),
            },
        };
        g.Freeze();
        return g;
    }

    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    private static Color Scale(Color c, double f) => Color.FromRgb(Byte(c.R * f), Byte(c.G * f), Byte(c.B * f));

    private static Color Lighten(Color c, double amt) => Color.FromRgb(
        Byte(c.R + (255 - c.R) * amt), Byte(c.G + (255 - c.G) * amt), Byte(c.B + (255 - c.B) * amt));

    private static byte Byte(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    // ── HSL, so the derived shades keep the chosen hue instead of drifting ────────
    private static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, h = 0, s = 0, d = max - min;

        if (d > 0)
        {
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h *= 60;
        }
        return (h, s, l);
    }

    private static Color FromHsl(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = l - c / 2;
        double r = 0, g = 0, b = 0;

        if (h < 60) { r = c; g = x; }
        else if (h < 120) { r = x; g = c; }
        else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; }
        else if (h < 300) { r = x; b = c; }
        else { r = c; b = x; }

        return Color.FromRgb(Byte((r + m) * 255), Byte((g + m) * 255), Byte((b + m) * 255));
    }
}
