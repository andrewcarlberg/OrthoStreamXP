using OrthoStreamXP.Textures;

namespace OrthoStreamXP.Tests;

/// <summary>The sharpening of the photos as a texture is built: an unsharp mask on the brightness, in place.</summary>
public sealed class SharpeningTests
{
    [Fact]
    public void A_flat_picture_stays_as_it_is()
    {
        byte[] rgb = Flat(32, 24, 90, 120, 60);
        byte[] before = (byte[])rgb.Clone();

        Sharpening.Apply(rgb, 32, 24, 1, Sharpening.AirportStrength);

        Assert.Equal(before, rgb);
    }

    [Fact]
    public void An_edge_gets_steeper_on_both_sides()
    {
        // Dark on the left, light on the right.
        const int width = 32, height = 8;
        byte[] rgb = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            rgb.AsSpan((y * width + x) * 3, 3).Fill(x < width / 2 ? (byte)80 : (byte)160);

        Sharpening.Apply(rgb, width, height, 1, Sharpening.AirportStrength);

        int row = 4 * width * 3;
        Assert.True(rgb[row + (width / 2 - 1) * 3] < 80, $"the dark side of the edge: {rgb[row + (width / 2 - 1) * 3]}");
        Assert.True(rgb[row + width / 2 * 3] > 160, $"the light side of the edge: {rgb[row + width / 2 * 3]}");
        Assert.Equal(80, rgb[row]);                    // far from the edge, as it was
        Assert.Equal(160, rgb[row + (width - 1) * 3]);
    }

    [Fact]
    public void Grey_stays_grey_so_edges_get_no_colour_fringes()
    {
        const int size = 48;
        byte[] rgb = Images.SyntheticTile(5, 6, 14) is var tile ? Images.Resize(tile, 256, 256, size, size, 1) : [];
        for (int i = 0; i < rgb.Length; i += 3) rgb[i + 1] = rgb[i + 2] = rgb[i];

        Sharpening.Apply(rgb, size, size, 1, Sharpening.AirportStrength);

        for (int i = 0; i < rgb.Length; i += 3)
            Assert.True(rgb[i] == rgb[i + 1] && rgb[i] == rgb[i + 2], $"pixel {i / 3}: {rgb[i]}, {rgb[i + 1]}, {rgb[i + 2]}");
    }

    [Fact]
    public void The_number_of_workers_changes_nothing()
    {
        const int size = 300; // several bands, the last one short
        byte[] one = Images.Resize(Images.SyntheticTile(1, 2, 14), 256, 256, size, size, 1), many = (byte[])one.Clone();

        Sharpening.Apply(one, size, size, 1, Sharpening.AirportStrength);
        Sharpening.Apply(many, size, size, 8, Sharpening.AirportStrength);

        Assert.Equal(one, many);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 5)]
    [InlineData(7, 3)]
    public void Pictures_smaller_than_the_blur_repeat_their_edges(int width, int height)
    {
        byte[] rgb = Flat(width, height, 10, 200, 30);
        rgb[0] = 250;
        byte[] before = (byte[])rgb.Clone();

        Sharpening.Apply(rgb, width, height, 4, Sharpening.AirportStrength);

        if (width * height == 1) Assert.Equal(before, rgb); // nothing round it to stand out from
        else Assert.True(rgb[0] > 250 && rgb[1] > 200, $"the brighter pixel only brighter: {rgb[0]}, {rgb[1]}");
    }

    [Fact]
    public void A_picture_shorter_than_its_size_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Sharpening.Apply(new byte[10], 4, 4, 1, Sharpening.AirportStrength));
    }

    [Theory]
    [InlineData(15, 1.0)]
    [InlineData(16, 1.0)] // the wide areas: full strength
    [InlineData(17, 1.0)]
    [InlineData(18, 0.6)] // the airports: finer imagery, less
    [InlineData(19, 0.6)]
    public void Areas_are_sharpened_at_full_strength_airports_at_60_percent(int fileZoom, double strength) =>
        Assert.Equal(strength, Sharpening.StrengthFor(fileZoom));

    [Fact]
    public void Full_strength_steepens_an_edge_more_and_zero_leaves_it()
    {
        const int width = 32, height = 8, dark = (width / 2 - 1) * 3;
        byte[] Edge() => [.. Enumerable.Range(0, width * height).SelectMany(i => Enumerable.Repeat(i % width < width / 2 ? (byte)80 : (byte)160, 3))];
        byte[] airport = Edge(), area = Edge(), none = Edge();

        Sharpening.Apply(airport, width, height, 1, Sharpening.AirportStrength);
        Sharpening.Apply(area, width, height, 1, Sharpening.AreaStrength);
        Sharpening.Apply(none, width, height, 1, 0);

        Assert.True(area[dark] < airport[dark] && airport[dark] < 80, $"the dark side: area {area[dark]}, airport {airport[dark]}");
        Assert.Equal(Edge(), none);
        Assert.Throws<ArgumentOutOfRangeException>(() => Sharpening.Apply(Edge(), width, height, 1, 1.5));
    }

    private static byte[] Flat(int width, int height, byte r, byte g, byte b)
    {
        byte[] rgb = new byte[width * height * 3];
        for (int i = 0; i < rgb.Length; i += 3) (rgb[i], rgb[i + 1], rgb[i + 2]) = (r, g, b);
        return rgb;
    }
}
