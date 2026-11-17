using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OrthoStreamXP.Gui;

/// <summary>
/// Shows, while a row of a list is dragged, the order it would make: the rows between its place and where it would land
/// slide over to open a gap there, and a faint copy of it sits in the gap. Only render transforms move, so the layout (and
/// where the pointer is measured from) stays as it was until the drop.
/// </summary>
internal sealed class ReorderPreview
{
    private const double PlaceholderOpacity = 0.25;
    private static readonly Duration Slide = new(TimeSpan.FromMilliseconds(160));
    private static readonly CubicEase Ease = CreateEase();

    private readonly FrameworkElement[] _rows;
    private readonly int _from;
    private int? _insertAt;

    /// <param name="list">A list whose rows are all realised (not virtualised).</param>
    /// <param name="from">The dragged row.</param>
    public ReorderPreview(ItemsControl list, int from)
    {
        _rows = [.. Enumerable.Range(0, list.Items.Count).Select(i => list.ItemContainerGenerator.ContainerFromIndex(i)).OfType<FrameworkElement>()];
        _from = from;
        foreach (FrameworkElement row in _rows)
            if (row.RenderTransform is not TranslateTransform) row.RenderTransform = new TranslateTransform();
        if (from >= 0 && from < _rows.Length) _rows[from].Opacity = PlaceholderOpacity;
    }

    /// <summary>
    /// The row the dragged one would go before (the row count: last), by which half of a row the pointer is over, measured
    /// on the rows' places before any slid aside, so the gap opening does not move the target under the pointer.
    /// </summary>
    public int InsertIndex(DragEventArgs e)
    {
        if (_rows.Length == 0 || VisualTreeHelper.GetParent(_rows[0]) is not UIElement panel) return 0;
        double y = e.GetPosition(panel).Y;
        for (int i = 0; i < _rows.Length; i++)
            if (y < VisualTreeHelper.GetOffset(_rows[i]).Y + _rows[i].ActualHeight / 2) return i;
        return _rows.Length;
    }

    /// <summary>Opens the gap before the row at <paramref name="insertAt"/>; null closes it (the pointer left the list).</summary>
    public void Show(int? insertAt)
    {
        if (insertAt == _insertAt || _from < 0 || _from >= _rows.Length) return;
        _insertAt = insertAt;
        int to = insertAt is not { } at ? _from : at > _from ? at - 1 : at;
        double placeholder = 0;
        for (int i = 0; i < _rows.Length; i++)
        {
            if (i == _from) continue;
            double y = to > _from && i > _from && i <= to ? -_rows[_from].ActualHeight
                     : to < _from && i >= to && i < _from ? _rows[_from].ActualHeight
                     : 0;
            placeholder -= Math.Sign(y) * _rows[i].ActualHeight;
            Animate(_rows[i], y);
        }
        Animate(_rows[_from], placeholder);
    }

    /// <summary>Puts every row back: sliding (the drag was cancelled), or at once (the list is about to take the new order).</summary>
    public void Reset(bool animate)
    {
        foreach (FrameworkElement row in _rows)
        {
            row.Opacity = 1;
            if (animate)
            {
                Animate(row, 0);
            }
            else
            {
                var transform = (TranslateTransform)row.RenderTransform;
                transform.BeginAnimation(TranslateTransform.YProperty, null);
                transform.Y = 0;
            }
        }
    }

    private static void Animate(FrameworkElement row, double y) =>
        ((TranslateTransform)row.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, Slide) { EasingFunction = Ease });

    private static CubicEase CreateEase()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ease.Freeze();
        return ease;
    }
}
