using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using OrthoStreamXP.Textures;

namespace OrthoStreamXP.Tests;

/// <summary>
/// Pictures as NASA GIBS sends its vegetation index: 8-bit palette PNGs in the colours of its colour map, the palette
/// index being the map's "ref", "no data" and the values below zero transparent.
/// </summary>
internal static class NdviPictures
{
    /// <summary>The colour map's entries: ref, colour, the middle of its class (NaN for "no data"), and whether it is transparent.</summary>
    public static readonly IReadOnlyList<(int Ref, byte R, byte G, byte B, double Value, bool Transparent)> Entries = Load();

    /// <summary>The ref of the class nearest a greenness of 0 to 1.</summary>
    public static int RefOf(double ndvi) =>
        Entries.Where(e => !double.IsNaN(e.Value) && !e.Transparent).MinBy(e => Math.Abs(e.Value - ndvi)).Ref;

    public const int NoData = 0;
    public const int Water = 1;

    /// <summary>A 512 x 512 picture, every pixel of class <paramref name="paint"/>(x, y).</summary>
    public static byte[] Picture(Func<int, int, int> paint, int size = VegetationTile.Pixels)
    {
        byte[] indices = new byte[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                indices[y * size + x] = (byte)paint(x, y);
        return Encode(indices, size, size);
    }

    /// <summary>A picture of one greenness everywhere.</summary>
    public static byte[] Uniform(double ndvi)
    {
        int index = RefOf(ndvi);
        return Picture((_, _) => index);
    }

    private static byte[] Encode(byte[] indices, int width, int height)
    {
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 3; // palette
        Chunk(png, "IHDR", header);
        int colours = Entries.Max(e => e.Ref) + 1;
        byte[] palette = new byte[colours * 3], alpha = new byte[colours];
        foreach (var e in Entries)
        {
            (palette[e.Ref * 3], palette[e.Ref * 3 + 1], palette[e.Ref * 3 + 2]) = (e.R, e.G, e.B);
            alpha[e.Ref] = e.Transparent ? (byte)0 : (byte)255;
        }
        Chunk(png, "PLTE", palette);
        Chunk(png, "tRNS", alpha);
        using var raw = new MemoryStream();
        using (var zlib = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
            for (int y = 0; y < height; y++)
            {
                zlib.WriteByte(0); // no filter
                zlib.Write(indices, y * width, width);
            }
        Chunk(png, "IDAT", raw.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream png, string type, byte[] data)
    {
        byte[] length = new byte[4], crc = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        png.Write(length);
        byte[] typed = [.. System.Text.Encoding.ASCII.GetBytes(type), .. data];
        png.Write(typed);
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typed));
        png.Write(crc);
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        }
        return ~crc;
    }

    private static List<(int, byte, byte, byte, double, bool)> Load()
    {
        using Stream stream = typeof(NdviPalette).Assembly.GetManifestResourceStream("OrthoStreamXP.ndvi-colormap.xml")!;
        var entries = new List<(int, byte, byte, byte, double, bool)>();
        foreach (XElement e in XDocument.Load(stream).Descendants("ColorMapEntry"))
        {
            byte[] rgb = [.. ((string)e.Attribute("rgb")!).Split(',').Select(c => byte.Parse(c, CultureInfo.InvariantCulture))];
            string[] ends = ((string)e.Attribute("value")!).Trim('[', ')').Split(',');
            double value = (string?)e.Attribute("nodata") == "true" ? double.NaN
                : Math.Max(0, (double.Parse(ends[0], CultureInfo.InvariantCulture) + double.Parse(ends[1], CultureInfo.InvariantCulture)) / 2);
            entries.Add((int.Parse((string)e.Attribute("ref")!, CultureInfo.InvariantCulture), rgb[0], rgb[1], rgb[2], value, (string?)e.Attribute("transparent") == "true"));
        }
        return entries;
    }
}
