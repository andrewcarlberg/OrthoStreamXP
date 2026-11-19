using System.Text.Json;
using Microsoft.Win32;
using OrthoStreamXP.Scenery;

namespace OrthoStreamXP.Tuning;

/// <summary>
/// The graphics memory the photo textures take in X-Plane, per texture size, as flown: the peak over a flight of what
/// X-Plane held. Measured in pixels X-Plane wanted (the same at the same distance, whatever the size), so one flight
/// tells every preset's: a smaller texture just reaches its own size nearer the aircraft. Saved in the log folder as
/// graphics-memory.json; before any flight, the figures of a test flight (Extra, 1 hour, a 3840 px screen).
/// <para>
/// A resolution may size the airports' (ZL18) textures apart from the wide areas' (Medium halves only the areas'), so a
/// flight also measures the airports' part of each peak: <see cref="BytesAt(int, int)"/> then adds the two, each from
/// its own size. Figures without that part (the test flight, earlier versions) count both at the larger size.
/// </para>
/// </summary>
/// <param name="FlownSize">The size flown: the wide areas' when <see cref="FlownAirportSize"/> is known, otherwise the largest.</param>
internal sealed record GraphicsMemory(DateTime MeasuredUtc, int FlownSize, IReadOnlyDictionary<int, long> PeakBySize)
{
    /// <summary>The airports' (ZL18) part of each peak, measured with it; null when it was not measured.</summary>
    public IReadOnlyDictionary<int, long>? AirportBySize { get; init; }

    /// <summary>The airports' texture size flown, when their part was measured.</summary>
    public int? FlownAirportSize { get; init; }

    public const string FileName = "graphics-memory.json";

    /// <summary>X-Plane keeps a texture while its scenery is loaded: what it wanted in the last 20 minutes counts.</summary>
    public static readonly TimeSpan HeldFor = TimeSpan.FromMinutes(20);

    /// <summary>What X-Plane itself takes of a graphics card beside the photo textures, about: the warning's margin.</summary>
    public const long XPlaneOwnBytes = 5L << 30;

    public static readonly int[] Sizes = [1024, 2048, 4096, 8192];

    /// <summary>
    /// The test flight's peaks, and at 8192 px, which it did not fly, an estimate: the step from 2048 to 4096 again
    /// (x 1.71), the textures beside the aircraft being the ones that grow.
    /// </summary>
    private static readonly Dictionary<int, long> ReferencePeaks = new()
    {
        [1024] = 462_000_000, [2048] = 741_000_000, [4096] = 1_267_000_000, [8192] = 2_166_000_000,
    };

    /// <summary>Measured on the test flight of 27 October 2026 (Extra over Poland, 57 min, peaks of 20-minute spans).</summary>
    public static readonly GraphicsMemory TestFlight = new(new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc), 4096,
        new Dictionary<int, long> { [1024] = 462_000_000, [2048] = 741_000_000, [4096] = 1_267_000_000 });

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>The figures in a history folder (<see cref="Setup.AppPaths.History"/>).</summary>
    public static string PathFor(string history) => Path.Join(history, FileName);

    /// <summary>
    /// The photo textures' graphics memory at a texture size. Above the size flown, X-Plane's wish for detail next to
    /// the aircraft was cut at that size, so the test flight's ratio fills it in.
    /// </summary>
    public long BytesAt(int size) => BytesAt(size, size);

    /// <summary>The photo textures' graphics memory with the textures of <paramref name="plan"/> (its areas' and airports' sizes).</summary>
    public long BytesAt(Textures.TexturePlan plan) => BytesAt(plan.SizeFor(16), plan.SizeFor(18));

    /// <summary>
    /// The photo textures' graphics memory with the wide areas' textures at <paramref name="areaSize"/> and the airports'
    /// at <paramref name="airportSize"/>: each part as measured up to the size flown, beyond it in the test flight's
    /// ratio. Without the airports' part measured, both count at the larger size (never less than they may take).
    /// </summary>
    public long BytesAt(int areaSize, int airportSize)
    {
        (areaSize, airportSize) = (Modelled(areaSize), Modelled(airportSize));
        if (AirportBySize is not { } airports || FlownAirportSize is not { } airportFlown)
            return Part(PeakBySize, Math.Max(areaSize, airportSize), FlownSize);
        var areas = PeakBySize.ToDictionary(p => p.Key, p => p.Value - airports.GetValueOrDefault(p.Key));
        return Part(areas, areaSize, FlownSize) + Part(airports, airportSize, airportFlown);
    }

    /// <summary>A measured part at a size: as measured up to the size flown; above it, the flown size's in the test flight's ratio.</summary>
    private static long Part(IReadOnlyDictionary<int, long> measured, int size, int flown)
    {
        if (size <= flown && measured.TryGetValue(size, out long bytes)) return bytes;
        flown = Modelled(flown);
        long atFlown = measured.TryGetValue(flown, out long f) ? f : Reference(flown);
        return (long)(atFlown * (double)Reference(size) / Reference(flown));
    }

    /// <summary>A size the model has figures for: a smaller texture (a 512 px airport at Low) counts as the smallest, never less.</summary>
    internal static int Modelled(int size) => Math.Clamp(size, Sizes[0], Sizes[^1]);

    private static long Reference(int size) =>
        ReferencePeaks.TryGetValue(size, out long bytes) ? bytes : throw new ArgumentOutOfRangeException(nameof(size), size, "Not a texture size of the graphics memory model");

    /// <summary>A texture's memory in BC1 with its smaller levels: half a byte a pixel, a third more for the levels.</summary>
    public static long TextureBytes(int pixels)
    {
        long side = Math.Max(4, pixels);
        return side * side / 2 * 4 / 3;
    }

    /// <summary>What the textures X-Plane held take at each size.</summary>
    public static Dictionary<int, long> HeldBytes(IEnumerable<int> detail)
    {
        int[] wanted = [.. detail];
        return Sizes.ToDictionary(size => size, size => wanted.Sum(d => TextureBytes(Math.Min(size, d))));
    }

    public static GraphicsMemory Load(string? history)
    {
        try
        {
            if (history is not null && File.Exists(PathFor(history)) && JsonSerializer.Deserialize<GraphicsMemory>(File.ReadAllText(PathFor(history))) is { } saved)
                return saved;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            // The test flight's figures, then.
        }
        return TestFlight;
    }

    public void Save(string history)
    {
        try
        {
            AtomicFile.Write(PathFor(history), JsonSerializer.SerializeToUtf8Bytes(this, Indented));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning($"Could not save {PathFor(history)}: {ex.Message}");
        }
    }

    /// <summary>
    /// The graphics card's own memory, from what its driver tells Windows (the largest card's: an integrated one beside
    /// it has little); null when Windows does not say.
    /// </summary>
    public static long? CardBytes()
    {
        try
        {
            using RegistryKey? cards = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cards is null) return null;
            long best = 0;
            foreach (string name in cards.GetSubKeyNames())
            {
                object? value;
                try
                {
                    using RegistryKey? card = cards.OpenSubKey(name);
                    value = card?.GetValue("HardwareInformation.qwMemorySize") ?? card?.GetValue("HardwareInformation.MemorySize");
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
                {
                    continue; // a key Windows keeps to itself ("Properties"): not a card
                }
                long bytes = value switch
                {
                    long l => l,
                    int i => (uint)i,
                    byte[] { Length: >= 8 } b => BitConverter.ToInt64(b),
                    byte[] { Length: >= 4 } b => BitConverter.ToUInt32(b),
                    _ => 0,
                };
                best = Math.Max(best, bytes);
            }
            return best > 0 ? best : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}

/// <summary>
/// Takes the peak of the photo textures' graphics memory over a session, once a minute, and the airports' part of it.
/// </summary>
/// <param name="areaSize">The wide areas' (ZL16) texture size flown.</param>
/// <param name="airportSize">The airports' (ZL18) texture size flown.</param>
internal sealed class GraphicsMemoryMeter(int areaSize, int airportSize)
{
    private readonly Dictionary<int, long> _peak = GraphicsMemory.Sizes.ToDictionary(s => s, _ => 0L);
    private readonly Dictionary<int, long> _airports = GraphicsMemory.Sizes.ToDictionary(s => s, _ => 0L);

    public void Sample(IEnumerable<HeldTexture> held)
    {
        HeldTexture[] textures = [.. held];
        foreach (int size in GraphicsMemory.Sizes)
        {
            long areas = 0, airports = 0;
            foreach (HeldTexture t in textures)
            {
                long bytes = GraphicsMemory.TextureBytes(Math.Min(size, t.Detail));
                if (t.Airport) airports += bytes;
                else areas += bytes;
            }
            // The airports' part at the peak itself, so the two parts add up to it.
            if (areas + airports <= _peak[size]) continue;
            _peak[size] = areas + airports;
            _airports[size] = airports;
        }
    }

    public GraphicsMemory Result() => new(DateTime.UtcNow, GraphicsMemory.Modelled(areaSize), new Dictionary<int, long>(_peak))
    {
        AirportBySize = new Dictionary<int, long>(_airports),
        FlownAirportSize = GraphicsMemory.Modelled(airportSize),
    };
}
