using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OrthoStreamXP.Gui;

/// <summary>
/// A map that zooms and pans: the mouse wheel zooms around the pointer, dragging moves the map (a drag never clicks what
/// it started on), and <see cref="ZoomIn"/>/<see cref="ZoomOut"/>/<see cref="ShowAll"/> are for buttons. Its layers
/// (<see cref="MapCanvas"/>, <see cref="Graticule"/>) place themselves by its <see cref="Viewport"/>. It takes the
/// width it is given and a height that follows the map's shape, between <see cref="MinViewHeight"/> and
/// <see cref="MaxViewHeight"/>.
/// </summary>
internal sealed class ZoomPanel : Grid
{
    public static readonly DependencyProperty ContentWidthProperty = DependencyProperty.Register(nameof(ContentWidth), typeof(double), typeof(ZoomPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ContentHeightProperty = DependencyProperty.Register(nameof(ContentHeight), typeof(double), typeof(ZoomPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxScaleProperty = DependencyProperty.Register(nameof(MaxScale), typeof(double), typeof(ZoomPanel),
        new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty MinViewHeightProperty = DependencyProperty.Register(nameof(MinViewHeight), typeof(double), typeof(ZoomPanel),
        new FrameworkPropertyMetadata(300.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxViewHeightProperty = DependencyProperty.Register(nameof(MaxViewHeight), typeof(double), typeof(ZoomPanel),
        new FrameworkPropertyMetadata(520.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>The scale shown (screen pixels a map unit): set by the panel, for a binding to read.</summary>
    public static readonly DependencyProperty ScaleProperty = DependencyProperty.Register(nameof(Scale), typeof(double), typeof(ZoomPanel),
        new FrameworkPropertyMetadata(1.0));

    private const double WheelStep = 1.25, ButtonStep = 1.6;
    private Point? _press;
    private bool _dragging;

    public ZoomPanel()
    {
        ClipToBounds = true;
        ZoomIn = new RelayCommand(() => ZoomBy(ButtonStep), () => Viewport.CanZoomIn);
        ZoomOut = new RelayCommand(() => ZoomBy(1 / ButtonStep), () => Viewport.CanZoomOut);
        ShowAll = new RelayCommand(() =>
        {
            Viewport.Fit();
            Changed();
        }, () => !Viewport.IsFitted);
    }

    /// <summary>The scale or the position changed: the layers place themselves again.</summary>
    public event EventHandler? ViewChanged;

    public MapViewport Viewport { get; } = new();

    public ICommand ZoomIn { get; }
    public ICommand ZoomOut { get; }
    public ICommand ShowAll { get; }

    /// <summary>The map's size in map units.</summary>
    public double ContentWidth
    {
        get => (double)GetValue(ContentWidthProperty);
        set => SetValue(ContentWidthProperty, value);
    }

    public double ContentHeight
    {
        get => (double)GetValue(ContentHeightProperty);
        set => SetValue(ContentHeightProperty, value);
    }

    public double MaxScale
    {
        get => (double)GetValue(MaxScaleProperty);
        set => SetValue(MaxScaleProperty, value);
    }

    public double MinViewHeight
    {
        get => (double)GetValue(MinViewHeightProperty);
        set => SetValue(MinViewHeightProperty, value);
    }

    public double MaxViewHeight
    {
        get => (double)GetValue(MaxViewHeightProperty);
        set => SetValue(MaxViewHeightProperty, value);
    }

    public double Scale
    {
        get => (double)GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        double width = double.IsInfinity(constraint.Width) ? ContentWidth : constraint.Width;
        double height = ContentWidth > 0 ? width * ContentHeight / ContentWidth : MinViewHeight;
        height = Math.Clamp(height, MinViewHeight, Math.Max(MinViewHeight, MaxViewHeight));
        if (!double.IsInfinity(constraint.Height)) height = Math.Min(height, constraint.Height);
        var size = new Size(width, height);
        base.MeasureOverride(size);
        return size;
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var before = (Viewport.Scale, Viewport.OffsetX, Viewport.OffsetY, Viewport.View, Viewport.Content);
        Viewport.MaxScale = MaxScale;
        Viewport.Resize(arrangeSize, new Size(ContentWidth, ContentHeight));
        if (before != (Viewport.Scale, Viewport.OffsetX, Viewport.OffsetY, Viewport.View, Viewport.Content)) Changed();
        return base.ArrangeOverride(arrangeSize);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        // At the limit the wheel scrolls the page instead.
        if (Viewport.ZoomAt(e.GetPosition(this), Math.Pow(WheelStep, e.Delta / 120.0)))
        {
            Changed();
            e.Handled = true;
        }
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        _press = e.GetPosition(this);
        _dragging = false;
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (_press is not { } press || e.LeftButton != MouseButtonState.Pressed)
        {
            _press = null;
            return;
        }
        Point now = e.GetPosition(this);
        if (!_dragging)
        {
            if (Math.Abs(now.X - press.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(now.Y - press.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            // Taking the mouse from the area pressed: its click is cancelled.
            _dragging = true;
            CaptureMouse();
            Cursor = Cursors.SizeAll;
        }
        Viewport.PanBy(now - press);
        _press = now;
        Changed();
        e.Handled = true;
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        _press = null;
        if (!_dragging) return;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragging = false;
        _press = null;
        ClearValue(CursorProperty);
    }

    private void ZoomBy(double factor)
    {
        if (Viewport.ZoomAt(new Point(ActualWidth / 2, ActualHeight / 2), factor)) Changed();
    }

    private void Changed()
    {
        if (Math.Abs(Scale - Viewport.Scale) > 1e-9)
        {
            SetCurrentValue(ScaleProperty, Viewport.Scale);
            RelayCommand.Refresh();
        }
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }
}
