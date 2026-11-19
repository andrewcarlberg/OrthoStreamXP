using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace OrthoStreamXP.Gui;

/// <summary>
/// The latitude and longitude lines over a <see cref="ZoomPanel"/>'s map, across the whole view, every 1, 2, 5 or 10
/// degrees as the zoom allows, with their labels along the top and left edges.
/// </summary>
internal sealed class Graticule : FrameworkElement
{
    public static readonly DependencyProperty ProjectionProperty = DependencyProperty.Register(nameof(Projection), typeof(MapProjection), typeof(Graticule),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(Graticule),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(Graticule),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelBackgroundProperty = DependencyProperty.Register(nameof(LabelBackground), typeof(Brush), typeof(Graticule),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly int[] Steps = [1, 2, 5, 10, 15, 30];
    private const double MinSpacing = 44, FontSize = 11;
    private ZoomPanel? _host;

    public Graticule()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    public MapProjection? Projection
    {
        get => (MapProjection?)GetValue(ProjectionProperty);
        set => SetValue(ProjectionProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public Brush LabelBackground
    {
        get => (Brush)GetValue(LabelBackgroundProperty);
        set => SetValue(LabelBackgroundProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Projection is not { } map || _host is null || ActualWidth <= 0 || ActualHeight <= 0) return;
        MapViewport view = _host.Viewport;
        double width = ActualWidth, height = ActualHeight;
        Point topLeft = view.ToMap(new Point(0, 0)), bottomRight = view.ToMap(new Point(width, height));
        int lonStep = Step(map.DegreeWidth * view.Scale), latStep = Step(map.Unit * view.Scale);
        var pen = new Pen(Stroke, 1);
        pen.Freeze();
        var lines = new List<(Point, Point)>();
        var labels = new List<(string Text, double X, double Y, bool Vertical)>();
        for (int lon = FloorTo(map.Lon(topLeft.X), lonStep); lon <= map.Lon(bottomRight.X); lon += lonStep)
        {
            double x = Math.Round(view.ToScreen(new Point(map.X(lon), 0)).X) + 0.5;
            lines.Add((new Point(x, 0), new Point(x, height)));
            labels.Add((Invariant($"{Math.Abs(lon)}°{(lon >= 0 ? 'E' : 'W')}"), x, 0, false));
        }
        for (int lat = FloorTo(map.Lat(bottomRight.Y), latStep); lat <= map.Lat(topLeft.Y); lat += latStep)
        {
            double y = Math.Round(view.ToScreen(new Point(0, map.Y(lat))).Y) + 0.5;
            lines.Add((new Point(0, y), new Point(width, y)));
            labels.Add((Invariant($"{Math.Abs(lat)}°{(lat >= 0 ? 'N' : 'S')}"), 0, y, true));
        }

        drawingContext.PushOpacity(0.45);
        foreach ((Point from, Point to) in lines) drawingContext.DrawLine(pen, from, to);
        drawingContext.Pop();

        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface(TextElementFont(), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        foreach ((string text, double x, double y, bool vertical) in labels)
        {
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, FontSize, Foreground, dip);
            // Latitudes at the left edge on their line, longitudes at the top edge beside theirs; none in the corner.
            var at = vertical ? new Point(4, y - formatted.Height / 2) : new Point(x + 3, 3);
            if (vertical ? at.Y < formatted.Height + 6 || at.Y + formatted.Height > height : at.X < 44 || at.X + formatted.Width > width) continue;
            var box = new Rect(at.X - 3, at.Y - 1, formatted.Width + 6, formatted.Height + 2);
            drawingContext.PushOpacity(0.85);
            drawingContext.DrawRoundedRectangle(LabelBackground, null, box, 3, 3);
            drawingContext.Pop();
            drawingContext.DrawText(formatted, at);
        }
    }

    /// <summary>Degrees between lines: the smallest step at least <see cref="MinSpacing"/> pixels apart.</summary>
    internal static int Step(double pixelsPerDegree)
    {
        foreach (int step in Steps)
            if (step * pixelsPerDegree >= MinSpacing) return step;
        return Steps[^1];
    }

    private static int FloorTo(double degrees, int step) => (int)Math.Floor(degrees / step) * step;

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);

    private FontFamily TextElementFont() => (FontFamily)GetValue(System.Windows.Documents.TextElement.FontFamilyProperty);

    private void Attach()
    {
        Detach();
        _host = MapCanvas.Ancestor<ZoomPanel>(this);
        if (_host is null) return;
        _host.ViewChanged += OnViewChanged;
        InvalidateVisual();
    }

    private void Detach()
    {
        if (_host is not null) _host.ViewChanged -= OnViewChanged;
        _host = null;
    }

    private void OnViewChanged(object? sender, EventArgs e) => InvalidateVisual();
}
