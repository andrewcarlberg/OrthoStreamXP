using System.Collections.Concurrent;

namespace OrthoStreamXP.Textures;

/// <summary>
/// The colour adjustments of a photo texture as X-Plane reads it: the season's (<see cref="Seasons"/>), the contrast
/// (<see cref="Contrast"/>, always on) and the exposure (<see cref="Exposure"/>), in that order, as one lookup table
/// made from all of them at once, so a colour is rounded to 565 once. The Settings page's preview makes them the same
/// way, so it shows what X-Plane will.
/// </summary>
internal static class PhotoColours
{
    private static readonly ConcurrentDictionary<(int Season, int Exposure), ushort[]> Tables = new();

    /// <summary>
    /// The lookup table of a season and an exposure in percent, shared by every texture; with summer and the full
    /// exposure, the contrast alone.
    /// </summary>
    public static ushort[] TableFor(SeasonState season, double exposure)
    {
        var key = (season.IsSummer ? 0 : Seasons.StateKey(season), Exposure.Percent(exposure));
        if (Tables.TryGetValue(key, out ushort[]? table)) return table;
        if (Tables.Count > 1024) Tables.Clear();
        return Tables.GetOrAdd(key, k => Build(season.IsSummer ? null : Seasons.Quantised(season), k.Exposure / 100.0));
    }

    /// <summary>
    /// Adjusts the whole colour blocks in <paramref name="data"/>, which holds a DDS's bytes from
    /// <paramref name="fileOffset"/>, by <paramref name="table"/> (<see cref="TableFor"/>); blocks cut by either end stay as they are.
    /// </summary>
    /// <param name="blockBytes">8 for DXT1 (BC1), 16 for DXT3 and DXT5.</param>
    public static void Apply(Span<byte> data, long fileOffset, int blockBytes, ushort[]? table)
    {
        if (table is not null) ColourTable.Apply(data, fileOffset, blockBytes, table);
    }

    /// <summary>One colour (0-255 per channel) through the season (none in summer), the contrast and the exposure, unrounded.</summary>
    internal static (double R, double G, double B) Adjust(double r, double g, double b, SeasonState? season, double factor)
    {
        if (season is { } state) (r, g, b) = Seasons.Recolor(r, g, b, state);
        (r, g, b) = (Contrast.Apply(r), Contrast.Apply(g), Contrast.Apply(b));
        return factor < 1 ? (Exposure.Darken(r, factor), Exposure.Darken(g, factor), Exposure.Darken(b, factor)) : (r, g, b);
    }

    private static ushort[] Build(SeasonState? season, double factor)
    {
        var table = new ushort[65536];
        for (int c = 0; c < table.Length; c++)
        {
            var (r, g, b) = ColourTable.Decode565(c);
            (r, g, b) = Adjust(r, g, b, season, factor);
            table[c] = (ushort)ColourTable.Encode565(r, g, b);
        }
        return table;
    }
}
