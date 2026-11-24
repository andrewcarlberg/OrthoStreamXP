using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace OrthoStreamXP.Setup;

/// <summary>A DSF atom: its four-character id (as stored, a little-endian integer) and contents.</summary>
internal sealed record DsfAtom(uint Id, byte[] Body);

/// <summary>
/// An uncompressed DSF (X-Plane's scenery tile format) as atoms: its properties (HEAD/PROP), definition tables (DEFN), point pools
/// and their scales (GEOD, kept as they are) and command stream (CMDS). Raster layers (DEMS) are not kept. Strings are Latin-1, so
/// they come back byte for byte.
/// </summary>
internal sealed record DsfFile(
    IReadOnlyList<KeyValuePair<string, string>> Properties,
    IReadOnlyDictionary<uint, IReadOnlyList<string>> Definitions,
    IReadOnlyList<DsfAtom> Geodesy,
    byte[] Commands)
{
    // Atom ids are four characters stored as a little-endian integer.
    public const uint Head = 0x48454144, Prop = 0x50524F50, Defn = 0x4445464E, Geod = 0x47454F44, Cmds = 0x434D4453;
    public const uint Terrains = 0x54455254, Objects = 0x4F424A54, Polygons = 0x504F4C59, Networks = 0x4E455457, Rasters = 0x44454D4E;
    public const uint Pool16 = 0x504F4F4C, Pool32 = 0x504F3332;

    /// <summary>The definition tables in the order X-Plane writes them.</summary>
    public static readonly uint[] DefinitionTables = [Terrains, Objects, Polygons, Networks, Rasters];

    private const int HeaderSize = 12, FooterSize = 16; // "XPLNEDSF", version; the MD5 at the end

    /// <summary>The values of every property named <paramref name="key"/>, in order.</summary>
    public IEnumerable<string> Values(string key) => Properties.Where(p => p.Key == key).Select(p => p.Value);

    /// <exception cref="InvalidDataException">Not an uncompressed DSF, or damaged.</exception>
    public static DsfFile Parse(byte[] file)
    {
        CheckHeader(file);
        try
        {
            IReadOnlyList<KeyValuePair<string, string>> properties = [];
            var definitions = new Dictionary<uint, IReadOnlyList<string>>();
            List<DsfAtom> geodesy = [];
            byte[] commands = [];
            foreach (DsfAtom atom in Atoms(file.AsSpan(HeaderSize, file.Length - HeaderSize - FooterSize)))
            {
                switch (atom.Id)
                {
                    case Head: properties = Pairs(Atoms(atom.Body).FirstOrDefault(a => a.Id == Prop)?.Body ?? []); break;
                    case Defn:
                        foreach (DsfAtom table in Atoms(atom.Body)) definitions[table.Id] = Strings(table.Body);
                        break;
                    case Geod: geodesy = Atoms(atom.Body); break;
                    case Cmds: commands = atom.Body; break;
                }
            }
            return new DsfFile(properties, definitions, geodesy, commands);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            throw new InvalidDataException("the DSF is cut short or damaged", ex);
        }
    }

    /// <summary>Only the properties of the DSF at <paramref name="path"/>, read from the start of the file.</summary>
    /// <exception cref="InvalidDataException">Not an uncompressed DSF, or damaged.</exception>
    public static IReadOnlyList<KeyValuePair<string, string>> ReadProperties(string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] start = new byte[HeaderSize + 8];
        stream.ReadExactly(start);
        CheckHeader(start);
        if (BinaryPrimitives.ReadUInt32LittleEndian(start.AsSpan(HeaderSize)) != Head) throw new InvalidDataException("the DSF does not start with its properties");
        int length = BinaryPrimitives.ReadInt32LittleEndian(start.AsSpan(HeaderSize + 4));
        if (length < 8 || length > stream.Length - HeaderSize) throw new InvalidDataException("the DSF's properties run past its end");
        byte[] head = new byte[length - 8];
        stream.ReadExactly(head);
        return Pairs(Atoms(head).FirstOrDefault(a => a.Id == Prop)?.Body ?? []);
    }

    /// <summary>The file X-Plane reads: header, atoms, and the MD5 of all of it.</summary>
    public byte[] ToBytes()
    {
        using var file = new MemoryStream();
        file.Write("XPLNEDSF"u8);
        WriteInt(file, 1);
        WriteAtom(file, Head, Atom(Prop, Latin1(Properties.SelectMany(p => new[] { p.Key, p.Value }))));
        WriteAtom(file, Defn, Concat(DefinitionTables.Select(id => Atom(id, Latin1(Definitions.GetValueOrDefault(id) ?? [])))));
        WriteAtom(file, Geod, Concat(Geodesy.Select(a => Atom(a.Id, a.Body))));
        WriteAtom(file, Cmds, Commands);
#pragma warning disable CA5351 // the DSF format ends with an MD5 checksum of the file; it is not used for security
        file.Write(MD5.HashData(file.GetBuffer().AsSpan(0, (int)file.Length)));
#pragma warning restore CA5351
        return file.ToArray();
    }

    private static void CheckHeader(ReadOnlySpan<byte> file)
    {
        if (file.Length >= 6 && file[..6].SequenceEqual((ReadOnlySpan<byte>)[0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C]))
            throw new InvalidDataException("the DSF is compressed (7-Zip)");
        if (file.Length < HeaderSize + 8 || !file[..8].SequenceEqual("XPLNEDSF"u8)) throw new InvalidDataException("not a DSF file");
    }

    private static List<DsfAtom> Atoms(ReadOnlySpan<byte> data)
    {
        var atoms = new List<DsfAtom>();
        int at = 0;
        while (at + 8 <= data.Length)
        {
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);
            int length = BinaryPrimitives.ReadInt32LittleEndian(data[(at + 4)..]);
            if (length < 8 || length > data.Length - at) throw new InvalidDataException("a DSF atom runs past its parent");
            atoms.Add(new DsfAtom(id, data[(at + 8)..(at + length)].ToArray()));
            at += length;
        }
        return atoms;
    }

    private static List<string> Strings(byte[] table)
    {
        List<string> strings = [.. Encoding.Latin1.GetString(table).Split('\0')];
        if (strings.Count > 0 && strings[^1].Length == 0) strings.RemoveAt(strings.Count - 1); // after the last terminator
        return strings;
    }

    private static List<KeyValuePair<string, string>> Pairs(byte[] table)
    {
        List<string> strings = Strings(table);
        var pairs = new List<KeyValuePair<string, string>>(strings.Count / 2);
        for (int i = 0; i + 1 < strings.Count; i += 2) pairs.Add(new(strings[i], strings[i + 1]));
        return pairs;
    }

    private static byte[] Latin1(IEnumerable<string> strings) => Encoding.Latin1.GetBytes(string.Concat(strings.Select(s => s + "\0")));

    private static byte[] Atom(uint id, byte[] body)
    {
        byte[] atom = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(atom, id);
        BinaryPrimitives.WriteInt32LittleEndian(atom.AsSpan(4), atom.Length);
        body.CopyTo(atom, 8);
        return atom;
    }

    private static byte[] Concat(IEnumerable<byte[]> parts) => [.. parts.SelectMany(p => p)];

    private static void WriteAtom(Stream file, uint id, byte[] body) => file.Write(Atom(id, body));

    private static void WriteInt(Stream file, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        file.Write(bytes);
    }
}
