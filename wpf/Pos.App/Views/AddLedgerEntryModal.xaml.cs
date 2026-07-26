using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pos.Core.Models;

namespace Pos.App.Views;

public partial class AddLedgerEntryModal : Window
{
    private static readonly Brush Red = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFEF4444"));
    private static readonly Brush Green = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF22C55E"));

    public string EntryType { get; private set; } = "debit";   // "debit" (borrow) | "credit" (paid)
    public double Amount { get; private set; }
    public string PaymentMode { get; private set; } = "cash";
    public string Remarks { get; private set; } = "";
    public DateTime EntryDate { get; private set; } = DateTime.Today;
    private TimeSpan _timeOfDay = DateTime.Now.TimeOfDay;

    public AddLedgerEntryModal(string customerName, LedgerEntry? existing = null)
    {
        InitializeComponent();

        if (existing != null)
        {
            TxtTitle.Text = "EDIT TRANSACTION";
            EntryType = existing.IsDebit ? "debit" : "credit";
            TxtAmount.Text = existing.Amount.ToString("0.##");
            PaymentMode = string.IsNullOrWhiteSpace(existing.PaymentMode) ? "cash" : existing.PaymentMode;
            TxtRemarks.Text = existing.Remarks ?? "";
            BtnSubmit.Content = "UPDATE TRANSACTION";
            if (DateTime.TryParse(existing.CreatedAt, out var d)) { DpDate.SelectedDate = d.Date; _timeOfDay = d.TimeOfDay; }
            else DpDate.SelectedDate = DateTime.Today;
        }
        else
        {
            TxtTitle.Text = $"NEW TRANSACTION — {customerName}";
            DpDate.SelectedDate = DateTime.Today;
        }

        SetType(EntryType);
        SetMode(PaymentMode);
        Loaded += (_, _) => TxtAmount.Focus();
    }

    private void Type_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string t) SetType(t);
    }

    private void SetType(string type)
    {
        EntryType = type;
        var card = (Brush)FindResource("CardBg");
        var muted = (Brush)FindResource("TextMuted");
        bool debit = type == "debit";
        BtnDebit.Background = debit ? Red : card;
        BtnDebit.Foreground = debit ? Brushes.White : muted;
        BtnCredit.Background = !debit ? Green : card;
        BtnCredit.Foreground = !debit ? Brushes.White : muted;
    }

    private void Mode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string t) SetMode(t);
    }

    private void SetMode(string mode)
    {
        PaymentMode = mode;
        var active = (Brush)FindResource("Accent");
        var card = (Brush)FindResource("CardBg");
        var muted = (Brush)FindResource("TextMuted");
        BtnCash.Background = mode == "cash" ? active : card;
        BtnCash.Foreground = mode == "cash" ? Brushes.White : muted;
        BtnUpi.Background = mode == "upi" ? active : card;
        BtnUpi.Foreground = mode == "upi" ? Brushes.White : muted;
        BtnBank.Background = mode == "bank" ? active : card;
        BtnBank.Foreground = mode == "bank" ? Brushes.White : muted;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(TxtAmount.Text, out var amt) && amt > 0)
        {
            Amount = amt;
            Remarks = TxtRemarks.Text.Trim();
            EntryDate = (DpDate.SelectedDate?.Date ?? DateTime.Today) + _timeOfDay;
            DialogResult = true;
        }
        else
        {
            MessageBox.Show("Please enter a valid amount greater than 0.", "Invalid Amount", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
