using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace OrthoStreamXP.Gui;

/// <summary>A picture of a dragged row that floats with the pointer, lifted by a shadow, drawn over the page.</summary>
internal sealed class DragGhost : Adorner
{
    private readonly ImageBrush _picture;
    private readonly Size _size;
    private Point _at;

    /// <param name="page">The element the ghost is drawn over; <see cref="MoveTo"/> takes points relative to it.</param>
    /// <param name="row">The row, pictured as it looks now.</param>
    public DragGhost(UIElement page, FrameworkElement row) : base(page)
    {
        IsHitTestVisible = false;
        _size = new Size(row.ActualWidth, row.ActualHeight);
        _picture = new ImageBrush(Snapshot(row, _size));
        _picture.Freeze();
        Opacity = 0.92;
        Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = 0.35 };
    }

    /// <summary>Puts the ghost's top-left corner at <paramref name="topLeft"/>.</summary>
    public void MoveTo(Point topLeft)
    {
        _at = topLeft;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext) =>
        drawingContext.DrawRectangle(_picture, null, new Rect(_at, _size));

    /// <summary>The row drawn through a brush, so the picture starts at its own corner, not at its place in the list.</summary>
    private static RenderTargetBitmap Snapshot(FrameworkElement row, Size size)
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(row);
        var drawing = new DrawingVisual();
        using (DrawingContext context = drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(row), null, new Rect(size));
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(size.Width * dpi.DpiScaleX)), Math.Max(1, (int)Math.Ceiling(size.Height * dpi.DpiScaleY)),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        bitmap.Freeze();
        return bitmap;
    }
}
