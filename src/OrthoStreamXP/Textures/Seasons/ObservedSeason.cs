namespace OrthoStreamXP.Textures;

/// <summary>
/// The season at a place by its satellite record (<see cref="GreennessYear"/>) as well as by the calendar.
/// <para>
/// The trees keep the calendar's colours and times (<see cref="SeasonModel"/>): a month's greenness over 1 km of mixed
/// country is mostly its fields and its snow, and cannot tell when the trees leaf out (Wrocław's winter crops and late
/// crop growth would put its spring five weeks late).
/// </para>
/// <para>
/// Grass and fields go by the record: as green as in the place's greenest month (when most photos are taken), they stay
/// green; much less green, they rest (olive and straw). So a coast green all winter (Brittany: January 85% of June)
/// stays green, a snowy or ploughed winter (Warsaw: 7%) rests fully, and a dry summer or the tropics' dry season shows
/// as it was, where the calendar has nothing or the opposite. A place with little vegetation even at its greenest (a
/// town, rock, sand) keeps the calendar.
/// </para>
/// </summary>
internal static class ObservedSeason
{
    /// <summary>The season on a day at a place with this record (its strength not yet applied).</summary>
    /// <param name="dayOfYear">0 is 1 January.</param>
    public static SeasonState For(GreennessYear year, int dayOfYear, double lat)
    {
        SeasonState calendar = SeasonModel.For(dayOfYear, lat);
        double sure = Vegetated(year);
        if (sure <= 0) return calendar;
        return calendar with { Dormant = calendar.Dormant + (Resting(year, dayOfYear) - calendar.Dormant) * sure };
    }

    /// <summary>How much the grass and fields rest on a day, 0 to 1: none at 90% or more of the greenest month, fully at 60% or less.</summary>
    public static double Resting(GreennessYear year, double dayOfYear) => 1 - SeasonModel.Smooth(0.6, 0.9, year.ShareOfPeak(dayOfYear));

    /// <summary>0 to 1: how much the record is of vegetation, by its greenest month (a town about 0.2, fields and forest 0.5 to 0.9).</summary>
    public static double Vegetated(GreennessYear year) => SeasonModel.Smooth(0.25, 0.45, year.Peak);

    /// <summary>"85% as green as in its greenest month: grass resting 7%", for the colour preview.</summary>
    public static string Describe(GreennessYear year, double dayOfYear) => Vegetated(year) <= 0
        ? "too little vegetation here to tell: the calendar's season"
        : FormattableString.Invariant($"{year.ShareOfPeak(dayOfYear) * 100:0}% as green as in its greenest month: grass and fields resting {Resting(year, dayOfYear) * 100:0}%");
}
