namespace OrthoStreamXP.Textures;

/// <summary>
/// How much a preset's textures are shrunk from the detail their tiles hold: Full keeps it; Medium halves the textures of
/// the wide ZL16 areas and keeps the airports' ZL18 ones; Low halves them all (a quarter of the graphics memory). The
/// imagery is downloaded at the same zoom, so a shrunk texture is the same picture with less detail, not coarser imagery.
/// Only halving: X-Plane refuses a DDS whose size is not a power of two ("is DDS but its size is not power of 2").
/// </summary>
/// <param name="Value">The name in the settings file (resolution-&lt;preset&gt; = full, medium or low).</param>
/// <param name="HalvesAreas">The textures of the wide ZL16 (and coarser) areas are halved.</param>
/// <param name="HalvesAirports">The ZL18 (airport) textures are halved too, not only the ZL16 areas'.</param>
internal sealed record TextureResolution(string Value, string Label, bool HalvesAreas, bool HalvesAirports)
{
    public static readonly TextureResolution Full = new("full", "Full", HalvesAreas: false, HalvesAirports: false);
    public static readonly TextureResolution Medium = new("medium", "Medium", HalvesAreas: true, HalvesAirports: false);
    public static readonly TextureResolution Low = new("low", "Low", HalvesAreas: true, HalvesAirports: true);

    public static IReadOnlyList<TextureResolution> All { get; } = [Full, Medium, Low];

    public bool IsFull => !HalvesAreas && !HalvesAirports;

    /// <summary>The resolution a settings value names (any case), or null for none.</summary>
    public static TextureResolution? Parse(string? value) =>
        All.FirstOrDefault(r => string.Equals(r.Value, value?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The size of a texture of <paramref name="fileZoom"/> whose tiles hold <paramref name="fullSize"/> pixels across:
    /// half of it where this resolution halves that zoom (ZL18 and finer are the airports'), never below 4 px.
    /// </summary>
    public int SizeOf(int fullSize, int fileZoom) =>
        (IsAirport(fileZoom) ? HalvesAirports : HalvesAreas) ? Math.Max(4, fullSize / 2) : fullSize;

    /// <summary>Textures of this zoom are the airports' (ZL18 and finer), not the wide areas' (ZL16).</summary>
    public static bool IsAirport(int fileZoom) => fileZoom >= 18;
}
