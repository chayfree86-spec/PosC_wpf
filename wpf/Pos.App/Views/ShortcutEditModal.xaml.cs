using System.Windows;
using System.Windows.Input;

namespace Pos.App.Views;

/// <summary>Hotkey-capture dialog matching the Electron "Customize Key" modal.</summary>
public partial class ShortcutEditModal : Window
{
    private static readonly HashSet<Key> ValidLoneKeys = new()
    {
        Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6, Key.F7, Key.F8, Key.F9, Key.F10, Key.F11, Key.F12,
        Key.Escape, Key.Tab, Key.Insert, Key.Space, Key.Back, Key.Delete, Key.Enter
    };

    public string NewKey { get; private set; } = "";

    public ShortcutEditModal(string action, string currentKey)
    {
        InitializeComponent();
        TitleText.Text = $"Customize Key: {action}";
        CurrentKeyText.Text = string.IsNullOrWhiteSpace(currentKey) ? "None" : currentKey;
        Loaded += (_, _) => CaptureBox.Focus();
    }

    private void CaptureBox_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => CaptureBox.Focus();

    private void CaptureBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift)
            return;

        var mods = Keyboard.Modifiers;
        string captured;

        if (mods == ModifierKeys.None)
        {
            if (!ValidLoneKeys.Contains(key))
            {
                ShowError("Sirf F1-F12, Escape, Tab, Insert, Space, Backspace, Delete, ya Enter allowed hai (bina modifier ke).");
                return;
            }
            captured = KeyName(key);
        }
        else
        {
            var parts = new List<string>();
            if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");

            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift)
            {
                ShowError("Kripya ek letter ya key modifier ke saath dabayein (jaise Ctrl+A).");
                return;
            }
            parts.Add(KeyName(key));
            captured = string.Join("+", parts);
        }

        NewKey = captured;
        PendingKeyText.Text = captured;
        PendingKeyBadge.Visibility = Visibility.Visible;
        NonePendingText.Visibility = Visibility.Collapsed;
        CaptureHintText.Text = "Press another key to change...";
        SaveKeyBtn.IsEnabled = true;
        ErrorBox.Visibility = Visibility.Collapsed;
    }

    private static string KeyName(Key key) => key switch
    {
        Key.Escape => "Escape",
        Key.Back => "Backspace",
        Key.Enter => "Enter",
        Key.Space => "Space",
        Key.Insert => "Insert",
        Key.Delete => "Delete",
        Key.Tab => "Tab",
        _ => key.ToString()
    };

    private void ShowError(string message)
    {
        ErrorText.Text = "⚠️  " + message;
        ErrorBox.Visibility = Visibility.Visible;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(NewKey)) return;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
