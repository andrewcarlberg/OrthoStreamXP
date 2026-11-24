namespace OrthoStreamXP.Setup;

/// <summary>Where an airport stands on flattening.</summary>
internal enum FlattenState
{
    /// <summary>X-Plane follows the terrain under it.</summary>
    NotFlattened,
    /// <summary>Its own scenery already asks X-Plane to flatten it: nothing to do.</summary>
    FlattenedByScenery,
    /// <summary>Flattened by OrthoStreamXP; can be undone.</summary>
    Flattened,
    /// <summary>
    /// Flattened by OrthoStreamXP, but its scenery changed since (the copy holds the old layout), or the copy was made without the
    /// airport's buildings (before OrthoStreamXP copied them).
    /// </summary>
    Outdated,
}

/// <summary>
/// An airport on the Airports page. <see cref="Source"/> is where X-Plane takes it from without OrthoStreamXP (null when that
/// package is gone and only the flattened copy is left); <see cref="InEffect"/> is false when the flattened copy is listed
/// below that package or switched off in scenery_packs.ini, so X-Plane does not use it.
/// </summary>
internal sealed record FlattenableAirport(string Ident, string Name, string? Place, SourcedAirport? Source, FlattenState State, bool InEffect)
{
    public bool IsFlattenedHere => State is FlattenState.Flattened or FlattenState.Outdated;
}

/// <summary>
/// Flattens one airport at a time, and undoes it: see <see cref="FlattenedAirports"/> for how. The airports come from the
/// apt.dat files of the enabled packages in X-Plane's scenery_packs.ini (see <see cref="AirportSources"/>).
/// </summary>
internal sealed class AirportFlattening(string xplane, AirportCatalog catalog)
{
    private readonly FlattenedAirports _package = new(xplane);

    /// <summary>The package flattened airports are kept in.</summary>
    public string PackageFolder => _package.Folder;

    /// <summary>Every airport X-Plane knows, with its state; reads the apt.dat files that changed since the last call.</summary>
    public IReadOnlyList<FlattenableAirport> Load(CancellationToken cancel)
    {
        IReadOnlyDictionary<string, SourcedAirport> sourced = catalog.Load(new AirportSources(xplane).Find(), cancel);
        var flattened = new Dictionary<string, AirportBlock>(StringComparer.OrdinalIgnoreCase);
        foreach (AirportBlock block in _package.Read()) flattened[block.Ident] = block;

        var airports = new List<FlattenableAirport>(sourced.Count + flattened.Count);
        foreach (SourcedAirport airport in sourced.Values)
            airports.Add(Describe(airport, flattened.GetValueOrDefault(airport.Record.Ident)));
        foreach (AirportBlock orphan in flattened.Values.Where(b => !sourced.ContainsKey(b.Ident)))
            airports.Add(new FlattenableAirport(orphan.Ident, orphan.Name, null, null, FlattenState.Flattened, InEffect: true));
        return airports;
    }

    /// <summary>Flattens <paramref name="airport"/> (again, for an outdated copy: the copy is taken from its scenery as it is now).</summary>
    public FlattenableAirport Flatten(FlattenableAirport airport)
    {
        if (airport.Source is not { } source) throw new InvalidOperationException($"{airport.Ident} has no scenery to take it from.");
        AirportBlock block = AptDatReader.ReadBlock(source.Source.AptDat, source.Record);
        IReadOnlyList<string> buildings = AirportDsfFinder.Find(Path.GetDirectoryName(source.Source.AptDat)!, block);
        _package.Add(block.Flattened(), source.Source.EntryPath, buildings);
        Log.Info($"Flattened {airport.Ident} ({airport.Name}), from {source.Source.Name}, in {FlattenedAirports.PackageName}"
                 + (buildings.Count > 0 ? $", with its buildings from {string.Join(", ", buildings.Select(Path.GetFileName))}" : ""));
        return airport with { State = FlattenState.Flattened, InEffect = _package.IsAbove(source.Source.EntryPath) };
    }

    /// <summary>Takes <paramref name="airport"/>'s flattened copy out: X-Plane uses its own scenery's again.</summary>
    public FlattenableAirport Undo(FlattenableAirport airport)
    {
        _package.Remove(airport.Ident);
        Log.Info($"Undid the flattening of {airport.Ident} ({airport.Name})");
        bool byScenery = airport.Source?.Record.Flattened == true;
        return airport with { State = byScenery ? FlattenState.FlattenedByScenery : FlattenState.NotFlattened, InEffect = true };
    }

    private FlattenableAirport Describe(SourcedAirport airport, AirportBlock? copy)
    {
        AirportRecord record = airport.Record;
        if (copy is null)
            return new(record.Ident, record.Name, record.Place, airport, record.Flattened ? FlattenState.FlattenedByScenery : FlattenState.NotFlattened, true);
        FlattenState state;
        try
        {
            state = AptDatReader.ReadBlock(airport.Source.AptDat, record).Flattened().SameAs(copy) && _package.Buildings.Knows(record.Ident)
                ? FlattenState.Flattened : FlattenState.Outdated;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            Log.Warning($"Could not compare the flattened {record.Ident} with {airport.Source.AptDat}: {ex.Message}");
            state = FlattenState.Flattened;
        }
        return new(record.Ident, record.Name, record.Place, airport, state, _package.IsAbove(airport.Source.EntryPath));
    }
}
