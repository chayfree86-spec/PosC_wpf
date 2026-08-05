using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Pos.App.Services;
using Pos.App.ViewModels;
using Pos.Core.Repositories;

namespace Pos.App.Views;

/// <summary>
/// Mobile number plus PIN, checked against the till's own copy of the staff list.
///
/// Everything here is local — see <see cref="AuthRepository"/> for why. The counter has to
/// open when the line is down, so nothing on this screen waits on the network.
/// </summary>
public partial class LoginWindow : Window
{
    /// <summary>Last number that signed in on THIS machine, so the usual operator only types
    /// a PIN. Local-only (<c>Set</c>, not <c>SetSynced</c>): it describes the till, not the
    /// business, and has no reason to travel to another counter.</summary>
    private const string LastMobileKey = "pos_wpf_last_login_mobile";

    private readonly AuthRepository _auth;
    private readonly AppSettingsRepository _settings;

    /// <summary>The four PIN boxes, left to right, and the digit each currently holds
    /// ('\0' = empty). Kept separately from the boxes' text so the boxes can show dots while the
    /// real PIN is read from here.</summary>
    private TextBox[] _pinBoxes = System.Array.Empty<TextBox>();
    private readonly char[] _pinDigits = new char[4];
    private bool _pinReveal;

    public LoginWindow()
    {
        InitializeComponent();

        _auth = App.Services.GetRequiredService<AuthRepository>();
        _settings = App.Services.GetRequiredService<AppSettingsRepository>();
        _pinBoxes = new[] { Pin1, Pin2, Pin3, Pin4 };

        RestoreLastMobile();
    }

    /// <summary>
    /// Puts the sign-in screen up and blocks until it is answered: true when an operator
    /// signed in, false when the window was dismissed.
    /// </summary>
    public static bool Authenticate()
    {
        var previous = Application.Current.ShutdownMode;

        // Without this the app exits the moment this window closes — at startup it is the only
        // window open, and at logout the main window is hidden rather than shown.
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            return new LoginWindow().ShowDialog() == true;
        }
        finally
        {
            Application.Current.ShutdownMode = previous;
        }
    }

    private void RestoreLastMobile()
    {
        var last = "";
        try
        {
            last = _settings.GetJson<string>(LastMobileKey) ?? "";
        }
        catch { }

        TxtMobile.Text = last;

        // Focus the field that still needs filling, so the usual operator starts on the PIN.
        Loaded += (_, _) =>
        {
            if (last.Length > 0)
            {
                Pin1.Focus();
            }
            else
            {
                TxtMobile.Focus();
            }
        };
    }

    private void Login_Click(object sender, RoutedEventArgs e) => TrySignIn();

    private void TrySignIn()
    {
        var mobile = TxtMobile.Text.Trim();
        var pin = CurrentPin();

        var result = _auth.Login(mobile, pin);
        if (!result.Ok || result.User is null)
        {
            // Wipe the PIN before saying anything: clearing a field raises the change event
            // that drops stale errors, and doing it the other way round swallows the message
            // the operator is meant to read.
            ClearPin();
            ShowError(result.Error ?? "Sign in nahi ho paya.");
            Pin1.Focus();
            return;
        }

        Session.SignIn(result.User);

        try
        {
            _settings.SetJson(LastMobileKey, mobile);
        }
        catch
        {
            // Remembering the number is a convenience; failing to save it must not undo a
            // sign-in that has already succeeded.
        }

        DialogResult = true;
        Close();
    }

    private string CurrentPin() =>
        new string(_pinDigits.Where(c => c != '\0').ToArray());

    private void ClearPin()
    {
        for (var i = 0; i < _pinDigits.Length; i++)
        {
            _pinDigits[i] = '\0';
            UpdatePinBox(i);
        }
    }

    private int PinIndex(object sender) =>
        sender is TextBox b && int.TryParse(b.Tag?.ToString(), out var i) ? i : -1;

    /// <summary>Digits only, one per box, advancing as it goes; the fourth signs in.</summary>
    private void Pin_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = true;   // the box's text is set from _pinDigits, not typed into directly
        var i = PinIndex(sender);
        if (i < 0 || e.Text.Length == 0 || !char.IsDigit(e.Text[^1]))
        {
            return;
        }

        _pinDigits[i] = e.Text[^1];
        UpdatePinBox(i);
        TxtError.Visibility = Visibility.Collapsed;

        if (i < _pinBoxes.Length - 1)
        {
            _pinBoxes[i + 1].Focus();
        }
        else
        {
            // Last digit entered — sign in.
            TrySignIn();
        }
    }

    private void Pin_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var i = PinIndex(sender);
        if (i < 0)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Back:
                e.Handled = true;
                if (_pinDigits[i] != '\0')
                {
                    _pinDigits[i] = '\0';
                    UpdatePinBox(i);
                }
                else if (i > 0)
                {
                    _pinDigits[i - 1] = '\0';
                    UpdatePinBox(i - 1);
                    _pinBoxes[i - 1].Focus();
                }
                break;

            case Key.Delete:
                e.Handled = true;
                _pinDigits[i] = '\0';
                UpdatePinBox(i);
                break;

            case Key.Left:
                if (i > 0) { _pinBoxes[i - 1].Focus(); e.Handled = true; }
                break;

            case Key.Right:
                if (i < _pinBoxes.Length - 1) { _pinBoxes[i + 1].Focus(); e.Handled = true; }
                break;
        }
    }

    /// <summary>Paints one box: the digit when revealed, a dot when masked, blank when empty.</summary>
    private void UpdatePinBox(int i)
    {
        var d = _pinDigits[i];
        _pinBoxes[i].Text = d == '\0' ? "" : (_pinReveal ? d.ToString() : "●");
        _pinBoxes[i].CaretIndex = _pinBoxes[i].Text.Length;
    }

    private void ShowError(string message)
    {
        TxtError.Text = message;
        TxtError.Visibility = Visibility.Visible;
    }

    /// <summary>A stale error under the fields the operator is retyping is just noise.</summary>
    private void Field_Changed(object sender, RoutedEventArgs e) =>
        TxtError.Visibility = Visibility.Collapsed;

    private void Mobile_Changed(object sender, RoutedEventArgs e)
    {
        Field_Changed(sender, e);
        ShowStaffName();
    }

    /// <summary>Names whoever the typed number belongs to, and says nothing at all when it
    /// belongs to no one — an empty line reads better than an accusation while someone is
    /// still halfway through typing.</summary>
    private void ShowStaffName()
    {
        StaffLookup? found = null;
        try
        {
            found = _auth.LookupName(TxtMobile.Text);
        }
        catch
        {
            // A lookup that fails is a nicety that didn't happen; sign-in still works.
        }

        if (found is null || string.IsNullOrWhiteSpace(found.Name))
        {
            StaffRow.Visibility = Visibility.Collapsed;
            return;
        }

        // The business is named alongside the operator because this is what picks it: one
        // counter serves Daal Roti and Chay Chaupal, and the number typed here decides which
        // of them the shift's bills belong to. Signing in as the wrong one is otherwise only
        // noticed when a bill prints the other brand's header.
        TxtStaffName.Text = string.IsNullOrWhiteSpace(found.ClientName)
            ? found.Name
            : $"{found.Name}  ·  {found.ClientName}";
        StaffRow.Visibility = Visibility.Visible;
    }

    private void TogglePin_Click(object sender, RoutedEventArgs e)
    {
        _pinReveal = !_pinReveal;
        for (var i = 0; i < _pinDigits.Length; i++)
        {
            UpdatePinBox(i);
        }

        IconPinEye.Kind = _pinReveal
            ? MaterialDesignThemes.Wpf.PackIconKind.EyeOffOutline
            : MaterialDesignThemes.Wpf.PackIconKind.EyeOutline;
    }

    /// <summary>The mobile field holds a number: refusing letters at the keystroke beats reporting
    /// them after the operator has typed the whole thing.</summary>
    private void Digits_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = e.Text.Length == 0 || !e.Text.All(char.IsDigit);

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                // Enter on the mobile field moves on rather than submitting a blank PIN.
                if (TxtMobile.IsKeyboardFocusWithin && CurrentPin().Length == 0)
                {
                    Pin1.Focus();
                }
                else
                {
                    TrySignIn();
                }
                e.Handled = true;
                break;

            case Key.Escape:
                Cancel();
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

    private void Close_Click(object sender, RoutedEventArgs e) => Cancel();

    private void Cancel()
    {
        DialogResult = false;
        Close();
    }
}
