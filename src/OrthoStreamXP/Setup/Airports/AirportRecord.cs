namespace OrthoStreamXP.Setup;

/// <summary>
/// An airport as an apt.dat lists it: its identifier, name and place, whether its own definition already asks X-Plane to flatten
/// it, and where its lines are in the file (from its header line to the next airport's).
/// </summary>
internal sealed record AirportRecord(string Ident, string Name, string? City, string? Country, bool Flattened, long Offset, int Length)
{
    /// <summary>"Brussels, BEL"; null when the file names neither.</summary>
    public string? Place => (City, Country) switch
    {
        (null, null) => null,
        ({ } city, null) => city,
        (null, { } country) => country,
        var (city, country) => $"{city}, {country}",
    };
}
