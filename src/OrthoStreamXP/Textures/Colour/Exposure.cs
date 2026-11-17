namespace OrthoStreamXP.Textures;

/// <summary>
/// The exposure of the photos: the light they let through, as a camera's exposure, from <see cref="Min"/> to 100 %.
/// Aerial photos are often brighter than X-Plane's own scenery around them; less exposure darkens them evenly. The
/// light is scaled in linear light and put back into sRGB, so 50 % is one stop darker without crushing the shadows
/// or greying the highlights. Made with the season and the contrast into the photos' one lookup table over the RGB565
/// colours (<see cref="PhotoColours"/>), applied as X-Plane reads.
/// </summary>
internal static class Exposure
{
    /// <summary>The settings' default and most, in percent: the photos as they are.</summary>
    public const int Full = 100;

    /// <summary>The least the settings allow, in percent: one stop darker.</summary>
    public const int Min = 50;

    /// <summary>The exposure a table is made for: <paramref name="percent"/> within <see cref="Min"/> to <see cref="Full"/>, rounded to a whole percent.</summary>
    internal static int Percent(double percent) => (int)Math.Round(Math.Clamp(percent, Min, Full));

    /// <summary>One channel's value (0-255, clamped) with its light scaled by <paramref name="factor"/>, unrounded.</summary>
    internal static double Darken(double value, double factor) => ToSrgb(ToLinear(Math.Clamp(value, 0, 255) / 255) * factor) * 255;

    internal static double ToLinear(double srgb) => srgb <= 0.04045 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);

    internal static double ToSrgb(double linear) => linear <= 0.0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055;
}
