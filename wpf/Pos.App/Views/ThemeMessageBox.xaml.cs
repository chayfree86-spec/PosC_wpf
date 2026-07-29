using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Pos.App.Views;

/// <summary>
/// The app's own alert box, used everywhere instead of <see cref="MessageBox"/>.
///
/// The native dialog paints itself in the Windows light theme with a system font, which on a
/// dark till screen reads as a different program interrupting this one. This is the same card,
/// palette and font as the rest of the app.
/// </summary>
public partial class ThemeMessageBox : Window
{
    /// <summary>Types that ask a question rather than state something, and so need a way to
    /// say no.</summary>
    private static readonly HashSet<string> ConfirmTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "yesno", "confirm", "danger",
    };

    private ThemeMessageBox(string message, string title, string type)
    {
        InitializeComponent();
        TxtMessage.Text = message;
        TxtTitle.Text = title.ToUpperInvariant();

        var kind = (type ?? "info").ToLowerInvariant();

        switch (kind)
        {
            case "success":
                SetIcon("✓", "#FF10B981");
                break;
            case "warning":
                SetIcon("⚠", "#FFF97316");
                break;
            case "error":
                SetIcon("✕", "#FFEF4444");
                break;
            case "yesno":
            case "confirm":
                SetIcon("?", "#FF38BDF8");
                break;
            case "danger":
                SetIcon("⚠", "#FFEF4444");
                break;
            default: // info
                SetIcon("ℹ", "#FF38BDF8");
                break;
        }

        // A question with only an OK button isn't a question. "yesno" was already being passed
        // in from the delete-bill and delete-note confirms and silently rendering as a notice,
        // so Yes was the only button on screen for an action that can't be undone.
        if (ConfirmTypes.Contains(kind))
        {
            BtnCancel.Visibility = Visibility.Visible;
            BtnConfirm.Content = "YES";

            // Something being destroyed shouldn't wear the same green as Save.
            if (kind == "danger")
            {
                BtnConfirm.Background = new SolidColorBrush(Hex("#FFEF4444"));
            }
        }

        Loaded += (_, _) => BtnConfirm.Focus();
    }

    /// <summary>
    /// Shows the box and blocks: true when confirmed, false when dismissed.
    /// </summary>
    /// <param name="type">
    /// <c>info</c> / <c>success</c> / <c>warning</c> / <c>error</c> for a notice with one button,
    /// <c>yesno</c> (or <c>confirm</c>) for a question, <c>danger</c> for a question whose yes
    /// destroys something.
    /// </param>
    public static bool? Show(Window? owner, string message, string title = "Notification", string type = "info")
    {
        // Print failures and the crash handlers can reach here off the UI thread, where the
        // native MessageBox this replaced was happy to run and a WPF window is not.
        var app = Application.Current;
        if (app is not null && !app.Dispatcher.CheckAccess())
        {
            return app.Dispatcher.Invoke(() => Show(owner, message, title, type));
        }

        var box = new ThemeMessageBox(message, title, type);

        // WPF refuses an owner that has never been shown, and centring on a hidden one puts the
        // box off in a corner — the startup error and the logout screen both hit that.
        if (owner is not null && owner.IsVisible)
        {
            box.Owner = owner;
        }
        else
        {
            box.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return box.ShowDialog();
    }

    /// <summary>For callers with no window of their own — view models and the crash handlers.
    /// Falls back to whichever window is in front.</summary>
    public static bool? Show(string message, string title = "Notification", string type = "info") =>
        Show(ActiveWindow(), message, title, type);

    /// <summary>True when the operator confirmed. Reads better than <c>== true</c> at the call
    /// site, and treats a dismissed box as "no" — which is what a confirm should do.</summary>
    public static bool Confirm(Window? owner, string message, string title, string type = "yesno") =>
        Show(owner, message, title, type) == true;

    private static Window? ActiveWindow()
    {
        var app = Application.Current;
        if (app is null)
        {
            return null;
        }

        return app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible)
            ?? (app.MainWindow is { IsVisible: true } main ? main : null);
    }

    private void SetIcon(string glyph, string colour)
    {
        TxtIcon.Text = glyph;
        TxtIcon.Foreground = new SolidColorBrush(Hex(colour));
    }

    private static Color Hex(string value) => (Color) ColorConverter.ConvertFromString(value)!;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Confirm_Click(sender, e);
                e.Handled = true;
                break;
            case Key.Escape:
                Close_Click(sender, e);
                e.Handled = true;
                break;
        }
    }

    /// <summary>There is no title bar to drag, so the whole card moves the window.</summary>
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
