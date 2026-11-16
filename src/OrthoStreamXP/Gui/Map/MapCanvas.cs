using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OrthoStreamXP.Gui;

/// <summary>
/// A layer of a <see cref="ZoomPanel"/>: places each child at its <see cref="XProperty"/>/<see cref="YProperty"/> in map
/// units, as the panel shows them. A child with a <see cref="SpanWidthProperty"/> and <see cref="SpanHeightProperty"/>
/// covers that much of the map (a picture of an area); one without keeps its own size whatever the zoom (a label).
/// </summary>
internal sealed class MapCanvas : Panel
{
    public static readonly DependencyProperty XProperty = DependencyProperty.RegisterAttached("X", typeof(double), typeof(MapCanvas),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty YProperty = DependencyProperty.RegisterAttached("Y", typeof(double), typeof(MapCanvas),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty SpanWidthProperty = DependencyProperty.RegisterAttached("SpanWidth", typeof(double), typeof(MapCanvas),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static readonly DependencyProperty SpanHeightProperty = DependencyProperty.RegisterAttached("SpanHeight", typeof(double), typeof(MapCanvas),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    private ZoomPanel? _host;

    public MapCanvas()
    {
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    public static double GetX(UIElement element) => (double)element.GetValue(XProperty);
    public static void SetX(UIElement element, double value) => element.SetValue(XProperty, value);
    public static double GetY(UIElement element) => (double)element.GetValue(YProperty);
    public static void SetY(UIElement element, double value) => element.SetValue(YProperty, value);
    public static double GetSpanWidth(UIElement element) => (double)element.GetValue(SpanWidthProperty);
    public static void SetSpanWidth(UIElement element, double value) => element.SetValue(SpanWidthProperty, value);
    public static double GetSpanHeight(UIElement element) => (double)element.GetValue(SpanHeightProperty);
    public static void SetSpanHeight(UIElement element, double value) => element.SetValue(SpanHeightProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        double scale = _host?.Viewport.Scale ?? 1;
        foreach (UIElement child in InternalChildren)
            child.Measure(Span(child, scale) ?? new Size(double.PositiveInfinity, double.PositiveInfinity));
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        MapViewport? viewport = _host?.Viewport;
        double scale = viewport?.Scale ?? 1;
        foreach (UIElement child in InternalChildren)
        {
            var at = new Point(GetX(child), GetY(child));
            Point screen = viewport?.ToScreen(at) ?? at;
            child.Arrange(new Rect(screen, Span(child, scale) ?? child.DesiredSize));
        }
        return finalSize;
    }

    private static Size? Span(UIElement child, double scale)
    {
        double width = GetSpanWidth(child), height = GetSpanHeight(child);
        return double.IsNaN(width) || double.IsNaN(height) ? null : new Size(width * scale, height * scale);
    }

    private void Attach()
    {
        Detach();
        _host = Ancestor<ZoomPanel>(this);
        if (_host is null) return;
        _host.ViewChanged += OnViewChanged;
        InvalidateMeasure();
    }

    private void Detach()
    {
        if (_host is not null) _host.ViewChanged -= OnViewChanged;
        _host = null;
    }

    private void OnViewChanged(object? sender, EventArgs e) => InvalidateMeasure();

    internal static T? Ancestor<T>(DependencyObject element) where T : DependencyObject
    {
        for (DependencyObject? parent = VisualTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is T found) return found;
        return null;
    }
}
