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
        new BillViewModal(row, items) { Owner = Window.GetWindow(this) }.ShowDialog();
    }
}
