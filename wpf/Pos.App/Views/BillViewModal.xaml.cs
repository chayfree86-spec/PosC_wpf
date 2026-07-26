using System.Collections.Generic;
using System.Windows;
using Pos.App.ViewModels;
using Pos.Core.Models;

namespace Pos.App.Views;

public partial class BillViewModal : Window
{
    public BillViewModal(ReportRow row, IReadOnlyList<OrderItem> items)
    {
        InitializeComponent();
        var o = row.Order;

        BillNoText.Text = row.BillNoText;
        DateText.Text = row.DateText;
        TableText.Text = string.IsNullOrWhiteSpace(o.TableNumber) ? "Quick Bill" : $"Table {o.TableNumber}";
        ItemsList.ItemsSource = items;

        if (o.DiscountAmount > 0)
        {
            DiscountRow.Visibility = Visibility.Visible;
            DiscountText.Text = "-₹" + o.DiscountAmount.ToString("0.##");
        }
        GrandTotalText.Text = "₹" + o.TotalAmount.ToString("0.##");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
