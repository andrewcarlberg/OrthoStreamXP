using System.Collections.ObjectModel;
using System.Windows.Input;
using OrthoStreamXP.Setup;

namespace OrthoStreamXP.Gui;

/// <summary>
/// The Airports page: find an airport or airfield and flatten the terrain under it (for an airport that sits on a bump or in a
/// hollow of the mesh), or undo that. One airport at a time. The airports are read from the apt.dat files of the packages in
/// X-Plane's scenery_packs.ini when the page is shown (only the files that changed are read again).
/// </summary>
internal sealed class AirportsViewModel : ObservableObject, IPage
{
    /// <summary>How many matches the list shows: enough to pick from, few enough to stay quick.</summary>
    private const int MaxRows = 50;

    private readonly MountController _mount;
    private readonly Func<string?> _xplane;
    private readonly AirportCatalog _catalog = new();
    private readonly Dictionary<string, FlattenableAirport> _airports = new(StringComparer.OrdinalIgnoreCase);
    private AirportFlattening? _flattening;
    private string _search = "";
    private bool _loading, _changing, _moreRows;
    private string? _message;

    /// <param name="xplane">The X-Plane folder of the settings; null while there is none.</param>
    public AirportsViewModel(MountController mount, Func<string?> xplane)
    {
        _mount = mount;
        _xplane = xplane;
        Refresh = new RelayCommand(() => _ = LoadAsync(), () => !Loading && !_changing);
        OpenFolder = new RelayCommand(() => Shell.Open(_flattening!.PackageFolder), () => _flattening is { } f && Directory.Exists(f.PackageFolder));
        // Starting and stopping rewrite scenery_packs.ini: no change of it meanwhile.
        _mount.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(MountController.State)) return;
            Raise(nameof(CanChange));
            RelayCommand.Refresh();
        };
    }

    public ObservableCollection<AirportRow> Rows { get; } = [];
    public ICommand Refresh { get; }
    public ICommand OpenFolder { get; }

    /// <summary>An ICAO code (or the start of one), or part of a name or city.</summary>
    public string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value ?? "")) Filter();
        }
    }

    public bool Loading
    {
        get => _loading;
        private set
        {
            Set(ref _loading, value);
            Raise(nameof(ListText));
            RelayCommand.Refresh();
        }
    }

    /// <summary>One airport is changed at a time, and none while OrthoStreamXP starts or stops.</summary>
    public bool CanChange => !_changing && !Loading && (_mount.IsRunning || _mount.IsStopped);

    /// <summary>What the list below shows.</summary>
    public string ListText
    {
        get
        {
            if (Loading) return "Reading the airports of your scenery…";
            if (_xplane() is null) return "Choose your X-Plane folder first (in Settings): its airports are listed here.";
            if (Search.Trim().Length == 0)
                return Rows.Count == 0 ? "Search for an airport by its ICAO code, name or city." : "The airports you flattened:";
            if (Rows.Count == 0) return $"No airport matches \"{Search.Trim()}\".";
            return _moreRows ? $"The first {MaxRows} matches: type more to narrow them." : "";
        }
    }

    public string? Message
    {
        get => _message;
        private set
        {
            Set(ref _message, value);
            Raise(nameof(HasMessage));
        }
    }

    public bool HasMessage => Message is not null;

    public void OnShown() => _ = LoadAsync();

    private async Task LoadAsync()
    {
        if (Loading || _changing) return; // the change in progress refreshes its own row
        Message = null;
        if (_xplane() is not { } xplane)
        {
            _flattening = null;
            _airports.Clear();
            Filter();
            return;
        }
        Loading = true;
        try
        {
            var flattening = new AirportFlattening(xplane, _catalog);
            IReadOnlyList<FlattenableAirport> airports = await Task.Run(() => flattening.Load(CancellationToken.None)).ConfigureAwait(true);
            _flattening = flattening;
            _airports.Clear();
            foreach (FlattenableAirport airport in airports) _airports[airport.Ident] = airport;
            if (_airports.Count == 0) Message = $"No airports found: X-Plane's Global Airports and the packages in {Path.Join(xplane, "Custom Scenery")} list none.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message = $"Could not read the airports: {ex.Message}";
            Log.Warning(Message);
        }
        catch (Exception ex)
        {
            // Runs unawaited from the page: nothing that goes wrong may vanish.
            Message = $"Could not read the airports: {ex.Message}";
            Log.Error("The airports could not be read", ex);
        }
        finally
        {
            Loading = false;
            Filter();
        }
    }

    public Task FlattenAsync(AirportRow row) => ChangeAsync(row, f => f.Flatten(row.Airport), "flattened");

    public Task UndoAsync(AirportRow row) => ChangeAsync(row, f => f.Undo(row.Airport), "put back");

    private async Task ChangeAsync(AirportRow row, Func<AirportFlattening, FlattenableAirport> change, string done)
    {
        if (!CanChange || _flattening is not { } flattening) return;
        _changing = true;
        row.Busy = true;
        row.Error = null;
        Raise(nameof(CanChange));
        try
        {
            FlattenableAirport changed = await Task.Run(() => change(flattening)).ConfigureAwait(true);
            _airports[changed.Ident] = changed;
            row.Airport = changed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            row.Error = $"{row.Ident} could not be {done}: {ex.Message}";
            Log.Warning(row.Error);
        }
        catch (Exception ex)
        {
            // Runs unawaited from a button: nothing that goes wrong may vanish.
            row.Error = $"{row.Ident} could not be {done}: {ex.Message}";
            Log.Error(row.Error, ex);
        }
        finally
        {
            _changing = false;
            row.Busy = false;
            Raise(nameof(CanChange));
            RelayCommand.Refresh();
        }
    }

    /// <summary>The airports matching the search (an exact code first, then codes starting with it, then names and cities); the flattened ones without a search.</summary>
    private void Filter()
    {
        string query = Search.Trim();
        IEnumerable<FlattenableAirport> matches = query.Length == 0
            ? _airports.Values.Where(a => a.IsFlattenedHere).OrderBy(a => a.Ident, StringComparer.OrdinalIgnoreCase)
            : _airports.Values.Select(a => (Airport: a, Rank: Rank(a, query))).Where(m => m.Rank >= 0)
                .OrderBy(m => m.Rank).ThenBy(m => m.Airport.Ident, StringComparer.OrdinalIgnoreCase).Select(m => m.Airport);
        FlattenableAirport[] shown = [.. matches.Take(MaxRows + 1)];
        _moreRows = shown.Length > MaxRows;
        Rows.Clear();
        foreach (FlattenableAirport airport in shown.Take(MaxRows)) Rows.Add(new AirportRow(airport, this));
        Raise(nameof(ListText));
        RelayCommand.Refresh();
    }

    private static int Rank(FlattenableAirport airport, string query)
    {
        if (airport.Ident.Equals(query, StringComparison.OrdinalIgnoreCase)) return 0;
        if (airport.Ident.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 1;
        if (airport.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) return 2;
        return airport.Place?.Contains(query, StringComparison.OrdinalIgnoreCase) == true ? 3 : -1;
    }
}
