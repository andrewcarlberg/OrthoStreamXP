using System.Collections.Concurrent;
using System.Globalization;
using System.Net;

namespace OrthoStreamXP.Textures;

/// <summary>
/// The satellite record of how green each place was over the last year: NASA GIBS's monthly vegetation index
/// (VIIRS on Suomi NPP, 1 km, free and without an account), one 512 x 512 picture a month for each
/// <see cref="VegetationKey"/> tile, about 150 KB each. Each month is kept on disk as its cells
/// (<see cref="VegetationTile.CellsOf"/>, 16 KB), so a place is downloaded once for the year.
/// <para>
/// The year is the latest twelve months NASA has finished: a month's picture appears some weeks after it ends. A
/// month that is not there is taken from the year before.
/// </para>
/// </summary>
internal sealed class VegetationSource : IDisposable
{
    /// <summary>The folder in the low-zoom base that holds the record.</summary>
    public const string FolderName = "vegetation";

    /// <summary>NASA's layer.</summary>
    public const string Layer = "VIIRS_SNPP_L3_NDVI_Monthly";

    /// <summary>How long after a month NASA's picture of it is counted on.</summary>
    private const int DaysToPublish = 40;

    /// <summary>The leading bytes of a month's file.</summary>
    private static readonly byte[] Magic = "OSXPNDV1"u8.ToArray();

    private readonly string _folder;
    private readonly TimeProvider _time;
    private readonly HttpClient? _http;

    /// <param name="folder">Where the months are kept.</param>
    /// <param name="download">False: only what is already on disk (the Settings page's preview never downloads).</param>
    /// <param name="handler">Replaces the network stack (tests).</param>
    public VegetationSource(string folder, TimeProvider time, bool download = true, HttpMessageHandler? handler = null)
    {
        _folder = Path.GetFullPath(folder);
        _time = time;
        if (!download) return;
        _http = new HttpClient(handler ?? new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10), ConnectTimeout = TimeSpan.FromSeconds(10) })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("orthostreamxp/1.0");
    }

    public string Folder => _folder;

    /// <summary>Pictures downloaded this session.</summary>
    public int Downloaded => Volatile.Read(ref _downloaded);

    private int _downloaded;

    /// <summary>The twelve months of the latest year NASA has finished, oldest first (each the first of its month).</summary>
    public IReadOnlyList<DateOnly> Year()
    {
        DateOnly published = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime.AddDays(-DaysToPublish));
        var last = new DateOnly(published.Year, published.Month, 1);
        return [.. Enumerable.Range(0, 12).Select(i => last.AddMonths(i - 11))];
    }

    /// <summary>
    /// A tile's year, from disk or downloaded; null when fewer than nine of its months could be had (offline, or NASA
    /// unreachable). Problems are logged, never thrown.
    /// </summary>
    public async Task<VegetationTile?> LoadAsync(VegetationKey key, CancellationToken cancel)
    {
        IReadOnlyList<DateOnly> year = Year();
        byte[]?[] months = new byte[]?[12];
        var problems = new ConcurrentQueue<string>();
        Task<byte[]?>[] loads = [.. year.Select(month => LoadMonthAsync(key, month, problems, cancel))];
        await Task.WhenAll(loads).ConfigureAwait(false);
        for (int i = 0; i < year.Count; i++) months[year[i].Month - 1] = await loads[i].ConfigureAwait(false);
        if (!problems.IsEmpty)
            Log.Warning($"Seasons: {problems.Count} month(s) of the satellite record for the tile with its {key} could not be downloaded " +
                        $"({problems.First()}); they are tried again at the next start");
        var tile = new VegetationTile(key, months);
        return tile.Months >= 12 - GreennessYear.MaxMissing ? tile : null;
    }

    /// <summary>Removes the months older than the year before the current one: the record moves on a month at a time.</summary>
    public void RemoveOldMonths()
    {
        if (!Directory.Exists(_folder)) return;
        string oldest = Name(Year()[0].AddMonths(-12));
        foreach (string folder in Directory.EnumerateDirectories(_folder))
        {
            string name = Path.GetFileName(folder);
            if (!DateOnly.TryParseExact(name, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                || string.CompareOrdinal(name, oldest) >= 0) continue;
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warning($"Seasons: could not remove the old satellite record {folder}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// A month's cells: kept on disk, else downloaded. When NASA has no picture of it, or cannot be reached (offline), the
    /// same month a year before stands in: kept on disk, else downloaded if NASA answered.
    /// </summary>
    private async Task<byte[]?> LoadMonthAsync(VegetationKey key, DateOnly month, ConcurrentQueue<string> problems, CancellationToken cancel)
    {
        if (Read(PathOf(key, month)) is { } kept) return kept;
        bool answered = false;
        if (_http is not null)
        {
            (answered, byte[]? cells) = await DownloadAsync(key, month, problems, cancel).ConfigureAwait(false);
            if (cells is not null) return cells;
        }
        DateOnly yearBefore = month.AddMonths(-12);
        if (Read(PathOf(key, yearBefore)) is { } older) return older;
        // Unreachable: not asked again for the year before.
        return answered ? (await DownloadAsync(key, yearBefore, problems, cancel).ConfigureAwait(false)).Cells : null;
    }

    /// <summary>
    /// Downloads and keeps a month: (true, cells), (true, null) when NASA has no such picture, (false, null) when it could
    /// not be had (the reason added to <paramref name="problems"/>).
    /// </summary>
    private async Task<(bool Answered, byte[]? Cells)> DownloadAsync(VegetationKey key, DateOnly month, ConcurrentQueue<string> problems, CancellationToken cancel)
    {
        Uri uri = UriOf(key, month);
        try
        {
            using HttpResponseMessage response = await _http!.GetAsync(uri, cancel).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest) return (true, null);
            response.EnsureSuccessStatusCode();
            byte[] png = await response.Content.ReadAsByteArrayAsync(cancel).ConfigureAwait(false);
            byte[] cells = VegetationTile.CellsOf(png);
            Interlocked.Increment(ref _downloaded);
            Save(PathOf(key, month), cells);
            return (true, cells);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException || (ex is TaskCanceledException && !cancel.IsCancellationRequested))
        {
            problems.Enqueue(string.Create(CultureInfo.InvariantCulture, $"{month:MMMM yyyy}: {ex.Message}"));
            return (false, null);
        }
    }

    /// <summary>NASA's picture of a tile in a month.</summary>
    public static Uri UriOf(VegetationKey key, DateOnly month) => new(string.Create(CultureInfo.InvariantCulture,
        $"https://gibs.earthdata.nasa.gov/wmts/epsg4326/best/{Layer}/default/{month:yyyy-MM-dd}/1km/6/{key.Row}/{key.Col}.png"));

    private string PathOf(VegetationKey key, DateOnly month) =>
        Path.Join(_folder, Name(month), string.Create(CultureInfo.InvariantCulture, $"{key.Row}_{key.Col}.ndvi"));

    private static string Name(DateOnly month) => month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static byte[]? Read(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            byte[] file = File.ReadAllBytes(path);
            if (file.Length == Magic.Length + VegetationTile.MonthBytes && file.AsSpan(0, Magic.Length).SequenceEqual(Magic))
                return file[Magic.Length..];
            Log.Warning($"Seasons: {path} is not a satellite record; it is downloaded again");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning($"Seasons: could not read {path}: {ex.Message}");
            return null;
        }
    }

    private static void Save(string path, byte[] cells)
    {
        try
        {
            AtomicFile.Write(path, [.. Magic, .. cells]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning($"Seasons: could not keep the satellite record in {path}: {ex.Message}");
        }
    }

    public void Dispose() => _http?.Dispose();
}
