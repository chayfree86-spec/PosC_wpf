using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Pos.App.Converters;

/// <summary>Table status → status colour (available = green, else orange).</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Available = new((Color)ColorConverter.ConvertFromString("#FF22C55E"));
    private static readonly SolidColorBrush Occupied = new((Color)ColorConverter.ConvertFromString("#FFF97316"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(value as string, "available", StringComparison.OrdinalIgnoreCase) ? Available : Occupied;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// A table shows one of three things: free, a running order (<c>ordered</c>), or one whose
/// bill has been printed (<c>completed</c>). Older rows — and anything arriving from the
/// server — may still say occupied/confirmed/pending, so those are read as "running" rather
/// than falling through to the billed colour and turning seated tables red.
/// </summary>
internal static class TableStatuses
{
    public static bool IsFree(string s) => s is "available" or "free" or "settled" or "";

    public static bool IsRunning(string s) => s is "ordered" or "occupied" or "confirmed" or "pending" or "active" or "running";
}

/// <summary>Table status → dot indicator colour (available = green, occupied = blue, completed = red).</summary>
public sealed class StatusToDotBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush GreenDot = new((Color)ColorConverter.ConvertFromString("#FF10B981"));
    private static readonly SolidColorBrush BlueDot = new((Color)ColorConverter.ConvertFromString("#FF38BDF8"));
    private static readonly SolidColorBrush RedDot = new((Color)ColorConverter.ConvertFromString("#FFEF4444"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = (value as string)?.ToLowerInvariant() ?? "";
        if (TableStatuses.IsFree(status)) return GreenDot;
        if (TableStatuses.IsRunning(status)) return BlueDot;
        return RedDot; // completed / printed / billed
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Table status → card background colour.</summary>
public sealed class StatusToCardBgConverter : IValueConverter
{
    private static readonly Brush FreeBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF1E1E1E"));
    private static readonly Brush OccupiedBg = new LinearGradientBrush(
        (Color)ColorConverter.ConvertFromString("#FF00469B"),
        (Color)ColorConverter.ConvertFromString("#FF0061FF"),
        new Point(0, 0),
        new Point(0, 1)
    );
    private static readonly Brush CompletedBg = new LinearGradientBrush(
        (Color)ColorConverter.ConvertFromString("#FFD10000"),
        (Color)ColorConverter.ConvertFromString("#FFA30000"),
        new Point(0, 0),
        new Point(1, 1)
    );

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = (value as string)?.ToLowerInvariant() ?? "";
        if (TableStatuses.IsFree(status))
        {
            if (Application.Current?.Resources != null && Application.Current.Resources.Contains("CardBg"))
            {
                return Application.Current.Resources["CardBg"] as Brush ?? FreeBg;
            }
            return FreeBg;
        }
        if (TableStatuses.IsRunning(status)) return OccupiedBg;
        return CompletedBg; // completed
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Table status → card border colour.</summary>
public sealed class StatusToBorderBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush FreeBorder = new((Color)ColorConverter.ConvertFromString("#FF2D2D2D"));
    private static readonly SolidColorBrush OccupiedBorder = new((Color)ColorConverter.ConvertFromString("#FF2563EB"));
    private static readonly SolidColorBrush CompletedBorder = new((Color)ColorConverter.ConvertFromString("#FFDC2626"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = (value as string)?.ToLowerInvariant() ?? "";
        if (TableStatuses.IsFree(status))
        {
            if (Application.Current?.Resources != null && Application.Current.Resources.Contains("Border"))
            {
                return Application.Current.Resources["Border"] as Brush ?? FreeBorder;
            }
            return FreeBorder;
        }
        if (TableStatuses.IsRunning(status)) return OccupiedBorder;
        return CompletedBorder; // completed
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Number → "₹123".</summary>
public sealed class CurrencyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var d = value is double dv ? dv : (value is long lv ? lv : 0d);
        return "₹" + d.ToString("0.##", CultureInfo.InvariantCulture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>count &gt; 0 → Visible. Pass "invert" to flip (0 → Visible).</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double num = 0;
        if (value is int i) num = i;
        else if (value is double d) num = d;
        else if (value is float f) num = f;
        else if (value is long l) num = l;
        else if (value is decimal dec) num = (double)dec;
        else if (value is bool b) num = b ? 1 : 0;
        else if (value != null && double.TryParse(value.ToString(), out double parsed)) num = parsed;

        var visible = num > 0;
        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
        {
            visible = !visible;
        }
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>value == parameter → Visible, else Collapsed.</summary>
public sealed class StringEqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(value as string, parameter as string, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>string.IsNullOrEmpty(value) → Visible (for watermark), else Collapsed.</summary>
public sealed class StringIsNullOrEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool empty = string.IsNullOrEmpty(value as string);
        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
        {
            empty = !empty;
        }
        return empty ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Ledger entry type ("gave" / "got") → Badge colour.</summary>
public sealed class LedgerTypeToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush GaveBrush = new((Color)ColorConverter.ConvertFromString("#FFEF4444")); // Red (Gave / Udhaar)
    private static readonly SolidColorBrush GotBrush = new((Color)ColorConverter.ConvertFromString("#FF10B981"));  // Green (Got / Received)

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var type = (value as string)?.ToLowerInvariant() ?? "";
        return type == "got" || type == "credit" || type == "payment" ? GotBrush : GaveBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Table status → Time text colour (occupied = dark blue, completed = dark red, free = default muted).</summary>
public sealed class StatusToTimeBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush DefaultBrush = new((Color)ColorConverter.ConvertFromString("#FF9E9F9F"));
    private static readonly SolidColorBrush DarkBlueBrush = new((Color)ColorConverter.ConvertFromString("#FFAECBFC"));
    private static readonly SolidColorBrush DarkRedBrush = new((Color)ColorConverter.ConvertFromString("#FFFECACA"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = (value as string)?.ToLowerInvariant() ?? "";
        if (TableStatuses.IsRunning(status)) return DarkBlueBrush;
        if (TableStatuses.IsFree(status)) return DefaultBrush;
        return DarkRedBrush; // completed
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Table status → Title text color (white for occupied/completed, dynamic TextPrimary for free).</summary>
public sealed class StatusToTitleForegroundConverter : IValueConverter
{
    private static readonly Brush WhiteBrush = Brushes.White;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = (value as string)?.ToLowerInvariant() ?? "";
        if (TableStatuses.IsFree(status))
        {
            if (Application.Current?.Resources != null && Application.Current.Resources.Contains("TextPrimary"))
            {
                return Application.Current.Resources["TextPrimary"] as Brush ?? WhiteBrush;
            }
            return WhiteBrush;
        }
        return WhiteBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// A hex colour string → a brush for the custom-colour preview swatch. Anything that doesn't
/// parse (a half-typed "#2fa…") shows transparent rather than throwing, so the preview simply
/// stays blank until the value is a real colour.
/// </summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hex = (value as string ?? "").Trim();
        if (hex.Length > 0 && !hex.StartsWith('#')) hex = "#" + hex;
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return Brushes.Transparent; }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
