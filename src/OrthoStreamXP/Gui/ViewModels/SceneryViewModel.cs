using System.Collections.ObjectModel;
using System.Windows.Input;
using OrthoStreamXP.Scenery;

namespace OrthoStreamXP.Gui;

/// <summary>
/// The X-Plane Custom Scenery page: every entry of X-Plane's Custom Scenery\scenery_packs.ini, to switch on or off and reorder.
/// Read again when the page is shown and when OrthoStreamXP starts or stops, as both change the file; changed only while
/// OrthoStreamXP is stopped, so the person's changes and the mount's never meet.
/// </summary>
internal sealed class SceneryViewModel : ObservableObject, IPage
{
    private readonly MountController _mount;
    private readonly Func<string?> _xplane;
    private string? _message;

    /// <param name="xplane">The X-Plane folder of the settings; null while there is none.</param>
    public SceneryViewModel(MountController mount, Func<string?> xplane)
    {
        _mount = mount;
        _xplane = xplane;
        Refresh = new RelayCommand(Load);
        OpenFolder = new RelayCommand(() => Shell.Open(CustomScenery!), () => CustomScenery is { } folder && Directory.Exists(folder));
        _mount.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(MountController.State)) return;
            Raise(nameof(CanEdit));
            Raise(nameof(RunningNote));
            RelayCommand.Refresh();
            if (_mount.IsRunning || _mount.IsStopped) Load();
        };
    }

    public ObservableCollection<SceneryPackRow> Rows { get; } = [];
    public ICommand Refresh { get; }
    public ICommand OpenFolder { get; }

    /// <summary>The list can be changed only while OrthoStreamXP is stopped: starting and stopping change the file too.</summary>
    public bool CanEdit => _mount.IsStopped;

    public string? RunningNote => CanEdit ? null : "OrthoStreamXP is running: stop it to change the list.";

    public string? CustomScenery => _xplane() is { } xplane ? Path.Join(xplane, "Custom Scenery") : null;

    public string FileText => CustomScenery is { } folder
        ? $"From {Path.Join(folder, SceneryPacks.FileName)}, highest priority first."
        : "Choose your X-Plane folder first (in Settings): its scenery list is shown here.";

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

    public void OnShown() => Load();

    private void Load()
    {
        Rows.Clear();
        Message = null;
        Raise(nameof(FileText));
        RelayCommand.Refresh();
        if (CustomScenery is not { } folder) return;
        var list = new SceneryPackList(folder);
        try
        {
            var streamed = new HashSet<string>(_mount.Running?.Packages.Select(p => p.Name) ?? [], StringComparer.OrdinalIgnoreCase);
            foreach (SceneryPackEntry entry in list.Read())
                Rows.Add(new SceneryPackRow(entry, streamed.Contains(entry.Name), Switch));
            if (Rows.Count == 0) Message = $"{list.FilePath} lists no scenery yet: X-Plane writes it when it starts.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message = $"Could not read {list.FilePath}: {ex.Message}";
        }
    }

    private bool Switch(SceneryPackRow row, bool enabled)
    {
        if (!CanEdit || CustomScenery is not { } folder) return false;
        try
        {
            new SceneryPackList(folder).SetEnabled(row.Path, enabled);
            Message = null;
            Log.Info($"{(enabled ? "Enabled" : "Disabled")} {row.Path} in {SceneryPacks.FileName}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message = $"Could not change {row.Name}: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Drops <paramref name="row"/> before the row at <paramref name="insertAt"/> (the row count puts it last), as dragged on the page.
    /// </summary>
    public void Drop(SceneryPackRow row, int insertAt)
    {
        int from = Rows.IndexOf(row);
        if (!CanEdit || CustomScenery is not { } folder || from < 0 || insertAt < 0 || insertAt > Rows.Count) return;
        int to = insertAt > from ? insertAt - 1 : insertAt;
        if (to == from) return;
        try
        {
            if (new SceneryPackList(folder).MoveTo(row.Path, to) && Rows.Count == new SceneryPackList(folder).Read().Count)
            {
                Rows.Move(from, to);
                Message = null;
                Log.Info($"Moved {row.Path} to place {to + 1} in {SceneryPacks.FileName}");
            }
            else
            {
                Load(); // The file changed meanwhile: show it as it is now.
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message = $"Could not move {row.Name}: {ex.Message}";
        }
    }
}
