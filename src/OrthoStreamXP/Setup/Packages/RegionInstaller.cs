using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using OrthoStreamXP.Scenery;

namespace OrthoStreamXP.Setup;

/// <summary>
/// A region installed in the packages folder: its release, whether its overlays came with it, and the version of the tiles
/// AutoOrtho lacks added to it (null: none, as before OrthoStreamXP published any).
/// </summary>
/// <param name="SupplementFiles">The files the tiles placed in the region's package, relative: a newer version may replace them, never another.</param>
/// <param name="SupplementOverlayFiles">The same in the shared overlays package.</param>
internal sealed record InstalledRegion(string Id, string Release, bool Overlays, DateTime InstalledUtc, int? Supplement = null,
    IReadOnlyList<string>? SupplementFiles = null, IReadOnlyList<string>? SupplementOverlayFiles = null)
{
    /// <summary>The region has not got all of <paramref name="supplement"/> yet (an older version of it, or none).</summary>
    public bool Lacks(RegionSupplement? supplement) => supplement is not null && (Supplement ?? 0) < supplement.Version;
}

/// <summary>How far an install is: what it does now, and the bytes downloaded of all it downloads.</summary>
internal sealed record InstallProgress(string Stage, long Done, long Total);

/// <summary>An install that cannot go on; the message says why, in the window's words.</summary>
internal sealed class InstallException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Installs, updates and deletes the regions of AutoOrtho's mesh in the scenery packages folder, each as a package of its
/// own (<see cref="MeshRegion.PackageName"/>) beside the person's other packages, its overlays in the shared overlays package.
/// <para>
/// Everything is prepared in the hidden <c>.orthostream-download</c> folder there, which is no package (it has no Earth nav data): a
/// zip is unpacked as soon as it is whole, then deleted, and a record of the zips unpacked lets a stopped install go on
/// where it was. The region takes its place only once all of it is there, with its marker written last, so X-Plane never
/// sees half a region. An update is prepared the same way and swapped in at the end.
/// </para>
/// <para>
/// The tiles AutoOrtho's release lacks (<see cref="MeshRegion.Supplement"/>) are added to the prepared region before it takes
/// its place, never over a file of AutoOrtho's; when they cannot be had, the region is installed without them and they are
/// added later (<see cref="AddSupplementAsync"/>).
/// </para>
/// </summary>
internal sealed class RegionInstaller(AssetDownload download, string packagesFolder)
{
    public const string MarkerName = ".orthostreamxp-region.json";
    public const string WorkFolderName = ".orthostream-download";
    /// <summary>Unpacked, a region takes about this much more than its zips (DSFs grow, each small .ter takes a whole disk block).</summary>
    private const double UnpackedRatio = 1.7;
    private const long SpareBytes = 2L << 30;
    /// <summary>The work folder of tiles added to an installed region: beside the region's own, which an install clears.</summary>
    private const string SupplementWorkSuffix = ".supplement";

    /// <param name="SupplementMesh">The version of the supplement whose mesh is added to the prepared region.</param>
    /// <param name="SupplementOverlays">The version of the supplement whose overlays are in <paramref name="OverlayFiles"/>.</param>
    /// <param name="SupplementMeshFiles">The mesh files a supplement placed (or is placing) in the prepared region.</param>
    /// <param name="SupplementOverlayFiles">Those of <paramref name="OverlayFiles"/> a supplement placed (or is placing).</param>
    private sealed record State(List<string> Unpacked, List<string>? OverlayFiles, int? SupplementMesh = null, int? SupplementOverlays = null,
        List<string>? SupplementMeshFiles = null, List<string>? SupplementOverlayFiles = null);

    public string PackagesFolder { get; } = packagesFolder;

    private string WorkRoot => Path.Join(PackagesFolder, WorkFolderName);

    private OverlayPackage Overlays => new(PackagesFolder);

    /// <summary>The regions installed in the packages folder, by id.</summary>
    public IReadOnlyDictionary<string, InstalledRegion> Installed()
    {
        var found = new Dictionary<string, InstalledRegion>();
        foreach (var (id, _, package) in MeshRegion.Known)
        {
            // Still under its old name when it was in use at the start: renamed before anything is installed or deleted.
            string folder = LegacyPackageNames.OldFolderOf(PackagesFolder, package) ?? Path.Join(PackagesFolder, package);
            string marker = Path.Join(folder, MarkerName);
            try
            {
                if (File.Exists(marker) && JsonSerializer.Deserialize<InstalledRegion>(File.ReadAllText(marker)) is { } region)
                    found[id] = region;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                Log.Warning($"Could not read {marker}: {ex.Message}");
            }
        }
        return found;
    }

    /// <summary>Bytes of a region already downloaded or unpacked by an install that stopped: what it will not download again.</summary>
    public long AlreadyDone(MeshRegion region, bool withOverlays)
    {
        string work = WorkOf(region);
        State state = LoadState(work);
        long done = region.Mesh.Where(a => state.Unpacked.Contains(a.Name)).Sum(a => a.Size);
        if (withOverlays && state.OverlayFiles is not null) done += region.OverlaysSize;
        foreach (MeshAsset asset in Pending(region, withOverlays, state))
            done += Math.Min(asset.Size, Length(Path.Join(work, "zips", asset.Name)));
        if (region.Supplement is { } supplement)
        {
            done += state.SupplementMesh == supplement.Version ? supplement.Mesh.Size : Math.Min(supplement.Mesh.Size, Length(Path.Join(work, "zips", supplement.Mesh.Name)));
            if (withOverlays && supplement.Overlays is { } overlays)
                done += state.SupplementOverlays == supplement.Version ? overlays.Size : Math.Min(overlays.Size, Length(Path.Join(work, "zips", overlays.Name)));
        }
        return done;
    }

    /// <summary>
    /// Installs or updates <paramref name="region"/>; a stop leaves what was downloaded for the next time. A region installed
    /// at this release that only lacks its supplement's tiles gets those alone (<see cref="AddSupplementAsync"/>).
    /// </summary>
    /// <exception cref="InstallException">Not enough disk space, a download that failed, or a damaged zip.</exception>
    public async Task InstallAsync(MeshRegion region, bool withOverlays, IProgress<InstallProgress> progress, CancellationToken cancel)
    {
        CheckRegion(region);
        MoveOldNames();
        if (region.Supplement is { } lacking && Installed().GetValueOrDefault(region.Id) is { } installed
            && installed.Release == region.Release && installed.Lacks(lacking))
        {
            await AddSupplementAsync(region.Id, lacking, progress, cancel).ConfigureAwait(false);
            return;
        }
        string work = WorkOf(region);
        PrepareWork(work);
        State state = LoadState(work);
        long total = region.DownloadSize(withOverlays);
        long done = AlreadyDone(region, withOverlays);
        // A zip is downloaded while the one before is unpacked: two can be on the disk at once.
        CheckSpace(total - done, region.Mesh.Count == 0 ? 0 : region.Mesh.Max(a => a.Size));
        Log.Info($"Installing the {region.Name} mesh (release {region.Release}{(withOverlays ? ", with its overlays" : "")}) into {PackagesFolder}" +
                 (done > 0 ? $"; {CacheInventory.FormatBytes(done)} of {CacheInventory.FormatBytes(total)} done before" : ""));
        void Downloaded(long bytes)
        {
            done += bytes;
            progress.Report(new InstallProgress($"Downloading the {region.Name} mesh", done, total));
        }

        // Stopped by the person, or by a zip that cannot be unpacked while the next one downloads.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        Task unpacking = Task.CompletedTask;
        string unpackingName = "";
        async Task UnpackMeshAsync(string name, string zip)
        {
            try
            {
                await Task.Run(() =>
                {
                    using FileStream file = File.OpenRead(zip);
                    ZipUnpacker.Unpack(file, Path.Join(work, "mesh"), stop.Token);
                }, stop.Token).ConfigureAwait(false);
                // Recorded only once whole: a stop in the middle unpacks the zip again next time, over what is there.
                state.Unpacked.Add(name);
                SaveState(work, state);
                File.Delete(zip);
            }
            catch
            {
                await stop.CancelAsync().ConfigureAwait(false); // no use downloading on: the download alongside stops
                throw;
            }
        }

        try
        {
            try
            {
                // Taken before the loop: the unpack running alongside adds to state.Unpacked.
                MeshAsset[] todo = [.. region.Mesh.Where(a => !state.Unpacked.Contains(a.Name))];
                foreach (MeshAsset asset in todo)
                {
                    string zip = Path.Join(work, "zips", asset.Name);
                    await download.GetAsync(asset, zip, Downloaded, stop.Token).ConfigureAwait(false);
                    // One unpack at a time, in the list's order: the one before ends first.
                    if (!unpacking.IsCompleted) progress.Report(new InstallProgress($"Unpacking {unpackingName}", done, total));
                    await unpacking.ConfigureAwait(false);
                    (unpacking, unpackingName) = (UnpackMeshAsync(asset.Name, zip), asset.Name);
                }

                var parts = new List<string>();
                bool overlays = withOverlays && state.OverlayFiles is null && region.Overlays.Count > 0;
                if (overlays)
                    foreach (MeshAsset part in region.Overlays) // while the last mesh zip is unpacked
                    {
                        string file = Path.Join(work, "zips", part.Name);
                        await download.GetAsync(part, file, Downloaded, stop.Token).ConfigureAwait(false);
                        parts.Add(file);
                    }
                if (!unpacking.IsCompleted) progress.Report(new InstallProgress($"Unpacking {unpackingName}", done, total));
                await unpacking.ConfigureAwait(false);
                if (overlays)
                {
                    progress.Report(new InstallProgress($"Unpacking the {region.Name} overlays", done, total));
                    IReadOnlyList<string> files = await UnpackPartsAsync(parts, Path.Join(work, "overlays"), cancel).ConfigureAwait(false);
                    state = state with { OverlayFiles = [.. files] };
                    SaveState(work, state);
                    foreach (string part in parts) File.Delete(part);
                }
            }
            catch (Exception error)
            {
                // Nothing may write once the install has ended: the unpack running alongside ends first.
                Exception? unpackError = await EndAsync(unpacking).ConfigureAwait(false);
                if (unpackError is not null && error is OperationCanceledException && !cancel.IsCancellationRequested)
                    ExceptionDispatchInfo.Throw(unpackError); // the download was stopped by that failure: it is the cause
                if (unpackError is not null and not OperationCanceledException && !ReferenceEquals(unpackError, error))
                    Log.Warning($"Unpacking {unpackingName} failed as well: {unpackError.Message}");
                throw;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or HttpRequestException or UnauthorizedAccessException)
        {
            throw new InstallException($"The {region.Name} mesh could not be installed: {ex.Message}", ex);
        }

        int? supplement = null;
        if (region.Supplement is { } tiles)
        {
            progress.Report(new InstallProgress($"Downloading the tiles AutoOrtho lacks for {region.Name}", done, total));
            (state, supplement) = await AddSupplementToWorkAsync(region, tiles, withOverlays, work, state, Downloaded, cancel).ConfigureAwait(false);
        }

        cancel.ThrowIfCancellationRequested(); // a Stop just before the end: nothing is put in place
        progress.Report(new InstallProgress($"Putting the {region.Name} mesh in place", total, total));
        PutInPlace(region, work, withOverlays ? state.OverlayFiles : null,
            new InstalledRegion(region.Id, region.Release, withOverlays && state.OverlayFiles is not null, DateTime.UtcNow, supplement,
                state.SupplementMeshFiles, withOverlays ? state.SupplementOverlayFiles : null));
        Log.Info($"The {region.Name} mesh is installed as {region.PackageName}{(withOverlays ? $", its overlays in {MeshRegion.OverlaysPackageName}" : "")}" +
                 (supplement is null ? "" : $", with the {region.Supplement!.Tiles.Count} tiles AutoOrtho lacks"));
    }

    /// <summary>
    /// Adds the tiles AutoOrtho lacks to the region prepared in <paramref name="work"/>, never over a file of AutoOrtho's
    /// (one a supplement placed is replaced by a newer one). Each half, mesh and overlays, is recorded as done once added,
    /// and its files before they move, so a stopped install finishes it next time. When the tiles cannot be had the region
    /// is installed without them: that is logged, and they are added later.
    /// </summary>
    /// <returns>The state, and the supplement's version once all of it is added (null when it failed).</returns>
    private async Task<(State State, int? Version)> AddSupplementToWorkAsync(MeshRegion region, RegionSupplement supplement, bool withOverlays,
        string work, State state, Action<long> downloaded, CancellationToken cancel)
    {
        string staging = Path.Join(work, "supplement");
        try
        {
            if (state.SupplementMesh != supplement.Version)
            {
                string from = Path.Join(staging, "mesh"), to = Path.Join(work, "mesh");
                IReadOnlyList<string> files = await DownloadAndUnpackAsync(supplement.Mesh, work, from, downloaded, cancel).ConfigureAwait(false);
                HashSet<string> ours = Set(state.SupplementMeshFiles);
                List<string> placing = Placeable(files, to, ours);
                state = state with { SupplementMeshFiles = [.. ours.Union(placing, StringComparer.OrdinalIgnoreCase)] };
                SaveState(work, state);
                MoveAll(from, placing, to);
                state = state with { SupplementMesh = supplement.Version };
                SaveState(work, state);
            }
            if (withOverlays && state.OverlayFiles is { } overlayFiles && supplement.Overlays is { } overlays && state.SupplementOverlays != supplement.Version)
            {
                string from = Path.Join(staging, "overlays"), to = Path.Join(work, "overlays");
                IReadOnlyList<string> files = await DownloadAndUnpackAsync(overlays, work, from, downloaded, cancel).ConfigureAwait(false);
                HashSet<string> ours = Set(state.SupplementOverlayFiles);
                var autoOrtho = new HashSet<string>(overlayFiles.Where(f => !ours.Contains(f)), StringComparer.OrdinalIgnoreCase);
                List<string> placing = [.. files.Where(f => !autoOrtho.Contains(f))];
                List<string> oursNow = [.. ours.Union(placing, StringComparer.OrdinalIgnoreCase)];
                state = state with { OverlayFiles = [.. autoOrtho, .. oursNow], SupplementOverlayFiles = oursNow };
                SaveState(work, state);
                MoveAll(from, placing, to);
                state = state with { SupplementOverlays = supplement.Version };
                SaveState(work, state);
            }
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            bool overlaysDone = !withOverlays || state.OverlayFiles is null || supplement.Overlays is null || state.SupplementOverlays == supplement.Version;
            return (state, state.SupplementMesh == supplement.Version && overlaysDone ? supplement.Version : null);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or HttpRequestException or UnauthorizedAccessException)
        {
            Log.Warning($"The tiles AutoOrtho lacks could not be added to the {region.Name} mesh ({ex.Message}); it is installed without them, and they are added later");
            // The overlays recorded before they moved, some perhaps not there: the region takes only those that are.
            string overlaysFolder = Path.Join(work, "overlays");
            bool There(string file) => File.Exists(Path.Join(overlaysFolder, file));
            return (state with
            {
                OverlayFiles = state.OverlayFiles is { } all ? [.. all.Where(There)] : null,
                SupplementOverlayFiles = state.SupplementOverlayFiles is { } ours ? [.. ours.Where(There)] : null,
            }, null);
        }
    }

    /// <summary>
    /// Adds the tiles AutoOrtho lacks to an installed region that has not got them (installed before they were published,
    /// or by an older OrthoStreamXP), or a newer version of them: the mesh into its package, the overlays into the shared
    /// one when the region has its overlays. A file the region has is kept unless an earlier supplement placed it, so
    /// AutoOrtho's stay; files are mostly added, so this may run while X-Plane uses the region, which sees the new tiles from
    /// its next start. A stop leaves what was downloaded for the next time.
    /// </summary>
    /// <exception cref="InstallException">The region is not installed, not enough disk space, a download that failed, or a damaged zip.</exception>
    public async Task AddSupplementAsync(string regionId, RegionSupplement supplement, IProgress<InstallProgress> progress, CancellationToken cancel)
    {
        CheckSupplement(regionId, supplement, []);
        MoveOldNames();
        string name = MeshRegion.NameOf(regionId);
        if (Installed().GetValueOrDefault(regionId) is not { } installed)
            throw new InstallException($"The {name} mesh is not installed: the tiles AutoOrtho lacks are added with it.");
        string package = Path.Join(PackagesFolder, MeshRegion.PackageOf(regionId));
        string work = Path.Join(WorkRoot, regionId + SupplementWorkSuffix, supplement.Version.ToString(CultureInfo.InvariantCulture));
        PrepareWork(work);
        bool withOverlays = installed.Overlays && supplement.Overlays is not null;
        long total = supplement.DownloadSize(withOverlays);
        long done = supplement.Assets(withOverlays).Sum(a => Math.Min(a.Size, Length(Path.Join(work, "zips", a.Name))));
        CheckSpace(total - done, 0);
        Log.Info($"Adding the {supplement.Tiles.Count} tiles AutoOrtho lacks (version {supplement.Version}) to the {name} mesh in {PackagesFolder}");
        void Downloaded(long bytes)
        {
            done += bytes;
            progress.Report(new InstallProgress($"Downloading the tiles AutoOrtho lacks for {name}", done, total));
        }

        string marker = Path.Join(package, MarkerName);
        int mesh, overlays = 0;
        try
        {
            string staging = Path.Join(work, "mesh");
            IReadOnlyList<string> files = await DownloadAndUnpackAsync(supplement.Mesh, work, staging, Downloaded, cancel).ConfigureAwait(false);
            HashSet<string> ours = Set(installed.SupplementFiles);
            List<string> placing = Placeable(files, package, ours);
            // Recorded as the supplement's before they move: a stop in the middle replaces them next time, as its own.
            installed = installed with { SupplementFiles = [.. ours.Union(placing, StringComparer.OrdinalIgnoreCase)] };
            AtomicFile.Write(marker, JsonSerializer.SerializeToUtf8Bytes(installed));
            MoveAll(staging, placing, package);
            mesh = placing.Count;
            if (withOverlays)
            {
                string overlayStaging = Path.Join(work, "overlays");
                files = await DownloadAndUnpackAsync(supplement.Overlays!, work, overlayStaging, Downloaded, cancel).ConfigureAwait(false);
                HashSet<string> oursOverlays = Set(installed.SupplementOverlayFiles);
                IReadOnlyList<string> placed = Overlays.Append(regionId, overlayStaging, files, oursOverlays);
                installed = installed with { SupplementOverlayFiles = [.. oursOverlays.Union(placed, StringComparer.OrdinalIgnoreCase)] };
                overlays = placed.Count;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or HttpRequestException or UnauthorizedAccessException)
        {
            throw new InstallException($"The tiles AutoOrtho lacks could not be added to the {name} mesh: {ex.Message}", ex);
        }

        // The version last: a stop before it adds the tiles again next time, over what is there.
        AtomicFile.Write(marker, JsonSerializer.SerializeToUtf8Bytes(installed with { Supplement = supplement.Version }));
        Directory.Delete(work, recursive: true);
        DeleteIfEmpty(Path.GetDirectoryName(work)!);
        DeleteIfEmpty(WorkRoot);
        Log.Info($"The tiles AutoOrtho lacks are added to the {name} mesh: {mesh} files of mesh, {overlays} of overlays " +
                 "(AutoOrtho's files were kept)");
    }

    /// <summary>Downloads a zip into the work folder, unpacks it into <paramref name="destination"/> and deletes it; returns its files, relative.</summary>
    private async Task<IReadOnlyList<string>> DownloadAndUnpackAsync(MeshAsset asset, string work, string destination, Action<long> downloaded, CancellationToken cancel)
    {
        string zip = Path.Join(work, "zips", asset.Name);
        await download.GetAsync(asset, zip, downloaded, cancel).ConfigureAwait(false);
        IReadOnlyList<string> files = await Task.Run(() =>
        {
            using FileStream file = File.OpenRead(zip);
            return ZipUnpacker.Unpack(file, destination, cancel);
        }, cancel).ConfigureAwait(false);
        File.Delete(zip);
        return files;
    }

    /// <summary>
    /// The supplement's files that may go into <paramref name="folder"/>: those not there yet, and those a supplement put
    /// there before (<paramref name="ours"/>), which a newer one replaces. Any other file there is AutoOrtho's and stays.
    /// </summary>
    private static List<string> Placeable(IEnumerable<string> files, string folder, HashSet<string> ours) =>
        [.. files.Where(f => ours.Contains(f) || !File.Exists(Path.Join(folder, f)))];

    /// <summary>Moves <paramref name="files"/> (relative) from <paramref name="from"/> to the same places under <paramref name="to"/>.</summary>
    private static void MoveAll(string from, IEnumerable<string> files, string to)
    {
        foreach (string file in files)
        {
            string target = Path.Join(to, file);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(Path.Join(from, file), target, overwrite: true);
        }
    }

    private static HashSet<string> Set(IEnumerable<string>? files) => new(files ?? [], StringComparer.OrdinalIgnoreCase);

    /// <summary>The downloaded parts of the overlays zip, unpacked as the one zip they make; returns the files, relative.</summary>
    private static Task<IReadOnlyList<string>> UnpackPartsAsync(IReadOnlyList<string> parts, string destination, CancellationToken cancel) =>
        Task.Run(() =>
        {
            using var zip = new ConcatenatedStream(parts);
            return ZipUnpacker.Unpack(zip, destination, cancel);
        }, cancel);

    /// <summary>Waits for a task to end, whatever way it ends; returns its exception, or null.</summary>
    private static async Task<Exception?> EndAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    /// <summary>Deletes an installed region and its overlays.</summary>
    /// <exception cref="InstallException">A package still under its old name could not be renamed.</exception>
    public void Delete(string regionId)
    {
        MoveOldNames();
        string package = Path.Join(PackagesFolder, MeshRegion.PackageOf(regionId));
        // The marker first: a region half deleted is no longer taken for installed.
        string marker = Path.Join(package, MarkerName);
        if (File.Exists(marker)) File.Delete(marker);
        if (Directory.Exists(package)) Directory.Delete(package, recursive: true);
        Overlays.Remove(regionId);
        Log.Info($"The {MeshRegion.NameOf(regionId)} mesh was deleted from {PackagesFolder}");
    }

    private void PutInPlace(MeshRegion region, string work, IReadOnlyList<string>? overlayFiles, InstalledRegion marker)
    {
        string package = Path.Join(PackagesFolder, region.PackageName);
        string mesh = Path.Join(work, "mesh");
        string old = Path.Join(work, "old");
        if (Directory.Exists(package)) Directory.Move(package, old); // an update: the new release takes its place
        try
        {
            Directory.Move(mesh, package);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (Directory.Exists(old) && !Directory.Exists(package)) Directory.Move(old, package);
            throw new InstallException($"The {region.Name} mesh could not be put in place: {ex.Message}", ex);
        }
        AtomicFile.Write(Path.Join(package, MarkerName), JsonSerializer.SerializeToUtf8Bytes(marker));

        OverlayPackage overlays = Overlays;
        overlays.Remove(region.Id);
        if (overlayFiles is not null) overlays.Add(region.Id, Path.Join(work, "overlays"), overlayFiles);

        Directory.Delete(work, recursive: true);
        DeleteIfEmpty(Path.GetDirectoryName(work)!);
        DeleteIfEmpty(WorkRoot);
    }

    /// <summary>
    /// Renames what an earlier version installed (<see cref="LegacyPackageNames"/>) first: installed or deleted under the new
    /// name alone, a region would be in the packages folder twice, or not deleted.
    /// </summary>
    private void MoveOldNames()
    {
        IReadOnlyList<string> left = LegacyPackageNames.MovePackages(PackagesFolder);
        if (left.Count == 0) return;
        throw new InstallException($"{string.Join(", ", left)} in {PackagesFolder} could not take its new name ({SceneryPacks.DownloadedPrefix}...). " +
                                   "Close what uses it, such as File Explorer, or remove the folder of the new name if one is there, and try again.");
    }

    private static void DeleteIfEmpty(string folder)
    {
        if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
    }

    /// <summary>The work folder; one beside it, left by another release or version, is deleted (a new one starts afresh).</summary>
    private void PrepareWork(string work)
    {
        string regionWork = Path.GetDirectoryName(work)!;
        if (Directory.Exists(regionWork))
            foreach (string other in Directory.EnumerateDirectories(regionWork))
                if (!string.Equals(Path.GetFullPath(other), Path.GetFullPath(work), StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(other, recursive: true);
        Directory.CreateDirectory(work);
        File.SetAttributes(WorkRoot, File.GetAttributes(WorkRoot) | FileAttributes.Hidden);
    }

    /// <summary>
    /// The region as the list of releases describes it is used for paths and downloads: a known region, plain file names
    /// (never a path that leads elsewhere) and HTTPS addresses only, whether the list came from GitHub or from the copy kept.
    /// </summary>
    private static void CheckRegion(MeshRegion region)
    {
        if (!MeshRegion.IsKnown(region.Id)) throw new InstallException($"'{region.Id}' is not a region OrthoStreamXP knows.");
        foreach (MeshAsset asset in region.Mesh.Concat(region.Overlays))
            if (!asset.IsUsable)
                throw new InstallException($"The list of the {region.Name} mesh's files has an entry that cannot be used ({asset.Name}). Press Refresh, then try again.");
        if (region.Supplement is { } supplement)
            CheckSupplement(region.Id, supplement, [.. region.Mesh.Concat(region.Overlays).Select(a => a.Name)]);
    }

    /// <summary>
    /// The same for the tiles AutoOrtho lacks, whose files also share the work folder's zips with AutoOrtho's
    /// (<paramref name="otherNames"/>): a name of theirs is refused.
    /// </summary>
    private static void CheckSupplement(string regionId, RegionSupplement supplement, IReadOnlyCollection<string> otherNames)
    {
        if (!MeshRegion.IsKnown(regionId)) throw new InstallException($"'{regionId}' is not a region OrthoStreamXP knows.");
        foreach (MeshAsset asset in supplement.Assets(withOverlays: true))
            if (!asset.IsUsable || otherNames.Contains(asset.Name, StringComparer.OrdinalIgnoreCase))
                throw new InstallException($"The list of the tiles AutoOrtho lacks for the {MeshRegion.NameOf(regionId)} mesh has an entry that cannot be used ({asset.Name}). Press Refresh, then try again.");
    }

    /// <param name="toDownload">What is left to download.</param>
    /// <param name="extraZip">A zip that may be on the disk beside the one being unpacked.</param>
    private void CheckSpace(long toDownload, long extraZip)
    {
        if (SystemCheck.FreeBytes(PackagesFolder) is not { } free) return;
        long needed = (long)(toDownload * UnpackedRatio) + extraZip + SpareBytes;
        if (free < needed)
            throw new InstallException($"Not enough disk space: the install needs about {CacheInventory.FormatBytes(needed)} " +
                                       $"and the drive of {PackagesFolder} has {CacheInventory.FormatBytes(free)} free.");
    }

    private static IEnumerable<MeshAsset> Pending(MeshRegion region, bool withOverlays, State state) =>
        region.Mesh.Where(a => !state.Unpacked.Contains(a.Name))
            .Concat(withOverlays && state.OverlayFiles is null ? region.Overlays : []);

    private string WorkOf(MeshRegion region) => Path.Join(WorkRoot, region.Id, Safe(region.Release));

    private static string Safe(string release) => string.Concat(release.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private static State LoadState(string work)
    {
        string file = Path.Join(work, "state.json");
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<State>(File.ReadAllText(file)) ?? new State([], null) : new State([], null);
        }
        catch (JsonException)
        {
            return new State([], null); // unreadable: the zips are unpacked again, over what is there
        }
    }

    private static void SaveState(string work, State state) =>
        AtomicFile.Write(Path.Join(work, "state.json"), JsonSerializer.SerializeToUtf8Bytes(state));

    private static long Length(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;
}
