using System.Globalization;
using OrthoStreamXP.Setup;
using OrthoStreamXP.Textures;

namespace OrthoStreamXP.Tuning;

/// <summary>The preset, at a resolution, recommended for this PC, and why in plain words.</summary>
internal sealed record Recommendation(QualityPreset Preset, TextureResolution Resolution, string Reason)
{
    /// <summary>"Ultra", or "Ultra at Medium resolution".</summary>
    public string Title => Resolution.IsFull ? Preset.Name : $"{Preset.Name} at {Resolution.Label} resolution";
}

/// <summary>How one preset suits this PC, for its card beside the recommended one.</summary>
/// <param name="Resolution">The finest resolution the graphics card holds it at, when nothing else rules it out; null when it does not suit.</param>
/// <param name="Text">"Also suits your PC", "Also suits your PC at Medium resolution: ...", or why it does not.</param>
internal sealed record PresetFit(QualityPreset Preset, TextureResolution? Resolution, string Text)
{
    public bool Suits => Resolution is not null;
}

/// <summary>
/// Which quality preset, at which resolution, suits this PC: the largest textures every known fact allows. The two
/// facts weigh on different things: the connection on the preset (the resolution downloads the same imagery), the
/// graphics card on both (it must hold the textures beside X-Plane's own needs).
/// <list type="bullet">
/// <item>The connection must have reached the preset's download speed (Sharp about 20 Mbit/s, Ultra 50, Extra 100: a
/// ZL16 texture is 4 times the tiles per step) on the fastest recent flight that downloaded enough to tell (a flight
/// over cached scenery downloads little and says nothing) or in the connection test, whichever is faster: each only
/// shows the connection can do at least that much, a flight downloading what its preset asks. Without either, no more
/// than Sharp's downloads.</item>
/// <item>A preset that was often blurry on the last flight is not recommended again (a lower resolution downloads the
/// same, so it would be as blurry).</item>
/// <item>Of the pairs left that the card holds, the one with the largest textures in the wide areas, then at the
/// airports; between equal textures, the preset that downloads less. So when the card cannot hold Ultra, "Ultra at
/// Medium" (4096 px everywhere) beats Sharp (4096 px, airports 2048), and ties with Extra at Low, which downloads more
/// for the same textures.</item>
/// </list>
/// When the card holds none, the smallest textures: Balanced at Low. <see cref="Assess"/> tells the same of every
/// preset, for the cards beside the recommended one.
/// </summary>
internal static class PresetAdvisor
{
    /// <summary>The peak download speed (Mbit/s) a preset wants, by level change.</summary>
    public static double NeededMbits(QualityPreset preset) => preset.Level switch
    {
        <= -2 => 0,
        -1 => 20,
        0 => 50,
        _ => 100,
    };

    /// <summary>Every preset at every resolution (Dynamic is its base's textures, so not among them).</summary>
    private static IEnumerable<(QualityPreset Preset, TextureResolution Resolution)> Pairs =>
        from preset in QualityPreset.All from resolution in TextureResolution.All select (preset, resolution);

    /// <summary>What is known of this PC: the connection's speed and where it was seen, the last flight's blur, the card.</summary>
    /// <param name="Peak">The connection's speed, Mbit/s; null when nothing has measured it.</param>
    /// <param name="FromFlights">The speed is the flights' (otherwise the connection test's).</param>
    /// <param name="BlurryLevel">The level of the last flight when it was often blurry; null otherwise.</param>
    private sealed record Evidence(double? Peak, bool FromFlights, int? BlurryLevel, long? Card, GraphicsMemory Memory)
    {
        public string CardText => Card is { } bytes ? $"{CacheInventory.FormatBytes(bytes)} graphics card" : "";

        public string SpeedText => Peak is not { } peak ? ""
            : FromFlights ? $"downloads reached {peak.ToString("0", CultureInfo.CurrentCulture)} Mbit/s on your recent flights"
            : $"the connection test measured {peak.ToString("0", CultureInfo.CurrentCulture)} Mbit/s";

        /// <summary>The connection keeps up with the preset; true when nothing has measured it (it is not judged then).</summary>
        public bool KeepsUp(QualityPreset preset) => Peak is not { } peak || peak >= NeededMbits(preset);

        /// <summary>The preset was often blurry on the last flight, or is sharper than one that was. Balanced never is: nothing is below it.</summary>
        public bool WasBlurry(QualityPreset preset) => BlurryLevel is { } level && preset.Level >= level && preset != QualityPreset.Balanced;

        public bool Fits(QualityPreset preset, TextureResolution resolution) =>
            Card is not { } card || Memory.BytesAt(preset.PlanAt(resolution)) + GraphicsMemory.XPlaneOwnBytes <= card;
    }

    private static Evidence Gather(long? card, GraphicsMemory memory, IReadOnlyList<FlightReport> flights, ConnectionResult? test)
    {
        // Evidence of the connection: the fastest flight that downloaded enough to show its speed, and the connection test.
        // Each only shows the connection can do at least that much (a flight downloads what its preset asks, not all the
        // connection can), so the faster one counts.
        FlightReport? fastest = flights.Where(f => f.Bytes > 200_000_000).MaxBy(f => f.PeakMbits);
        double? tested = test is { Mbits: > 0 } t ? t.Mbits : null;
        bool fromFlights = fastest is not null && (tested is null || fastest.PeakMbits >= tested);
        FlightReport? lastFlight = flights.Count > 0 ? flights[0] : null;
        int? flownLevel = lastFlight is null ? null : CacheInventory.ParseSetupName(lastFlight.Quality)?.Level;
        bool blurry = lastFlight is not null && ReportSummary.SharpnessRating(lastFlight) == Rating.Poor;
        return new Evidence(fromFlights ? fastest!.PeakMbits : tested, fromFlights, blurry ? flownLevel : null, card, memory);
    }

    /// <param name="card">The graphics card's memory, when Windows says.</param>
    /// <param name="memory">The photo textures' graphics memory per texture size, as last measured.</param>
    /// <param name="flights">The flight reports kept, newest first (<see cref="FlightReport.History"/>).</param>
    /// <param name="test">The latest connection test, when there is one.</param>
    public static Recommendation Recommend(long? card, GraphicsMemory memory, IReadOnlyList<FlightReport> flights, ConnectionResult? test = null)
    {
        Evidence e = Gather(card, memory, flights, test);
        if (e.Peak is null)
        {
            if (Best(p => p.Preset.Level <= QualityPreset.Sharp.Level && e.Fits(p.Preset, p.Resolution)) is not { } start)
                return Smallest($"Your {e.CardText} is small for sharper textures. {MeasureFirst}");
            string opening = start.Preset == QualityPreset.Sharp && start.Resolution.IsFull ? "A good start on most PCs"
                : start.Resolution.IsFull ? $"Your {e.CardText} is small for sharper textures"
                : $"{start.Preset.Name} at Full resolution would not fit your {e.CardText}, at {start.Resolution.Label} it does";
            return new(start.Preset, start.Resolution, $"{opening}. {MeasureFirst}");
        }

        if (Best(p => e.KeepsUp(p.Preset) && !e.WasBlurry(p.Preset) && e.Fits(p.Preset, p.Resolution)) is { } best)
            return new(best.Preset, best.Resolution, Because(e, best));
        return Smallest(Because(e, null));
    }

    /// <summary>
    /// How every preset the window offers suits this PC: the finest resolution the card holds it at when the connection
    /// keeps up and it was not blurry, otherwise the reason it does not suit. The recommended one is among them.
    /// </summary>
    /// <param name="presets">The presets to tell of: the window's cards (<see cref="QualityPreset.Choices"/>) when null.</param>
    public static IReadOnlyList<PresetFit> Assess(long? card, GraphicsMemory memory, IReadOnlyList<FlightReport> flights,
        ConnectionResult? test = null, IEnumerable<QualityPreset>? presets = null)
    {
        Evidence e = Gather(card, memory, flights, test);
        return [.. (presets ?? QualityPreset.Choices).Select(preset => Assess(e, preset))];
    }

    private static PresetFit Assess(Evidence e, QualityPreset preset)
    {
        if (!e.KeepsUp(preset))
            return new(preset, null, $"Needs about {NeededMbits(preset):0} Mbit/s of downloads: {e.SpeedText}.");
        if (e.WasBlurry(preset))
            return new(preset, null, "Not for now: your last flight was often blurry at this quality or a lower one.");
        // Full first: the finest resolution the card holds.
        if (TextureResolution.All.FirstOrDefault(r => e.Fits(preset, r)) is not { } resolution)
            return new(preset, null, $"Too large for your {e.CardText}, even at Low resolution.");
        string suits = resolution.IsFull ? "Also suits your PC"
            : $"Also suits your PC at {resolution.Label} resolution: at Full its textures would not fit your {e.CardText}";
        // Nothing has measured the connection: only Sharp's downloads and below are judged to keep up.
        return e.Peak is null && preset.Level > QualityPreset.Sharp.Level
            ? new(preset, resolution, $"Fits your graphics card{(resolution.IsFull ? "" : $" at {resolution.Label} resolution")}. Test your connection to see whether it keeps up with its downloads.")
            : new(preset, resolution, $"{suits}.");
    }

    private const string MeasureFirst = "Test your connection or fly once, and the recommendation follows your PC and connection.";

    /// <summary>The pair with the largest textures (areas, then airports); between equal ones, the fewer downloads.</summary>
    private static (QualityPreset Preset, TextureResolution Resolution)? Best(Func<(QualityPreset Preset, TextureResolution Resolution), bool> allowed) =>
        Pairs.Where(allowed)
            .OrderByDescending(p => p.Preset.PlanAt(p.Resolution).SizeFor(16))
            .ThenByDescending(p => p.Preset.PlanAt(p.Resolution).SizeFor(18))
            .ThenBy(p => p.Preset.Level)
            .Select(p => ((QualityPreset, TextureResolution)?)p)
            .FirstOrDefault();

    /// <summary>When the card holds nothing: the smallest textures there are.</summary>
    private static Recommendation Smallest(string reason) => new(QualityPreset.Balanced, TextureResolution.Low, reason);

    private static string Because(Evidence e, (QualityPreset Preset, TextureResolution Resolution)? pick)
    {
        var facts = new List<string> { e.SpeedText };
        if (e.CardText.Length > 0)
            facts.Add(pick is not { } p ? $"your {e.CardText} is small even for the smallest textures"
                : p.Resolution.IsFull ? $"your {e.CardText} holds its textures"
                : $"your {e.CardText} would not hold {p.Preset.Name}'s textures at Full resolution, at {p.Resolution.Label} it does, from the same imagery");
        if (e.BlurryLevel is not null) facts.Add("a sharper quality was often blurry");
        return $"For your PC: {string.Join(", ", facts)}.";
    }
}
