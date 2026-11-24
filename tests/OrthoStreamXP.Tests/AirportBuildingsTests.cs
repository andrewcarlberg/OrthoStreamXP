using OrthoStreamXP.Setup;

namespace OrthoStreamXP.Tests;

public sealed class AirportBuildingsTests : IDisposable
{
    private const string Ebbr = """
        1    145 0 0 EBBR Brussels National
        1302 city Brussels
        100 45.00 35 727 0.00 1 3 0 07R 50.8890356 4.4803936 126 0 6 0 0 2 25L 50.8989489 4.5233233 0 0 6 2 1 2
        """;

    private const string Ebaw = """
        1    40 0 0 EBAW Antwerp
        1302 datum_lat 50.9894
        1302 datum_lon 4.4603
        """;

    private const string Tile = "X-Plane 12/Global Scenery/Global Airports/Earth nav data/+50+000/+50+004.dsf";

    private readonly TempTree _tree = new();

    public void Dispose() => _tree.Dispose();

    private static DsfFile Dsf(string[] properties, string[] objects, int pools, byte[] commands) => new(
        [.. new[] { "sim/west", "4", "sim/south", "50", "sim/overlay", "1", "sim/creation_agent", "WorldEditor" }.Concat(properties).Chunk(2).Select(p => new KeyValuePair<string, string>(p[0], p[1]))],
        new Dictionary<uint, IReadOnlyList<string>>
        {
            [DsfFile.Objects] = objects,
            [DsfFile.Polygons] = ["p.pol"],
            [DsfFile.Networks] = ["roads.net"],
        },
        [.. Enumerable.Range(0, pools).SelectMany(i => new[] { new DsfAtom(DsfFile.Pool16, [(byte)i]), new DsfAtom(0x5343414C, [(byte)i]) }),
         new DsfAtom(DsfFile.Pool32, [9]), new DsfAtom(0x53433332, [9])],
        commands);

    private static byte[] Filter(int index) => [32, 6, 1, 0, (byte)index, 0, 0, 0];

    /// <summary>Two airports in one tile, the way WorldEditor exports Global Airports: EBAW is filter 0, EBBR filter 1.</summary>
    private static DsfFile GlobalTile() => Dsf(
        ["sim/filter/aptid", "EBAW", "sim/exclude_obj", "a", "sim/filter/aptid", "EBBR", "sim/exclude_obj", "b"],
        ["x.obj", "y.obj"],
        pools: 2,
        [
            1, 0, 0, .. Filter(0), 3, 0, 7, 1, 0, // EBAW: x.obj
            .. Filter(1), 3, 1, 7, 2, 0, // EBBR: y.obj
            1, 1, 0, 3, 0, 13, 0xFF, 0, 0, 0, 4, 0, // EBBR: a polygon range from pool 1
            32, 6, 2, 0, 1, 0, 0, 0, // a marker
            .. Filter(0), 7, 3, 0, // EBAW again
            .. Filter(1), 1, 0, 0, 6, 5, 2, 10, 0, 0, 0, 3, 0, 10, 0, 0, 2, 0, // EBBR: a road
        ]);

    [Fact]
    public void Only_the_chosen_airports_filters_are_copied_with_the_state_their_commands_need()
    {
        var merger = new AirportDsfMerger();

        Assert.True(merger.Add(GlobalTile(), new HashSet<string>(["EBBR"])));
        DsfFile copy = merger.Build();

        Assert.Equal(
            ["sim/west", "sim/south", "sim/overlay", "sim/creation_agent", "sim/filter/aptid", "sim/exclude_obj"],
            copy.Properties.Select(p => p.Key));
        Assert.Equal(["EBBR"], copy.Values("sim/filter/aptid"));
        Assert.Equal(["b"], copy.Values("sim/exclude_obj"));
        Assert.Equal<byte>(
            [
                .. Filter(0), 1, 0, 0, 5, 1, 0, 0, 0, 7, 2, 0, // y.obj
                1, 1, 0, 5, 0, 0, 0, 0, 13, 0xFF, 0, 0, 0, 4, 0, // the polygon
                32, 6, 2, 0, 1, 0, 0, 0, 1, 0, 0, 6, 5, 2, 10, 0, 0, 0, 10, 0, 0, 2, 0, // the marker, then the road from PO32 pool 0
            ],
            copy.Commands);
        // The file X-Plane reads holds the same.
        DsfFile read = DsfFile.Parse(copy.ToBytes());
        Assert.Equal(copy.Commands, read.Commands);
        Assert.Equal(copy.Properties, read.Properties);
        Assert.Equal(["x.obj", "y.obj"], read.Definitions[DsfFile.Objects]);
    }

    [Fact]
    public void Airports_from_two_files_share_definitions_and_get_their_own_pools_and_filter_numbers()
    {
        DsfFile other = Dsf(["sim/filter/aptid", "EBAW"], ["z.obj", "y.obj"], pools: 1, [1, 0, 0, .. Filter(0), 3, 1, 7, 0, 0]);
        var merger = new AirportDsfMerger();

        merger.Add(GlobalTile(), new HashSet<string>(["EBBR"]));
        merger.Add(other, new HashSet<string>(["EBAW"]));
        DsfFile copy = merger.Build();

        Assert.Equal(["EBBR", "EBAW"], copy.Values("sim/filter/aptid"));
        Assert.Equal(["x.obj", "y.obj", "z.obj"], copy.Definitions[DsfFile.Objects]);
        Assert.Equal(3, copy.Geodesy.Count(a => a.Id == DsfFile.Pool16));
        // EBAW's y.obj is the same definition as EBBR's; its pool 0 is the third one now.
        Assert.Equal<byte>([.. Filter(1), 1, 2, 0, 5, 1, 0, 0, 0, 7, 0, 0], copy.Commands[^19..]);
    }

    [Fact]
    public void A_file_without_the_airport_copies_nothing_and_one_that_is_not_an_overlay_is_refused()
    {
        var merger = new AirportDsfMerger();

        Assert.False(merger.Add(GlobalTile(), new HashSet<string>(["LFBD"])));
        Assert.True(merger.IsEmpty);
        DsfFile mesh = GlobalTile() with { Properties = [new("sim/overlay", "0"), new("sim/filter/aptid", "EBBR")] };
        Assert.Throws<InvalidDataException>(() => merger.Add(mesh, new HashSet<string>(["EBBR"])));
    }

    [Fact]
    public void Flattening_copies_the_airports_buildings_into_the_package_and_undo_takes_them_out()
    {
        _tree.WriteText("X-Plane 12/Global Scenery/Global Airports/Earth nav data/apt.dat", $"I\n1200 Version\n\n{Ebbr}\n\n{Ebaw}\n\n99\n");
        _tree.WriteBytes(Tile, GlobalTile().ToBytes());
        string package = _tree.PathOf($"X-Plane 12/Custom Scenery/{FlattenedAirports.PackageName}");
        string copied = Path.Join(package, "Earth nav data", "+50+000", "+50+004.dsf");
        var flattening = new AirportFlattening(_tree.PathOf("X-Plane 12"), new AirportCatalog());
        IReadOnlyList<FlattenableAirport> airports = flattening.Load(CancellationToken.None);

        FlattenableAirport ebbr = flattening.Flatten(airports.Single(a => a.Ident == "EBBR"));
        Assert.Equal(["EBBR"], DsfFile.Parse(File.ReadAllBytes(copied)).Values("sim/filter/aptid"));
        FlattenableAirport ebaw = flattening.Flatten(airports.Single(a => a.Ident == "EBAW"));
        Assert.Equal(["EBAW", "EBBR"], DsfFile.Parse(File.ReadAllBytes(copied)).Values("sim/filter/aptid"));
        Assert.Equal(FlattenState.Flattened, flattening.Load(CancellationToken.None).Single(a => a.Ident == "EBBR").State);

        flattening.Undo(ebbr);
        Assert.Equal(["EBAW"], DsfFile.Parse(File.ReadAllBytes(copied)).Values("sim/filter/aptid"));
        flattening.Undo(ebaw);
        Assert.False(Directory.Exists(package));
    }

    [Fact]
    public void A_copy_made_without_its_buildings_is_outdated()
    {
        _tree.WriteText("X-Plane 12/Global Scenery/Global Airports/Earth nav data/apt.dat", $"I\n1200 Version\n\n{Ebbr}\n\n99\n");
        _tree.WriteBytes(Tile, GlobalTile().ToBytes());
        var flattening = new AirportFlattening(_tree.PathOf("X-Plane 12"), new AirportCatalog());
        flattening.Flatten(flattening.Load(CancellationToken.None).Single());
        File.Delete(_tree.PathOf($"X-Plane 12/Custom Scenery/{FlattenedAirports.PackageName}/buildings.txt"));

        Assert.Equal(FlattenState.Outdated, flattening.Load(CancellationToken.None).Single().State);
    }

    [Fact]
    public void An_airports_area_spans_its_runways_and_datum()
    {
        Assert.Equal((50.8890356, 4.4803936, 50.8989489, 4.5233233), new AirportBlock(Ebbr).Area());
        Assert.Equal((50.9894, 4.4603, 50.9894, 4.4603), new AirportBlock(Ebaw).Area());
        Assert.Equal(Path.Join("+40-010", "+44-001.dsf"), AirportDsfFinder.TilePath(44, -1));
        Assert.Equal(Path.Join("-10-080", "-01-071.dsf"), AirportDsfFinder.TilePath(-1, -71));
    }
}
