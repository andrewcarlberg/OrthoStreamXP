using OrthoStreamXP.Gui;
using OrthoStreamXP.Scenery;
using OrthoStreamXP.Textures;

namespace OrthoStreamXP.Tests;

/// <summary>The exposure: darkening the photos in linear light, its setting, and the preview of the colour settings.</summary>
public sealed class ExposureTests : IDisposable
{
    private readonly TempTree _tree = new();

    public void Dispose() => _tree.Dispose();

    [Fact]
    public void An_exposure_is_a_whole_percent_from_one_stop_darker_to_full()
    {
        Assert.Equal(Exposure.Full, Exposure.Percent(120));
        Assert.Equal(Exposure.Min, Exposure.Percent(10));
        Assert.Equal(85, Exposure.Percent(84.6));
    }

    [Fact]
    public void Half_the_light_is_one_stop_darker_in_linear_light_and_black_stays_black()
    {
        Assert.Equal(Exposure.ToSrgb(0.5) * 255, Exposure.Darken(255, 0.5), 6); // about 188, not 128: the shadows are not crushed
        Assert.Equal(0, Exposure.Darken(0, 0.5), 6);
        Assert.Equal(255, Exposure.Darken(255, 1), 6);
    }

    [Fact]
    public void Less_exposure_is_darker_for_every_value_and_keeps_its_order()
    {
        for (int v = 1; v < 256; v++)
        {
            Assert.True(Exposure.Darken(v, 0.8) < v, $"80 % of {v}");
            Assert.True(Exposure.Darken(v, 0.5) < Exposure.Darken(v, 0.8), $"50 % of {v}");
            Assert.True(Exposure.Darken(v, 0.5) > Exposure.Darken(v - 1, 0.5), $"50 % of {v} keeps its order");
        }
    }

    [Fact]
    public void The_setting_is_100_unless_lowered_and_never_below_50()
    {
        MountSettings Parse(params string[] extra)
        {
            Directory.CreateDirectory(_tree.PathOf("source"));
            return MountSettings.FromValues(TestSettings.Of(
                [$"source = {_tree.PathOf("source")}", $"cache = {_tree.PathOf("cache")}", $"mountpoint = {_tree.PathOf("mount")}", "texture-provider = bing", .. extra]));
        }
        Assert.Equal(100, Parse().Exposure);
        Assert.Equal(85, Parse("exposure = 85").Exposure);
        Assert.Throws<SettingsException>(() => Parse("exposure = 40"));
        Assert.Throws<SettingsException>(() => Parse("exposure = 101"));
    }

    // ---------------------------------------------------------------- the preview

    /// <summary>A cache with one finished ZL16 photo texture (256 px, a summer green) and one streaming level that is not offered.</summary>
    private string Cache()
    {
        byte[] rgb = new byte[256 * 256 * 3];
        for (int i = 0; i < rgb.Length; i += 3) (rgb[i], rgb[i + 1], rgb[i + 2]) = (60, 100, 50);
        _tree.WriteBytes("cache/level0_256-256/Pack/textures/21840_35584_BI16.dds", Dds.Encode(rgb, 256, 256, 1, out _, out _));
        _tree.WriteBytes("cache/level0_256-256/Pack/.xpstream/mips/21840_35600_BI16.dds", [1, 2, 3]);
        Directory.CreateDirectory(_tree.PathOf("history"));
        return _tree.PathOf("cache");
    }

    private static double Brightness(byte[] rgb) => rgb.Average(v => (double)v);

    [Fact]
    public void The_preview_offers_the_finished_photos_and_shows_the_contrast_without_settings()
    {
        var preview = new ColourPreview(Cache(), _tree.PathOf("history"));

        PreviewPhoto photo = Assert.Single(preview.FindPhotos(TestContext.Current.CancellationToken));
        PreviewPicture picture = preview.Render(photo, new ColourAdjustments(SeasonOptions.Off, 100))!;

        Assert.Equal(256, picture.Size);
        // The contrast is always on: the summer green (darker than mid-grey) comes out darker.
        Assert.True(Brightness(picture.After) < Brightness(picture.Before), "the contrast changes nothing");
        Assert.Contains("Contrast 60 %", Assert.Single(picture.Notes), StringComparison.Ordinal);
    }

    [Fact]
    public void The_preview_shows_the_season_and_the_exposure_as_X_Plane_gets_them()
    {
        var preview = new ColourPreview(Cache(), _tree.PathOf("history"));
        PreviewPhoto photo = preview.FindPhotos(TestContext.Current.CancellationToken)[0];

        PreviewPicture darker = preview.Render(photo, new ColourAdjustments(SeasonOptions.Off, 50))!;
        PreviewPicture winter = preview.Render(photo, new ColourAdjustments(new SeasonOptions(true, 1, SeasonChoice.Winter), 100))!;

        Assert.True(Brightness(darker.After) < Brightness(darker.Before) - 20);
        Assert.NotEqual(winter.Before, winter.After);
        Assert.True(winter.After[0] > winter.After[1], "winter: the green turns brown"); // red above green
    }
}
