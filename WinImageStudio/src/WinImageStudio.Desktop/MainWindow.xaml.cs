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
        initialized = true;
        plan.CollectionChanged += (_, _) => { if (project is not null) { project.WorkflowStage = Math.Min(project.WorkflowStage, 2); project.OutputIso = null; } RefreshJourney(); };
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
    private async void ApplyTools(object sender, RoutedEventArgs e) => await RunAsync("اعتماد الأدوات", _ => { var replacement = NewEngine(); engine.Dispose(); engine = replacement; return Task.CompletedTask; });
    private async void CreateProject(object sender, RoutedEventArgs e) => await RunAsync("إنشاء مشروع واستيراد المصدر", async token =>
    {
        if (project?.State is ImageState.Mounted or ImageState.RecoveryRequired) throw new InvalidOperationException("ألغِ تركيب المشروع الحالي أو استعده قبل فتح مشروع آخر.");
        project = await engine.CreateAsync(SourceBox.Text, WorkspaceBox.Text, token);
        plan.Clear(); ResetStrong(); project.WorkflowMode = StrongMode.IsChecked == true ? "Strong" : "Manual";
        ImagesBox.ItemsSource = await engine.InspectAsync(project, token); SelectPro(); project.WorkflowStage = 1;
        EditorTabs.SelectedItem = PrepareTab;
    });
    private async void OpenProject(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog(); if (dialog.ShowDialog(this) != true) return;
        await RunAsync("فتح المشروع", async token =>
        {
            if (project?.State is ImageState.Mounted or ImageState.RecoveryRequired) throw new InvalidOperationException("ألغِ تركيب المشروع الحالي أو استعده أولًا.");
            var loaded = await store.LoadAsync(dialog.FolderName, token);
            plan.Clear(); project = loaded; ResetStrong();
            StrongMode.IsChecked = project.WorkflowMode != "Manual"; ManualMode.IsChecked = project.WorkflowMode == "Manual";
            ImagesBox.ItemsSource = project.Image is null ? await engine.InspectAsync(project, token) : new[] { project.Image };
            SelectPro();
            if (project.WorkflowStage == 0) project.WorkflowStage = project.State == ImageState.Committed ? 4 : project.State == ImageState.Mounted ? 2 : 1;
        });
    }
    private async void ImportImage(object sender, RoutedEventArgs e) => await RunAsync("نسخ الإصدار المختار", async token =>
    {
        var image = ImagesBox.SelectedItem as ImageInfo ?? throw new InvalidOperationException("اختر إصدارًا من القائمة.");
        await engine.ImportAsync(Session(), image, token);
    });
    private async void MountImage(object sender, RoutedEventArgs e) => await RunAsync("تركيب صورة المشروع", token => engine.MountAsync(Session(), token));
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
    private async void ApplyPlan(object sender, RoutedEventArgs e) => await RunAsync("تطبيق الخطة", async token =>
    {
        if (AcknowledgeBox.IsChecked != true) throw new InvalidOperationException("راجع الخطة وحدد مربع الإقرار قبل تطبيقها.");
        var snapshot = plan.ToArray(); if (snapshot.Length == 0) throw new InvalidOperationException("الخطة فارغة.");
        var completed = new SynchronousProgress(operation => Dispatcher.Invoke(() => plan.Remove(operation)));
        await engine.ApplyAsync(Session(), snapshot, token, completed); plan.Clear(); AcknowledgeBox.IsChecked = false;
        Session().WorkflowStage = 3; ResetStrong();
        if (engine.OperationNotices.Count > 0)
        {
            StrongStatus.Text = "نتيجة التطبيق — راجع العناصر التي بقيت:" + Environment.NewLine + string.Join(Environment.NewLine, engine.OperationNotices);
            foreach (string notice in engine.OperationNotices) Log(notice);
            MessageBox.Show(this, StrongStatus.Text, "تقرير التخفيف", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    });
    private async void CommitImage(object sender, RoutedEventArgs e) => await RunAsync("حفظ الصورة وإلغاء التركيب", async token =>
    {
        if (plan.Count != 0 || Session().WorkflowStage < 3) throw new InvalidOperationException("طبّق الخطة أولًا، أو اختر المتابعة دون تعديلات بعد مراجعتها.");
        await engine.UnmountAsync(Session(), true, token); Session().WorkflowStage = 4; EditorTabs.SelectedItem = OutputTab;
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
    private async void BuildIso(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "ISO image|*.iso", FileName = "Windows-Custom.iso", OverwritePrompt = true }; if (dialog.ShowDialog(this) != true) return;
        await RunAsync("بناء ISO قابل للإقلاع", async token => { await engine.BuildIsoAsync(Session(), dialog.FileName, token); Session().WorkflowStage = 5; Session().OutputIso = dialog.FileName; });
    }
    private void ResetStrong()
    {
        strongReport = null; strongProjectId = null; StrongPreview.ItemsSource = null;
        StrongStatus.Text = "ركّب صورة Pro ثم حلّلها. المتجر والطباعة محفوظان في التخفيف التلقائي.";
        FeaturesGrid.ItemsSource = null; PackagesGrid.ItemsSource = null; DriversGrid.ItemsSource = null; AppsGrid.ItemsSource = null; ServicesGrid.ItemsSource = null;
    }
    private async void AnalyzeStrong(object sender, RoutedEventArgs e) => await RunAsync("تحليل التخفيف القوي لصورة Pro", async token =>
    {
        strongReport = null; strongProjectId = null; StrongPreview.ItemsSource = null;
        var options = new AggressiveOptions(StrongDefenderBox.IsChecked == true, StrongLanguagesBox.IsChecked == true);
        var report = await engine.AnalyzeAggressiveAsync(Session(), options, token);
        strongReport = report; strongProjectId = Session().Id;
        StrongPreview.ItemsSource = report.Operations;
        StrongStatus.Text = "الإصدار: " + report.Profile.EditionId + " | Build " + report.Profile.Build + Environment.NewLine +
            "عدد الخطوات المقترحة: " + report.Operations.Count + Environment.NewLine +
            string.Join(Environment.NewLine, report.Notes.Select(n => "• " + n));
    });
    private async void QueueStrong(object sender, RoutedEventArgs e) => await RunAsync("إضافة التخفيف للخطة القابلة للمراجعة", _ =>
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
    private async void ApproveNoChanges(object sender, RoutedEventArgs e) => await RunAsync("مراجعة صورة دون تعديلات", _ =>
    {
        if (Session().State != ImageState.Mounted || plan.Count != 0) throw new InvalidOperationException("هذا الخيار لصورة مركبة وخطة فارغة فقط.");
        if (StrongMode.IsChecked == true && (strongReport is null || strongProjectId != Session().Id || strongReport.Operations.Count != 0))
            throw new InvalidOperationException("حلّل التخفيف أولًا؛ إذا وُجدت تعديلات مقترحة، أضفها وطبّقها.");
        Session().WorkflowStage = 3; return Task.CompletedTask;
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
    private async void PrepareImage(object sender, RoutedEventArgs e) => await RunAsync("نسخ الإصدار وتركيب الصورة", async token =>
    {
        var session = Session();
        if (session.State == ImageState.RecoveryRequired) throw new InvalidOperationException("استعد الجلسة المتعثرة أولًا من زر الاستعادة.");
        var image = ImagesBox.SelectedItem as ImageInfo ?? throw new InvalidOperationException("اختر إصدار Windows.");
        if (StrongMode.IsChecked == true && !image.EditionId.Equals("Professional", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("مسار التخفيف القوي مخصص لإصدار Windows Pro القياسي فقط.");
        if (!File.Exists(session.WorkingWim)) await engine.ImportAsync(session, image, token);
        if (session.State != ImageState.Mounted) await engine.MountAsync(session, token);
        session.WorkflowStage = 2; session.OutputIso = null;
        EditorTabs.SelectedItem = StrongMode.IsChecked == true ? StrongTab : ComponentsTab;
    });
    private void ModeChanged(object sender, RoutedEventArgs e)
    {
        if (!initialized) return;
        if (project is not null) project.WorkflowMode = StrongMode.IsChecked == true ? "Strong" : "Manual";
        RefreshJourney();
        if (EditorTabs.SelectedItem is TabItem selected && selected.Visibility != Visibility.Visible)
            EditorTabs.SelectedItem = StrongMode.IsChecked == true ? StrongTab : ComponentsTab;
    }
    private void ClearPlan(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        plan.Clear(); AcknowledgeBox.IsChecked = false; ResetStrong(); RefreshJourney();
    }
    private void GoStep(object sender, RoutedEventArgs e)
    {
        if (busy || sender is not Button button || !int.TryParse(button.Tag?.ToString(), out int step)) return;
        EditorTabs.SelectedItem = step switch { 1 => SourceTab, 2 => PrepareTab, 3 => StrongMode.IsChecked == true ? StrongTab : ComponentsTab, 4 => ReviewTab, _ => OutputTab };
    }
    private void NextStep(object sender, RoutedEventArgs e)
    {
        int stage = project?.WorkflowStage ?? 0;
        if (stage == 0) EditorTabs.SelectedItem = SourceTab;
        else if (stage == 1) EditorTabs.SelectedItem = PrepareTab;
        else if (stage == 2) EditorTabs.SelectedItem = plan.Count > 0 ? ReviewTab : StrongMode.IsChecked == true ? StrongTab : ComponentsTab;
        else if (stage == 3) EditorTabs.SelectedItem = ReviewTab;
        else EditorTabs.SelectedItem = OutputTab;
    }
    private void RefreshJourney()
    {
        if (!initialized) return;
        bool strong = StrongMode.IsChecked == true;
        ComponentsTab.Visibility = IntegrationTab.Visibility = RegistryTab.Visibility = strong ? Visibility.Collapsed : Visibility.Visible;
        StrongTab.Visibility = strong ? Visibility.Visible : Visibility.Collapsed;
        StrongMode.IsEnabled = ManualMode.IsEnabled = !busy && plan.Count == 0;
        NextButton.IsEnabled = !busy;
        int stage = project?.WorkflowStage ?? 0;
        bool recovery = project?.State == ImageState.RecoveryRequired;
        Button[] buttons = [Step1, Step2, Step3, Step4, Step5];
        string[] titles = ["① المصدر", "② اختيار Pro وتركيبه", "③ التعديلات", "④ التطبيق والحفظ", "⑤ إنشاء ISO"];
        for (int i = 0; i < buttons.Length; i++)
        {
            // The modifications step completes only after every operation succeeds.
            bool done = stage >= i + 1 && (!recovery || i == 0) && (i != 4 || File.Exists(project?.OutputIso));
            buttons[i].Content = (done ? "✓ " : "") + titles[i];
            buttons[i].IsEnabled = !busy && (i switch
            {
                0 => true,
                1 => project is not null,
                2 or 3 => project?.State == ImageState.Mounted,
                _ => project?.State == ImageState.Committed
            });
            buttons[i].Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(done ? "#145C48" : "#26364A"));
        }
        ImagesBox.IsEnabled = !busy && project?.Image is null;
        BuildIsoButton.IsEnabled = !busy && project?.State == ImageState.Committed && plan.Count == 0;
        OutputPathBox.Text = project?.OutputIso ?? "لم يُنشأ ملف ISO بعد.";
        GuideText.Text = recovery ? "توقفت العملية: افتح الخطوة ② لاستعادة جلسة المشروع وتجاهل التعديلات غير المحفوظة، ثم أعد تجهيز الصورة." : stage switch
        {
            0 => "① اختر ISO وحدد مجلد المشروع ثم اضغط إنشاء المشروع.",
            1 => "② اختر Windows Pro ثم اضغط تجهيز الإصدار وتركيبه.",
            2 => plan.Count > 0 ? "④ راجع الخطة وحدد الإقرار، ثم طبّق التعديلات." : strong ? "③ اضغط تحليل الصورة، راجع التقرير، ثم أضف التخفيف إلى الخطة." : "③ اقرأ المكونات وأضف اختياراتك إلى الخطة، ثم افتح الخطوة ④.",
            3 => "④ نجح التطبيق. راجع التقرير ثم اضغط حفظ الصورة وإلغاء التركيب.",
            4 => "⑤ حُفظت الصورة. يمكنك الآن إنشاء ISO.",
            _ => "✓ اكتملت الخطوات وحُفظ ISO في المسار المعروض في الخطوة ⑤."
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
