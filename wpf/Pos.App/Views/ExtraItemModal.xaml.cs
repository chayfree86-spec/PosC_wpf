using System.Windows;
using System.Windows.Input;

namespace Pos.App.Views;

public partial class ExtraItemModal : Window
{
    public double Amount { get; private set; }
    public string Remarks { get; private set; } = "Extra Charge";

    public ExtraItemModal()
    {
        InitializeComponent();
        Loaded += (s, e) =>
        {
            TxtAmount.Focus();
        };
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(TxtAmount.Text, out double amt) && amt > 0)
        {
            Amount = amt;
            if (!string.IsNullOrWhiteSpace(TxtRemarks.Text))
            {
                Remarks = TxtRemarks.Text.Trim();
            }
            DialogResult = true;
            Close();
        }
        else
        {
            ThemeMessageBox.Show(this, "Please enter a valid amount greater than 0.", "Invalid Amount", "warning");
        }
    }

    private void Txt_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Add_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close_Click(sender, e);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
