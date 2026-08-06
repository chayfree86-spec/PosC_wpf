using System.Windows;
using System.Windows.Controls.Primitives;

namespace Pos.App.Controls;

/// <summary>
/// A <see cref="UniformGrid"/> that picks its own column count so ALL the cards fit the space
/// available — no scrolling — while staying as readable as the size allows.
///
/// It balances two limits: a card should be at least <see cref="MinColumnWidth"/> wide, and the
/// rows should fit the height without each card dropping below <see cref="MinRowHeight"/>. It uses
/// as few columns as it can (wider cards) but adds columns when that's what it takes to keep every
/// table on screen at once. Because a UniformGrid stretches its cells, the chosen grid then fills
/// the whole area top-to-bottom and left-to-right.
/// </summary>
public sealed class AdaptiveUniformGrid : UniformGrid
{
    public static readonly DependencyProperty MinColumnWidthProperty =
        DependencyProperty.Register(
            nameof(MinColumnWidth), typeof(double), typeof(AdaptiveUniformGrid),
            new FrameworkPropertyMetadata(150.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MinRowHeightProperty =
        DependencyProperty.Register(
            nameof(MinRowHeight), typeof(double), typeof(AdaptiveUniformGrid),
            new FrameworkPropertyMetadata(66.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Narrowest a column may get before the grid stops adding columns for width.</summary>
    public double MinColumnWidth
    {
        get => (double)GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    /// <summary>The row height the grid tries to keep, adding columns to reduce the row count so
    /// every card stays this tall when the space allows.</summary>
    public double MinRowHeight
    {
        get => (double)GetValue(MinRowHeightProperty);
        set => SetValue(MinRowHeightProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var count = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility != Visibility.Collapsed)
            {
                count++;
            }
        }

        if (count > 0 && MinColumnWidth > 0 && !double.IsInfinity(constraint.Width) && constraint.Width > 0)
        {
            // Start from the most columns the width allows without cards getting too narrow.
            var cols = Math.Max(1, (int)(constraint.Width / MinColumnWidth));

            // If the height is known, make sure the resulting rows fit: add columns until the row
            // count is low enough that each row can be at least MinRowHeight tall.
            if (!double.IsInfinity(constraint.Height) && constraint.Height > 0 && MinRowHeight > 0)
            {
                var maxRows = Math.Max(1, (int)(constraint.Height / MinRowHeight));
                var colsToFitHeight = (int)Math.Ceiling((double)count / maxRows);
                cols = Math.Max(cols, colsToFitHeight);
            }

            Columns = Math.Min(cols, count);
            // Rows = 0 lets the grid derive the row count and split the height across them.
            Rows = 0;
        }

        var result = base.MeasureOverride(constraint);
        if (count > 0 && Columns > 0 && !double.IsInfinity(constraint.Height) && constraint.Height > 0)
        {
            var calculatedRows = (int)Math.Ceiling((double)count / Columns);
            var cellHeight = constraint.Height / calculatedRows;
            
            // Limit each card's height to a comfortable range (between MinRowHeight and 125px)
            cellHeight = Math.Min(cellHeight, 125.0);
            cellHeight = Math.Max(cellHeight, MinRowHeight);
            
            return new Size(result.Width, calculatedRows * cellHeight);
        }
        return result;
    }
}
