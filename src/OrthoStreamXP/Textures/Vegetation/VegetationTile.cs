using System.Globalization;
using SkiaSharp;

namespace OrthoStreamXP.Textures;

/// <summary>
/// One tile of NASA GIBS's 1 km grid at its level 6 (EPSG:4326, 512 x 512 pixels): <see cref="Span"/> degrees square,
/// 40 rows from the North Pole, 80 columns from 180° West.
/// </summary>
internal readonly record struct VegetationKey(int Row, int Col)
{
    /// <summary>Degrees a tile spans: about 500 km north to south.</summary>
    public const double Span = 4.5;

    public double North => 90 - Row * Span;
    public double West => -180 + Col * Span;

    /// <summary>The tile a place is in.</summary>
    public static VegetationKey At(double lat, double lon) =>
        new((int)Math.Clamp(Math.Floor((90 - lat) / Span), 0, 180 / Span - 1), (int)Math.Clamp(Math.Floor((lon + 180) / Span), 0, 360 / Span - 1));

    /// <summary>"north-west corner 54.0° N, 18.0° E".</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"north-west corner {Math.Abs(North):0.0}° {(North >= 0 ? 'N' : 'S')}, {Math.Abs(West):0.0}° {(West >= 0 ? 'E' : 'W')}");
}

/// <summary>
/// A year of greenness (NDVI) over one <see cref="VegetationKey"/> tile: a month's picture averaged into
/// <see cref="Cells"/> x <see cref="Cells"/> cells of about 4 km, a byte each (<see cref="NdviPalette.CodeOf"/>), January first.
/// </summary>
internal sealed class VegetationTile
{
    /// <summary>Cells a side: 4 x 4 of the picture's pixels each, 3.9 km north to south.</summary>
    public const int Cells = 128;

    /// <summary>Pixels a side of NASA's picture.</summary>
    public const int Pixels = 512;

    /// <summary>Bytes of one month.</summary>
    public const int MonthBytes = Cells * Cells;

    private readonly byte[]?[] _months;

    /// <param name="months">Twelve months of <see cref="MonthBytes"/> bytes, January first; null for a month without data.</param>
    public VegetationTile(VegetationKey key, byte[]?[] months)
    {
        if (months.Length != 12) throw new ArgumentException("A year has twelve months", nameof(months));
        if (months.Any(m => m is not null && m.Length != MonthBytes)) throw new ArgumentException($"A month is {MonthBytes} bytes", nameof(months));
        Key = key;
        _months = months;
    }

    public VegetationKey Key { get; }

    /// <summary>The months that have data.</summary>
    public int Months => _months.Count(m => m is not null);

    /// <summary>
    /// The year at a place: its cell and the eight around it (about 12 km), each counted by how green it is at its greenest.
    /// A town or a lake is never very green, so a place in one takes the season of the country around it.
    /// Null where no cell has a year (the sea, or too many months missing).
    /// </summary>
    public GreennessYear? YearAt(double lat, double lon)
    {
        int cx = (int)Math.Clamp((lon - Key.West) / VegetationKey.Span * Cells, 0, Cells - 1);
        int cy = (int)Math.Clamp((Key.North - lat) / VegetationKey.Span * Cells, 0, Cells - 1);
        Span<double> weights = stackalloc double[12], sums = stackalloc double[12], cell = stackalloc double[12];
        weights.Clear();
        sums.Clear();
        for (int y = Math.Max(0, cy - 1); y <= Math.Min(Cells - 1, cy + 1); y++)
            for (int x = Math.Max(0, cx - 1); x <= Math.Min(Cells - 1, cx + 1); x++)
            {
                int missing = 0;
                double high = 0;
                for (int m = 0; m < 12; m++)
                {
                    byte code = _months[m]?[y * Cells + x] ?? NdviPalette.NoData;
                    cell[m] = code == NdviPalette.NoData ? double.NaN : NdviPalette.ValueOf(code);
                    if (double.IsNaN(cell[m])) missing++;
                    else high = Math.Max(high, cell[m]);
                }
                if (missing > GreennessYear.MaxMissing) continue;
                double weight = high * high + 1e-4;
                for (int m = 0; m < 12; m++)
                {
                    if (double.IsNaN(cell[m])) continue;
                    weights[m] += weight;
                    sums[m] += weight * cell[m];
                }
            }
        Span<double> year = stackalloc double[12];
        for (int m = 0; m < 12; m++) year[m] = weights[m] > 0 ? sums[m] / weights[m] : double.NaN;
        return GreennessYear.From(year);
    }

    /// <summary>
    /// A month's cells from NASA's picture (a 512 x 512 PNG in the colours of <see cref="NdviPalette"/>): each cell the mean
    /// of its 16 pixels that have data, or <see cref="NdviPalette.NoData"/> when fewer than 4 have.
    /// </summary>
    /// <exception cref="InvalidDataException">Not such a picture.</exception>
    public static byte[] CellsOf(byte[] png)
    {
        using var data = SKData.CreateCopy(png);
        using SKCodec codec = SKCodec.Create(data) ?? throw new InvalidDataException("Not a decodable picture");
        if (codec.Info.Width != Pixels || codec.Info.Height != Pixels)
            throw new InvalidDataException($"A vegetation picture is {Pixels} x {Pixels} pixels, not {codec.Info.Width} x {codec.Info.Height}");
        // Unpremultiplied: the transparent colours (water and snow, "no data") keep their colour, which says which they are.
        var info = new SKImageInfo(Pixels, Pixels, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        SKCodecResult result = codec.GetPixels(info, bitmap.GetPixels());
        if (result != SKCodecResult.Success) throw new InvalidDataException($"Vegetation picture decode failed: {result}");
        ReadOnlySpan<byte> rgba = bitmap.GetPixelSpan();
        byte[] cells = new byte[MonthBytes];
        const int step = Pixels / Cells;
        for (int cy = 0; cy < Cells; cy++)
            for (int cx = 0; cx < Cells; cx++)
            {
                double sum = 0;
                int count = 0;
                for (int py = cy * step; py < (cy + 1) * step; py++)
                    for (int px = cx * step; px < (cx + 1) * step; px++)
                    {
                        int at = (py * Pixels + px) * 4;
                        if (!NdviPalette.TryValue(rgba[at], rgba[at + 1], rgba[at + 2], out double ndvi)) continue;
                        sum += ndvi;
                        count++;
                    }
                cells[cy * Cells + cx] = count >= 4 ? NdviPalette.CodeOf(sum / count) : NdviPalette.NoData;
            }
        return cells;
    }
}
