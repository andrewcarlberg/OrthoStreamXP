using OrthoStreamXP.Setup;

namespace OrthoStreamXP.Gui;

/// <summary>
/// At each start of the window, adds the tiles AutoOrtho lacks to the regions installed without them: installed by an
/// older OrthoStreamXP (the first start after an update), before the tiles were published, or while they could not be
/// downloaded. It takes the install slot like any install; when an install has it, or a download fails, the tiles are
/// added at the next start.
/// </summary>
internal sealed class SupplementUpdater(HttpClient http, SupplementCatalog catalog, InstallSlot slot, Func<string?> folder)
{
    /// <returns>The names of the regions the tiles were added to.</returns>
    public async Task<IReadOnlyList<string>> RunAsync(IProgress<InstallProgress> progress, CancellationToken cancel)
    {
        if (folder() is not { } packages || !Directory.Exists(packages)) return [];
        var installer = new RegionInstaller(new AssetDownload(http), packages);
        IReadOnlyDictionary<string, InstalledRegion> installed = installer.Installed();
        if (installed.Count == 0) return [];
        IReadOnlyDictionary<string, RegionSupplement> supplements = await catalog.LoadAsync(refresh: false, cancel).ConfigureAwait(true);
        List<InstalledRegion> lacking = [.. installed.Values.Where(r => r.Lacks(supplements.GetValueOrDefault(r.Id)))];
        if (lacking.Count == 0) return [];
        if (!slot.TryTake())
        {
            Log.Info("The tiles AutoOrtho lacks are added at the next start: another install is running");
            return [];
        }
        var added = new List<string>();
        try
        {
            foreach (InstalledRegion region in lacking)
            {
                try
                {
                    await installer.AddSupplementAsync(region.Id, supplements[region.Id], progress, cancel).ConfigureAwait(true);
                    added.Add(MeshRegion.NameOf(region.Id));
                }
                catch (InstallException ex)
                {
                    Log.Warning($"{ex.Message} They are added at the next start.");
                }
            }
        }
        finally
        {
            slot.Release();
            RelayCommand.Refresh();
        }
        return added;
    }
}
