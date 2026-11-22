using System.Text.Json.Serialization;
using OrthoStreamXP.Scenery;

namespace OrthoStreamXP.Setup;

/// <summary>One file of a region's release: its download address, size, and the address of its SHA-256 when one is published.</summary>
internal sealed record MeshAsset(string Name, string Url, long Size, string? Sha256Url = null)
{
    /// <summary>
    /// A plain file name (never a path that leads elsewhere) and HTTPS addresses only: what a list of files, from GitHub or
    /// from a copy kept on the disk, must hold before it is used for paths and downloads.
    /// </summary>
    public bool IsUsable =>
        Name.Length > 0 && Name == Path.GetFileName(Name) && Name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && Name.Trim('.').Length > 0
        && Size >= 0 && IsHttps(Url) && (Sha256Url is null || IsHttps(Sha256Url));

    private static bool IsHttps(string url) => Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps;
}

/// <summary>
/// A region of AutoOrtho's scenery mesh (github.com/kubilus1/autoortho-scenery): the zips of its mesh, each holding one
/// top folder with Earth nav data, terrain and textures, and the parts of one overlays zip (X-Plane's roads, forests and
/// buildings for its squares). Installed as the package <see cref="PackageName"/>, its overlays into
/// <see cref="OverlaysPackageName"/>, with the tiles AutoOrtho's release lacks (<see cref="Supplement"/>) added to both.
/// </summary>
internal sealed record MeshRegion(string Id, string Release, IReadOnlyList<MeshAsset> Mesh, IReadOnlyList<MeshAsset> Overlays)
{
    /// <summary>
    /// The region's tiles from OrthoStreamXP's own scenery repository, when it publishes some for this region. Not kept
    /// with GitHub's list of AutoOrtho's releases: <see cref="SupplementCatalog"/> keeps its own.
    /// </summary>
    [JsonIgnore]
    public RegionSupplement? Supplement { get; init; }

    /// <summary>The package every region's overlays go into.</summary>
    public const string OverlaysPackageName = SceneryPacks.DownloadedPrefix + "Overlays";

    /// <summary>The regions in the order they are listed, with their names and their packages' names.</summary>
    public static readonly IReadOnlyList<(string Id, string Name, string Package)> Known =
    [
        ("eur", "Europe", SceneryPacks.DownloadedPrefix + "Eur"),
        ("na", "North America", SceneryPacks.DownloadedPrefix + "NA"),
        ("sa", "South America", SceneryPacks.DownloadedPrefix + "SA"),
        ("afr", "Africa", SceneryPacks.DownloadedPrefix + "Afr"),
        ("asi", "Asia", SceneryPacks.DownloadedPrefix + "Asi"),
        ("aus_pac", "Australia and the Pacific", SceneryPacks.DownloadedPrefix + "AusPac"),
    ];

    public static bool IsKnown(string id) => Known.Any(k => k.Id == id);

    public static string NameOf(string id) => Known.FirstOrDefault(k => k.Id == id).Name ?? id;

    public static string PackageOf(string id) => Known.FirstOrDefault(k => k.Id == id).Package ?? SceneryPacks.DownloadedPrefix + id;

    public string Name => NameOf(Id);

    public string PackageName => PackageOf(Id);

    public long MeshSize => Mesh.Sum(a => a.Size);

    public long OverlaysSize => Overlays.Sum(a => a.Size);

    /// <summary>What an install downloads, with or without the overlays, the supplement's tiles included.</summary>
    public long DownloadSize(bool withOverlays) => MeshSize + (withOverlays ? OverlaysSize : 0) + (Supplement?.DownloadSize(withOverlays) ?? 0);
}
