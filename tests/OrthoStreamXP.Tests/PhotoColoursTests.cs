using OrthoStreamXP.Scenery;
using OrthoStreamXP.Textures;

namespace OrthoStreamXP.Tests;

/// <summary>The season, the contrast and the exposure made into one lookup table, rounded to 565 once.</summary>
public sealed class PhotoColoursTests
{
    // Whole 1/32 steps of the seasons, so the tables are made for exactly these.
    private static readonly SeasonState Winter = new(0, 0, 1, 1), Autumn = new(0, 1, 0.125, 0);

    [Fact]
    public void Without_a_season_or_an_exposure_the_photos_still_get_the_contrast()
    {
        ushort[] table = PhotoColours.TableFor(SeasonState.Summer, Exposure.Full);

        for (int c = 0; c < 65536; c += 97)
        {
            var (r, g, b) = ColourTable.Decode565(c);
            Assert.Equal(ColourTable.Encode565(Contrast.Apply(r), Contrast.Apply(g), Contrast.Apply(b)), table[c]);
        }
        Assert.NotEqual(ColourTable.Encode565(60, 100, 50), table[ColourTable.Encode565(60, 100, 50)]);
        // A season too small to show is summer: the same table.
        Assert.Same(table, PhotoColours.TableFor(new SeasonState(0.015, 0, 0.015, 0), Exposure.Full));
    }

    [Fact]
    public void Each_season_and_exposure_is_one_table_kept_for_every_texture()
    {
        ushort[] both = PhotoColours.TableFor(Winter, 60);

        Assert.Same(both, PhotoColours.TableFor(Winter, 60.2)); // a whole percent
        Assert.NotSame(both, PhotoColours.TableFor(Winter, 70));
        Assert.NotSame(both, PhotoColours.TableFor(Winter, Exposure.Full));
        Assert.NotSame(both, PhotoColours.TableFor(SeasonState.Summer, 60));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(75)]
    [InlineData(50)]
    public void One_rounding_is_never_farther_from_the_exact_colour_than_one_per_adjustment(double exposure)
    {
        double factor = exposure / 100, oneRounding = 0, eachRounded = 0;
        ushort[] season = Table(c => Seasons.Recolor(c.R, c.G, c.B, Autumn));
        ushort[] contrast = Table(c => (Contrast.Apply(c.R), Contrast.Apply(c.G), Contrast.Apply(c.B)));
        ushort[] darker = Table(c => (Exposure.Darken(c.R, factor), Exposure.Darken(c.G, factor), Exposure.Darken(c.B, factor)));
        ushort[] all = PhotoColours.TableFor(Autumn, exposure);

        for (int c = 0; c < 65536; c++)
        {
            var (r, g, b) = ColourTable.Decode565(c);
            var exact = PhotoColours.Adjust(r, g, b, Autumn, factor);
            var one = Errors(exact, all[c]);
            var each = Errors(exact, darker[contrast[season[c]]]);
            Assert.True(one.R <= each.R + 1e-9 && one.G <= each.G + 1e-9 && one.B <= each.B + 1e-9, $"colour {c}: {one} against {each}");
            oneRounding += one.R + one.G + one.B;
            eachRounded += each.R + each.G + each.B;
        }

        Assert.True(oneRounding < eachRounded * 0.95, $"one rounding {oneRounding:F0}, one per adjustment {eachRounded:F0}");
    }

    [Fact]
    public void A_season_clock_gives_each_texture_one_table_with_the_contrast_and_the_exposure()
    {
        Assert.True(TextureName.TryParseFile("21840_35584_BI16.dds", out TextureName name));

        Assert.Same(PhotoColours.TableFor(Winter, 60),
            new SeasonClock(new SeasonOptions(true, 1, SeasonChoice.Winter), null, exposure: 60).TableFor(name));
        Assert.Same(PhotoColours.TableFor(SeasonState.Summer, 60),
            new SeasonClock(new SeasonOptions(true, 1, SeasonChoice.Summer), null, exposure: 60).TableFor(name));
        Assert.Same(PhotoColours.TableFor(SeasonState.Summer, Exposure.Full),
            new SeasonClock(new SeasonOptions(true, 1, SeasonChoice.Summer), null).TableFor(name));
    }

    [Fact]
    public void Applying_is_the_tables_recolouring_and_nothing_without_one()
    {
        byte[] dds = Dds.Encode(Images.SyntheticTile(3, 4, 14), 256, 256, 1, out _, out _);
        byte[] expected = (byte[])dds.Clone(), unchanged = (byte[])dds.Clone();
        ushort[] table = PhotoColours.TableFor(Autumn, 75);

        ColourTable.Apply(expected, 0, 8, table);
        PhotoColours.Apply(dds, 0, 8, table);
        PhotoColours.Apply(unchanged, 0, 8, null);

        Assert.Equal(expected, dds);
        Assert.Equal(Dds.Encode(Images.SyntheticTile(3, 4, 14), 256, 256, 1, out _, out _), unchanged);
    }

    /// <summary>One adjustment alone as a lookup table, rounded to 565.</summary>
    private static ushort[] Table(Func<(double R, double G, double B), (double R, double G, double B)> adjust)
    {
        var table = new ushort[65536];
        for (int c = 0; c < table.Length; c++)
        {
            var (r, g, b) = adjust(ColourTable.Decode565(c));
            table[c] = (ushort)ColourTable.Encode565(r, g, b);
        }
        return table;
    }

    /// <summary>How far each channel of a 565 colour lies from the exact colour.</summary>
    private static (double R, double G, double B) Errors((double R, double G, double B) exact, int c)
    {
        var (r, g, b) = ColourTable.Decode565(c);
        return (Math.Abs(r - exact.R), Math.Abs(g - exact.G), Math.Abs(b - exact.B));
    }
}
