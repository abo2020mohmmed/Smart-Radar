using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;

namespace WinImageStudio.Core;

[SupportedOSPlatform("windows")]
public sealed class ImageEngine : IDisposable
{
    private readonly ICommandRunner runner;
    private readonly IProjectStore store;
    private readonly ToolSettings tools;
    private readonly string script;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly OfflineRegistry registry;
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> operationNotices = new();
    public IReadOnlyList<string> OperationNotices => operationNotices.ToArray();
    public ImageEngine(ICommandRunner runner, IProjectStore store, ToolSettings tools, string script)
    {
        this.runner = runner; this.store = store; this.tools = tools;
        this.script = Validation.ExistingFile(script, ".ps1");
        registry = new OfflineRegistry(runner, tools.Reg);
    }
    public async Task<ProjectSession> CreateAsync(string source, string parent, CancellationToken token)
    {
        source = Validation.ExistingFile(source, ".wim", ".esd", ".swm", ".iso");
        parent = Path.GetFullPath(parent); Directory.CreateDirectory(parent); Validation.RejectReparseAncestors(parent);
        string root = Path.Combine(parent, "WinImageStudio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var project = new ProjectSession { Root = root, Source = source, WorkingWim = Path.Combine(root, "install.wim"), MountPath = Path.Combine(root, "mount"), ImageSource = source };
        Directory.CreateDirectory(project.MountPath); Directory.CreateDirectory(Path.Combine(root, "logs"));
        await store.SaveAsync(project, token).ConfigureAwait(false);
        await ExclusiveAsync(async () =>
        {
            if (Path.GetExtension(source).Equals(".iso", StringComparison.OrdinalIgnoreCase))
            {
                project.MediaPath = Path.Combine(root, "media");
                await JournalAsync(project, "Extract installation ISO", async () =>
                {
                    await PowerShellAsync("ExtractIso", token, "-Source", source, "-Destination", project.MediaPath).ConfigureAwait(false);
                    string sources = Path.Combine(project.MediaPath, "sources");
                    project.ImageSource = new[] { "install.wim", "install.esd", "install.swm" }.Select(x => Path.Combine(sources, x)).FirstOrDefault(File.Exists)
                        ?? throw new FileNotFoundException("The ISO has no supported Windows installation image.");
                }).ConfigureAwait(false);
            }
            if (Path.GetExtension(project.ImageSource).Equals(".swm", StringComparison.OrdinalIgnoreCase))
            {
                string wimlib = Validation.ExistingFile(tools.Wimlib ?? throw new InvalidOperationException("Split SWM import requires wimlib-imagex.exe. Configure it before importing."), ".exe");
                string first = project.ImageSource!;
                string directory = Path.GetDirectoryName(first)!;
                string stem = Path.GetFileNameWithoutExtension(first);
                if (stem.Any(char.IsDigit)) throw new ArgumentException("Select the first split file, for example install.swm, not install2.swm.");
                string[] parts = Directory.GetFiles(directory, stem + "*.swm").Where(x => Path.GetFileNameWithoutExtension(x) == stem || int.TryParse(Path.GetFileNameWithoutExtension(x)[stem.Length..], out _)).OrderBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase).ToArray();
                if (parts.Length == 0) throw new FileNotFoundException("No SWM parts were found.");
                string joined = Path.Combine(root, "joined.wim");
                await JournalAsync(project, "Join split SWM image", async () =>
                {
                    var args = new List<string> { "join", joined }; args.AddRange(parts);
                    (await runner.RunAsync(wimlib, args, token).ConfigureAwait(false)).EnsureSuccess("Join split image");
                    project.ImageSource = joined;
                }).ConfigureAwait(false);
            }
            await store.SaveAsync(project).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
        return project;
    }
    public async Task<IReadOnlyList<ImageInfo>> InspectAsync(ProjectSession project, CancellationToken token)
    {
        return await ExclusiveAsync(async () =>
        {
            string source = Validation.ExistingFile(project.ImageSource ?? project.Source, ".wim", ".esd");
            string json = await PowerShellAsync("Images", token, "-Source", source).ConfigureAwait(false);
            return (IReadOnlyList<ImageInfo>)(JsonSerializer.Deserialize<ImageInfo[]>(json, JsonOptions) ?? []);
        }, token).ConfigureAwait(false);
    }
    public Task ImportAsync(ProjectSession project, ImageInfo image, CancellationToken token) => ExclusiveAsync(async () =>
    {
        if (project.State is ImageState.Mounted or ImageState.RecoveryRequired) throw new InvalidOperationException("Commit/discard or recover the existing mount first.");
        if (File.Exists(project.WorkingWim)) throw new IOException("This project already has a working image. Create a new project to select another edition.");
        string source = Validation.ExistingFile(project.ImageSource ?? project.Source, ".wim", ".esd");
        await JournalAsync(project, "Export selected image into project", async () =>
        {
            await DismAsync(project, DismCommands.Export(source, image.ImageIndex, project.WorkingWim), token).ConfigureAwait(false);
            project.Image = image; project.State = ImageState.Prepared;
        }).ConfigureAwait(false);
    }, token);
    public Task MountAsync(ProjectSession project, CancellationToken token) => ExclusiveAsync(async () =>
    {
        if (project.State is ImageState.Mounted or ImageState.RecoveryRequired) throw new InvalidOperationException("The project is mounted or requires recovery.");
        Validation.ExistingFile(project.WorkingWim, ".wim");
        if (Directory.EnumerateFileSystemEntries(project.MountPath).Any()) throw new IOException("Mount directory is not empty.");
        await JournalAsync(project, "Mount working image", async () =>
        {
            await DismAsync(project, DismCommands.Mount(project.WorkingWim, project.MountPath), token).ConfigureAwait(false);
            project.State = ImageState.Mounted;
        }).ConfigureAwait(false);
    }, token);
    public Task<Inventory> InventoryAsync(ProjectSession project, CancellationToken token) => ExclusiveAsync(() => ReadInventoryCoreAsync(project, token), token);
    private async Task<Inventory> ReadInventoryCoreAsync(ProjectSession project, CancellationToken token)
    {
        RequireMounted(project);
        Inventory? inventory = null;
        await JournalAsync(project, "Read offline image inventory", async () =>
        {
            string json = await PowerShellAsync("Inventory", token, "-MountPath", project.MountPath, "-ProjectId", project.Id).ConfigureAwait(false);
            inventory = JsonSerializer.Deserialize<Inventory>(json, JsonOptions) ?? throw new InvalidDataException("Invalid image inventory.");
            project.State = ImageState.Mounted;
        }).ConfigureAwait(false);
        return inventory!;
    }
    public Task<AggressiveReport> AnalyzeAggressiveAsync(ProjectSession project, AggressiveOptions options, CancellationToken token) => ExclusiveAsync(async () =>
    {
        var inventory = await ReadInventoryCoreAsync(project, token).ConfigureAwait(false);
        var report = AggressivePreset.Build(inventory, options);
        await File.WriteAllTextAsync(Path.Combine(project.Root, "aggressive-report.json"), JsonSerializer.Serialize(report, ProjectStore.JsonOptions), token).ConfigureAwait(false);
        return report;
    }, token);
    public Task ApplyAsync(ProjectSession project, IReadOnlyList<PlannedOperation> operations, CancellationToken token, IProgress<PlannedOperation>? completed = null) => ExclusiveAsync(async () =>
    {
        RequireMounted(project);
        foreach (var operation in operations) Validation.Operation(operation);
        operationNotices.Clear();
        if (operations.Any(o => o.PresetId is not null))
        {
            var fresh = await ReadInventoryCoreAsync(project, token).ConfigureAwait(false);
            AggressivePreset.ValidatePlan(operations, fresh);
        }
        await File.WriteAllTextAsync(Path.Combine(project.Root, "last-plan.json"), JsonSerializer.Serialize(operations, ProjectStore.JsonOptions), token).ConfigureAwait(false);
        foreach (var operation in operations)
        {
            token.ThrowIfCancellationRequested();
            await JournalAsync(project, operation.Description, async () =>
            {
                switch (operation.Kind)
                {
                    case OperationKind.SetRegistry:
                        await registry.SetValueAsync(project, operation.Registry!, token).ConfigureAwait(false); break;
                    case OperationKind.SetServiceStart:
                        await registry.SetServiceAsync(project, operation.Target, int.Parse(operation.Arguments, CultureInfo.InvariantCulture), token, AggressivePreset.KnownPreset(operation.PresetId)).ConfigureAwait(false); break;
                    case OperationKind.RemoveOneDrive:
                        foreach (string notice in await registry.RemoveOneDriveAsync(project, token).ConfigureAwait(false)) operationNotices.Enqueue(notice);
                        break;
                    case OperationKind.StageInstaller:
                        await InstallerStager.StageAsync(project.MountPath, operation.Target, operation.Arguments, token).ConfigureAwait(false); break;
                    case OperationKind.WriteUnattend:
                        string xml = Path.Combine(project.Root, "Autounattend.xml");
                        UnattendWriter.Create(operation.Unattend!).Save(xml);
                        if (project.MediaPath is not null) File.Copy(xml, Path.Combine(project.MediaPath, "Autounattend.xml"), true);
                        break;
                    default:
                        await DismAsync(project, DismCommands.Change(project.MountPath, operation), token).ConfigureAwait(false); break;
                }
                project.State = ImageState.Mounted;
            }).ConfigureAwait(false);
            completed?.Report(operation);
        }
        if (operations.Any(o => AggressivePreset.KnownPreset(o.PresetId)))
        {
            var after = await ReadInventoryCoreAsync(project, token).ConfigureAwait(false);
            string summary = AggressivePreset.AfterApplySummary(after);
            operationNotices.Enqueue(summary);
            await File.WriteAllTextAsync(Path.Combine(project.Root, "aggressive-after.txt"), string.Join(Environment.NewLine, OperationNotices), token).ConfigureAwait(false);
        }
    }, token);
    public Task UnmountAsync(ProjectSession project, bool commit, CancellationToken token) => ExclusiveAsync(async () =>
    {
        RequireMounted(project);
        await JournalAsync(project, commit ? "Commit image" : "Discard mounted changes", async () =>
        {
            await DismAsync(project, DismCommands.Unmount(project.MountPath, commit), token).ConfigureAwait(false);
            project.State = commit ? ImageState.Committed : ImageState.Discarded;
        }).ConfigureAwait(false);
    }, token);
    public Task RecoverDiscardAsync(ProjectSession project, CancellationToken token) => ExclusiveAsync(async () =>
    {
        await registry.RecoverUnloadAsync(project).ConfigureAwait(false);
        string json = await PowerShellAsync("Mounts", token).ConfigureAwait(false);
        var mounts = JsonSerializer.Deserialize<MountRecord[]>(json, JsonOptions) ?? [];
        var mount = mounts.FirstOrDefault(x => string.Equals(Path.GetFullPath(x.Path), Path.GetFullPath(project.MountPath), StringComparison.OrdinalIgnoreCase));
        await JournalAsync(project, "Recover this project's mount and discard changes", async () =>
        {
            if (mount is not null)
            {
                if (mount.Status.Equals("NeedsRemount", StringComparison.OrdinalIgnoreCase)) await DismAsync(project, ["/English", "/Remount-Image", "/MountDir:" + project.MountPath], token).ConfigureAwait(false);
                await DismAsync(project, DismCommands.Unmount(project.MountPath, false), token).ConfigureAwait(false);
            }
            if (File.Exists(project.WorkingWim)) await DismAsync(project, ["/English", "/Get-WimInfo", "/WimFile:" + project.WorkingWim], token).ConfigureAwait(false);
            project.State = ImageState.Discarded;
        }).ConfigureAwait(false);
    }, token);
    public Task BuildIsoAsync(ProjectSession project, string output, CancellationToken token, bool useEsd = true) => ExclusiveAsync(async () =>
    {
        if (project.State != ImageState.Committed) throw new InvalidOperationException("Commit the image successfully before building the ISO.");
        string media = project.MediaPath ?? throw new InvalidOperationException("ISO output requires a project imported from a complete Windows installation ISO.");
        string oscdimg = Validation.ExistingFile(tools.Oscdimg ?? throw new InvalidOperationException("Configure oscdimg.exe from Windows ADK."), ".exe");
        output = Path.GetFullPath(output);
        Validation.RejectReparseAncestors(output);
        if (!Path.GetExtension(output).Equals(".iso", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Output must end with .iso.");
        if (File.Exists(output)) throw new IOException("Output already exists; choose a new filename.");
        string mediaPrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(media)) + Path.DirectorySeparatorChar;
        if (output.StartsWith(mediaPrefix, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("ISO output cannot be inside its source media directory.");
        string temporary = output + ".partial-" + Guid.NewGuid().ToString("N");
        await JournalAsync(project, "Build bootable ISO", async () =>
        {
            string sources = Path.Combine(media, "sources");
            string destination = Validation.UnderRoot(project.Root, Path.Combine(sources, useEsd ? "install.esd" : "install.wim"));
            // Re-export into a new container. Copying the edited WIM retains old/unreferenced data.
            string stagedImage = Validation.UnderRoot(project.Root, Path.Combine(project.Root, "final-" + Guid.NewGuid().ToString("N") + (useEsd ? ".esd" : ".wim")));
            try
            {
                await DismAsync(project, DismCommands.ExportFinal(project.WorkingWim, stagedImage, useEsd), token).ConfigureAwait(false);
                if (!File.Exists(stagedImage) || new FileInfo(stagedImage).Length == 0) throw new IOException("DISM did not produce a nonempty installation image.");
                File.Move(stagedImage, destination, true);
            }
            finally { if (File.Exists(stagedImage)) File.Delete(stagedImage); }
            foreach (string file in Directory.GetFiles(sources))
            {
                string name = Path.GetFileName(file);
                bool install = name.Equals("install.wim", StringComparison.OrdinalIgnoreCase) || name.Equals("install.esd", StringComparison.OrdinalIgnoreCase) || name.Equals("install.swm", StringComparison.OrdinalIgnoreCase) ||
                    (name.StartsWith("install", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".swm", StringComparison.OrdinalIgnoreCase) && int.TryParse(name[7..^4], out _));
                if (install && !file.Equals(destination, StringComparison.OrdinalIgnoreCase)) { Validation.UnderRoot(project.Root, file); File.Delete(file); }
            }
            var result = await runner.RunAsync(oscdimg, DismCommands.Iso(media, temporary), token).ConfigureAwait(false);
            result.EnsureSuccess("Build ISO");
            File.Move(temporary, output, false);
            var summary = new IsoBuildSummary(useEsd ? "ESD / recovery" : "WIM / max",
                Path.GetExtension(project.Source).Equals(".iso", StringComparison.OrdinalIgnoreCase) && File.Exists(project.Source) ? new FileInfo(project.Source).Length : null,
                new FileInfo(output).Length, new FileInfo(project.WorkingWim).Length, new FileInfo(destination).Length);
            await File.WriteAllTextAsync(Path.Combine(project.Root, "iso-build-summary.json"), JsonSerializer.Serialize(summary, ProjectStore.JsonOptions), CancellationToken.None).ConfigureAwait(false);
            project.State = ImageState.Committed;
        }).ConfigureAwait(false);
    }, token);
    public Task<string> AuditCommittedAsync(ProjectSession project, CancellationToken token) => ExclusiveAsync(async () =>
    {
        if (project.State != ImageState.Committed) throw new InvalidOperationException("احفظ الصورة وألغِ تركيبها قبل الفحص.");
        if (Directory.EnumerateFileSystemEntries(project.MountPath).Any()) throw new IOException("مجلد التركيب غير فارغ؛ لا يمكن بدء الفحص.");
        string report = Validation.UnderRoot(project.Root, Path.Combine(project.Root, "image-audit.html"));
        await JournalAsync(project, "Audit saved image without committing changes", async () =>
        {
            Inventory inventory;
            bool mounted = false;
            Exception? primary = null;
            try
            {
                await DismAsync(project, DismCommands.Mount(project.WorkingWim, project.MountPath), token).ConfigureAwait(false);
                mounted = true;
                string json = await PowerShellAsync("Inventory", token, "-MountPath", project.MountPath, "-ProjectId", project.Id).ConfigureAwait(false);
                inventory = JsonSerializer.Deserialize<Inventory>(json, JsonOptions) ?? throw new InvalidDataException("Invalid image audit inventory.");
                string installers = Validation.UnderRoot(project.MountPath, Path.Combine(project.MountPath, "WinImageStudio", "Installers"));
                var staged = Directory.Exists(installers) ? Directory.EnumerateFiles(installers).Where(f => Path.GetExtension(f).ToLowerInvariant() is ".exe" or ".msi")
                    .Select(f => new InventoryItem(Path.GetFileName(f), "Staged, not verified installed", "SetupComplete installer payload")).ToArray() : [];
                inventory = inventory with { StagedInstallers = staged };
            }
            catch (Exception ex) { primary = ex; throw; }
            finally
            {
                if (mounted)
                {
                    try
                    {
                        if (primary is not null) await registry.RecoverUnloadAsync(project).ConfigureAwait(false);
                        await DismAsync(project, DismCommands.Unmount(project.MountPath, false), CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception cleanup) { if (primary is not null) throw new AggregateException(primary, cleanup); throw; }
                }
            }
            await File.WriteAllTextAsync(Validation.UnderRoot(project.Root, Path.Combine(project.Root, "image-audit.json")), JsonSerializer.Serialize(inventory, ProjectStore.JsonOptions), token).ConfigureAwait(false);
            await File.WriteAllTextAsync(report, ImageAuditReport.Html(project, inventory), token).ConfigureAwait(false);
            project.State = ImageState.Committed;
        }).ConfigureAwait(false);
        return report;
    }, token);
    private async Task DismAsync(ProjectSession project, IReadOnlyList<string> arguments, CancellationToken token)
    {
        Validation.UnderRoot(project.Root, project.MountPath);
        var args = arguments.ToList();
        string log = Path.Combine(project.Root, "logs", "dism-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".log");
        args.Add("/LogPath:" + log);
        (await runner.RunAsync(tools.Dism, args, token).ConfigureAwait(false)).EnsureSuccess("DISM");
    }
    private async Task<string> PowerShellAsync(string action, CancellationToken token, params string[] parameters)
    {
        var args = new List<string> { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-Action", action };
        args.AddRange(parameters);
        var result = await runner.RunAsync(tools.PowerShell, args, token).ConfigureAwait(false);
        result.EnsureSuccess("Windows image query " + action);
        return result.StandardOutput.Trim().TrimStart('\uFEFF');
    }
    private async Task JournalAsync(ProjectSession project, string description, Func<Task> action)
    {
        project.PendingOperation = description; project.LastError = null;
        project.State = ImageState.RecoveryRequired;
        await store.SaveAsync(project).ConfigureAwait(false);
        try
        {
            await action().ConfigureAwait(false);
            if (project.State == ImageState.RecoveryRequired) project.State = ImageState.Prepared;
            project.PendingOperation = null;
            await store.SaveAsync(project).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            project.State = ImageState.RecoveryRequired; project.LastError = ex.Message;
            try { await store.SaveAsync(project).ConfigureAwait(false); }
            catch (Exception saveError) { throw new AggregateException("Operation failed and recovery journal could not be updated.", ex, saveError); }
            throw;
        }
    }
    private static void RequireMounted(ProjectSession project)
    {
        if (project.State != ImageState.Mounted) throw new InvalidOperationException("Mount the image first. A failed operation requires recovery/discard before continuing.");
    }
    private async Task ExclusiveAsync(Func<Task> action, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { await Task.Run(action, CancellationToken.None).ConfigureAwait(false); } finally { gate.Release(); }
    }
    private async Task<T> ExclusiveAsync<T>(Func<Task<T>> action, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { return await Task.Run(action, CancellationToken.None).ConfigureAwait(false); } finally { gate.Release(); }
    }
    public void Dispose() => gate.Dispose();
    private sealed record MountRecord(string Path, string Status);
    private static JsonSerializerOptions JsonOptions { get; } = new() { PropertyNameCaseInsensitive = true };
}
