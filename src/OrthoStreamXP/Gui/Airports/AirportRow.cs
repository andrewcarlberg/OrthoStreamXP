using System.Windows.Input;
using OrthoStreamXP.Setup;

namespace OrthoStreamXP.Gui;

/// <summary>An airport on the Airports page, with its own Flatten or Undo button (one airport at a time: there is no batch).</summary>
internal sealed class AirportRow : ObservableObject
{
    private FlattenableAirport _airport;
    private bool _busy;
    private string? _error;

    public AirportRow(FlattenableAirport airport, AirportsViewModel page)
    {
        _airport = airport;
        Flatten = new RelayCommand(() => _ = page.FlattenAsync(this), () => page.CanChange && CanFlatten);
        Undo = new RelayCommand(() => _ = page.UndoAsync(this), () => page.CanChange && CanUndo);
    }

    public FlattenableAirport Airport
    {
        get => _airport;
        set
        {
            _airport = value;
            Raise(null); // every property follows from it
        }
    }

    public string Ident => Airport.Ident;
    public string Name => Airport.Name;

    /// <summary>"Brussels, BEL · from Global Airports".</summary>
    public string Details
    {
        get
        {
            string from = Airport.Source is { } source ? $"from {source.Source.Name}" : "its scenery package is no longer in the scenery list";
            return Airport.Place is { } place ? $"{place} · {from}" : char.ToUpperInvariant(from[0]) + from[1..];
        }
    }

    /// <summary>The airport's flattening in words; null while it is not flattened.</summary>
    public string? StateText => Airport.State switch
    {
        FlattenState.FlattenedByScenery => "Already flattened by its own scenery",
        FlattenState.Outdated => "Flattened, but the copy is out of date (its scenery changed, or it was made without the buildings): flatten it again",
        FlattenState.Flattened when !Airport.InEffect =>
            $"Flattened, but X-Plane uses {Airport.Source?.Source.Name}'s copy: it is higher in the scenery list, or {FlattenedAirports.PackageName} is switched off. Flatten it again to fix that",
        FlattenState.Flattened => "Flattened by OrthoStreamXP",
        _ => null,
    };

    /// <summary>A problem to point out (an outdated or overridden copy) rather than a plain state.</summary>
    public bool StateIsWarning => Airport.State == FlattenState.Outdated || (Airport.IsFlattenedHere && !Airport.InEffect);

    public bool CanFlatten => !Busy && Airport.Source is not null
                              && (Airport.State is FlattenState.NotFlattened or FlattenState.Outdated || (Airport.IsFlattenedHere && !Airport.InEffect));

    public bool CanUndo => !Busy && Airport.IsFlattenedHere;

    public bool ShowFlatten => Airport.Source is not null && Airport.State != FlattenState.FlattenedByScenery && (!Airport.IsFlattenedHere || StateIsWarning);
    public bool ShowUndo => Airport.IsFlattenedHere;
    public string FlattenLabel => Airport.IsFlattenedHere ? "Flatten again" : "Flatten";

    public ICommand Flatten { get; }
    public ICommand Undo { get; }

    public bool Busy
    {
        get => _busy;
        set
        {
            Set(ref _busy, value);
            RelayCommand.Refresh();
        }
    }

    public string? Error
    {
        get => _error;
        set => Set(ref _error, value);
    }
}
