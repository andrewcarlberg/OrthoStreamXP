namespace OrthoStreamXP.Gui;

/// <summary>
/// Where places are on a map laid out in map units, north up from (<see cref="West"/>, <see cref="North"/>): a degree of
/// latitude is <see cref="Unit"/> units, a degree of longitude that narrowed by <see cref="Squeeze"/> (the cosine of the
/// map's middle latitude, as on a map).
/// </summary>
internal sealed record MapProjection(double West, double North, double Unit, double Squeeze)
{
    public double DegreeWidth => Unit * Squeeze;

    public double X(double lon) => (lon - West) * DegreeWidth;

    public double Y(double lat) => (North - lat) * Unit;

    public double Lon(double x) => West + x / DegreeWidth;

    public double Lat(double y) => North - y / Unit;
}
