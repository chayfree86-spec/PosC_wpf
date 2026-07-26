using System.Collections.Generic;
using System.Windows;
using Pos.App.ViewModels;
using Pos.Core.Models;
using Pos.Core.Printing;

namespace Pos.App.Views;

public partial class BillViewModal : Window
{
    private readonly Action? _reprint;

    /// <param name="onReprint">Sends this bill to the printer. Null hides the button — a
    /// preview opened without a printer behind it must not offer paper it cannot produce.</param>
    /// <param name="branding">Settings → Profile as the bill will print it. The heading shows
    /// exactly the details switched on there, so the preview is the paper.</param>
    public BillViewModal(ReportRow row, IReadOnlyList<OrderItem> items, Action? onReprint = null,
        PrintConfig? branding = null)
    {
        InitializeComponent();
        _reprint = onReprint;
        if (onReprint is null) PrintButton.Visibility = Visibility.Collapsed;
        if (branding is not null) ShowBranding(branding);

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

    /// <summary>
    /// Puts the same branding block on screen that <see cref="ReceiptBuilder"/> puts on paper.
    /// A detail whose "PRINT OFF" switch is set is left out here too — otherwise the preview
    /// would promise a phone number or GST line the printed bill never carries.
    /// </summary>
    private void ShowBranding(PrintConfig cfg)
    {
        var builder = new ReceiptBuilder(cfg);

        if (builder.StoreHeading is { } heading) StoreName.Text = heading;
        else StoreName.Visibility = Visibility.Collapsed;

        StoreDetails.ItemsSource = builder.HeaderDetailLines();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // The spooler takes the job on its own thread, so the preview can close straight away —
    // a printer that is off surfaces its own error from PrintSpooler.Failed.
    private void Print_Click(object sender, RoutedEventArgs e)
    {
        _reprint?.Invoke();
        Close();
    }
}
