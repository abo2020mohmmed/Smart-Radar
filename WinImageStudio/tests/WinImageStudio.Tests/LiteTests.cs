using WinImageStudio.Core;

internal static class LiteTests
{
    public static IEnumerable<(string Name, Action Body)> Cases()
    {
        yield return ("Lite accepts Home N Education Workstation Enterprise and LTSC client editions", () =>
        {
            foreach (string edition in new[] { "Core", "CoreSingleLanguage", "Professional", "ProfessionalN", "Education", "ProfessionalWorkstation", "Enterprise", "EnterpriseS", "IoTEnterpriseS" })
            {
                var sample = StrongPresetTests.Sample(); sample = sample with { Profile = sample.Profile! with { EditionId = edition } };
                var report = AggressivePreset.Build(sample, new(AllowOtherClientEditions: true));
                Check(report.PresetId == AggressivePreset.LiteId); AggressivePreset.ValidatePlan(report.Operations, sample);
                Check(report.Operations.All(o => o.PresetId == AggressivePreset.LiteId));
            }
        });
        yield return ("Lite supports storeless LTSC and records missing source printing without fake preservation", () =>
        {
            var sample = StrongPresetTests.Sample(); sample = sample with { Apps = [], Services = [], Profile = sample.Profile! with { EditionId = "EnterpriseS" } };
            var report = AggressivePreset.Build(sample, new(AllowOtherClientEditions: true));
            Check(report.Notes.Any(n => n.Contains("المصدر أصلًا", StringComparison.Ordinal)));
            Check(report.Notes.Any(n => n.Contains("خدمة الطباعة", StringComparison.Ordinal)));
            Check(!report.Operations.Any(o => o.Kind == OperationKind.SetServiceStart && o.Target == "Spooler"));
            AggressivePreset.ValidatePlan(report.Operations, sample);
        });
        yield return ("Lite rejects Server old builds and native edition mismatches", () =>
        {
            var sample = StrongPresetTests.Sample();
            foreach (var profile in new[] { sample.Profile! with { InstallationType = "Server" }, sample.Profile! with { Build = "9600" }, sample.Profile! with { EditionId = "Mismatch:Core/Professional" }, sample.Profile! with { EditionId = "" } })
                Reject(() => AggressivePreset.Build(sample with { Profile = profile }, new(AllowOtherClientEditions: true)));
        });
        yield return ("Lite retains Store dependencies printing and Arabic English under its own canonical plan", () =>
        {
            var sample = StrongPresetTests.Sample(); var report = AggressivePreset.Build(sample, new(AllowOtherClientEditions: true));
            Check(!report.Operations.Any(o => o.Kind == OperationKind.RemoveProvisionedApp && (o.Target.StartsWith("Microsoft.WindowsStore", StringComparison.OrdinalIgnoreCase) || o.Target.StartsWith("Microsoft.VCLibs", StringComparison.OrdinalIgnoreCase))));
            Reject(() => AggressivePreset.ValidatePlan([.. report.Operations, new(OperationKind.SetServiceStart, "Spooler", "4", PresetId: AggressivePreset.LiteId)], sample));
            var legacy = AggressivePreset.Build(sample, new()).Operations[0];
            Reject(() => AggressivePreset.ValidatePlan([.. report.Operations, legacy], sample));
        });
        yield return ("Lite automatic sequence completes every action and writes one final ISO", () => Fixture(async (project, image, fake) =>
        {
            var result = await new LiteAutomation(fake).RunAsync(project, image, Path.Combine(project.Root, "output.iso"), null, default);
            Check(fake.Calls.SequenceEqual(new[] { "tools", "import", "mount", "analyze", "apply", "commit", "build" }));
            Check(File.Exists(result.IsoPath) && File.Exists(result.ReportPath) && project.WorkflowStage == 5 && project.State == ImageState.Committed);
        }));
        yield return ("Lite stops before commit and ISO on application failure", () => Fixture(async (project, image, fake) =>
        {
            fake.FailApply = true;
            try { await new LiteAutomation(fake).RunAsync(project, image, Path.Combine(project.Root, "failed.iso"), null, default); throw new Exception("Failure hidden"); } catch (IOException) { }
            Check(!fake.Calls.Contains("commit") && !fake.Calls.Contains("build") && project.WorkflowStage < 5);
        }));
        yield return ("Lite resumes a saved image at compression without applying twice", () => Fixture(async (project, image, fake) =>
        {
            File.WriteAllText(project.WorkingWim, "saved image"); project.Image = image; project.State = ImageState.Committed; project.WorkflowStage = 4;
            await new LiteAutomation(fake).RunAsync(project, image, Path.Combine(project.Root, "resumed.iso"), null, default);
            Check(fake.Calls.SequenceEqual(new[] { "tools", "build" }));
        }));
        yield return ("Lite refuses existing output files before changing the image", () => Fixture(async (project, image, fake) =>
        {
            string output = Path.Combine(project.Root, "existing.iso"); File.WriteAllText(output, "original");
            try { await new LiteAutomation(fake).RunAsync(project, image, output, null, default); throw new Exception("Overwrote existing ISO"); } catch (IOException) { }
            Check(fake.Calls.Count == 0 && File.ReadAllText(output) == "original");
        }));
        yield return ("Lite never reports success when the build produces no ISO", () => Fixture(async (project, image, fake) =>
        {
            fake.MissingOutput = true;
            try { await new LiteAutomation(fake).RunAsync(project, image, Path.Combine(project.Root, "missing.iso"), null, default); throw new Exception("False success"); } catch (IOException) { }
            Check(project.WorkflowStage == 4 && !File.Exists(Path.Combine(project.Root, "lite-result.json")));
        }));
        yield return ("Lite recovery requires explicit user action before automatic work", () => Fixture(async (project, image, fake) =>
        {
            project.State = ImageState.RecoveryRequired;
            try { await new LiteAutomation(fake).RunAsync(project, image, Path.Combine(project.Root, "recovery.iso"), null, default); throw new Exception("Implicit recovery"); } catch (InvalidOperationException) { }
            Check(fake.Calls.Count == 0);
        }));
    }
    private static void Fixture(Func<ProjectSession, ImageInfo, Fake, Task> action)
    {
        string root = Path.Combine(Path.GetTempPath(), "WIS-Lite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "media", "boot")); Directory.CreateDirectory(Path.Combine(root, "mount"));
        File.WriteAllText(Path.Combine(root, "media", "boot", "etfsboot.com"), "inert boot fixture");
        var project = new ProjectSession { Root = root, Source = Path.Combine(root, "source.iso"), WorkingWim = Path.Combine(root, "install.wim"), MountPath = Path.Combine(root, "mount"), MediaPath = Path.Combine(root, "media"), WorkflowMode = "Lite" };
        var image = new ImageInfo(2, "Windows Home", "", "amd64", "10.0.19045", "Core");
        try { action(project, image, new Fake()).GetAwaiter().GetResult(); } finally { Directory.Delete(root, true); }
    }
    private sealed class Fake : ILiteWorkflow
    {
        public List<string> Calls { get; } = []; public bool FailApply; public bool MissingOutput;
        public IReadOnlyList<string> Notices => ["OneDrive protected fixture"];
        public Task EnsureToolsAsync(CancellationToken t) { Calls.Add("tools"); return Task.CompletedTask; }
        public Task ImportAsync(ProjectSession p, ImageInfo i, CancellationToken t) { Calls.Add("import"); p.Image = i; File.WriteAllText(p.WorkingWim, "fixture"); return Task.CompletedTask; }
        public Task MountAsync(ProjectSession p, CancellationToken t) { Calls.Add("mount"); p.State = ImageState.Mounted; return Task.CompletedTask; }
        public Task<AggressiveReport> AnalyzeAsync(ProjectSession p, CancellationToken t)
        {
            Calls.Add("analyze"); var sample = StrongPresetTests.Sample(); return Task.FromResult(AggressivePreset.Build(sample with { Profile = sample.Profile! with { EditionId = "Core" } }, new(AllowOtherClientEditions: true)));
        }
        public Task ApplyAsync(ProjectSession p, IReadOnlyList<PlannedOperation> ops, CancellationToken t) { Calls.Add("apply"); if (FailApply) throw new IOException("fixture apply failed"); return Task.CompletedTask; }
        public Task CommitAsync(ProjectSession p, CancellationToken t) { Calls.Add("commit"); p.State = ImageState.Committed; return Task.CompletedTask; }
        public Task BuildIsoAsync(ProjectSession p, string output, CancellationToken t) { Calls.Add("build"); if (!MissingOutput) File.WriteAllText(output, "fixture ISO"); return Task.CompletedTask; }
        public Task SaveAsync(ProjectSession p, CancellationToken t) => Task.CompletedTask;
    }
    private static void Check(bool value) { if (!value) throw new Exception("Lite regression"); }
    private static void Reject(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Invalid Lite plan accepted"); }
}
