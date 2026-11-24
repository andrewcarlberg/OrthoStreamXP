using System.Buffers.Binary;

namespace OrthoStreamXP.Setup;

/// <summary>
/// Copies chosen airports' 3D out of overlay DSFs into one new DSF. X-Plane's gateway airports keep their buildings, ground
/// polygons and roads in "filters": a sim/filter/aptid property names the airport, and a comment command (type 1, then the
/// filter's number in the order of those properties) starts its commands. X-Plane draws a filter only when the airport it names
/// comes from the same package, so an airport taken over by another package's apt.dat needs its filters copied there too.
/// The new file keeps each source's point pools whole and re-numbers pools, definitions and filters as it merges; everything
/// outside the chosen filters is left out.
/// </summary>
internal sealed class AirportDsfMerger
{
    public const string FilterProperty = "sim/filter/aptid";

    private const ushort FilterComment = 1;

    private readonly List<KeyValuePair<string, string>> _global = [], _filters = [];
    private readonly Dictionary<uint, List<string>> _definitions = DsfFile.DefinitionTables.ToDictionary(id => id, _ => new List<string>());
    private readonly List<DsfAtom> _geodesy = [];
    private readonly List<byte> _commands = [];
    private int _filterCount;

    // What the new command stream has set so far (-1: nothing yet).
    private long _pool = -1, _definition = -1, _subtype = -1, _junction = -1, _filter = -1;
    private readonly Dictionary<ushort, byte[]> _markers = [];

    /// <summary>Whether any airport was copied.</summary>
    public bool IsEmpty => _filterCount == 0;

    /// <summary>Copies the filters of <paramref name="idents"/> from <paramref name="source"/>; false when it has none of them.</summary>
    /// <exception cref="InvalidDataException">The source is not an overlay, or its commands are damaged or draw terrain in a chosen filter.</exception>
    public bool Add(DsfFile source, IReadOnlySet<string> idents)
    {
        if (!source.Values("sim/overlay").Contains("1")) throw new InvalidDataException("the DSF is not an overlay");
        Dictionary<int, int> filters = TakeProperties(source, idents);
        if (filters.Count == 0) return false;

        var definitions = DsfFile.DefinitionTables.ToDictionary(id => id, id => Merge(id, source.Definitions.GetValueOrDefault(id) ?? []));
        var poolOffset = new Dictionary<uint, int> { [DsfFile.Pool16] = Count(DsfFile.Pool16), [DsfFile.Pool32] = Count(DsfFile.Pool32) };
        _geodesy.AddRange(source.Geodesy);
        try
        {
            new Copier(this, filters, definitions, poolOffset).Run(source.Commands);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException or OverflowException)
        {
            throw new InvalidDataException("the DSF commands are cut short or damaged", ex);
        }
        return true;
    }

    /// <summary>The new DSF: the sources' general properties, then each copied filter with its own (exclusions and the like).</summary>
    public DsfFile Build() => new(
        [.. _global, .. _filters],
        DsfFile.DefinitionTables.ToDictionary(id => id, id => (IReadOnlyList<string>)_definitions[id]),
        _geodesy,
        [.. _commands]);

    /// <summary>
    /// Takes the source's general properties (those before its first filter) and the properties of the chosen filters. Returns
    /// the chosen filters' numbers in the source and in the new file.
    /// </summary>
    private Dictionary<int, int> TakeProperties(DsfFile source, IReadOnlySet<string> idents)
    {
        var filters = new Dictionary<int, int>();
        int filter = -1;
        bool chosen = false;
        foreach (KeyValuePair<string, string> property in source.Properties)
        {
            if (property.Key == FilterProperty)
            {
                filter++;
                chosen = idents.Contains(property.Value);
                if (chosen) filters[filter] = _filterCount++;
            }
            if (filter < 0)
            {
                if (property.Key != "sim/creation_agent" && !_global.Contains(property)) _global.Add(property);
            }
            else if (chosen)
            {
                _filters.Add(property);
            }
        }
        if (!_global.Any(p => p.Key == "sim/creation_agent")) _global.Add(new("sim/creation_agent", "OrthoStreamXP"));
        return filters;
    }

    /// <summary>Adds a source's definition table to the new one (an entry already there is shared); its entries' new numbers.</summary>
    private int[] Merge(uint table, IReadOnlyList<string> entries)
    {
        List<string> merged = _definitions[table];
        var numbers = new int[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            int at = merged.IndexOf(entries[i]);
            if (at < 0)
            {
                at = merged.Count;
                merged.Add(entries[i]);
            }
            numbers[i] = at;
        }
        return numbers;
    }

    private int Count(uint pool) => _geodesy.Count(a => a.Id == pool);

    private enum Kind
    {
        Object,
        Network,
        Polygon,
    }

    /// <summary>Walks one source's command stream, writing the chosen filters' commands with the state they need.</summary>
    private sealed class Copier(AirportDsfMerger target, Dictionary<int, int> filters, Dictionary<uint, int[]> definitions, Dictionary<uint, int> poolOffset)
    {
        private int _pool, _filter = -1, _subtype;
        private uint _definition, _junction;
        private readonly Dictionary<ushort, byte[]> _markers = [];

        public void Run(byte[] commands)
        {
            int at = 0;
            while (at < commands.Length)
            {
                byte command = commands[at];
                int length = 1 + PayloadLength(commands, at + 1, command);
                if (at + length > commands.Length) throw new InvalidDataException("the DSF commands are cut short");
                ReadOnlySpan<byte> payload = commands.AsSpan(at + 1, length - 1);
                switch (command)
                {
                    case 1: _pool = BinaryPrimitives.ReadUInt16LittleEndian(payload); break;
                    case 2: _junction = BinaryPrimitives.ReadUInt32LittleEndian(payload); break;
                    case 3: _definition = payload[0]; break;
                    case 4: _definition = BinaryPrimitives.ReadUInt16LittleEndian(payload); break;
                    case 5: _definition = BinaryPrimitives.ReadUInt32LittleEndian(payload); break;
                    case 6: _subtype = payload[0]; break;
                    case 7 or 8: Copy(Kind.Object, commands.AsSpan(at, length), usesJunction: false); break;
                    case 9 or 10: Copy(Kind.Network, commands.AsSpan(at, length), usesJunction: true); break;
                    case 11: Copy(Kind.Network, commands.AsSpan(at, length), usesJunction: false); break;
                    case >= 12 and <= 15: Copy(Kind.Polygon, commands.AsSpan(at, length), usesJunction: false); break;
                    case 32: Comment(payload[1..]); break;
                    case (>= 16 and <= 18) or (>= 23 and <= 31):
                        if (filters.ContainsKey(_filter)) throw new InvalidDataException("an airport's filter draws terrain");
                        break;
                }
                at += length;
            }
        }

        /// <summary>A comment either starts a filter (type 1) or marks the commands after it: kept to be written before them.</summary>
        private void Comment(ReadOnlySpan<byte> text)
        {
            if (text.Length < 2) return;
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(text);
            if (type == FilterComment) _filter = text.Length >= 6 ? BinaryPrimitives.ReadInt32LittleEndian(text[2..]) : -1;
            else _markers[type] = text.ToArray();
        }

        private void Copy(Kind kind, ReadOnlySpan<byte> command, bool usesJunction)
        {
            if (!filters.TryGetValue(_filter, out int filter)) return;
            if (filter != target._filter)
            {
                Span<byte> text = stackalloc byte[6];
                BinaryPrimitives.WriteUInt16LittleEndian(text, FilterComment);
                BinaryPrimitives.WriteInt32LittleEndian(text[2..], filter);
                WriteComment(text);
                target._filter = filter;
            }
            foreach (var (type, text) in _markers)
            {
                if (target._markers.TryGetValue(type, out byte[]? written) && written.AsSpan().SequenceEqual(text)) continue;
                WriteComment(text);
                target._markers[type] = text;
            }

            int pool = checked(_pool + poolOffset[kind == Kind.Network ? DsfFile.Pool32 : DsfFile.Pool16]);
            if (pool > ushort.MaxValue) throw new InvalidDataException("too many point pools to merge");
            if (pool != target._pool)
            {
                Write(1, BitConverter.GetBytes((ushort)pool));
                target._pool = pool;
            }
            int[] numbers = definitions[kind switch { Kind.Object => DsfFile.Objects, Kind.Network => DsfFile.Networks, _ => DsfFile.Polygons }];
            if (_definition >= numbers.Length) throw new InvalidDataException("a DSF command uses a definition the file does not have");
            int definition = numbers[_definition];
            if (definition != target._definition)
            {
                Write(5, BitConverter.GetBytes((uint)definition));
                target._definition = definition;
            }
            if (kind == Kind.Network && _subtype != target._subtype)
            {
                Write(6, [(byte)_subtype]);
                target._subtype = _subtype;
            }
            if (usesJunction && _junction != target._junction)
            {
                Write(2, BitConverter.GetBytes(_junction));
                target._junction = _junction;
            }
            foreach (byte b in command) target._commands.Add(b);
        }

        private void WriteComment(ReadOnlySpan<byte> text) => Write(32, [(byte)text.Length, .. text]);

        private void Write(byte command, ReadOnlySpan<byte> payload)
        {
            target._commands.Add(command);
            foreach (byte b in payload) target._commands.Add(b);
        }

        /// <summary>How many bytes follow the command byte, from the DSF specification.</summary>
        private static int PayloadLength(byte[] c, int at, byte command) => command switch
        {
            1 => 2,
            2 => 4,
            3 => 1,
            4 => 2,
            5 => 4,
            6 => 1,
            7 => 2,
            8 => 4,
            9 => 1 + 2 * c[at],
            10 => 4,
            11 => 1 + 4 * c[at],
            12 => 3 + 2 * c[at + 2],
            13 => 6,
            14 => NestedPolygonLength(c, at),
            15 => 3 + 2 * (c[at + 2] + 1),
            16 => 0,
            17 => 1,
            18 => 9,
            23 or 26 or 29 => 1 + 2 * c[at],
            24 or 27 or 30 => 1 + 4 * c[at],
            25 or 28 or 31 => 4,
            32 => 1 + c[at],
            33 => 2 + BinaryPrimitives.ReadUInt16LittleEndian(c.AsSpan(at)),
            34 => checked(4 + (int)BinaryPrimitives.ReadUInt32LittleEndian(c.AsSpan(at))),
            _ => throw new InvalidDataException($"unknown DSF command {command}"),
        };

        /// <summary>A nested polygon: parameter, number of windings, and each winding's count and 16-bit indices.</summary>
        private static int NestedPolygonLength(byte[] c, int at)
        {
            int length = 3, windings = c[at + 2];
            for (int w = 0; w < windings; w++) length += 1 + 2 * c[at + length];
            return length;
        }
    }
}
