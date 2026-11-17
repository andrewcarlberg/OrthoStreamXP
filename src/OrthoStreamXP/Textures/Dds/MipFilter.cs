namespace OrthoStreamXP.Textures;

/// <summary>
/// The next smaller mip level: each 2x2 average made in linear light, as the GPU filters an sRGB texture, not of the
/// stored values. Averaging the stored values darkens every level a little more (a bright and a dark pixel average to
/// a darker grey than the light they give): about 1 step of 255 at 1024 px, 3 at 256 px, 5 at 16 px of a 4096 px
/// photo texture, so the distant ground was darker than near. Integer tables, no floating point per pixel: about as
/// fast as the plain average.
/// </summary>
internal static class MipFilter
{
    private const int CoarseShift = 10;
    private const double Scale = 1 << 24;

    /// <summary>Linear light of each stored value (sRGB curve), scaled to 2^24.</summary>
    private static readonly uint[] ToLinear = [.. Enumerable.Range(0, 256).Select(c => (uint)Math.Round(Linear(c / 255.0) * Scale))];
    /// <summary>Where each stored value's rounding ends in linear light: the light of value c + 0.5.</summary>
    private static readonly uint[] Threshold =
        [.. Enumerable.Range(0, 256).Select(c => c == 255 ? uint.MaxValue : (uint)Math.Round(Linear((c + 0.5) / 255.0) * Scale))];
    /// <summary>The stored value at the start of each 2^10-wide stretch of linear light (the search starts there).</summary>
    private static readonly byte[] Coarse = CoarseTable();

    private static byte[] CoarseTable()
    {
        var table = new byte[(1 << 24 >> CoarseShift) + 1];
        int code = 0;
        for (int i = 0; i < table.Length; i++)
        {
            uint start = (uint)i << CoarseShift;
            while (code < 255 && start >= Threshold[code]) code++;
            table[i] = (byte)code;
        }
        return table;
    }

    /// <summary>The level below an RGB image, a new array; odd edges repeat their last row/column.</summary>
    public static byte[] Half(byte[] src, int w, int h, int workers)
    {
        byte[] dst = new byte[Math.Max(1, w >> 1) * Math.Max(1, h >> 1) * 3];
        HalfInto(src, w, h, dst, workers);
        return dst;
    }

    /// <summary><see cref="Half"/> into <paramref name="dst"/>, which may be longer than the half-size image (a pooled array).</summary>
    public static void HalfInto(byte[] src, int w, int h, byte[] dst, int workers)
    {
        int nw = Math.Max(1, w >> 1), nh = Math.Max(1, h >> 1);
        if (dst.Length < nw * nh * 3) throw new ArgumentException("The destination is smaller than the half-size image", nameof(dst));
        void Row(int ny)
        {
            int y0 = Math.Min(h - 1, ny * 2), y1 = Math.Min(h - 1, ny * 2 + 1);
            for (int nx = 0; nx < nw; nx++)
            {
                int x0 = Math.Min(w - 1, nx * 2), x1 = Math.Min(w - 1, nx * 2 + 1);
                int a = (y0 * w + x0) * 3, b = (y0 * w + x1) * 3, c = (y1 * w + x0) * 3, d = (y1 * w + x1) * 3, o = (ny * nw + nx) * 3;
                for (int k = 0; k < 3; k++)
                    dst[o + k] = Stored((ToLinear[src[a + k]] + ToLinear[src[b + k]] + ToLinear[src[c + k]] + ToLinear[src[d + k]] + 2) >> 2);
            }
        }
        if (workers <= 1 || nh < 64)
            for (int ny = 0; ny < nh; ny++) Row(ny);
        else
            Parallel.For(0, nh, new ParallelOptions { MaxDegreeOfParallelism = workers }, Row);
    }

    /// <summary>The stored value nearest (on the stored scale) to an amount of linear light.</summary>
    internal static byte Stored(uint linear)
    {
        int code = Coarse[linear >> CoarseShift];
        while (code < 255 && linear >= Threshold[code]) code++;
        return (byte)code;
    }

    /// <summary>The linear light of a stored value (sRGB curve, 0 to 1).</summary>
    internal static double Linear(double stored) => stored <= 0.04045 ? stored / 12.92 : Math.Pow((stored + 0.055) / 1.055, 2.4);
}
