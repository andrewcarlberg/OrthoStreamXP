using OrthoStreamXP.Textures;

namespace OrthoStreamXP.Scenery;

/// <summary>
/// Readies the scenery tiles X-Plane will load next, minutes before it does. When X-Plane loads a tile it opens its
/// terrain definitions and some 5 s later reads one level of every texture in it (<see cref="MipWarmer.LoadReadPx"/>),
/// and its loading waits for each read: in the test flight of 17 Nov the 400 airport textures of each new tile kept it
/// waiting 0.3-6 s apiece, and the flight stuttered at every tile. Here each tile on the way gets its textures from its
/// DSF's terrain table (<see cref="DsfTerrainTable"/>) and the warmer fetches that level of each, after everything the
/// loaded scenery needs, so X-Plane's reads find them done.
/// <para>
/// Where X-Plane loads: a block of 3 x 4 tiles around the aircraft, the rows of its latitude and one either side, the
/// columns within 1.5° of its longitude (+46..+48 and +011..+014 at 47.72, 13.05); it moves the block a row or column
/// some way after the aircraft crosses into the next tile. The tiles of that block along the aircraft's path for
/// <see cref="LookaheadSeconds"/> that X-Plane has not opened are the ones readied.
/// </para>
/// </summary>
internal sealed class UpcomingScenery : IDisposable
{
    /// <summary>How far along its path the aircraft's block of tiles is looked at.</summary>
    public const double LookaheadSeconds = 300;
    private const double StepSeconds = 30;

    private readonly string _packageSource;
    private readonly ViewTracker _view;
    private readonly SceneryLoads _loads;
    private readonly MipWarmer _warmer;
    private readonly Lock _gate = new();
    private readonly HashSet<SceneryTile> _readied = [];
    private int _busy;
    private volatile bool _disposed;

    public UpcomingScenery(string packageSource, ViewTracker view, SceneryLoads loads, MipWarmer warmer)
    {
        _packageSource = packageSource;
        _view = view;
        _loads = loads;
        _warmer = warmer;
        view.Moved += OnMoved;
    }

    /// <summary>The tiles X-Plane keeps loaded with the aircraft at a point.</summary>
    internal static IEnumerable<SceneryTile> LoadedAround(double lat, double lon)
    {
        int row = (int)Math.Floor(lat), firstColumn = (int)Math.Floor(lon - 1.5);
        for (int r = row - 1; r <= row + 1; r++)
        for (int c = firstColumn; c <= firstColumn + 3; c++)
            yield return SceneryTile.At(r, c);
    }

    /// <summary>The tiles X-Plane will load along the path, nearest first; none for a parked or estimated aircraft.</summary>
    internal static List<SceneryTile> Ahead(CameraReport camera)
    {
        if (camera.Inferred || camera.Motion is not { SpeedMps: >= PrefetchPlanner.MovingSpeedMps } motion) return [];
        var tiles = new List<SceneryTile>();
        var now = new HashSet<SceneryTile>(LoadedAround(camera.Lat, camera.Lon));
        double track = motion.TrackDeg * Math.PI / 180, metresPerDegLat = 110_574;
        for (double seconds = StepSeconds; seconds <= LookaheadSeconds; seconds += StepSeconds)
        {
            double metres = motion.SpeedMps * seconds;
            double lat = camera.Lat + metres * Math.Cos(track) / metresPerDegLat;
            double lon = camera.Lon + metres * Math.Sin(track) / (metresPerDegLat * Math.Max(0.01, Math.Cos(camera.Lat * Math.PI / 180)));
            foreach (SceneryTile tile in LoadedAround(Math.Clamp(lat, -89.9, 89.9), lon))
                if (!now.Contains(tile) && !tiles.Contains(tile)) tiles.Add(tile);
        }
        return tiles;
    }

    private void OnMoved(CameraReport camera)
    {
        List<SceneryTile> ahead = Ahead(camera);
        lock (_gate) ahead.RemoveAll(tile => _readied.Contains(tile) || _loads.Seen(tile));
        if (ahead.Count == 0 || Interlocked.Exchange(ref _busy, 1) == 1) return;
        _ = Task.Run(() =>
        {
            try
            {
                foreach (SceneryTile tile in ahead) Ready(tile);
            }
            finally
            {
                Volatile.Write(ref _busy, 0);
            }
        });
    }

    /// <summary>Queues the textures of one tile's DSF in this package; a tile the package lacks is left alone.</summary>
    internal int Ready(SceneryTile tile)
    {
        lock (_gate)
            if (!_readied.Add(tile)) return 0;
        string dsf = Path.Join(_packageSource, tile.DsfPath);
        if (!File.Exists(dsf)) return 0;
        IReadOnlyList<string> terrain;
        try
        {
            terrain = DsfTerrainTable.Read(dsf);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning($"Could not read the terrain table of {dsf}: {ex.Message}");
            return 0;
        }

        var textures = new List<VirtualPath>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string definition in terrain)
            if (TerrainDefinitions.TextureOf(Path.GetFileName(definition), out _, out _) is { } name && seen.Add(name.Stem))
                textures.Add(VirtualPath.Of("textures", name.Stem + ".dds"));
        if (textures.Count == 0 || _disposed) return 0;
        int queued = _warmer.PrepareUpcoming(textures);
        Log.Info($"Scenery tile {tile} is ahead: {textures.Count} textures readied at the level X-Plane reads when it loads the tile");
        return queued;
    }

    public void Dispose()
    {
        _disposed = true;
        _view.Moved -= OnMoved;
    }
}
