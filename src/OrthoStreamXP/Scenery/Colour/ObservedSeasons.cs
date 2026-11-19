using System.Collections.Concurrent;
using System.Diagnostics;
using OrthoStreamXP.Textures;

namespace OrthoStreamXP.Scenery;

/// <summary>
/// The satellite record (<see cref="VegetationSource"/>) of the places a flight sees, loaded a tile (about 500 km) at a
/// time: ahead of the aircraft (<see cref="Prepare"/>), or when a texture first needs its place. A texture that
/// needs a tile still on its way waits for it for at most <see cref="Wait"/> from the start of its download, once
/// for the tile; after that, places in it go by the calendar until it is there.
/// </summary>
internal sealed class ObservedSeasons : IDisposable
{
    /// <summary>The degrees around the aircraft whose tiles are loaded ahead of it.</summary>
    private const double AheadDegrees = 1.5;

    private readonly VegetationSource _source;
    private readonly ConcurrentDictionary<VegetationKey, TileLoad> _tiles = new();
    private readonly CancellationTokenSource _cancel = new();
    private readonly CancellationToken _stopping;

    /// <param name="wait">How long the first texture of a tile may wait for its download.</param>
    public ObservedSeasons(VegetationSource source, TimeSpan wait)
    {
        _source = source;
        Wait = wait;
        _stopping = _cancel.Token;
    }

    public TimeSpan Wait { get; }

    /// <summary>Tiles whose record is loaded.</summary>
    public int TilesLoaded => _tiles.Values.Count(t => t.Task.IsCompletedSuccessfully && t.Task.Result is not null);

    /// <summary>Tiles without a record: not downloadable now, or the sea.</summary>
    public int TilesMissing => _tiles.Values.Count(t => t.Task.IsCompleted && (!t.Task.IsCompletedSuccessfully || t.Task.Result is null));

    /// <summary>Starts loading the tiles around a place (the aircraft's), so its textures find them ready.</summary>
    public void Prepare(double lat, double lon)
    {
        for (double dLat = -AheadDegrees; dLat <= AheadDegrees; dLat += AheadDegrees)
            for (double dLon = -AheadDegrees; dLon <= AheadDegrees; dLon += AheadDegrees)
                Load(VegetationKey.At(Math.Clamp(lat + dLat, -89.9, 89.9), WrapLon(lon + dLon)));
    }

    /// <summary>The year of a place, waiting for its tile as <see cref="Wait"/> allows; null when there is none (yet).</summary>
    public GreennessYear? YearAt(double lat, double lon)
    {
        TileLoad load = Load(VegetationKey.At(lat, lon));
        if (!load.Task.IsCompleted)
        {
            TimeSpan left = Wait - Stopwatch.GetElapsedTime(load.Started);
            if (left <= TimeSpan.Zero || !((IAsyncResult)load.Task).AsyncWaitHandle.WaitOne(left)) return null;
        }
        return load.Task.IsCompletedSuccessfully ? load.Task.Result?.YearAt(lat, lon) : null;
    }

    private TileLoad Load(VegetationKey key) =>
        _tiles.GetOrAdd(key, k => new TileLoad(Stopwatch.GetTimestamp(), Task.Run(() => LoadAsync(k))));

    private async Task<VegetationTile?> LoadAsync(VegetationKey key)
    {
        try
        {
            VegetationTile? tile = await _source.LoadAsync(key, _stopping).ConfigureAwait(false);
            if (tile is null) Log.Info($"Seasons: no satellite record for the tile with its {key} (offline, or the sea); the calendar's season is used there");
            else Log.Detail($"Seasons: satellite record of the tile with its {key}: {tile.Months} months");
            return tile;
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            return null;
        }
    }

    private static double WrapLon(double lon) => ((lon + 180) % 360 + 360) % 360 - 180;

    /// <summary>Stops the downloads still running; the record kept on disk stays for the next start.</summary>
    public void Dispose()
    {
        _cancel.Cancel();
        _cancel.Dispose();
        _source.Dispose();
    }

    private sealed record TileLoad(long Started, Task<VegetationTile?> Task);
}
