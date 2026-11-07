using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
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
    }
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
        busy = true; EditorTabs.IsEnabled = false; CancelButton.IsEnabled = true; Progress.IsIndeterminate = true;
        cancellation = new CancellationTokenSource(); StatusLabel.Text = title; Log(title);
        try { await action(cancellation.Token); StatusLabel.Text = "اكتملت العملية"; }
        catch (OperationCanceledException) { StatusLabel.Text = "توقفت المهمة بين الخطوات؛ راجع حالة المشروع قبل المتابعة."; Log(StatusLabel.Text); }
        catch (Exception ex)
        {
            StatusLabel.Text = "فشلت العملية؛ راجع السجل وحالة المشروع.";
            Log(ex.ToString()); MessageBox.Show(this, ex.Message, "WinImageStudio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            cancellation.Dispose(); cancellation = null; busy = false; EditorTabs.IsEnabled = true; CancelButton.IsEnabled = false; Progress.IsIndeterminate = false;
            UpdateSessionLabel();
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
        plan.Clear(); ImagesBox.ItemsSource = await engine.InspectAsync(project, token); ImagesBox.SelectedIndex = 0;
    });
    private async void OpenProject(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog(); if (dialog.ShowDialog(this) != true) return;
        await RunAsync("فتح المشروع", async token =>
        {
            if (project?.State is ImageState.Mounted or ImageState.RecoveryRequired) throw new InvalidOperationException("ألغِ تركيب المشروع الحالي أو استعده أولًا.");
            project = await store.LoadAsync(dialog.FolderName, token); plan.Clear();
            ImagesBox.ItemsSource = project.Image is null ? await engine.InspectAsync(project, token) : new[] { project.Image };
            ImagesBox.SelectedIndex = 0;
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
        var completed = new Progress<PlannedOperation>(operation => plan.Remove(operation));
        await engine.ApplyAsync(Session(), snapshot, token, completed); plan.Clear(); AcknowledgeBox.IsChecked = false;
    });
    private async void CommitImage(object sender, RoutedEventArgs e) => await RunAsync("حفظ الصورة وإلغاء التركيب", token => engine.UnmountAsync(Session(), true, token));
    private async void DiscardImage(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "تجاهل كل التعديلات غير المحفوظة في جلسة الصورة الحالية؟", "Discard", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await RunAsync("تجاهل التعديلات", token => engine.UnmountAsync(Session(), false, token));
    }
    private async void RecoverImage(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "ستُستعاد جلسة هذا المشروع فقط وتُتجاهل تعديلات التركيب غير المحفوظة. المتابعة؟", "Recovery", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await RunAsync("استعادة جلسة المشروع", token => engine.RecoverDiscardAsync(Session(), token));
    }
    private async void BuildIso(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "ISO image|*.iso", FileName = "Windows-Custom.iso", OverwritePrompt = true }; if (dialog.ShowDialog(this) != true) return;
        await RunAsync("بناء ISO قابل للإقلاع", token => engine.BuildIsoAsync(Session(), dialog.FileName, token));
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
