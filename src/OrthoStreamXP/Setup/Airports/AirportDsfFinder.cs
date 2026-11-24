using System.Globalization;

namespace OrthoStreamXP.Setup;

/// <summary>
/// Finds the DSF tiles of a package that hold an airport's 3D in a filter of its own (see <see cref="AirportDsfMerger"/>): the
/// tiles under the airport and a few kilometres around it, in the package's Earth nav data folder.
/// </summary>
internal static class AirportDsfFinder
{
    /// <summary>How far past its pavement an airport's buildings are looked for, in degrees.</summary>
    private const double Margin = 0.05;

    /// <summary>The DSF files in <paramref name="earthNavData"/> with a filter for <paramref name="airport"/>.</summary>
    public static IReadOnlyList<string> Find(string earthNavData, AirportBlock airport)
    {
        if (airport.Area() is not var (south, west, north, east)) return [];
        var found = new List<string>();
        for (int lat = Floor(south - Margin); lat <= Floor(north + Margin); lat++)
            for (int lon = Math.Max(-180, Floor(west - Margin)); lon <= Math.Min(179, Floor(east + Margin)); lon++)
            {
                string path = Path.Join(earthNavData, TilePath(lat, lon));
                if (File.Exists(path) && HasFilter(path, airport.Ident)) found.Add(path);
            }
        return found;
    }

    /// <summary>"+40-010\+44-001.dsf": the ten-degree folder and the one-degree tile.</summary>
    public static string TilePath(int lat, int lon) =>
        Path.Join(Name(Floor(lat / 10.0) * 10, Floor(lon / 10.0) * 10), Name(lat, lon) + ".dsf");

    private static string Name(int lat, int lon) =>
        lat.ToString("+00;-00", CultureInfo.InvariantCulture) + lon.ToString("+000;-000", CultureInfo.InvariantCulture);

    private static int Floor(double degrees) => (int)Math.Floor(degrees);

    private static bool HasFilter(string path, string ident)
    {
        try
        {
            return DsfFile.ReadProperties(path).Any(p => p.Key == AirportDsfMerger.FilterProperty && p.Value.Equals(ident, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Log.Warning($"Could not look for {ident}'s buildings in {path}: {ex.Message}");
            return false;
        }
    }
}
