namespace OrthoStreamXP;

/// <summary>
/// The packages of base-packages left out of the mount, switched off on the Home page: the skip-packages setting, their
/// folder names separated by commas.
/// </summary>
internal sealed class SkippedPackages
{
    public const string Setting = "skip-packages";

    private readonly HashSet<string> _names;

    private SkippedPackages(IEnumerable<string> names) => _names = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

    public static SkippedPackages Parse(string? value) =>
        new((value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public bool Contains(string name) => _names.Contains(name);

    /// <summary>These, with <paramref name="name"/> left out of the mount (<paramref name="skipped"/>) or mounted again.</summary>
    public SkippedPackages With(string name, bool skipped) =>
        new(skipped ? _names.Append(name) : _names.Where(n => !n.Equals(name, StringComparison.OrdinalIgnoreCase)));

    /// <summary>The setting's value; null when every package is mounted (the line is then removed).</summary>
    public string? ToValue() => _names.Count == 0 ? null : string.Join(", ", _names.Order(StringComparer.OrdinalIgnoreCase));
}
