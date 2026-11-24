namespace OrthoStreamXP.Setup;

/// <summary>
/// One airport's lines of an apt.dat: its header (row 1, 16 or 17), its 1302 metadata, and its runways, taxiways and the rest.
/// The lines are kept as they are, without the blank lines around them.
/// </summary>
internal sealed class AirportBlock
{
    public const int LandAirport = 1, SeaplaneBase = 16, Heliport = 17, Metadata1302 = 1302, EndOfFile = 99;
    private const string FlattenLine = "1302 flatten 1";

    private readonly string[] _lines;

    public AirportBlock(string text)
    {
        _lines = text.ReplaceLineEndings("\n").Trim('\n').Split('\n').Select(l => l.TrimEnd()).ToArray();
        if (Header(_lines[0]) is not var (ident, name)) throw new FormatException($"Not an airport header: {_lines[0]}");
        (Ident, Name) = (ident, name);
    }

    private AirportBlock(string[] lines, string ident, string name) => (_lines, Ident, Name) = (lines, ident, name);

    public string Ident { get; }
    public string Name { get; }

    /// <summary>The lines, ending with a line break.</summary>
    public string Text => string.Join("\r\n", _lines) + "\r\n";

    /// <summary>Whether the airport asks X-Plane to flatten the terrain under it (1302 flatten 1).</summary>
    public bool IsFlattened => _lines.Any(line => Metadata(line) is ("flatten", "1"));

    /// <summary>
    /// The same airport asking X-Plane to flatten it: an existing flatten line is set to 1, or one is added after the header's
    /// metadata lines.
    /// </summary>
    public AirportBlock Flattened()
    {
        var lines = new List<string>(_lines);
        int existing = lines.FindIndex(line => Metadata(line) is ("flatten", _));
        if (existing >= 0)
        {
            lines[existing] = FlattenLine;
        }
        else
        {
            int at = 1;
            while (at < lines.Count && Metadata(lines[at]) is not null) at++;
            lines.Insert(at, FlattenLine);
        }
        return new AirportBlock([.. lines], Ident, Name);
    }

    /// <summary>
    /// The area its runways, helipads, pavement, start positions, signs and datum span, in degrees (south, west, north, east);
    /// null when no line places it.
    /// </summary>
    public (double South, double West, double North, double East)? Area()
    {
        double south = double.MaxValue, west = double.MaxValue, north = double.MinValue, east = double.MinValue;
        void Add(string lat, string lon)
        {
            if (!double.TryParse(lat, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double y)
                || !double.TryParse(lon, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double x)
                || Math.Abs(y) > 90 || Math.Abs(x) > 180) return;
            (south, north, west, east) = (Math.Min(south, y), Math.Max(north, y), Math.Min(west, x), Math.Max(east, x));
        }

        string? datumLat = null, datumLon = null;
        foreach (string line in _lines)
        {
            string[] p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) continue;
            switch (p[0])
            {
                case "100" when p.Length > 19: Add(p[9], p[10]); Add(p[18], p[19]); break; // runway ends
                case "101" when p.Length > 8: Add(p[4], p[5]); Add(p[7], p[8]); break; // water runway ends
                case "102" when p.Length > 3: Add(p[2], p[3]); break; // helipad
                case "14" or "15" or "19" or "20" or "1300" or "111" or "112" or "113" or "114" or "115" or "116" when p.Length > 2:
                    Add(p[1], p[2]); break; // viewpoint, start, windsock, sign, start location, pavement nodes
                case "21" when p.Length > 3: Add(p[2], p[3]); break; // lighting
                case "1302" when p.Length > 2 && p[1] == "datum_lat": datumLat = p[2]; break;
                case "1302" when p.Length > 2 && p[1] == "datum_lon": datumLon = p[2]; break;
            }
        }
        if (datumLat is not null && datumLon is not null) Add(datumLat, datumLon);
        return south <= north ? (south, west, north, east) : null;
    }

    /// <summary>Whether both hold the same lines.</summary>
    public bool SameAs(AirportBlock other) => _lines.SequenceEqual(other._lines, StringComparer.Ordinal);

    /// <summary>The airports of an apt.dat's text, in order; the file's own header and end line are left out.</summary>
    public static IReadOnlyList<AirportBlock> Split(string text)
    {
        var blocks = new List<AirportBlock>();
        List<string>? current = null;
        foreach (string line in text.ReplaceLineEndings("\n").Split('\n'))
        {
            int code = RowCode(line);
            if (code is LandAirport or SeaplaneBase or Heliport or EndOfFile)
            {
                Add(current, blocks);
                current = code == EndOfFile ? null : [];
            }
            current?.Add(line);
        }
        Add(current, blocks);
        return blocks;

        static void Add(List<string>? lines, List<AirportBlock> blocks)
        {
            if (lines is not null && Header(lines[0]) is not null) blocks.Add(new AirportBlock(string.Join('\n', lines)));
        }
    }

    /// <summary>The ident and name of an airport header line ("1    145 0 0 EBBR Brussels National"), or null.</summary>
    public static (string Ident, string Name)? Header(string line)
    {
        string[] parts = line.Split((char[]?)null, 6, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 5 || RowCode(line) is not (LandAirport or SeaplaneBase or Heliport)) return null;
        return (parts[4], parts.Length > 5 ? parts[5] : "");
    }

    /// <summary>The key and value of a 1302 line ("1302 city Brussels"), or null for any other line.</summary>
    public static (string Key, string Value)? Metadata(string line)
    {
        string[] parts = line.Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 && parts[0] == "1302" ? (parts[1], parts.Length > 2 ? parts[2] : "") : null;
    }

    private static int RowCode(string line)
    {
        int blank = line.IndexOfAny([' ', '\t']);
        return int.TryParse(blank < 0 ? line : line[..blank], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int code) ? code : -1;
    }
}
