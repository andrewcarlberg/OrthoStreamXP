using System.Globalization;
using System.Text.RegularExpressions;

namespace OrthoStreamXP.Textures;

/// <summary>
/// How each texture is built from the level-change setting. Each level downloads fixed zooms (<see cref="Zooms"/>):
/// one for the wide areas' textures (ZL16, and ZL17 in finer packages) and one for the airports' (ZL18 and finer):
/// Balanced 15/16, Sharp 16/17, Ultra 17/18, Extra 17/19, whatever the zoom in the package's file names.
/// <para>
/// The size is the detail the tiles hold at <see cref="TextureResolution.Full"/>: 4096 px when the download zoom is the
/// file's own (16 x 16 tiles), twice as much one zoom finer, half per zoom coarser. ZL16 textures: Balanced 2048, Sharp
/// 4096, Ultra and Extra 8192; ZL18 textures: Balanced 1024, Sharp 2048, Ultra 4096, Extra 8192; a ZL17/ZL19 package's
/// textures are half of those sizes, from the same imagery. A lower <see cref="Resolution"/> halves the textures from the
/// same tiles (Ultra at Medium: ZL16 4096 px from zoom 17, ZL18 still 4096 px).
/// </para>
/// </summary>
/// <param name="LevelChange">One of the <see cref="Zooms"/> levels: the presets' -2..1 (the settings accept only these), or -3/-4 (quick test builds).</param>
/// <param name="Limit">A size limit instead of the level's own (tests of the streaming engine at full detail).</param>
internal sealed partial record TexturePlan(int LevelChange, int? Limit = null)
{
    /// <summary>Levels finer than the file zoom; BuildAsync accepts up to +3.</summary>
    public const int MaxLevel = 3;

    /// <summary>
    /// The zooms each level downloads: (the areas' textures, below ZL18; the airports', ZL18 and finer). -3 and -4 are no
    /// preset and the settings refuse them: they are coarse, quick builds for the tests.
    /// </summary>
    private static readonly Dictionary<int, (int Area, int Airport)> Zooms = new()
    {
        [-4] = (13, 14),
        [-3] = (14, 15),
        [-2] = (15, 16), // Balanced
        [-1] = (16, 17), // Sharp
        [0] = (17, 18),  // Ultra
        [1] = (17, 19),  // Extra
    };

    public int LevelChange { get; } = Zooms.ContainsKey(LevelChange) ? LevelChange
        : throw new ArgumentOutOfRangeException(nameof(LevelChange), LevelChange, "No texture zooms for this level");

    /// <summary>Whether a level has its texture zooms (the presets' -2..1, and the tests' -3/-4).</summary>
    public static bool IsKnownLevel(int levelChange) => Zooms.ContainsKey(levelChange);

    /// <summary>How much the textures are shrunk from the detail their tiles hold (the preset's resolution setting).</summary>
    public TextureResolution Resolution { get; init; } = TextureResolution.Full;

    /// <summary>The largest texture built: <see cref="TextureSizing.MaxSize"/>, or the explicit limit (tests).</summary>
    public int SizeLimit => Limit ?? TextureSizing.MaxSize;

    /// <summary>The zooms finer (positive) or coarser than its own that a texture of <paramref name="fileZoom"/> is downloaded at.</summary>
    public int LevelFor(int fileZoom)
    {
        var (area, airport) = Zooms[LevelChange];
        return Math.Clamp((TextureResolution.IsAirport(fileZoom) ? airport : area) - fileZoom, -4, MaxLevel);
    }

    /// <summary>The zoom its tiles are downloaded at.</summary>
    public int FetchZoom(int fileZoom) => fileZoom + LevelFor(fileZoom);

    /// <summary>Its size in pixels: the downloaded detail shrunk by the resolution, capped by the size limit, never enlarged.</summary>
    public int SizeFor(int fileZoom) => Math.Min(Resolution.SizeOf(TextureSizing.DownloadedDetail(LevelFor(fileZoom)), fileZoom), SizeLimit);

    /// <summary>
    /// A texture's name in the cache: the row and column of its top-left tile at the zoom it is downloaded at, and that
    /// zoom, such as 87520_143440_BI18 → 43760_71720_PX17 at level -1. The package's code is left out: it never chose
    /// the imagery. Only ZL16 and ZL18 textures get such a name; the others keep their package name: they are downloaded at
    /// the zoom of ZL16 or ZL18 (ZL17 at the areas', ZL19 at the airports'), where a texture of a different width could
    /// start at the same tile.
    /// </summary>
    public string CacheStem(TextureName name)
    {
        if (name.Zoom is not (16 or 18)) return name.Stem;
        int level = LevelFor(name.Zoom);
        return FormattableString.Invariant($"{Shift(name.Row, level)}_{Shift(name.Col, level)}_{TextureName.CacheCode}{FetchZoom(name.Zoom)}");
    }

    /// <summary>The texture a cache name (<see cref="CacheStem"/>) stands for, with the cache code; null for other names.</summary>
    public TextureName? FromCacheStem(string stem)
    {
        Match m = CacheStemPattern().Match(stem);
        if (!m.Success
            || !int.TryParse(m.Groups[1].Value, CultureInfo.InvariantCulture, out int row)
            || !int.TryParse(m.Groups[2].Value, CultureInfo.InvariantCulture, out int col)
            || !int.TryParse(m.Groups[3].Value, CultureInfo.InvariantCulture, out int fetchZoom))
            return null;
        foreach (int zoom in PackageZooms)
        {
            if (FetchZoom(zoom) != fetchZoom) continue;
            int level = LevelFor(zoom);
            if (TextureName.TryCreate(Shift(row, -level), Shift(col, -level), TextureName.CacheCode, zoom, out TextureName name)
                && CacheStem(name) == stem)
                return name;
        }
        return null;
    }

    private static int Shift(int tile, int levels) => levels >= 0 ? tile << levels : tile >> -levels;

    [GeneratedRegex(@"^(\d+)_(\d+)_PX(\d+)$")]
    private static partial Regex CacheStemPattern();

    /// <summary>The size for a texture file name such as 21904_35824_BI16.dds, or null when it is not a photo tile.</summary>
    public int? SizeFor(string textureFileName) => TextureName.TryParseFile(textureFileName, out TextureName name) ? SizeFor(name.Zoom) : null;

    /// <summary>
    /// The cache subfolder for textures of this plan, such as Balanced_1024-1024 (the quality preset, or level-3 for a
    /// level no preset has, then the ZL16 and ZL18 sizes): settings that build the same textures share it, any other
    /// setting gets its own.
    /// </summary>
    public string FolderName => FormattableString.Invariant($"{Setup.QualityPreset.ForLevel(LevelChange)?.Name ?? $"level{LevelChange:+0;-0;0}"}_{SizeFor(16)}-{SizeFor(18)}");

    private static readonly int[] PackageZooms = [16, 18];

    /// <summary>One line for the log, e.g. "ZL16 → zoom 15, 1024 px; ZL18 → zoom 16, 1024 px".</summary>
    public string Describe() => string.Join("; ", PackageZooms.Select(z => $"ZL{z} → zoom {FetchZoom(z)}, {SizeFor(z)} px"));
}

internal static class TextureSizing
{
    /// <summary>The largest texture built: a ZL16 texture from zoom 17, or a ZL18 one from zoom 19, at full detail.</summary>
    public const int MaxSize = 8192;

    /// <summary>Pixels across a texture at its file's own zoom: 16 x 16 tiles of 256 px.</summary>
    public const int FileZoomDetail = 4096;

    /// <summary>
    /// Pixels of detail a level change downloads for one texture: 4096 at the file's zoom, twice as much per level
    /// finer, half per level coarser, up to <see cref="MaxSize"/>.
    /// </summary>
    public static int DownloadedDetail(int levelChange) =>
        levelChange >= 0 ? FileZoomDetail << Math.Min(levelChange, 1) : FileZoomDetail >> -levelChange;
}
