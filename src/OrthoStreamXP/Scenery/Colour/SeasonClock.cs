using System.Collections.Concurrent;
using System.Globalization;
using OrthoStreamXP.Textures;

namespace OrthoStreamXP.Scenery;

/// <summary>The seasons settings: on or off, how strong (0-1), and a season chosen by hand.</summary>
internal sealed record SeasonOptions(bool On, double Strength, SeasonChoice Choice)
{
    public const double DefaultStrength = 80;

    public static readonly SeasonOptions Off = new(false, DefaultStrength / 100, SeasonChoice.Auto);

    /// <summary>The season follows X-Plane's date (by the calendar, or with the satellite record), not a season chosen by hand.</summary>
    public bool FollowsDate => Choice is SeasonChoice.Auto or SeasonChoice.Satellite;
}

/// <summary>
/// Which season the photo textures show, and each texture's lookup table for it and the exposure (<see cref="PhotoColours"/>). The date is X-Plane's
/// (sim/time/local_date_days), taken when a flight starts, so every texture of a flight is coloured for the same day:
/// a date changed in X-Plane shows from the next flight or move to another place. Until X-Plane sends a date, the date
/// of the last flight is used (kept in the history folder), else today's.
/// <para>
/// With <see cref="SeasonChoice.Satellite"/> each place's grass and fields go by its satellite record
/// (<see cref="ObservedSeason"/>), where it has one; else by the calendar. A texture keeps the table it first got for the
/// day, so a record arriving while X-Plane reads it never gives its levels different colours.
/// </para>
/// </summary>
internal sealed class SeasonClock
{
    /// <summary>The file in the history folder that keeps the last X-Plane date.</summary>
    public const string DayFileName = "sim-date.txt";

    private readonly SeasonOptions _options;
    private readonly double _exposure;
    private readonly string? _dayFile;
    private readonly ObservedSeasons? _observed;
    /// <summary>With the satellite record: each texture's table for the day it was first read on.</summary>
    private readonly ConcurrentDictionary<TextureName, (int Day, ushort[] Table)> _tables = new();
    private int _day;
    private bool _fromXPlane;
    private long _bySatellite, _byCalendar;
    /// <summary>Whether a texture has been coloured for the day in use (X-Plane keeps what it has read).</summary>
    private volatile bool _coloured;

    /// <param name="dayFile">Where the latest X-Plane date is kept between runs.</param>
    /// <param name="observed">The satellite record, with <see cref="SeasonChoice.Satellite"/>; null goes by the calendar.</param>
    /// <param name="exposure">The exposure in percent, made into the same table as the season and the contrast.</param>
    public SeasonClock(SeasonOptions options, string? dayFile, ObservedSeasons? observed = null, double exposure = Exposure.Full)
    {
        _options = options;
        _exposure = exposure;
        _dayFile = dayFile;
        _observed = options.Choice == SeasonChoice.Satellite ? observed : null;
        _day = Remembered(dayFile) ?? DateTime.Now.DayOfYear - 1;
    }

    /// <summary>The day of the year in use (0 is 1 January).</summary>
    public int Day => Volatile.Read(ref _day);

    public SeasonOptions Options => _options;

    /// <summary>
    /// A date from X-Plane: used if it is the first, or a flight is starting (a new flight, or a move to another place).
    /// Returns true when it changes the season of textures X-Plane has already read: X-Plane sends its date only once a
    /// flight has loaded, so the first area of a flight on another date than the last one is coloured for the old date
    /// until X-Plane reads it again.
    /// </summary>
    public bool OfferDate(int dayOfYear, bool newFlight)
    {
        if (dayOfYear is < 0 or > 365 || (_fromXPlane && !newFlight)) return false;
        bool first = !_fromXPlane;
        _fromXPlane = true;
        if (dayOfYear == Day && !first) return false;
        if (first || dayOfYear != Day)
            Log.Step($"Seasons: X-Plane's date is {Describe(dayOfYear)}" +
                     (_options.FollowsDate ? "" : $" (the season is set by hand: {_options.Choice})"));
        if (dayOfYear == Day) return false;
        Volatile.Write(ref _day, dayOfYear);
        _tables.Clear(); // a new flight: every texture is read again
        bool stale = _coloured && _options.FollowsDate;
        _coloured = false;
        try
        {
            if (_dayFile is not null) File.WriteAllText(_dayFile, dayOfYear.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only a convenience for the next start.
        }
        return stale;
    }

    /// <summary>The season at a place by the calendar (or chosen by hand) in the settings' strength (summer when it changes nothing).</summary>
    public SeasonState StateAt(double lat) =>
        (_options.FollowsDate ? SeasonModel.For(Day, lat) : SeasonModel.For(_options.Choice)).Scaled(_options.Strength);

    /// <summary>A texture's lookup table for the season at its centre, the contrast and the exposure (<see cref="PhotoColours.TableFor"/>).</summary>
    public ushort[] TableFor(TextureName name)
    {
        if (!_coloured) _coloured = true;
        if (_observed is null) return PhotoColours.TableFor(StateAt(name.Center.Lat), _exposure);
        int day = Day;
        if (_tables.TryGetValue(name, out var kept) && kept.Day == day) return kept.Table;
        var (lat, lon) = name.Center;
        GreennessYear? year = _observed.YearAt(lat, lon);
        Interlocked.Increment(ref year is null ? ref _byCalendar : ref _bySatellite);
        SeasonState state = year is null ? SeasonModel.For(day, lat) : ObservedSeason.For(year, day, lat);
        ushort[] table = PhotoColours.TableFor(state.Scaled(_options.Strength), _exposure);
        // The first table wins: two reads at once of a texture still give X-Plane the same colours.
        return _tables.GetOrAdd(name, (day, table)) is var (keptDay, keptTable) && keptDay == day ? keptTable : table;
    }

    /// <summary>"1,204 textures coloured by the satellite record, 36 by the calendar alone", or null without the record.</summary>
    public string? Summary() => _observed is null ? null : string.Create(CultureInfo.InvariantCulture,
        $"Seasons: {Interlocked.Read(ref _bySatellite):N0} textures coloured by the satellite record, {Interlocked.Read(ref _byCalendar):N0} by the calendar alone (satellite record of {_observed.TilesLoaded} area(s) of about 500 km, none for {_observed.TilesMissing})");

    /// <summary>"27 October" for day 299.</summary>
    public static string Describe(int dayOfYear) =>
        new DateTime(2025, 1, 1).AddDays(Math.Clamp(dayOfYear, 0, 364)).ToString("d MMMM", CultureInfo.InvariantCulture);

    private static int? Remembered(string? file)
    {
        try
        {
            return file is not null && File.Exists(file) && int.TryParse(File.ReadAllText(file).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int day)
                   && day is >= 0 and <= 365 ? day : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
