using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using Pos.App.ViewModels;
using Pos.Core.Models;

namespace Pos.App.Views;

public class SplitItemModel : INotifyPropertyChanged
{
    public CartLine OriginalLine { get; }
    public string Name => OriginalLine.Name;
    public long Qty => OriginalLine.Qty;
    public string SinglePriceText => $"₹{OriginalLine.Price:0.##}";

    private bool _isSelectedForSplit;
    public bool IsSelectedForSplit
    {
        get => _isSelectedForSplit;
        set
        {
            if (_isSelectedForSplit != value)
            {
                _isSelectedForSplit = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectedForSplit)));
            }
        }
    }

    public SplitItemModel(CartLine line)
    {
        OriginalLine = line;
        IsSelectedForSplit = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class SplitTableModal : Window
{
    public TableView? SelectedTargetTable { get; private set; }
    public List<CartLine> SelectedSplitLines { get; private set; } = new();

    public SplitTableModal(string sourceTableNumber, IEnumerable<CartLine> cartItems, IEnumerable<TableView> freeTables)
    {
        InitializeComponent();
        TxtTitle.Text = $"SPLIT ORDER FROM {sourceTableNumber}";

        var itemModels = cartItems.Select(c => new SplitItemModel(c)).ToList();
        LstCartItems.ItemsSource = itemModels;

        var freeList = freeTables.ToList();
        CmbTargetTables.ItemsSource = freeList;
        if (freeList.Count > 0)
        {
            CmbTargetTables.SelectedIndex = 0;
        }
    }

    private void Split_Click(object sender, RoutedEventArgs e)
    {
        if (CmbTargetTables.SelectedItem is not TableView targetTable)
        {
            ThemeMessageBox.Show(this, "Please select a target free table.", "Split Table", "warning");
            return;
        }

        if (LstCartItems.ItemsSource is List<SplitItemModel> models)
        {
            SelectedSplitLines = models.Where(m => m.IsSelectedForSplit).Select(m => m.OriginalLine).ToList();
        }

        if (SelectedSplitLines.Count == 0)
        {
            ThemeMessageBox.Show(this, "Please select at least one item to split.", "Split Table", "warning");
            return;
        }

        SelectedTargetTable = targetTable;
        DialogResult = true;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
