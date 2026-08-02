using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Pos.Core.Models;

namespace Pos.App.Views;

/// <summary>
/// Picks the customer a bill's udhaar goes to: an existing one from the list, or a new one typed
/// in below. The two are mutually exclusive — selecting from the list clears the new-customer
/// fields, and typing a new name clears the selection — so there is never a doubt about whose
/// khata the amount lands on.
/// </summary>
public partial class BillToLedgerModal : Window
{
    private readonly List<Customer> _all;
    private bool _syncing;

    /// <summary>Set when an existing customer was chosen; null means create a new one.</summary>
    public long? SelectedCustomerId { get; private set; }
    public string CustomerName { get; private set; } = "";
    public string CustomerMobile { get; private set; } = "";

    public BillToLedgerModal(double amount, IReadOnlyList<Customer> customers, string prefillName, string prefillMobile)
    {
        InitializeComponent();
        _all = customers.ToList();
        TxtAmount.Text = "₹" + amount.ToString("0.##", CultureInfo.InvariantCulture);
        CustomerList.ItemsSource = _all;

        // A customer already tagged on the order (mobile matches) is pre-selected; otherwise the
        // typed name/mobile seed the new-customer fields.
        var match = !string.IsNullOrWhiteSpace(prefillMobile)
            ? _all.FirstOrDefault(c => c.Phone == prefillMobile.Trim())
            : null;
        if (match is not null)
        {
            CustomerList.SelectedItem = match;
        }
        else
        {
            TxtName.Text = prefillName ?? "";
            TxtMobile.Text = prefillMobile ?? "";
        }
        Loaded += (_, _) => TxtSearch.Focus();
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        var q = TxtSearch.Text.Trim().ToLowerInvariant();
        CustomerList.ItemsSource = q.Length == 0
            ? _all
            : _all.Where(c => c.Name.ToLowerInvariant().Contains(q) || (c.Phone ?? "").Contains(q)).ToList();
    }

    // Selecting an existing customer and typing a new one are mutually exclusive.
    private void Customer_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || CustomerList.SelectedItem is not Customer) return;
        _syncing = true;
        TxtName.Text = "";
        TxtMobile.Text = "";
        UpdatePlaceholders();
        _syncing = false;
    }

    private void NewField_Changed(object sender, TextChangedEventArgs e)
    {
        UpdatePlaceholders();
        if (_syncing) return;
        if (!string.IsNullOrEmpty(TxtName.Text) || !string.IsNullOrEmpty(TxtMobile.Text))
        {
            _syncing = true;
            CustomerList.SelectedItem = null;
            _syncing = false;
        }
    }

    private void UpdatePlaceholders()
    {
        PhName.Visibility = string.IsNullOrEmpty(TxtName.Text) ? Visibility.Visible : Visibility.Collapsed;
        PhMobile.Visibility = string.IsNullOrEmpty(TxtMobile.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (CustomerList.SelectedItem is Customer picked)
        {
            SelectedCustomerId = picked.Id;
            CustomerName = picked.Name;
            CustomerMobile = picked.Phone;
        }
        else if (!string.IsNullOrWhiteSpace(TxtName.Text))
        {
            SelectedCustomerId = null;
            CustomerName = TxtName.Text.Trim();
            CustomerMobile = TxtMobile.Text.Trim();
        }
        else
        {
            ThemeMessageBox.Show(this, "Customer chunein ya naya naam daalein.", "Customer Required", "warning");
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
