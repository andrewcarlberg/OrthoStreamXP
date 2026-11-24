using System.Text;

namespace OrthoStreamXP.Setup;

/// <summary>
/// Lists the airports of an apt.dat (X-Plane's Global Airports one is close to 400 MB) and reads one airport's lines back. Only
/// the header and 1302 metadata lines are decoded; the rest is passed over as bytes.
/// </summary>
internal static class AptDatReader
{
    private const int BufferSize = 1 << 20;

    /// <summary>The airports in the file's order. A file that lists one airport twice lists both.</summary>
    public static IReadOnlyList<AirportRecord> Read(string file, CancellationToken cancel)
    {
        var airports = new List<AirportRecord>();
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.SequentialScan);
        var current = new PendingAirport();
        byte[] buffer = new byte[BufferSize];
        int filled = 0;
        long bufferOffset = 0; // the file offset of buffer[0]
        while (true)
        {
            cancel.ThrowIfCancellationRequested();
            int read = stream.Read(buffer, filled, buffer.Length - filled);
            filled += read;
            int start = 0;
            while (start < filled)
            {
                int newline = Array.IndexOf(buffer, (byte)'\n', start, filled - start);
                if (newline < 0 && read > 0) break; // the rest of this line is still to be read
                int end = newline < 0 ? filled : newline;
                OnLine(buffer.AsSpan(start, end - start), bufferOffset + start, current, airports);
                start = end + 1;
            }
            if (read == 0) break;
            Buffer.BlockCopy(buffer, start, buffer, 0, filled - start);
            bufferOffset += start;
            filled -= start;
            if (filled == buffer.Length) Array.Resize(ref buffer, buffer.Length * 2); // one line longer than the buffer
        }
        current.Finish(bufferOffset + filled, airports);
        return airports;
    }

    /// <summary>
    /// The lines of <paramref name="airport"/>, read from <paramref name="file"/> where <see cref="Read"/> found them. Throws an
    /// <see cref="IOException"/> when another airport is there now: the file changed since.
    /// </summary>
    public static AirportBlock ReadBlock(string file, AirportRecord airport)
    {
        byte[] bytes = new byte[airport.Length];
        using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            stream.Position = airport.Offset;
            stream.ReadExactly(bytes);
        }
        var block = new AirportBlock(Encoding.UTF8.GetString(bytes));
        if (!block.Ident.Equals(airport.Ident, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"{file} changed since it was read: press Refresh and try again.");
        return block;
    }

    private static void OnLine(ReadOnlySpan<byte> line, long offset, PendingAirport current, List<AirportRecord> airports)
    {
        if (line.EndsWith("\r"u8)) line = line[..^1];
        int code = RowCode(line);
        switch (code)
        {
            case AirportBlock.LandAirport or AirportBlock.SeaplaneBase or AirportBlock.Heliport:
                current.Finish(offset, airports);
                current.Start(Encoding.UTF8.GetString(line), offset);
                break;
            case AirportBlock.EndOfFile:
                current.Finish(offset, airports);
                break;
            case AirportBlock.Metadata1302 when current.IsOpen:
                current.Add(AirportBlock.Metadata(Encoding.UTF8.GetString(line)));
                break;
        }
    }

    /// <summary>The row code a line starts with (the number before the first blank), or -1.</summary>
    private static int RowCode(ReadOnlySpan<byte> line)
    {
        int code = 0, i = 0;
        for (; i < line.Length && line[i] is >= (byte)'0' and <= (byte)'9'; i++)
        {
            code = code * 10 + (line[i] - '0');
            if (code > 99_999) return -1;
        }
        return i > 0 && (i == line.Length || line[i] is (byte)' ' or (byte)'\t') ? code : -1;
    }

    /// <summary>The airport whose lines are being read: started at its header, written out at the next one.</summary>
    private sealed class PendingAirport
    {
        private string? _ident, _name, _city, _country;
        private bool _flattened;
        private long _offset;

        public bool IsOpen => _ident is not null;

        public void Start(string header, long offset)
        {
            (_ident, _name, _city, _country, _flattened, _offset) = (null, null, null, null, false, offset);
            if (AirportBlock.Header(header) is var (ident, name)) (_ident, _name) = (ident, name); // a header without an ident is skipped
        }

        public void Add((string Key, string Value)? metadata)
        {
            switch (metadata)
            {
                case ("city", var city):
                    _city = city;
                    break;
                case ("country", var country):
                    _country = country;
                    break;
                case ("flatten", var flatten):
                    _flattened = flatten == "1";
                    break;
            }
        }

        public void Finish(long end, List<AirportRecord> airports)
        {
            if (_ident is null) return;
            airports.Add(new AirportRecord(_ident, _name ?? "", _city, _country, _flattened, _offset, checked((int)(end - _offset))));
            _ident = null;
        }
    }
}
