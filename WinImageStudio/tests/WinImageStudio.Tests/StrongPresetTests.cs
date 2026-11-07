using System.Text.Json;
using WinImageStudio.Core;

internal static class StrongPresetTests
{
    private static InventoryItem App(string name, bool removable = true, bool framework = false) => new(name + "_1.0_neutral__test", "Provisioned", name, removable, IsFramework: framework);
    private static InventoryItem Language(string tag, bool removable = true) => new("Microsoft-Windows-Client-LanguagePack-Package~token~amd64~" + tag + "~10.0.1.0", "Installed", "", removable, "LanguagePack", tag);
    private static Inventory Fixture() => new(
        [new("Printing-Foundation-Features", "Enabled", ""), new("MicrosoftWindowsPowerShellV2", "Enabled", ""), new("NetFx3", "Enabled", ""), new("DirectPlay", "Enabled", ""), new("SMB1Protocol", "Enabled", "")],
        [Language("en-US"), Language("ar-SA"), Language("fr-FR"), new("Microsoft-Windows-Defender-Client-Package~token~amd64~~10.0.1.0", "Installed", "Permanent", false, "Defender")], [],
        [App("Microsoft.WindowsStore"), App("Microsoft.StorePurchaseApp"), App("Microsoft.DesktopAppInstaller"), App("Microsoft.VCLibs.140.00", framework:true), App("Microsoft.NET.Native.Runtime.2.2", framework:true), App("Microsoft.UI.Xaml.2.8", framework:true), App("Microsoft.WindowsAppRuntime.1.6", framework:true), App("Microsoft.SecHealthUI"), App("Microsoft.Windows.Photos"), App("Microsoft.GamingApp"), App("Microsoft.XboxGamingOverlay"), App("Microsoft.GetHelp"), App("Microsoft.OneDriveSync"), App("Microsoft.LanguageExperiencePackfr-FR"), App("Microsoft.LanguageExperiencePackar-SA")],
        [new("Spooler", "2", ""), new("AppXSvc", "3", ""), new("wuauserv", "3", ""), new("MpsSvc", "2", ""), new("DiagTrack", "2", ""), new("XblAuthManager", "3", ""), new("WinDefend", "2", "", false)],
        new("Professional", "Client", "19045", "en-US", ["en-US", "ar-SA", "fr-FR"], false, true, true),
        [new("Language.TextToSpeech~~~fr-FR~0.0.1.0", "Installed", ""), new("Language.Basic~~~fr-FR~0.0.1.0", "Installed", ""), new("Language.Basic~~~en-US~0.0.1.0", "Installed", ""), new("Language.Basic~~~ar-SA~0.0.1.0", "Installed", ""), new("Language.Fonts.Arab~~~und-ARAB~0.0.1.0", "Installed", "")]);
    public static Inventory Sample() => Fixture();
    private static AggressiveReport Report(Inventory? inventory = null, AggressiveOptions? options = null) => AggressivePreset.Build(inventory ?? Fixture(), options ?? new());
    private static void Check(bool value) { if (!value) throw new Exception("Preset assertion failed."); }
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return; } throw new Exception("Unsafe/invalid input was accepted."); }
    private static bool Has(AggressiveReport report, OperationKind kind, string targetPart) => report.Operations.Any(o => o.Kind == kind && o.Target.Contains(targetPart, StringComparison.OrdinalIgnoreCase));
    public static IEnumerable<(string Name, Action Body)> Cases()
    {
        yield return ("Preset accepts standard Professional client", () => Check(Report().Profile.EditionId == "Professional"));
        yield return ("Preset rejects Home, Pro N, Education, Workstations and Enterprise", () =>
        {
            foreach (string edition in new[] { "Core", "ProfessionalN", "ProfessionalEducation", "ProfessionalWorkstation", "Enterprise" }) Reject(() => Report(Fixture() with { Profile = Fixture().Profile! with { EditionId = edition } }));
        });
        yield return ("Preset rejects Server even if edition is labeled Professional", () => Reject(() => Report(Fixture() with { Profile = Fixture().Profile! with { InstallationType = "Server" } })));
        yield return ("Preset rejects unknown profile", () => Reject(() => Report(Fixture() with { Profile = null })));
        yield return ("Preset rejects unsupported pre-Windows-10 builds", () => Reject(() => Report(Fixture() with { Profile = Fixture().Profile! with { Build = "7601" } })));
        yield return ("Preset rejects a source missing Microsoft Store", () => Reject(() => Report(Fixture() with { Apps = Fixture().Apps.Where(a => a.Details != "Microsoft.WindowsStore").ToArray() })));
        yield return ("Preset rejects a source missing its printing service", () => Reject(() => Report(Fixture() with { Services=Fixture().Services.Where(s=>s.Name!="Spooler").ToArray() })));
        yield return ("Preset retains Store, purchase, App Installer and framework dependencies", () =>
        {
            foreach (var app in Fixture().Apps.Where(a => AggressivePreset.IsProtectedApp(a.Details))) Check(!Has(Report(), OperationKind.RemoveProvisionedApp, app.Name));
        });
        yield return ("Preset removes known Xbox and Gaming App packages", () => Check(Has(Report(), OperationKind.RemoveProvisionedApp, "Microsoft.XboxGamingOverlay_") && Has(Report(), OperationKind.RemoveProvisionedApp, "Microsoft.GamingApp_")));
        yield return ("Preset removes cataloged bloat and OneDrive provisioning", () => Check(Has(Report(), OperationKind.RemoveProvisionedApp, "Microsoft.GetHelp_") && Has(Report(), OperationKind.RemoveProvisionedApp, "Microsoft.OneDriveSync_")));
        yield return ("Preset never removes unknown vendor apps by wildcard", () => Check(!Has(Report(Fixture() with { Apps = Fixture().Apps.Append(App("Vendor.SomeApplication")).ToArray() }), OperationKind.RemoveProvisionedApp, "Vendor.SomeApplication")));
        yield return ("Nonremovable bloat remains", () => Check(!Has(Report(Fixture() with { Apps = Fixture().Apps.Where(a => a.Details != "Microsoft.GetHelp").Append(App("Microsoft.GetHelp", removable:false)).ToArray() }), OperationKind.RemoveProvisionedApp, "Microsoft.GetHelp_")));
        yield return ("Framework flag overrides a removal catalog match", () => Check(!Has(Report(Fixture() with { Apps = Fixture().Apps.Where(a => a.Details != "Microsoft.GamingApp").Append(App("Microsoft.GamingApp", framework:true)).ToArray() }), OperationKind.RemoveProvisionedApp, "Microsoft.GamingApp_")));
        yield return ("All Arabic and English regional variants are protected", () =>
        {
            foreach (string language in new[] { "ar", "ar-SA", "ar-EG", "en", "en-US", "en-GB", "EN-AU" }) Check(AggressivePreset.KeepLanguage(language));
            Check(!AggressivePreset.KeepLanguage("fr-FR"));
        });
        yield return ("Arabic and English packages, capabilities and LXP remain", () =>
        {
            var report = Report(); Check(!report.Operations.Any(o => o.Kind == OperationKind.RemovePackage && (o.Target.Contains("~ar-SA~") || o.Target.Contains("~en-US~"))));
            Check(!Has(report, OperationKind.RemoveCapability, "~~~ar-SA~")); Check(!Has(report, OperationKind.RemoveCapability, "~~~en-US~")); Check(!Has(report, OperationKind.RemoveProvisionedApp, "LanguageExperiencePackar-SA"));
        });
        yield return ("Foreign language is removed in LXP/FOD/LP order", () =>
        {
            var operations = Report().Operations.ToList();
            int lxp = operations.FindIndex(o => o.Kind == OperationKind.RemoveProvisionedApp && o.Target.Contains("LanguageExperiencePackfr-FR"));
            int speech = operations.FindIndex(o => o.Kind == OperationKind.RemoveCapability && o.Target.StartsWith("Language.TextToSpeech"));
            int basic = operations.FindIndex(o => o.Kind == OperationKind.RemoveCapability && o.Target.StartsWith("Language.Basic"));
            int package = operations.FindIndex(o => o.Kind == OperationKind.RemovePackage && o.Target.Contains("~fr-FR~"));
            Check(lxp >= 0 && speech > lxp && basic > speech && package > basic);
        });
        yield return ("Foreign base language is retained with an explicit notice", () =>
        {
            var report = Report(Fixture() with { Profile = Fixture().Profile! with { BaseLanguage = "fr-FR" } });
            Check(!Has(report, OperationKind.RemovePackage, "~fr-FR~")); Check(!Has(report, OperationKind.RemoveCapability, "~~~fr-FR~")); Check(!Has(report, OperationKind.RemoveProvisionedApp, "LanguageExperiencePackfr-FR")); Check(report.Notes.Any(n => n.Contains("لغة الأساس fr-FR")));
        });
        yield return ("Unknown base language blocks all language removal", () =>
        {
            var report = Report(Fixture() with { Profile = Fixture().Profile! with { BaseLanguage = "" } });
            Check(!Has(report, OperationKind.RemoveCapability, "Language.")); Check(!Has(report, OperationKind.RemovePackage, "LanguagePack")); Check(!Has(report, OperationKind.RemoveProvisionedApp, "LanguageExperiencePack"));
        });
        yield return ("Pending servicing blocks language deletion", () =>
        {
            var report = Report(Fixture() with { Profile = Fixture().Profile! with { PendingActions = true } });
            Check(!Has(report, OperationKind.RemoveCapability, "Language.")); Check(!Has(report, OperationKind.RemovePackage, "LanguagePack"));
        });
        yield return ("Missing Arabic is reported rather than invented", () =>
        {
            var report = Report(Fixture() with { Profile = Fixture().Profile! with { InstalledLanguages = ["en-US"] } });
            Check(report.Notes.Any(n => n.StartsWith("العربية غير مثبتة"))); Check(!report.Operations.Any(o => o.Kind == OperationKind.AddPackage));
        });
        yield return ("Absent capabilities are not removed", () =>
        {
            var inv = Fixture(); Check(!Has(Report(inv with { Capabilities = inv.Capabilities!.Select(c => c with { State = "NotPresent" }).ToArray() }), OperationKind.RemoveCapability, "Language."));
        });
        yield return ("Shared font capabilities are retained", () => Check(!Has(Report(), OperationKind.RemoveCapability, "Language.Fonts")));
        yield return ("Protected foreign language package is retained", () =>
        {
            var inv = Fixture(); Check(!Has(Report(inv with { Packages = inv.Packages.Where(p => p.Language != "fr-FR").Append(Language("fr-FR",false)).ToArray() }), OperationKind.RemovePackage, "~fr-FR~"));
        });
        yield return ("Uncataloged language satellites are not blindly removed", () =>
        {
            var p = new InventoryItem("Microsoft-Windows-MSPaint-FoD-Package~token~amd64~fr-FR~10.0.1.0", "Installed", "", true, "Other", "fr-FR");
            Check(!Has(Report(Fixture() with { Packages = Fixture().Packages.Append(p).ToArray() }), OperationKind.RemovePackage, "MSPaint"));
        });
        yield return ("Protected Defender is retained and explicitly reported", () => Check(!Has(Report(), OperationKind.RemovePackage, "Defender") && Report().Notes.Any(n => n.Contains("سيبقى"))));
        yield return ("Only cataloged removable Defender packages are queued", () =>
        {
            var inv = Fixture(); Check(Has(Report(inv with { Packages = inv.Packages.Select(p => p.Category == "Defender" ? p with { Removable = true } : p).ToArray() }), OperationKind.RemovePackage, "Defender-Client-Package"));
        });
        yield return ("Defender common/shared packages are not guessed removable", () =>
        {
            var p = new InventoryItem("Microsoft-Windows-Defender-Common-Package~token~amd64~~1", "Installed", "", true, "Defender");
            Check(!Has(Report(Fixture() with { Packages = Fixture().Packages.Append(p).ToArray() }), OperationKind.RemovePackage, "Defender-Common"));
        });
        yield return ("Defender removal option can be unchecked", () =>
        {
            var inv = Fixture() with { Packages = Fixture().Packages.Select(p => p with { Removable = true }).ToArray() };
            Check(!Has(Report(inv, new(false,true)), OperationKind.RemovePackage, "Defender"));
        });
        yield return ("Language removal option can be unchecked", () => Check(!Has(Report(options:new(true,false)), OperationKind.RemoveCapability, "Language.")));
        yield return ("Preset disables only named optional features", () =>
        {
            var r = Report(); Check(Has(r, OperationKind.DisableFeature, "MicrosoftWindowsPowerShellV2"));
            foreach (string feature in new[] { "Printing-", "NetFx", "DirectPlay", "SMB1" }) Check(!Has(r, OperationKind.DisableFeature, feature));
        });
        yield return ("Disabled printing foundation and spooler are restored", () =>
        {
            var inv = Fixture() with { Features = Fixture().Features.Select(f => f.Name.StartsWith("Printing-") ? f with { State="Disabled" } : f).ToArray(), Services=Fixture().Services.Select(s => s.Name=="Spooler" ? s with { State="4" } : s).ToArray() };
            var r = Report(inv); Check(Has(r, OperationKind.EnableFeature, "Printing-Foundation")); Check(r.Operations.Any(o => o.Kind==OperationKind.SetServiceStart && o.Target=="Spooler" && o.Arguments=="2"));
        });
        yield return ("Store, firewall, printing and update services stay enabled", () =>
        {
            foreach (string service in new[] { "Spooler", "AppXSvc", "wuauserv", "MpsSvc" }) Check(!Report().Operations.Any(o => o.Kind==OperationKind.SetServiceStart && o.Target==service && o.Arguments=="4"));
        });
        yield return ("Boot/kernel services are excluded by inventory flags", () =>
        {
            var inv = Fixture() with { Services = Fixture().Services.Select(s => s.Name=="DiagTrack" ? s with { Removable=false } : s).ToArray() };
            Check(!Has(Report(inv), OperationKind.SetServiceStart, "DiagTrack"));
        });
        yield return ("Already-disabled services do not create duplicate work", () =>
        {
            var inv = Fixture() with { Services = Fixture().Services.Select(s => s with { State="4" }).ToArray() };
            Check(!Has(Report(inv), OperationKind.SetServiceStart, "DiagTrack"));
        });
        yield return ("Missing OneDrive files are not claimed removed", () => Check(!Has(Report(Fixture() with { Profile=Fixture().Profile! with { OneDrivePresent=false } }), OperationKind.RemoveOneDrive, "OneDrive")));
        yield return ("OneDrive cleanup is strictly scoped", () =>
        {
            Validation.Operation(new(OperationKind.RemoveOneDrive,"OneDrive")); Reject(() => Validation.Operation(new(OperationKind.RemoveOneDrive,"C:\\Windows"))); Reject(() => Validation.Operation(new(OperationKind.RemoveOneDrive,"OneDrive","extra")));
        });
        yield return ("Language capability command is offline and separately quoted", () =>
        {
            var args = DismCommands.Change("C:\\mount space",new(OperationKind.RemoveCapability,"Language.Basic~~~fr-FR~0.0.1.0"));
            Check(args.Contains("/Remove-Capability") && args.Contains("/Image:C:\\mount space") && !args.Contains("/Online"));
            Reject(() => Validation.Operation(new(OperationKind.RemoveCapability,"Language.Basic /Online")));
        });
        yield return ("Feature payload removal uses the official Remove flag", () => Check(DismCommands.Change("mount",new(OperationKind.RemoveFeaturePayload,"Windows-Defender")).Contains("/Remove")));
        yield return ("A generated plan passes fresh inventory revalidation", () => AggressivePreset.ValidatePlan(Report().Operations,Fixture()));
        yield return ("Tampered preset target is rejected before execution", () => Reject(() => AggressivePreset.ValidatePlan([new(OperationKind.SetServiceStart,"WinDefend","4",PresetId:AggressivePreset.Id)],Fixture())));
        yield return ("Unknown preset identifiers are rejected", () => Reject(() => Validation.Operation(new(OperationKind.DisableFeature,"TelnetClient",PresetId:"unknown"))));
        yield return ("A stale package operation requires reanalysis", () =>
        {
            var operations=Report().Operations; var inv=Fixture() with { Apps=Fixture().Apps.Where(a=>a.Details!="Microsoft.GetHelp").ToArray() };
            Reject(() => AggressivePreset.ValidatePlan(operations,inv));
        });
        yield return ("A manual Store removal cannot be mixed into the strong plan", () => Reject(() => AggressivePreset.ValidatePlan(Report().Operations.Append(new(OperationKind.RemoveProvisionedApp,App("Microsoft.WindowsStore").Name)).ToArray(),Fixture())));
        yield return ("Unknown framework packages are protected in a mixed plan", () =>
        {
            var framework=App("Vendor.SharedFramework",framework:true); var inv=Fixture() with { Apps=Fixture().Apps.Append(framework).ToArray() };
            Reject(()=>AggressivePreset.ValidatePlan(AggressivePreset.Build(inv,new()).Operations.Append(new(OperationKind.RemoveProvisionedApp,framework.Name)).ToArray(),inv));
        });
        yield return ("A manual printing disable cannot be mixed into the strong plan", () => Reject(() => AggressivePreset.ValidatePlan(Report().Operations.Append(new(OperationKind.DisableFeature,"Printing-Foundation-Features")).ToArray(),Fixture())));
        yield return ("A manual spooler disable cannot override preservation", () => Reject(() => AggressivePreset.ValidatePlan(Report().Operations.Append(new(OperationKind.SetServiceStart,"Spooler","4")).ToArray(),Fixture())));
        yield return ("A manual AppX service disable cannot override preservation", () => Reject(() => AggressivePreset.ValidatePlan(Report().Operations.Append(new(OperationKind.SetServiceStart,"AppXSvc","4")).ToArray(),Fixture())));
        yield return ("Changing explanatory text does not invalidate the same operations", () => AggressivePreset.ValidatePlan(Report().Operations.Select(o=>o with { Reason="reviewed" }).ToArray(),Fixture()));
        yield return ("Preset never silently substitutes disabling Defender for removal", () =>
        {
            Check(!Report().Operations.Any(o => o.Kind==OperationKind.SetRegistry && o.Registry!.Key.Contains("Defender",StringComparison.OrdinalIgnoreCase)));
            Check(!Report().Operations.Any(o => o.Kind==OperationKind.SetServiceStart && o.Target=="WinDefend"));
        });
        yield return ("Preset leaves UAC and firewall registry settings untouched", () => Check(!Report().Operations.Any(o => o.Registry?.Name is "EnableLUA" or "EnableFirewall")));
        yield return ("After-report clearly states Defender remains", () => Check(AggressivePreset.AfterApplySummary(Fixture()).Contains("لم تتم إزالته بالكامل")));
        yield return ("Plan JSON retains preset identity and review reasons", () =>
        {
            var operations=Report().Operations; var json=JsonSerializer.Serialize(operations,ProjectStore.JsonOptions);
            var copy=JsonSerializer.Deserialize<PlannedOperation[]>(json,ProjectStore.JsonOptions)!; Check(copy.Length==operations.Count && copy.All(o=>o.PresetId==AggressivePreset.Id && o.Reason is not null));
        });
        yield return ("Repeated analysis is deterministic", () => Check(Report().Operations.Select(AggressivePreset.Key).SequenceEqual(Report().Operations.Select(AggressivePreset.Key))));
    }
}
