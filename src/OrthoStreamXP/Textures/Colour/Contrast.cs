namespace OrthoStreamXP.Textures;

/// <summary>
/// The contrast of the photos: an S-curve on each sRGB channel round mid-grey (sigmoidal contrast), black and white
/// kept, nothing clipped. Fixed at 60 % of a scale whose 100 % is a steepness of 6: the mid-tones about 1.26 times as
/// contrasty, aerial photos being hazy (the samples of 2026-11-18). Always on, whatever the other colour settings; made
/// into the photos' one lookup table as X-Plane reads them (<see cref="PhotoColours"/>).
/// </summary>
internal static class Contrast
{
    /// <summary>The strength in use, of the scale whose 100 % is a steepness of 6.</summary>
    public const double Strength = 0.6;

    private const double Steepness = 6 * Strength;
    private static readonly double Low = Sigmoid(-Steepness / 2), High = Sigmoid(Steepness / 2);

    /// <summary>One channel's value (0-255, clamped) through the curve, unrounded.</summary>
    public static double Apply(double value) =>
        (Sigmoid(Steepness * (Math.Clamp(value, 0, 255) / 255 - 0.5)) - Low) / (High - Low) * 255;

    private static double Sigmoid(double x) => 1 / (1 + Math.Exp(-x));
}
