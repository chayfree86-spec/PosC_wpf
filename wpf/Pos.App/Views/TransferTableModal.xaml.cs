using System.Windows;
using Pos.Core.Models;

namespace Pos.App.Views;

public partial class TransferTableModal : Window
{
    public TableView? SelectedTargetTable { get; private set; }

    public TransferTableModal(string currentTableNumber, IEnumerable<TableView> freeTables)
    {
        InitializeComponent();
        TxtTitle.Text = $"TRANSFER TABLE ({currentTableNumber})";
        LstTargetTables.ItemsSource = freeTables;
    }

    private void Transfer_Click(object sender, RoutedEventArgs e)
    {
        if (LstTargetTables.SelectedItem is TableView target)
        {
            SelectedTargetTable = target;
            DialogResult = true;
            Close();
        }
        else
        {
            ThemeMessageBox.Show(this, "Please select a target free table.", "Select Table", "warning");
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
