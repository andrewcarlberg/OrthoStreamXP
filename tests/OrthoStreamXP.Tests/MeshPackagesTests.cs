using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using OrthoStreamXP.Scenery;
using OrthoStreamXP.Setup;

namespace OrthoStreamXP.Tests;

public sealed class MeshPackagesTests : IDisposable
{
    private const string Host = "https://example.test/";
    private readonly TempTree _tree = new();
    private readonly FakeReleases _server = new();
    private readonly HttpClient _http;

    public MeshPackagesTests() => _http = new HttpClient(_server);

    public void Dispose()
    {
        _http.Dispose();
        _tree.Dispose();
    }

    private string Packages => _tree.PathOf("Base Packages");

    private RegionInstaller Installer() => new(new AssetDownload(_http, attempts: 1), Packages);

    private static readonly IProgress<InstallProgress> NoProgress = new Progress<InstallProgress>();

    // ---------------------------------------------------------------- the catalog

    [Fact]
    public void The_catalog_takes_the_newest_full_release_of_each_region()
    {
        string json = """
            [
              { "tag_name": "0.0.54", "prerelease": false, "assets": [
                  { "name": "na_info.json", "browser_download_url": "u/na_info.json", "size": 1 },
                  { "name": "z_na_01.zip", "browser_download_url": "u/z_na_01.zip", "size": 20 },
                  { "name": "z_na_00.zip", "browser_download_url": "u/z_na_00.zip", "size": 10 },
                  { "name": "z_na_00.zip.sha256", "browser_download_url": "u/z_na_00.zip.sha256", "size": 64 },
                  { "name": "y_na_overlays.zip.00", "browser_download_url": "u/y0", "size": 5 } ] },
              { "tag_name": "0.0.52", "prerelease": true, "assets": [
                  { "name": "eur_info.json", "browser_download_url": "u/x", "size": 1 },
                  { "name": "z_eur_00.zip", "browser_download_url": "u/pre", "size": 99 } ] },
              { "tag_name": "0.0.51", "prerelease": false, "assets": [
                  { "name": "eur_info.json", "browser_download_url": "u/eur_info.json", "size": 1 },
                  { "name": "y_eur_overlays.zip.02", "browser_download_url": "u/e2", "size": 3 },
                  { "name": "y_eur_overlays.zip.00", "browser_download_url": "u/e0", "size": 1 },
                  { "name": "y_eur_overlays.zip.01", "browser_download_url": "u/e1", "size": 2 },
                  { "name": "z_eur_00.zip", "browser_download_url": "u/z_eur_00.zip", "size": 7 } ] },
              { "tag_name": "0.0.50", "prerelease": false, "assets": [
                  { "name": "eur_info.json", "browser_download_url": "u/old", "size": 1 },
                  { "name": "z_eur_00.zip", "browser_download_url": "u/old.zip", "size": 7 } ] },
              { "tag_name": "0.0.48", "prerelease": false, "assets": [
                  { "name": "z_afr_00.zip", "browser_download_url": "u/no-info", "size": 7 } ] }
            ]
            """;

        IReadOnlyList<MeshRegion> regions = MeshCatalog.Parse(json);

        Assert.Equal(["eur", "na"], regions.Select(r => r.Id)); // the window's order; Africa has no info.json
        MeshRegion eur = regions[0];
        Assert.Equal("0.0.51", eur.Release);
        Assert.Equal(["z_eur_00.zip"], eur.Mesh.Select(a => a.Name));
        Assert.Equal(["y_eur_overlays.zip.00", "y_eur_overlays.zip.01", "y_eur_overlays.zip.02"], eur.Overlays.Select(a => a.Name));
        Assert.Equal("OrthoStream_Eur", eur.PackageName);
        MeshRegion na = regions[1];
        Assert.Equal(["z_na_00.zip", "z_na_01.zip"], na.Mesh.Select(a => a.Name));
        Assert.Equal("u/z_na_00.zip.sha256", na.Mesh[0].Sha256Url);
        Assert.Null(na.Mesh[1].Sha256Url);
        Assert.Equal(30, na.MeshSize);
    }

    // ---------------------------------------------------------------- installing

    [Fact]
    public async Task A_region_becomes_one_package_and_its_overlays_go_to_the_shared_package()
    {
        MeshRegion eur = Region("eur", "0.0.51",
            [Zip(("z_eur_00/Earth nav data/+40+000/+45+005.dsf", "dsf A"), ("z_eur_00/terrain/1_2_BI16.ter", "ter A")),
             Zip(("z_eur_01/Earth nav data/+50+010/+51+011.dsf", "dsf B"))],
            Zip(("y_eur/yOrtho4XP_Overlays/Earth nav data/+40+000/+45+005.dsf", "overlay A")), parts: 3);

        await Installer().InstallAsync(eur, withOverlays: true, NoProgress, CancellationToken.None);

        Assert.Equal("dsf A", File.ReadAllText(Path.Join(Packages, "OrthoStream_Eur", "Earth nav data", "+40+000", "+45+005.dsf")));
        Assert.Equal("dsf B", File.ReadAllText(Path.Join(Packages, "OrthoStream_Eur", "Earth nav data", "+50+010", "+51+011.dsf")));
        Assert.Equal("ter A", File.ReadAllText(Path.Join(Packages, "OrthoStream_Eur", "terrain", "1_2_BI16.ter")));
        Assert.Equal("overlay A", File.ReadAllText(Path.Join(Packages, "OrthoStream_Overlays", "Earth nav data", "+40+000", "+45+005.dsf")));
        Assert.False(Directory.Exists(Path.Join(Packages, RegionInstaller.WorkFolderName)));
        InstalledRegion installed = Assert.Single(Installer().Installed()).Value;
        Assert.Equal(("eur", "0.0.51", true), (installed.Id, installed.Release, installed.Overlays));
        // Both are packages the mount picks up, the work folder never is.
        Assert.Equal(["OrthoStream_Eur", "OrthoStream_Overlays"], SceneryPacks.FindPackages(Packages, out _).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Without_overlays_only_the_mesh_is_installed()
    {
        MeshRegion eur = Region("eur", "1", [Zip(("z_eur_00/Earth nav data/+40+000/+45+005.dsf", "dsf"))],
            Zip(("y_eur/Earth nav data/+40+000/+45+005.dsf", "overlay")));

        await Installer().InstallAsync(eur, withOverlays: false, NoProgress, CancellationToken.None);

        Assert.False(Directory.Exists(Path.Join(Packages, "OrthoStream_Overlays")));
        Assert.False(Installer().Installed()["eur"].Overlays);
        Assert.DoesNotContain(_server.Requests, r => r.Url.Contains("overlays", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_partial_download_is_resumed_from_where_it_stopped()
    {
        // Text that does not compress, so the zip is well over the 1000 bytes already there.
        string content = string.Concat(Enumerable.Range(0, 200).Select(_ => Guid.NewGuid().ToString("N")));
        byte[] zip = Zip(("z_eur_00/Earth nav data/+40+000/+45+005.dsf", content));
        MeshRegion eur = Region("eur", "1", [zip]);
        _tree.WriteBytes($"Base Packages/{RegionInstaller.WorkFolderName}/eur/1/zips/z_eur_00.zip", zip[..1000]);

        Assert.Equal(1000, Installer().AlreadyDone(eur, withOverlays: true));
        await Installer().InstallAsync(eur, withOverlays: true, NoProgress, CancellationToken.None);

        Assert.Equal(1000, Assert.Single(_server.Requests, r => r.Url.EndsWith("z_eur_00.zip", StringComparison.Ordinal)).From);
        Assert.Equal(content, File.ReadAllText(Path.Join(Packages, "OrthoStream_Eur", "Earth nav data", "+40+000", "+45+005.dsf")));
    }

    [Fact]
    public async Task A_stopped_install_goes_on_with_the_zips_it_has_not_unpacked()
    {
        MeshRegion eur = Region("eur", "1",
            [Zip(("z_eur_00/Earth nav data/+40+000/+45+005.dsf", "A")), Zip(("z_eur_01/Earth nav data/+50+010/+51+011.dsf", "B"))]);
        using var stop = new CancellationTokenSource();
        // Stops as the second zip is about to be unpacked: the first is unpacked, the second downloaded.
        var progress = new SyncProgress(p =>
        {
            if (p.Stage.Contains("z_eur_01", StringComparison.Ordinal)) stop.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Installer().InstallAsync(eur, true, progress, stop.Token));
        Assert.Empty(Installer().Installed());
        await Installer().InstallAsync(eur, withOverlays: true, NoProgress, CancellationToken.None);

        Assert.Single(_server.Requests, r => r.Url.EndsWith("z_eur_00.zip", StringComparison.Ordinal));
        Assert.Single(_server.Requests, r => r.Url.EndsWith("z_eur_01.zip", StringComparison.Ordinal));
        Assert.Equal("B", File.ReadAllText(Path.Join(Packages, "OrthoStream_Eur", "Earth nav data", "+50+010", "+51+011.dsf")));
    }

    [Fact]
    public async Task The_next_zip_downloads_while_the_one_before_is_unpacked()
    {
        // Many small files: unpacking the first zip takes a while.
        byte[] first = Zip([("z_eur_00/Earth nav data/+40+000/+45+005.dsf", "A"),
            .. Enumerable.Range(0, 500).Select(i => ($"z_eur_00/terrain/{i}_0_BI16.ter", $"ter {i}"))]);
        MeshRegion eur = Region("eur", "1", [first, Zip(("z_eur_01/Earth nav data/+50+010/+51+011.dsf", "B"))]);
        string state = Path.Join(Packages, RegionInstaller.WorkFolderName, "eur", "1", "state.json");
        bool? firstRecordedWhenSecondAsked = null;
        _server.OnRequest = (url, _) =>
        {
            if (url.EndsWith("z_eur_01.zip", StringComparison.Ordinal))
                firstRecordedWhenSecondAsked = File.Exists(state) && File.ReadAllText(state).Contains("z_eur_00.zip", StringComparison.Ordinal);
            return Task.CompletedTask;
        };

        await Installer().InstallAsync(eur, withOverlays: true, NoProgress, CancellationToken.None);

        Assert.False(firstRecordedWhenSecondAsked); // asked before the first zip was unpacked and recorded
        Assert.Equal(500, Directory.GetFiles(Path.Join(Packages, "OrthoStream_Eur", "terrain")).Length);
        Assert.Equal("B", File.ReadAllText(Path.Join(Packages, "OrthoStream_Eur", "Earth nav data", "+50+010", "+51+011.dsf")));
    }

    [Fact]
    public async Task A_damaged_zip_stops_the_download_running_alongside_and_is_reported_as_the_cause()
    {
        byte[] damaged = Encoding.ASCII.GetBytes("this is not a zip at all, just bytes that end the download");
        MeshRegion eur = Region("eur", "1", [damaged, Zip(("z_eur_01/Earth nav data/+50+010/+51+011.dsf", "B"))]);
        // The second zip never comes unless the install stops asking for it.
        _server.OnRequest = (url, cancel) =>
            url.EndsWith("z_eur_01.zip", StringComparison.Ordinal) ? Task.Delay(Timeout.Infinite, cancel) : Task.CompletedTask;

        var error = await Assert.ThrowsAsync<InstallException>(() => Installer().InstallAsync(eur, true, NoProgress, CancellationToken.None))
            .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.IsType<InvalidDataException>(error.InnerException);
        Assert.Empty(Installer().Installed());
    }

    [Fact]
    public async Task A_zip_that_does_not_match_its_published_sha256_is_refused_and_deleted()
    {
        byte[] zip = Zip(("z_na_00/Earth nav data/+40-080/+45-075.dsf", "dsf"));
        _server.Files[Host + "z_na_00.zip.sha256"] = Encoding.ASCII.GetBytes(new string('0', 64) + "  z_na_00.zip\n");
        MeshRegion na = Region("na", "1", [zip]) with
        {
            Mesh = [new MeshAsset("z_na_00.zip", Host + "z_na_00.zip", zip.Length, Host + "z_na_00.zip.sha256")],
        };

        var error = await Assert.ThrowsAsync<InstallException>(() => Installer().InstallAsync(na, true, NoProgress, CancellationToken.None));

        Assert.Contains("SHA-256", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Join(Packages, RegionInstaller.WorkFolderName, "na", "1", "zips", "z_na_00.zip")));
        Assert.False(Directory.Exists(Path.Join(Packages, "OrthoStream_NA")));
    }

    [Fact]
    public async Task A_matching_sha256_is_accepted()
    {
        byte[] zip = Zip(("z_na_00/Earth nav data/+40-080/+45-075.dsf", "dsf"));
        _server.Files[Host + "z_na_00.zip.sha256"] = Encoding.ASCII.GetBytes(Convert.ToHexStringLower(SHA256.HashData(zip)) + "  z_na_00.zip\n");
        MeshRegion na = Region("na", "1", [zip]) with
        {
            Mesh = [new MeshAsset("z_na_00.zip", Host + "z_na_00.zip", zip.Length, Host + "z_na_00.zip.sha256")],
        };

        await Installer().InstallAsync(na, true, NoProgress, CancellationToken.None);

        Assert.True(File.Exists(Path.Join(Packages, "OrthoStream_NA", "Earth nav data", "+40-080", "+45-075.dsf")));
    }

    [Fact]
    public async Task An_entry_pointing_outside_the_package_is_refused()
    {
        MeshRegion eur = Region("eur", "1",
            [Zip(("z_eur_00/Earth nav data/+40+000/+45+005.dsf", "dsf"), ("z_eur_00/../../../evil.txt", "evil"))]);

        await Assert.ThrowsAsync<InstallException>(() => Installer().InstallAsync(eur, true, NoProgress, CancellationToken.None));

        Assert.False(File.Exists(_tree.PathOf("evil.txt")));
        Assert.False(File.Exists(Path.Join(Packages, "evil.txt")));
    }

    [Theory]
    [InlineData("..\\..\\escaped.zip", Host + "z.zip")]
    [InlineData("z_eur_00.zip", "http://example.test/z_eur_00.zip")]
    [InlineData("..", Host + "z.zip")]
    public async Task A_file_list_entry_that_could_write_elsewhere_or_download_insecurely_is_refused(string name, string url)
    {
        // As a tampered copy of the kept list could describe it: it never goes through GitHub's parser.
        var region = new MeshRegion("eur", "1", [new MeshAsset(name, url, 10)], []);

        await Assert.ThrowsAsync<InstallException>(() => Installer().InstallAsync(region, true, NoProgress, CancellationToken.None));

        Assert.Empty(_server.Requests);
        Assert.False(File.Exists(_tree.PathOf("escaped.zip")));
    }

    [Fact]
    public async Task A_region_the_app_does_not_know_is_refused()
    {
        var region = new MeshRegion("..\\evil", "1", [new MeshAsset("z.zip", Host + "z.zip", 10)], []);

        await Assert.ThrowsAsync<InstallException>(() => Installer().InstallAsync(region, true, NoProgress, CancellationToken.None));

        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task An_entry_with_an_unusable_name_is_reported_not_lost()
    {
        MeshRegion eur = Region("eur", "1", [Zip(("z_eur_00/Earth nav data/+40+000/+45+005.dsf", "dsf"), ("z_eur_00/C:/x.txt", "x"))]);

        var error = await Assert.ThrowsAsync<InstallException>(() => Installer().InstallAsync(eur, true, NoProgress, CancellationToken.None));

        Assert.IsType<InvalidDataException>(error.InnerException);
    }

    [Fact]
    public async Task An_update_replaces_the_region_and_its_overlays()
    {
        await Installer().InstallAsync(Region("eur", "1", [Zip(("z_eur_00/Earth nav data/+40+000/+45+005.dsf", "old"), ("z_eur_00/terrain/gone.ter", "gone"))],
            Zip(("y/Earth nav data/+40+000/+45+005.dsf", "old overlay"))), true, NoProgress, CancellationToken.None);
        _server.Files.Clear();

        await Installer().InstallAsync(Region("eur", "2", [Zip(("z_eur_00/Earth nav data/+40+000/+45+005.dsf", "new"))],
            Zip(("y/Earth nav data/+40+000/+45+005.dsf", "new overlay"))), true, NoProgress, CancellationToken.None);

        Assert.Equal("new", File.ReadAllText(Path.Join(Packages, "OrthoStream_Eur", "Earth nav data", "+40+000", "+45+005.dsf")));
        Assert.False(File.Exists(Path.Join(Packages, "OrthoStream_Eur", "terrain", "gone.ter")));
        Assert.Equal("new overlay", File.ReadAllText(Path.Join(Packages, "OrthoStream_Overlays", "Earth nav data", "+40+000", "+45+005.dsf")));
        Assert.Equal("2", Installer().Installed()["eur"].Release);
    }

    [Fact]
    public async Task Deleting_a_region_keeps_the_overlays_of_the_others()
    {
        await Installer().InstallAsync(Region("eur", "1", [Zip(("z_eur_00/Earth nav data/+30+000/+35+005.dsf", "eur"))],
            Zip(("y/Earth nav data/+30+000/+35+005.dsf", "border, Europe's"), ("y/Earth nav data/+40+000/+45+005.dsf", "Europe only"))),
            true, NoProgress, CancellationToken.None);
        await Installer().InstallAsync(Region("afr", "1", [Zip(("z_afr_00/Earth nav data/+30+000/+31+001.dsf", "afr"))],
            Zip(("y/Earth nav data/+30+000/+35+005.dsf", "border, Africa's"), ("y/Earth nav data/+00+010/+05+015.dsf", "Africa only"))),
            true, NoProgress, CancellationToken.None);
        string overlays = Path.Join(Packages, "OrthoStream_Overlays", "Earth nav data");
        Assert.Equal("border, Europe's", File.ReadAllText(Path.Join(overlays, "+30+000", "+35+005.dsf"))); // placed first, kept

        Installer().Delete("afr");

        Assert.False(Directory.Exists(Path.Join(Packages, "OrthoStream_Afr")));
        Assert.False(File.Exists(Path.Join(overlays, "+00+010", "+05+015.dsf")));
        Assert.False(Directory.Exists(Path.Join(overlays, "+00+010")));
        Assert.Equal("border, Europe's", File.ReadAllText(Path.Join(overlays, "+30+000", "+35+005.dsf")));
        Assert.True(File.Exists(Path.Join(overlays, "+40+000", "+45+005.dsf")));

        Installer().Delete("eur");

        Assert.False(Directory.Exists(Path.Join(Packages, "OrthoStream_Overlays")));
        Assert.Empty(Installer().Installed());
    }

    [Fact]
    public async Task While_the_setup_installs_a_region_the_Packages_page_starts_no_other()
    {
        MeshRegion eur = Region("eur", "1", [Zip(("z_eur_00/Earth nav data/+40+000/+45+005.dsf", "A"))]);
        MeshRegion afr = Region("afr", "1", [Zip(("z_afr_00/Earth nav data/+30+000/+31+001.dsf", "B"))]);
        var asked = new TaskCompletionSource();
        _server.OnRequest = async (url, cancel) =>
        {
            if (!url.Contains("z_eur_00", StringComparison.Ordinal)) return;
            asked.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancel); // held until the install is stopped
        };
        var mount = new OrthoStreamXP.Gui.MountController();
        var preferences = new OrthoStreamXP.Gui.WindowPreferences();
        var slot = new OrthoStreamXP.Gui.InstallSlot();
        var setup = new OrthoStreamXP.Gui.PackagesViewModel(mount, preferences, _http, slot, () => Packages);
        var page = new OrthoStreamXP.Gui.PackagesViewModel(mount, preferences, _http, slot, () => Packages);
        var setupRow = new OrthoStreamXP.Gui.RegionRow(setup, eur);
        var pageRow = new OrthoStreamXP.Gui.RegionRow(page, afr);

        Task installing = setup.InstallAsync(setupRow);
        await asked.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.False(page.CanInstall(pageRow));
        await page.InstallAsync(pageRow); // refused: returns at once, nothing asked
        Assert.DoesNotContain(_server.Requests, r => r.Url.Contains("z_afr_00", StringComparison.Ordinal));

        setup.StopInstall();
        await installing.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.True(page.CanInstall(pageRow)); // the slot is free again
    }

    [Fact]
    public void The_downloaded_meshes_are_listed_below_the_persons_own_packages()
    {
        List<string> lines = SceneryPacks.Reorder(["I", "1000 Version", "SCENERY", ""],
            ["OrthoStream_Eur", "OrthoStream_Overlays", "XPME_Europe", "yOrtho4XP_Overlays", "zOrtho4XP_+47+008"]);

        Assert.Equal([
            "I", "1000 Version", "SCENERY",
            "SCENERY_PACK Custom Scenery/OrthoStream_Overlays/",
            "SCENERY_PACK Custom Scenery/yOrtho4XP_Overlays/",
            "SCENERY_PACK Custom Scenery/XPME_Europe/",
            "SCENERY_PACK Custom Scenery/zOrtho4XP_+47+008/",
            "SCENERY_PACK Custom Scenery/OrthoStream_Eur/",
        ], lines);
    }

    [Fact]
    public void The_parts_of_a_split_zip_read_as_one_stream()
    {
        byte[] whole = Encoding.ASCII.GetBytes("0123456789abcdefghij");
        string[] parts = [_tree.WriteBytes("p.00", whole[..7]), _tree.WriteBytes("p.01", whole[7..8]), _tree.WriteBytes("p.02", whole[8..])];
        using var stream = new ConcatenatedStream(parts);

        Assert.Equal(whole.Length, stream.Length);
        stream.Position = 5;
        byte[] read = new byte[10];
        stream.ReadExactly(read);
        Assert.Equal("56789abcde", Encoding.ASCII.GetString(read));
        stream.Seek(-3, SeekOrigin.End);
        Assert.Equal('h', (char)stream.ReadByte());
    }

    // ---------------------------------------------------------------- the tiles AutoOrtho lacks

    [Fact]
    public void The_supplements_list_keeps_the_usable_entries_of_known_regions()
    {
        string json = """
            { "schema": 1, "tag": "v3", "regions": {
              "na": { "version": 2, "tiles": ["+44-122", "+45-123"], "contentHash": "ab12",
                      "mesh": { "name": "z_na_supplement.zip", "url": "https://x.test/z_na_supplement.zip", "size": 100,
                                "sha256Url": "https://x.test/z_na_supplement.zip.sha256" },
                      "overlays": { "name": "y_na_supplement_overlays.zip", "url": "https://x.test/y.zip", "size": 10 } },
              "aus_pac": { "version": 1, "tiles": ["-19-174"], "mesh": { "name": "z.zip", "url": "https://x.test/z.zip", "size": 5 } },
              "mars": { "version": 1, "tiles": ["+00+000"], "mesh": { "name": "z.zip", "url": "https://x.test/z.zip", "size": 5 } },
              "eur": { "version": 1, "tiles": ["+45+005"], "mesh": { "name": "..\\evil.zip", "url": "https://x.test/z.zip", "size": 5 } },
              "afr": { "version": 1, "tiles": ["+30+000"], "mesh": { "name": "z.zip", "url": "http://x.test/z.zip", "size": 5 } },
              "sa": { "version": 0, "tiles": ["-30-060"], "mesh": { "name": "z.zip", "url": "https://x.test/z.zip", "size": 5 } },
              "asi": { "version": "one", "tiles": [], "mesh": {} }
            } }
            """;

        IReadOnlyDictionary<string, RegionSupplement> supplements = SupplementCatalog.Parse(json);

        Assert.Equal(["aus_pac", "na"], supplements.Keys.Order(StringComparer.Ordinal));
        RegionSupplement na = supplements["na"];
        Assert.Equal((2, 2, 110L), (na.Version, na.Tiles.Count, na.DownloadSize(withOverlays: true)));
        Assert.Equal("https://x.test/z_na_supplement.zip.sha256", na.Mesh.Sha256Url);
        Assert.Null(supplements["aus_pac"].Overlays);
    }

    [Fact]
    public void A_supplements_list_of_another_format_gives_none()
    {
        Assert.Empty(SupplementCatalog.Parse("""{ "schema": 2, "regions": { "na": {} } }"""));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => SupplementCatalog.Parse("not json"));
    }

    [Fact]
    public async Task The_tiles_AutoOrtho_lacks_come_with_a_new_install_and_never_replace_its_files()
    {
        // Another region's overlays keep the shared package when North America is deleted: what it placed is seen to go.
        await Installer().InstallAsync(Region("eur", "1", [Zip(("z_eur_00/Earth nav data/+40+000/+45+005.dsf", "eur"))],
            Zip(("y/Earth nav data/+40+000/+45+005.dsf", "eur overlay"))), true, NoProgress, CancellationToken.None);
        MeshRegion na = Region("na", "1",
            [Zip(("z_na_00/Earth nav data/+40-130/+43-124.dsf", "AO dsf"), ("z_na_00/textures/23040_10192_ZL16.png", "AO mask"))],
            Zip(("y/Earth nav data/+40-130/+43-124.dsf", "AO overlay"))) with
        {
            Supplement = Supplement("na", 1,
                Zip(("z_na_supplement/Earth nav data/+40-130/+44-124.dsf", "our dsf"),
                    ("z_na_supplement/Earth nav data/+40-130/+43-124.dsf", "our copy of AO's tile"),
                    ("z_na_supplement/textures/23040_10192_ZL16.png", "our mask"),
                    ("z_na_supplement/textures/23040_10160_ZL16.png", "new mask")),
                Zip(("y_na_supplement_overlays/Earth nav data/+40-130/+44-124.dsf", "our overlay"),
                    ("y_na_supplement_overlays/Earth nav data/+40-130/+43-124.dsf", "our copy of AO's overlay"))),
        };

        await Installer().InstallAsync(na, withOverlays: true, NoProgress, CancellationToken.None);

        string mesh = Path.Join(Packages, "OrthoStream_NA");
        string overlays = Path.Join(Packages, "OrthoStream_Overlays", "Earth nav data", "+40-130");
        Assert.Equal("our dsf", File.ReadAllText(Path.Join(mesh, "Earth nav data", "+40-130", "+44-124.dsf")));
        Assert.Equal("AO dsf", File.ReadAllText(Path.Join(mesh, "Earth nav data", "+40-130", "+43-124.dsf")));
        Assert.Equal("AO mask", File.ReadAllText(Path.Join(mesh, "textures", "23040_10192_ZL16.png")));
        Assert.Equal("new mask", File.ReadAllText(Path.Join(mesh, "textures", "23040_10160_ZL16.png")));
        Assert.Equal("our overlay", File.ReadAllText(Path.Join(overlays, "+44-124.dsf")));
        Assert.Equal("AO overlay", File.ReadAllText(Path.Join(overlays, "+43-124.dsf")));
        Assert.Equal(1, Installer().Installed()["na"].Supplement);
        Assert.False(Directory.Exists(Path.Join(Packages, RegionInstaller.WorkFolderName)));

        Installer().Delete("na");

        Assert.False(Directory.Exists(overlays)); // the supplement's overlays were recorded as North America's
        Assert.True(File.Exists(Path.Join(Packages, "OrthoStream_Overlays", "Earth nav data", "+40+000", "+45+005.dsf")));
    }

    [Fact]
    public async Task A_region_whose_supplement_cannot_be_had_is_installed_without_it_and_gets_it_later()
    {
        byte[] aoZip = Zip(("z_na_00/Earth nav data/+40-130/+43-124.dsf", "AO dsf"));
        MeshRegion na = Region("na", "1", [aoZip]) with
        {
            // Not on the server: its download fails.
            Supplement = new RegionSupplement(1, ["+44-124"], new MeshAsset("z_na_supplement.zip", Host + "z_na_supplement.zip", 50), null),
        };

        await Installer().InstallAsync(na, withOverlays: true, NoProgress, CancellationToken.None);

        InstalledRegion installed = Installer().Installed()["na"];
        Assert.Null(installed.Supplement);
        Assert.True(File.Exists(Path.Join(Packages, "OrthoStream_NA", "Earth nav data", "+40-130", "+43-124.dsf")));

        na = na with { Supplement = Supplement("na", 1, Zip(("z_na_supplement/Earth nav data/+40-130/+44-124.dsf", "our dsf"))) };
        Assert.True(installed.Lacks(na.Supplement));
        _server.Requests.Clear();
        await Installer().InstallAsync(na, withOverlays: true, NoProgress, CancellationToken.None);

        Assert.DoesNotContain(_server.Requests, r => r.Url.Contains("z_na_00", StringComparison.Ordinal)); // only the tiles were downloaded
        Assert.Equal("our dsf", File.ReadAllText(Path.Join(Packages, "OrthoStream_NA", "Earth nav data", "+40-130", "+44-124.dsf")));
        Assert.Equal(("1", 1), (Installer().Installed()["na"].Release, Installer().Installed()["na"].Supplement));
    }

    [Fact]
    public async Task Tiles_added_to_an_installed_region_keep_the_overlays_any_region_placed()
    {
        await Installer().InstallAsync(Region("na", "1", [Zip(("z_na_00/Earth nav data/+40-130/+43-124.dsf", "AO dsf"))],
            Zip(("y/Earth nav data/+40-130/+43-124.dsf", "AO overlay"))), true, NoProgress, CancellationToken.None);
        // Left by an earlier try that stopped before it was recorded: no region placed it, so it is taken over.
        _tree.WriteBytes("Base Packages/OrthoStream_Overlays/Earth nav data/+40-130/+45-124.dsf", Encoding.UTF8.GetBytes("half"));
        RegionSupplement supplement = Supplement("na", 1,
            Zip(("z_na_supplement/Earth nav data/+40-130/+44-124.dsf", "our dsf")),
            Zip(("y/Earth nav data/+40-130/+44-124.dsf", "our overlay"), ("y/Earth nav data/+40-130/+43-124.dsf", "our copy"),
                ("y/Earth nav data/+40-130/+45-124.dsf", "whole")));

        await Installer().AddSupplementAsync("na", supplement, NoProgress, CancellationToken.None);

        string overlays = Path.Join(Packages, "OrthoStream_Overlays", "Earth nav data", "+40-130");
        Assert.Equal("AO overlay", File.ReadAllText(Path.Join(overlays, "+43-124.dsf")));
        Assert.Equal("our overlay", File.ReadAllText(Path.Join(overlays, "+44-124.dsf")));
        Assert.Equal("whole", File.ReadAllText(Path.Join(overlays, "+45-124.dsf")));
        Assert.Equal(1, Installer().Installed()["na"].Supplement);
        Assert.False(Directory.Exists(Path.Join(Packages, RegionInstaller.WorkFolderName)));

        Installer().Delete("na");

        Assert.False(Directory.Exists(Path.Join(Packages, "OrthoStream_Overlays")));
    }

    [Fact]
    public async Task A_newer_version_of_the_tiles_replaces_its_own_files_but_still_not_AutoOrthos()
    {
        MeshRegion na = Region("na", "1",
            [Zip(("z_na_00/Earth nav data/+40-130/+43-124.dsf", "AO dsf"), ("z_na_00/textures/1_2_ZL16.png", "AO mask"))],
            Zip(("y/Earth nav data/+40-130/+43-124.dsf", "AO overlay"))) with
        {
            Supplement = Supplement("na", 1,
                Zip(("z/Earth nav data/+40-130/+44-124.dsf", "ours 1"), ("z/textures/1_2_ZL16.png", "our mask 1")),
                Zip(("y/Earth nav data/+40-130/+44-124.dsf", "our overlay 1"))),
        };
        await Installer().InstallAsync(na, withOverlays: true, NoProgress, CancellationToken.None);
        RegionSupplement second = Supplement("na", 2,
            Zip(("z/Earth nav data/+40-130/+44-124.dsf", "ours 2"), ("z/Earth nav data/+40-130/+43-124.dsf", "ours over AO's"),
                ("z/textures/1_2_ZL16.png", "our mask 2"), ("z/Earth nav data/+40-130/+45-124.dsf", "new in 2")),
            Zip(("y/Earth nav data/+40-130/+44-124.dsf", "our overlay 2"), ("y/Earth nav data/+40-130/+43-124.dsf", "our copy of AO's")));

        await Installer().AddSupplementAsync("na", second, NoProgress, CancellationToken.None);

        string mesh = Path.Join(Packages, "OrthoStream_NA", "Earth nav data", "+40-130");
        string overlays = Path.Join(Packages, "OrthoStream_Overlays", "Earth nav data", "+40-130");
        Assert.Equal("ours 2", File.ReadAllText(Path.Join(mesh, "+44-124.dsf")));
        Assert.Equal("new in 2", File.ReadAllText(Path.Join(mesh, "+45-124.dsf")));
        Assert.Equal("AO dsf", File.ReadAllText(Path.Join(mesh, "+43-124.dsf")));
        Assert.Equal("AO mask", File.ReadAllText(Path.Join(Packages, "OrthoStream_NA", "textures", "1_2_ZL16.png"))); // AutoOrtho's from the start
        Assert.Equal("our overlay 2", File.ReadAllText(Path.Join(overlays, "+44-124.dsf")));
        Assert.Equal("AO overlay", File.ReadAllText(Path.Join(overlays, "+43-124.dsf")));
        InstalledRegion installed = Installer().Installed()["na"];
        Assert.Equal(2, installed.Supplement);
        Assert.Equal(2, installed.SupplementFiles!.Count);
    }

    [Fact]
    public async Task A_supplement_file_named_like_one_of_AutoOrthos_zips_is_refused()
    {
        MeshRegion na = Region("na", "1", [Zip(("z_na_00/Earth nav data/+40-130/+43-124.dsf", "AO dsf"))]) with
        {
            Supplement = new RegionSupplement(1, ["+44-124"], new MeshAsset("z_na_00.zip", Host + "other.zip", 50), null),
        };

        await Assert.ThrowsAsync<InstallException>(() => Installer().InstallAsync(na, true, NoProgress, CancellationToken.None));

        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task At_the_start_the_tiles_are_added_to_regions_installed_without_them()
    {
        await Installer().InstallAsync(Region("na", "1", [Zip(("z_na_00/Earth nav data/+40-130/+43-124.dsf", "AO dsf"))]),
            true, NoProgress, CancellationToken.None);
        Supplement("na", 3, Zip(("z_na_supplement/Earth nav data/+40-130/+44-124.dsf", "our dsf")));
        _server.Files[SupplementCatalog.ManifestUrl] = Encoding.UTF8.GetBytes($$"""
            { "schema": 1, "regions": { "na": { "version": 3, "tiles": ["+44-124"],
              "mesh": { "name": "z_na_supplement.zip", "url": "{{Host}}z_na_supplement.zip", "size": {{_server.Files[Host + "z_na_supplement.zip"].Length}} } } } }
            """);
        var slot = new OrthoStreamXP.Gui.InstallSlot();
        var catalog = new SupplementCatalog(_http, _tree.PathOf("mesh-supplements.json"));
        var updater = new OrthoStreamXP.Gui.SupplementUpdater(_http, catalog, slot, () => Packages);

        Assert.Equal(["North America"], await updater.RunAsync(NoProgress, CancellationToken.None));

        Assert.Equal("our dsf", File.ReadAllText(Path.Join(Packages, "OrthoStream_NA", "Earth nav data", "+40-130", "+44-124.dsf")));
        Assert.Equal(3, Installer().Installed()["na"].Supplement);
        Assert.False(slot.Busy);
        _server.Requests.Clear();
        Assert.Empty(await updater.RunAsync(NoProgress, CancellationToken.None)); // has them now: nothing is downloaded
        Assert.DoesNotContain(_server.Requests, r => r.Url.EndsWith(".zip", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_the_supplements_list_nothing_is_added_at_the_start()
    {
        await Installer().InstallAsync(Region("na", "1", [Zip(("z_na_00/Earth nav data/+40-130/+43-124.dsf", "AO dsf"))]),
            true, NoProgress, CancellationToken.None);
        var catalog = new SupplementCatalog(_http, _tree.PathOf("mesh-supplements.json"));
        var updater = new OrthoStreamXP.Gui.SupplementUpdater(_http, catalog, new OrthoStreamXP.Gui.InstallSlot(), () => Packages);

        Assert.Empty(await updater.RunAsync(NoProgress, CancellationToken.None)); // the list is not on the server

        Assert.Null(Installer().Installed()["na"].Supplement);
    }

    [Fact]
    public void A_region_installed_without_its_supplement_offers_an_update_of_the_tiles_alone()
    {
        var page = new OrthoStreamXP.Gui.PackagesViewModel(new OrthoStreamXP.Gui.MountController(), new OrthoStreamXP.Gui.WindowPreferences(),
            _http, new OrthoStreamXP.Gui.InstallSlot(), () => Packages);
        MeshRegion na = Region("na", "1", [Zip(("z_na_00/Earth nav data/+40-130/+43-124.dsf", "AO dsf"))]) with
        {
            Supplement = Supplement("na", 2, Zip(("z_na_supplement/Earth nav data/+40-130/+44-124.dsf", "our dsf"))),
        };
        var row = new OrthoStreamXP.Gui.RegionRow(page, na) { Installed = new InstalledRegion("na", "1", true, DateTime.UtcNow, Supplement: 1) };

        Assert.False(row.UpToDate);
        Assert.True(row.OnlyTilesLacking);
        Assert.Equal("Update", row.InstallLabel);
        Assert.Contains("tiles AutoOrtho lacks", row.Status, StringComparison.Ordinal);

        row.Installed = row.Installed with { Supplement = 2 };

        Assert.True(row.UpToDate);
        Assert.False(row.OnlyTilesLacking);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>The tiles AutoOrtho lacks for a region, served by the fake server (the zips' names as the scenery repository publishes them).</summary>
    private RegionSupplement Supplement(string id, int version, byte[] meshZip, byte[]? overlaysZip = null)
    {
        string mesh = $"z_{id}_supplement.zip";
        _server.Files[Host + mesh] = meshZip;
        MeshAsset? overlays = null;
        if (overlaysZip is not null)
        {
            string name = $"y_{id}_supplement_overlays.zip";
            _server.Files[Host + name] = overlaysZip;
            overlays = new MeshAsset(name, Host + name, overlaysZip.Length);
        }
        return new RegionSupplement(version, ["+44-124"], new MeshAsset(mesh, Host + mesh, meshZip.Length), overlays);
    }

    /// <summary>A region whose files the fake server serves; its overlays zip cut in <paramref name="parts"/> parts.</summary>
    private MeshRegion Region(string id, string release, byte[][] meshZips, byte[]? overlaysZip = null, int parts = 1)
    {
        var mesh = new List<MeshAsset>();
        for (int i = 0; i < meshZips.Length; i++)
        {
            string name = $"z_{id}_{i:00}.zip";
            _server.Files[Host + name] = meshZips[i];
            mesh.Add(new MeshAsset(name, Host + name, meshZips[i].Length));
        }
        var overlays = new List<MeshAsset>();
        if (overlaysZip is not null)
        {
            int size = (overlaysZip.Length + parts - 1) / parts;
            for (int p = 0; p < parts; p++)
            {
                byte[] part = overlaysZip[(p * size)..Math.Min(overlaysZip.Length, (p + 1) * size)];
                string name = $"y_{id}_overlays.zip.{p:00}";
                _server.Files[Host + name] = part;
                overlays.Add(new MeshAsset(name, Host + name, part.Length));
            }
        }
        return new MeshRegion(id, release, mesh, overlays);
    }

    private static byte[] Zip(params (string Name, string Text)[] entries)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, text) in entries)
            {
                using Stream entry = zip.CreateEntry(name).Open();
                entry.Write(Encoding.UTF8.GetBytes(text));
            }
        return memory.ToArray();
    }

    /// <summary>Progress reported on the installer's own thread, so a test can act on a stage at once.</summary>
    private sealed class SyncProgress(Action<InstallProgress> report) : IProgress<InstallProgress>
    {
        public void Report(InstallProgress value) => report(value);
    }

    /// <summary>GitHub's release downloads, from memory: whole files, or the rest of one from a range's start.</summary>
    private sealed class FakeReleases : HttpMessageHandler
    {
        public Dictionary<string, byte[]> Files { get; } = [];
        public List<(string Url, long? From)> Requests { get; } = [];

        /// <summary>Called as a request arrives, before it is answered: a test can look at the disk, or hold the answer.</summary>
        public Func<string, CancellationToken, Task>? OnRequest { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            long? from = request.Headers.Range?.Ranges.First().From;
            lock (Requests) Requests.Add((url, from));
            if (OnRequest is { } onRequest) await onRequest(url, cancellationToken);
            return Answer(url, from);
        }

        private HttpResponseMessage Answer(string url, long? from)
        {
            if (!Files.TryGetValue(url, out byte[]? data)) return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (from is not long start) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };
            var partial = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(data[(int)start..]) };
            partial.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, data.Length - 1, data.Length);
            return partial;
        }
    }
}
