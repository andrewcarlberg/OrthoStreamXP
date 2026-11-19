namespace OrthoStreamXP.Textures;

/// <summary>
/// A 256x256 source tile as it is kept in RAM: the image bytes as downloaded (a JPEG of some 20-40 KB), decoded only
/// where its pixels are used. A decoded tile is 192 KB, a large object .NET collects only in full collections: a full-size
/// texture's 1024 of them made some 190 MB of large-object garbage per build. The synthetic imagery keeps its pixels.
/// </summary>
internal sealed class SourceTile
{
    private readonly byte[]? _encoded;
    private readonly byte[]? _rgb;

    private SourceTile(byte[]? encoded, byte[]? rgb) => (_encoded, _rgb) = (encoded, rgb);

    /// <summary>An image file's bytes, already checked to decode (<see cref="Images.CheckTile"/>).</summary>
    public static SourceTile FromEncoded(byte[] encoded) => new(encoded, null);

    /// <summary>256x256 RGB pixels.</summary>
    public static SourceTile FromRgb(byte[] rgb) =>
        rgb.Length == Tiles.TileBytes ? new(null, rgb) : throw new ArgumentException("Not a 256x256 RGB tile", nameof(rgb));

    /// <summary>The bytes it takes in RAM.</summary>
    public int Length => (_encoded ?? _rgb)!.Length;

    /// <summary>Writes the tile's 256x256 RGB pixels into <paramref name="rgb"/> (a pooled buffer may be longer).</summary>
    public void CopyPixelsTo(Span<byte> rgb)
    {
        if (_rgb is not null) _rgb.CopyTo(rgb);
        else Images.DecodeTileInto(_encoded!, rgb);
    }

    /// <summary>The tile's 256x256 RGB pixels in an array of their own (the synthetic tile's own array, not to be changed).</summary>
    public byte[] ToRgb() => _rgb ?? Images.DecodeTile(_encoded!);
}
