using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Pos.App.Services;
using Pos.App.ViewModels;
using Pos.App.Views;
using Pos.Core.Models;
using FoodItem = Pos.Core.Models.MenuItem;
using TableView = Pos.Core.Models.TableView;
using Microsoft.Extensions.DependencyInjection;
using Pos.Core.Repositories;

namespace Pos.App;

public partial class MainWindow : Window
{
    private CartLine? _lastAddedLine;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += MainWindow_DataContextChanged;
    }

    /// <summary>The window handle exists from here on, which is what DWM needs to recolour
    /// the title bar.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Helpers.WindowTheme.ApplyDarkTitleBar(this);
    }

    private void MainWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldVm)
        {
            oldVm.PropertyChanged -= ViewModel_PropertyChanged;
        }
        if (e.NewValue is MainViewModel newVm)
        {
            newVm.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedTable) ||
            e.PropertyName == nameof(MainViewModel.SelectedCategoryTab) ||
            e.PropertyName == nameof(MainViewModel.SelectedArea) ||
            e.PropertyName == nameof(MainViewModel.CenterMode))
        {
            RefocusSearchInput();
        }
    }

    private MouseButtonEventHandler? _activationClickHandler;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    /// <summary>Physical left-button state. WPF's own <c>Mouse.LeftButton</c> is still
    /// "released" while the window is activating — its input state hasn't caught up with the
    /// OS message yet — so the activating click has to be detected at the source.</summary>
    private static bool IsLeftMouseDown() => (GetAsyncKeyState(0x01) & 0x8000) != 0;

    /// <summary>
    /// Puts the keyboard back in the search box when the operator returns to the app.
    ///
    /// The click that re-activates the window has to keep working, though: grabbing focus
    /// while that click was still in progress swallowed it, so the first click on an
    /// inactive window only activated it and the Table/Menu tabs appeared to need two
    /// clicks. When activation arrives mid-click, the refocus waits for the button to come
    /// back up.
    /// </summary>
    private void Window_Activated(object? sender, EventArgs e)
    {
        if (IsLeftMouseDown())
        {
            _activationClickHandler ??= RefocusAfterActivationClick;
            RemoveHandler(MouseLeftButtonUpEvent, _activationClickHandler);
            // handledEventsToo: whatever was clicked (a Button, say) marks the event handled.
            AddHandler(MouseLeftButtonUpEvent, _activationClickHandler, handledEventsToo: true);
            return;
        }

        RefocusSearchInput();
    }

    private void RefocusAfterActivationClick(object sender, MouseButtonEventArgs e)
    {
        if (_activationClickHandler != null)
        {
            RemoveHandler(MouseLeftButtonUpEvent, _activationClickHandler);
        }
        RefocusSearchInput();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshSessionUser();
        RefocusSearchInput();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            try
            {
                var settings = App.Services.GetRequiredService<AppSettingsRepository>();
                var savedWidth = settings.GetJson<double>("pos_wpf_order_panel_width");
                if (savedWidth >= 300 && savedWidth <= 1000)
                {
                    ColOrderPanel.Width = new GridLength(savedWidth);
                }

                var isCollapsed = settings.GetJson<bool>("pos_wpf_sidebar_collapsed");
                if (isCollapsed)
                {
                    SetSidebarCollapsed(true, saveSetting: false);
                }

                // Get the other screens ready while the till is sitting idle, so the first
                // visit to each one is as instant as every later visit.
                Helpers.LazyContent.WarmUp(Dispatcher);
            }
            catch { }
        }));
    }

    private void GridSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        try
        {
            var settings = App.Services.GetRequiredService<AppSettingsRepository>();
            settings.SetJson("pos_wpf_order_panel_width", ColOrderPanel.ActualWidth);
        }
        catch { }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            var settings = App.Services.GetRequiredService<AppSettingsRepository>();
            settings.SetJson("pos_wpf_order_panel_width", ColOrderPanel.ActualWidth);
        }
        catch { }
    }

    /// <summary>The suggestions dropdown is an in-window overlay, so it has none of a Popup's
    /// StaysOpen dismissal. Close it when the search box actually loses the keyboard.</summary>
    private void SearchInput_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.IsSearchPopupOpen = false;
        }
    }

    private void SearchInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (string.IsNullOrWhiteSpace(vm.SearchText) || vm.Suggestions.Count == 0)
        {
            if (e.Key == Key.Down)
            {
                if (CartListBox.Items.Count > 0)
                {
                    e.Handled = true;
                    NavigateCartSelection(1);
                }
                return;
            }
            else if (e.Key == Key.Up)
            {
                if (CartListBox.Items.Count > 0)
                {
                    e.Handled = true;
                    NavigateCartSelection(-1);
                }
                return;
            }
        }

        if (e.Key == Key.Down)
        {
            e.Handled = true;
            if (!vm.IsSearchPopupOpen && vm.Suggestions.Count > 0)
            {
                vm.IsSearchPopupOpen = true;
            }
            vm.SelectNextMenuItem();
            if (vm.SelectedMenuItem != null)
            {
                PopupListBox.ScrollIntoView(vm.SelectedMenuItem);
            }
        }
        else if (e.Key == Key.Up)
        {
            e.Handled = true;
            vm.SelectPreviousMenuItem();
            if (vm.SelectedMenuItem != null)
            {
                PopupListBox.ScrollIntoView(vm.SelectedMenuItem);
            }
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;

            // Nothing typed means nothing to add. Without this, Enter on an empty box
            // acted on whatever the suggestion list happened to hold.
            if (string.IsNullOrWhiteSpace(vm.SearchText))
            {
                return;
            }

            FoodItem? itemToAdd = null;

            if (vm.SelectedMenuItem != null)
            {
                itemToAdd = vm.SelectedMenuItem;
            }
            else if (vm.Suggestions.Count > 0)
            {
                itemToAdd = vm.Suggestions[0];
            }

            if (itemToAdd != null)
            {
                _lastAddedLine = vm.AddAndReturnLine(itemToAdd);
                FocusCartQty(_lastAddedLine);
            }
        }
        else if (e.Key == Key.Escape)
        {
            vm.IsSearchPopupOpen = false;
        }
    }

    private void FocusCartQty(CartLine line) => FocusCartEditor(line, "QtyInput");

    private void FocusCartPrice(CartLine line) => FocusCartEditor(line, "PriceInput");

    /// <summary>Moves the keyboard into one of a cart row's inline editors. Deferred, because
    /// the row's container may still be being generated when this is called after an add.</summary>
    private void FocusCartEditor(CartLine line, string editorName)
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
        {
            var container = FindVisualChildWithItem(CartListBox, line);
            if (container != null)
            {
                var box = FindChild<TextBox>(container, editorName);
                if (box != null)
                {
                    box.Focus();
                    box.SelectAll();
                }
            }
        }));
    }

    private void QtyInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (DataContext is MainViewModel vm)
            {
                vm.SearchText = "";
            }
            SearchInput.Focus();
            SearchInput.SelectAll();
        }
    }

    /// <summary>Set while focus is moving into a qty box because the user typed a digit on a
    /// selected cart row. That digit has already replaced the quantity, so the box must land
    /// with the caret at the end — selecting the text instead would make the next digit
    /// overwrite the first, so "12" could never be typed.</summary>
    private bool _qtyTypedEntry;

    private void QtyInput_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;

        var caretToEnd = _qtyTypedEntry;
        _qtyTypedEntry = false;
        tb.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (caretToEnd)
            {
                tb.CaretIndex = tb.Text.Length;
            }
            else
            {
                tb.SelectAll();
            }
        }));
    }

    private void QtyInput_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is CartLine line)
        {
            if (string.IsNullOrWhiteSpace(line.QtyText) || line.Qty <= 0)
            {
                line.QtyText = line.Qty > 0 ? line.Qty.ToString() : "1";
            }
        }
        RefocusSearchInput();
    }

    private void QtyInput_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !System.Text.RegularExpressions.Regex.IsMatch(e.Text, "^[0-9]+$");
    }

    /// <summary>
    /// Swallows the click that focuses a cart editor, so the whole value stays selected and
    /// the next keystroke replaces it. Without this the click lands after GotFocus and drops
    /// a caret mid-number, so typing a new price appended to the old one (40 → 403030).
    /// </summary>
    private void CellEditor_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBox tb && !tb.IsKeyboardFocusWithin)
        {
            tb.Focus();
            e.Handled = true;
        }
    }

    private void PriceInput_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;

        // Deferred: selecting inline is undone by the rest of the focus/click processing.
        tb.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle,
            new Action(tb.SelectAll));
    }

    /// <summary>Prices are numbers; letters would just fail to parse and leave a stale value.</summary>
    private void PriceInput_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !System.Text.RegularExpressions.Regex.IsMatch(e.Text, @"^[0-9.]+$");
    }

    private void PriceInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (DataContext is MainViewModel vm)
            {
                vm.SearchText = "";
            }
            SearchInput.Focus();
            SearchInput.SelectAll();
        }
    }

    private void PopupListBox_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (vm.SelectedMenuItem != null)
        {
            _lastAddedLine = vm.AddAndReturnLine(vm.SelectedMenuItem);
        }
        RefocusSearchInput();
    }

    /// <summary>Set while a table context menu is open — see <see cref="RefocusSearchInput"/>.</summary>
    private bool _tableMenuOpen;

    public void RefocusSearchInput()
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
        {
            // Opening a context menu moves focus to the menu and re-activates the window,
            // which would land here and yank focus back — closing the menu the instant it
            // appeared. Leave focus alone while a menu is up.
            if (_tableMenuOpen) return;

            // Same for a cart row the user arrowed onto: +/- and Delete only reach the row
            // while the row itself holds focus, so stealing it back to the search box left
            // arrow navigation able to move the highlight but do nothing with it.
            if (FindAncestor<ListBoxItem>(Keyboard.FocusedElement as DependencyObject) is { } row
                && ItemsControl.ItemsControlFromItemContainer(row) == CartListBox)
            {
                return;
            }

            if (Keyboard.FocusedElement is not TextBox || Keyboard.FocusedElement == SearchInput)
            {
                SearchInput.Focus();
                SearchInput.SelectAll();
            }
        }));
    }

    private bool _isSidebarCollapsed = false;

    private void BtnToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        SetSidebarCollapsed(!_isSidebarCollapsed, saveSetting: true);
    }

    private void SetSidebarCollapsed(bool collapsed, bool saveSetting = false)
    {
        _isSidebarCollapsed = collapsed;

        ColSidebar.Width = new GridLength(collapsed ? 68 : 256);
        SidebarDockPanel.Margin = collapsed ? new Thickness(10, 20, 10, 20) : new Thickness(16, 20, 16, 20);
        TxtSidebarToggleIcon.Text = collapsed ? "\uE76C" : "\uE76B";

        if (collapsed)
        {
            SidebarLogoContainer.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetRow(BtnToggleSidebar, 1);
            BtnToggleSidebar.HorizontalAlignment = HorizontalAlignment.Center;
            BtnToggleSidebar.Margin = new Thickness(0, 10, 0, 0);
            SidebarHeaderGrid.Margin = new Thickness(0, 0, 0, 16);
        }
        else
        {
            SidebarLogoContainer.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetRow(BtnToggleSidebar, 0);
            BtnToggleSidebar.HorizontalAlignment = HorizontalAlignment.Right;
            BtnToggleSidebar.Margin = new Thickness(0, 0, 0, 0);
            SidebarHeaderGrid.Margin = new Thickness(0, 0, 0, 26);
        }

        var visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarExpandedHeader.Visibility = visibility;
        // Collapsed, the logout button keeps its icon and drops the word, like the nav items.
        SidebarExpandedFooterText.Visibility = visibility;

        ToggleNavTextVisibility(SidebarNavPanel, visibility);
        ToggleNavTextVisibility(SidebarBottomNavPanel, visibility);

        if (saveSetting)
        {
            try
            {
                var settings = App.Services.GetRequiredService<AppSettingsRepository>();
                settings.SetJson("pos_wpf_sidebar_collapsed", _isSidebarCollapsed);
            }
            catch { }
        }
    }

    /// <summary>
    /// Hands the till to the next operator: back to the sign-in screen, not out of the app.
    ///
    /// The window is hidden rather than closed so the shift change keeps whatever is on the
    /// counter — a part-typed order does not belong in the bin because the person billing it
    /// changed. On a machine whose staff list has never synced there is nobody to sign in as,
    /// so closing is still the only honest thing logout can do there.
    /// </summary>
    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        var auth = App.Services.GetRequiredService<AuthRepository>();
        var canSignIn = auth.HasUsers();

        var confirmed = ThemeMessageBox.Confirm(
            this,
            canSignIn ? $"{Session.DisplayName} ko logout karein?" : "App band karein?",
            "Logout");
        if (!confirmed)
        {
            return;
        }

        if (!canSignIn)
        {
            Application.Current.Shutdown();
            return;
        }

        Session.SignOut();
        Hide();

        if (LoginWindow.Authenticate())
        {
            // The counter can change brand at a shift change, not just at startup — point the
            // repositories at the business that just signed in before anything is billed.
            App.ApplySignedInClient();
            RefreshSessionUser();
            Show();
            Activate();
            Focus();
        }
        else
        {
            Application.Current.Shutdown();
        }
    }

    /// <summary>Names the counter above the logout button, so the sidebar says which business
    /// this shift is billing for — the one thing that decides whose takings the day's sales
    /// land under. Falls back to the operator's name on a till whose profile hasn't synced a
    /// business name yet, so the row is never blank while someone is signed in.</summary>
    private void RefreshSessionUser()
    {
        SidebarUserRow.Visibility = Session.IsSignedIn ? Visibility.Visible : Visibility.Collapsed;
        SidebarUserText.Text = Session.BusinessName is { Length: > 0 } counter ? counter : Session.DisplayName;
        BtnLogout.ToolTip = Session.IsSignedIn ? $"Logout — {Session.DisplayName}" : "Logout";

        // Reports is a singleton that outlives a shift, so the staff filter has to be pointed
        // at whoever just signed in — otherwise the new operator opens it on the last one's sales.
        try
        {
            App.Services.GetRequiredService<ReportsViewModel>().SyncToSession();
        }
        catch { }
    }

    private static void ToggleNavTextVisibility(DependencyObject parent, Visibility visibility)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBlock tb && tb.Tag?.ToString() == "NavText")
            {
                tb.Visibility = visibility;
            }
            ToggleNavTextVisibility(child, visibility);
        }
    }

    private void Extra_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        vm.IsAddingExtra = !vm.IsAddingExtra;
        if (vm.IsAddingExtra)
        {
            TxtExtraPrice.Text = "";
            TxtExtraRemarks.Text = "";
            Dispatcher.BeginInvoke(new Action(() => TxtExtraPrice.Focus()), System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private void ExtraInput_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            tb.SelectAll();
        }
    }

    private void TxtExtraPrice_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            TxtExtraRemarks.Focus();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            vm.IsAddingExtra = false;
        }
    }

    private void TxtExtraRemarks_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            BtnAddExtra_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            vm.IsAddingExtra = false;
        }
    }

    private void BtnAddExtra_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (double.TryParse(TxtExtraPrice.Text.Trim(), out double price) && price > 0)
        {
            var remarks = string.IsNullOrWhiteSpace(TxtExtraRemarks.Text) ? "extra mark" : TxtExtraRemarks.Text.Trim();
            var line = new CartLine
            {
                ItemId = -1,
                Name = remarks,
                Price = price,
                Qty = 1,
                PriceText = price.ToString("0.##")
            };
            vm.AddAndReturnLineCustom(line);
            vm.IsAddingExtra = false;
        }
        else
        {
            Views.ThemeMessageBox.Show(this, "Please enter a valid price greater than 0.", "Invalid Price", "warning");
            TxtExtraPrice.Focus();
            TxtExtraPrice.SelectAll();
        }
    }

    private void BtnCancelExtra_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        vm.IsAddingExtra = false;
    }

    private void Parcel_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (CartListBox.SelectedItem is CartLine line)
        {
            if (line.Qty == 1)
            {
                line.IsParcel = !line.IsParcel;
                vm.DisplayCartItemsView?.Refresh();
            }
            else if (line.Qty > 1)
            {
                var dlg = new Views.ParcelQtyModal(line.Qty) { Owner = this };
                if (dlg.ShowDialog() == true && dlg.SelectedQty > 0)
                {
                    long qtyToToggle = dlg.SelectedQty;
                    if (qtyToToggle == line.Qty)
                    {
                        line.IsParcel = !line.IsParcel;
                    }
                    else
                    {
                        line.Qty -= qtyToToggle;
                        var oppositeParcel = !line.IsParcel;
                        var existing = vm.Cart.FirstOrDefault(l => l.ItemId == line.ItemId && 
                                                                   l.Name == line.Name &&
                                                                   l.IsParcel == oppositeParcel && 
                                                                   l.IsSaved == line.IsSaved);
                        if (existing != null)
                        {
                            existing.Qty += qtyToToggle;
                        }
                        else
                        {
                            var newLine = new CartLine
                            {
                                ItemId = line.ItemId,
                                Name = line.Name,
                                Price = line.Price,
                                Qty = qtyToToggle,
                                IsParcel = oppositeParcel,
                                IsSaved = line.IsSaved
                            };
                            vm.Cart.Add(newLine);
                        }
                    }
                    vm.DisplayCartItemsView?.Refresh();
                }
            }
        }
        else
        {
            vm.IsParcelMode = !vm.IsParcelMode;
            Views.ThemeMessageBox.Show(this, vm.IsParcelMode ? "Global Parcel Mode: Enabled" : "Global Parcel Mode: Disabled", "Parcel Mode", "info");
        }
    }

    private void Transfer_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.Cart.Count == 0) return;

        var freeTables = vm.Tables.Where(t => t.Status == "available").ToList();
        if (freeTables.Count == 0)
        {
            Views.ThemeMessageBox.Show(this, "No available free tables to transfer to.", "Transfer Order", "warning");
            return;
        }

        var currentLabel = vm.SelectedTable?.TableNumber ?? "Quick Bill";
        var dlg = new Views.TransferTableModal(currentLabel, freeTables) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.SelectedTargetTable != null)
        {
            vm.TransferTableOrder(dlg.SelectedTargetTable.Id);
            RefocusSearchInput();
        }
    }

    private void ChangeTable_Click(object sender, RoutedEventArgs e)
    {
        Transfer_Click(sender, e);
    }

    private void MergeTable_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (vm.SelectedTable == null)
        {
            Views.ThemeMessageBox.Show(this, "Please select an active table first to merge orders into.", "Merge Table", "warning");
            return;
        }

        var occupiedTables = vm.Tables.Where(t => t.Status == "occupied" && t.Id != vm.SelectedTable.Id).ToList();
        if (occupiedTables.Count == 0)
        {
            Views.ThemeMessageBox.Show(this, "No other occupied tables available to merge.", "Merge Table", "warning");
            return;
        }

        var dlg = new Views.MergeTableModal(vm.SelectedTable.TableNumber, occupiedTables) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.SelectedSourceTable != null)
        {
            vm.MergeTableOrder(dlg.SelectedSourceTable.Id, vm.SelectedTable.Id);
            RefocusSearchInput();
        }
    }

    private void SplitTable_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (vm.SelectedTable == null || vm.Cart.Count == 0)
        {
            Views.ThemeMessageBox.Show(this, "Please select an active table order with items to split.", "Split Table", "warning");
            return;
        }

        var freeTables = vm.Tables.Where(t => t.Status == "available" && t.Id != vm.SelectedTable.Id).ToList();
        if (freeTables.Count == 0)
        {
            Views.ThemeMessageBox.Show(this, "No available free tables to split order to.", "Split Table", "warning");
            return;
        }

        var dlg = new Views.SplitTableModal(vm.SelectedTable.TableNumber, vm.Cart, freeTables) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.SelectedTargetTable != null && dlg.SelectedSplitLines.Count > 0)
        {
            vm.SplitTableOrder(dlg.SelectedTargetTable.Id, dlg.SelectedSplitLines);
            RefocusSearchInput();
        }
    }

    /// <summary>
    /// A table is "free" only when its status says available/free — every other status
    /// ("occupied", "active", "running", …) means there's a running bill. Checking for
    /// == "occupied" alone wrongly treats active/running tables as empty.
    /// </summary>
    private static bool IsTableFree(TableView table)
        => string.Equals(table.Status, "available", StringComparison.OrdinalIgnoreCase)
           || string.Equals(table.Status, "free", StringComparison.OrdinalIgnoreCase)
           || string.IsNullOrWhiteSpace(table.Status);

    /// <summary>
    /// One ContextMenu is shared by the whole table grid (a single instance can't be
    /// attached per-card), so resolve the right-clicked table here and hand it to the
    /// menu as its DataContext. A free table has no bill to change/merge/split/settle/
    /// delete, so it gets no menu at all instead of a list of dead options.
    /// </summary>
    private void TablesListBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // OriginalSource here is the ListBox that owns the menu, not the card under the
        // cursor, so hit-test from the mouse to find which table was right-clicked.
        var hit = FindAncestor<ListBoxItem>(Mouse.DirectlyOver as DependencyObject)
                  ?? FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        var table = hit?.DataContext as TableView;
        if (table == null || IsTableFree(table))
        {
            e.Handled = true;
            return;
        }

        // Do NOT set SelectedTable here: that raises PropertyChanged, which refocuses the
        // search box and instantly closes the menu we're about to open. The MenuItem Click
        // handlers already select the table via GetTargetTableFromMenuItem.
        if (sender is FrameworkElement fe && fe.ContextMenu is { } menu)
        {
            menu.DataContext = table;
            _tableMenuOpen = true;
            menu.Closed -= TableMenu_Closed;
            menu.Closed += TableMenu_Closed;
        }
    }

    private void TableMenu_Closed(object sender, RoutedEventArgs e)
    {
        _tableMenuOpen = false;
        RefocusSearchInput();
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match) return match;
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }

    private TableView? GetTargetTableFromMenuItem(object sender)
    {
        if (sender is System.Windows.Controls.MenuItem mi && mi.DataContext is TableView table)
        {
            if (DataContext is MainViewModel vm && vm.SelectedTable != table)
            {
                vm.SelectedTable = table;
            }
            return table;
        }
        return null;
    }

    private void TableContext_ChangeTable_Click(object sender, RoutedEventArgs e)
    {
        GetTargetTableFromMenuItem(sender);
        ChangeTable_Click(sender, e);
    }

    private void TableContext_MergeTable_Click(object sender, RoutedEventArgs e)
    {
        GetTargetTableFromMenuItem(sender);
        MergeTable_Click(sender, e);
    }

    private void TableContext_SplitTable_Click(object sender, RoutedEventArgs e)
    {
        GetTargetTableFromMenuItem(sender);
        SplitTable_Click(sender, e);
    }

    private void TableContext_SettleBill_Click(object sender, RoutedEventArgs e)
    {
        var table = GetTargetTableFromMenuItem(sender);
        if (table == null || DataContext is not MainViewModel vm) return;

        if (IsTableFree(table))
        {
            Views.ThemeMessageBox.Show(this, $"Table {table.TableNumber} is already available/empty.", "Settle Bill", "warning");
            return;
        }

        vm.SelectedTable = table;
        vm.SettleOrder();
        RefocusSearchInput();
    }

    private void TableContext_DeleteBill_Click(object sender, RoutedEventArgs e)
    {
        var table = GetTargetTableFromMenuItem(sender);
        if (table == null || DataContext is not MainViewModel vm) return;

        if (IsTableFree(table))
        {
            Views.ThemeMessageBox.Show(this, $"Table {table.TableNumber} is already available/empty.", "Delete Bill", "warning");
            return;
        }

        if (Views.ThemeMessageBox.Confirm(this, $"WARNING: Are you sure you want to CANCEL & DELETE the bill for Table {table.TableNumber}?\nThis action cannot be undone.", "Confirm Delete Bill", "danger"))
        {
            vm.DeleteTableOrder(table.Id);
            RefocusSearchInput();
        }
    }

    private void Note_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (vm.Cart.Count > 0)
        {
            var modal = new Views.SaveNoteModal { Owner = this };
            if (vm.EditingNoteId != null)
            {
                modal.SetEditingMode(vm.EditingCustomerName, vm.EditingCustomerMobile, vm.EditingTargetTime);
            }

            if (modal.ShowDialog() == true)
            {
                vm.SaveCurrentOrderToNote(modal.CustomerName, modal.CustomerMobile, modal.TargetTime);
                RefocusSearchInput();
            }
        }
        else
        {
            OpenSavedNotesModal(vm);
        }
    }

    private void OpenSavedNotesModal(MainViewModel vm)
    {
        var modal = new Views.SavedNotesModal(vm.QuickNotesRepo) { Owner = this };
        if (modal.ShowDialog() == true && modal.Result.SelectedNote != null)
        {
            var note = modal.Result.SelectedNote;
            if (modal.Result.Action == Views.NoteActionType.Edit)
            {
                EditQuickNoteFromNotesView(note);
            }
            else if (modal.Result.Action == Views.NoteActionType.Transfer)
            {
                var freeTables = vm.Tables.Where(t => t.Status == "available").ToList();
                if (freeTables.Count == 0)
                {
                    Views.ThemeMessageBox.Show(this, "No available free tables to transfer this note order.", "Transfer Note Order", "warning");
                    return;
                }

                var transferDlg = new Views.TransferTableModal(string.IsNullOrWhiteSpace(note.CustomerName) ? "Quick Note" : note.CustomerName, freeTables) { Owner = this };
                if (transferDlg.ShowDialog() == true && transferDlg.SelectedTargetTable != null)
                {
                    vm.TransferNoteToTable(note, transferDlg.SelectedTargetTable.Id);
                    RefocusSearchInput();
                }
            }
        }
    }

    public void EditQuickNoteFromNotesView(QuickNote note)
    {
        if (DataContext is not MainViewModel vm) return;
        vm.LoadNoteToCart(note);
        vm.ActiveScreen = "Orders";
        RefocusSearchInput();
    }

    public void TransferQuickNoteFromNotesView(QuickNote note)
    {
        if (DataContext is not MainViewModel vm) return;
        var freeTables = vm.Tables.Where(t => t.Status == "available").ToList();
        if (freeTables.Count == 0)
        {
            Views.ThemeMessageBox.Show(this, "No available free tables to transfer this note order.", "Transfer Note Order", "warning");
            return;
        }

        var transferDlg = new Views.TransferTableModal(string.IsNullOrWhiteSpace(note.CustomerName) ? "Quick Note" : note.CustomerName, freeTables) { Owner = this };
        if (transferDlg.ShowDialog() == true && transferDlg.SelectedTargetTable != null)
        {
            vm.TransferNoteToTable(note, transferDlg.SelectedTargetTable.Id);
            vm.ActiveScreen = "Orders";
            RefocusSearchInput();
        }
    }

    public void DeleteQuickNoteFromNotesView(QuickNote note)
    {
        if (DataContext is not MainViewModel vm) return;
        var displayName = string.IsNullOrWhiteSpace(note.CustomerName) ? $"Quick Note #{note.Id}" : note.CustomerName;
        if (Views.ThemeMessageBox.Confirm(this, $"Are you sure you want to DELETE note for '{displayName}'?", "Confirm Delete Note", "danger"))
        {
            var notesRepo = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<QuickNotesRepository>(App.Services);
            notesRepo.DeleteNote(note.Id);
            vm.Notes.LoadQuickNotes();
            vm.HasSavedNotes = vm.Notes.HasQuickNotes;
        }
    }

    /// <summary>
    /// Settles straight away — quick bill or table, no dialog and no confirmation. Payment is
    /// taken as cash for the full amount, which is the common case at the counter; anything
    /// else would put a popup between the operator and the next customer.
    /// </summary>
    private void Settle_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.Cart.Count == 0) return;

        vm.SettleOrder();
        RefocusSearchInput();
    }

    private void Discount_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (vm.Cart.Count == 0)
        {
            Views.ThemeMessageBox.Show(this, "Pehle cart me item add karein.", "Discount", "warning");
            return;
        }

        var dlg = new Views.DiscountModal(vm.Subtotal, vm.DiscountType, vm.DiscountValue) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            vm.SetDiscount(dlg.DiscountType, dlg.DiscountValue);
        }
        RefocusSearchInput();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        // Auto-focus SearchInput when typing any printable key outside of another TextBox
        if (Keyboard.FocusedElement is not TextBox)
        {
            if ((e.Key >= Key.A && e.Key <= Key.Z) ||
                (e.Key >= Key.D0 && e.Key <= Key.D9) ||
                (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9) ||
                e.Key == Key.Space || e.Key == Key.Back)
            {
                SearchInput.Focus();
            }
        }

        // Hotkeys resolve through Settings → Shortcuts, so user remaps actually take effect
        // (these were previously hard-coded, which made that whole tab decorative).
        var sc = vm.Settings;
        if (sc.ShortcutMatches("item_search", e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
            SearchInput.Focus();
        }
        else if (sc.ShortcutMatches("save_kot", e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
            vm.SaveKot();
        }
        else if (sc.ShortcutMatches("print_kot", e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
            vm.PrintKot();
        }
        else if (sc.ShortcutMatches("print_bill", e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
            vm.PrintBill();
        }
        else if (sc.ShortcutMatches("settle_bill", e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
            Settle_Click(sender, e);
        }
        else if (sc.ShortcutMatches("parcel", e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
            Parcel_Click(sender, e);
        }
        else if (sc.ShortcutMatches("transfer_table", e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
            Transfer_Click(sender, e);
        }
        else if (sc.ShortcutMatches("extra_options", e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
            Extra_Click(sender, e);
        }
        else if (sc.ShortcutMatches("change_table", e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
            ChangeTable_Click(sender, e);
        }
        else if (sc.ShortcutMatches("merge_table", e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
            MergeTable_Click(sender, e);
        }
        else if (sc.ShortcutMatches("split_table", e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
            SplitTable_Click(sender, e);
        }
        else if (sc.ShortcutMatches("toggle_billing_mode", e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
            vm.SetBillModeCommand.Execute(vm.BillMode == "Table" ? "Quick" : "Table");
        }

        // Keyboard control for selected cart item row (+, -, Delete)
        if (CartListBox.SelectedItem is CartLine selectedLine && Keyboard.FocusedElement is not TextBox)
        {
            if (e.Key == Key.Add || e.Key == Key.OemPlus)
            {
                e.Handled = true;
                selectedLine.Qty++;
            }
            else if (e.Key == Key.Subtract || e.Key == Key.OemMinus)
            {
                e.Handled = true;
                if (selectedLine.Qty > 1)
                {
                    selectedLine.Qty--;
                }
            }
            else if (e.Key == Key.Delete)
            {
                e.Handled = true;
                vm.Cart.Remove(selectedLine);
            }
        }

        // Activate cart using arrow keys if focus is not in search/textbox
        if (Keyboard.FocusedElement is not TextBox)
        {
            if (e.Key == Key.Down)
            {
                if (CartListBox.Items.Count > 0)
                {
                    e.Handled = true;
                    NavigateCartSelection(1);
                }
            }
            else if (e.Key == Key.Up)
            {
                if (CartListBox.Items.Count > 0)
                {
                    e.Handled = true;
                    NavigateCartSelection(-1);
                }
            }
        }
    }

    private void TablesListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right)
        {
            e.Handled = true;
        }
    }

    private static T? FindChild<T>(DependencyObject parent, string childName) where T : DependencyObject
    {
        if (parent == null) return null;
        int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childrenCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild && (string.IsNullOrEmpty(childName) || (child as FrameworkElement)?.Name == childName))
            {
                return typedChild;
            }
            var result = FindChild<T>(child, childName);
            if (result != null) return result;
        }
        return null;
    }

    private void ListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(listBox);
            if (scrollViewer != null)
            {
                if (e.Delta < 0)
                    scrollViewer.LineRight();
                else
                    scrollViewer.LineLeft();
                e.Handled = true;
            }
        }
    }

    private void BtnScrollAreasLeft_Click(object sender, RoutedEventArgs e)
    {
        var scrollViewer = FindVisualChild<ScrollViewer>(AreasListBox);
        scrollViewer?.LineLeft();
    }

    private void BtnScrollAreasRight_Click(object sender, RoutedEventArgs e)
    {
        var scrollViewer = FindVisualChild<ScrollViewer>(AreasListBox);
        scrollViewer?.LineRight();
    }

    private void BtnScrollCategoriesLeft_Click(object sender, RoutedEventArgs e)
    {
        var scrollViewer = FindVisualChild<ScrollViewer>(CategoriesListBox);
        scrollViewer?.LineLeft();
    }

    private void BtnScrollCategoriesRight_Click(object sender, RoutedEventArgs e)
    {
        var scrollViewer = FindVisualChild<ScrollViewer>(CategoriesListBox);
        scrollViewer?.LineRight();
    }

    private static T? FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            var child = VisualTreeHelper.GetChild(obj, i);
            if (child is T t)
                return t;
            
            var childOfChild = FindVisualChild<T>(child);
            if (childOfChild != null)
                return childOfChild;
        }
        return null;
    }

    /// <summary>The digit a key stands for, or null. Modifiers excluded so Shift+1 ("!")
    /// isn't mistaken for a quantity.</summary>
    private static char? DigitFromKey(Key key)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return null;
        if (key is >= Key.D0 and <= Key.D9) return (char)('0' + (key - Key.D0));
        if (key is >= Key.NumPad0 and <= Key.NumPad9) return (char)('0' + (key - Key.NumPad0));
        return null;
    }

    /// <summary>
    /// Keyboard control for the highlighted cart row. Only runs while the row itself holds
    /// focus — once the caret is in a qty/price box those keys belong to the editor.
    /// </summary>
    private void CartListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        // Up/Down walk the cart from anywhere inside it, the qty/price editors included —
        // focus lands in the qty box right after an item is added, and a TextBox swallows
        // the arrows in its own class handler before a KeyDown handler on the box sees them,
        // so this has to happen here on the tunnelling pass.
        if (e.Key is Key.Down or Key.Up)
        {
            if (CartListBox.Items.Count > 0)
            {
                e.Handled = true;
                NavigateCartSelection(e.Key == Key.Down ? 1 : -1);
            }
            return;
        }

        if (Keyboard.FocusedElement is TextBox) return;

        if (CartListBox.SelectedItem is not CartLine line) return;

        // Typing a number on a selected row edits its quantity straight away: the digit
        // replaces the old value and the caret moves into the box so further digits append.
        if (DigitFromKey(e.Key) is { } digit)
        {
            e.Handled = true;
            line.QtyText = digit.ToString();
            _qtyTypedEntry = true;
            FocusCartQty(line);
            return;
        }

        switch (e.Key)
        {
            case Key.Add or Key.OemPlus:
                e.Handled = true;
                line.Qty++;
                break;

            case Key.Subtract or Key.OemMinus:
                e.Handled = true;
                if (line.Qty > 1) line.Qty--;
                break;

            case Key.Delete:
                e.Handled = true;
                vm.Cart.Remove(line);
                break;

            case Key.Enter:
                // Enter edits the quantity of the highlighted row, Left its price — both
                // land with the whole value selected so typing replaces it.
                e.Handled = true;
                FocusCartQty(line);
                break;

            case Key.Left:
                e.Handled = true;
                FocusCartPrice(line);
                break;

            case Key.Escape:
                e.Handled = true;
                CartListBox.SelectedItem = null;
                SearchInput.Focus();
                break;
        }
    }

    private void NavigateCartSelection(int direction)
    {
        if (CartListBox.Items.Count == 0) return;

        var current = CartListBox.SelectedItem;

        // Focus can be sitting in a row's qty/price editor with nothing selected yet — that
        // is exactly the state right after an item is added. Walk from that row instead of
        // restarting at the top of the cart.
        if (current == null
            && FindAncestor<ListBoxItem>(Keyboard.FocusedElement as DependencyObject) is { } focusedRow
            && ItemsControl.ItemsControlFromItemContainer(focusedRow) == CartListBox)
        {
            current = focusedRow.DataContext;
        }

        var currentIndex = current != null ? CartListBox.Items.IndexOf(current) : -1;

        int targetIndex;
        if (currentIndex == -1)
        {
            targetIndex = direction > 0 ? 0 : CartListBox.Items.Count - 1;
        }
        else
        {
            targetIndex = Math.Clamp(currentIndex + direction, 0, CartListBox.Items.Count - 1);
        }

        var targetItem = CartListBox.Items[targetIndex];
        if (targetItem != null)
        {
            CartListBox.SelectedItem = targetItem;
            CartListBox.ScrollIntoView(targetItem);
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
            {
                var container = FindVisualChildWithItem(CartListBox, targetItem);
                container?.Focus();
            }));
        }
    }

    private static ListBoxItem? FindVisualChildWithItem(DependencyObject obj, object item)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            var child = VisualTreeHelper.GetChild(obj, i);
            if (child is ListBoxItem lbi && lbi.DataContext == item)
                return lbi;
            
            var childOfChild = FindVisualChildWithItem(child, item);
            if (childOfChild != null)
                return childOfChild;
        }
        return null;
    }
}