using System.Windows;
using System.Windows.Controls;
using Pos.App.ViewModels;

namespace Pos.App.Views;

public partial class ReportsView : UserControl
{
    public ReportsView() => InitializeComponent();

    private void ViewBill_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ReportsViewModel vm) return;
        if ((sender as FrameworkElement)?.DataContext is not ReportRow row) return;

        var items = vm.LoadItems(row.Order.Id);
        var owner = Window.GetWindow(this);

        // Reprinting goes through the main view model so the duplicate uses the one print
        // spooler — a second spooler would put two jobs on a thermal printer at once.
        var main = owner?.DataContext as MainViewModel;
        var reprint = main is null ? (Action?)null : () => main.ReprintBill(row.Order, items);

        new BillViewModal(row, items, reprint, main?.Settings.BuildPrintConfig()) { Owner = owner }.ShowDialog();
    }

    /// <summary>Puts this already-billed order on a customer's khata — pick an existing customer
    /// or add a new one, and the bill's amount is filed as udhaar against them.</summary>
    private void AddToLedger_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ReportsViewModel vm) return;
        if ((sender as FrameworkElement)?.DataContext is not ReportRow row) return;

        var owner = Window.GetWindow(this);
        var order = row.Order;
        var dlg = new BillToLedgerModal(order.TotalAmount, vm.GetLedgerCustomers(),
            order.CustomerName ?? "", order.CustomerMobile ?? "") { Owner = owner };
        if (dlg.ShowDialog() != true) return;

        vm.AddOrderToLedger(row, dlg.SelectedCustomerId, dlg.CustomerName, dlg.CustomerMobile);
        ThemeMessageBox.Show(owner,
            $"{row.BillNoText} — {dlg.CustomerName} ke khaate me ₹{order.TotalAmount:0.##} jud gaya.",
            "Khata Updated", "success");
    }
}
