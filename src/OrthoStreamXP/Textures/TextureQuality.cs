using System.Globalization;
using System.Text.RegularExpressions;

namespace OrthoStreamXP.Textures;

/// <summary>
/// How each texture is built from the level-change setting. The zoom depends on the texture's own zoom: ZL18 textures (around
/// airports) use 18 + level, ZL16 textures one level finer up to level 0, 16 + (level + 1), so at the same setting the
/// wide ZL16 areas are not left much softer than the airports; above level 0 they use 16 + level (Extra +1: zoom 17,
/// as Ultra, while the airports get zoom 19). Other zooms use zoom + level.
/// <para>
/// The size is the detail the tiles hold at <see cref="TextureResolution.Full"/>: 4096 px at the file's own zoom (16 x
/// 16 tiles), twice as much per level finer, half per level coarser. ZL16 textures: Balanced 2048, Sharp 4096, Ultra
/// and Extra 8192; ZL18 textures: Balanced 1024, Sharp 2048, Ultra 4096, Extra 8192. A level beyond Extra's, whose
/// detail would pass <see cref="TextureSizing.MaxSize"/>, is shrunk to it. A lower <see cref="Resolution"/> halves the
/// textures from the same tiles (Ultra at Medium: ZL16 4096 px from zoom 17, ZL18 still 4096 px).
/// </para>
/// </summary>
/// <param name="Limit">A size limit instead of the level's own (tests of the streaming engine at full detail).</param>
internal sealed partial record TexturePlan(int LevelChange, int? Limit = null)
{
    /// <summary>Levels finer than the file zoom; BuildAsync accepts up to +3.</summary>
    public const int MaxLevel = 3;

    /// <summary>How much the textures are shrunk from the detail their tiles hold (the preset's resolution setting).</summary>
    public TextureResolution Resolution { get; init; } = TextureResolution.Full;

    /// <summary>The largest texture built: <see cref="TextureSizing.MaxSize"/>, or the explicit limit (tests).</summary>
    public int SizeLimit => Limit ?? TextureSizing.MaxSize;

    /// <summary>The level change applied to a texture of <paramref name="fileZoom"/>.</summary>
    public int LevelFor(int fileZoom) => Math.Clamp(LevelChange + (fileZoom == 16 && LevelChange < 1 ? 1 : 0), -4, MaxLevel);

    /// <summary>The zoom its tiles are downloaded at.</summary>
    public int FetchZoom(int fileZoom) => fileZoom + LevelFor(fileZoom);

    /// <summary>Its size in pixels: the downloaded detail shrunk by the resolution, capped by the size limit, never enlarged.</summary>
    public int SizeFor(int fileZoom) => Math.Min(Resolution.SizeOf(TextureSizing.DownloadedDetail(LevelFor(fileZoom)), fileZoom), SizeLimit);

    /// <summary>
    /// A texture's name in the cache: the row and column of its top-left tile at the zoom it is downloaded at, and that
    /// zoom, such as 87520_143440_BI18 → 43760_71720_PX17 at level -1. The package's code is left out: it never chose
    /// the imagery. ZL17 keeps its package name: it is downloaded at the zoom of ZL16 (one level finer), where a ZL16
    /// texture twice as wide could start at the same tile.
    /// </summary>
    public string CacheStem(TextureName name)
    {
        if (name.Zoom == 17) return name.Stem;
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
        for (int zoom = 4; zoom <= 22; zoom++)
        {
            if (zoom == 17 || FetchZoom(zoom) != fetchZoom) continue;
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
