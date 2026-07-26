using System.Windows;
using System.Windows.Media;

namespace Pos.App.Views;

public partial class ThemeMessageBox : Window
{
    private ThemeMessageBox(string message, string title, string type)
    {
        InitializeComponent();
        TxtMessage.Text = message;
        TxtTitle.Text = title.ToUpperInvariant();

        // Setup Icon/Theme based on Type
        switch (type.ToLowerInvariant())
        {
            case "success":
                TxtIcon.Text = "✓";
                TxtIcon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF10B981"));
                break;
            case "warning":
                TxtIcon.Text = "⚠";
                TxtIcon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF97316"));
                break;
            case "error":
                TxtIcon.Text = "✕";
                TxtIcon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFEF4444"));
                break;
            default: // info
                TxtIcon.Text = "ℹ";
                TxtIcon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF38BDF8"));
                break;
        }

        Loaded += (s, e) =>
        {
            // Focus the OK button by default
            Focus();
        };
    }

    public static bool? Show(Window owner, string message, string title = "Notification", string type = "info")
    {
        var box = new ThemeMessageBox(message, title, type) { Owner = owner };
        return box.ShowDialog();
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
