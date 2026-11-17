using OrthoStreamXP.Scenery;
using OrthoStreamXP.Textures;

namespace OrthoStreamXP.Tests;

/// <summary>Builds keep downloaded tiles as their image bytes and decode them into reused buffers, with the same pixels.</summary>
public sealed class TileMemoryTests
{
    private static readonly TextureName Name = new(21904, 35824, "BI", 16);

    [Theory]
    [InlineData(4096)] // each tile as it is
    [InlineData(1024)] // each tile shrunk twice (256 -> 64 px)
    public async Task The_stitched_region_has_each_tile_decoded_on_its_own(int size)
    {
        // One tile comes at 512 px, which is resized to a tile first.
        bool Large(int x, int y, int z) => x == Name.Col + 7 && y == Name.Row + 2;
        using var tiles = new TileSource("test", PhotoTileServer.Template, 8, handler: new PhotoTileServer(large: Large));

        var (rgb, regionSize) = await new TextureBuilder(tiles, 2).FetchRegionAsync(Name, 16, size, new FetchTicket(FetchPriority.Warm), new TileStats());

        Assert.Equal(size, regionSize);
        int tilePx = size / Tiles.TextureSpan;
        byte[] expected = new byte[size * size * 3];
        for (int row = 0; row < Tiles.TextureSpan; row++)
        for (int column = 0; column < Tiles.TextureSpan; column++)
        {
            int x = Name.Col + column, y = Name.Row + row;
            byte[] tile = Images.DecodeTile(PhotoTileServer.Jpeg(x, y, 16, Large(x, y, 16)));
            for (int px = Tiles.TilePx; px > tilePx; px >>= 1) tile = MipFilter.Half(tile, px, px, 1);
            for (int line = 0; line < tilePx; line++)
                Buffer.BlockCopy(tile, line * tilePx * 3, expected, ((row * tilePx + line) * size + column * tilePx) * 3, tilePx * 3);
        }
        Assert.True(expected.AsSpan().SequenceEqual(rgb), "The stitched region differs from its tiles decoded one by one");
    }

    [Fact]
    public async Task A_tile_that_does_not_decode_is_rejected_when_downloaded()
    {
        using var tiles = new TileSource("test", PhotoTileServer.Template, 1, attempts: 1, handler: new BrokenTileServer());

        await Assert.ThrowsAsync<IOException>(() => tiles.GetSourceTileAsync(1, 2, 3, new FetchTicket(FetchPriority.Warm), new TileStats()));
    }

    [Fact]
    public void Header_and_levels_are_written_as_one_file()
    {
        using var tree = new TempTree();
        string path = tree.PathOf("textures/a.dds");

        AtomicFile.Write(path, "DDS "u8, [1, 2, 3]);

        Assert.Equal([.. "DDS "u8, 1, 2, 3], File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.partial"));
    }

    /// <summary>Answers every tile with a truncated JPEG.</summary>
    private sealed class BrokenTileServer : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(PhotoTileServer.Jpeg(1, 2, 3)[..200]),
            });
    }
}

[CollectionDefinition(nameof(AllocationCounting), DisableParallelization = true)]
public sealed class AllocationCounting;

/// <summary>Counts the bytes a build allocates, so no other test may run meanwhile.</summary>
[Collection(nameof(AllocationCounting))]
public sealed class BuildAllocationTests
{
    [Fact]
    public async Task A_build_allocates_little_beyond_the_levels_it_keeps()
    {
        var name = new TextureName(21904, 35824, "BI", 16);
        var layout = DdsLayout.For(4096);
        using var tiles = new TileSource("test", PhotoTileServer.Template, 8, handler: new PhotoTileServer());
        var builder = new TextureBuilder(tiles, 2);
        // The first build fills the buffer pools and the server's own JPEG cache.
        await builder.BuildChainAsync(name, layout, 0, 16, new FetchTicket(FetchPriority.Warm), new TileStats());

        long before = GC.GetTotalAllocatedBytes(precise: true);
        byte[] chain = await builder.BuildChainAsync(name, layout, 0, 16, new FetchTicket(FetchPriority.Warm), new TileStats());
        long extra = GC.GetTotalAllocatedBytes(precise: true) - before - chain.Length;

        // 256 tiles: their JPEGs and the HTTP answers carrying them, some 30 KB each. Decoded into arrays of their own
        // they were 48 MB more.
        Assert.True(extra < 24L << 20, $"The build allocated {extra >> 20} MB beyond its {chain.Length >> 20} MB of levels");
    }

    [Fact]
    public async Task A_build_holds_no_more_than_its_weight()
    {
        const int size = 4096;
        var name = new TextureName(21904, 35824, "BI", 16);
        using var tiles = new TileSource("test", PhotoTileServer.Template, 8, handler: new PhotoTileServer());
        var buffers = new BuildBufferPool(1L << 30);

        byte[] chain = await new TextureBuilder(tiles, 2, buffers).BuildChainAsync(name, DdsLayout.For(size), 0, 16, new FetchTicket(FetchPriority.Warm), new TileStats());

        // The picture and the two its smaller levels took turns in, each at its exact size, all given back.
        Assert.Equal(0, buffers.RentedBytes);
        Assert.Equal((long)size * size * 3 + size / 2 * (size / 2) * 3 + size / 4 * (size / 4) * 3, buffers.KeptBytes);
        Assert.True(buffers.KeptBytes + chain.Length <= TextureStreamer.BuildWeight(size),
            $"The build held {(buffers.KeptBytes + chain.Length) >> 20} MB, more than its weight of {TextureStreamer.BuildWeight(size) >> 20} MB");
    }
}
