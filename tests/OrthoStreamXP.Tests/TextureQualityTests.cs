using OrthoStreamXP.Textures;

namespace OrthoStreamXP.Tests;

/// <summary>
/// Texture size = the detail --level-change downloads, never shrunk below it (up to 8192 px), never enlarged.
/// </summary>
public sealed class TextureQualityTests
{
    [Theory]
    //          level  ZL16  ZL18
    [InlineData(-4, 512, 256)]
    [InlineData(-3, 1024, 512)]
    [InlineData(-2, 2048, 1024)]  // Balanced
    [InlineData(-1, 4096, 2048)]  // Sharp
    [InlineData(0, 8192, 4096)]   // Ultra
    [InlineData(1, 8192, 8192)]   // Extra
    public void The_size_follows_the_level(int level, int zl16, int zl18)
    {
        var plan = new TexturePlan(level);
        Assert.Equal((zl16, zl18), (plan.SizeFor(16), plan.SizeFor(18)));
    }

    [Theory]
    //          level  ZL16 detail / size  ZL18 detail / size
    [InlineData(-2, 1, 1)]
    [InlineData(-1, 1, 1)]
    [InlineData(0, 1, 1)]
    [InlineData(1, 1, 1)]
    public void Textures_keep_the_detail_they_download(int level, int zl16, int zl18)
    {
        var plan = new TexturePlan(level);
        // The pixels a texture's tiles hold: 4096 at its own zoom (16 x 16 tiles of 256 px), twice as many per zoom finer.
        int Detail(int fileZoom) => (int)(4096 * Math.Pow(2, plan.FetchZoom(fileZoom) - fileZoom));
        Assert.Equal(zl16 * plan.SizeFor(16), Detail(16));
        Assert.Equal(zl18 * plan.SizeFor(18), Detail(18));
    }

    [Fact]
    public void Level_plus_one_downloads_zoom_17_and_19_and_builds_8192()
    {
        var plan = new TexturePlan(1);
        Assert.Equal((17, 8192), (plan.FetchZoom(16), plan.SizeFor(16)));
        Assert.Equal((19, 8192), (plan.FetchZoom(18), plan.SizeFor(18)));
        Assert.Equal("Extra_8192-8192", plan.FolderName);
    }

    [Theory]
    //          level  areas  airports
    [InlineData(-3, 14, 15)]
    [InlineData(-2, 15, 16)] // Balanced
    [InlineData(-1, 16, 17)] // Sharp
    [InlineData(0, 17, 18)]  // Ultra
    [InlineData(1, 17, 19)]  // Extra
    public void Each_level_downloads_fixed_zooms_whatever_the_package_zoom(int level, int areas, int airports)
    {
        var plan = new TexturePlan(level);
        Assert.Equal((areas, areas), (plan.FetchZoom(16), plan.FetchZoom(17)));       // ZL16/ZL17: the areas'
        Assert.Equal((airports, airports), (plan.FetchZoom(18), plan.FetchZoom(19))); // ZL18/ZL19: the airports'
    }

    [Fact]
    public void A_finer_package_gets_the_same_imagery_in_smaller_textures()
    {
        var extra = new TexturePlan(1);
        Assert.Equal((4096, 4096), (extra.SizeFor(17), extra.SizeFor(19))); // zoom 17 and 19 at the files' own zoom
        var ultra = new TexturePlan(0);
        Assert.Equal((4096, 2048), (ultra.SizeFor(17), ultra.SizeFor(19))); // zoom 17 and 18
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-5)]
    public void A_level_without_its_zooms_is_refused(int level)
    {
        Assert.False(TexturePlan.IsKnownLevel(level));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TexturePlan(level));
    }

    [Fact]
    public void Sizes_by_file_name_and_the_log_line()
    {
        var plan = new TexturePlan(-2);
        Assert.Equal(2048, plan.SizeFor("21904_35824_BI16.dds"));
        Assert.Null(plan.SizeFor("water_transition.dds"));
        Assert.Equal("ZL16 → zoom 15, 2048 px; ZL18 → zoom 16, 1024 px", plan.Describe());
        Assert.Equal(4096, new TexturePlan(0, 4096).SizeFor(16)); // an explicit limit (tests) caps the size
    }

    [Theory]
    [InlineData(-2, "Balanced_2048-1024")]
    [InlineData(-1, "Sharp_4096-2048")]
    [InlineData(0, "Ultra_8192-4096")]
    [InlineData(1, "Extra_8192-8192")]
    [InlineData(-3, "level-3_1024-512")]
    public void Each_level_has_its_own_cache_folder(int level, string folder) =>
        Assert.Equal(folder, new TexturePlan(level).FolderName);

    [Theory]
    [InlineData(-1, "87520_143440_BI18", "43760_71720_PX17")]
    [InlineData(0, "87520_143440_BI18", "87520_143440_PX18")]
    [InlineData(1, "87520_143440_BI18", "175040_286880_PX19")]
    [InlineData(-1, "21904_35824_BI16", "21904_35824_PX16")] // ZL16 downloads one zoom finer
    [InlineData(0, "21904_35824_BI16", "43808_71648_PX17")]
    [InlineData(-2, "21904_35824_ARC16", "10952_17912_PX15")] // the package's code is left out
    [InlineData(0, "43792_71648_BI17", "43792_71648_BI17")] // ZL17 is downloaded where ZL16 is: it keeps its name
    [InlineData(1, "175040_286880_BI19", "175040_286880_BI19")] // ZL19 where ZL18 is
    [InlineData(1, "10944_17904_BI15", "10944_17904_BI15")]
    public void The_cache_names_a_texture_by_the_tiles_it_downloads(int level, string stem, string cacheStem)
    {
        Assert.True(TextureName.TryParseStem(stem, out TextureName name));
        Assert.Equal(cacheStem, new TexturePlan(level).CacheStem(name));
    }

    [Fact]
    public void A_cache_name_reads_back_as_its_texture_at_every_level()
    {
        TextureName[] names = [new(21904, 35824, "BI", 16), new(87520, 143440, "BI", 18)];
        for (int level = -4; level <= 1; level++)
        {
            var plan = new TexturePlan(level);
            foreach (TextureName name in names)
                Assert.Equal(name with { Code = TextureName.CacheCode }, plan.FromCacheStem(plan.CacheStem(name)));
            Assert.Equal(names.Length, names.Select(plan.CacheStem).Distinct().Count());
        }
        Assert.Null(new TexturePlan(0).FromCacheStem("21904_35824_BI16"));
        Assert.Null(new TexturePlan(0).FromCacheStem("43808_71649_PX17")); // not the corner of any texture
    }

    [Fact]
    public void A_cache_name_is_not_a_package_name() => Assert.False(TextureName.TryParseStem("87520_143440_PX18", out _));

    [Fact]
    public async Task Finer_detail_is_shrunk_to_the_size_when_built()
    {
        using var tree = new TempTree();
        using var tiles = new TileSource("synthetic", null, 8);
        string destination = tree.PathOf("21904_35824_BI16.dds");
        const int size = 512; // from level -2's 1024 px of detail: a level beyond Extra's still shrinks what it downloads

        BuildResult result = await new TextureBuilder(tiles, 2).BuildAsync("21904_35824_BI16.dds", size, destination, levelChange: -2);

        Assert.Equal(16, result.Misses); // still downloads level -2's 16 tiles
        Assert.Equal((512, 512), Dds.ReadDimensions(destination));
    }
}
