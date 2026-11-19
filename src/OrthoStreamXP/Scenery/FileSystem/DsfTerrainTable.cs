using System.Buffers.Binary;
using System.Text;

namespace OrthoStreamXP.Scenery;

/// <summary>
/// The terrain definitions a DSF uses, from its terrain table (the DEFN atom's TERT strings): what X-Plane opens when
/// it loads the tile, read without X-Plane. The table sits after the 100-byte header, before the mesh, so only the
/// first few KB of the file are read (17 KB for a tile of 478 definitions).
/// </summary>
internal static class DsfTerrainTable
{
    private const int HeaderBytes = 12; // "XPLNEDSF" and the version
    private const int AtomHeaderBytes = 8; // id and length, the length counting these 8 bytes
    // Atom ids are 4 characters stored as a little-endian number, so they appear reversed in the file.
    private const uint Definitions = 'D' << 24 | 'E' << 16 | 'F' << 8 | 'N';
    private const uint TerrainTable = 'T' << 24 | 'E' << 16 | 'R' << 8 | 'T';
    /// <summary>A definitions atom larger than this is not a terrain table worth reading (they run to tens of KB).</summary>
    private const int MaxDefinitionsBytes = 16 << 20;

    /// <summary>
    /// The terrain definition names (such as "terrain/22992_34800_BI16.ter"), empty when the file is not an
    /// uncompressed DSF (a 7-Zip one is never read here) or has no terrain table.
    /// </summary>
    public static IReadOnlyList<string> Read(string dsfPath)
    {
        using var file = new FileStream(dsfPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.RandomAccess);
        Span<byte> header = stackalloc byte[HeaderBytes];
        if (file.ReadAtLeast(header, HeaderBytes, throwOnEndOfStream: false) < HeaderBytes || !header[..8].SequenceEqual("XPLNEDSF"u8)) return [];

        Span<byte> atom = stackalloc byte[AtomHeaderBytes];
        long at = HeaderBytes;
        while (at + AtomHeaderBytes <= file.Length)
        {
            file.Position = at;
            if (file.ReadAtLeast(atom, AtomHeaderBytes, throwOnEndOfStream: false) < AtomHeaderBytes) return [];
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(atom);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(atom[4..]);
            if (length < AtomHeaderBytes) return [];
            if (id == Definitions)
            {
                if (length > MaxDefinitionsBytes) return [];
                byte[] definitions = new byte[length - AtomHeaderBytes];
                file.ReadExactly(definitions);
                return TerrainNames(definitions);
            }
            at += length;
        }
        return [];
    }

    /// <summary>The TERT strings among the definitions atom's sub-atoms.</summary>
    private static string[] TerrainNames(ReadOnlySpan<byte> definitions)
    {
        int at = 0;
        while (at + AtomHeaderBytes <= definitions.Length)
        {
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(definitions[at..]);
            int length = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(definitions[(at + 4)..]), int.MaxValue);
            if (length < AtomHeaderBytes || at + length > definitions.Length) return [];
            if (id == TerrainTable)
                return Encoding.ASCII.GetString(definitions.Slice(at + AtomHeaderBytes, length - AtomHeaderBytes))
                    .Split('\0', StringSplitOptions.RemoveEmptyEntries);
            at += length;
        }
        return [];
    }
}
