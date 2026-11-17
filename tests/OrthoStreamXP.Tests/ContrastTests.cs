using OrthoStreamXP.Textures;

namespace OrthoStreamXP.Tests;

/// <summary>The contrast: an S-curve round mid-grey, black and white kept, nothing clipped.</summary>
public sealed class ContrastTests
{
    [Fact]
    public void Black_white_and_mid_grey_stay_where_they_are()
    {
        Assert.Equal(0, Contrast.Apply(0), 9);
        Assert.Equal(255, Contrast.Apply(255), 9);
        Assert.Equal(127.5, Contrast.Apply(127.5), 9);
    }

    [Fact]
    public void Darker_than_mid_grey_gets_darker_and_lighter_lighter_without_clipping()
    {
        for (int v = 1; v < 255; v++)
        {
            double after = Contrast.Apply(v);
            if (v < 127) Assert.True(after < v, $"{v} -> {after:F1}");
            if (v > 128) Assert.True(after > v, $"{v} -> {after:F1}");
            Assert.InRange(after, 0.0, 255.0);
            Assert.True(after > Contrast.Apply(v - 1), $"{v} keeps its order");
        }
    }

    [Fact]
    public void The_mid_tones_are_steeper_and_the_ends_gentler()
    {
        double mid = Contrast.Apply(137.5) - Contrast.Apply(117.5), end = Contrast.Apply(20) - Contrast.Apply(0);

        Assert.True(mid > 20 * 1.2, $"mid-tones: 20 steps became {mid:F1}"); // about 1.26 times at 60 %
        Assert.True(end < 20, $"shadows: 20 steps became {end:F1}");
    }

    [Fact]
    public void Values_outside_the_range_are_clamped()
    {
        Assert.Equal(Contrast.Apply(0), Contrast.Apply(-10), 9);
        Assert.Equal(Contrast.Apply(255), Contrast.Apply(300), 9);
    }
}
