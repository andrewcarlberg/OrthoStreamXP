using System.Buffers.Binary;
using System.Text;
using OrthoStreamXP.Scenery;

namespace OrthoStreamXP.Tests;

/// <summary>The scenery tiles X-Plane will load next, and the textures their DSFs use.</summary>
public sealed class UpcomingSceneryTests
{
    [Fact]
    public void The_terrain_table_lists_the_definitions_of_a_DSF()
    {
        using var tree = new TempTree();
        string path = tree.PathOf("+47+011.dsf");
        File.WriteAllBytes(path, Dsf("terrain_Water", "terrain/22992_34800_BI16.ter", "terrain/89376_140560_BI18_water_overlay.ter"));

        Assert.Equal(["terrain_Water", "terrain/22992_34800_BI16.ter", "terrain/89376_140560_BI18_water_overlay.ter"], DsfTerrainTable.Read(path));
    }

    [Fact]
    public void A_file_that_is_no_DSF_has_no_terrain_table()
    {
        using var tree = new TempTree();
        string path = tree.PathOf("+47+011.dsf");
        File.WriteAllBytes(path, [(byte)'7', (byte)'z', 0xBC, 0xAF, 0x27, 0x1C, 0, 4, 1, 2, 3, 4, 5, 6]); // a 7-Zip DSF

        Assert.Empty(DsfTerrainTable.Read(path));
    }

    [Fact]
    public void XPlane_keeps_three_rows_and_four_columns_of_tiles_around_the_aircraft()
    {
        // The test flight of 17 Nov: at 47.72, 13.05 X-Plane loaded +46..+48 and +011..+014.
        var expected = from lat in new[] { 46, 47, 48 } from lon in new[] { 11, 12, 13, 14 } select new SceneryTile(lat, lon);

        Assert.Equal(expected.ToHashSet(), UpcomingScenery.LoadedAround(47.72, 13.05).ToHashSet());
    }

    [Fact]
    public void Flying_north_the_next_row_is_ahead()
    {
        // 150 m/s north from 47.95: past 48 within the look-ahead, so the block takes in row +49.
        var camera = new CameraReport(47.95, 12.6, 900, null, new Motion(150, 0), false, DateTime.UtcNow);

        List<SceneryTile> ahead = UpcomingScenery.Ahead(camera);

        Assert.Equal(new[] { 11, 12, 13, 14 }.Select(lon => new SceneryTile(49, lon)).ToHashSet(), ahead.ToHashSet());
    }

    [Fact]
    public void Parked_or_placed_from_reads_nothing_is_ahead()
    {
        Assert.Empty(UpcomingScenery.Ahead(new CameraReport(47.95, 12.6, 2, null, null, false, DateTime.UtcNow)));
        Assert.Empty(UpcomingScenery.Ahead(new CameraReport(47.95, 12.6, 0, null, new Motion(150, 0), false, DateTime.UtcNow, Inferred: true)));
    }

    [Fact]
    public void A_tile_DSF_lies_in_its_ten_degree_folder()
    {
        Assert.Equal(Path.Join("Earth nav data", "+40+010", "+47+011.dsf"), new SceneryTile(47, 11).DsfPath);
        Assert.Equal(Path.Join("Earth nav data", "-10-080", "-03-071.dsf"), new SceneryTile(-3, -71).DsfPath);
        Assert.Equal(new SceneryTile(-1, -180), SceneryTile.At(-0.5, 180.5));
    }

    /// <summary>A DSF of a header atom, the definitions (the terrain table and an empty object table) and an empty mesh.</summary>
    internal static byte[] Dsf(params string[] terrain)
    {
        static byte[] Atom(string id, byte[] payload)
        {
            byte[] atom = new byte[8 + payload.Length];
            Encoding.ASCII.GetBytes(new string([.. id.Reverse()])).CopyTo(atom, 0); // ids are stored as little-endian numbers
            BinaryPrimitives.WriteUInt32LittleEndian(atom.AsSpan(4), (uint)atom.Length);
            payload.CopyTo(atom, 8);
            return atom;
        }

        byte[] table = Encoding.ASCII.GetBytes(string.Concat(terrain.Select(t => t + "\0")));
        byte[] definitions = [.. Atom("TERT", table), .. Atom("OBJT", [])];
        byte[] version = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(version, 1);
        return [.. "XPLNEDSF"u8, .. version, .. Atom("HEAD", new byte[86]), .. Atom("DEFN", definitions), .. Atom("GEOD", new byte[32]), .. new byte[16]];
    }
}
