namespace OrthoStreamXP.Scenery;

/// <summary>A SCENERY_PACK line of X-Plane's scenery_packs.ini: the package's path as written, and whether X-Plane loads it.</summary>
internal sealed record SceneryPackEntry(string Path, bool Enabled)
{
    private const string EnabledPrefix = "SCENERY_PACK ", DisabledPrefix = "SCENERY_PACK_DISABLED ";

    /// <summary>The package's folder name (the last part of the path), or the path itself for X-Plane's own entries such as *GLOBAL_AIRPORTS*.</summary>
    public string Name
    {
        get
        {
            string normalized = Normalize(Path);
            return normalized[(normalized.LastIndexOf('/') + 1)..];
        }
    }

    /// <summary>The entry a line holds, or null for the header and other lines.</summary>
    public static SceneryPackEntry? Parse(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.StartsWith(DisabledPrefix, StringComparison.Ordinal)) return new(trimmed[DisabledPrefix.Length..].Trim(), Enabled: false);
        if (trimmed.StartsWith(EnabledPrefix, StringComparison.Ordinal)) return new(trimmed[EnabledPrefix.Length..].Trim(), Enabled: true);
        return null;
    }

    /// <summary>The line X-Plane reads for this entry; the path stays as it was written.</summary>
    public string ToLine() => (Enabled ? EnabledPrefix : DisabledPrefix) + Path;

    /// <summary>Whether <paramref name="path"/> names the same folder, whichever slashes and trailing slash it is written with.</summary>
    public bool IsAt(string path) => Normalize(Path).Equals(Normalize(path), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) => path.Trim().Replace('\\', '/').TrimEnd('/');
}
