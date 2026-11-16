using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OrthoStreamXP.Gui;

/// <summary>
/// Keeps an element of a scrolled page in sight: once it reaches the top of the viewport it stays there while
/// the rest of its panel scrolls under it, until the panel's end pushes it up. Only a render offset: the layout
/// is untouched, so nothing below it moves.
/// </summary>
internal sealed class StickyHeader
{
    private readonly ScrollViewer _scroller;
    private readonly FrameworkElement _header;
    private readonly Action<bool> _pinnedChanged;
    private readonly TranslateTransform _shift = new();
    private bool _pinned;

    /// <param name="scroller">The page's scroll viewer.</param>
    /// <param name="header">The element to keep at the top; its parent panel bounds how far it follows.</param>
    /// <param name="pinnedChanged">Told when the header starts or stops covering content (to give it a backdrop).</param>
    public StickyHeader(ScrollViewer scroller, FrameworkElement header, Action<bool> pinnedChanged)
    {
        _scroller = scroller;
        _header = header;
        _pinnedChanged = pinnedChanged;
        header.RenderTransform = _shift;
        Panel.SetZIndex(header, 1);
        scroller.ScrollChanged += (_, _) => Update();
        header.SizeChanged += (_, _) => Update();
        header.IsVisibleChanged += (_, _) => Update();
    }

    private void Update()
    {
        double shift = 0;
        if (_header.IsVisible && VisualTreeHelper.GetParent(_header) is FrameworkElement panel && panel.IsDescendantOf(_scroller))
        {
            double top = _header.TransformToAncestor(_scroller).Transform(default).Y - _shift.Y;
            double panelBottom = panel.TransformToAncestor(_scroller).Transform(default).Y + panel.ActualHeight;
            shift = Math.Clamp(-top, 0, Math.Max(0, panelBottom - (top + _header.ActualHeight)));
        }

        _shift.Y = shift;
        bool pinned = shift > 0;
        if (pinned == _pinned) return;
        _pinned = pinned;
        _pinnedChanged(pinned);
    }
}
