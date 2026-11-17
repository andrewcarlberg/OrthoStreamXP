using System.Buffers;

namespace OrthoStreamXP.Textures;

/// <summary>
/// The sharpening of the photos as a texture is built, before its smaller levels and BC1: an unsharp mask on the
/// brightness (a Gaussian of 1 px), its change added to red, green and blue alike so edges get no colour fringes. Fixed
/// at 60 % of a scale whose 100 % is an amount of 1.5: roofs, field edges and tree crowns crisper, no visible halos
/// (the samples of 2026-11-18). Integer arithmetic, in place, in bands of rows.
/// </summary>
internal static class Sharpening
{
    /// <summary>The strength in use, of the scale whose 100 % is an unsharp-mask amount of 1.5.</summary>
    public const double Strength = 0.6;

    /// <summary>The amount (1.5 x <see cref="Strength"/> = 0.9) in 1/256.</summary>
    private const int Amount = 230;

    /// <summary>The Gaussian (sigma 1 px) over 7 pixels, in 1/1024.</summary>
    private static readonly int[] Kernel = [5, 55, 248, 408, 248, 55, 5];

    private const int Radius = 3, Taps = 2 * Radius + 1, KernelShift = 10;

    /// <summary>Rows per band a worker sharpens at a time.</summary>
    private const int BandRows = 64;

    /// <summary>Sharpens a tightly packed RGB image in place.</summary>
    public static void Apply(byte[] rgb, int width, int height, int workers)
    {
        if (rgb.Length < width * height * 3) throw new ArgumentException("The image is smaller than its size", nameof(rgb));
        if (width < 1 || height < 1) return;
        byte[] luma = ArrayPool<byte>.Shared.Rent(width * height);
        try
        {
            // The brightness of every pixel first: a band changes its own rows while its neighbours still read theirs.
            int bands = (height + BandRows - 1) / BandRows;
            ForEach(bands, workers, band =>
            {
                for (int y = band * BandRows, end = Math.Min(height, y + BandRows); y < end; y++)
                for (int x = 0, i = y * width; x < width; x++, i++)
                    luma[i] = (byte)((54 * rgb[i * 3] + 183 * rgb[i * 3 + 1] + 19 * rgb[i * 3 + 2] + 128) >> 8);
            });
            ForEach(bands, workers, band => Band(rgb, luma, width, height, band * BandRows, Math.Min(height, (band + 1) * BandRows)));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(luma);
        }
    }

    private static void ForEach(int count, int workers, Action<int> body)
    {
        if (workers <= 1 || count == 1)
            for (int i = 0; i < count; i++) body(i);
        else
            Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = workers }, body);
    }

    /// <summary>Rows <paramref name="first"/> to <paramref name="end"/>: the vertical pass over a ring of 7 horizontally blurred rows.</summary>
    private static void Band(byte[] rgb, byte[] luma, int width, int height, int first, int end)
    {
        int[] ring = ArrayPool<int>.Shared.Rent(Taps * width);
        Span<int> held = stackalloc int[Taps];
        held.Fill(-1);
        try
        {
            for (int y = first; y < end; y++)
            {
                for (int d = -Radius; d <= Radius; d++)
                {
                    int source = Math.Clamp(y + d, 0, height - 1), slot = source % Taps;
                    if (held[slot] == source) continue;
                    Horizontal(luma.AsSpan(source * width, width), ring.AsSpan(slot * width, width));
                    held[slot] = source;
                }
                Sharpen(rgb, luma, ring, width, height, y);
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(ring);
        }
    }

    /// <summary>One row of brightness blurred along the row, in 1/1024; the edges repeat their last pixel.</summary>
    private static void Horizontal(ReadOnlySpan<byte> row, Span<int> blurred)
    {
        int width = row.Length;
        for (int x = 0; x < width; x++)
        {
            int sum = 0;
            if (x >= Radius && x < width - Radius)
                for (int k = 0; k < Taps; k++) sum += Kernel[k] * row[x - Radius + k];
            else
                for (int k = 0; k < Taps; k++) sum += Kernel[k] * row[Math.Clamp(x - Radius + k, 0, width - 1)];
            blurred[x] = sum;
        }
    }

    /// <summary>Row <paramref name="y"/>: each pixel moved away from its blurred brightness by the amount, red, green and blue alike.</summary>
    private static void Sharpen(byte[] rgb, byte[] luma, int[] ring, int width, int height, int y)
    {
        Span<int> offsets = stackalloc int[Taps];
        for (int d = -Radius; d <= Radius; d++) offsets[d + Radius] = Math.Clamp(y + d, 0, height - 1) % Taps * width;
        int rowStart = y * width;
        for (int x = 0; x < width; x++)
        {
            int blurred = 0; // in 1/2^20
            for (int k = 0; k < Taps; k++) blurred += Kernel[k] * ring[offsets[k] + x];
            int difference = (luma[rowStart + x] << (2 * KernelShift)) - blurred;
            int delta = (int)(((long)difference * Amount + (1L << (2 * KernelShift + 7))) >> (2 * KernelShift + 8));
            if (delta == 0) continue;
            int at = (rowStart + x) * 3;
            rgb[at] = Clamp(rgb[at] + delta);
            rgb[at + 1] = Clamp(rgb[at + 1] + delta);
            rgb[at + 2] = Clamp(rgb[at + 2] + delta);
        }
    }

    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);
}
