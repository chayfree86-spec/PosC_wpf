using System.Windows;
using Pos.Core.Models;

namespace Pos.App.Views;

public partial class AddCustomerModal : Window
{
    public string CustomerName { get; private set; } = "";
    public string Phone { get; private set; } = "";
    public string Address { get; private set; } = "";
    public double OpeningBalance { get; private set; }

    public AddCustomerModal()
    {
        InitializeComponent();
        Loaded += (s, e) => TxtName.Focus();
    }

    /// <summary>Edit mode: pre-fill and hide the opening-balance field.</summary>
    public AddCustomerModal(Customer edit) : this()
    {
        TxtTitle.Text = "EDIT CUSTOMER";
        TxtName.Text = edit.Name;
        TxtPhone.Text = edit.Phone;
        TxtAddress.Text = edit.Address ?? "";
        OpeningLabel.Visibility = Visibility.Collapsed;
        OpeningBorder.Visibility = Visibility.Collapsed;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        {
            MessageBox.Show("Please enter customer name.", "Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        CustomerName = TxtName.Text.Trim();
        Phone = TxtPhone.Text.Trim();
        Address = TxtAddress.Text.Trim();
        OpeningBalance = double.TryParse(TxtBalance.Text, out double b) ? b : 0;

        DialogResult = true;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
