using System.Text.Json;

namespace OrthoStreamXP.Setup;

/// <summary>
/// The tiles AutoOrtho's releases lack, by region: <c>supplements.json</c> of the newest release of OrthoStreamXP's own
/// scenery repository, read from its download address (no GitHub API call, so no hourly limit). The list is kept beside
/// the settings and asked for again at most every hour. It is never needed: when it cannot be read and none was kept, the
/// regions come without supplements and are installed as AutoOrtho publishes them.
/// </summary>
internal sealed class SupplementCatalog(HttpClient http, string cacheFile)
{
    public const string Repository = "flysimdev/orthostreamxp-scenery";
    public const string ProjectPage = "https://github.com/" + Repository;
    public const string ManifestUrl = ProjectPage + "/releases/latest/download/supplements.json";
    /// <summary>The manifest's format this version reads; a newer one is left alone until the program is updated.</summary>
    public const int Schema = 1;
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(1), RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The supplements by region id: from the list kept when it is recent (unless <paramref name="refresh"/>), else from GitHub.</summary>
    public async Task<IReadOnlyDictionary<string, RegionSupplement>> LoadAsync(bool refresh, CancellationToken cancel)
    {
        bool fresh = !refresh && File.Exists(cacheFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cacheFile) < MaxAge;
        if (fresh && ReadCache() is { } kept) return kept;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(RequestTimeout);
            using HttpResponseMessage response = await http.GetAsync(new Uri(ManifestUrl), timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            IReadOnlyDictionary<string, RegionSupplement> supplements = Parse(json);
            WriteCache(json);
            return supplements;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or OperationCanceledException && !cancel.IsCancellationRequested)
        {
            IReadOnlyDictionary<string, RegionSupplement>? older = ReadCache();
            Log.Info($"The list of tiles AutoOrtho lacks could not be read from {Repository} ({ex.Message}); " +
                     (older is null ? "the regions are installed as AutoOrtho publishes them" : "the list kept from before is used"));
            return older ?? new Dictionary<string, RegionSupplement>();
        }
    }

    /// <summary>The regions with their supplements attached (a region without one is left as it is).</summary>
    public static IReadOnlyList<MeshRegion> Attach(IReadOnlyList<MeshRegion> regions, IReadOnlyDictionary<string, RegionSupplement> supplements) =>
        [.. regions.Select(r => supplements.TryGetValue(r.Id, out RegionSupplement? s) ? r with { Supplement = s } : r)];

    /// <summary>
    /// The regions of a manifest. A region the program does not know, or an entry that cannot be used (a file name that is
    /// a path, an address that is not HTTPS, a version below 1), is left out and logged; a manifest of another
    /// <see cref="Schema"/> gives none.
    /// </summary>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    internal static IReadOnlyDictionary<string, RegionSupplement> Parse(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        var found = new Dictionary<string, RegionSupplement>();
        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schema", out JsonElement schema)
            || schema.ValueKind != JsonValueKind.Number || schema.GetInt32() != Schema)
        {
            Log.Info("The list of tiles AutoOrtho lacks is in a format this version does not read; update OrthoStreamXP to use it");
            return found;
        }
        if (!root.TryGetProperty("regions", out JsonElement regions) || regions.ValueKind != JsonValueKind.Object) return found;
        foreach (JsonProperty entry in regions.EnumerateObject())
        {
            RegionSupplement? supplement = null;
            try
            {
                supplement = Region(entry.Value);
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException or KeyNotFoundException)
            {
                // A value of the wrong kind: the entry is left out below.
            }
            if (MeshRegion.IsKnown(entry.Name) && supplement is not null) found[entry.Name] = supplement;
            else Log.Warning($"The tiles AutoOrtho lacks for '{entry.Name}' are left out: the entry cannot be used");
        }
        return found;
    }

    private static RegionSupplement? Region(JsonElement e)
    {
        int version = e.GetProperty("version").GetInt32();
        string[] tiles = [.. e.GetProperty("tiles").EnumerateArray().Select(t => t.GetString() ?? "")];
        MeshAsset mesh = Asset(e.GetProperty("mesh"));
        MeshAsset? overlays = e.TryGetProperty("overlays", out JsonElement o) && o.ValueKind == JsonValueKind.Object ? Asset(o) : null;
        bool usable = version >= 1 && tiles.Length > 0 && mesh.IsUsable && overlays?.IsUsable != false;
        return usable ? new RegionSupplement(version, tiles, mesh, overlays) : null;
    }

    private static MeshAsset Asset(JsonElement e) => new(
        e.GetProperty("name").GetString() ?? "",
        e.GetProperty("url").GetString() ?? "",
        e.GetProperty("size").GetInt64(),
        e.TryGetProperty("sha256Url", out JsonElement sha) && sha.ValueKind == JsonValueKind.String ? sha.GetString() : null);

    private IReadOnlyDictionary<string, RegionSupplement>? ReadCache()
    {
        try
        {
            return File.Exists(cacheFile) ? Parse(File.ReadAllText(cacheFile)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void WriteCache(string json)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
            AtomicFile.Write(cacheFile, System.Text.Encoding.UTF8.GetBytes(json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning($"Could not keep the list of tiles AutoOrtho lacks: {ex.Message}");
        }
    }
}
