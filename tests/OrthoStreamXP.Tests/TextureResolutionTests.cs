using OrthoStreamXP.Scenery;
using OrthoStreamXP.Setup;
using OrthoStreamXP.Textures;

namespace OrthoStreamXP.Tests;

/// <summary>
/// A preset's resolution shrinks its textures from the same downloads: Full as they are, Medium halves the wide ZL16
/// areas' and keeps the airports' ZL18 ones, Low halves them all. Each preset keeps its own in the settings.
/// </summary>
public sealed class TextureResolutionTests : IDisposable
{
    private readonly TempTree _tree = new();

    public void Dispose() => _tree.Dispose();

    // ---------------------------------------------------------------- sizes

    [Theory]
    //          level  resolution  ZL16  ZL18
    [InlineData(-2, "full", 2048, 1024)]   // Balanced
    [InlineData(-2, "medium", 1024, 1024)]
    [InlineData(-2, "low", 1024, 512)]
    [InlineData(-1, "medium", 2048, 2048)] // Sharp
    [InlineData(0, "full", 8192, 4096)]    // Ultra
    [InlineData(0, "medium", 4096, 4096)]
    [InlineData(0, "low", 4096, 2048)]
    [InlineData(1, "medium", 4096, 8192)]  // Extra
    [InlineData(1, "low", 4096, 4096)]
    public void The_resolution_halves_the_size_but_not_the_imagery(int level, string resolution, int zl16, int zl18)
    {
        var plan = new TexturePlan(level) { Resolution = TextureResolution.Parse(resolution)! };
        var full = new TexturePlan(level);

        Assert.Equal((zl16, zl18), (plan.SizeFor(16), plan.SizeFor(18)));
        Assert.Equal((full.FetchZoom(16), full.FetchZoom(18)), (plan.FetchZoom(16), plan.FetchZoom(18))); // the same downloads
    }

    [Fact]
    public void Every_size_is_a_power_of_two_X_Plane_takes()
    {
        // X-Plane: "The texture ... is DDS but its size is not power of 2", and the texture is missing.
        foreach (TextureResolution resolution in TextureResolution.All)
        foreach (QualityPreset preset in QualityPreset.Choices)
        foreach (int zoom in new[] { 14, 15, 16, 17, 18, 19 })
        {
            int size = preset.PlanAt(resolution).SizeFor(zoom);
            Assert.True(size >= 4 && (size & (size - 1)) == 0, $"{preset.Name} at {resolution.Label}: ZL{zoom} is {size} px");
        }
    }

    [Fact]
    public void Each_resolution_keeps_its_own_cache_folder_and_it_is_named_after_it()
    {
        Assert.Equal("Ultra_8192-4096", new TexturePlan(0).FolderName);
        Assert.Equal("Ultra_4096-4096", new TexturePlan(0) { Resolution = TextureResolution.Medium }.FolderName);
        Assert.Equal("Ultra_4096-2048", new TexturePlan(0) { Resolution = TextureResolution.Low }.FolderName);

        Assert.Equal("Ultra", CacheInventory.ParseSetupName("Ultra_8192-4096")!.PresetTitle);
        Assert.Equal("Ultra, Medium resolution", CacheInventory.ParseSetupName("Ultra_4096-4096")!.PresetTitle);
        Assert.Equal("Ultra, Low resolution", CacheInventory.ParseSetupName("Ultra_4096-2048")!.PresetTitle);
        Assert.Equal("Extra, Medium resolution", CacheInventory.ParseSetupName("Extra_4096-8192")!.PresetTitle);
    }

    [Theory]
    [InlineData("full", false, false)]
    [InlineData("Medium", true, false)]
    [InlineData(" low ", true, true)]
    public void Resolutions_are_read_by_name(string value, bool areas, bool airports)
    {
        TextureResolution resolution = TextureResolution.Parse(value)!;
        Assert.Equal((areas, airports), (resolution.HalvesAreas, resolution.HalvesAirports));
    }

    [Fact]
    public void An_unknown_resolution_is_none() => Assert.Null(TextureResolution.Parse("high"));

    // ---------------------------------------------------------------- the settings

    private MountSettings Parse(params string[] extra)
    {
        Directory.CreateDirectory(_tree.PathOf("source"));
        return MountSettings.FromValues(TestSettings.Of(
            [$"source = {_tree.PathOf("source")}", $"cache = {_tree.PathOf("cache")}", $"mountpoint = {_tree.PathOf("mount")}", .. extra]));
    }

    [Fact]
    public void The_mount_uses_the_resolution_of_the_preset_the_settings_are_at()
    {
        Assert.Same(TextureResolution.Full, Parse("level-change = 0").Textures.Resolution); // not set: Full

        MountSettings ultra = Parse("level-change = 0", "resolution-ultra = medium", "resolution-sharp = low");
        Assert.Same(TextureResolution.Medium, ultra.Textures.Resolution);
        Assert.Equal((4096, 4096), (ultra.Textures.SizeFor(16), ultra.Textures.SizeFor(18)));
        Assert.EndsWith("Ultra_4096-4096", ultra.PlanCache, StringComparison.Ordinal);

        Assert.Same(TextureResolution.Low, Parse("level-change = -1", "resolution-ultra = medium", "resolution-sharp = low").Textures.Resolution);
    }

    [Fact]
    public void Dynamic_has_its_own_resolution_apart_from_Extras()
    {
        Assert.Same(TextureResolution.Low,
            Parse("level-change = 1", "dynamic-quality = on", "resolution-dynamic = low", "resolution-extra = medium").Textures.Resolution);
        Assert.Same(TextureResolution.Medium,
            Parse("level-change = 1", "dynamic-quality = off", "resolution-dynamic = low", "resolution-extra = medium").Textures.Resolution);
    }

    [Fact]
    public void A_level_no_preset_has_and_a_wrong_resolution_are_errors()
    {
        foreach (string level in new[] { "-4", "-3", "2" })
            Assert.Contains("level-change", Assert.Throws<SettingsException>(() => Parse($"level-change = {level}")).Message, StringComparison.Ordinal);
        var error = Assert.Throws<SettingsException>(() => Parse("level-change = 0", "resolution-ultra = high"));
        Assert.Contains("resolution-ultra", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_preset_has_its_resolution_setting_and_the_window_reads_it()
    {
        foreach (QualityPreset preset in QualityPreset.Choices)
            TestSettings.Of($"{preset.ResolutionKey} = medium"); // an unknown setting would throw

        SettingsDocument document = SettingsDocument.FromText(_tree.PathOf("settings.ini"), "level-change = 1\ndynamic-quality = on\nresolution-dynamic = low\n");
        Assert.Same(TextureResolution.Low, QualityPreset.Dynamic.ResolutionIn(document));
        Assert.Same(TextureResolution.Full, QualityPreset.Extra.ResolutionIn(document));
        Assert.Equal((4096, 4096), (QualityPreset.PlanIn(document).SizeFor(16), QualityPreset.PlanIn(document).SizeFor(18)));
    }

    [Fact]
    public void A_new_settings_file_lists_every_presets_resolution_and_the_setup_sets_them_in_place()
    {
        SettingsDocument settings = SettingsDocument.CreateNew(_tree.PathOf("orthostreamxp.ini"), @"C:\X-Plane 12", @"C:\Packages", @"D:\cache", QualityPreset.Ultra);
        Assert.All(QualityPreset.Choices, p => Assert.Same(TextureResolution.Full, p.ResolutionIn(settings)));
        int lines = settings.Lines.Count;

        settings.Set(QualityPreset.Ultra.ResolutionKey, TextureResolution.Medium.Value); // what the setup writes for each card
        Assert.Equal(lines, settings.Lines.Count); // in its place in the file, not added at the end
        Assert.Same(TextureResolution.Medium, QualityPreset.Ultra.ResolutionIn(settings));
        Assert.Equal(4096, QualityPreset.PlanIn(settings).SizeFor(16));
        settings.Parse(); // the file reads as settings
    }

    [Fact]
    public void Dynamic_qualitys_steps_shrink_with_the_mounts_resolution()
    {
        var mount = new TexturePlan(1) { Resolution = TextureResolution.Low };
        IReadOnlyList<DetailTier> tiers = new DynamicOptions().TiersBelow(mount);
        Assert.Equal([2048, 1024], tiers.Select(t => t.Plan.SizeFor(16))); // Sharp and Balanced at Low
    }
}
