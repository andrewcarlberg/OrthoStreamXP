using System.Globalization;
using OrthoStreamXP.Textures;

namespace OrthoStreamXP.Setup;

/// <summary>
/// A ready-made speed and sharpness choice: the level change and every setting the window does not show, as the test
/// flights tuned them for it (the same values as tests\OrthoStreamXP.Tests\examples\orthostreamxp-&lt;level&gt;.ini). The connection's own settings
/// (download-workers, timeout, attempts) and the paging model are not part of a preset, so choosing one keeps them.
/// The flight tuning adjusts some of these values for the PC and connection (<see cref="Tuning.TunedSettings"/>).
/// </summary>
internal sealed record QualityPreset(string Name, int Level, string Summary, string BestFor, string FirstVisit,
    IReadOnlyDictionary<string, string?> Settings)
{
    public static readonly QualityPreset Balanced = new("Balanced", -2,
        "2048 px textures from zoom 15 imagery, 1024 px from zoom 16 near airports: light on graphics memory and the connection, fine from higher up.",
        "High flying, a slower connection or little graphics memory", "about 3 min",
        Values(buildWorkers: 16, fileSystemThreads: 64, provisionalLod: 0, provisionalWait: null, radiusKm: 6, textures: 16, buildMemoryMb: 512, lookahead: 180));

    public static readonly QualityPreset Sharp = new("Sharp", -1,
        "4096 px textures from zoom 16 imagery, 2048 px from zoom 17 near airports: twice as sharp as Balanced.",
        "Everyday flying, approaches included", "about 3 min",
        Values(buildWorkers: 8, fileSystemThreads: 96, provisionalLod: 2, provisionalWait: 5, radiusKm: 5, textures: 8, buildMemoryMb: 512));

    public static readonly QualityPreset Ultra = new("Ultra", 0,
        "8192 px textures from zoom 17 imagery, 4096 px from zoom 18 near airports: twice as sharp as Sharp.",
        "Low flight and GA, with plenty of graphics memory", "about 3 min",
        Values(buildWorkers: 8, fileSystemThreads: 96, provisionalLod: 3, provisionalWait: 3, radiusKm: 4, textures: 6, buildMemoryMb: 1024));

    // Level +1: ZL18 airports at zoom 19 as 8192 px textures, ZL16 areas at zoom 17 as Ultra's. The finer, newer imagery
    // of zoom 19 often avoids the clouds and other artifacts of the coarser ones. A ZL18 texture downloads 1024 tiles,
    // four times Ultra's; a ZL16 texture the same 1024 as Ultra's.
    public static readonly QualityPreset Extra = new("Extra", 1,
        "8192 px textures everywhere: Ultra's, and the finest imagery (zoom 19) near airports, twice as sharp as Ultra's there and often without its clouds and other artifacts: four times Ultra's downloads there.",
        "Low and slow flight and screenshots, on a fast connection", "about 4 min, then sharp next to the aircraft",
        Values(buildWorkers: 8, fileSystemThreads: 96, provisionalLod: 3, provisionalWait: 3, radiusKm: 4, textures: 6, buildMemoryMb: 1024, lookahead: 90));

    // Extra near the ground, Sharp from 5,000 ft and Balanced from 25,000 ft above it: Extra's level and cache, with the
    // detail held back high up (dynamic-quality). Measured on a day flight: full-detail textures were 82 % of the
    // downloads at every height, and Sharp's detail above 5,000 ft would have saved about half of the climb and cruise.
    public static readonly QualityPreset Dynamic = DynamicFrom(Extra,
        "Extra near the ground, Sharp from 5,000 ft and Balanced from 25,000 ft above it: full detail where it can be seen, about half the downloads in a climb and cruise.",
        "Climbs, cruise and approaches in one flight, on a fast connection");

    // Ultra near the ground: its ZL16 areas are Extra's (zoom 17), the airports zoom 18 instead of 19, so a quarter of
    // Extra's downloads there; the same steps above.
    public static readonly QualityPreset DynamicUltra = DynamicFrom(Ultra,
        "Ultra near the ground, Sharp from 5,000 ft and Balanced from 25,000 ft above it: Ultra's detail where it can be seen, far fewer downloads in a climb and cruise.",
        "Climbs, cruise and approaches in one flight, on a good connection");

    // Sharp near the ground: Sharp's own step is no saving, so only Balanced's is left, from 25,000 ft.
    public static readonly QualityPreset DynamicSharp = DynamicFrom(Sharp,
        "Sharp near the ground and through the climb, Balanced from 25,000 ft above it: fewer downloads at airliner heights.",
        "Long flights at airliner heights, on an everyday connection");

    /// <summary>The presets by their level change: Dynamic is its base's level (its cache, tuning and texture size), so it is not among them.</summary>
    public static IReadOnlyList<QualityPreset> All { get; } = [Balanced, Sharp, Ultra, Extra];

    /// <summary>The presets offered in the window: every level, and Dynamic (one card; its base is chosen on it).</summary>
    public static IReadOnlyList<QualityPreset> Choices { get; } = [.. All, Dynamic];

    /// <summary>Dynamic from each base it can have, the sharpest first: Extra (the default), Ultra and Sharp.</summary>
    public static IReadOnlyList<QualityPreset> DynamicChoices { get; } = [Dynamic, DynamicUltra, DynamicSharp];

    /// <summary>Every preset the window can be at: every level, and Dynamic from each base.</summary>
    public static IReadOnlyList<QualityPreset> Offered { get; } = [.. All, .. DynamicChoices];

    /// <summary>For Dynamic, the preset it is near the ground (its level, settings and cache); null for the others.</summary>
    public QualityPreset? DynamicBase { get; private init; }

    public bool IsDynamic => DynamicBase is not null;

    /// <summary>Its name in the window's words: "Ultra", or "Dynamic from Ultra".</summary>
    public string Title => DynamicBase is { } near ? $"{Name} from {near.Name}" : Name;

    /// <summary>
    /// Dynamic's steps at the default heights, lowest first: the presets it holds the detail to higher up (Sharp from
    /// 5,000 ft, Balanced from 25,000 ft), those coarser than its base; empty for the other presets.
    /// </summary>
    public IReadOnlyList<Scenery.DetailTier> DynamicSteps =>
        IsDynamic ? new Scenery.DynamicOptions().TiersBelow(new TexturePlan(Level)) : [];

    /// <summary>The preset with this level change, if one has it (Extra for Dynamic's default level).</summary>
    public static QualityPreset? ForLevel(int? level) => All.FirstOrDefault(p => p.Level == level);

    /// <summary>
    /// The preset the settings are at: Dynamic from the level's preset when dynamic-quality is on at a level Dynamic
    /// can have, otherwise the level's.
    /// </summary>
    public static QualityPreset? Current(int? level, bool dynamicQuality) =>
        (dynamicQuality ? DynamicChoices.FirstOrDefault(p => p.Level == level) : null) ?? ForLevel(level);

    /// <summary>Dynamic from <paramref name="near"/>, or null when Dynamic cannot have that base.</summary>
    public static QualityPreset? DynamicOn(QualityPreset? near) => DynamicChoices.FirstOrDefault(p => p.DynamicBase == near);

    /// <summary>
    /// The Dynamic the settings have: the current one when the settings are at Dynamic, otherwise the base kept in
    /// dynamic-base (Extra when there is none).
    /// </summary>
    public static QualityPreset DynamicIn(SettingsDocument settings) =>
        Current(settings.GetInt("level-change"), settings.GetBool(DynamicQualityKey) == true) is { IsDynamic: true } current ? current
            : DynamicOn(All.FirstOrDefault(p => string.Equals(p.Name, settings.Get(DynamicBaseKey)?.Trim(), StringComparison.OrdinalIgnoreCase))) ?? Dynamic;

    public string? Get(string name) => Settings.TryGetValue(name, out string? value) ? value : null;

    /// <summary>Zoom of the common ZL16 textures at this preset (the ones that cover most of the ground).</summary>
    public int Zl16Zoom => new TexturePlan(Level).FetchZoom(16);

    /// <summary>Zoom of the ZL18 textures at this preset (the ones the packages have near airports).</summary>
    public int Zl18Zoom => new TexturePlan(Level).FetchZoom(18);

    /// <summary>Texture size in the ZL16 areas at full resolution (the size follows the level).</summary>
    public int Size => SizeAt(TextureResolution.Full);

    /// <summary>Texture size in the ZL16 areas at <paramref name="resolution"/>.</summary>
    public int SizeAt(TextureResolution resolution) => PlanAt(resolution).SizeFor(16);

    /// <summary>The textures this preset builds at <paramref name="resolution"/>.</summary>
    public TexturePlan PlanAt(TextureResolution resolution) => new(Level) { Resolution = resolution };

    /// <summary>Ground size of one texture pixel in ZL16 areas at mid latitudes (45°) at full resolution: a ZL16 texture is about 6.9 km across.</summary>
    public double MetresPerPixel => MetresPerPixelAt(TextureResolution.Full);

    public double MetresPerPixelAt(TextureResolution resolution) =>
        2 * Math.PI * 6_378_137.0 * Math.Cos(Math.PI / 4) * Tiles.TextureSpan / (1 << 16) / SizeAt(resolution);

    /// <summary>The setting of this preset's resolution (resolution-ultra = full, medium or low), kept for each preset.</summary>
    public string ResolutionKey => ResolutionPrefix + Name.ToLowerInvariant();

    public const string ResolutionPrefix = "resolution-";

    /// <summary>This preset's resolution in the settings: Full when not set, or not one of the names.</summary>
    public TextureResolution ResolutionIn(SettingsDocument settings) => TextureResolution.Parse(settings.Get(ResolutionKey)) ?? TextureResolution.Full;

    /// <summary>The textures the settings build: the level, at the current preset's resolution (Full for a level no preset has).</summary>
    public static TexturePlan PlanIn(SettingsDocument settings)
    {
        int level = settings.GetInt("level-change") ?? 0;
        TextureResolution resolution = Current(level, settings.GetBool(DynamicQualityKey) == true)?.ResolutionIn(settings) ?? TextureResolution.Full;
        return new TexturePlan(level) { Resolution = resolution };
    }

    /// <summary>Writes the preset's values (and its level) into the settings.</summary>
    public void ApplyTo(SettingsDocument settings)
    {
        settings.Set("level-change", Level);
        // Dynamic quality is Dynamic's alone: any other preset turns it off (an older settings file has no line for it).
        if (!Settings.ContainsKey(DynamicQualityKey)) settings.Set(DynamicQualityKey, "off");
        foreach (var (name, value) in Settings) settings.Set(name, value);
        if (DynamicBase is { } near) settings.Set(DynamicBaseKey, near.Name.ToLowerInvariant());
    }

    /// <summary>The setting that makes Extra's, Ultra's or Sharp's level <see cref="Dynamic"/>.</summary>
    public const string DynamicQualityKey = "dynamic-quality";

    /// <summary>The base Dynamic is kept at in the window (extra, ultra or sharp) while another preset is chosen; level-change sets it.</summary>
    public const string DynamicBaseKey = "dynamic-base";

    private static QualityPreset DynamicFrom(QualityPreset near, string summary, string bestFor) =>
        new("Dynamic", near.Level, summary, bestFor, near.FirstVisit,
            new Dictionary<string, string?>(near.Settings) { [DynamicQualityKey] = "on" }) { DynamicBase = near };

    private static Dictionary<string, string?> Values(int buildWorkers, int fileSystemThreads, int provisionalLod,
        double? provisionalWait, double radiusKm, int textures, int buildMemoryMb, double lookahead = MountSettings.DefaultLookaheadSeconds) => new()
    {
        ["dds-build-workers"] = Text(buildWorkers),
        ["encoder-workers"] = "4",
        ["filesystem-threads"] = Text(fileSystemThreads),
        ["provisional-lod"] = Text(provisionalLod),
        ["provisional-wait"] = provisionalWait is { } wait ? Text(wait) : null,
        ["prefetch-radius-km"] = Text(radiusKm),
        ["prefetch-lookahead-seconds"] = Text(lookahead),
        ["prefetch-textures"] = Text(textures),
        ["mip-streaming"] = "on",
        ["near-wait"] = Text(MountSettings.DefaultNearWait),
        ["max-wait"] = Text(MountSettings.DefaultMaxWait),
        ["build-memory"] = Text(buildMemoryMb),
        ["build-tiles"] = Text(Scenery.TextureStreamer.DefaultBuildTiles),
        ["prefetch-loaded-scenery"] = "on",
        ["track-aircraft"] = "true",
        ["lazy-terrain-listing"] = "true",
    };

    private static string Text(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
