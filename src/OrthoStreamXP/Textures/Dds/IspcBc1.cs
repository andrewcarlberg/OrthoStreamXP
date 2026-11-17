using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace OrthoStreamXP.Textures;

/// <summary>
/// BC1 encoding by Intel's ISPC Texture Compressor (<c>ispc_texcomp.dll</c>, MIT, built by native\ispc_texcomp\build.ps1):
/// on aerial textures 6-8 times faster than <see cref="Dds"/>'s own encoder (about twice with the refinement after it,
/// <see cref="Bc1Refinement"/>) and sharper. The DLL is loaded
/// from the program's folder once; without it <see cref="IsAvailable"/> is false and <see cref="Dds"/> encodes itself.
/// </summary>
internal static unsafe class IspcBc1
{
    private const string LibraryName = "ispc_texcomp.dll";
    /// <summary>Block rows converted to RGBA and encoded at a time: 32 pixel rows, 512 KB for a 4096 px level.</summary>
    private const int ChunkBlockRows = 8;

    private static readonly delegate* unmanaged[Cdecl]<Surface*, byte*, void> CompressBlocksBC1 = Load();
    /// <summary>For each 8-bit value, the 5-bit (6-bit for green) endpoint pair whose 2/3 : 1/3 mix comes closest to it.</summary>
    private static readonly (byte E0, byte E1)[] Match5 = SingleColourTable(5), Match6 = SingleColourTable(6);

    /// <summary>Whether the DLL was loaded; false where it is missing or not loadable.</summary>
    public static bool IsAvailable => CompressBlocksBC1 != null;

    /// <summary>
    /// Encodes an RGB image into BC1 blocks at <paramref name="offset"/> of <paramref name="output"/>, the same layout and
    /// edge handling (a partial block repeats its last row and column) as <see cref="Dds"/>'s own encoder, every block
    /// in the opaque four-colour mode. Returns the bytes written.
    /// </summary>
    public static int Encode(byte[] input, int width, int height, byte[] output, int offset, int workers)
    {
        if (!IsAvailable) throw new InvalidOperationException($"{LibraryName} is not loaded");
        int blocksX = Math.Max(1, (width + 3) / 4), blocksY = Math.Max(1, (height + 3) / 4), length = blocksX * blocksY * 8;
        if (input.Length < width * height * 3) throw new ArgumentException("The image is smaller than its size", nameof(input));
        if (offset < 0 || output.Length - offset < length) throw new ArgumentException("The output cannot hold the blocks", nameof(output));

        int chunks = (blocksY + ChunkBlockRows - 1) / ChunkBlockRows;
        void Chunk(int chunk)
        {
            int firstRow = chunk * ChunkBlockRows, rows = Math.Min(ChunkBlockRows, blocksY - firstRow);
            int paddedWidth = blocksX * 4, stride = paddedWidth * 4;
            byte[] rgba = ArrayPool<byte>.Shared.Rent(stride * rows * 4);
            try
            {
                ToRgba(input, width, height, firstRow * 4, rows * 4, rgba, paddedWidth);
                int at = offset + firstRow * blocksX * 8;
                fixed (byte* source = rgba, blocks = output)
                {
                    var surface = new Surface { Pixels = source, Width = paddedWidth, Height = rows * 4, Stride = stride };
                    CompressBlocksBC1(&surface, blocks + at);
                }
                Finish(output.AsSpan(at, rows * blocksX * 8), rgba, stride);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rgba);
            }
        }
        if (workers <= 1 || blocksY < 16)
            for (int chunk = 0; chunk < chunks; chunk++) Chunk(chunk);
        else
            Parallel.For(0, chunks, new ParallelOptions { MaxDegreeOfParallelism = workers }, Chunk);
        return length;
    }

    /// <summary>
    /// Pixel rows <paramref name="firstY"/> on of an RGB image as opaque RGBA, <paramref name="paddedWidth"/> wide; rows
    /// and columns beyond the image repeat its last ones.
    /// </summary>
    private static void ToRgba(byte[] input, int width, int height, int firstY, int rows, byte[] rgba, int paddedWidth)
    {
        for (int y = 0; y < rows; y++)
        {
            int sourceRow = Math.Min(height - 1, firstY + y) * width * 3, o = y * paddedWidth * 4;
            for (int x = 0; x < paddedWidth; x++, o += 4)
            {
                int i = sourceRow + Math.Min(width - 1, x) * 3;
                rgba[o] = input[i];
                rgba[o + 1] = input[i + 1];
                rgba[o + 2] = input[i + 2];
                rgba[o + 3] = 255;
            }
        }
    }

    /// <summary>
    /// After ISPC, for the blocks of a chunk (<paramref name="rgba"/> its pixels): a one-colour block gets the endpoint
    /// pair that mixes closest to its colour (ISPC lands up to a 565 step off: water and flat fields); every other block
    /// the opaque four-colour mode (first colour the larger), which the recolouring (<see cref="ColourTable"/>) expects,
    /// and then the least-squares refinement of its two colours (<see cref="Bc1Refinement"/>), which ISPC leaves out.
    /// </summary>
    private static void Finish(Span<byte> blocks, byte[] rgba, int stride)
    {
        int blocksX = stride / 16;
        Span<byte> pixels = stackalloc byte[48];
        for (int index = 0; index * 8 + 8 <= blocks.Length; index++)
        {
            Span<byte> block = blocks.Slice(index * 8, 8);
            int first = index / blocksX * 4 * stride + index % blocksX * 16;
            if (IsOneColour(rgba, first, stride))
            {
                WriteOneColour(block, rgba[first], rgba[first + 1], rgba[first + 2]);
                continue;
            }
            ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(block), c1 = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);
            if (c0 <= c1) ColourTable.WriteColors(block, 8, c0, c1);
            BlockPixels(rgba, first, stride, pixels);
            Bc1Refinement.Refine(block, pixels);
        }
    }

    /// <summary>A block's 16 pixels as RGB, row by row, from the RGBA rows of its chunk.</summary>
    private static void BlockPixels(byte[] rgba, int first, int stride, Span<byte> pixels)
    {
        for (int i = 0; i < 16; i++)
        {
            int at = first + (i >> 2) * stride + (i & 3) * 4;
            pixels[i * 3] = rgba[at];
            pixels[i * 3 + 1] = rgba[at + 1];
            pixels[i * 3 + 2] = rgba[at + 2];
        }
    }

    private static bool IsOneColour(byte[] rgba, int first, int stride)
    {
        // Alpha is always 255, so whole pixels compare as 32-bit words.
        uint colour = BinaryPrimitives.ReadUInt32LittleEndian(rgba.AsSpan(first));
        for (int y = 0; y < 4; y++)
        {
            ReadOnlySpan<uint> row = MemoryMarshal.Cast<byte, uint>(rgba.AsSpan(first + y * stride, 16));
            if (row[0] != colour || row[1] != colour || row[2] != colour || row[3] != colour) return false;
        }
        return true;
    }

    /// <summary>A block of one colour: every pixel the 2/3 : 1/3 mix of the best endpoint pair, in the four-colour mode.</summary>
    internal static void WriteOneColour(Span<byte> block, byte r, byte g, byte b)
    {
        var (r0, r1) = Match5[r];
        var (g0, g1) = Match6[g];
        var (b0, b1) = Match5[b];
        ushort c0 = (ushort)(r0 << 11 | g0 << 5 | b0), c1 = (ushort)(r1 << 11 | g1 << 5 | b1);
        // Every pixel index 2, the mix (2 c0 + c1) / 3. WriteColors puts the larger colour first, turning the indices to 3
        // (the same mix) where it swaps them, and makes a pair of equal colours index 0 of that colour.
        BinaryPrimitives.WriteUInt32LittleEndian(block[4..], 0xAAAAAAAAu);
        ColourTable.WriteColors(block, 8, c0, c1);
    }

    /// <summary>
    /// For every 8-bit value, the pair of <paramref name="bits"/>-bit endpoints (expanded by bit replication, as a GPU
    /// does) whose palette entry (2 e0 + e1) / 3 is closest; of equally close pairs, the nearest together.
    /// </summary>
    private static (byte E0, byte E1)[] SingleColourTable(int bits)
    {
        int levels = 1 << bits;
        var table = new (byte, byte)[256];
        for (int value = 0; value < 256; value++)
        {
            int bestError = int.MaxValue, bestSpread = int.MaxValue;
            for (int e0 = 0; e0 < levels; e0++)
            for (int e1 = 0; e1 < levels; e1++)
            {
                int mix = (2 * Expand(e0, bits) + Expand(e1, bits) + 1) / 3, error = Math.Abs(mix - value), spread = Math.Abs(e0 - e1);
                if (error < bestError || (error == bestError && spread < bestSpread))
                {
                    (bestError, bestSpread) = (error, spread);
                    table[value] = ((byte)e0, (byte)e1);
                }
            }
        }
        return table;

        static int Expand(int e, int bits) => e << (8 - bits) | e >> (2 * bits - 8);
    }

    private static delegate* unmanaged[Cdecl]<Surface*, byte*, void> Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, LibraryName);
        return NativeLibrary.TryLoad(path, out nint library) && NativeLibrary.TryGetExport(library, "CompressBlocksBC1", out nint export)
            ? (delegate* unmanaged[Cdecl]<Surface*, byte*, void>)export
            : null;
    }

    /// <summary>ISPC's <c>rgba_surface</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Surface
    {
        public byte* Pixels;
        public int Width;
        public int Height;
        public int Stride;
    }
}
