using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Pos.App.Views;

public partial class SaveNoteModal : Window
{
    public string CustomerName { get; private set; } = "";
    public string CustomerMobile { get; private set; } = "";
    public string TargetTime { get; private set; } = "";

    public SaveNoteModal()
    {
        InitializeComponent();
        TxtTime.Text = DateTime.Now.ToString("hh:mm tt");
        Loaded += (_, _) =>
        {
            TxtName.Focus();
            TxtName.SelectAll();
        };
    }

    public void SetEditingMode(string customerName, string customerMobile, string targetTime)
    {
        TxtHeaderTitle.Text = "Update Order Note";
        TxtHeaderSubtitle.Text = "Update quick order items, customer details, or serving time";
        TxtSubmitButton.Text = "UPDATE NOTE";
        IconSubmit.Kind = MaterialDesignThemes.Wpf.PackIconKind.Pencil;

        if (!string.IsNullOrWhiteSpace(customerName)) TxtName.Text = customerName;
        if (!string.IsNullOrWhiteSpace(customerMobile)) TxtMobile.Text = customerMobile;
        if (!string.IsNullOrWhiteSpace(targetTime)) TxtTime.Text = targetTime;
    }

    private void TxtName_TextChanged(object sender, TextChangedEventArgs e)
    {
        PlaceholderName.Visibility = string.IsNullOrEmpty(TxtName.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TxtMobile_TextChanged(object sender, TextChangedEventArgs e)
    {
        PlaceholderMobile.Visibility = string.IsNullOrEmpty(TxtMobile.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TxtTime_TextChanged(object sender, TextChangedEventArgs e)
    {
        PlaceholderTime.Visibility = string.IsNullOrEmpty(TxtTime.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TxtName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            TxtMobile.Focus();
            TxtMobile.SelectAll();
        }
    }

    private void TxtMobile_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            TxtTime.Focus();
            TxtTime.SelectAll();
        }
    }

    private void TxtTime_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            ConfirmAndSave();
        }
    }

    private void TxtMobile_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        ConfirmAndSave();
    }

    private void ConfirmAndSave()
    {
        CustomerName = TxtName.Text.Trim();
        CustomerMobile = TxtMobile.Text.Trim();
        TargetTime = string.IsNullOrWhiteSpace(TxtTime.Text) ? DateTime.Now.ToString("hh:mm tt") : TxtTime.Text.Trim();

        DialogResult = true;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }
}
