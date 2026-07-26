using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Pos.App.Views;

/// <summary>
/// Bill-level discount entry. Returns the type and the value the user typed; the amount
/// itself is worked out by the view model so the bill, the receipt and the stored order
/// all agree on one number.
/// </summary>
public partial class DiscountModal : Window
{
    private readonly double _subtotal;

    public string DiscountType { get; private set; } = "percentage";
    public double DiscountValue { get; private set; }

    public DiscountModal(double subtotal, string type, double value)
    {
        InitializeComponent();
        _subtotal = subtotal;
        DiscountType = string.IsNullOrWhiteSpace(type) ? "percentage" : type;
        DiscountValue = value;
        TxtValue.Text = value > 0 ? value.ToString("0.##") : "";
        UpdateTypeButtons();
        Recalculate();
        Loaded += (_, _) => { TxtValue.Focus(); TxtValue.SelectAll(); };
    }

    private void Type_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string tag)
        {
            DiscountType = tag;
            UpdateTypeButtons();
            Recalculate();
            TxtValue.Focus();
            TxtValue.SelectAll();
        }
    }

    private void UpdateTypeButtons()
    {
        var on = (Brush)Application.Current.FindResource("GreenBg");
        var onBorder = (Brush)Application.Current.FindResource("Accent");
        var onText = (Brush)Application.Current.FindResource("AccentText");
        var off = (Brush)Application.Current.FindResource("CardBg");
        var offBorder = (Brush)Application.Current.FindResource("Border");
        var offText = (Brush)Application.Current.FindResource("TextMuted");

        var percent = DiscountType == "percentage";
        BtnPercent.Background = percent ? on : off;
        BtnPercent.BorderBrush = percent ? onBorder : offBorder;
        BtnPercent.Foreground = percent ? onText : offText;
        BtnFlat.Background = percent ? off : on;
        BtnFlat.BorderBrush = percent ? offBorder : onBorder;
        BtnFlat.Foreground = percent ? offText : onText;

        TxtValueLabel.Text = percent ? "DISCOUNT %" : "DISCOUNT ₹";
    }

    private double ParsedValue =>
        double.TryParse(TxtValue.Text, out var v) && v > 0 ? v : 0;

    private double Amount
    {
        get
        {
            var raw = DiscountType == "percentage" ? _subtotal * ParsedValue / 100 : ParsedValue;
            return Math.Round(Math.Clamp(raw, 0, _subtotal), 2);
        }
    }

    private void Recalculate()
    {
        TxtSubtotal.Text = $"₹ {_subtotal:0.##}";
        TxtDiscount.Text = $"− ₹ {Amount:0.##}";
        TxtNet.Text = $"₹ {_subtotal - Amount:0.##}";
    }

    private void TxtValue_TextChanged(object sender, TextChangedEventArgs e) => Recalculate();

    private void TxtValue_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !System.Text.RegularExpressions.Regex.IsMatch(e.Text, @"^[0-9.]+$");

    private void TxtValue_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Apply_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Cancel_Click(sender, e);
        }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        DiscountValue = ParsedValue;
        DialogResult = true;
        Close();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        DiscountValue = 0;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
