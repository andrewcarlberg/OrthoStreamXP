using System.Windows;
using OrthoStreamXP.Gui;

namespace OrthoStreamXP.Tests;

/// <summary>The cache map's zoom and pan: the whole map fits at first, zooming keeps the place under the pointer, and the map never leaves the view.</summary>
public sealed class MapViewportTests
{
    private static MapViewport Fitted(double viewWidth = 800, double viewHeight = 400, double mapWidth = 1000, double mapHeight = 1000)
    {
        var viewport = new MapViewport { MaxScale = 4 };
        viewport.Resize(new Size(viewWidth, viewHeight), new Size(mapWidth, mapHeight));
        return viewport;
    }

    [Fact]
    public void The_whole_map_fits_and_is_centred_on_its_short_side()
    {
        MapViewport viewport = Fitted();

        Assert.Equal(0.4, viewport.Scale, 6);
        Assert.Equal((800 - 400) / 2.0, viewport.OffsetX, 6);
        Assert.Equal(0, viewport.OffsetY, 6);
        Assert.True(viewport.IsFitted);
        Assert.False(viewport.CanZoomOut);
        Assert.True(viewport.CanZoomIn);
    }

    [Fact]
    public void Zooming_keeps_the_place_under_the_pointer()
    {
        MapViewport viewport = Fitted();
        var pointer = new Point(400, 200);
        Point before = viewport.ToMap(pointer);

        Assert.True(viewport.ZoomAt(pointer, 2));

        Assert.Equal(0.8, viewport.Scale, 6);
        Point after = viewport.ToMap(pointer);
        Assert.Equal(before.X, after.X, 6);
        Assert.Equal(before.Y, after.Y, 6);
        Assert.False(viewport.IsFitted);
    }

    [Fact]
    public void Zoom_stops_at_the_whole_map_and_at_the_largest_scale()
    {
        MapViewport viewport = Fitted();

        Assert.False(viewport.ZoomAt(new Point(0, 0), 0.5));
        Assert.True(viewport.ZoomAt(new Point(0, 0), 100));
        Assert.Equal(4, viewport.Scale, 6);
        Assert.False(viewport.ZoomAt(new Point(0, 0), 2));

        Assert.True(viewport.ZoomAt(new Point(0, 0), 0.001));
        Assert.Equal(0.4, viewport.Scale, 6);
        Assert.True(viewport.IsFitted);
    }

    [Fact]
    public void The_map_cannot_be_dragged_out_of_sight()
    {
        MapViewport viewport = Fitted();
        viewport.ZoomAt(new Point(400, 200), 2); // the map is now 800 x 800 in a 800 x 400 view

        viewport.PanBy(new Vector(5000, 5000));
        Assert.Equal(0, viewport.OffsetX, 6);
        Assert.Equal(0, viewport.OffsetY, 6);

        viewport.PanBy(new Vector(-5000, -5000));
        Assert.Equal(0, viewport.OffsetX, 6);
        Assert.Equal(400 - 800, viewport.OffsetY, 6);
    }

    [Fact]
    public void A_fitted_map_refits_when_the_view_grows_but_a_zoomed_one_keeps_its_zoom()
    {
        MapViewport viewport = Fitted();
        viewport.Resize(new Size(800, 800), new Size(1000, 1000));
        Assert.Equal(0.8, viewport.Scale, 6);

        viewport.ZoomAt(new Point(0, 0), 2);
        viewport.Resize(new Size(900, 800), new Size(1000, 1000));
        Assert.Equal(1.6, viewport.Scale, 6);

        viewport.Fit();
        Assert.Equal(0.8, viewport.Scale, 6);
    }

    [Theory]
    [InlineData(100, 1)]
    [InlineData(30, 2)]
    [InlineData(10, 5)]
    [InlineData(0.5, 30)]
    public void Grid_lines_are_far_enough_apart(double pixelsPerDegree, int step) =>
        Assert.Equal(step, Graticule.Step(pixelsPerDegree));

    [Fact]
    public void The_projection_turns_back_into_degrees()
    {
        var map = new MapProjection(10, 55, 100, 0.6);

        Assert.Equal(0, map.X(10), 6);
        Assert.Equal(60, map.X(11), 6);
        Assert.Equal(200, map.Y(53), 6);
        Assert.Equal(12.5, map.Lon(map.X(12.5)), 6);
        Assert.Equal(51.25, map.Lat(map.Y(51.25)), 6);
    }
}
