using System.Windows;
using System.Windows.Controls;

namespace Pos.App.Helpers;

/// <summary>
/// Builds a full-page view the first time its screen is actually opened, instead of with the
/// main window.
///
/// All five secondary screens (ledger, settings, reports, QR, notes) used to be instantiated
/// at launch even though only the Orders screen is ever visible at that point — about 1.3s of
/// every start. The view is kept once created, so returning to a screen is instant and it
/// holds on to whatever the user had on it.
///
/// Usage: <c>&lt;ContentControl helpers:LazyContent.CreateWith="{x:Type views:SettingsView}"/&gt;</c>
/// </summary>
public static class LazyContent
{
    public static readonly DependencyProperty CreateWithProperty =
        DependencyProperty.RegisterAttached(
            "CreateWith", typeof(Type), typeof(LazyContent), new PropertyMetadata(null, OnCreateWithChanged));

    public static void SetCreateWith(DependencyObject o, Type? value) => o.SetValue(CreateWithProperty, value);
    public static Type? GetCreateWith(DependencyObject o) => (Type?)o.GetValue(CreateWithProperty);

    /// <summary>Screens that haven't been built yet, so idle time can get them ready.</summary>
    private static readonly List<ContentControl> Pending = new();

    /// <summary>
    /// Builds whatever the user hasn't opened yet, once the app has nothing else to do.
    /// Startup stays fast (this runs after the window is up) and by the time anyone reaches
    /// for a screen it is usually already built, so the switch is instant.
    /// </summary>
    public static void WarmUp(System.Windows.Threading.Dispatcher dispatcher)
    {
        foreach (var host in Pending.ToArray())
        {
            // One per callback, at idle priority: input always gets served first.
            dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(() => Create(host)));
        }
    }

    private static void OnCreateWithChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ContentControl host || e.NewValue is not Type)
        {
            return;
        }

        Pending.Add(host);

        // Not before the first layout pass: until the owning screen's Visibility binding has
        // resolved, every one of these still reports itself visible, and acting on that would
        // build all five screens at startup — exactly what this exists to avoid.
        host.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            if (host.IsVisible)
            {
                CreateSoon(host);
                return;
            }

            host.IsVisibleChanged += OnVisibleChanged;
        }));
    }

    private static void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var host = (ContentControl)sender;
        if (!host.IsVisible)
        {
            return;
        }

        host.IsVisibleChanged -= OnVisibleChanged;   // one-shot: the view is kept from here on
        CreateSoon(host);
    }

    /// <summary>
    /// Builds on the next dispatcher beat rather than inline, so the screen switch — and with
    /// it the sidebar item turning green — paints immediately. Building inline held the UI
    /// thread for about 100ms on a screen's first open, which read as the click not landing.
    /// </summary>
    private static void CreateSoon(ContentControl host) =>
        host.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => Create(host)));

    private static void Create(ContentControl host)
    {
        Pending.Remove(host);
        if (host.Content == null && GetCreateWith(host) is { } type)
        {
            host.Content = Activator.CreateInstance(type);
        }
    }
}
