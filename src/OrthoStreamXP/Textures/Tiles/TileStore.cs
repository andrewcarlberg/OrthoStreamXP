using System.Globalization;

namespace OrthoStreamXP.Textures;

/// <summary>
/// Source tiles kept on disk for good: the low-zoom base (zoom 13 and coarser). Four zoom 13 tiles cover a whole
/// ZL16 texture, so far scenery is drawn from here at once, without a download, on every later flight. The base
/// is shared by every setup and package in the cache, and deleting a setup's textures leaves it in place.
/// Tiles are kept as the provider sent them (JPEG or PNG), about 10-30 KB each.
/// </summary>
/// <remarks>
/// Not zoom 12: Bing's zoom 12 and coarser are other imagery, lighter and yellower than its zoom 13 and finer (mean
/// colour 31 steps off zoom 16 at 20 places, up to 110 in the Alps; zoom 13 is 2 off), so far levels and the colour
/// matching's target made from it did not match the textures' detailed levels. Esri's zooms agree throughout.
/// </remarks>
internal sealed class TileStore(string root, int maxZoom = TileStore.DefaultMaxZoom)
{
    public const int DefaultMaxZoom = 13;
    /// <summary>The folder in the cache that holds the base.</summary>
    public const string FolderName = ".orthostreamxp-base";

    public string Root { get; } = Path.GetFullPath(root);
    public int MaxZoom { get; } = maxZoom;

    public bool Covers(int zoom) => zoom <= MaxZoom;

    private string PathOf(int x, int y, int zoom) => Path.Join(Root,
        zoom.ToString(CultureInfo.InvariantCulture), x.ToString(CultureInfo.InvariantCulture), y.ToString(CultureInfo.InvariantCulture) + ".tile");

    public bool Contains(int x, int y, int zoom) => Covers(zoom) && File.Exists(PathOf(x, y, zoom));

    /// <summary>The tile as the provider sent it, or null when it is not stored.</summary>
    public byte[]? TryRead(int x, int y, int zoom)
    {
        if (!Covers(zoom)) return null;
        string path = PathOf(x, y, zoom);
        if (!File.Exists(path)) return null; // the common case for coarse stand-in lookups: no exception
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning($"Could not read the stored tile {zoom}/{x}/{y}: {ex.Message}");
            return null;
        }
    }

    public void Save(int x, int y, int zoom, byte[] encoded)
    {
        if (!Covers(zoom)) return;
        try
        {
            AtomicFile.Write(PathOf(x, y, zoom), encoded);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning($"Could not store the tile {zoom}/{x}/{y} in the low-zoom base: {ex.Message}");
        }
    }
}
