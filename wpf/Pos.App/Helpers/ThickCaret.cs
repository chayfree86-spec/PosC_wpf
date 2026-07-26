using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Pos.App.Helpers;

/// <summary>
/// Gives a TextBox a caret of a chosen width.
///
/// WPF has no caret-width property — it draws the caret at the machine-wide Windows
/// thickness (SystemParameters.CaretWidth, the "blinking cursor thickness" accessibility
/// setting). Getting a per-control width therefore means hiding the built-in caret and
/// painting our own in the adorner layer.
///
/// Usage: <c>helpers:ThickCaret.Width="4"</c> on a TextBox or in its Style.
/// </summary>
public static class ThickCaret
{
    public static readonly DependencyProperty WidthProperty =
        DependencyProperty.RegisterAttached(
            "Width", typeof(double), typeof(ThickCaret), new PropertyMetadata(0d, OnWidthChanged));

    public static void SetWidth(DependencyObject o, double value) => o.SetValue(WidthProperty, value);
    public static double GetWidth(DependencyObject o) => (double)o.GetValue(WidthProperty);

    private static void OnWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox tb || (double)e.NewValue <= 0)
        {
            return;
        }

        // Loaded/Unloaded fire again every time a box is re-used (cart rows come and go), so
        // the hook stays subscribed for the life of the box and attaches on each load.
        tb.Loaded += OnLoaded;
        tb.Unloaded += OnUnloaded;
        if (tb.IsLoaded)
        {
            Attach(tb);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => Attach((TextBox)sender);

    private static void OnUnloaded(object sender, RoutedEventArgs e) => Detach((TextBox)sender);

    private static void Attach(TextBox tb)
    {
        // No adorner layer means no window chrome yet (or the box lives somewhere that
        // can't be adorned); leave the system caret alone rather than hiding it forever.
        var layer = AdornerLayer.GetAdornerLayer(tb);
        if (layer == null || Find(layer, tb) != null)
        {
            return;
        }

        layer.Add(new CaretAdorner(tb, GetWidth(tb)));
    }

    /// <summary>
    /// Tears the adorner down with its box. The adorner lives in the window's adorner layer
    /// and listens to the TextBox, so leaving it behind would both keep every cart row's
    /// qty/price box alive and pile up dead adorners in the layer.
    /// </summary>
    private static void Detach(TextBox tb)
    {
        var layer = AdornerLayer.GetAdornerLayer(tb);
        if (layer == null)
        {
            return;
        }

        if (Find(layer, tb) is { } adorner)
        {
            adorner.Release();
            layer.Remove(adorner);
        }
    }

    private static CaretAdorner? Find(AdornerLayer layer, TextBox tb) =>
        layer.GetAdorners(tb)?.OfType<CaretAdorner>().FirstOrDefault();

    private sealed class CaretAdorner : Adorner
    {
        /// <summary>Windows' default caret blink interval.</summary>
        private const double BlinkMs = 530;

        private readonly TextBox _tb;
        private readonly double _width;
        private readonly Brush _brush;
        private readonly DoubleAnimationUsingKeyFrames _blink;

        public CaretAdorner(TextBox tb, double width) : base(tb)
        {
            _tb = tb;
            _width = width;
            IsHitTestVisible = false;

            // Keep whatever colour the box was configured with — only the width changes.
            var brush = tb.CaretBrush ?? tb.Foreground ?? Brushes.White;
            _brush = brush.CloneCurrentValue();
            if (_brush.CanFreeze)
            {
                _brush.Freeze();
            }
            tb.CaretBrush = Brushes.Transparent;

            _blink = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
            _blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            _blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(BlinkMs))));
            _blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(BlinkMs * 2))));
            // The caret only flips twice a second; without this the animation clock would
            // wake the render thread at the full frame rate for the life of the app.
            Timeline.SetDesiredFrameRate(_blink, 10);
            _blink.Freeze();

            tb.SelectionChanged += OnCaretMoved;
            tb.TextChanged += OnCaretMoved;
            tb.GotKeyboardFocus += OnFocusChanged;
            tb.LostKeyboardFocus += OnFocusChanged;
            tb.SizeChanged += OnLayoutChanged;
            // The text scrolls sideways once it outgrows the box; the caret moves with it.
            tb.AddHandler(ScrollViewer.ScrollChangedEvent, (ScrollChangedEventHandler)OnScrolled);

            Restart();
        }

        /// <summary>Drops every hook into the TextBox and stops the blink clock.</summary>
        public void Release()
        {
            _tb.SelectionChanged -= OnCaretMoved;
            _tb.TextChanged -= OnCaretMoved;
            _tb.GotKeyboardFocus -= OnFocusChanged;
            _tb.LostKeyboardFocus -= OnFocusChanged;
            _tb.SizeChanged -= OnLayoutChanged;
            _tb.RemoveHandler(ScrollViewer.ScrollChangedEvent, (ScrollChangedEventHandler)OnScrolled);
            BeginAnimation(OpacityProperty, null);
        }

        private void OnCaretMoved(object sender, RoutedEventArgs e) => Restart();
        private void OnFocusChanged(object sender, KeyboardFocusChangedEventArgs e) => Restart();
        private void OnLayoutChanged(object sender, SizeChangedEventArgs e) => InvalidateVisual();
        private void OnScrolled(object sender, ScrollChangedEventArgs e) => InvalidateVisual();

        /// <summary>Redraws and restarts the blink, so the caret is solid right after it moves —
        /// and stops the timeline entirely while the box isn't focused.</summary>
        private void Restart()
        {
            BeginAnimation(OpacityProperty, null);
            if (_tb.IsKeyboardFocused)
            {
                BeginAnimation(OpacityProperty, _blink);
            }
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (!_tb.IsKeyboardFocused || _tb.SelectionLength > 0)
            {
                return;
            }

            var r = _tb.GetRectFromCharacterIndex(_tb.CaretIndex);
            if (double.IsNaN(r.X) || double.IsInfinity(r.X) || double.IsInfinity(r.Y))
            {
                return;
            }

            var height = r.Height > 0 ? r.Height : _tb.FontSize * 1.3;
            dc.DrawRectangle(_brush, null, new Rect(r.X, r.Y, _width, height));
        }
    }
}
