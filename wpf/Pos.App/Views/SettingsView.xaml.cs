using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pos.App.ViewModels;
using Pos.Core.Models;

namespace Pos.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshQrPreview();
    }

    private SettingsViewModel? Vm => DataContext as SettingsViewModel;

    /// <summary>
    /// Wraps a catalog CREATE. Creating a row calls the server synchronously (see
    /// CatalogRepository) — if the till is offline or the server errors, the row is never
    /// written locally either, so the operator needs to know it didn't happen rather than
    /// silently losing the new item.
    /// </summary>
    private static void TrySave(Action save)
    {
        try { save(); }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Save Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    private static T? Item<T>(object sender) where T : class => (sender as FrameworkElement)?.DataContext as T;

    // ── Category ──
    private void AddCategory_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var dlg = new CatalogEditModal("category", "Add Category", sortOrder: Vm.Categories.Count + 1) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            TrySave(() => Vm.SaveCategory(new Category { ClientId = 1, Name = dlg.ItemName, SortOrder = dlg.SortOrderValue }));
    }

    private void EditCategory_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null || Item<CategoryRow>(sender) is not { } row) return;
        var c = row.Category;
        var dlg = new CatalogEditModal("category", row.IsParent ? "Edit Category" : "Edit Subcategory", name: c.Name, sortOrder: c.SortOrder) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            Vm.SaveCategory(new Category { Id = c.Id, ClientId = c.ClientId, Name = dlg.ItemName, ParentId = c.ParentId, SortOrder = dlg.SortOrderValue });
    }

    private void AddSubcategory_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var parents = Vm.Categories.Where(c => !c.ParentId.HasValue || c.ParentId == 0).ToList();
        if (parents.Count == 0)
        {
            MessageBox.Show("Pehle ek Parent Category banayein.", "No Parent Category", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var dlg = new CatalogEditModal("subcategory", "Add Subcategory", parentCategories: parents) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            TrySave(() => Vm.SaveCategory(new Category { ClientId = 1, Name = dlg.ItemName, ParentId = dlg.ParentCategoryId, SortOrder = Vm.Categories.Count + 1 }));
    }

    private void DeleteCategoryRow_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null || Item<CategoryRow>(sender) is not { } row) return;
        var c = row.Category;
        var msg = row.IsParent
            ? $"Kya aap sach me category \"{c.Name}\" ko delete karna chahte hain? Isse is category ke sabhi main/sub items unassigned ho sakte hain."
            : $"Kya aap sach me subcategory \"{c.Name}\" ko delete karna chahte hain? Iske andar ke sabhi items auto-transfer hokar iski main category me chale jayenge.";
        if (MessageBox.Show(msg, row.IsParent ? "Delete Category" : "Delete Subcategory", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            Vm.DeleteCategoryWithTransfer(c);
    }

    // ── Dining Area ──
    private void AddArea_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var dlg = new CatalogEditModal("area", "Add Dining Area") { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            TrySave(() => Vm.SaveArea(new DiningArea { ClientId = 1, Name = dlg.ItemName }));
    }

    private void EditArea_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null || Item<DiningArea>(sender) is not { } a) return;
        var dlg = new CatalogEditModal("area", "Edit Dining Area", name: a.Name) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            Vm.SaveArea(new DiningArea { Id = a.Id, ClientId = a.ClientId, Name = dlg.ItemName, SortOrder = a.SortOrder });
    }

    // ── Table ──
    private void AddTable_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var dlg = new CatalogEditModal("table", "Add Table", areas: Vm.Areas) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            TrySave(() => Vm.SaveTable(new TableEdit { ClientId = 1, TableNumber = dlg.ItemName, AreaId = dlg.AreaId }));
    }

    private void EditTable_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null || Item<TableEdit>(sender) is not { } t) return;
        var dlg = new CatalogEditModal("table", "Edit Table", name: t.TableNumber, areaId: t.AreaId, areas: Vm.Areas) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            Vm.SaveTable(new TableEdit { Id = t.Id, ClientId = t.ClientId, TableNumber = dlg.ItemName, AreaId = dlg.AreaId });
    }

    // ── GST ──
    private void AddGst_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var dlg = new CatalogEditModal("gst", "Add GST Rate") { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            TrySave(() => Vm.SaveGstRate(new GstRate { ClientId = 1, Name = dlg.ItemName, Rate = dlg.RateValue }));
    }

    private void EditGst_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null || Item<GstRate>(sender) is not { } g) return;
        var dlg = new CatalogEditModal("gst", "Edit GST Rate", name: g.Name, rate: g.Rate) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            Vm.SaveGstRate(new GstRate { Id = g.Id, ClientId = g.ClientId, Name = dlg.ItemName, Rate = dlg.RateValue });
    }

    // ── Menu item ──
    private void AddMenu_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var dlg = new MenuItemModal("Create Menu Item", Vm.Categories, Vm.SaveMenuItem, isEdit: false)
            { Owner = Window.GetWindow(this) };
        dlg.ShowDialog();
    }

    private void EditMenu_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null || Item<MenuItemRow>(sender) is not { } row) return;
        var m = row.Item;
        var dlg = new MenuItemModal("Edit Menu Item Details", Vm.Categories, Vm.SaveMenuItem, isEdit: true,
            name: m.Name, price: m.Price, code: m.Code, categoryId: m.CategoryId, subCategoryId: m.SubCategoryId,
            existingId: m.Id, existingClientId: m.ClientId, existingType: m.Type,
            existingIsAvailable: m.IsAvailable, existingIsParcel: m.IsParcel) { Owner = Window.GetWindow(this) };
        dlg.ShowDialog();
    }

    // ── Security PIN ──
    private void EyeNewPin_Checked(object sender, RoutedEventArgs e) => PbNewPin.PasswordChar = '\0';
    private void EyeNewPin_Unchecked(object sender, RoutedEventArgs e) => PbNewPin.PasswordChar = '●';
    private void EyeConfirmPin_Checked(object sender, RoutedEventArgs e) => PbConfirmPin.PasswordChar = '\0';
    private void EyeConfirmPin_Unchecked(object sender, RoutedEventArgs e) => PbConfirmPin.PasswordChar = '●';

    private void UpdatePin_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if (Vm.UpdatePin(PbNewPin.Password, PbConfirmPin.Password))
        {
            PbNewPin.Clear();
            PbConfirmPin.Clear();
        }
    }

    // ── Printer tab ──
    private void TestPrint_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var is58 = Vm.PaperSize.Contains("58");
        double width = is58 ? 200 : 280;
        int cols = is58 ? 32 : 42;

        var pd = new PrintDialog();
        try
        {
            if (!string.IsNullOrWhiteSpace(Vm.SelectedPrinter)
                && !Vm.SelectedPrinter.StartsWith("Default Thermal", StringComparison.OrdinalIgnoreCase))
            {
                var server = new System.Printing.LocalPrintServer();
                pd.PrintQueue = server.GetPrintQueue(Vm.SelectedPrinter);
            }
            else if (pd.ShowDialog() != true) return;
        }
        catch
        {
            if (pd.ShowDialog() != true) return;
        }

        var doc = new FlowDocument
        {
            PageWidth = width, PageHeight = 5000, ColumnWidth = width,
            PagePadding = new Thickness(6, 8, 6, 8),
            FontFamily = new FontFamily("Consolas"), FontSize = 9, Foreground = Brushes.Black
        };
        void Add(string t, double size = 9, bool bold = false, TextAlignment a = TextAlignment.Left)
            => doc.Blocks.Add(new Paragraph(new Run(t)) { FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, TextAlignment = a, Margin = new Thickness(0), LineHeight = size + 3 });

        Add(Vm.StoreName, 15, true, TextAlignment.Center);
        Add("TEST PRINT", 9, false, TextAlignment.Center);
        Add(new string('-', cols));
        Add($"Printer : {Vm.SelectedPrinter}");
        Add($"Paper   : {Vm.PaperSize}");
        Add($"Copies  : {Vm.PrintCopies}");
        Add($"Time    : {DateTime.Now:dd-MMM-yyyy hh:mm tt}");
        Add(new string('-', cols));
        Add("Printer connection is working correctly.", 9, false, TextAlignment.Center);
        Add(new string('-', cols));

        var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
        paginator.PageSize = new Size(width, 5000);
        pd.PrintDocument(paginator, "Test Print");
    }

    private void UploadQr_Click(object sender, MouseButtonEventArgs e)
    {
        if (Vm is null) return;
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg" };
        if (dlg.ShowDialog() != true) return;

        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var dir = Path.Combine(docs, "ChayChaupalPOS", "qr");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, "upi-qr" + Path.GetExtension(dlg.FileName));
        File.Copy(dlg.FileName, dest, true);

        Vm.QrImagePath = dest;
        RefreshQrPreview();
    }

    private void RefreshQrPreview()
    {
        if (Vm is null || string.IsNullOrWhiteSpace(Vm.QrImagePath) || !File.Exists(Vm.QrImagePath))
        {
            QrPreview.Visibility = Visibility.Collapsed;
            QrPlaceholder.Visibility = Visibility.Visible;
            return;
        }
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(Vm.QrImagePath);
        bmp.EndInit();
        QrPreview.Source = bmp;
        QrPreview.Visibility = Visibility.Visible;
        QrPlaceholder.Visibility = Visibility.Collapsed;
    }

    // ── Shortcuts tab ──
    private void EditShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null || Item<ShortcutItem>(sender) is not { } s) return;
        var dlg = new ShortcutEditModal(s.Action, s.Key) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            Vm.UpdateShortcutKey(s, dlg.NewKey);
    }
}
