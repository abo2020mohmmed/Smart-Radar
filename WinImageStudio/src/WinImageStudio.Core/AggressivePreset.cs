using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WinImageStudio.Core;

public static partial class AggressivePreset
{
    public const string Id = "ProLight-v1";
    private static readonly HashSet<string> BloatApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.549981C3F5F10", "Microsoft.BingNews", "Microsoft.BingWeather", "Microsoft.BingFinance", "Microsoft.BingSports",
        "Microsoft.GetHelp", "Microsoft.Getstarted", "Microsoft.Microsoft3DViewer", "Microsoft.MicrosoftOfficeHub",
        "Microsoft.MicrosoftSolitaireCollection", "Microsoft.MixedReality.Portal", "Microsoft.Office.OneNote", "Microsoft.OneConnect",
        "Microsoft.People", "Microsoft.SkypeApp", "Microsoft.WindowsAlarms", "Microsoft.WindowsFeedbackHub", "Microsoft.WindowsMaps",
        "Microsoft.WindowsSoundRecorder", "Microsoft.YourPhone", "Microsoft.ZuneMusic", "Microsoft.ZuneVideo", "Clipchamp.Clipchamp",
        "Microsoft.OutlookForWindows", "MicrosoftTeams", "MSTeams", "Microsoft.Todos", "king.com.CandyCrushSaga", "king.com.CandyCrushSodaSaga"
    };
    private static readonly HashSet<string> GamingApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.GamingApp", "Microsoft.GamingServices", "Microsoft.Xbox.TCUI", "Microsoft.XboxApp", "Microsoft.XboxGameOverlay",
        "Microsoft.XboxGamingOverlay", "Microsoft.XboxIdentityProvider", "Microsoft.XboxSpeechToTextOverlay"
    };
    private static readonly HashSet<string> OptionalFeatures = new(StringComparer.OrdinalIgnoreCase)
    { "MicrosoftWindowsPowerShellV2", "MicrosoftWindowsPowerShellV2Root", "WorkFolders-Client", "TelnetClient", "TFTP", "SimpleTCP" };
    private static readonly HashSet<string> OptionalServices = new(StringComparer.OrdinalIgnoreCase)
    { "DiagTrack", "dmwappushservice", "RetailDemo", "MapsBroker", "WMPNetworkSvc", "XblAuthManager", "XblGameSave", "XboxGipSvc", "XboxNetApiSvc" };
    private static readonly HashSet<string> ProtectedServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "Spooler", "PrintNotify", "Fax", "AppXSvc", "ClipSVC", "InstallService", "LicenseManager", "TokenBroker", "wlidsvc", "StateRepository",
        "BITS", "wuauserv", "DoSvc", "MpsSvc", "BFE", "RpcSs", "DcomLaunch", "EventLog", "Schedule", "Winmgmt", "Audiosrv", "AudioEndpointBuilder"
    };
    private static readonly string[] ProtectedAppPrefixes = [
        "Microsoft.WindowsStore", "Microsoft.StorePurchaseApp", "Microsoft.DesktopAppInstaller", "Microsoft.VCLibs.", "Microsoft.NET.Native.",
        "Microsoft.UI.Xaml.", "Microsoft.WindowsAppRuntime.", "Microsoft.Services.Store.Engagement", "Microsoft.AAD.BrokerPlugin",
        "Microsoft.AccountsControl", "Microsoft.Windows.CloudExperienceHost", "Microsoft.Windows.ShellExperienceHost", "Microsoft.Windows.CBS",
        "Microsoft.SecHealthUI", "Microsoft.Windows.SecHealthUI", "Microsoft.Windows.Photos", "Microsoft.Paint"
    ];
    private static readonly string[] DefenderPackageRoots = ["Microsoft-Windows-Defender-Client-Package", "Microsoft-Windows-Defender-Default-Definitions-Package"];
    [GeneratedRegex(@"^Language\.(Basic|Handwriting|OCR|Speech|TextToSpeech)~~~(?<tag>[a-z]{2,3}(?:-[a-z0-9]{2,8})+)~[0-9.]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex CapabilityPattern();
    [GeneratedRegex(@"^Microsoft\.LanguageExperiencePack(?<tag>[a-z]{2,3}(?:-[a-z0-9]{2,8})+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex LxpPattern();
    public static bool IsProtectedApp(string name) => ProtectedAppPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    public static bool IsProtectedService(string name) => ProtectedServices.Contains(name) || name.StartsWith("Print", StringComparison.OrdinalIgnoreCase);
    public static bool KeepLanguage(string tag) => tag.Equals("ar", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("ar-", StringComparison.OrdinalIgnoreCase) || tag.Equals("en", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("en-", StringComparison.OrdinalIgnoreCase);
    public static string CapabilityLanguage(string name) { var m = CapabilityPattern().Match(name); return m.Success ? m.Groups["tag"].Value : ""; }
    public static string LxpLanguage(string name) { var m = LxpPattern().Match(name); return m.Success ? m.Groups["tag"].Value : ""; }
    public static void RequirePro(ImageProfile? profile)
    {
        if (profile is null || !profile.EditionId.Equals("Professional", StringComparison.OrdinalIgnoreCase) || !profile.InstallationType.Equals("Client", StringComparison.OrdinalIgnoreCase) || !int.TryParse(profile.Build, NumberStyles.None, CultureInfo.InvariantCulture, out int build) || build < 10240)
            throw new InvalidOperationException("وضع التخفيف القوي يتطلب صورة Windows 10/11 Pro العادية (Professional). لا يُقبل اسم الإصدار الظاهر وحده؛ ركّب الصورة وأعد قراءة مكوناتها.");
    }
    public static AggressiveReport Build(Inventory inventory, AggressiveOptions options)
    {
        RequirePro(inventory.Profile);
        var profile = inventory.Profile!;
        if (!inventory.Apps.Any(a => a.Details.Equals("Microsoft.WindowsStore", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Microsoft Store غير موجود في قائمة تطبيقات هذه الصورة. استخدم مصدر Pro رسميًا يحتوي المتجر قبل تطبيق هذا الوضع.");
        if (!inventory.Services.Any(s => s.Name.Equals("Spooler", StringComparison.OrdinalIgnoreCase) && s.State is "2" or "3" or "4"))
            throw new InvalidOperationException("خدمة الطباعة غير موجودة أو حالتها غير معروفة في هذه الصورة. استخدم صورة Pro تحتوي مكونات الطباعة قبل التخفيف.");
        var operations = new List<PlannedOperation>();
        var notes = new List<string>
        {
            "المتجر واعتمادياته والطباعة والتعريفات و.NET وDirectX والصوت والشبكة والتحديثات وUAC والجدار الناري والاسترداد خارج قائمة الحذف التلقائي.",
            "إزالة Xbox وGaming Services قد تمنع تشغيل ألعاب تتطلبهما، حتى مع بقاء Microsoft Store.",
            "لا يتضمن هذا الوضع تنزيل برامج من Ghost أو تعديل HPET أو ملف الذاكرة الافتراضية، ولا يضمن زيادة FPS أو مقدارًا محددًا من RAM."
        };
        void Add(OperationKind kind, string target, string reason, string args = "", RegistryChange? registry = null) => operations.Add(new(kind, target, args, Registry: registry, PresetId: Id, Reason: reason));
        foreach (var app in inventory.Apps.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!app.Removable || app.IsFramework || IsProtectedApp(app.Details)) continue;
            if (BloatApps.Contains(app.Details) || GamingApps.Contains(app.Details) || (app.Details.Equals("Microsoft.OneDriveSync", StringComparison.OrdinalIgnoreCase) || app.Details.Equals("Microsoft.OneDrive", StringComparison.OrdinalIgnoreCase)))
                Add(OperationKind.RemoveProvisionedApp, app.Name, GamingApps.Contains(app.Details) ? "إزالة Xbox/Game Pass ومكوناتهما الاختيارية" : "إزالة تطبيق إضافي من قائمة محددة؛ لا حذف عام لتطبيقات Microsoft");
        }
        foreach (var printing in inventory.Features.Where(f => f.Name.Equals("Printing-Foundation-Features", StringComparison.OrdinalIgnoreCase) && f.State.Contains("Removed", StringComparison.OrdinalIgnoreCase)))
            notes.Add("حمولة أساس الطباعة محذوفة من المصدر أصلًا؛ يلزم توفير حمولة/صورة رسمية لإعادتها. لا تُعتبر الطباعة جاهزة بمجرد إبقاء الخدمة.");
        bool knownBase = !string.IsNullOrWhiteSpace(profile.BaseLanguage);
        bool pruneLanguages = options.RemoveOtherLanguages && knownBase && !profile.PendingActions;
        if (options.RemoveOtherLanguages && !knownBase) notes.Add("لم يمكن تحديد لغة الأساس؛ لم تُضف أي خطوة لحذف اللغات حتى لا تُحذف لغة لازمة للإقلاع أو التثبيت.");
        if (options.RemoveOtherLanguages && profile.PendingActions) notes.Add("توجد عمليات servicing معلّقة؛ حُجب حذف اللغات لهذه الجلسة وفق متطلبات Windows.");
        if (knownBase && !KeepLanguage(profile.BaseLanguage)) notes.Add("لغة الأساس " + profile.BaseLanguage + " ستبقى. لإنشاء صورة بلغتي العربية والإنجليزية فقط، استخدم ISO أساسه إحدى هاتين اللغتين.");
        foreach (string family in new[] { "ar", "en" })
            if (!profile.InstalledLanguages.Any(x => x.Equals(family, StringComparison.OrdinalIgnoreCase) || x.StartsWith(family + "-", StringComparison.OrdinalIgnoreCase)))
                notes.Add((family == "ar" ? "العربية" : "الإنجليزية") + " غير مثبتة كلغة واجهة بحسب فحص الصورة. الوضع يحافظ على اللغات الموجودة ولا يضيف حزمًا غير متاحة؛ أضف حزمة اللغة الرسمية من قسم الدمج ثم أعد التحليل.");
        bool CanPrune(string tag) => pruneLanguages && !string.IsNullOrWhiteSpace(tag) && !KeepLanguage(tag) && !tag.Equals(profile.BaseLanguage, StringComparison.OrdinalIgnoreCase);
        foreach (var app in inventory.Apps)
        {
            string tag = LxpLanguage(app.Details);
            if (app.Removable && !app.IsFramework && CanPrune(tag)) Add(OperationKind.RemoveProvisionedApp, app.Name, "إزالة حزمة تجربة اللغة " + tag);
        }
        foreach (var capability in (inventory.Capabilities ?? []).Where(c => c.State.Equals("Installed", StringComparison.OrdinalIgnoreCase)).OrderBy(c => c.Name.StartsWith("Language.Basic", StringComparison.OrdinalIgnoreCase) ? 1 : 0).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            string tag = CapabilityLanguage(capability.Name);
            if (CanPrune(tag)) Add(OperationKind.RemoveCapability, capability.Name, "إزالة ميزة لغة إضافية " + tag + "؛ مع الحفاظ على الخطوط المشتركة");
        }
        foreach (var package in inventory.Packages.Where(p => p.Category.Equals("LanguagePack", StringComparison.OrdinalIgnoreCase) && p.State.Equals("Installed", StringComparison.OrdinalIgnoreCase)))
        {
            if (!CanPrune(package.Language)) continue;
            if (package.Removable) Add(OperationKind.RemovePackage, package.Name, "إزالة حزمة واجهة اللغة " + package.Language);
            else notes.Add("حزمة لغة محمية أو لم يمكن إثبات قابليتها للإزالة؛ ستبقى: " + package.Name);
        }
        if (options.RemoveDefender)
        {
            int before = operations.Count;
            foreach (var package in inventory.Packages.Where(p => DefenderPackageRoots.Any(n => p.Name.StartsWith(n + "~", StringComparison.OrdinalIgnoreCase)) && p.State.Equals("Installed", StringComparison.OrdinalIgnoreCase)))
            {
                if (package.Removable) Add(OperationKind.RemovePackage, package.Name, "محاولة إزالة حزمة Defender مخصصة يقبلها مسار servicing؛ القرار النهائي لـDISM");
                else notes.Add("Defender: حزمة محمية أو غير مؤكدة القابلية للإزالة، لم تُضف للحذف: " + package.Name);
            }
            foreach (var feature in inventory.Features.Where(f => (f.Name.Equals("Windows-Defender", StringComparison.OrdinalIgnoreCase) || f.Name.Equals("Windows-Defender-Default-Definitions", StringComparison.OrdinalIgnoreCase)) && f.Removable && !f.State.Contains("Removed", StringComparison.OrdinalIgnoreCase)))
                Add(OperationKind.RemoveFeaturePayload, feature.Name, "إزالة حمولة ميزة Defender إذا كانت معروضة كميزة اختيارية قابلة للإزالة");
            if (profile.DefenderPresent)
                notes.Add(operations.Count == before ? "Defender موجود ولا توجد له إزالة مدعومة مؤكدة في هذه الصورة. سيبقى؛ هذا الوضع لا يدّعي حذفه أو تعطيله، ولا يغيّر ملكية ملفات النظام أو حزمًا دائمة." : "أُضيفت عناصر Defender القابلة للإزالة فقط. بقاء مكوناته يُفحص بعد التطبيق؛ لا تُعتبر الصورة خالية منه قبل تقرير ما بعد التطبيق.");
            else notes.Add("لم يُكتشف محرك Defender في المسارات والخدمة المفحوصة؛ هذا فحص حالة وليس شهادة بخلو كل موارد الصورة المرتبطة به.");
        }
        foreach (var feature in inventory.Features.Where(f => OptionalFeatures.Contains(f.Name) && f.State.Equals("Enabled", StringComparison.OrdinalIgnoreCase)))
            Add(OperationKind.DisableFeature, feature.Name, "تعطيل ميزة قديمة/اختيارية محددة، دون المساس بالطباعة أو مكتبات الألعاب");
        foreach (var feature in inventory.Features.Where(f => f.Name.Equals("Printing-Foundation-Features", StringComparison.OrdinalIgnoreCase) && f.State.Equals("Disabled", StringComparison.OrdinalIgnoreCase)))
            Add(OperationKind.EnableFeature, feature.Name, "الحفاظ على دعم الطباعة بإعادة تفعيل أساس الطباعة المتاح");
        foreach (var service in inventory.Services)
        {
            if (service.Name.Equals("Spooler", StringComparison.OrdinalIgnoreCase) && service.State == "4" && service.Removable)
                Add(OperationKind.SetServiceStart, service.Name, "إعادة تشغيل خدمة الطباعة تلقائيًا", "2");
            else if (OptionalServices.Contains(service.Name) && !IsProtectedService(service.Name) && (service.State is "2" or "3") && service.Removable)
                Add(OperationKind.SetServiceStart, service.Name, "تعطيل خدمة اختيارية محددة داخل الصورة فقط", "4");
        }
        if (profile.OneDrivePresent) Add(OperationKind.RemoveOneDrive, "OneDrive", "إزالة ملفات إعداد OneDrive المخصصة ومنع تهيئته للحسابات الجديدة؛ تُبلّغ الملفات المحمية إن تعذر حذفها");
        AddPolicy(@"Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", "1", "تقليل اقتراحات التطبيقات الاستهلاكية حيث يدعم الإصدار السياسة");
        AddPolicy(@"Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", "0", "إيقاف تسجيل Game DVR في الخلفية");
        AddPolicy(@"Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", "1", "استخدام المستوى الأساسي الذي تدعمه إصدارات Pro، دون الادعاء بإلغاء كل الاتصالات");
        AddPolicy(@"Policies\Microsoft\Windows\OneDrive", "DisableFileSyncNGSC", "1", "منع استخدام OneDrive من خلال سياسة Pro");
        void AddPolicy(string key, string name, string value, string reason) => Add(OperationKind.SetRegistry, key, reason, registry: new(HiveKind.Software, key, name, ValueKind.DWord, value));
        return new AggressiveReport(Id, options, profile, operations.DistinctBy(Key).ToArray(), notes);
    }
    public static string Key(PlannedOperation operation) => JsonSerializer.Serialize(operation with { Reason = null });
    public static void ValidatePlan(IReadOnlyList<PlannedOperation> operations, Inventory freshInventory)
    {
        RequirePro(freshInventory.Profile);
        var canonical = Build(freshInventory, new()).Operations.Select(Key).ToHashSet(StringComparer.Ordinal);
        foreach (var operation in operations)
        {
            if (operation.PresetId is not null)
            {
                if (operation.PresetId != Id || !canonical.Contains(Key(operation)))
                    throw new InvalidOperationException("خطة التخفيف تغيّرت أو لم تعد مطابقة للصورة. أعد تحليل التخفيف قبل التطبيق: " + operation.Target);
            }
            if (operation.Kind == OperationKind.RemoveProvisionedApp)
            {
                var app = freshInventory.Apps.FirstOrDefault(a => a.Name.Equals(operation.Target, StringComparison.OrdinalIgnoreCase));
                string name = app?.Details ?? operation.Target.Split('_')[0];
                if (app?.IsFramework == true || IsProtectedApp(name)) throw new InvalidOperationException("الخطة تتعارض مع إبقاء المتجر أو اعتمادياته: " + name);
            }
            if ((operation.Kind is OperationKind.DisableFeature or OperationKind.RemoveFeaturePayload) && operation.Target.StartsWith("Printing-", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("التخفيف القوي يحافظ على ميزات الطباعة. احذف خطوة التعطيل المتعارضة من الخطة.");
            if (operation.Kind == OperationKind.SetServiceStart && operation.Arguments == "4" && IsProtectedService(operation.Target))
                throw new InvalidOperationException("خطوة تعطيل خدمة تتعارض مع المتجر/الطباعة/الخدمات المحفوظة: " + operation.Target);
        }
    }
    public static string AfterApplySummary(Inventory inventory) =>
        "المتجر: " + (inventory.Apps.Any(a => a.Details.Equals("Microsoft.WindowsStore", StringComparison.OrdinalIgnoreCase)) ? "موجود" : "لم يُكتشف؛ راجع الصورة") + Environment.NewLine +
        "خدمة الطباعة: " + (inventory.Services.FirstOrDefault(s => s.Name.Equals("Spooler", StringComparison.OrdinalIgnoreCase))?.State ?? "غير معروفة") + " (2 تلقائي، 3 يدوي، 4 معطل)" + Environment.NewLine +
        "Defender: " + (inventory.Profile?.DefenderPresent == true ? "ما زال موجودًا بحسب فحص المحرك/الخدمة. لم تتم إزالته بالكامل." : "لم يُكتشف في فحص المحرك/الخدمة؛ لا يمثل ذلك تدقيقًا لكل ملفات الصورة.") + Environment.NewLine +
        "OneDrive: " + (inventory.Profile?.OneDrivePresent == true ? "ما زالت عناصر مثبتة/ملفات إعداد؛ راجع إشعارات التنفيذ." : "لم تُكتشف عناصره المفحوصة.") + Environment.NewLine +
        "لغات الواجهة المكتشفة: " + string.Join(", ", inventory.Profile?.InstalledLanguages ?? []);
}
