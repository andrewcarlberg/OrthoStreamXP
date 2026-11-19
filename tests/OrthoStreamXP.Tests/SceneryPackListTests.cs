using OrthoStreamXP.Scenery;

namespace OrthoStreamXP.Tests;

public sealed class SceneryPackListTests : IDisposable
{
    private readonly TempTree _tree = new();

    public void Dispose() => _tree.Dispose();

    private string CustomScenery => _tree.PathOf("X-Plane 12/Custom Scenery");

    private string FileText => File.ReadAllText(Path.Join(CustomScenery, "scenery_packs.ini"));

    private void WriteFile(string text) => _tree.WriteText("X-Plane 12/Custom Scenery/scenery_packs.ini", text);

    [Fact]
    public void The_entries_are_read_in_order_with_their_state()
    {
        WriteFile("I\r\n1000 Version\r\nSCENERY\r\n\r\nSCENERY_PACK Custom Scenery/EDDB Berlin/\r\nSCENERY_PACK *GLOBAL_AIRPORTS*\r\nSCENERY_PACK_DISABLED Custom Scenery\\XPME_Africa\r\n");

        IReadOnlyList<SceneryPackEntry> entries = new SceneryPackList(CustomScenery).Read();

        Assert.Equal(
            [
                new SceneryPackEntry("Custom Scenery/EDDB Berlin/", true),
                new SceneryPackEntry("*GLOBAL_AIRPORTS*", true),
                new SceneryPackEntry(@"Custom Scenery\XPME_Africa", false),
            ], entries);
        Assert.Equal(["EDDB Berlin", "*GLOBAL_AIRPORTS*", "XPME_Africa"], entries.Select(e => e.Name));
    }

    [Fact]
    public void No_file_lists_nothing()
    {
        Directory.CreateDirectory(CustomScenery);
        Assert.Empty(new SceneryPackList(CustomScenery).Read());
        Assert.False(new SceneryPackList(CustomScenery).SetEnabled("Custom Scenery/KSEA/", false));
    }

    [Fact]
    public void An_entry_is_switched_in_place_and_the_rest_of_the_file_stays()
    {
        WriteFile("I\r\n1000 Version\r\nSCENERY\r\n\r\nSCENERY_PACK Custom Scenery/KSEA/\r\nSCENERY_PACK Custom Scenery/EDDB Berlin/\r\nSCENERY_PACK *GLOBAL_AIRPORTS*\r\n");
        var list = new SceneryPackList(CustomScenery);

        Assert.True(list.SetEnabled("Custom Scenery/EDDB Berlin/", false));
        Assert.Equal("I\r\n1000 Version\r\nSCENERY\r\n\r\nSCENERY_PACK Custom Scenery/KSEA/\r\nSCENERY_PACK_DISABLED Custom Scenery/EDDB Berlin/\r\nSCENERY_PACK *GLOBAL_AIRPORTS*\r\n", FileText);
        Assert.False(list.SetEnabled("Custom Scenery/EDDB Berlin/", false), "already off: left alone");

        Assert.True(list.SetEnabled("Custom Scenery/EDDB Berlin/", true));
        Assert.Equal("I\r\n1000 Version\r\nSCENERY\r\n\r\nSCENERY_PACK Custom Scenery/KSEA/\r\nSCENERY_PACK Custom Scenery/EDDB Berlin/\r\nSCENERY_PACK *GLOBAL_AIRPORTS*\r\n", FileText);
    }

    [Fact]
    public void An_entry_is_found_whichever_slashes_it_is_written_with_and_LF_endings_stay()
    {
        WriteFile("I\n1000 Version\nSCENERY\n\nSCENERY_PACK_DISABLED Custom Scenery\\XPME_Africa\n");

        Assert.True(new SceneryPackList(CustomScenery).SetEnabled("custom scenery/XPME_Africa/", true));
        Assert.Equal("I\n1000 Version\nSCENERY\n\nSCENERY_PACK Custom Scenery\\XPME_Africa\n", FileText);
    }

    [Fact]
    public void A_path_not_listed_changes_nothing()
    {
        WriteFile("I\r\n1000 Version\r\nSCENERY\r\n\r\nSCENERY_PACK Custom Scenery/KSEA/\r\n");
        Assert.False(new SceneryPackList(CustomScenery).SetEnabled("Custom Scenery/KSEAT/", false));
        Assert.Equal("I\r\n1000 Version\r\nSCENERY\r\n\r\nSCENERY_PACK Custom Scenery/KSEA/\r\n", FileText);
    }

    [Fact]
    public void An_entry_moves_to_any_place_the_ones_between_shift_and_the_other_lines_stay()
    {
        WriteFile("I\r\n1000 Version\r\nSCENERY\r\n\r\nSCENERY_PACK Custom Scenery/KSEA/\r\n\r\nSCENERY_PACK Custom Scenery/EGLL/\r\nSCENERY_PACK_DISABLED Custom Scenery\\EDDB/\r\nSCENERY_PACK *GLOBAL_AIRPORTS*\r\n");
        var list = new SceneryPackList(CustomScenery);

        Assert.True(list.MoveTo("Custom Scenery/EDDB", 0));
        Assert.Equal("I\r\n1000 Version\r\nSCENERY\r\n\r\nSCENERY_PACK_DISABLED Custom Scenery\\EDDB/\r\n\r\nSCENERY_PACK Custom Scenery/KSEA/\r\nSCENERY_PACK Custom Scenery/EGLL/\r\nSCENERY_PACK *GLOBAL_AIRPORTS*\r\n", FileText);

        Assert.True(list.MoveTo("Custom Scenery/EDDB/", 3));
        Assert.Equal(["Custom Scenery/KSEA/", "Custom Scenery/EGLL/", "*GLOBAL_AIRPORTS*", @"Custom Scenery\EDDB/"], list.Read().Select(e => e.Path));

        Assert.True(list.MoveTo("Custom Scenery/KSEA/", 1));
        Assert.Equal(["Custom Scenery/EGLL/", "Custom Scenery/KSEA/", "*GLOBAL_AIRPORTS*", @"Custom Scenery\EDDB/"], list.Read().Select(e => e.Path));
    }

    [Theory]
    [InlineData("Custom Scenery/KSEA/", 0)]
    [InlineData("Custom Scenery/KSEA/", 2)]
    [InlineData("Custom Scenery/KSEA/", -1)]
    [InlineData("Custom Scenery/Missing/", 0)]
    public void A_move_to_its_own_place_outside_the_list_or_of_a_missing_entry_changes_nothing(string path, int to)
    {
        const string Text = "I\n1000 Version\nSCENERY\n\nSCENERY_PACK Custom Scenery/KSEA/\nSCENERY_PACK *GLOBAL_AIRPORTS*\n";
        WriteFile(Text);
        Assert.False(new SceneryPackList(CustomScenery).MoveTo(path, to));
        Assert.Equal(Text, FileText);
    }
}
