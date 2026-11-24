namespace OrthoStreamXP.Setup;

/// <summary>
/// The flattened airports' 3D (buildings, ground polygons, roads) in the flattened airports' package. X-Plane draws a gateway
/// airport's 3D only when the airport comes from the package the 3D is in, so it is copied out of the airport's own package's
/// DSF tiles into tiles of the same name here (see <see cref="AirportDsfMerger"/>). buildings.txt lists, per airport, the
/// tiles it was copied from ("LFBD", tab, path; the ident alone when there were none), so a tile shared by several airports is
/// made again from what is left when one of them goes.
/// </summary>
internal sealed class FlattenedBuildings(string package)
{
    private string ListFile => Path.Join(package, "buildings.txt");
    private string NavData => Path.Join(package, "Earth nav data");

    /// <summary>Whether the airport's 3D was looked for when it was flattened (copies older than this have none).</summary>
    public bool Knows(string ident) => Read().Any(e => e.Ident.Equals(ident, StringComparison.OrdinalIgnoreCase));

    /// <summary>Copies <paramref name="ident"/>'s 3D from <paramref name="sources"/> (DSF files), replacing what it had.</summary>
    public void Set(string ident, IReadOnlyList<string> sources)
    {
        List<(string Ident, string? Source)> entries = Read();
        HashSet<string> tiles = Tiles(entries, ident);
        entries.RemoveAll(e => e.Ident.Equals(ident, StringComparison.OrdinalIgnoreCase));
        if (sources.Count == 0) entries.Add((ident, null));
        foreach (string source in sources)
        {
            entries.Add((ident, source));
            tiles.Add(TileOf(source));
        }
        Save(entries, tiles);
    }

    /// <summary>Takes <paramref name="ident"/>'s 3D out.</summary>
    public void Remove(string ident)
    {
        List<(string Ident, string? Source)> entries = Read();
        HashSet<string> tiles = Tiles(entries, ident);
        if (entries.RemoveAll(e => e.Ident.Equals(ident, StringComparison.OrdinalIgnoreCase)) > 0) Save(entries, tiles);
    }

    /// <summary>Removes every tile and the list.</summary>
    public void Delete()
    {
        List<(string Ident, string? Source)> entries = Read();
        foreach (string tile in entries.Where(e => e.Source is not null).Select(e => TileOf(e.Source!)).Distinct(StringComparer.OrdinalIgnoreCase))
            DeleteTile(tile);
        File.Delete(ListFile);
    }

    private void Save(List<(string Ident, string? Source)> entries, HashSet<string> tiles)
    {
        foreach (string tile in tiles) Make(tile, entries.Where(e => e.Source is not null && TileOf(e.Source).Equals(tile, StringComparison.OrdinalIgnoreCase)));
        Directory.CreateDirectory(package);
        AtomicFile.Write(ListFile, System.Text.Encoding.UTF8.GetBytes(string.Concat(entries.Select(e => e.Source is null ? $"{e.Ident}\r\n" : $"{e.Ident}\t{e.Source}\r\n"))));
    }

    /// <summary>Makes the tile again from the airports copied into it; without any, it goes.</summary>
    private void Make(string tile, IEnumerable<(string Ident, string? Source)> entries)
    {
        var merger = new AirportDsfMerger();
        foreach (var group in entries.GroupBy(e => e.Source!, StringComparer.OrdinalIgnoreCase))
        {
            var idents = group.Select(e => e.Ident).ToHashSet(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!merger.Add(DsfFile.Parse(File.ReadAllBytes(group.Key)), idents))
                    Log.Warning($"{group.Key} no longer has the buildings of {string.Join(", ", idents)}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Log.Warning($"Could not copy the buildings of {string.Join(", ", idents)} from {group.Key}: {ex.Message}");
            }
        }
        if (merger.IsEmpty)
        {
            DeleteTile(tile);
            return;
        }
        string path = Path.Join(NavData, tile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.Write(path, merger.Build().ToBytes());
    }

    private void DeleteTile(string tile)
    {
        string path = Path.Join(NavData, tile);
        File.Delete(path);
        string folder = Path.GetDirectoryName(path)!;
        if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
    }

    /// <summary>The tiles <paramref name="ident"/> has 3D in now.</summary>
    private static HashSet<string> Tiles(List<(string Ident, string? Source)> entries, string ident) =>
        entries.Where(e => e.Source is not null && e.Ident.Equals(ident, StringComparison.OrdinalIgnoreCase))
            .Select(e => TileOf(e.Source!)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>"+40-010\+44-001.dsf" of a source tile's path.</summary>
    private static string TileOf(string source) => Path.Join(Path.GetFileName(Path.GetDirectoryName(source)), Path.GetFileName(source));

    private List<(string Ident, string? Source)> Read()
    {
        if (!File.Exists(ListFile)) return [];
        var entries = new List<(string, string?)>();
        foreach (string line in File.ReadAllLines(ListFile))
        {
            string[] parts = line.Split('\t', 2, StringSplitOptions.TrimEntries);
            if (parts[0].Length > 0) entries.Add((parts[0], parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null));
        }
        return entries;
    }
}
