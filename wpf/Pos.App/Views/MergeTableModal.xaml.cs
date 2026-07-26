using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Pos.App.ViewModels;
using Pos.Core.Models;

namespace Pos.App.Views;

public partial class MergeTableModal : Window
{
    public TableView? SelectedSourceTable { get; private set; }

    public MergeTableModal(string targetTableNumber, IEnumerable<TableView> occupiedTables)
    {
        InitializeComponent();
        TxtTitle.Text = $"MERGE TABLE INTO {targetTableNumber}";
        var list = occupiedTables.ToList();
        LstOccupiedTables.ItemsSource = list;
        if (list.Count > 0)
        {
            LstOccupiedTables.SelectedIndex = 0;
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
