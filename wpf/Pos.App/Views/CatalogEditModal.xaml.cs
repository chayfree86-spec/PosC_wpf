using System.Collections.Generic;
using System.Windows;
using Pos.Core.Models;

namespace Pos.App.Views;

/// <summary>
/// Reusable add/edit dialog for catalog entities (table, category, gst, menu),
/// matching the Electron Settings modals. Shows only the fields relevant to
/// <paramref name="kind"/>. Read the result properties after ShowDialog()==true.
/// </summary>
public partial class CatalogEditModal : Window
{
    private readonly string _kind;

    public string ItemName => NameBox.Text.Trim();
    public double PriceValue => double.TryParse(PriceBox.Text, out var p) ? p : 0;
    public double RateValue => double.TryParse(RateBox.Text, out var r) ? r : 0;
    public long? AreaId => AreaCombo.SelectedValue as long?;
    public long? CategoryId => CategoryCombo.SelectedValue as long?;
    public string TypeValue => (TypeCombo.SelectedItem as string) ?? "veg";
    public long SortOrderValue => long.TryParse(SortOrderBox.Text, out var s) ? s : 0;
    public long? ParentCategoryId => ParentCatCombo.SelectedValue as long?;

    public CatalogEditModal(string kind, string title,
        string name = "", double price = 0, double rate = 0,
        long? areaId = null, long? categoryId = null, string type = "veg",
        IEnumerable<DiningArea>? areas = null, IEnumerable<Category>? categories = null,
        long sortOrder = 0, IEnumerable<Category>? parentCategories = null, long? parentCategoryId = null)
    {
        InitializeComponent();
        _kind = kind;
        TitleText.Text = title;
        NameBox.Text = name;
        PriceBox.Text = price > 0 ? price.ToString("0.##") : "";
        RateBox.Text = rate > 0 ? rate.ToString("0.##") : "";
        SortOrderBox.Text = sortOrder.ToString();

        TypeCombo.ItemsSource = new[] { "veg", "nonveg" };
        TypeCombo.SelectedItem = type == "nonveg" ? "nonveg" : "veg";

        if (areas != null)
        {
            AreaCombo.ItemsSource = new List<DiningArea>(areas);
            AreaCombo.SelectedValue = areaId;
        }
        if (categories != null)
        {
            CategoryCombo.ItemsSource = new List<Category>(categories);
            CategoryCombo.SelectedValue = categoryId;
        }
        if (parentCategories != null)
        {
            var list = new List<Category>(parentCategories);
            ParentCatCombo.ItemsSource = list;
            ParentCatCombo.SelectedValue = parentCategoryId ?? list.FirstOrDefault()?.Id;
        }

        // Show only the fields for this kind.
        PricePanel.Visibility = kind == "menu" ? Visibility.Visible : Visibility.Collapsed;
        CategoryPanel.Visibility = kind == "menu" ? Visibility.Visible : Visibility.Collapsed;
        TypePanel.Visibility = kind == "menu" ? Visibility.Visible : Visibility.Collapsed;
        RatePanel.Visibility = kind == "gst" ? Visibility.Visible : Visibility.Collapsed;
        AreaPanel.Visibility = kind == "table" ? Visibility.Visible : Visibility.Collapsed;
        ParentCatPanel.Visibility = kind == "subcategory" ? Visibility.Visible : Visibility.Collapsed;
        SortOrderPanel.Visibility = kind == "category" ? Visibility.Visible : Visibility.Collapsed;

        NameLabel.Text = kind switch
        {
            "table" => "TABLE NUMBER / NAME *",
            "gst" => "GST LABEL *",
            "category" => "CATEGORY NAME *",
            "subcategory" => "SUBCATEGORY NAME *",
            "area" => "DINING AREA NAME *",
            _ => "ITEM NAME *"
        };

        Loaded += (_, _) => NameBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ThemeMessageBox.Show(this, "Name is required.", "Required", "warning");
            return;
        }
        if (_kind == "subcategory" && ParentCategoryId is null)
        {
            ThemeMessageBox.Show(this, "Parent Category select karein.", "Required", "warning");
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
