using System.Buffers.Binary;
using OrthoStreamXP.Textures;

namespace OrthoStreamXP.Tests;

public sealed class Bc1EncoderTests
{
    [Fact]
    public void Ispc_is_loaded_from_the_program_folder()
    {
        Assert.True(IspcBc1.IsAvailable, "ispc_texcomp.dll did not load from the output folder");
        Assert.Equal("ispc", Dds.Encoder);
    }

    [Fact]
    public void Ispc_keeps_the_quality_of_the_range_fit()
    {
        // On real aerial photos ISPC is 0.1-0.4 dB closer (native\ispc_texcomp\README.md); the synthetic tile's
        // per-pixel noise is no photo, so here it only has to stay about level with the range fit.
        const int size = 256;
        byte[] rgb = Images.SyntheticTile(1234, 5678, 16);

        double ispc = Psnr(Ispc(rgb, size, size, 1), rgb, size, size);
        double rangeFit = Psnr(RangeFit(rgb, size, size), rgb, size, size);

        Assert.True(ispc >= rangeFit - 0.5, $"ISPC {ispc:F2} dB, range fit {rangeFit:F2} dB");
        Assert.True(ispc > 30, $"ISPC {ispc:F2} dB");
    }

    [Fact]
    public void Every_block_is_in_the_opaque_four_colour_mode()
    {
        // Flat areas (a colour 565 holds exactly and one it does not), a gradient and photo-like noise.
        const int size = 64;
        byte[] rgb = new byte[size * size * 3];
        byte[] noise = Images.SyntheticTile(10, 20, 15);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int i = (y * size + x) * 3;
            (rgb[i], rgb[i + 1], rgb[i + 2]) = (x / 16) switch
            {
                0 => ((byte)255, (byte)0, (byte)255),
                1 => ((byte)90, (byte)121, (byte)57),
                2 => ((byte)(x * 4), (byte)(y * 4), (byte)128),
                _ => (noise[i], noise[i + 1], noise[i + 2]),
            };
        }

        byte[] blocks = Ispc(rgb, size, size, 1);

        for (int at = 0; at < blocks.Length; at += 8)
        {
            ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(blocks.AsSpan(at)), c1 = BinaryPrimitives.ReadUInt16LittleEndian(blocks.AsSpan(at + 2));
            Assert.True(c0 > c1, $"block {at / 8}: colours {c0} and {c1}");
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 5)]
    [InlineData(8, 4)]
    [InlineData(13, 7)]
    public void Sizes_off_the_block_grid_repeat_their_edges_like_the_range_fit(int width, int height)
    {
        byte[] flat = new byte[width * height * 3];
        for (int i = 0; i < flat.Length; i += 3) (flat[i], flat[i + 1], flat[i + 2]) = (255, 0, 255);
        byte[] gradient = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = (y * width + x) * 3;
            (gradient[i], gradient[i + 1], gradient[i + 2]) = ((byte)(40 + x * 120 / width), (byte)(60 + y * 100 / height), 128);
        }

        byte[] flatBlocks = Ispc(flat, width, height, 1);
        Assert.Equal(RangeFit(flat, width, height).Length, flatBlocks.Length);
        Assert.Equal(flat, Dds.DecodeLevel(flatBlocks, width, height));
        double ispc = Psnr(Ispc(gradient, width, height, 1), gradient, width, height);
        double rangeFit = Psnr(RangeFit(gradient, width, height), gradient, width, height);
        Assert.True(ispc >= rangeFit - 0.5, $"{width}x{height}: ISPC {ispc:F2} dB, range fit {rangeFit:F2} dB");
    }

    [Theory]
    [InlineData(40, 60, 128)]
    [InlineData(18, 52, 77)] // dark water
    [InlineData(201, 203, 199)]
    [InlineData(0, 0, 0)]
    [InlineData(255, 255, 255)]
    public void A_one_colour_block_lands_within_a_step_of_its_colour(byte r, byte g, byte b)
    {
        byte[] rgb = new byte[8 * 8 * 3];
        for (int i = 0; i < rgb.Length; i += 3) (rgb[i], rgb[i + 1], rgb[i + 2]) = (r, g, b);

        byte[] blocks = Ispc(rgb, 8, 8, 1);
        byte[] decoded = Dds.DecodeLevel(blocks, 8, 8);
        byte[] rangeFit = Dds.DecodeLevel(RangeFit(rgb, 8, 8), 8, 8);

        for (int i = 0; i < rgb.Length; i++)
        {
            Assert.True(Math.Abs(decoded[i] - rgb[i]) <= 2, $"channel {i % 3}: {decoded[i]} for {rgb[i]}");
            Assert.True(Math.Abs(decoded[i] - rgb[i]) <= Math.Abs(rangeFit[i] - rgb[i]), $"channel {i % 3}: {decoded[i]}, range fit {rangeFit[i]}, for {rgb[i]}");
        }
        for (int at = 0; at < blocks.Length; at += 8)
            Assert.True(BinaryPrimitives.ReadUInt16LittleEndian(blocks.AsSpan(at)) > BinaryPrimitives.ReadUInt16LittleEndian(blocks.AsSpan(at + 2)) || (r, g, b) == (0, 0, 0));
    }

    [Fact]
    public void The_number_of_workers_changes_nothing()
    {
        const int size = 512;
        byte[] rgb = Images.Resize(Images.SyntheticTile(3, 4, 14), 256, 256, size, size, 1);

        Assert.Equal(Ispc(rgb, size, size, 1), Ispc(rgb, size, size, 8));
    }

    [Fact]
    public void A_short_output_is_refused_before_the_native_code_writes()
    {
        byte[] rgb = new byte[16 * 16 * 3];
        Assert.Throws<ArgumentException>(() => IspcBc1.Encode(rgb, 16, 16, new byte[16 * 8 - 1], 0, 1));
        Assert.Throws<ArgumentException>(() => IspcBc1.Encode(rgb, 16, 16, new byte[16 * 8], 8, 1));
    }

    private static byte[] Ispc(byte[] rgb, int width, int height, int workers)
    {
        byte[] output = new byte[Dds.LevelLength(1) * Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4)];
        IspcBc1.Encode(rgb, width, height, output, 0, workers);
        return output;
    }

    private static byte[] RangeFit(byte[] rgb, int width, int height)
    {
        byte[] output = new byte[8 * Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4)];
        Dds.EncodeRangeFit(rgb, width, height, output, 0, 1);
        return output;
    }

    private static double Psnr(byte[] blocks, byte[] source, int width, int height)
    {
        byte[] decoded = Dds.DecodeLevel(blocks, width, height);
        double squares = 0;
        for (int i = 0; i < decoded.Length; i++)
        {
            double d = decoded[i] - source[i];
            squares += d * d;
        }
        return squares == 0 ? 99 : 10 * Math.Log10(255.0 * 255.0 * decoded.Length / squares);
    }
}
