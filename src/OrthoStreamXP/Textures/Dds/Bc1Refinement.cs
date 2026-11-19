using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace OrthoStreamXP.Textures;

/// <summary>
/// Least-squares refinement of a BC1 block's two colours, as nvtt's fast mode does after its first fit: for the
/// palette colour each pixel chose, solve the two colours that come closest to the pixels, round them to 565, let
/// every pixel choose again, and keep the result only where the block's error went down. ISPC fits a block once;
/// two passes bring aerial textures 0.3-0.4 dB closer (cached Bing imagery, 4096 px).
/// <para>
/// Eight pixels at a time in <see cref="Vector256"/>: X-Plane 12's least CPUs have AVX2, and without it .NET computes
/// the same in software, only slower.
/// </para>
/// </summary>
internal static class Bc1Refinement
{
    /// <summary>Passes per block: the second adds about a quarter of the first's gain, a third almost nothing.</summary>
    public const int Passes = 2;

    /// <summary>Decoded mixes of two 5-bit values (<see cref="MixTable"/>).</summary>
    private static readonly byte[] Five = MixTable(5);

    /// <summary>Decoded mixes of two 6-bit values.</summary>
    private static readonly byte[] Six = MixTable(6);

    /// <summary>
    /// Refines a block in the opaque four-colour mode (left as it is in any other) against its 16 pixels,
    /// <paramref name="pixels"/> holding them as RGB, row by row. The block stays in the four-colour mode.
    /// </summary>
    [SkipLocalsInit]
    public static void Refine(Span<byte> block, ReadOnlySpan<byte> pixels)
    {
        if (pixels.Length < 48) throw new ArgumentException("A block has 16 RGB pixels", nameof(pixels));
        ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(block), c1 = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);
        if (c0 <= c1) return;
        var texels = Texels.From(pixels);
        var choice = Choice.From(BinaryPrimitives.ReadUInt32LittleEndian(block[4..]));
        Span<int> palette = stackalloc int[12];
        Palette(c0, c1, palette);
        int bestError = Error(texels, palette, choice);
        bool changed = false;
        for (int pass = 0; pass < Passes && bestError > 0; pass++)
        {
            if (!Solve(texels, choice, out int n0, out int n1)) break;
            Palette(n0, n1, palette);
            Choice next = Choose(texels, palette, out int error);
            if (error >= bestError) break;
            (bestError, c0, c1, choice, changed) = (error, (ushort)n0, (ushort)n1, next, true);
        }
        if (!changed) return;
        BinaryPrimitives.WriteUInt16LittleEndian(block, c0);
        BinaryPrimitives.WriteUInt16LittleEndian(block[2..], c1);
        BinaryPrimitives.WriteUInt32LittleEndian(block[4..], choice.Packed());
    }

    /// <summary>
    /// The two 565 colours (the larger first) that come closest, in least squares, to the pixels for their chosen
    /// palette colours; false where the choices cannot fix both (every pixel on one colour) or both round to one.
    /// </summary>
    private static bool Solve(in Texels texels, in Choice choice, out int n0, out int n1)
    {
        n0 = n1 = 0;
        // The normal equations in thirds: a pixel's share of the first colour is a / 3 and of the second (3 - a) / 3,
        // so aa, ab and bb are 9 times the real sums and the right-hand sides 3 times.
        Vector256<int> aLow = Share(choice.Low), aHigh = Share(choice.High), three = Vector256.Create(3);
        Vector256<int> bLow = three - aLow, bHigh = three - aHigh;
        long aa = Vector256.Sum(aLow * aLow + aHigh * aHigh), ab = Vector256.Sum(aLow * bLow + aHigh * bHigh), bb = Vector256.Sum(bLow * bLow + bHigh * bHigh);
        long det = aa * bb - ab * ab;
        if (det == 0) return false;
        long ar = Vector256.Sum(aLow * texels.R0 + aHigh * texels.R1), br = Vector256.Sum(bLow * texels.R0 + bHigh * texels.R1);
        long ag = Vector256.Sum(aLow * texels.G0 + aHigh * texels.G1), bg = Vector256.Sum(bLow * texels.G0 + bHigh * texels.G1);
        long ablue = Vector256.Sum(aLow * texels.B0 + aHigh * texels.B1), bblue = Vector256.Sum(bLow * texels.B0 + bHigh * texels.B1);
        double scale = 3.0 / det;
        n0 = Quantise((bb * ar - ab * br) * scale, (bb * ag - ab * bg) * scale, (bb * ablue - ab * bblue) * scale);
        n1 = Quantise((aa * br - ab * ar) * scale, (aa * bg - ab * ag) * scale, (aa * bblue - ab * ablue) * scale);
        if (n0 == n1) return false;
        if (n0 < n1) (n0, n1) = (n1, n0);
        return true;
    }

    /// <summary>Each palette index's share of the first colour, in thirds: 0 is 3, 1 is 0, 2 (2/3 : 1/3) is 2, 3 (1/3 : 2/3) is 1.</summary>
    private static Vector256<int> Share(Vector256<int> index) =>
        Vector256.ConditionalSelect(Vector256.Equals(index, Vector256<int>.Zero), Vector256.Create(3),
            Vector256.ConditionalSelect(Vector256.Equals(index, Vector256<int>.One), Vector256<int>.Zero, Vector256.Create(4) - index));

    /// <summary>The nearest 565 colour of a solved one (0-255 per channel, clamped).</summary>
    private static int Quantise(double r, double g, double b) =>
        (Level(r, 31) << 11) | (Level(g, 63) << 5) | Level(b, 31);

    private static int Level(double value, int max) => (int)(Math.Clamp(value, 0, 255) * max / 255 + 0.5);

    /// <summary>Each pixel's nearest palette colour (of equally near ones, the first) and the block's squared error with them.</summary>
    internal static Choice Choose(in Texels texels, ReadOnlySpan<int> palette, out int error)
    {
        var (lowDistance, lowIndex) = Nearest(texels.R0, texels.G0, texels.B0, palette);
        var (highDistance, highIndex) = Nearest(texels.R1, texels.G1, texels.B1, palette);
        error = Vector256.Sum(lowDistance + highDistance);
        return new Choice(lowIndex, highIndex);
    }

    private static (Vector256<int> Distance, Vector256<int> Index) Nearest(Vector256<int> r, Vector256<int> g, Vector256<int> b, ReadOnlySpan<int> palette)
    {
        Vector256<int> best = Distance(r, g, b, palette, 0), index = Vector256<int>.Zero;
        for (int k = 1; k < 4; k++)
        {
            Vector256<int> distance = Distance(r, g, b, palette, k);
            index = Vector256.ConditionalSelect(Vector256.LessThan(distance, best), Vector256.Create(k), index);
            best = Vector256.Min(best, distance);
        }
        return (best, index);
    }

    /// <summary>The block's squared error with its pixels' current choices.</summary>
    private static int Error(in Texels texels, ReadOnlySpan<int> palette, in Choice choice) =>
        Vector256.Sum(Chosen(texels.R0, texels.G0, texels.B0, palette, choice.Low) + Chosen(texels.R1, texels.G1, texels.B1, palette, choice.High));

    private static Vector256<int> Chosen(Vector256<int> r, Vector256<int> g, Vector256<int> b, ReadOnlySpan<int> palette, Vector256<int> index)
    {
        Vector256<int> distance = Distance(r, g, b, palette, 0);
        for (int k = 1; k < 4; k++)
            distance = Vector256.ConditionalSelect(Vector256.Equals(index, Vector256.Create(k)), Distance(r, g, b, palette, k), distance);
        return distance;
    }

    private static Vector256<int> Distance(Vector256<int> r, Vector256<int> g, Vector256<int> b, ReadOnlySpan<int> palette, int k)
    {
        Vector256<int> dr = r - Vector256.Create(palette[k * 3]), dg = g - Vector256.Create(palette[k * 3 + 1]), db = b - Vector256.Create(palette[k * 3 + 2]);
        return dr * dr + dg * dg + db * db;
    }

    /// <summary>The four-colour palette of two 565 colours, rounded as <see cref="Dds.DecodeLevel"/> decodes it.</summary>
    private static void Palette(int c0, int c1, Span<int> palette)
    {
        int r0 = c0 >> 11, g0 = (c0 >> 5) & 63, b0 = c0 & 31, r1 = c1 >> 11, g1 = (c1 >> 5) & 63, b1 = c1 & 31;
        palette[0] = Five[r0 * 33];
        palette[1] = Six[g0 * 65];
        palette[2] = Five[b0 * 33];
        palette[3] = Five[r1 * 33];
        palette[4] = Six[g1 * 65];
        palette[5] = Five[b1 * 33];
        palette[6] = Five[r0 * 32 + r1];
        palette[7] = Six[g0 * 64 + g1];
        palette[8] = Five[b0 * 32 + b1];
        palette[9] = Five[r1 * 32 + r0];
        palette[10] = Six[g1 * 64 + g0];
        palette[11] = Five[b1 * 32 + b0];
    }

    /// <summary>
    /// For a channel of <paramref name="bits"/> bits, at [a * levels + b] the decoded value of the mix (2 a + b) / 3,
    /// rounded from the exact values as <see cref="Dds.DecodeLevel"/> does; [a * levels + a] is a itself.
    /// </summary>
    private static byte[] MixTable(int bits)
    {
        int levels = 1 << bits;
        double max = levels - 1;
        var table = new byte[levels * levels];
        for (int a = 0; a < levels; a++)
        for (int b = 0; b < levels; b++)
        {
            double va = a * 255.0 / max, vb = b * 255.0 / max;
            table[a * levels + b] = (byte)Math.Round(a == b ? va : (2 * va + vb) / 3);
        }
        return table;
    }

    /// <summary>A block's 16 pixels as channel vectors of eight: pixels 0-7 and 8-15.</summary>
    internal readonly struct Texels
    {
        public Vector256<int> R0 { get; init; }
        public Vector256<int> G0 { get; init; }
        public Vector256<int> B0 { get; init; }
        public Vector256<int> R1 { get; init; }
        public Vector256<int> G1 { get; init; }
        public Vector256<int> B1 { get; init; }

        [SkipLocalsInit]
        public static Texels From(ReadOnlySpan<byte> pixels)
        {
            Span<int> channels = stackalloc int[48];
            for (int i = 0; i < 16; i++)
            {
                channels[i] = pixels[i * 3];
                channels[16 + i] = pixels[i * 3 + 1];
                channels[32 + i] = pixels[i * 3 + 2];
            }
            return new Texels
            {
                R0 = Vector256.Create<int>(channels[..8]), R1 = Vector256.Create<int>(channels[8..16]),
                G0 = Vector256.Create<int>(channels[16..24]), G1 = Vector256.Create<int>(channels[24..32]),
                B0 = Vector256.Create<int>(channels[32..40]), B1 = Vector256.Create<int>(channels[40..48]),
            };
        }
    }

    /// <summary>The palette index (0-3) each of a block's 16 pixels takes: pixels 0-7 and 8-15.</summary>
    internal readonly record struct Choice(Vector256<int> Low, Vector256<int> High)
    {
        /// <summary>Each lane's place in the packed indices: times 4^lane moves its index to its 2 bits.</summary>
        private static readonly Vector256<int> Place = Vector256.Create(1, 4, 16, 64, 256, 1024, 4096, 16384);

        [SkipLocalsInit]
        public static Choice From(uint indices)
        {
            Span<int> lanes = stackalloc int[16];
            for (int i = 0; i < 16; i++) lanes[i] = (int)(indices >> (2 * i)) & 3;
            return new Choice(Vector256.Create<int>(lanes[..8]), Vector256.Create<int>(lanes[8..]));
        }

        /// <summary>The indices as BC1 stores them, 2 bits a pixel from the lowest; the lanes' bits never overlap, so their sum is their OR.</summary>
        public uint Packed() => (uint)Vector256.Sum(Low * Place) | (uint)Vector256.Sum(High * Place) << 16;
    }
}
