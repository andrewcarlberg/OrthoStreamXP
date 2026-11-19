using System.Buffers.Binary;
using System.Globalization;
using OrthoStreamXP.Scenery;
using OrthoStreamXP.Setup;
using OrthoStreamXP.Textures;

namespace OrthoStreamXP.Gui;

/// <summary>The colour settings a preview shows, as the Settings page has them.</summary>
/// <param name="Exposure">In percent, <see cref="Textures.Exposure.Min"/> to 100.</param>
internal sealed record ColourAdjustments(SeasonOptions Seasons, double Exposure);

/// <summary>A photo texture of the cache, at its package name.</summary>
internal sealed record PreviewPhoto(string Path, TextureName Name);

/// <summary>One photo before and after the colour settings: RGB pictures <see cref="Size"/> pixels square, and what was done to it.</summary>
internal sealed record PreviewPicture(byte[] Before, byte[] After, int Size, string Place, IReadOnlyList<string> Notes);

/// <summary>
/// The colour settings' preview: a photo texture from the cache, adjusted the way X-Plane gets it (<see cref="PhotoColours"/>)
/// on the level nearest <see cref="PictureSize"/>. The season is that of the last flight's date at the photo's place
/// (or the one chosen), as the next flight will.
/// </summary>
internal sealed class ColourPreview
{
    /// <summary>The size of the pictures shown, in pixels.</summary>
    public const int PictureSize = 512;

    /// <summary>The most recently written photos offered.</summary>
    private const int MaxPhotos = 40;

    private readonly string _cache;
    private readonly string _history;

    /// <param name="cache">The cache folder.</param>
    /// <param name="history">The folder of the flights' history (the last X-Plane date).</param>
    public ColourPreview(string cache, string history)
    {
        _cache = cache;
        _history = history;
    }

    /// <summary>The photos to show, most recently made first; the finer ground zooms (ZL16 and up) before the coarse far scenery.</summary>
    public IReadOnlyList<PreviewPhoto> FindPhotos(CancellationToken cancel = default)
    {
        var found = new List<(PreviewPhoto Photo, DateTime Written)>();
        foreach (var (path, name) in CacheAreas.PhotoTextures(_cache))
        {
            cancel.ThrowIfCancellationRequested();
            found.Add((new PreviewPhoto(path, name), File.GetLastWriteTimeUtc(path)));
        }
        return [.. found.OrderByDescending(f => f.Photo.Name.Zoom >= 16).ThenByDescending(f => f.Written).Take(MaxPhotos).Select(f => f.Photo)];
    }

    /// <summary>The photo before and after <paramref name="settings"/>; null when it is not a whole DXT1 texture (or is being replaced).</summary>
    public PreviewPicture? Render(PreviewPhoto photo, ColourAdjustments settings)
    {
        using var file = File.OpenHandle(photo.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        byte[] header = new byte[Dds.HeaderSize];
        if (RandomAccess.Read(file, header, 0) < header.Length || !header.AsSpan(0, 4).SequenceEqual("DDS "u8)
            || ColourTable.BlockBytes(header.AsSpan(84, 4)) != 8) return null;
        int height = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(12)), size = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(16));
        if (size != height || size < 4 || (size & (size - 1)) != 0) return null;
        var layout = DdsLayout.For(size);
        if (RandomAccess.GetLength(file) < layout.Length) return null;
        int mip = Math.Clamp((int)Math.Round(Math.Log2((double)size / PictureSize)), 0, layout.Levels - 1), levelSize = layout.SizeOf(mip);
        byte[] blocks = new byte[layout.LengthOf(mip)];
        if (RandomAccess.Read(file, blocks, layout.Offset(mip)) < blocks.Length) return null;
        byte[] before = Dds.DecodeLevel(blocks, levelSize, levelSize);

        var notes = new List<string>();
        // As the mount: one table for the season, the contrast (always on) and the exposure.
        ushort[] withoutSeason = PhotoColours.TableFor(SeasonState.Summer, settings.Exposure);
        ushort[] table = settings.Seasons.On ? Season(photo.Name, settings.Seasons, settings.Exposure, withoutSeason, notes) : withoutSeason;
        if (Exposure.Percent(settings.Exposure) < Exposure.Full) notes.Add(FormattableString.Invariant($"Exposure {settings.Exposure:0} %"));
        notes.Add(FormattableString.Invariant($"Contrast {Contrast.Strength * 100:0} % (always on)"));

        PhotoColours.Apply(blocks, layout.Offset(mip), 8, table);
        return new PreviewPicture(before, Dds.DecodeLevel(blocks, levelSize, levelSize), levelSize, Place(photo.Name), notes);
    }

    /// <summary>The texture's table for its season and <paramref name="exposurePercent"/> (<paramref name="withoutSeason"/>: the table of summer).</summary>
    private ushort[] Season(TextureName name, SeasonOptions options, double exposurePercent, ushort[] withoutSeason, List<string> notes)
    {
        string dayFile = Path.Join(_history, SeasonClock.DayFileName);
        // The satellite record as the flights kept it: nothing is downloaded.
        using ObservedSeasons? observed = options.Choice == SeasonChoice.Satellite
            ? new ObservedSeasons(new VegetationSource(Path.Join(_cache, TileStore.FolderName, VegetationSource.FolderName), TimeProvider.System, download: false), TimeSpan.FromSeconds(5))
            : null;
        var clock = new SeasonClock(options, dayFile, observed, exposurePercent);
        string when = options.FollowsDate
            ? $"Season of {SeasonClock.Describe(clock.Day)} ({(File.Exists(dayFile) ? "the last flight's date" : "today")}) here"
            : $"Season: {SeasonModel.Names.First(n => n.Value == options.Choice).Key.Replace('-', ' ')}";
        if (observed is not null)
        {
            var (lat, lon) = name.Center;
            notes.Add(observed.YearAt(lat, lon) is { } year
                ? $"Satellite record here on {SeasonClock.Describe(clock.Day)}: {ObservedSeason.Describe(year, clock.Day)}"
                : "No satellite record of this place yet (it is downloaded on the first flight there): the calendar's season");
        }
        ushort[] table = clock.TableFor(name);
        // Where the season changes nothing, the table is summer's (PhotoColours.TableFor).
        bool seasonal = !ReferenceEquals(table, withoutSeason);
        notes.Add(seasonal ? FormattableString.Invariant($"{when}, {options.Strength * 100:0} %") : $"{when}: the photo as it is");
        return table;
    }

    private static string Place(TextureName name)
    {
        var (lat, lon) = name.Center;
        return string.Create(CultureInfo.InvariantCulture,
            $"{Math.Abs(lat):0.00}° {(lat >= 0 ? 'N' : 'S')}, {Math.Abs(lon):0.00}° {(lon >= 0 ? 'E' : 'W')} (ZL{name.Zoom})");
    }
}
