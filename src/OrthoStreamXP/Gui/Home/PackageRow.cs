namespace OrthoStreamXP.Gui;

/// <summary>
/// A package of the scenery packages folder on the Home page: its name, where X-Plane sees it, and whether it is mounted,
/// switched in place while OrthoStreamXP is stopped.
/// </summary>
internal sealed class PackageRow : ObservableObject
{
    private readonly Func<PackageRow, bool, bool>? _switch;
    private bool _mounted;

    /// <param name="switchTo">Writes the change, false when it could not (the switch goes back); null while it cannot be changed.</param>
    public PackageRow(string name, string where, bool mounted, Func<PackageRow, bool, bool>? switchTo)
    {
        Name = name;
        Where = where;
        _mounted = mounted;
        _switch = switchTo;
    }

    public string Name { get; }
    public string Where { get; }
    public bool CanSwitch => _switch is not null;

    /// <summary>Whether it is (or at the next start will be) shown to X-Plane.</summary>
    public bool IsMounted
    {
        get => _mounted;
        set
        {
            if (_mounted == value || _switch is null) return;
            if (_switch(this, value)) _mounted = value;
            Raise();
        }
    }
}
