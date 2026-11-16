using System.Windows;

namespace OrthoStreamXP.Gui;

/// <summary>
/// What a zoomable map shows: its scale from map units to screen pixels and where the map's corner is on screen. The
/// whole map fits the view at the smallest scale (no zooming out past that), and it cannot be dragged out of sight; a
/// map smaller than the view on one side stays centred on that side.
/// </summary>
internal sealed class MapViewport
{
    public double Scale { get; private set; } = 1;
    public double OffsetX { get; private set; }
    public double OffsetY { get; private set; }
    public Size View { get; private set; }
    public Size Content { get; private set; }

    /// <summary>The largest scale (screen pixels a map unit).</summary>
    public double MaxScale { get; set; } = 4;

    /// <summary>Showing the whole map: it refits when the view or the map changes size.</summary>
    public bool IsFitted { get; private set; } = true;

    /// <summary>The scale that shows the whole map.</summary>
    public double FitScale =>
        Content.Width > 0 && Content.Height > 0 && View.Width > 0 && View.Height > 0
            ? Math.Min(Math.Min(View.Width / Content.Width, View.Height / Content.Height), MaxScale)
            : 1;

    public bool CanZoomIn => Scale < Math.Max(FitScale, MaxScale) - 1e-9;

    public bool CanZoomOut => Scale > FitScale + 1e-9;

    public void Resize(Size view, Size content)
    {
        View = view;
        Content = content;
        if (IsFitted) Fit();
        else Clamp();
    }

    public void Fit()
    {
        Scale = FitScale;
        IsFitted = true;
        Clamp();
    }

    /// <summary>Zooms by <paramref name="factor"/>, keeping the place under <paramref name="at"/> (screen) where it is; false when at the limit.</summary>
    public bool ZoomAt(Point at, double factor)
    {
        double fit = FitScale;
        double scale = Math.Clamp(Scale * factor, fit, Math.Max(fit, MaxScale));
        if (Math.Abs(scale - Scale) < 1e-9) return false;
        OffsetX = at.X - (at.X - OffsetX) * scale / Scale;
        OffsetY = at.Y - (at.Y - OffsetY) * scale / Scale;
        Scale = scale;
        IsFitted = scale <= fit + 1e-9;
        Clamp();
        return true;
    }

    public void PanBy(Vector by)
    {
        OffsetX += by.X;
        OffsetY += by.Y;
        Clamp();
    }

    public Point ToScreen(Point map) => new(OffsetX + map.X * Scale, OffsetY + map.Y * Scale);

    public Point ToMap(Point screen) => new((screen.X - OffsetX) / Scale, (screen.Y - OffsetY) / Scale);

    private void Clamp()
    {
        Scale = Math.Max(Scale, FitScale);
        OffsetX = ClampAxis(OffsetX, View.Width, Content.Width * Scale);
        OffsetY = ClampAxis(OffsetY, View.Height, Content.Height * Scale);
    }

    private static double ClampAxis(double offset, double view, double content) =>
        content <= view ? (view - content) / 2 : Math.Clamp(offset, view - content, 0);
}
