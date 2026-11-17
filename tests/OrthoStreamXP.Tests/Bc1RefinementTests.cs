using System.Buffers.Binary;
using OrthoStreamXP.Textures;

namespace OrthoStreamXP.Tests;

public sealed class Bc1RefinementTests
{
    [Fact]
    public void Refining_never_makes_a_block_worse_and_brings_the_whole_closer()
    {
        const int size = 128;
        byte[] rgb = PhotoLike(size);
        byte[] blocks = RangeFit(rgb, size);
        byte[] refined = (byte[])blocks.Clone();

        int blocksX = size / 4;
        for (int b = 0; b < blocksX * blocksX; b++)
        {
            byte[] pixels = BlockPixels(rgb, size, b % blocksX, b / blocksX);
            Span<byte> block = refined.AsSpan(b * 8, 8);
            Bc1Refinement.Refine(block, pixels);
            Assert.True(BlockError(block, pixels) <= BlockError(blocks.AsSpan(b * 8, 8), pixels), $"block {b} got worse");
        }

        double before = Psnr(blocks, rgb, size), after = Psnr(refined, rgb, size);
        Assert.True(after > before + 0.2, $"range fit {before:F2} dB, refined {after:F2} dB");
    }

    [Fact]
    public void Refined_blocks_stay_in_the_opaque_four_colour_mode()
    {
        const int size = 64;
        byte[] rgb = PhotoLike(size);
        byte[] blocks = RangeFit(rgb, size);

        for (int b = 0; b < blocks.Length / 8; b++)
        {
            Span<byte> block = blocks.AsSpan(b * 8, 8);
            Bc1Refinement.Refine(block, BlockPixels(rgb, size, b % (size / 4), b / (size / 4)));
            Assert.True(BinaryPrimitives.ReadUInt16LittleEndian(block) > BinaryPrimitives.ReadUInt16LittleEndian(block[2..]), $"block {b}");
        }
    }

    [Fact]
    public void A_block_of_two_565_colours_finds_them_exactly()
    {
        // Half the pixels one colour 565 holds exactly, half another; the block starts with both colours a few steps
        // off but each pixel already on the right one (index 0 or 1), so one pass solves them exactly.
        int a = (20 << 11) | (40 << 5) | 10, b = (5 << 11) | (12 << 5) | 25;
        byte[] pixels = new byte[48];
        for (int i = 0; i < 16; i++) Put(pixels, i, i < 8 ? a : b);
        byte[] block = Block((22 << 11) | (37 << 5) | 12, (6 << 11) | (15 << 5) | 23, 0x5555_0000u);

        Bc1Refinement.Refine(block, pixels);

        Assert.Equal(0, BlockError(block, pixels));
        Assert.Equal(a, BinaryPrimitives.ReadUInt16LittleEndian(block));
        Assert.Equal(b, BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(2)));
    }

    [Fact]
    public void A_block_that_is_already_exact_is_left_as_it_is()
    {
        byte[] block = Block((25 << 11) | (50 << 5) | 20, (3 << 11) | (9 << 5) | 4, 0xE4E4_1B1Bu);
        byte[] pixels = Decoded(block);
        byte[] before = (byte[])block.Clone();

        Bc1Refinement.Refine(block, pixels);

        Assert.Equal(before, block);
    }

    [Fact]
    public void A_block_whose_pixels_all_take_one_colour_is_left_as_it_is()
    {
        // Every index 2: the normal equations cannot fix two colours from one mix.
        byte[] pixels = new byte[48];
        for (int i = 0; i < 16; i++) (pixels[i * 3], pixels[i * 3 + 1], pixels[i * 3 + 2]) = ((byte)(100 + i), 90, 80);
        byte[] block = Block((15 << 11) | (25 << 5) | 12, (10 << 11) | (20 << 5) | 8, 0xAAAA_AAAAu);
        byte[] before = (byte[])block.Clone();

        Bc1Refinement.Refine(block, pixels);

        Assert.Equal(before, block);
    }

    [Fact]
    public void A_three_colour_block_is_left_as_it_is()
    {
        byte[] block = Block(100, 2000, 0x1234_5678u);
        byte[] before = (byte[])block.Clone();

        Bc1Refinement.Refine(block, new byte[48]);

        Assert.Equal(before, block);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(0xFFFF_FFFFu)]
    [InlineData(0x1B1B_E4E4u)]
    [InlineData(0x8000_0001u)]
    public void A_blocks_indices_survive_the_vector_lanes(uint indices)
    {
        Assert.Equal(indices, Bc1Refinement.Choice.From(indices).Packed());
    }

    [Fact]
    public void Fewer_than_16_pixels_are_refused()
    {
        byte[] block = Block(2000, 100, 0);
        Assert.Throws<ArgumentException>(() => Bc1Refinement.Refine(block, new byte[47]));
    }

    /// <summary>A more photo-like picture than the synthetic tile's per-pixel noise: the tile shrunk, which averages it.</summary>
    private static byte[] PhotoLike(int size) => Images.Resize(Images.SyntheticTile(7, 9, 14), 256, 256, size, size, 1);

    private static byte[] RangeFit(byte[] rgb, int size)
    {
        byte[] output = new byte[Dds.LevelLength(size)];
        Dds.EncodeRangeFit(rgb, size, size, output, 0, 1);
        return output;
    }

    private static byte[] BlockPixels(byte[] rgb, int size, int bx, int by)
    {
        byte[] pixels = new byte[48];
        for (int i = 0; i < 16; i++)
            rgb.AsSpan(((by * 4 + (i >> 2)) * size + bx * 4 + (i & 3)) * 3, 3).CopyTo(pixels.AsSpan(i * 3));
        return pixels;
    }

    private static byte[] Block(int c0, int c1, uint indices)
    {
        byte[] block = new byte[8];
        BinaryPrimitives.WriteUInt16LittleEndian(block, (ushort)c0);
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(2), (ushort)c1);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(4), indices);
        return block;
    }

    private static void Put(byte[] pixels, int i, int c)
    {
        var (r, g, b) = ColourTable.Decode565(c);
        (pixels[i * 3], pixels[i * 3 + 1], pixels[i * 3 + 2]) = ((byte)Math.Round(r), (byte)Math.Round(g), (byte)Math.Round(b));
    }

    private static byte[] Decoded(ReadOnlySpan<byte> block) => Dds.DecodeLevel(block, 4, 4);

    private static int BlockError(ReadOnlySpan<byte> block, byte[] pixels)
    {
        byte[] decoded = Decoded(block);
        int total = 0;
        for (int i = 0; i < 48; i++) total += (decoded[i] - pixels[i]) * (decoded[i] - pixels[i]);
        return total;
    }

    private static double Psnr(byte[] blocks, byte[] source, int size)
    {
        byte[] decoded = Dds.DecodeLevel(blocks, size, size);
        double squares = 0;
        for (int i = 0; i < decoded.Length; i++) squares += (decoded[i] - source[i]) * (double)(decoded[i] - source[i]);
        return squares == 0 ? 99 : 10 * Math.Log10(255.0 * 255.0 * decoded.Length / squares);
    }
}
