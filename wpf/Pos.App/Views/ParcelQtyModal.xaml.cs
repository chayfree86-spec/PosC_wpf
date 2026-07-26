using System.Windows;
using System.Windows.Input;

namespace Pos.App.Views;

public partial class ParcelQtyModal : Window
{
    public long SelectedQty { get; private set; }
    private readonly long _maxQty;

    public ParcelQtyModal(long maxQty)
    {
        InitializeComponent();
        _maxQty = maxQty;
        TxtPrompt.Text = $"QUANTITY TO PARCEL (मात्रा चुनें, Max: {maxQty}):";
        TxtQty.Text = maxQty.ToString();
        
        Loaded += (s, e) =>
        {
            TxtQty.Focus();
            TxtQty.SelectAll();
        };
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (long.TryParse(TxtQty.Text.Trim(), out long qty) && qty > 0 && qty <= _maxQty)
        {
            SelectedQty = qty;
            DialogResult = true;
            Close();
        }
        else
        {
            ThemeMessageBox.Show(this, $"Please enter a valid quantity between 1 and {_maxQty}.", "Invalid Quantity", "warning");
        }
    }

    private void TxtQty_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Confirm_Click(sender, e);
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
