using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Pos.App.ViewModels;
using Pos.Core.Models;

namespace Pos.App.Views;

public partial class MergeTableModal : Window
{
    private readonly List<TableView> _allTables;

    public TableView? SelectedSourceTable { get; private set; }

    public MergeTableModal(string targetTableNumber, IEnumerable<TableView> occupiedTables)
    {
        InitializeComponent();
        TxtTitle.Text = $"MERGE TABLE INTO {targetTableNumber}";
        _allTables = occupiedTables.ToList();
        LstOccupiedTables.ItemsSource = _allTables;
        if (_allTables.Count > 0)
        {
            LstOccupiedTables.SelectedIndex = 0;
        }
        Loaded += (_, _) => TxtSearch.Focus();
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        var q = TxtSearch.Text.Trim();
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(q) ? Visibility.Visible : Visibility.Collapsed;
        LstOccupiedTables.ItemsSource = string.IsNullOrEmpty(q)
            ? _allTables
            : _allTables.Where(t => (t.TableNumber ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        if (LstOccupiedTables.Items.Count > 0)
        {
            LstOccupiedTables.SelectedIndex = 0;
        }
    }

    private void Search_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && LstOccupiedTables.Items.Count > 0)
        {
            LstOccupiedTables.SelectedIndex = 0;
            e.Handled = true;
        }
    }

    private void Merge_Click(object sender, RoutedEventArgs e)
    {
        if (LstOccupiedTables.SelectedItem is TableView table)
        {
            SelectedSourceTable = table;
            DialogResult = true;
            Close();
        }
        else
        {
            ThemeMessageBox.Show(this, "Please select an occupied table to merge.", "Merge Table", "warning");
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
