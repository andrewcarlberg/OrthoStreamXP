using OrthoStreamXP.Scenery;

namespace OrthoStreamXP.Setup;

/// <summary>A scenery package with airports: its scenery_packs.ini entry (as written there) and its apt.dat.</summary>
internal sealed record AirportSource(string Name, string EntryPath, string AptDat);

/// <summary>
/// The apt.dat files X-Plane takes its airports from, highest priority first: the enabled entries of Custom Scenery\scenery_packs.ini
/// that have one, with *GLOBAL_AIRPORTS* standing for Global Scenery\Global Airports. X-Plane uses an airport's definition from the
/// first of them that lists it. The package of the flattened airports is left out: it is what this is compared with.
/// </summary>
internal sealed class AirportSources(string xplane)
{
    public const string GlobalAirportsEntry = "*GLOBAL_AIRPORTS*";

    public IReadOnlyList<AirportSource> Find()
    {
        var sources = new List<AirportSource>();
        bool globalListed = false;
        foreach (SceneryPackEntry entry in new SceneryPackList(Path.Join(xplane, "Custom Scenery")).Read())
        {
            bool global = entry.IsAt(GlobalAirportsEntry);
            globalListed |= global;
            if (!entry.Enabled || entry.IsAt(FlattenedAirports.EntryPath)) continue;
            string folder = global ? GlobalAirportsFolder : Path.IsPathRooted(entry.Path) ? entry.Path : Path.Join(xplane, entry.Path);
            Add(sources, global ? "Global Airports" : entry.Name, entry.Path, folder);
        }
        // X-Plane loads its own airports below the custom scenery when the list does not place them.
        if (!globalListed) Add(sources, "Global Airports", GlobalAirportsEntry, GlobalAirportsFolder);
        return sources;
    }

    private string GlobalAirportsFolder => Path.Join(xplane, "Global Scenery", "Global Airports");

    private static void Add(List<AirportSource> sources, string name, string entryPath, string folder)
    {
        string aptDat = Path.Join(folder, "Earth nav data", "apt.dat");
        if (File.Exists(aptDat)) sources.Add(new AirportSource(name, entryPath, aptDat));
    }
}
