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
}
