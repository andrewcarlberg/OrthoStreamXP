namespace OrthoStreamXP.Setup;

/// <summary>
/// Tiles of a region that AutoOrtho's release lacks, published by OrthoStreamXP's own scenery repository
/// (<see cref="SupplementCatalog.Repository"/>): one zip of mesh (Earth nav data and the tiles' sea masks) and one of
/// overlays, added to the region's package and to the shared overlays package. A file already there is never replaced, so
/// AutoOrtho's own files win, and a tile AutoOrtho's release gains later is AutoOrtho's.
/// </summary>
/// <param name="Version">Goes up whenever the region's files in the repository change; an installed region records the one it has.</param>
/// <param name="Tiles">The tiles, as their DSFs are named (+44-122).</param>
internal sealed record RegionSupplement(int Version, IReadOnlyList<string> Tiles, MeshAsset Mesh, MeshAsset? Overlays)
{
    /// <summary>What adding the tiles downloads, with or without their overlays.</summary>
    public long DownloadSize(bool withOverlays) => Mesh.Size + (withOverlays && Overlays is not null ? Overlays.Size : 0);

    /// <summary>The files adding the tiles downloads.</summary>
    public IEnumerable<MeshAsset> Assets(bool withOverlays) => withOverlays && Overlays is not null ? [Mesh, Overlays] : [Mesh];
}
