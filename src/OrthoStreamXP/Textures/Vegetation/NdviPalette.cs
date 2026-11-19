using System.Globalization;
using System.Xml.Linq;

namespace OrthoStreamXP.Textures;

/// <summary>
/// The colours of NASA GIBS's vegetation index pictures and the greenness (NDVI) each stands for, from GIBS's colour
/// map (ndvi-colormap.xml, MODIS_L3_NDVI, used by its MODIS and VIIRS NDVI layers alike): a picture's colours are
/// classes 0.005 to 0.01 wide. Water, snow and bare rock (zero or below) count as 0; "no data" as nothing.
/// <para>
/// A place's greenness is kept as a byte (<see cref="CodeOf"/>): 0 for no data, 1 to 251 for 0 to 1 in steps of 0.004.
/// </para>
/// </summary>
internal static class NdviPalette
{
    /// <summary>The byte of a place without data.</summary>
    public const byte NoData = 0;

    private static readonly Lazy<Dictionary<int, double>> Colours = new(Load);

    /// <summary>The greenness a byte stands for (not for <see cref="NoData"/>).</summary>
    public static double ValueOf(byte code) => (code - 1) / 250.0;

    /// <summary>The byte of a greenness; below 0 counts as 0.</summary>
    public static byte CodeOf(double ndvi) => (byte)(1 + Math.Round(Math.Clamp(ndvi, 0, 1) * 250));

    /// <summary>The greenness of a picture's colour: false for "no data" and for a colour not in the map.</summary>
    public static bool TryValue(byte r, byte g, byte b, out double ndvi)
    {
        if (Colours.Value.TryGetValue(Key(r, g, b), out ndvi) && !double.IsNaN(ndvi)) return true;
        ndvi = 0;
        return false;
    }

    private static int Key(int r, int g, int b) => (r << 16) | (g << 8) | b;

    /// <summary>Colour to greenness, the middle of each class; NaN for "no data".</summary>
    private static Dictionary<int, double> Load()
    {
        using Stream stream = typeof(NdviPalette).Assembly.GetManifestResourceStream("OrthoStreamXP.ndvi-colormap.xml")
                              ?? throw new InvalidOperationException("The vegetation colour map is missing from the program");
        var colours = new Dictionary<int, double>();
        foreach (XElement entry in XDocument.Load(stream).Descendants("ColorMapEntry"))
        {
            int[] rgb = [.. ((string?)entry.Attribute("rgb") ?? "").Split(',').Select(c => int.Parse(c, CultureInfo.InvariantCulture))];
            if (rgb.Length != 3) continue;
            double value = (string?)entry.Attribute("nodata") == "true" ? double.NaN : Middle((string?)entry.Attribute("value") ?? "");
            colours[Key(rgb[0], rgb[1], rgb[2])] = value;
        }
        return colours;
    }

    /// <summary>The middle of a class such as "[0.0501,0.0551)", at least 0.</summary>
    private static double Middle(string range)
    {
        string[] ends = range.Trim('[', ']', '(', ')').Split(',');
        return ends.Length == 2
               && double.TryParse(ends[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double low)
               && double.TryParse(ends[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double high)
            ? Math.Max(0, (low + high) / 2)
            : double.NaN;
    }
}
