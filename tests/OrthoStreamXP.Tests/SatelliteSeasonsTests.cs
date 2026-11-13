using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using OrthoStreamXP.Scenery;
using OrthoStreamXP.Textures;

namespace OrthoStreamXP.Tests;

/// <summary>
/// The seasons timed by the satellite record: a place's year of greenness, its season against the calendar, NASA's
/// pictures read into cells, the record downloaded and kept, and the textures' tables.
/// </summary>
public sealed class SatelliteSeasonsTests : IDisposable
{
    private readonly TempTree _tree = new();

    public void Dispose() => _tree.Dispose();

    private static int Day(int month, int day) => new DateTime(2025, month, day).DayOfYear - 1;

    // NASA's VIIRS monthly greenness of real places, October 2025 to September 2026, January first.
    private static readonly double[] Warsaw = [0.02, 0.04, 0.27, 0.40, 0.53, 0.58, 0.51, 0.52, 0.53, 0.44, 0.32, 0.27];
    private static readonly double[] Wroclaw = [0.44, 0.43, 0.43, 0.54, 0.68, 0.74, 0.71, 0.69, 0.69, 0.62, 0.55, 0.50]; // farmland with winter crops
    private static readonly double[] Brest = [0.51, 0.51, 0.55, 0.59, 0.59, 0.60, 0.58, 0.54, 0.54, 0.51, 0.50, 0.50];
    private static readonly double[] Brittany = [0.46, 0.74, 0.73, 0.74, 0.76, 0.73, 0.63, 0.54, 0.58, 0.78, 0.71, 0.73];
    private static readonly double[] Kenya = [0.42, 0.34, 0.65, 0.62, 0.68, 0.57, 0.44, 0.31, 0.29, 0.33, 0.40, 0.34];

    private static GreennessYear Year(double[] months) => GreennessYear.From(months)!;

    // ---------------------------------------------------------------- a year of greenness

    [Fact]
    public void A_missing_month_is_drawn_between_its_neighbours_and_too_many_missing_is_no_year()
    {
        double[] months = [.. Warsaw];
        months[5] = double.NaN;
        Assert.InRange(Year(months).At(Day(6, 15)), 0.51, 0.53);

        double[] gappy = [.. Warsaw];
        for (int m = 0; m < 4; m++) gappy[m * 3] = double.NaN;
        Assert.Null(GreennessYear.From(gappy));
    }

    [Fact]
    public void One_odd_month_is_evened_out_but_a_season_of_two_and_the_greenest_month_stay()
    {
        double[] cloudy = [.. Enumerable.Repeat(0.7, 12)];
        cloudy[0] = 0.3; // one cloudy or flooded January
        Assert.True(Year(cloudy).Range < 0.01);

        double[] winter = [.. Enumerable.Repeat(0.7, 12)];
        winter[0] = winter[1] = 0.3;
        Assert.InRange(Year(winter).Range, 0.39, 0.41);

        Assert.Equal(0.58, Year(Warsaw).Peak, 3); // June, greener than May and July
    }

    [Fact]
    public void The_greenness_runs_between_the_middles_of_the_months_and_round_the_year()
    {
        GreennessYear year = Year([0.0, 0.0, 0.0, 0.0, 0.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 0.0]);
        Assert.Equal(0, year.ShareOfPeak(Day(3, 15)), 2);
        Assert.Equal(1, year.ShareOfPeak(Day(8, 15)), 2);
        Assert.InRange(year.ShareOfPeak(Day(5, 31)), 0.4, 0.6);
        Assert.Equal(year.At(0), year.At(365), 6);
        Assert.Equal(1, Year(new double[12]).ShareOfPeak(Day(1, 15))); // never green: nothing to rest from
    }

    // ---------------------------------------------------------------- the season against the calendar

    [Fact]
    public void The_trees_keep_the_calendar_where_the_fields_would_mistime_them()
    {
        // Wroclaw's winter crops and late crop growth once made its spring five weeks late: bare trees in mid-May.
        foreach (int day in new[] { Day(4, 20), Day(5, 15), Day(10, 15), Day(11, 6) })
        {
            SeasonState calendar = SeasonModel.For(day, 51.15), observed = ObservedSeason.For(Year(Wroclaw), day, 51.15);
            Assert.Equal((calendar.Spring, calendar.Autumn, calendar.Bare), (observed.Spring, observed.Autumn, observed.Bare));
        }
        Assert.True(ObservedSeason.For(Year(Wroclaw), Day(5, 15), 51.15).Spring > 0.9);
    }

    [Fact]
    public void A_snowy_winter_rests_fully_and_the_summer_is_the_photos()
    {
        SeasonState jan = ObservedSeason.For(Year(Warsaw), Day(1, 15), 52.1);

        Assert.True(jan.Bare > 0.9 && jan.Dormant > 0.95, $"January: {jan}");
        Assert.True(ObservedSeason.For(Year(Warsaw), Day(7, 15), 52.1).IsSummer);
    }

    [Fact]
    public void A_coast_green_all_year_stays_green_in_winter()
    {
        SeasonState calendar = SeasonModel.For(Day(1, 15), 48.37), observed = ObservedSeason.For(Year(Brest), Day(1, 15), 48.37);

        Assert.True(calendar.Dormant > 0.9);
        Assert.True(observed.Dormant < 0.2, $"January's grass and fields green: {observed}");
        Assert.Equal(calendar.Bare, observed.Bare); // the broadleaf trees are bare all the same
    }

    [Fact]
    public void A_dry_summer_dries_the_fields_without_autumn_leaves()
    {
        SeasonState august = ObservedSeason.For(Year(Brittany), Day(8, 20), 48.2);

        Assert.True(SeasonModel.For(Day(8, 20), 48.2).IsSummer);
        Assert.True(august.Dormant > 0.4, $"August's dry grass and harvested fields: {august}");
        Assert.Equal(0, august.Autumn + august.Bare + august.Spring);
    }

    [Fact]
    public void The_tropics_show_their_dry_season_on_the_grass_and_leave_the_trees()
    {
        SeasonState dry = ObservedSeason.For(Year(Kenya), Day(8, 20), -1.5), wet = ObservedSeason.For(Year(Kenya), Day(5, 1), -1.5);

        Assert.True(SeasonModel.For(Day(8, 20), -1.5).IsSummer);
        Assert.True(dry.Dormant > 0.9, $"the dry season: {dry}");
        Assert.Equal(0, dry.Bare + dry.Autumn + dry.Spring);
        Assert.True(wet.IsSummer, $"the rains: {wet}");
    }

    [Fact]
    public void A_place_never_very_green_keeps_the_calendar()
    {
        GreennessYear town = Year([0.10, 0.11, 0.12, 0.16, 0.20, 0.25, 0.25, 0.24, 0.20, 0.16, 0.12, 0.10]);

        foreach (int day in new[] { Day(1, 15), Day(4, 20), Day(7, 15), Day(10, 15) })
            Assert.Equal(SeasonModel.For(day, 50), ObservedSeason.For(town, day, 50));
        Assert.StartsWith("too little vegetation", ObservedSeason.Describe(town, Day(1, 15)), StringComparison.Ordinal);
    }

    [Fact]
    public void The_preview_says_how_green_the_place_is_against_its_greenest_month()
    {
        Assert.Equal("85% as green as in its greenest month: grass and fields resting 8%", ObservedSeason.Describe(Year(Brest), Day(1, 15)));
    }

    // ---------------------------------------------------------------- NASA's pictures

    [Fact]
    public void A_picture_is_averaged_into_cells_with_water_as_zero_and_no_data_as_nothing()
    {
        int green = NdviPictures.RefOf(0.6), dry = NdviPictures.RefOf(0.2);
        byte[] png = NdviPictures.Picture((x, y) => x < 128 ? NdviPictures.NoData : x < 256 ? NdviPictures.Water : x < 384 ? green
            // the last cells: alternate pixels dry and green, and the very last row of cells mostly without data
            : y >= 508 ? (x % 4 == 0 && y % 4 == 0 ? green : NdviPictures.NoData) : (x + y) % 2 == 0 ? dry : green);

        byte[] cells = VegetationTile.CellsOf(png);

        Assert.Equal(NdviPalette.NoData, cells[10 * VegetationTile.Cells + 5]);
        Assert.Equal(0, NdviPalette.ValueOf(cells[10 * VegetationTile.Cells + 40]), 3);
        Assert.Equal(0.6, NdviPalette.ValueOf(cells[10 * VegetationTile.Cells + 80]), 2);
        Assert.Equal(0.4, NdviPalette.ValueOf(cells[10 * VegetationTile.Cells + 110]), 2);
        Assert.Equal(NdviPalette.NoData, cells[127 * VegetationTile.Cells + 110]);
        Assert.Throws<InvalidDataException>(() => VegetationTile.CellsOf(NdviPictures.Picture((_, _) => green, size: 256)));
        Assert.Throws<InvalidDataException>(() => VegetationTile.CellsOf([1, 2, 3]));
    }

    [Fact]
    public void A_town_takes_the_season_of_the_fields_around_it()
    {
        var key = VegetationKey.At(52.2, 21.0);
        byte[]?[] months = new byte[]?[12];
        for (int m = 0; m < 12; m++)
        {
            months[m] = new byte[VegetationTile.MonthBytes];
            Array.Fill(months[m]!, NdviPalette.CodeOf(Warsaw[m]));
        }
        // The town's cell: grey all year.
        int cx = (int)((21.0 - key.West) / VegetationKey.Span * VegetationTile.Cells), cy = (int)((key.North - 52.2) / VegetationKey.Span * VegetationTile.Cells);
        foreach (byte[]? month in months) month![cy * VegetationTile.Cells + cx] = NdviPalette.CodeOf(0.15);

        GreennessYear year = new VegetationTile(key, months).YearAt(52.2, 21.0)!;

        Assert.True(year.Range > 0.45, $"range {year.Range}");
        Assert.Null(new VegetationTile(key, new byte[]?[12]).YearAt(52.2, 21.0));
    }

    [Fact]
    public void A_place_is_in_the_tile_NASA_numbers_it_by()
    {
        var key = VegetationKey.At(52.2, 21.0);
        Assert.Equal(new VegetationKey(8, 44), key);
        Assert.Equal(new VegetationKey(39, 0), VegetationKey.At(-90, -180));
        Assert.Equal(new VegetationKey(0, 79), VegetationKey.At(90, 180));
        Assert.Equal("https://gibs.earthdata.nasa.gov/wmts/epsg4326/best/VIIRS_SNPP_L3_NDVI_Monthly/default/2026-07-01/1km/6/8/44.png",
            VegetationSource.UriOf(key, new DateOnly(2026, 7, 1)).ToString());
    }

    // ---------------------------------------------------------------- downloaded and kept

    private static readonly FixedTime November = new(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void The_year_is_the_latest_twelve_months_NASA_has_finished()
    {
        using var source = new VegetationSource(_tree.PathOf("vegetation"), November, download: false);
        IReadOnlyList<DateOnly> year = source.Year();

        Assert.Equal(new DateOnly(2025, 11, 1), year[0]);
        Assert.Equal(new DateOnly(2026, 10, 1), year[^1]);
        Assert.Equal(12, year.Distinct().Count());
    }

    [Fact]
    public async Task A_tiles_year_is_downloaded_once_and_then_read_from_disk()
    {
        var server = new FakeGibs(month => Warsaw[month.Month - 1]);
        var key = VegetationKey.At(52.2, 21.0);
        using (var source = new VegetationSource(_tree.PathOf("vegetation"), November, handler: server))
        {
            VegetationTile tile = (await source.LoadAsync(key, TestContext.Current.CancellationToken))!;
            Assert.Equal(12, tile.Months);
            Assert.Equal(12, source.Downloaded);
            Assert.Equal(0.58, tile.YearAt(52.2, 21.0)!.Peak, 2);
        }
        Assert.Equal(12, server.Requests.Count);
        Assert.All(server.Requests, uri => Assert.Contains("/1km/6/8/44.png", uri.ToString(), StringComparison.Ordinal));

        using var offline = new VegetationSource(_tree.PathOf("vegetation"), November, download: false);
        VegetationTile again = (await offline.LoadAsync(key, TestContext.Current.CancellationToken))!;
        Assert.Equal(0.58, again.YearAt(52.2, 21.0)!.Peak, 2);
    }

    [Fact]
    public async Task A_month_NASA_does_not_have_is_taken_from_the_year_before()
    {
        var server = new FakeGibs(month => Warsaw[month.Month - 1], status: month => month == new DateOnly(2026, 10, 1) ? HttpStatusCode.NotFound : HttpStatusCode.OK);
        using var source = new VegetationSource(_tree.PathOf("vegetation"), November, handler: server);

        VegetationTile tile = (await source.LoadAsync(VegetationKey.At(52.2, 21.0), TestContext.Current.CancellationToken))!;

        Assert.Equal(12, tile.Months);
        Assert.Contains(server.Requests, uri => uri.ToString().Contains("/2025-10-01/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Offline_a_new_month_is_stood_in_for_by_the_same_month_a_year_before()
    {
        var key = VegetationKey.At(52.2, 21.0);
        using (var lastYear = new VegetationSource(_tree.PathOf("vegetation"), new FixedTime(November.GetUtcNow().AddYears(-1)), handler: new FakeGibs(month => Warsaw[month.Month - 1])))
            Assert.NotNull(await lastYear.LoadAsync(key, TestContext.Current.CancellationToken));
        var offline = new FakeGibs(_ => 0.5, status: _ => HttpStatusCode.ServiceUnavailable);
        using var source = new VegetationSource(_tree.PathOf("vegetation"), November, handler: offline);

        VegetationTile tile = (await source.LoadAsync(key, TestContext.Current.CancellationToken))!;

        Assert.Equal(12, tile.Months);
        Assert.Equal(0.58, tile.YearAt(52.2, 21.0)!.Peak, 2);
        Assert.Equal(12, offline.Requests.Count); // each new month tried once, the year before read from disk
    }

    [Fact]
    public async Task Without_NASA_there_is_no_record_and_nothing_is_kept()
    {
        var server = new FakeGibs(_ => 0.5, status: _ => HttpStatusCode.ServiceUnavailable);
        using var source = new VegetationSource(_tree.PathOf("vegetation"), November, handler: server);

        Assert.Null(await source.LoadAsync(VegetationKey.At(52.2, 21.0), TestContext.Current.CancellationToken));
        Assert.Equal(12, server.Requests.Count); // not asked again for the year before
        Assert.False(Directory.Exists(_tree.PathOf("vegetation")) && Directory.EnumerateFiles(_tree.PathOf("vegetation"), "*.ndvi", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public void Months_older_than_the_year_before_are_removed()
    {
        foreach (string month in new[] { "2024-10", "2024-11", "2025-11", "2026-10", "notes" })
            _tree.WriteText($"vegetation/{month}/8_44.ndvi", "x");
        using var source = new VegetationSource(_tree.PathOf("vegetation"), November, download: false);

        source.RemoveOldMonths();

        Assert.Equal(["2024-11", "2025-11", "2026-10", "notes"],
            Directory.EnumerateDirectories(_tree.PathOf("vegetation")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    // ---------------------------------------------------------------- the textures

    [Fact]
    public void A_texture_keeps_its_first_table_while_a_record_arrives_and_the_next_one_gets_the_record()
    {
        var release = new TaskCompletionSource();
        var server = new FakeGibs(month => Brittany[month.Month - 1], hold: release.Task);
        var source = new VegetationSource(_tree.PathOf("vegetation"), November, handler: server);
        using var observed = new ObservedSeasons(source, TimeSpan.Zero);
        var clock = new SeasonClock(new SeasonOptions(true, 1, SeasonChoice.Satellite), null, observed);
        clock.OfferDate(Day(1, 15), newFlight: true);
        Assert.True(TextureName.TryParseFile("21840_35584_BI16.dds", out TextureName first));
        Assert.True(TextureName.TryParseFile("21840_35600_BI16.dds", out TextureName second));
        ushort[]? calendar = Seasons.TableFor(SeasonModel.For(Day(1, 15), first.Center.Lat));

        Assert.Same(calendar, clock.TableFor(first)); // the record is on its way: the calendar
        release.SetResult();
        Assert.True(SpinWait.SpinUntil(() => observed.TilesLoaded == 1, TimeSpan.FromSeconds(10)));

        Assert.Same(calendar, clock.TableFor(first));
        Assert.NotSame(calendar, clock.TableFor(second));
        Assert.Contains("1 textures coloured by the satellite record, 1 by the calendar alone", clock.Summary(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_date_arriving_after_textures_were_coloured_for_another_asks_for_them_again()
    {
        Assert.True(TextureName.TryParseFile("21840_35584_BI16.dds", out TextureName name));
        string file = _tree.WriteText("sim-date.txt", Day(11, 6).ToString(CultureInfo.InvariantCulture));

        var clock = new SeasonClock(new SeasonOptions(true, 1, SeasonChoice.Auto), file);
        clock.TableFor(name); // coloured for the last flight's 6 November
        Assert.True(clock.OfferDate(Day(1, 15), newFlight: false));
        clock.TableFor(name);
        Assert.False(clock.OfferDate(Day(1, 15), newFlight: true)); // the same date again
        Assert.True(clock.OfferDate(Day(7, 1), newFlight: true));   // a new flight on another date, after its first reads

        var unread = new SeasonClock(new SeasonOptions(true, 1, SeasonChoice.Auto), file);
        Assert.False(unread.OfferDate(Day(3, 1), newFlight: false)); // nothing read yet: nothing to read again

        var byHand = new SeasonClock(new SeasonOptions(true, 1, SeasonChoice.Winter), file);
        byHand.TableFor(name);
        Assert.False(byHand.OfferDate(Day(3, 1), newFlight: false)); // the date does not matter
    }

    [Fact]
    public void Without_the_satellite_choice_the_record_is_not_used()
    {
        var source = new VegetationSource(_tree.PathOf("vegetation"), November, download: false);
        using var observed = new ObservedSeasons(source, TimeSpan.Zero);
        var clock = new SeasonClock(new SeasonOptions(true, 1, SeasonChoice.Auto), null, observed);

        Assert.True(TextureName.TryParseFile("21840_35584_BI16.dds", out TextureName name));
        Assert.Same(Seasons.TableFor(clock.StateAt(name.Center.Lat)), clock.TableFor(name));
        Assert.Null(clock.Summary());
    }

    [Fact]
    public void X_Plane_is_asked_to_reload_its_scenery_as_its_UDP_interface_documents()
    {
        Assert.Equal("CMND\0sim/operation/reload_scenery"u8.ToArray(), XPlaneUdp.CommandRequest(XPlaneUdp.ReloadSceneryCommand));
    }

    [Fact]
    public void The_settings_choose_the_satellite_record()
    {
        SeasonOptions options = MountSettings.ParseSeasons(TestSettings.Of("seasons = on", "season = satellite"));

        Assert.Equal(SeasonChoice.Satellite, options.Choice);
        Assert.True(options.FollowsDate);
        Assert.False(new SeasonOptions(true, 1, SeasonChoice.Winter).FollowsDate);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Stands in for NASA GIBS: each month's picture of one greenness everywhere.</summary>
    private sealed class FakeGibs(Func<DateOnly, double> ndvi, Func<DateOnly, HttpStatusCode>? status = null, Task? hold = null) : HttpMessageHandler
    {
        public ConcurrentQueue<Uri> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!;
            Requests.Enqueue(uri);
            if (hold is not null) await hold.WaitAsync(cancellationToken);
            DateOnly month = DateOnly.ParseExact(uri.Segments[^5].TrimEnd('/'), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            HttpStatusCode code = status?.Invoke(month) ?? HttpStatusCode.OK;
            return new HttpResponseMessage(code) { Content = new ByteArrayContent(code == HttpStatusCode.OK ? NdviPictures.Uniform(ndvi(month)) : []) };
        }
    }
}
