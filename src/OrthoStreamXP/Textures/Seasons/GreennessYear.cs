namespace OrthoStreamXP.Textures;

/// <summary>
/// How green a place was through a year, month by month, as satellites measure it (NDVI, 0 bare or snow to 1 dense
/// vegetation; <see cref="VegetationTile"/>). A month less green than both its neighbours (a cloudy or flooded
/// composite, one snowfall: what goes wrong in a satellite's month makes it less green, never more) is raised to the
/// less green of them, so one odd month does not make a season; the greenest month stays as it was.
/// </summary>
internal sealed class GreennessYear
{
    /// <summary>The middle of a month, in days of the year.</summary>
    private const double MonthDays = 365.0 / 12;

    /// <summary>The most months that may be missing: they are filled from the months either side.</summary>
    public const int MaxMissing = 3;

    private readonly double[] _months;

    private GreennessYear(double[] months)
    {
        _months = months;
        Low = months.Min();
        Peak = months.Max();
    }

    /// <summary>The least green month (often the snow or the dry season).</summary>
    public double Low { get; }

    /// <summary>The greenest month (most aerial photos show the place about then).</summary>
    public double Peak { get; }

    /// <summary>How much the greenness changes through the year: about 0.05 for a town or an evergreen jungle, 0.5 or more for snowy fields and broadleaf forest.</summary>
    public double Range => Peak - Low;

    /// <summary>
    /// A year from twelve monthly values, January first; <see cref="double.NaN"/> for a month without data. Null when more
    /// than <see cref="MaxMissing"/> months are missing.
    /// </summary>
    public static GreennessYear? From(ReadOnlySpan<double> months)
    {
        if (months.Length != 12) throw new ArgumentException("A year has twelve months", nameof(months));
        int missing = 0;
        foreach (double m in months)
            if (double.IsNaN(m)) missing++;
        if (missing > MaxMissing) return null;
        double[] filled = Filled(months);
        double[] even = new double[12];
        for (int m = 0; m < 12; m++)
            even[m] = Math.Max(filled[m], Math.Min(filled[(m + 11) % 12], filled[(m + 1) % 12]));
        return new GreennessYear(even);
    }

    /// <summary>The greenness on a day of the year (0 is 1 January), between the middles of the months either side.</summary>
    public double At(double day)
    {
        double t = ((day - MonthDays / 2) / MonthDays % 12 + 12) % 12;
        int month = (int)t;
        double f = t - month;
        return _months[month] + (_months[(month + 1) % 12] - _months[month]) * f;
    }

    /// <summary>The greenness on a day as a share of the greenest month's, 0 to 1; 1 when the place is never green.</summary>
    public double ShareOfPeak(double day) => Peak <= 0 ? 1 : Math.Clamp(At(day) / Peak, 0, 1);

    /// <summary>The months with each missing one drawn between the nearest months that have data, around the year's end too.</summary>
    private static double[] Filled(ReadOnlySpan<double> months)
    {
        double[] filled = months.ToArray();
        for (int m = 0; m < 12; m++)
        {
            if (!double.IsNaN(months[m])) continue;
            int back = 1, ahead = 1;
            while (double.IsNaN(months[(m - back + 12) % 12])) back++;
            while (double.IsNaN(months[(m + ahead) % 12])) ahead++;
            double a = months[(m - back + 12) % 12], b = months[(m + ahead) % 12];
            filled[m] = a + (b - a) * back / (back + ahead);
        }
        return filled;
    }
}
