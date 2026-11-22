using System.Text.Json;

namespace OrthoStreamXP.Setup;

/// <summary>
/// The one overlays package every downloaded region shares (<see cref="MeshRegion.OverlaysPackageName"/>). It records which
/// region placed each file, so a region can be taken out again without touching the others'. A file another region placed
/// already (a square on the border of two) stays that region's.
/// </summary>
internal sealed class OverlayPackage(string packagesFolder)
{
    public const string RecordName = ".orthostreamxp-overlays.json";

    public string Folder { get; } = Path.Join(packagesFolder, MeshRegion.OverlaysPackageName);

    private string RecordFile => Path.Join(Folder, RecordName);

    /// <summary>The regions whose overlays are in the package.</summary>
    public IReadOnlyCollection<string> Regions => Load().Keys;

    /// <summary>Moves a region's unpacked overlays from <paramref name="staging"/> into the package.</summary>
    public void Add(string regionId, string staging, IReadOnlyList<string> files)
    {
        Dictionary<string, List<string>> record = Load();
        var owner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (region, owned) in record)
            foreach (string file in owned)
                owner[file] = region;
        var placed = new List<string>();
        foreach (string file in files)
        {
            string target = Path.Join(Folder, file);
            if (owner.TryGetValue(file, out string? other) ? other != regionId : File.Exists(target)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(Path.Join(staging, file), target, overwrite: true);
            placed.Add(file);
        }
        record[regionId] = placed;
        Save(record);
    }

    /// <summary>
    /// Moves more files of a region (the tiles AutoOrtho lacks) from <paramref name="staging"/> into the package, beside
    /// those it placed already. A file any region placed is kept, unless it is this region's and one of
    /// <paramref name="replaceable"/> (placed by an earlier version of the same tiles). A file no region placed is taken
    /// over: one left by an earlier try that stopped before its record was saved.
    /// </summary>
    /// <returns>The files placed (added or replaced).</returns>
    public IReadOnlyList<string> Append(string regionId, string staging, IReadOnlyList<string> files, IReadOnlySet<string> replaceable)
    {
        Dictionary<string, List<string>> record = Load();
        var owner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (region, owned) in record)
            foreach (string file in owned)
                owner[file] = region;
        var placed = new List<string>();
        foreach (string file in files)
        {
            if (owner.TryGetValue(file, out string? other) && !(other == regionId && replaceable.Contains(file))) continue;
            string target = Path.Join(Folder, file);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(Path.Join(staging, file), target, overwrite: true);
            placed.Add(file);
        }
        List<string> added = [.. placed.Where(f => !owner.ContainsKey(f))];
        if (added.Count == 0) return placed;
        record[regionId] = [.. record.GetValueOrDefault(regionId) ?? [], .. added];
        Save(record);
        return placed;
    }

    /// <summary>Deletes the files a region placed; the package goes with its last region.</summary>
    public void Remove(string regionId)
    {
        Dictionary<string, List<string>> record = Load();
        if (!record.Remove(regionId, out List<string>? owned)) return;
        foreach (string file in owned)
        {
            string path = Path.Join(Folder, file);
            if (File.Exists(path)) File.Delete(path);
        }
        if (record.Count == 0)
        {
            Directory.Delete(Folder, recursive: true);
            return;
        }
        RemoveEmptyFolders(Folder);
        Save(record);
    }

    private Dictionary<string, List<string>> Load()
    {
        try
        {
            return File.Exists(RecordFile)
                ? JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(RecordFile)) ?? []
                : [];
        }
        catch (JsonException ex)
        {
            throw new IOException($"The record of {MeshRegion.OverlaysPackageName} could not be read: {ex.Message}", ex);
        }
    }

    private void Save(Dictionary<string, List<string>> record)
    {
        Directory.CreateDirectory(Folder);
        AtomicFile.Write(RecordFile, JsonSerializer.SerializeToUtf8Bytes(record));
    }

    private static void RemoveEmptyFolders(string folder)
    {
        foreach (string sub in Directory.EnumerateDirectories(folder))
        {
            RemoveEmptyFolders(sub);
            if (!Directory.EnumerateFileSystemEntries(sub).Any()) Directory.Delete(sub);
        }
    }
}
