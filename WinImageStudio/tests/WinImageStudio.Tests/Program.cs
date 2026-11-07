// Engine lifecycle tests use an injected fake runner and never call Windows registry APIs.
#pragma warning disable CA1416
using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using WinImageStudio.Core;

int passed = 0, failed = 0;
string root = Path.Combine(Path.GetTempPath(), "WinImageStudioTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
async Task Test(string name, Func<Task> action)
{
    try { await action(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception ex) { Console.Error.WriteLine("FAIL " + name + ": " + ex); failed++; }
}
Task Sync(Action action) { action(); return Task.CompletedTask; }
void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }
void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
try
{
    await Test("DISM export keeps paths with spaces in single arguments", () => Sync(() =>
    {
        var args = DismCommands.Export("C:\\Windows media\\install.esd", 3, "D:\\Work images\\install.wim");
        Equal("/SourceImageFile:C:\\Windows media\\install.esd", args[2]); Equal("/SourceIndex:3", args[3]); Equal(7, args.Count);
    }));
    await Test("Discard does not request commit-only integrity check", () => Sync(() => Equal(false, DismCommands.Unmount("mount", false).Contains("/CheckIntegrity"))));
    await Test("Commit checks image integrity", () => Sync(() => Equal(true, DismCommands.Unmount("mount", true).Contains("/CheckIntegrity"))));
    await Test("Invalid registry enum rejected", () => Sync(() => Throws<ArgumentException>(() => Validation.CheckRegistry(new((HiveKind)99, "Vendor", "x", ValueKind.String, "y")))));
    await Test("Empty unattended language rejected", () => Sync(() => Throws<ArgumentException>(() => UnattendWriter.Create(new(Language: "")))));
    await Test("Invalid export index rejected", () => Sync(() => Throws<ArgumentOutOfRangeException>(() => DismCommands.Export("a", 0, "b"))));
    await Test("Feature command has offline target and no restart", () => Sync(() =>
    {
        var args = DismCommands.Change("D:\\mount", new(OperationKind.DisableFeature, "TelnetClient"));
        Equal(true, args.Contains("/Image:D:\\mount")); Equal(true, args.Contains("/NoRestart")); Equal(false, args.Contains("/Online"));
    }));
    await Test("Feature switch injection rejected", () => Sync(() => Throws<ArgumentException>(() => Validation.Operation(new(OperationKind.EnableFeature, "x /Online")))));
    await Test("Unknown operation rejected", () => Sync(() => Throws<ArgumentException>(() => Validation.Operation(new((OperationKind)999, "x")))));
    await Test("Inbox driver removal rejected", () => Sync(() => Throws<ArgumentException>(() => Validation.Operation(new(OperationKind.RemoveDriver, "disk.inf")))));
    await Test("Third-party driver removal allowed", () => Sync(() => Validation.Operation(new(OperationKind.RemoveDriver, "oem42.inf"))));
    await Test("Parent traversal rejected", () => Sync(() => Throws<ArgumentException>(() => Validation.UnderRoot(root, Path.Combine(root, "..", "outside")))));
    await Test("Sibling prefix rejected", () => Sync(() => Throws<ArgumentException>(() => Validation.UnderRoot(root, root + "-sibling/file"))));
    await Test("Root itself is not a descendant", () => Sync(() => Throws<ArgumentException>(() => Validation.UnderRoot(root, root))));
    await Test("Valid child allowed", () => Sync(() => Equal(Path.Combine(root, "mount"), Validation.UnderRoot(root, Path.Combine(root, "mount")))));
    await Test("Installer metacharacters rejected", () => Sync(() =>
    {
        foreach (string arg in new[] { "/quiet & whoami", "%COMSPEC%", "/q\r\nwhoami", "a|b", "a^b", "a!b", "a>b", "a(b)", "\"broken" }) Throws<ArgumentException>(() => Validation.CheckInstallerArguments(arg));
    }));
    await Test("Ordinary silent switches accepted", () => Sync(() => Validation.CheckInstallerArguments("/qn /norestart PROPERTY=\"some value\"")));
    await Test("Registry traversal rejected", () => Sync(() => Throws<ArgumentException>(() => Validation.CheckRegistry(new(HiveKind.Software, "A\\..\\B", "x", ValueKind.String, "y")))));
    await Test("DWORD maximum supported", () => Sync(() => Validation.CheckRegistry(new(HiveKind.Software, "Vendor", "x", ValueKind.DWord, "4294967295"))));
    await Test("DWORD overflow rejected", () => Sync(() => Throws<OverflowException>(() => Validation.CheckRegistry(new(HiveKind.Software, "Vendor", "x", ValueKind.DWord, "4294967296")))));
    await Test("Invalid binary rejected", () => Sync(() => Throws<FormatException>(() => Validation.CheckRegistry(new(HiveKind.Software, "Vendor", "x", ValueKind.Binary, "zz")))));
    await Test("Unattend schema namespaces and passes", () => Sync(() =>
    {
        var xml = UnattendWriter.Create(new()); XNamespace ns = "urn:schemas-microsoft-com:unattend";
        Equal(ns + "unattend", xml.Root!.Name); Equal(2, xml.Root.Elements(ns + "settings").Count());
        Equal("WINPC", xml.Descendants(ns + "ComputerName").Single().Value); Equal(false, xml.ToString().Contains("Password", StringComparison.OrdinalIgnoreCase));
    }));
    await Test("Unattend XML escapes user values", () => Sync(() =>
    {
        string xml = UnattendWriter.Create(new(TimeZone: "A&B")).ToString(); Equal(true, xml.Contains("A&amp;B", StringComparison.Ordinal));
    }));
    await Test("Unattend invalid computer name rejected", () => Sync(() => Throws<ArgumentException>(() => UnattendWriter.Create(new(ComputerName: "bad/name")))));
    await Test("Unattend invalid architecture rejected", () => Sync(() => Throws<ArgumentException>(() => UnattendWriter.Create(new(Architecture: "sparc")))));
    await Test("Reboot exit accepted and failure retained", () => Sync(() =>
    {
        new ProcessResult(3010, "", "").EnsureSuccess("update"); Throws<ToolException>(() => new ProcessResult(5, "out", "denied").EnsureSuccess("update"));
    }));
    await Test("Project journal atomic roundtrip", async () =>
    {
        string folder = Path.Combine(root, "project"); Directory.CreateDirectory(folder);
        var session = new ProjectSession { Root = folder, Source = "source.wim", WorkingWim = Path.Combine(folder, "install.wim"), MountPath = Path.Combine(folder, "mount"), State = ImageState.RecoveryRequired, PendingOperation = "Mount image" };
        var store = new ProjectStore(); await store.SaveAsync(session); var loaded = await store.LoadAsync(folder);
        Equal(session.Id, loaded.Id); Equal(ImageState.RecoveryRequired, loaded.State); Equal("Mount image", loaded.PendingOperation);
        Equal(0, Directory.GetFiles(folder, "*.tmp").Length);
    });
    await Test("Tampered project root rejected", async () =>
    {
        string folder = Path.Combine(root, "tampered"); Directory.CreateDirectory(folder);
        var session = new ProjectSession { Root = folder + "-other", Source = "x", WorkingWim = Path.Combine(folder, "x"), MountPath = Path.Combine(folder, "mount") };
        await File.WriteAllTextAsync(Path.Combine(folder, "project.json"), JsonSerializer.Serialize(session, ProjectStore.JsonOptions));
        try { await new ProjectStore().LoadAsync(folder); throw new Exception("Tampered project accepted"); } catch (InvalidDataException) { }
    });
    await Test("Installer is staged, never executed on the host", async () =>
    {
        string mount = Path.Combine(root, "installer-mount"); Directory.CreateDirectory(mount);
        string input = Path.Combine(root, "sample.msi"); await File.WriteAllTextAsync(input, "inert test data");
        await InstallerStager.StageAsync(mount, input, "/qn /norestart", default);
        string script = await File.ReadAllTextAsync(Path.Combine(mount, "Windows", "Setup", "Scripts", "WinImageStudio-Install.cmd"));
        Equal(true, script.Contains("msiexec.exe", StringComparison.Ordinal)); Equal(1, Directory.GetFiles(Path.Combine(mount, "WinImageStudio", "Installers")).Length);
    });
    await Test("Foreign SetupComplete script is preserved", async () =>
    {
        string mount = Path.Combine(root, "foreign-mount"); string scripts = Path.Combine(mount, "Windows", "Setup", "Scripts"); Directory.CreateDirectory(scripts);
        string setup = Path.Combine(scripts, "SetupComplete.cmd"); await File.WriteAllTextAsync(setup, "original owner");
        try { await InstallerStager.StageAsync(mount, Path.Combine(root, "sample.msi"), "/qn", default); throw new Exception("Foreign script overwritten"); } catch (IOException) { }
        Equal("original owner", await File.ReadAllTextAsync(setup));
    });
    await Test("ISO boot images required", () => Sync(() => Throws<FileNotFoundException>(() => DismCommands.Iso(root, "out.iso"))));
    await Test("ISO contains BIOS and UEFI boot entries", () => Sync(() =>
    {
        string media = Path.Combine(root, "media"); Directory.CreateDirectory(Path.Combine(media, "boot")); Directory.CreateDirectory(Path.Combine(media, "efi", "microsoft", "boot"));
        File.WriteAllText(Path.Combine(media, "boot", "etfsboot.com"), "x"); File.WriteAllText(Path.Combine(media, "efi", "microsoft", "boot", "efisys.bin"), "x");
        var args = DismCommands.Iso(media, "out.iso"); Equal(true, args.Any(x => x.StartsWith("-bootdata:2#p0,e,b", StringComparison.Ordinal) && x.Contains("#pEF,e,b", StringComparison.Ordinal)));
    }));

    async Task<ProjectSession> NewEngineProject(string name)
    {
        string folder = Path.Combine(root, name); Directory.CreateDirectory(folder); Directory.CreateDirectory(Path.Combine(folder, "mount")); Directory.CreateDirectory(Path.Combine(folder, "logs"));
        string wim = Path.Combine(folder, "install.wim"); await File.WriteAllTextAsync(wim, "inert mocked WIM");
        return new ProjectSession { Root = folder, Source = "source.wim", WorkingWim = wim, MountPath = Path.Combine(folder, "mount") };
    }
    string mockScript = Path.Combine(root, "mock.ps1"); await File.WriteAllTextAsync(mockScript, "# Mocked runner: never executed");
    ImageEngine Engine(FakeRunner runner) => new(runner, new ProjectStore(), new ToolSettings("powershell", "dism", "reg"), mockScript);
    await Test("Mount journals recovery state before starting the tool", async () =>
    {
        var project = await NewEngineProject("engine-mount-ok");
        var runner = new FakeRunner(async _ =>
        {
            var journal = await new ProjectStore().LoadAsync(project.Root);
            Equal(ImageState.RecoveryRequired, journal.State); Equal("Mount working image", journal.PendingOperation);
            return new ProcessResult(0, "", "");
        });
        using var engine = Engine(runner); await engine.MountAsync(project, default);
        Equal(ImageState.Mounted, project.State); Equal(ImageState.Mounted, (await new ProjectStore().LoadAsync(project.Root)).State);
    });
    await Test("Failed mount remains recoverable with the original error", async () =>
    {
        var project = await NewEngineProject("engine-mount-fail");
        using var engine = Engine(new FakeRunner(_ => Task.FromResult(new ProcessResult(5, "", "denied"))));
        try { await engine.MountAsync(project, default); throw new Exception("Expected tool failure"); } catch (ToolException) { }
        var journal = await new ProjectStore().LoadAsync(project.Root);
        Equal(ImageState.RecoveryRequired, journal.State); Equal(true, journal.LastError!.Contains("denied", StringComparison.Ordinal));
    });
    await Test("Plan validation completes before any mutation", async () =>
    {
        var project = await NewEngineProject("engine-plan-invalid"); project.State = ImageState.Mounted;
        var runner = new FakeRunner(_ => Task.FromResult(new ProcessResult(0, "", "")));
        using var engine = Engine(runner);
        try { await engine.ApplyAsync(project, [new(OperationKind.DisableFeature, "ValidFeature"), new(OperationKind.DisableFeature, "x /Online")], default); throw new Exception("Expected validation failure"); } catch (ArgumentException) { }
        Equal(0, runner.Calls.Count); Equal(ImageState.Mounted, project.State);
    });
    await Test("Plan stops at the first failed servicing command", async () =>
    {
        var project = await NewEngineProject("engine-plan-fail"); project.State = ImageState.Mounted;
        var runner = new FakeRunner(args => Task.FromResult(new ProcessResult(args.Contains("/FeatureName:Second") ? 87 : 0, "", "test failure")));
        using var engine = Engine(runner);
        try { await engine.ApplyAsync(project, [new(OperationKind.DisableFeature, "First"), new(OperationKind.DisableFeature, "Second"), new(OperationKind.DisableFeature, "Third")], default); throw new Exception("Expected failure"); } catch (ToolException) { }
        Equal(2, runner.Calls.Count); Equal(ImageState.RecoveryRequired, project.State);
        Equal(true, project.PendingOperation!.Contains("Second", StringComparison.Ordinal));
    });
    await Test("Cancellation between steps preserves completed image state", async () =>
    {
        var project = await NewEngineProject("engine-plan-cancel"); project.State = ImageState.Mounted;
        using var cts = new CancellationTokenSource();
        var runner = new FakeRunner(_ => { cts.Cancel(); return Task.FromResult(new ProcessResult(0, "", "")); });
        using var engine = Engine(runner);
        try { await engine.ApplyAsync(project, [new(OperationKind.DisableFeature, "First"), new(OperationKind.DisableFeature, "Second")], cts.Token); throw new Exception("Expected cancellation"); } catch (OperationCanceledException) { }
        Equal(1, runner.Calls.Count); Equal(ImageState.Mounted, project.State); Equal<string?>(null, project.PendingOperation);
    });
    await Test("One engine serializes competing mount requests", async () =>
    {
        var project = await NewEngineProject("engine-concurrency");
        var runner = new FakeRunner(async _ => { await Task.Delay(30); return new ProcessResult(0, "", ""); });
        using var engine = Engine(runner);
        Task first = engine.MountAsync(project, default); Task second = engine.MountAsync(project, default);
        await first;
        try { await second; throw new Exception("Second mount should be refused"); } catch (InvalidOperationException) { }
        Equal(1, runner.Calls.Count);
    });
    await Test("Commit and discard end in distinct durable states", async () =>
    {
        foreach (bool commit in new[] { true, false })
        {
            var project = await NewEngineProject("engine-unmount-" + commit); project.State = ImageState.Mounted;
            var runner = new FakeRunner(_ => Task.FromResult(new ProcessResult(0, "", ""))); using var engine = Engine(runner);
            await engine.UnmountAsync(project, commit, default);
            Equal(commit ? ImageState.Committed : ImageState.Discarded, (await new ProjectStore().LoadAsync(project.Root)).State);
            Equal(true, runner.Calls.Single().Contains(commit ? "/Commit" : "/Discard"));
        }
    });


    await Test("Strong analysis journals real inventory and saves a reviewable report", async () =>
    {
        var project = await NewEngineProject("engine-strong-analyze"); project.State = ImageState.Mounted;
        var runner = new FakeRunner(_ => Task.FromResult(new ProcessResult(0, JsonSerializer.Serialize(StrongPresetTests.Sample()), "")));
        using var engine = Engine(runner); var report = await engine.AnalyzeAggressiveAsync(project, new(), default);
        Equal(AggressivePreset.Id, report.PresetId); Equal(1, runner.Calls.Count); Equal(ImageState.Mounted, project.State);
        Equal(true, File.Exists(Path.Combine(project.Root, "aggressive-report.json")));
    });
    await Test("Strong plan revalidates before DISM and reports surviving Defender", async () =>
    {
        var project = await NewEngineProject("engine-strong-apply"); project.State = ImageState.Mounted;
        var inventory = StrongPresetTests.Sample();
        var operation = AggressivePreset.Build(inventory,new()).Operations.First(o=>o.Kind==OperationKind.RemoveProvisionedApp && o.Target.StartsWith("Microsoft.GetHelp_"));
        var runner = new FakeRunner(args => Task.FromResult(new ProcessResult(0,args.Contains("Inventory") ? JsonSerializer.Serialize(inventory) : "", "")));
        using var engine = Engine(runner); await engine.ApplyAsync(project, [operation], default);
        Equal(3, runner.Calls.Count); Equal(true, runner.Calls[0].Contains("Inventory")); Equal(true, runner.Calls[1].Contains("/Remove-ProvisionedAppxPackage")); Equal(true, runner.Calls[2].Contains("Inventory"));
        Equal(true, engine.OperationNotices.Any(n=>n.Contains("لم تتم إزالته بالكامل"))); Equal(true, File.Exists(Path.Combine(project.Root,"aggressive-after.txt")));
    });
    await Test("Strong plan rejects a non-Pro image before any servicing mutation", async () =>
    {
        var project = await NewEngineProject("engine-strong-wrong-edition"); project.State = ImageState.Mounted;
        var inventory = StrongPresetTests.Sample(); inventory=inventory with { Profile=inventory.Profile! with { EditionId="Core" } };
        var runner = new FakeRunner(_=>Task.FromResult(new ProcessResult(0,JsonSerializer.Serialize(inventory),"")));
        using var engine = Engine(runner);
        try { await engine.ApplyAsync(project,[new(OperationKind.DisableFeature,"TelnetClient",PresetId:AggressivePreset.Id)],default); throw new Exception("Expected edition rejection"); } catch (InvalidOperationException) { }
        Equal(1,runner.Calls.Count); Equal(ImageState.Mounted,project.State);
    });
    await Test("Tampered strong service plan is rejected before registry writes", async () =>
    {
        var project = await NewEngineProject("engine-strong-tamper"); project.State=ImageState.Mounted;
        var runner = new FakeRunner(_=>Task.FromResult(new ProcessResult(0,JsonSerializer.Serialize(StrongPresetTests.Sample()),"")));
        using var engine=Engine(runner);
        try { await engine.ApplyAsync(project,[new(OperationKind.SetServiceStart,"WinDefend","4",PresetId:AggressivePreset.Id)],default); throw new Exception("Expected canonical-plan rejection"); } catch (InvalidOperationException) { }
        Equal(1,runner.Calls.Count); Equal(ImageState.Mounted,project.State);
    });
    await Test("Strong preflight query failure retains recovery state", async () =>
    {
        var project = await NewEngineProject("engine-strong-preflight-fail"); project.State=ImageState.Mounted;
        var runner = new FakeRunner(_=>Task.FromResult(new ProcessResult(87,"","mocked inventory error")));
        using var engine=Engine(runner);
        try { await engine.AnalyzeAggressiveAsync(project,new(),default); throw new Exception("Expected native query failure"); } catch (ToolException) { }
        Equal(ImageState.RecoveryRequired,project.State); Equal(1,runner.Calls.Count);
    });
    foreach (var test in StrongPresetTests.Cases()) await Test(test.Name, () => Sync(test.Body));
    if (!OperatingSystem.IsWindows())
    {
        await Test("Reparse/symlink output path rejected", () => Sync(() =>
        {
            string link = Path.Combine(root, "link"); Directory.CreateSymbolicLink(link, Path.GetTempPath());
            Throws<IOException>(() => Validation.UnderRoot(root, Path.Combine(link, "file")));
        }));
        await Test("Process runner drains stdout and stderr", async () =>
        {
            var result = await new ProcessRunner().RunAsync("/bin/dash", ["-c", "printf 'out'; printf 'err' >&2"], default);
            Equal(0, result.ExitCode); Equal("out", result.StandardOutput.Trim()); Equal("err", result.StandardError.Trim());
        });
        await Test("Cancellation does not kill a started servicing process", async () =>
        {
            using var cts = new CancellationTokenSource(); cts.CancelAfter(30);
            var result = await new ProcessRunner().RunAsync("/bin/dash", ["-c", "sleep 0.15; printf 'completed'"], cts.Token);
            Equal("completed", result.StandardOutput.Trim()); Equal(true, cts.IsCancellationRequested);
        });
        await Test("Cancellation before start prevents execution", async () =>
        {
            using var cts = new CancellationTokenSource(); cts.Cancel();
            try { await new ProcessRunner().RunAsync("/bin/dash", ["-c", "exit 0"], cts.Token); throw new Exception("Expected cancellation"); } catch (OperationCanceledException) { }
        });
    }
}
finally { Directory.Delete(root, true); }
Console.WriteLine($"{passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;

sealed class FakeRunner(Func<IReadOnlyList<string>, Task<ProcessResult>> behavior) : ICommandRunner
{
    public List<IReadOnlyList<string>> Calls { get; } = [];
    public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); Calls.Add(arguments.ToArray()); return behavior(arguments);
    }
}
