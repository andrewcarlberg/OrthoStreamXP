using System.Text;

namespace OrthoStreamXP.Scenery;

/// <summary>
/// The entries of X-Plane's Custom Scenery\scenery_packs.ini, for the person to switch on or off and reorder. The file is read
/// again for every change, so what changed it meanwhile (X-Plane, a mount starting or stopping) is kept; the other lines and the
/// line endings stay.
/// </summary>
internal sealed class SceneryPackList(string customScenery)
{
    public string FilePath { get; } = Path.Join(customScenery, SceneryPacks.FileName);

    /// <summary>The entries in the file's order (highest priority first); none while there is no file.</summary>
    public IReadOnlyList<SceneryPackEntry> Read() =>
        File.Exists(FilePath) ? [.. File.ReadAllLines(FilePath).Select(SceneryPackEntry.Parse).OfType<SceneryPackEntry>()] : [];

    /// <summary>
    /// Switches the entries at <paramref name="path"/> on or off in place. Returns whether the file changed (not when there is
    /// no such entry, or it already is that way).
    /// </summary>
    public bool SetEnabled(string path, bool enabled) => Change(lines =>
    {
        bool changed = false;
        for (int i = 0; i < lines.Length; i++)
        {
            if (SceneryPackEntry.Parse(lines[i]) is not { } entry || entry.Enabled == enabled || !entry.IsAt(path)) continue;
            lines[i] = (entry with { Enabled = enabled }).ToLine();
            changed = true;
        }
        return changed;
    });

    /// <summary>
    /// Moves the entry at <paramref name="path"/> so it becomes entry number <paramref name="to"/> (0 is the first, the highest
    /// priority in X-Plane); the entries between shift by one, and the lines that are not entries keep their places. Returns
    /// whether the file changed (not when there is no such entry, it already is there, or <paramref name="to"/> is outside the list).
    /// </summary>
    public bool MoveTo(string path, int to) => Change(lines =>
    {
        int[] entries = [.. Enumerable.Range(0, lines.Length).Where(i => SceneryPackEntry.Parse(lines[i]) is not null)];
        int from = Array.FindIndex(entries, i => SceneryPackEntry.Parse(lines[i])!.IsAt(path));
        if (from < 0 || to < 0 || to >= entries.Length || from == to) return false;
        string moved = lines[entries[from]];
        int step = to < from ? -1 : 1;
        for (int k = from; k != to; k += step) lines[entries[k]] = lines[entries[k + step]];
        lines[entries[to]] = moved;
        return true;
    });

    /// <summary>Reads the file's lines, lets <paramref name="edit"/> change them in place, and writes them back if it did.</summary>
    private bool Change(Func<string[], bool> edit)
    {
        if (!File.Exists(FilePath)) return false;
        string text = File.ReadAllText(FilePath);
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string[] lines = text.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
        if (!edit(lines)) return false;
        AtomicFile.Write(FilePath, Encoding.UTF8.GetBytes(string.Join(newline, lines) + newline));
        return true;
    }
}
