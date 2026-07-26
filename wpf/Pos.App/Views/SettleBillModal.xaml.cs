using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Pos.App.Views;

/// <summary>
/// Payment-mode / cash-received dialog. Currently not part of any flow — settling is
/// immediate and takes cash for the full amount — but kept intact for when a payment step
/// is wanted again.
/// </summary>
public partial class SettleBillModal : Window
{
    public string SelectedPaymentMethod { get; private set; } = "cash";
    public double NetAmount { get; }
    public double ReceivedAmount { get; private set; }
    public double ChangeDue { get; private set; }

    public SettleBillModal(double netAmount)
    {
        InitializeComponent();
        NetAmount = netAmount;
        TxtNetTotal.Text = $"₹ {NetAmount:0.##}";
        TxtReceived.Text = NetAmount > 0 ? NetAmount.ToString("0.##") : "";
        UpdateModeButtons();
        CalculateChange();
        Loaded += (s, e) =>
        {
            TxtReceived.Focus();
            TxtReceived.SelectAll();
        };
    }

    private void Mode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            SelectedPaymentMethod = tag;
            UpdateModeButtons();
        }
    }

    private void UpdateModeButtons()
    {
        var activeBrush = (Brush)Application.Current.FindResource("Accent");
        var inactiveBrush = (Brush)Application.Current.FindResource("CardBg");
        var textActive = Brushes.White;
        var textInactive = (Brush)Application.Current.FindResource("TextMuted");

        SetBtnStyle(BtnCash, SelectedPaymentMethod == "cash", activeBrush, inactiveBrush, textActive, textInactive);
        SetBtnStyle(BtnUpi, SelectedPaymentMethod == "upi", activeBrush, inactiveBrush, textActive, textInactive);
        SetBtnStyle(BtnCard, SelectedPaymentMethod == "card", activeBrush, inactiveBrush, textActive, textInactive);
        SetBtnStyle(BtnKhata, SelectedPaymentMethod == "credit", activeBrush, inactiveBrush, textActive, textInactive);

        if (SelectedPaymentMethod == "cash")
        {
            CashPanel.Visibility = Visibility.Visible;
            NonCashInfo.Visibility = Visibility.Collapsed;
            TxtReceived.Focus();
            TxtReceived.SelectAll();
        }
        else
        {
            CashPanel.Visibility = Visibility.Collapsed;
            NonCashInfo.Visibility = Visibility.Visible;
            NonCashInfo.Text = $"Selected: {SelectedPaymentMethod.ToUpper()} Payment. Press Enter to Settle & Print.";
        }
    }

    private static void SetBtnStyle(Button btn, bool isActive, Brush activeBg, Brush inactiveBg, Brush textActive, Brush textInactive)
    {
        btn.Background = isActive ? activeBg : inactiveBg;
        btn.Foreground = isActive ? textActive : textInactive;
        btn.FontWeight = isActive ? FontWeights.Bold : FontWeights.SemiBold;
        btn.Height = 36;
        btn.Resources[typeof(Border)] = new Style(typeof(Border)) { Setters = { new Setter(Border.CornerRadiusProperty, new CornerRadius(8)) } };
    }

    private void QuickAmount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            if (tag == "exact")
            {
                TxtReceived.Text = NetAmount.ToString("0.##");
            }
            else if (double.TryParse(tag, out double addVal))
            {
                double current = double.TryParse(TxtReceived.Text, out double parsed) ? parsed : 0;
                TxtReceived.Text = (current + addVal).ToString("0.##");
            }
            TxtReceived.Focus();
            TxtReceived.SelectAll();
        }
    }

    private void TxtReceived_TextChanged(object sender, TextChangedEventArgs e) => CalculateChange();

    /// <summary>Cash received is a number; letters here silently parsed as zero.</summary>
    private void TxtReceived_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !System.Text.RegularExpressions.Regex.IsMatch(e.Text, @"^[0-9.]+$");

    private void CalculateChange()
    {
        if (double.TryParse(TxtReceived.Text, out double rec))
        {
            ReceivedAmount = rec;
            ChangeDue = Math.Max(0, ReceivedAmount - NetAmount);
            TxtChangeDue.Text = $"₹ {ChangeDue:0.##}";
        }
        else
        {
            ReceivedAmount = 0;
            ChangeDue = 0;
            TxtChangeDue.Text = "₹ 0";
        }
    }

    private void TxtReceived_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Settle_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close_Click(sender, e);
        }
    }

    private void Settle_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
