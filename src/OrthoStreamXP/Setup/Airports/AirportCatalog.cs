namespace OrthoStreamXP.Setup;

/// <summary>An airport and the package X-Plane takes it from.</summary>
internal sealed record SourcedAirport(AirportRecord Record, AirportSource Source);

/// <summary>
/// The airports X-Plane uses: each ident from the first source that lists it. A file is read again only when its size or time
/// changed, so a refresh after flattening does not read Global Airports' 400 MB again.
/// </summary>
internal sealed class AirportCatalog
{
    private readonly Dictionary<string, (long Length, DateTime Written, IReadOnlyList<AirportRecord> Airports)> _files =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    public IReadOnlyDictionary<string, SourcedAirport> Load(IReadOnlyList<AirportSource> sources, CancellationToken cancel)
    {
        var airports = new Dictionary<string, SourcedAirport>(StringComparer.OrdinalIgnoreCase);
        foreach (AirportSource source in sources)
            foreach (AirportRecord airport in Airports(source.AptDat, cancel))
                airports.TryAdd(airport.Ident, new SourcedAirport(airport, source));
        return airports;
    }

    private IReadOnlyList<AirportRecord> Airports(string aptDat, CancellationToken cancel)
    {
        var file = new FileInfo(aptDat);
        lock (_lock)
        {
            if (_files.TryGetValue(aptDat, out var known) && known.Length == file.Length && known.Written == file.LastWriteTimeUtc)
                return known.Airports;
        }
        IReadOnlyList<AirportRecord> airports = AptDatReader.Read(aptDat, cancel);
        lock (_lock) _files[aptDat] = (file.Length, file.LastWriteTimeUtc, airports);
        return airports;
    }
}
