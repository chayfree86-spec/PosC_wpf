using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Pos.Core.Models;
using MenuItem = Pos.Core.Models.MenuItem;

namespace Pos.App.Views;

/// <summary>
/// Add/Edit Menu Item dialog matching the Electron app's "Create Menu Item" modal:
/// Category + Sub Category dropdowns, an Item Name field with a Hindi/English typing
/// toggle backed by the Google Input Tools transliteration API, and Price/Code fields.
///
/// In "add" mode the dialog saves via <paramref name="onSave"/> and stays open after
/// each save (showing a "Last Added Item" confirmation and clearing only name/code/price,
/// keeping category/subcategory) so a cafe owner can rapidly enter a whole category's
/// dishes without reopening the dialog each time — matching the Electron reference.
/// In "edit" mode it saves once and closes.
/// </summary>
public partial class MenuItemModal : Window
{
    private static readonly HttpClient _http = new();
    private readonly List<Category> _allCategories;
    private readonly Action<MenuItem> _onSave;
    private readonly bool _isEdit;
    private readonly long _existingId;
    private readonly long _existingClientId;
    private readonly string _existingType;
    private readonly long _existingIsAvailable;
    private readonly long _existingIsParcel;
    private bool _hindiMode = true;
    private int _suggestionRequestToken;
    // Pre-filling the name in edit mode raises TextChanged; without this we'd fire a
    // transliteration lookup for an already-complete name and a stray Space would replace it.
    private bool _suppressSuggestions = true;

    private string ItemName => NameBox.Text.Trim();
    private string? ItemCode => string.IsNullOrWhiteSpace(CodeBox.Text) ? null : CodeBox.Text.Trim();
    private double PriceValue => double.TryParse(PriceBox.Text, out var p) ? p : 0;
    private long? CategoryId => CategoryCombo.SelectedValue as long?;
    private long? SubCategoryId => SubCategoryCombo.SelectedValue is long v && v != 0 ? v : null;

    public MenuItemModal(string title, IEnumerable<Category> allCategories, Action<MenuItem> onSave, bool isEdit,
        string name = "", double price = 0, string? code = null,
        long? categoryId = null, long? subCategoryId = null,
        long existingId = 0, long existingClientId = 1, string existingType = "veg",
        long existingIsAvailable = 1, long existingIsParcel = 0)
    {
        InitializeComponent();
        TitleText.Text = title;
        _onSave = onSave;
        _isEdit = isEdit;
        _existingId = existingId;
        _existingClientId = existingClientId;
        _existingType = existingType;
        _existingIsAvailable = existingIsAvailable;
        _existingIsParcel = existingIsParcel;
        SaveButton.Content = isEdit ? "Update details" : "Save Menu Item";
        _allCategories = new List<Category>(allCategories);

        var parents = _allCategories.Where(c => !c.ParentId.HasValue || c.ParentId == 0).OrderBy(c => c.SortOrder).ToList();
        CategoryCombo.ItemsSource = parents;

        NameBox.Text = name;
        PriceBox.Text = price > 0 ? price.ToString("0.##") : "";
        CodeBox.Text = code ?? "";

        var initialCategoryId = categoryId ?? parents.FirstOrDefault()?.Id;
        CategoryCombo.SelectedValue = initialCategoryId;
        PopulateSubCategories(initialCategoryId);
        SubCategoryCombo.SelectedValue = subCategoryId ?? 0L;

        UpdatePlaceholder();
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.CaretIndex = NameBox.Text.Length;
            _suppressSuggestions = false;
        };
    }

    private void PopulateSubCategories(long? parentId)
    {
        var list = new List<Category> { new() { Id = 0, Name = "No Sub Category" } };
        if (parentId.HasValue)
            list.AddRange(_allCategories.Where(c => c.ParentId == parentId).OrderBy(c => c.SortOrder));
        SubCategoryCombo.ItemsSource = list;
        SubCategoryCombo.SelectedValue = 0L;
    }

    private void CategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var parentId = CategoryCombo.SelectedValue as long?;
        PopulateSubCategories(parentId);
        SubCategoryCombo.IsEnabled = parentId.HasValue;
    }

    // ── Hindi/English toggle ──
    private void HindiToggle_Click(object sender, RoutedEventArgs e) => SetLangMode(true);
    private void EnglishToggle_Click(object sender, RoutedEventArgs e) => SetLangMode(false);

    private void SetLangMode(bool hindi)
    {
        _hindiMode = hindi;
        HindiToggle.IsChecked = hindi;
        EnglishToggle.IsChecked = !hindi;
        SuggestionsPanel.Visibility = hindi ? Visibility.Visible : Visibility.Collapsed;
        UpdatePlaceholder();
        ClearSuggestions();
    }

    private void UpdatePlaceholder()
    {
        NamePlaceholder.Text = _hindiMode ? "Type in English for Hindi" : "e.g. Masala Chai";
        NamePlaceholder.Visibility = string.IsNullOrEmpty(NameBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Transliteration ──
    private async void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        NamePlaceholder.Visibility = string.IsNullOrEmpty(NameBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (!_hindiMode || _suppressSuggestions) return;

        var words = NameBox.Text.Split(' ');
        var lastWord = words[^1];
        if (string.IsNullOrWhiteSpace(lastWord))
        {
            ClearSuggestions();
            return;
        }

        var token = ++_suggestionRequestToken;
        try
        {
            var url = $"https://inputtools.google.com/request?text={Uri.EscapeDataString(lastWord)}&itc=hi-t-i0-und&num=5&cp=0&cs=1&ie=utf-8&oe=utf-8&app=demopage";
            var json = await _http.GetStringAsync(url);
            if (token != _suggestionRequestToken) return; // a newer keystroke superseded this request

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.GetArrayLength() > 0 && root[0].GetString() == "SUCCESS")
            {
                var sugs = root[1][0][1].EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0).Take(5).ToList();
                ShowSuggestions(sugs);
            }
            else ClearSuggestions();
        }
        catch
        {
            if (token == _suggestionRequestToken) ClearSuggestions();
        }
    }

    private void NameBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_hindiMode || e.Key != Key.Space) return;
        if (SuggestionsList.ItemsSource is not List<string> sugs || sugs.Count == 0) return;
        e.Handled = true;
        ApplySuggestion(sugs[0]);
    }

    private void Suggestion_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is string sug) ApplySuggestion(sug);
    }

    private void ApplySuggestion(string sug)
    {
        var words = NameBox.Text.Split(' ');
        words[^1] = sug;
        NameBox.Text = string.Join(' ', words) + " ";
        NameBox.CaretIndex = NameBox.Text.Length;
        ClearSuggestions();
    }

    private void ShowSuggestions(List<string> sugs)
    {
        SuggestionsList.ItemsSource = sugs;
        NoSuggestionsText.Visibility = sugs.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ClearSuggestions()
    {
        SuggestionsList.ItemsSource = null;
        NoSuggestionsText.Visibility = Visibility.Visible;
    }

    // ── Save/Cancel ──
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show("Item Name is required.", "Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (CategoryCombo.SelectedValue is null)
        {
            MessageBox.Show("Category select karein.", "Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (PriceValue <= 0)
        {
            MessageBox.Show("Ek valid price bharein.", "Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var item = new MenuItem
        {
            Id = _existingId,
            ClientId = _existingClientId,
            Name = ItemName,
            Code = ItemCode,
            Price = PriceValue,
            CategoryId = CategoryId,
            SubCategoryId = SubCategoryId,
            Type = _existingType,
            IsAvailable = _existingIsAvailable,
            IsParcel = _existingIsParcel
        };
        try
        {
            _onSave(item);
        }
        catch (Exception ex)
        {
            // Add-mode calls CatalogRepository.SaveMenuItem, which creates the row on the
            // server synchronously — offline or a server error means the item was never
            // saved anywhere, not even locally, so the dialog must stay open and say so
            // rather than clearing the form as if it worked.
            MessageBox.Show(ex.Message, "Save Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_isEdit)
        {
            DialogResult = true;
            return;
        }

        // Add mode: show a confirmation, reset name/code/price but keep category/subcategory
        // selected, and leave the dialog open for the next item (matches the Electron flow).
        LastAddedName.Text = item.Name;
        LastAddedCode.Text = string.IsNullOrEmpty(item.Code) ? "" : $"({item.Code})";
        LastAddedPrice.Text = $"₹{item.Price:0.##}";
        LastAddedPanel.Visibility = Visibility.Visible;

        NameBox.Clear();
        CodeBox.Clear();
        PriceBox.Clear();
        ClearSuggestions();
        NameBox.Focus();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
