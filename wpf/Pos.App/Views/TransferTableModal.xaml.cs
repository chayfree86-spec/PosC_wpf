using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Pos.Core.Models;

namespace Pos.App.Views;

public partial class TransferTableModal : Window
{
    private readonly List<TableView> _allTables;

    public TableView? SelectedTargetTable { get; private set; }

    public TransferTableModal(string currentTableNumber, IEnumerable<TableView> freeTables)
    {
        InitializeComponent();
        TxtTitle.Text = $"TRANSFER TABLE ({currentTableNumber})";
        _allTables = freeTables.ToList();
        LstTargetTables.ItemsSource = _allTables;
        // Focus the search on open so the operator can just start typing the table number.
        Loaded += (_, _) => TxtSearch.Focus();
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        var q = TxtSearch.Text.Trim();
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(q) ? Visibility.Visible : Visibility.Collapsed;
        LstTargetTables.ItemsSource = string.IsNullOrEmpty(q)
            ? _allTables
            : _allTables.Where(t => (t.TableNumber ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    // Enter picks the first match, so a quick "type + Enter + Enter" transfers without the mouse.
    private void Search_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && LstTargetTables.Items.Count > 0)
        {
            LstTargetTables.SelectedIndex = 0;
            e.Handled = true;
        }
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
