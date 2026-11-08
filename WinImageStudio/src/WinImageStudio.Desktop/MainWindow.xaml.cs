using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WinImageStudio.Core;
using Validation = WinImageStudio.Core.Validation;

namespace WinImageStudio.Desktop;

public partial class MainWindow : Window
{
    private readonly ProjectStore store = new();
    private readonly ObservableCollection<PlannedOperation> plan = [];
    private ImageEngine engine;
    private ProjectSession? project;
    private CancellationTokenSource? cancellation;
    private bool busy;
    private bool initialized;
    private int previousPlanCount;
    private AggressiveReport? strongReport;
    private string? strongProjectId;
    private readonly object logLock = new();
    private readonly string logPath;
    public MainWindow()
    {
        InitializeComponent();
        string logs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinImageStudio", "Logs");
        Directory.CreateDirectory(logs);
        logPath = Path.Combine(logs, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".log");
        WorkspaceBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WinImageStudioProjects");
        string defaultOscdimg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10", "Assessment and Deployment Kit", "Deployment Tools", "amd64", "Oscdimg", "oscdimg.exe");
        if (File.Exists(defaultOscdimg)) OscdimgBox.Text = defaultOscdimg;
        RegistryTypeBox.ItemsSource = Enum.GetValues<ValueKind>(); RegistryTypeBox.SelectedIndex = 0;
        PlanGrid.ItemsSource = plan;
        engine = NewEngine();
        EditorTabs.SelectedItem = SourceTab;
        initialized = true;
        plan.CollectionChanged += (_, _) =>
        {
            WorkflowRules.PendingPlanChanged(project, previousPlanCount, plan.Count);
            previousPlanCount = plan.Count; RefreshJourney();
        };
        ImagesBox.SelectionChanged += (_, _) => { if (initialized) RefreshJourney(); };
        EditorTabs.SelectionChanged += (_, args) => { if (args.Source == EditorTabs) RefreshJourney(); };
        Loaded += async (_, _) =>
        {
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length == 3 && args[1] == "--project") await OpenProjectAsync(args[2]);
        };
        RefreshJourney();
    }
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        int enabled = 1;
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
            _ = DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
    }
    [DllImport("dwmapi.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    private ImageEngine NewEngine()
    {
        var defaults = ToolSettings.WindowsDefaults();
        var settings = defaults with { Oscdimg = NullIfEmpty(OscdimgBox.Text), Wimlib = NullIfEmpty(WimlibBox.Text) };
        return new ImageEngine(new ProcessRunner(Log), store, settings, Path.Combine(AppContext.BaseDirectory, "Scripts", "ImageTools.ps1"));
    }
    private static string? NullIfEmpty(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    private ProjectSession Session() => project ?? throw new InvalidOperationException("أنشئ مشروعًا أو افتح مشروعًا سابقًا أولًا.");
    private async Task RunAsync(string title, Func<CancellationToken, Task> action)
    {
        if (busy) return;
        busy = true; RefreshJourney(); EditorTabs.IsEnabled = false; CancelButton.IsEnabled = true; Progress.IsIndeterminate = true;
        cancellation = new CancellationTokenSource(); StatusLabel.Text = title; Log(title);
        try { await action(cancellation.Token); StatusLabel.Text = "اكتملت العملية"; }
        catch (OperationCanceledException) { StatusLabel.Text = "توقفت المهمة بين الخطوات؛ راجع حالة المشروع قبل المتابعة."; Log(StatusLabel.Text); }
        catch (Exception ex)
        {
            StatusLabel.Text = "فشلت العملية؛ راجع السجل وحالة المشروع.";
            Log(ex.ToString()); UpdateSessionLabel(); RefreshJourney();
            string help = project?.State == ImageState.RecoveryRequired ? "\n\nافتح الخطوة ② لاستعادة الجلسة. الاستعادة تتجاهل التعديلات غير المحفوظة؛ بعدها أعد التجهيز والتحليل." : "";
            MessageBox.Show(this, ex.Message + help, "WinImageStudio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            cancellation.Dispose(); cancellation = null; CancelButton.IsEnabled = false; Progress.IsIndeterminate = false;
            if (project is not null)
            {
                try { await store.SaveAsync(project); }
                catch (Exception ex) { Log("تعذر حفظ تقدم الواجهة: " + ex.Message); StatusLabel.Text = "تعذر حفظ تقدم المشروع؛ راجع السجل."; }
            }
            busy = false; EditorTabs.IsEnabled = true; UpdateSessionLabel(); RefreshJourney();
        }
    }
    private void UpdateSessionLabel() => SessionLabel.Text = project is null ? "لا يوجد مشروع مفتوح" : $"{project.Image?.ImageName ?? "المصدر"} | {project.State}\n{project.Root}\n{project.PendingOperation}";
    private void Log(string message)
    {
        string line = DateTimeOffset.Now.ToString("HH:mm:ss") + " " + message;
        try { lock (logLock) File.AppendAllText(logPath, line + Environment.NewLine); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine(ex); }
        if (Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (LogBox.Text.Length > 180000) LogBox.Text = LogBox.Text[^90000..];
            LogBox.AppendText(line + Environment.NewLine); LogBox.ScrollToEnd();
        });
    }
    private static string? PickFile(string filter)
    {
        var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
    private void BrowseSource(object sender, RoutedEventArgs e) { if (PickFile("Windows images|*.iso;*.wim;*.esd;*.swm") is { } file) SourceBox.Text = file; }
    private void BrowseWorkspace(object sender, RoutedEventArgs e) { var dialog = new OpenFolderDialog(); if (dialog.ShowDialog(this) == true) WorkspaceBox.Text = dialog.FolderName; }
    private void BrowseOscdimg(object sender, RoutedEventArgs e) { if (PickFile("oscdimg|oscdimg.exe") is { } file) OscdimgBox.Text = file; }
    private void BrowseWimlib(object sender, RoutedEventArgs e) { if (PickFile("wimlib-imagex|wimlib-imagex.exe") is { } file) WimlibBox.Text = file; }
    private void BrowseInstaller(object sender, RoutedEventArgs e) { if (PickFile("Installer|*.exe;*.msi") is { } file) InstallerBox.Text = file; }
    private Task CreateProjectAsync() => RunAsync("إنشاء مشروع واستيراد المصدر", async token =>
    {
        if (project?.State is ImageState.Mounted or ImageState.RecoveryRequired) throw new InvalidOperationException("ألغِ تركيب المشروع الحالي أو استعده قبل فتح مشروع آخر.");
        ReplaceEngine();
        project = await engine.CreateAsync(SourceBox.Text, WorkspaceBox.Text, token);
        plan.Clear(); ResetStrong(); project.WorkflowMode = StrongMode.IsChecked == true ? "Strong" : "Manual";
        ImagesBox.ItemsSource = await engine.InspectAsync(project, token); SelectPro(); project.WorkflowStage = 1;
        EditorTabs.SelectedItem = PrepareTab;
    });
    private async void OpenProject(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog(); if (dialog.ShowDialog(this) != true) return;
        await OpenProjectAsync(dialog.FolderName);
    }
    private Task OpenProjectAsync(string folder) => RunAsync("فتح المشروع", async token =>
        {
            if (project?.State is ImageState.Mounted or ImageState.RecoveryRequired) throw new InvalidOperationException("ألغِ تركيب المشروع الحالي أو استعده أولًا.");
            var loaded = await store.LoadAsync(folder, token);
            plan.Clear(); project = loaded; ResetStrong();
            StrongMode.IsChecked = project.WorkflowMode != "Manual"; ManualMode.IsChecked = project.WorkflowMode == "Manual";
            ImagesBox.ItemsSource = project.Image is null ? await engine.InspectAsync(project, token) : new[] { project.Image };
            SourceBox.Text = project.Source; WorkspaceBox.Text = Path.GetDirectoryName(project.Root)!;
            OutputFileBox.Text = project.OutputIso ?? Path.Combine(project.Root, "Windows-Custom.iso");
            SelectPro();
            if (project.WorkflowStage == 0) project.WorkflowStage = project.State == ImageState.Committed ? 4 : project.State == ImageState.Mounted ? 2 : 1;
            if (project.WorkflowStage >= 3)
            {
                string reportFile = Validation.UnderRoot(project.Root, Path.Combine(project.Root, "aggressive-after.txt"));
                ReviewResultBox.Text = "✓ الخطة مطبقة/مراجعة في هذه الجلسة. " + (project.State == ImageState.Mounted ? "يمكنك حفظ الصورة مباشرة." : "الصورة محفوظة.");
                try
                {
                    if (File.Exists(reportFile) && new FileInfo(reportFile).Length <= 1024 * 1024)
                        ReviewResultBox.Text += Environment.NewLine + await File.ReadAllTextAsync(reportFile, token);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log("تعذر قراءة التقرير السابق: " + ex.Message); }
                EditorTabs.SelectedItem = project.State == ImageState.Mounted ? ReviewTab : OutputTab;
            }
            ResumePage();
        });
    private async void ReadInventory(object sender, RoutedEventArgs e) => await RunAsync("قراءة مكونات الصورة", async token =>
    {
        var inventory = await engine.InventoryAsync(Session(), token);
        FeaturesGrid.ItemsSource = inventory.Features; PackagesGrid.ItemsSource = inventory.Packages; DriversGrid.ItemsSource = inventory.Drivers; AppsGrid.ItemsSource = inventory.Apps; ServicesGrid.ItemsSource = inventory.Services;
    });
    private async Task AddAsync(Func<PlannedOperation> factory) => await RunAsync("إضافة خطوة إلى الخطة", _ =>
    {
        var operation = factory(); Validation.Operation(operation); plan.Add(operation); AcknowledgeBox.IsChecked = false; return Task.CompletedTask;
    });
    private static InventoryItem Selected(DataGrid grid) => grid.SelectedItem as InventoryItem ?? throw new InvalidOperationException("حدد عنصرًا من القائمة المناسبة.");
    private async void EnableFeature(object sender, RoutedEventArgs e) => await AddAsync(() => new(OperationKind.EnableFeature, Selected(FeaturesGrid).Name));
    private async void DisableFeature(object sender, RoutedEventArgs e) => await AddAsync(() => new(OperationKind.DisableFeature, Selected(FeaturesGrid).Name));
    private async void RemoveSelected(object sender, RoutedEventArgs e) => await AddAsync(() => InventoryTabs.SelectedIndex switch
    {
        1 => new(OperationKind.RemovePackage, Selected(PackagesGrid).Name),
        2 => new(OperationKind.RemoveDriver, Selected(DriversGrid).Name),
        3 => new(OperationKind.RemoveProvisionedApp, Selected(AppsGrid).Name),
        _ => throw new InvalidOperationException("الإزالة للحزم أو تعريفات الطرف الثالث أو التطبيقات. استخدم تعطيل للميزات.")
    });
    private async void ChangeService(object sender, RoutedEventArgs e) => await AddAsync(() => new(OperationKind.SetServiceStart, Selected(ServicesGrid).Name, ((ComboBoxItem)ServiceStartBox.SelectedItem).Tag.ToString()!));
    private async void AddPackages(object sender, RoutedEventArgs e) => await AddFilesAsync("Packages|*.cab;*.msu", OperationKind.AddPackage);
    private async void AddDrivers(object sender, RoutedEventArgs e) => await AddFilesAsync("Drivers|*.inf", OperationKind.AddDriver);
    private async Task AddFilesAsync(string filter, OperationKind kind)
    {
        var dialog = new OpenFileDialog { Filter = filter, Multiselect = true };
        if (dialog.ShowDialog(this) != true) return;
        await RunAsync("إضافة ملفات للخطة", _ =>
        {
            var operations = dialog.FileNames.Select(path => new PlannedOperation(kind, path)).ToArray();
            foreach (var operation in operations) Validation.Operation(operation);
            foreach (var operation in operations) plan.Add(operation);
            AcknowledgeBox.IsChecked = false; return Task.CompletedTask;
        });
    }
    private async void StageInstaller(object sender, RoutedEventArgs e) => await AddAsync(() => new(OperationKind.StageInstaller, InstallerBox.Text, InstallerArgumentsBox.Text));
    private async void AddRegistry(object sender, RoutedEventArgs e) => await AddAsync(() => new(OperationKind.SetRegistry, RegistryKeyBox.Text, Registry: new RegistryChange(HiveBox.SelectedIndex == 0 ? HiveKind.Software : HiveKind.System, RegistryKeyBox.Text, RegistryNameBox.Text, (ValueKind)RegistryTypeBox.SelectedItem, RegistryValueBox.Text)));
    private async void AddUnattend(object sender, RoutedEventArgs e) => await AddAsync(() => new(OperationKind.WriteUnattend, "Autounattend.xml", Unattend: new UnattendOptions(ComputerBox.Text, LanguageBox.Text, InputLocaleBox.Text, TimeZoneBox.Text, ((ComboBoxItem)ArchitectureBox.SelectedItem).Content.ToString()!)));
    private void RemovePlan(object sender, RoutedEventArgs e) { if (PlanGrid.SelectedItem is PlannedOperation operation) plan.Remove(operation); AcknowledgeBox.IsChecked = false; }
    private async void SavePlan(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "JSON plan|*.json", FileName = "image-plan.json" }; if (dialog.ShowDialog(this) != true) return;
        await RunAsync("حفظ الخطة", token => File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(plan, ProjectStore.JsonOptions), token));
    }
    private async void LoadPlan(object sender, RoutedEventArgs e)
    {
        string? file = PickFile("JSON plan|*.json"); if (file is null) return;
        await RunAsync("تحميل الخطة ومراجعتها", async token =>
        {
            if (new FileInfo(file).Length > 8 * 1024 * 1024) throw new InvalidDataException("Plan file exceeds 8 MiB.");
            var data = JsonSerializer.Deserialize<PlannedOperation[]>(await File.ReadAllTextAsync(file, token), ProjectStore.JsonOptions) ?? throw new InvalidDataException("خطة فارغة.");
            if (data.Length > 1000) throw new InvalidDataException("الخطة أكبر من الحد المسموح.");
            foreach (var operation in data) Validation.Operation(operation);
            plan.Clear(); foreach (var operation in data) plan.Add(operation); AcknowledgeBox.IsChecked = false;
        });
    }
    private Task ApplyPlanAsync() => RunAsync("تطبيق الخطة", async token =>
    {
        if (AcknowledgeBox.IsChecked != true) throw new InvalidOperationException("راجع الخطة وحدد مربع الإقرار قبل تطبيقها.");
        var snapshot = plan.ToArray(); if (snapshot.Length == 0) throw new InvalidOperationException("الخطة فارغة.");
        var completed = new SynchronousProgress(operation => Dispatcher.Invoke(() => plan.Remove(operation)));
        await engine.ApplyAsync(Session(), snapshot, token, completed); plan.Clear(); AcknowledgeBox.IsChecked = false;
        Session().WorkflowStage = 3; ResetStrong();
        ReviewResultBox.Text = "✓ اكتمل تطبيق الخطة. الخطوة التالية: ④ ب · حفظ الصورة وإلغاء التركيب.";
        if (engine.OperationNotices.Count > 0)
        {
            StrongStatus.Text = "نتيجة التطبيق — راجع العناصر التي بقيت:" + Environment.NewLine + string.Join(Environment.NewLine, engine.OperationNotices);
            foreach (string notice in engine.OperationNotices) Log(notice);
            ReviewResultBox.Text += Environment.NewLine + StrongStatus.Text;

        }
    });
    private Task CommitImageAsync() => RunAsync("حفظ الصورة وإلغاء التركيب", async token =>
    {
        if (!WorkflowRules.CanCommit(Session(), plan.Count)) throw new InvalidOperationException("طبّق الخطة أولًا، أو اختر المتابعة دون تعديلات بعد مراجعتها.");
        await engine.UnmountAsync(Session(), true, token); Session().WorkflowStage = 4; SetDefaultOutput(); EditorTabs.SelectedItem = OutputTab;
    });
    private async void DiscardImage(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "تجاهل كل التعديلات غير المحفوظة في جلسة الصورة الحالية؟", "Discard", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await RunAsync("تجاهل التعديلات", async token => { await engine.UnmountAsync(Session(), false, token); ResetAfterDiscard(); });
    }
    private async void RecoverImage(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "ستُستعاد جلسة هذا المشروع فقط وتُتجاهل تعديلات التركيب غير المحفوظة. المتابعة؟", "Recovery", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await RunAsync("استعادة جلسة المشروع", async token => { await engine.RecoverDiscardAsync(Session(), token); ResetAfterDiscard(); });
    }
    private void BrowseOutput(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "ISO image|*.iso", FileName = "Windows-Custom.iso", OverwritePrompt = true };
        if (dialog.ShowDialog(this) == true) OutputFileBox.Text = dialog.FileName;
    }
    private Task BuildIsoAsync() => RunAsync("إنشاء ISO قابل للإقلاع", async token =>
    {
        ReplaceEngine();
        await engine.BuildIsoAsync(Session(), OutputFileBox.Text, token, HighCompressionBox.IsChecked == true);
        Session().WorkflowStage = 5; Session().OutputIso = Path.GetFullPath(OutputFileBox.Text);
        var summary = JsonSerializer.Deserialize<IsoBuildSummary>(await File.ReadAllTextAsync(Path.Combine(Session().Root, "iso-build-summary.json"), token), ProjectStore.JsonOptions);
        if (summary is not null) BuildSizeBox.Text = $"الضغط: {summary.Compression} | ISO الجديد: {summary.OutputIsoBytes / 1073741824.0:F2} GiB" + (summary.SourceIsoBytes is long sourceBytes ? $" | الأصلي: {sourceBytes / 1073741824.0:F2} GiB" : "") + "\nالحجم لا يثبت اكتمال إزالة البرامج؛ استخدم تقرير الفحص.";
        EditorTabs.SelectedItem = DoneTab;
    });
    private async void InstallAdk(object sender, RoutedEventArgs e) => await RunAsync("تنزيل ADK الرسمي والتحقق من توقيعه وتثبيت Deployment Tools…", async token =>
    {
        var installer = new AdkInstaller(new ProcessRunner(Log), ToolSettings.WindowsDefaults().PowerShell, Path.Combine(AppContext.BaseDirectory, "Scripts", "VerifyAdk.ps1"));
        var result = await installer.EnsureInstalledAsync(token);
        OscdimgBox.Text = result.Path;
        AdkStatus.Text = result.RebootRequired ? "✓ اكتمل إعداد الأداة. طلب المثبت إعادة تشغيل الجهاز؛ احفظ المشروع وأعد التشغيل قبل البناء إذا لزم." : "✓ أداة ISO جاهزة ومحددة تلقائيًا. يمكنك الآن الضغط على التالي.";
    });
    private async void AuditImage(object sender, RoutedEventArgs e) => await RunAsync("فحص الصورة المحفوظة وجرد البرامج دون حفظ أي تغيير…", async token =>
    {
        string report = await engine.AuditCommittedAsync(Session(), token);
        AuditStatus.Text = "✓ اكتمل جرد صورة المشروع. اضغط فتح تقرير البرامج. هذا فحص غير متصل، وليس اختبار إقلاع أو فحصًا أمنيًا.";
        Log("تقرير البرامج: " + report);
    });
    private async void ViewAudit(object sender, RoutedEventArgs e) => await RunAsync("فتح تقرير البرامج", token =>
    {
        string report = Validation.UnderRoot(Session().Root, Path.Combine(Session().Root, "image-audit.html"));
        Validation.ExistingFile(report, ".html");
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(report) { UseShellExecute = true });
        return Task.CompletedTask;
    });
    private void RebuildIso(object sender, RoutedEventArgs e)
    {
        if (busy || project?.State != ImageState.Committed || project.MediaPath is null) return;
        string folder = Path.GetDirectoryName(project.OutputIso ?? Path.Combine(project.Root, "Windows-Custom.iso"))!;
        string name = Path.Combine(folder, "Windows-Custom-ESD-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".iso");
        if (File.Exists(name)) name = Path.Combine(folder, "Windows-Custom-ESD-" + Guid.NewGuid().ToString("N") + ".iso");
        OutputFileBox.Text = name; HighCompressionBox.IsChecked = true; project.WorkflowStage = 4;
        EditorTabs.SelectedItem = OutputTab; RefreshJourney();
    }
    private void ReplaceEngine() { var replacement = NewEngine(); engine.Dispose(); engine = replacement; }
    private void SetDefaultOutput()
    {
        if (project is not null && string.IsNullOrWhiteSpace(OutputFileBox.Text)) OutputFileBox.Text = Path.Combine(project.Root, "Windows-Custom.iso");
    }
    private void ResetStrong()
    {
        strongReport = null; strongProjectId = null; StrongPreview.ItemsSource = null;
        ReviewResultBox.Text = ""; BuildSizeBox.Clear(); AuditStatus.Text = "";
        StrongStatus.Text = "ركّب صورة Pro ثم حلّلها. المتجر والطباعة محفوظان في التخفيف التلقائي.";
        FeaturesGrid.ItemsSource = null; PackagesGrid.ItemsSource = null; DriversGrid.ItemsSource = null; AppsGrid.ItemsSource = null; ServicesGrid.ItemsSource = null;
    }
    private Task AnalyzeStrongAsync() => RunAsync("تحليل التخفيف القوي لصورة Pro", async token =>
    {
        ShowCustomization(); await AnalyzeStrongCoreAsync(token);
    });
    private async Task AnalyzeStrongCoreAsync(CancellationToken token)
    {
        strongReport = null; strongProjectId = null; StrongPreview.ItemsSource = null;
        var options = new AggressiveOptions(StrongDefenderBox.IsChecked == true, StrongLanguagesBox.IsChecked == true);
        var report = await engine.AnalyzeAggressiveAsync(Session(), options, token);
        strongReport = report; strongProjectId = Session().Id;
        StrongPreview.ItemsSource = report.Operations;
        StrongStatus.Text = "الإصدار: " + report.Profile.EditionId + " | Build " + report.Profile.Build + Environment.NewLine +
            "عدد الخطوات المقترحة: " + report.Operations.Count + Environment.NewLine +
            string.Join(Environment.NewLine, report.Notes.Select(n => "• " + n));
    }
    private Task QueueStrongAsync() => RunAsync("إضافة التخفيف للخطة القابلة للمراجعة", _ =>
    {
        if (strongReport is null || strongProjectId != Session().Id) throw new InvalidOperationException("حلّل صورة المشروع الحالي أولًا.");
        if (strongReport.Options != new AggressiveOptions(StrongDefenderBox.IsChecked == true, StrongLanguagesBox.IsChecked == true)) throw new InvalidOperationException("تغيّرت خيارات التخفيف؛ أعد التحليل أولًا.");
        foreach (var operation in plan.Where(o => o.PresetId == AggressivePreset.Id).ToArray()) plan.Remove(operation);
        foreach (var operation in strongReport.Operations)
        {
            Validation.Operation(operation);
            if (!plan.Any(o => o.Kind == operation.Kind && o.Target == operation.Target && o.Arguments == operation.Arguments && o.Registry == operation.Registry)) plan.Add(operation);
        }
        AcknowledgeBox.IsChecked = false; EditorTabs.SelectedItem = ReviewTab;
        return Task.CompletedTask;
    });
    private sealed class SynchronousProgress(Action<PlannedOperation> report) : IProgress<PlannedOperation>
    {
        public void Report(PlannedOperation value) => report(value);
    }
    private Task ApproveNoChangesAsync() => RunAsync("مراجعة صورة دون تعديلات", _ =>
    {
        if (!WorkflowRules.CanApproveNoChanges(Session(), plan.Count, StrongMode.IsChecked == true, strongReport, strongProjectId,
            new AggressiveOptions(StrongDefenderBox.IsChecked == true, StrongLanguagesBox.IsChecked == true)))
            throw new InvalidOperationException("هذا الخيار لخطة فارغة وصورة مركبة. في المسار القوي، حلّل الصورة أولًا؛ إذا وجدت تعديلات مقترحة فأضفها وطبّقها.");
        Session().WorkflowStage = 3;
        ReviewResultBox.Text = "✓ تمت المراجعة. اضغط ④ ب · حفظ الصورة وإلغاء التركيب. لا يلزم تكرار التحليل بعد نجاح تطبيق الخطة.";
        return Task.CompletedTask;
    });
    private void SelectPro()
    {
        if (ImagesBox.ItemsSource is IEnumerable<ImageInfo> images)
            ImagesBox.SelectedItem = images.FirstOrDefault(i => i.EditionId.Equals("Professional", StringComparison.OrdinalIgnoreCase)) ?? images.FirstOrDefault();
    }
    private void ResetAfterDiscard()
    {
        plan.Clear(); ResetStrong(); Session().WorkflowStage = 1; Session().OutputIso = null; EditorTabs.SelectedItem = PrepareTab;
    }
    private Task PrepareImageAsync() => RunAsync("نسخ الإصدار وتركيب الصورة", async token =>
    {
        var session = Session();
        if (session.State == ImageState.RecoveryRequired) throw new InvalidOperationException("استعد الجلسة المتعثرة أولًا من زر الاستعادة.");
        var image = ImagesBox.SelectedItem as ImageInfo ?? throw new InvalidOperationException("اختر إصدار Windows.");
        if (StrongMode.IsChecked == true && !image.EditionId.Equals("Professional", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("مسار التخفيف القوي مخصص لإصدار Windows Pro القياسي فقط.");
        if (WorkflowRules.CanCommit(session, plan.Count)) { EditorTabs.SelectedItem = ReviewTab; return; }
        if (!File.Exists(session.WorkingWim)) await engine.ImportAsync(session, image, token);
        if (session.State != ImageState.Mounted) await engine.MountAsync(session, token);
        session.WorkflowStage = 2; session.OutputIso = null;
        ShowCustomization();
        if (StrongMode.IsChecked == true) await AnalyzeStrongCoreAsync(token);
        else
        {
            var inventory = await engine.InventoryAsync(session, token);
            FeaturesGrid.ItemsSource = inventory.Features; PackagesGrid.ItemsSource = inventory.Packages; DriversGrid.ItemsSource = inventory.Drivers; AppsGrid.ItemsSource = inventory.Apps; ServicesGrid.ItemsSource = inventory.Services;
        }
    });
    private void ModeChanged(object sender, RoutedEventArgs e)
    {
        if (!initialized) return;
        if (project is not null) project.WorkflowMode = StrongMode.IsChecked == true ? "Strong" : "Manual";
        RefreshJourney();
        if (CurrentPage == WizardPage.Customize) ShowCustomization();
    }
    private void ClearPlan(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        plan.Clear(); AcknowledgeBox.IsChecked = false; ResetStrong(); RefreshJourney();
    }
    private WizardPage CurrentPage => EditorTabs.SelectedItem == SourceTab ? WizardPage.Source : EditorTabs.SelectedItem == PrepareTab ? WizardPage.Prepare :
        EditorTabs.SelectedItem == CustomizeTab ? WizardPage.Customize : EditorTabs.SelectedItem == ReviewTab ? WizardPage.Review : EditorTabs.SelectedItem == OutputTab ? WizardPage.Output : WizardPage.Done;
    private bool AnalysisReady => strongReport is not null && strongProjectId == project?.Id && strongReport.Options == CurrentOptions;
    private AggressiveOptions CurrentOptions => new(StrongDefenderBox.IsChecked == true, StrongLanguagesBox.IsChecked == true);
    private WizardDecision Decision => WizardFlow.Next(CurrentPage, project, plan.Count, StrongMode.IsChecked == true, AnalysisReady,
        WorkflowRules.CanApproveNoChanges(project, plan.Count, StrongMode.IsChecked == true, strongReport, strongProjectId, CurrentOptions),
        AcknowledgeBox.IsChecked == true, File.Exists(SourceBox.Text) && !string.IsNullOrWhiteSpace(WorkspaceBox.Text),
        File.Exists(OscdimgBox.Text) && !string.IsNullOrWhiteSpace(OutputFileBox.Text));
    private void ShowCustomization()
    {
        EditorTabs.SelectedItem = CustomizeTab;
        CustomTabs.SelectedItem = StrongMode.IsChecked == true ? StrongTab : ComponentsTab;
    }
    private void ResumePage()
    {
        SetDefaultOutput();
        if (project?.State == ImageState.RecoveryRequired) EditorTabs.SelectedItem = PrepareTab;
        else if (project?.WorkflowStage == 5 && File.Exists(project.OutputIso)) EditorTabs.SelectedItem = DoneTab;
        else if (project?.State == ImageState.Committed) EditorTabs.SelectedItem = OutputTab;
        else if (project?.State == ImageState.Mounted && project.WorkflowStage >= 3) EditorTabs.SelectedItem = ReviewTab;
        else if (project?.State == ImageState.Mounted) ShowCustomization();
        else EditorTabs.SelectedItem = PrepareTab;
    }
    private async void NextStep(object sender, RoutedEventArgs e)
    {
        if (busy || !Decision.Enabled) return;
        switch (Decision.Action)
        {
            case WizardAction.Create: await CreateProjectAsync(); break;
            case WizardAction.Resume: ResumePage(); break;
            case WizardAction.Prepare: await PrepareImageAsync(); break;
            case WizardAction.Analyze: await AnalyzeStrongAsync(); break;
            case WizardAction.Review:
                if (StrongMode.IsChecked == true) await QueueStrongAsync();
                else EditorTabs.SelectedItem = ReviewTab;
                break;
            case WizardAction.Apply: await ApplyPlanAsync(); break;
            case WizardAction.Approve: await ApproveNoChangesAsync(); break;
            case WizardAction.Commit: await CommitImageAsync(); break;
            case WizardAction.BuildIso: await BuildIsoAsync(); break;
            case WizardAction.Finish: EditorTabs.SelectedItem = DoneTab; break;
            case WizardAction.OpenFolder:
                await RunAsync("فتح مجلد النتيجة", token =>
                {
                    string file = Session().OutputIso ?? Session().WorkingWim;
                    var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = true };
                    start.ArgumentList.Add(Path.GetDirectoryName(file)!); _ = System.Diagnostics.Process.Start(start);
                    return Task.CompletedTask;
                }); break;
        }
        RefreshJourney();
    }
    private void PreviousStep(object sender, RoutedEventArgs e)
    {
        if (busy || (project?.WorkflowStage ?? 0) >= 3) return;
        if (CurrentPage == WizardPage.Review) ShowCustomization();
        else if (CurrentPage == WizardPage.Customize) EditorTabs.SelectedItem = PrepareTab;
        else if (CurrentPage == WizardPage.Prepare) EditorTabs.SelectedItem = SourceTab;
        RefreshJourney();
    }
    private void NewProject(object sender, RoutedEventArgs e)
    {
        if (busy || project?.State is ImageState.Mounted or ImageState.RecoveryRequired) return;
        project = null; plan.Clear(); ResetStrong(); ImagesBox.ItemsSource = null; SourceBox.Text = ""; OutputFileBox.Text = "";
        EditorTabs.SelectedItem = SourceTab; RefreshJourney();
    }
    private void WizardInputChanged(object sender, RoutedEventArgs e) { if (initialized) RefreshJourney(); }
    private void StrongOptionsChanged(object sender, RoutedEventArgs e)
    {
        if (!initialized) return;
        if (!AnalysisReady) { StrongPreview.ItemsSource = null; StrongStatus.Text = "تغيرت الخيارات. اضغط التالي لإعادة التحليل قبل مراجعة الخطة."; }
        RefreshJourney();
    }
    private void RefreshJourney()
    {
        if (!initialized) return;
        bool strong = StrongMode.IsChecked == true;
        ComponentsTab.Visibility = IntegrationTab.Visibility = RegistryTab.Visibility = strong ? Visibility.Collapsed : Visibility.Visible;
        StrongTab.Visibility = strong ? Visibility.Visible : Visibility.Collapsed;
        StrongMode.IsEnabled = ManualMode.IsEnabled = !busy && plan.Count == 0 && (project?.WorkflowStage ?? 0) < 3;
        AuditButton.IsEnabled = !busy && project?.State == ImageState.Committed;
        ViewAuditButton.IsEnabled = !busy && project is not null && File.Exists(Path.Combine(project.Root, "image-audit.html"));
        RebuildButton.IsEnabled = !busy && project?.State == ImageState.Committed && project.MediaPath is not null;
        AdkButton.IsEnabled = !busy;
        OpenProjectButton.IsEnabled = !busy && project?.State is not (ImageState.Mounted or ImageState.RecoveryRequired);
        bool recovery = project?.State == ImageState.RecoveryRequired;
        if (recovery && EditorTabs.SelectedItem != PrepareTab) { EditorTabs.SelectedItem = PrepareTab; return; }
        var decision = Decision;
        NextButton.Content = busy ? "جارٍ التنفيذ…" : decision.Caption;
        NextButton.IsEnabled = !busy && decision.Enabled && (decision.Action != WizardAction.Prepare || ImagesBox.SelectedItem is not null);
        BackButton.IsEnabled = !busy && CurrentPage is not (WizardPage.Source or WizardPage.Done) && (project?.WorkflowStage ?? 0) < 3 && !recovery;
        GuideText.Text = busy ? "تُنفّذ العملية الآن. انتظر اكتمالها قبل الانتقال." : decision.Hint;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        RecoveryPanel.Visibility = recovery ? Visibility.Visible : Visibility.Collapsed;
        ImagesBox.IsEnabled = !busy && project?.Image is null;
        SourceBox.IsReadOnly = WorkspaceBox.IsReadOnly = project is not null;
        SourceBrowse.IsEnabled = WorkspaceBrowse.IsEnabled = project is null && !busy;
        AcknowledgeBox.Visibility = project?.WorkflowStage >= 3 ? Visibility.Collapsed : Visibility.Visible;
        PlanGrid.Visibility = project?.WorkflowStage >= 3 ? Visibility.Collapsed : Visibility.Visible;
        ReviewResultBox.Visibility = project?.WorkflowStage >= 3 ? Visibility.Visible : Visibility.Collapsed;
        IsoSettings.Visibility = project?.MediaPath is null ? Visibility.Collapsed : Visibility.Visible;
        OutputHint.Text = project?.MediaPath is null ? "✓ حُفظت صورة WIM. المصدر لا يحتوي وسائط ISO كاملة، لذلك لن يُنشأ ISO. اضغط إنهاء لعرض مكان الصورة." : "✓ حُفظت الصورة. اختر مكان ISO وحدد أداة oscdimg هنا، ثم اضغط التالي.";
        OutputPathBox.Text = project?.OutputIso ?? project?.WorkingWim ?? "";
        TextBlock[] badges = [Step1, Step2, Step3, Step4, Step5];
        string[] labels = ["① المصدر", "② الإصدار", "③ التعديلات", "④ التطبيق والحفظ", "⑤ الإخراج"];
        int stage = project?.WorkflowStage ?? 0;
        for (int i = 0; i < badges.Length; i++)
        {
            bool done = stage >= i + 1 && (!recovery || i == 0) && (i != 4 || File.Exists(project?.OutputIso));
            if (i == 4 && CurrentPage == WizardPage.Done && project?.State == ImageState.Committed && project.MediaPath is null && File.Exists(project.WorkingWim)) done = true;
            badges[i].Text = (done ? "✓ " : "") + labels[i];
            badges[i].Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(done ? "#65D6A6" : "#AAB7C8"));
        }
        (PageTitle.Text, PageDescription.Text) = CurrentPage switch
        {
            WizardPage.Source => ("① اختر المصدر والمسار", "كل مرحلة لها صفحة واحدة. زر التالي أسفل النافذة ينفّذ الإجراء وينقلك للمرحلة التالية."),
            WizardPage.Prepare => ("② اختر إصدار Windows", "ستُجرى عمليات النسخ والتركيب على نسخة داخل المشروع، مع بقاء المصدر محفوظًا."),
            WizardPage.Customize => ("③ راجع التعديلات", strong ? "هذه هي التعديلات المقترحة. بعد مراجعتها اضغط التالي." : "اختر تعديلاتك من الأقسام أدناه، ثم اضغط التالي لمراجعة الخطة."),
            WizardPage.Review => (stage >= 3 ? "④ نجح التطبيق — احفظ الصورة" : "④ وافق على الخطة وطبّقها", stage >= 3 ? "راجع تقرير العناصر المتبقية أدناه. زر التالي يحفظ الصورة فعلًا ويلغي تركيبها." : "لن تُطبّق التعديلات إلا بعد تحديد الموافقة والضغط على التالي."),
            WizardPage.Output => ("⑤ احفظ النتيجة النهائية", "إعدادات الإخراج كلها في هذه الصفحة."),
            _ => ("✓ النتيجة جاهزة", "يمكنك الآن فتح مجلد النتيجة واختبار النسخة.")
        };
    }
    private void CancelClick(object sender, RoutedEventArgs e) { cancellation?.Cancel(); CancelButton.IsEnabled = false; StatusLabel.Text = "طُلب التوقف؛ انتظار اكتمال خطوة النظام الحالية."; }
    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (busy) { e.Cancel = true; StatusLabel.Text = "انتظر اكتمال الخطوة الحالية قبل إغلاق التطبيق."; return; }
        if (project?.State is ImageState.Mounted or ImageState.RecoveryRequired)
        {
            if (MessageBox.Show(this, "الصورة ما زالت مركبة أو تحتاج استعادة. يُنصح بالحفظ أو التجاهل أولًا. إغلاق التطبيق مع إبقاء الجلسة؟", "WinImageStudio", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) { e.Cancel = true; return; }
        }
        engine.Dispose();
    }
}
