using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using WinImageStudio.Core;

namespace WinImageStudio.Lite;
public partial class LiteWindow : Window
{
    private readonly NativeLiteWorkflow workflow;
    private readonly string marker;
    private readonly string logPath;
    private ProjectSession? project;
    private ImageInfo? selected;
    private CancellationTokenSource? cancellation;
    private bool busy;
    private bool initialized;
    private readonly object logLock = new();
    public LiteWindow()
    {
        InitializeComponent();
        string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinImageStudioLite");
        WinImageStudio.Core.Validation.RejectReparseAncestors(local); Directory.CreateDirectory(local); marker = Path.Combine(local, "last-project.txt");
        logPath = Path.Combine(local, "session-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
        workflow = new NativeLiteWorkflow(Log);
        OutputFolderBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WinImageStudioLiteOutput");
        initialized = true; Refresh();
        Loaded += async (_, _) =>
        {
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length == 3 && args[1] == "--project") await LoadProjectAsync(args[2]);
            else if (File.Exists(marker) && new FileInfo(marker).Length < 16384)
            {
                try { WinImageStudio.Core.Validation.RejectReparse(marker); string folder = await File.ReadAllTextAsync(marker); if (Directory.Exists(folder)) await LoadProjectAsync(folder); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log(ex.Message); }
            }
        };
    }
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e); int dark = 1; _ = DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
    }
    [DllImport("dwmapi.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    private void Log(string text)
    {
        string line = DateTimeOffset.Now.ToString("HH:mm:ss") + " " + text;
        try { lock (logLock) File.AppendAllText(logPath, line + Environment.NewLine); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine(ex); }
        if (!Dispatcher.HasShutdownStarted) _ = Dispatcher.BeginInvoke(() => { if (LogBox.Text.Length > 160000) LogBox.Text = LogBox.Text[^80000..]; LogBox.AppendText(line + Environment.NewLine); LogBox.ScrollToEnd(); });
    }
    private void Page(FrameworkElement page)
    {
        foreach (var item in new FrameworkElement[] { SourcePage, EditionPage, ProcessingPage, DonePage, ErrorPage }) item.Visibility = item == page ? Visibility.Visible : Visibility.Collapsed;
        Refresh();
    }
    private void Refresh()
    {
        if (!initialized) return;
        BrowseButton.IsEnabled = OpenProjectButton.IsEnabled = !busy;
        EditionBox.IsEnabled = !busy && project is not null && !File.Exists(project.WorkingWim);
        NextButton.Content = busy ? "جارٍ التنفيذ…" : DonePage.Visibility == Visibility.Visible ? "فتح مجلد النتيجة" : EditionPage.Visibility == Visibility.Visible ? "التالي — ابدأ التخفيف التلقائي" : "التالي — اختيار الإصدار";
        NextButton.IsEnabled = !busy && (DonePage.Visibility == Visibility.Visible || (SourcePage.Visibility == Visibility.Visible && File.Exists(SourceBox.Text) && Path.GetExtension(SourceBox.Text).Equals(".iso", StringComparison.OrdinalIgnoreCase)) || (EditionPage.Visibility == Visibility.Visible && EditionBox.SelectedItem is ImageInfo));
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed; CancelButton.IsEnabled = busy;
        RetryButton.IsEnabled = !busy && project is not null && project.State != ImageState.RecoveryRequired;
        EditionBackButton.IsEnabled = !busy && project?.State is not (ImageState.Mounted or ImageState.RecoveryRequired);
        ErrorNewButton.IsEnabled = !busy && project?.State is not (ImageState.Mounted or ImageState.RecoveryRequired);
        RecoverButton.IsEnabled = !busy; RecoverButton.Visibility = project?.State == ImageState.RecoveryRequired ? Visibility.Visible : Visibility.Collapsed;
    }
    private async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        if (busy) return; busy = true; cancellation = new(); Progress.IsIndeterminate = true; Refresh();
        try { await operation(cancellation.Token); }
        catch (Exception ex)
        {
            Log(ex.ToString()); StatusText.Text = ex is OperationCanceledException ? "توقفت المهمة بين الخطوات." : "توقفت المهمة عند خطأ؛ راجع السبب أدناه.";
            ErrorText.Text = ex.Message; RecoveryHint.Text = project is null ? "اختر مصدرًا آخر أو راجع السجل." : "المشروع: " + project.Root + Environment.NewLine + "الحالة: " + project.State;
            Page(ErrorPage);
        }
        finally { cancellation.Dispose(); cancellation = null; busy = false; Progress.IsIndeterminate = false; Refresh(); }
    }
    private void BrowseSource(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Windows ISO|*.iso", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) SourceBox.Text = dialog.FileName;
    }
    private void BrowseOutput(object sender, RoutedEventArgs e) { var dialog = new OpenFolderDialog(); if (dialog.ShowDialog(this) == true) OutputFolderBox.Text = dialog.FolderName; }
    private async void Next(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (DonePage.Visibility == Visibility.Visible)
        {
            await RunAsync(_ => { var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = true }; info.ArgumentList.Add(Path.GetDirectoryName(project!.OutputIso!)!); using var process = Process.Start(info); return Task.CompletedTask; }); return;
        }
        if (SourcePage.Visibility == Visibility.Visible)
        {
            await RunAsync(async token =>
            {
                StatusText.Text = "قراءة ISO ونسخ ملفات المصدر إلى مشروع مستقل…";
                WinImageStudio.Core.Validation.ExistingFile(SourceBox.Text, ".iso");
                project = await workflow.CreateAsync(SourceBox.Text, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WinImageStudioProjects", "Lite"), token);
                project.WorkflowMode = "Lite"; await workflow.SaveAsync(project, token); await File.WriteAllTextAsync(marker, project.Root, token);
                EditionBox.ItemsSource = await workflow.InspectAsync(project, token); EditionBox.SelectedIndex = 0;
                if (EditionBox.SelectedItem is null) throw new InvalidDataException("لم يُكتشف إصدار Windows داخل ISO.");
                ProjectText.Text = project.Root; StatusText.Text = "اختر أي إصدار Client موجود، ثم اضغط التالي لبدء كل العمليات."; Page(EditionPage);
            }); return;
        }
        if (EditionBox.SelectedItem is ImageInfo image) { selected = image; await StartAsync(); }
    }
    private async Task StartAsync() => await RunAsync(async token =>
    {
        if (project is null || selected is null) throw new InvalidOperationException("اختر إصدارًا أولًا.");
        Directory.CreateDirectory(OutputFolderBox.Text);
        string output = Path.Combine(Path.GetFullPath(OutputFolderBox.Text), "Windows-Lite-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6] + ".iso");
        Page(ProcessingPage);
        var progress = new UiProgress(value => Dispatcher.Invoke(() =>
        {
            Progress.IsIndeterminate = false; Progress.Value = value.Completed; StatusText.Text = value.Action;
            string[] labels = ["تجهيز أدوات ISO", "نسخ الإصدار", "تركيب الصورة", "تحليل المكونات", "تطبيق التخفيف", "حفظ الصورة", "ضغط ESD وإنشاء ISO"];
            StepsText.Text = string.Join(Environment.NewLine + Environment.NewLine, labels.Select((label, index) => (index < value.Completed ? "✓ " : index == value.Completed ? "◉ " : "○ ") + label));
        }));
        var result = await new LiteAutomation(workflow).RunAsync(project, selected, output, progress, token);
        OutputBox.Text = result.IsoPath; ResultText.Text = string.Join(Environment.NewLine, result.Notices);
        SizeText.Text = "ISO: " + (new FileInfo(result.IsoPath).Length / 1073741824.0).ToString("F2") + " GiB | تقرير: " + result.ReportPath;
        FooterHint.Text = "حُفظ ISO باسم جديد. اختبره في جهاز افتراضي."; StatusText.Text = "✓ اكتمل إنشاء النسخة."; Page(DonePage);
    });
    private sealed class UiProgress(Action<LiteProgress> action) : IProgress<LiteProgress> { public void Report(LiteProgress value) => action(value); }
    private void Changed(object sender, RoutedEventArgs e) => Refresh();
    private void Cancel(object sender, RoutedEventArgs e) { cancellation?.Cancel(); CancelButton.IsEnabled = false; StatusText.Text = "طُلب التوقف بعد اكتمال الخطوة الحالية…"; }
    private async void Retry(object sender, RoutedEventArgs e) { if (project is not null && selected is not null) await StartAsync(); else Page(SourcePage); }
    private async void Recover(object sender, RoutedEventArgs e)
    {
        if (project is null || busy) return;
        if (MessageBox.Show(this, "استعادة هذه الجلسة ستتجاهل تعديلات التركيب غير المحفوظة ثم تعيد العملية. المتابعة؟", "WinImageStudio Lite", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await RunAsync(async token => { await workflow.RecoverAsync(project, token); project.WorkflowStage = 1; await workflow.SaveAsync(project, token); StatusText.Text = "استُعيدت الجلسة. اضغط التالي لإعادة العملية."; Page(EditionPage); });
    }
    private async void OpenProject(object sender, RoutedEventArgs e) { var dialog = new OpenFolderDialog(); if (dialog.ShowDialog(this) == true) await LoadProjectAsync(dialog.FolderName); }
    private Task LoadProjectAsync(string folder) => RunAsync(async token =>
    {
        var loaded = await workflow.LoadAsync(folder, token);
        if (loaded.WorkflowMode != "Lite") throw new InvalidDataException("هذا مشروع النسخة الكاملة؛ افتحه هناك، أو اختر ISO الأصلي لبدء مشروع Lite.");
        project = loaded; SourceBox.Text = loaded.Source; await File.WriteAllTextAsync(marker, loaded.Root, token);
        EditionBox.ItemsSource = loaded.Image is null ? await workflow.InspectAsync(loaded, token) : new[] { loaded.Image }; EditionBox.SelectedIndex = 0; selected = EditionBox.SelectedItem as ImageInfo;
        ProjectText.Text = loaded.Root;
        if (loaded.State == ImageState.RecoveryRequired) throw new InvalidOperationException("هذا المشروع يحتاج استعادة صريحة للجلسة قبل إعادة المحاولة.");
        if (loaded.WorkflowStage == 5 && File.Exists(loaded.OutputIso)) { OutputBox.Text = loaded.OutputIso; ResultText.Text = "حُفظت النتيجة سابقًا. التقارير داخل مجلد المشروع: " + loaded.Root; Page(DonePage); }
        else { StatusText.Text = "المشروع جاهز للمتابعة التلقائية. اضغط التالي."; Page(EditionPage); }
    });
    private void NewSource(object sender, RoutedEventArgs e)
    {
        if (busy || project?.State is ImageState.Mounted or ImageState.RecoveryRequired) return;
        project = null; selected = null; SourceBox.Clear(); EditionBox.ItemsSource = null; Progress.Value = 0;
        if (File.Exists(marker)) File.Delete(marker); StatusText.Text = "اختر ISO جديدًا."; Page(SourcePage);
    }
    private void ClosingWindow(object? sender, CancelEventArgs e)
    {
        if (busy) { e.Cancel = true; StatusText.Text = "أوقف بعد الخطوة الحالية أو انتظر اكتمال المهمة قبل الإغلاق."; return; }
        if (project?.State is ImageState.Mounted or ImageState.RecoveryRequired)
            if (MessageBox.Show(this, "الجلسة لم تُحفظ بعد. إغلاق التطبيق مع إبقاء المشروع والجلسة للمتابعة لاحقًا؟", "WinImageStudio Lite", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) { e.Cancel = true; return; }
        workflow.Dispose();
    }
}
