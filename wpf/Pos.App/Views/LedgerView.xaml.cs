using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Pos.App.ViewModels;
using Pos.Core.Models;

namespace Pos.App.Views;

public partial class LedgerView : UserControl
{
    public LedgerView() => InitializeComponent();

    private LedgerViewModel? Vm => DataContext as LedgerViewModel;

    private static string StoreName =>
        App.Services?.GetService<SettingsViewModel>()?.StoreName ?? "Chay Chaupal";

    // ── Customer CRUD ──
    private void AddCustomer_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var dlg = new AddCustomerModal { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            Vm.AddCustomer(dlg.CustomerName, dlg.Phone, dlg.Address, dlg.OpeningBalance);
    }

    private void EditCustomer_Click(object sender, RoutedEventArgs e)
    {
        if (Vm?.SelectedCustomer is not { } c) return;
        var dlg = new AddCustomerModal(c) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            Vm.EditCustomer(c, dlg.CustomerName, dlg.Phone, dlg.Address);
    }

    private void DeleteCustomer_Click(object sender, RoutedEventArgs e)
    {
        if (Vm?.SelectedCustomer is not { } c) return;
        if (MessageBox.Show($"Delete customer '{c.Name}' and all their transactions?", "Delete Customer",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            Vm.DeleteSelectedCustomer();
        }
    }

    // ── Transaction CRUD ──
    private void NewTransaction_Click(object sender, RoutedEventArgs e)
    {
        if (Vm?.SelectedCustomer is not { } c) return;
        var dlg = new AddLedgerEntryModal(c.Name) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            Vm.AddEntry(dlg.EntryType, dlg.Amount, dlg.PaymentMode, dlg.Remarks, dlg.EntryDate);
    }

    private void EditEntry_Click(object sender, RoutedEventArgs e)
    {
        if (Vm?.SelectedCustomer is not { } c) return;
        if ((sender as FrameworkElement)?.DataContext is not LedgerEntry entry) return;
        var dlg = new AddLedgerEntryModal(c.Name, entry) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            Vm.UpdateEntry(entry, dlg.EntryType, dlg.Amount, dlg.PaymentMode, dlg.Remarks, dlg.EntryDate);
    }

    private void DeleteEntry_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if ((sender as FrameworkElement)?.DataContext is not LedgerEntry entry) return;
        if (MessageBox.Show("Delete this transaction?", "Delete Transaction",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            Vm.DeleteEntry(entry);
        }
    }

    // ── WhatsApp share ──
    private void Share_Click(object sender, RoutedEventArgs e)
    {
        if (Vm?.SelectedCustomer is not { } c) return;
        var text = BuildStatementText(c);
        var digits = new string((c.Phone ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length == 10) digits = "91" + digits;   // default India country code

        var url = digits.Length >= 11
            ? $"https://wa.me/{digits}?text={Uri.EscapeDataString(text)}"
            : $"https://wa.me/?text={Uri.EscapeDataString(text)}";
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not open WhatsApp: " + ex.Message, "Share", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private string BuildStatementText(Customer c)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"*{StoreName} — Customer Statement*");
        sb.AppendLine($"Customer: {c.Name}");
        if (!string.IsNullOrWhiteSpace(c.Phone)) sb.AppendLine($"Mobile: {c.Phone}");
        sb.AppendLine($"Outstanding: {c.FormattedBalance}");
        sb.AppendLine();
        sb.AppendLine("Transactions:");
        foreach (var t in Vm!.CustomerEntries)
        {
            sb.AppendLine($"{t.DateShort}  |  {t.TypeLabel}  {t.SignedAmountText}  |  {t.Description}");
        }
        return sb.ToString();
    }

    // ── Print statement (thermal 58/80mm) ──
    private void Print_Click(object sender, RoutedEventArgs e)
    {
        if (Vm?.SelectedCustomer is not { } c) return;

        var settings = App.Services?.GetService<SettingsViewModel>();
        var printerName = settings?.SelectedPrinter ?? "";
        var is58 = (settings?.PaperSize ?? "80mm").Contains("58");
        double width = is58 ? 200 : 280;          // ~58mm / 80mm printable, at 96 dpi
        int cols = is58 ? 32 : 42;

        var pd = new System.Windows.Controls.PrintDialog();
        // Print straight to the thermal printer configured in Settings; if it's not
        // resolvable, fall back to the standard print dialog.
        try
        {
            if (!string.IsNullOrWhiteSpace(printerName)
                && !printerName.StartsWith("Default Thermal", StringComparison.OrdinalIgnoreCase))
            {
                var server = new System.Printing.LocalPrintServer();
                pd.PrintQueue = server.GetPrintQueue(printerName);
            }
            else if (pd.ShowDialog() != true) return;
        }
        catch
        {
            if (pd.ShowDialog() != true) return;
        }

        var doc = BuildThermalDoc(c, width, cols);
        var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
        paginator.PageSize = new Size(width, 5000);
        pd.PrintDocument(paginator, $"Statement - {c.Name}");
    }

    private FlowDocument BuildThermalDoc(Customer c, double width, int cols)
    {
        var doc = new FlowDocument
        {
            PageWidth = width, PageHeight = 5000, ColumnWidth = width,
            PagePadding = new Thickness(6, 8, 6, 8),
            FontFamily = new FontFamily("Consolas"), FontSize = 9,
            Foreground = Brushes.Black
        };

        void Add(string t, double size = 9, bool bold = false, TextAlignment a = TextAlignment.Left)
            => doc.Blocks.Add(new Paragraph(new Run(t))
            {
                FontSize = size,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                TextAlignment = a,
                Margin = new Thickness(0),
                LineHeight = size + 3
            });

        Add(StoreName, 15, true, TextAlignment.Center);
        Add("CUSTOMER STATEMENT", 9, false, TextAlignment.Center);
        Add(new string('-', cols));
        Add($"Name : {c.Name}", 11, true);
        if (!string.IsNullOrWhiteSpace(c.Phone)) Add($"Mob  : {c.Phone}");
        Add($"Balance : {Money(c.FormattedBalance)}", 11, true);
        Add(new string('-', cols));

        foreach (var t in Vm!.CustomerEntries)
        {
            Add(t.DateShort, 8);
            var amt = (t.IsDebit ? "+ Rs." : "- Rs.") + t.Amount.ToString("0.##");
            var desc = $"{t.TypeLabel} {t.Description}";
            if (desc.Length + amt.Length + 1 > cols)
                desc = desc.Substring(0, Math.Max(0, cols - amt.Length - 1));
            Add(desc.PadRight(Math.Max(0, cols - amt.Length)) + amt, 9);
        }

        Add(new string('-', cols));
        Add($"Printed: {DateTime.Now:dd/MM/yyyy HH:mm}", 8, false, TextAlignment.Center);
        Add("Thank you!", 9, true, TextAlignment.Center);
        return doc;
    }

    private static string Money(string s) => s.Replace("₹", "Rs.");
}
