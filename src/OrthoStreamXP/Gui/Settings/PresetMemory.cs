using System.Globalization;
using System.Windows.Input;
using OrthoStreamXP.Setup;
using OrthoStreamXP.Textures;
using OrthoStreamXP.Tuning;

namespace OrthoStreamXP.Gui;

/// <summary>The graphics memory figures for the preset cards: the photo textures' estimate, and the graphics card's own.</summary>
internal static class PresetMemory
{
    public static Tuning.GraphicsMemory Estimate { get; private set; } = Tuning.GraphicsMemory.TestFlight;

    public static long? Card { get; } = Tuning.GraphicsMemory.CardBytes();

    /// <summary>The latest connection test, kept beside the settings file.</summary>
    public static ConnectionResult? Connection { get; private set; } = ConnectionResult.Load(ConnectionResult.PathFor(SettingsDocument.DefaultPath));

    /// <summary>The preset for this PC: from the graphics card, and the last flight once there is one.</summary>
    public static Tuning.Recommendation Recommended { get; private set; } = Tuning.PresetAdvisor.Recommend(Card, Tuning.GraphicsMemory.TestFlight, [], Connection);

    /// <summary>
    /// How each preset suits this PC, from the same facts as <see cref="Recommended"/>; Dynamic from each base is among
    /// them, so its card tells of the base chosen on it.
    /// </summary>
    public static IReadOnlyList<PresetFit> Fits { get; private set; } = Tuning.PresetAdvisor.Assess(Card, Tuning.GraphicsMemory.TestFlight, [], Connection, QualityPreset.Offered);

    public static PresetFit? FitOf(QualityPreset preset) => Fits.FirstOrDefault(f => f.Preset == preset);

    public static void Load(string? cache)
    {
        Estimate = Tuning.GraphicsMemory.Load(AppPaths.History);
        Connection = ConnectionResult.Load(ConnectionResult.PathFor(SettingsDocument.DefaultPath));
        Tuning.FlightReport[] flights = [.. Tuning.FlightReport.History(AppPaths.History)];
        Recommended = Tuning.PresetAdvisor.Recommend(Card, Estimate, flights, Connection);
        Fits = Tuning.PresetAdvisor.Assess(Card, Estimate, flights, Connection, QualityPreset.Offered);
    }
}
