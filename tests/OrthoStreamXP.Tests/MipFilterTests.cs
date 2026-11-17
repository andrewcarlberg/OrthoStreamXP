using OrthoStreamXP.Textures;

namespace OrthoStreamXP.Tests;

public sealed class MipFilterTests
{
    [Fact]
    public void Every_flat_colour_stays_exactly_itself()
    {
        byte[] values = [.. Enumerable.Range(0, 256).Select(c => (byte)c)];
        // 256 pixels in a 16 x 16 picture, each 2 x 2 group one value: the half is those values again.
        byte[] picture = new byte[32 * 32 * 3];
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
                for (int k = 0; k < 3; k++)
                    picture[(y * 32 + x) * 3 + k] = values[y / 2 * 16 + x / 2];

        byte[] half = MipFilter.Half(picture, 32, 32, 1);

        for (int i = 0; i < 256; i++) Assert.Equal(values[i], half[i * 3]);
    }

    [Fact]
    public void Black_and_white_average_to_the_grey_of_half_their_light_not_to_128()
    {
        byte[] checker = new byte[2 * 2 * 3];
        checker.AsSpan(0, 3).Fill(255);
        checker.AsSpan(9, 3).Fill(255);

        Assert.Equal(188, MipFilter.Half(checker, 2, 2, 1)[0]); // sRGB 188 is half of white's light
        Assert.Equal(128, Images.BoxHalf(checker, 2, 2, 1)[0]); // the stored values' average, far darker
    }

    [Fact]
    public void The_tables_round_like_the_curve_itself()
    {
        var random = new Random(5);
        for (int i = 0; i < 20_000; i++)
        {
            // Dark values are where the tables are tightest: sample them as densely as the rest.
            double light = i % 2 == 0 ? random.NextDouble() : Math.Pow(random.NextDouble(), 6);
            uint scaled = (uint)Math.Round(light * (1 << 24));
            double value = StoredValue(scaled / (double)(1 << 24));
            int exact = (int)Math.Round(value, MidpointRounding.AwayFromZero);
            bool onBoundary = Math.Abs(value - Math.Floor(value) - 0.5) < 0.01; // closer than the 2^24 scale can tell
            if (!onBoundary) Assert.Equal(exact, MipFilter.Stored(scaled));
            else Assert.InRange(MipFilter.Stored(scaled), exact - 1, exact);
        }
    }

    [Fact]
    public void The_whole_chain_keeps_the_pictures_light_where_stored_averages_darken_it()
    {
        const int size = 256;
        byte[] photo = Images.SyntheticTile(400, 700, 15);
        // Each level is rounded to whole stored values once: across eight levels that drifts a little, never far.
        Assert.InRange(StoredValue(MeanLight(Chain(photo, size, MipFilter.Half))) - StoredValue(MeanLight(photo)), -1.5, 1.5);

        // A fine pattern of dark and light (shadows between roofs): the light it gives is sRGB 149, not the values' 120.
        byte[] pattern = new byte[size * size * 3];
        for (int p = 0; p < size * size; p++) pattern.AsSpan(p * 3, 3).Fill((p + p / size) % 2 == 0 ? (byte)40 : (byte)200);
        Assert.InRange(Chain(pattern, size, MipFilter.Half)[0], 148, 150);
        Assert.Equal(120, Chain(pattern, size, Images.BoxHalf)[0]);
    }

    private static byte[] Chain(byte[] picture, int size, Func<byte[], int, int, int, byte[]> half)
    {
        for (int s = size; s > 1; s >>= 1) picture = half(picture, s, s, 1);
        return picture;
    }

    private static double MeanLight(byte[] rgb) => rgb.Average(v => MipFilter.Linear(v / 255.0));

    private static double StoredValue(double light) => 255 * (light <= 0.0031308 ? light * 12.92 : 1.055 * Math.Pow(light, 1 / 2.4) - 0.055);
}
