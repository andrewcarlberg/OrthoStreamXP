using System.Text;
using OrthoStreamXP.Scenery;

namespace OrthoStreamXP.Setup;

/// <summary>
/// The scenery package OrthoStreamXP keeps its flattened airports in: Custom Scenery\OrthoStreamXP Flattened Airports, with an
/// apt.dat holding a copy of each airport plus "1302 flatten 1". X-Plane uses an airport's definition from the highest package
/// that lists it, so the package is kept above the airports' own ones in scenery_packs.ini (a new entry goes just above the first
/// airport's package, not above unrelated ones); their files are never changed.
/// Undoing takes the airport out again, and the package goes when its last airport does.
/// </summary>
internal sealed class FlattenedAirports(string xplane)
{
    public const string PackageName = "OrthoStreamXP Flattened Airports";
    public const string EntryPath = $"Custom Scenery/{PackageName}/";

    private string CustomScenery => Path.Join(xplane, "Custom Scenery");
    public string Folder => Path.Join(CustomScenery, PackageName);
    private string NavData => Path.Join(Folder, "Earth nav data");
    private string AptDat => Path.Join(NavData, "apt.dat");
    private SceneryPackList List => new(CustomScenery);

    /// <summary>The airports flattened so far.</summary>
    public IReadOnlyList<AirportBlock> Read() => File.Exists(AptDat) ? AirportBlock.Split(File.ReadAllText(AptDat)) : [];

    /// <summary>
    /// Adds (or replaces) <paramref name="airport"/>, and makes sure the package is listed, enabled, above
    /// <paramref name="sourceEntry"/> (the scenery_packs.ini entry of the package the airport comes from).
    /// </summary>
    public void Add(AirportBlock airport, string sourceEntry)
    {
        Write([.. Read().Where(b => !b.Ident.Equals(airport.Ident, StringComparison.OrdinalIgnoreCase)), airport]);
        PlaceAbove(sourceEntry);
    }

    /// <summary>Takes the airport out; the last one takes the package and its scenery_packs.ini entry with it. False when it was not there.</summary>
    public bool Remove(string ident)
    {
        IReadOnlyList<AirportBlock> airports = Read();
        AirportBlock[] kept = [.. airports.Where(b => !b.Ident.Equals(ident, StringComparison.OrdinalIgnoreCase))];
        if (kept.Length == airports.Count) return false;
        if (kept.Length > 0) Write(kept);
        else Delete();
        return true;
    }

    /// <summary>
    /// Whether X-Plane takes this package's airports over those of <paramref name="sourceEntry"/>: it is enabled and listed above
    /// it, or not listed yet (X-Plane lists a new package first).
    /// </summary>
    public bool IsAbove(string sourceEntry)
    {
        IReadOnlyList<SceneryPackEntry> entries = List.Read();
        int ours = IndexOf(entries, EntryPath);
        if (ours < 0) return true;
        if (!entries[ours].Enabled) return false;
        int source = IndexOf(entries, sourceEntry);
        return source < 0 || ours < source;
    }

    private void PlaceAbove(string sourceEntry)
    {
        SceneryPackList list = List;
        if (list.AddAbove(EntryPath, sourceEntry)) return;
        list.SetEnabled(EntryPath, enabled: true);
        IReadOnlyList<SceneryPackEntry> entries = list.Read();
        int ours = IndexOf(entries, EntryPath), source = IndexOf(entries, sourceEntry);
        if (ours >= 0 && source >= 0 && ours > source) list.MoveTo(EntryPath, source);
    }

    private void Write(IEnumerable<AirportBlock> airports)
    {
        var text = new StringBuilder("I\r\n1200 Version - airports flattened by OrthoStreamXP\r\n\r\n");
        foreach (AirportBlock airport in airports.OrderBy(a => a.Ident, StringComparer.OrdinalIgnoreCase))
            text.Append(airport.Text).Append("\r\n");
        text.Append("99\r\n");
        Directory.CreateDirectory(NavData);
        AtomicFile.Write(AptDat, new UTF8Encoding(false).GetBytes(text.ToString()));
    }

    /// <summary>Removes the package's own file and folders (anything else put there is left, with its folder) and its list entry.</summary>
    private void Delete()
    {
        File.Delete(AptDat);
        foreach (string folder in new[] { NavData, Folder })
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
        SceneryPacks.RemoveEntries(CustomScenery, [PackageName]);
    }

    private static int IndexOf(IReadOnlyList<SceneryPackEntry> entries, string path)
    {
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].IsAt(path)) return i;
        return -1;
    }
}
