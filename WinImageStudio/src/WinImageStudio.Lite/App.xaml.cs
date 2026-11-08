using System.IO;
using System.Windows;
using System.Windows.Threading;
using WinImageStudio.Core;

namespace WinImageStudio.Lite;
public partial class App : Application
{
    private Mutex? instance;
    private bool installerMode;
    protected override void OnStartup(StartupEventArgs e)
    {
        installerMode = e.Args.Length == 1 && e.Args[0] == "--install-bundled-adk";
        DispatcherUnhandledException += OnDispatcherError;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => EmergencyLog(args.ExceptionObject.ToString() ?? "Unknown exception");
        TaskScheduler.UnobservedTaskException += (_, args) => { EmergencyLog(args.Exception.ToString()); args.SetObserved(); };
        instance = new Mutex(false, "WinImageStudio.Lite", out bool first);
        if (!first)
        {
            if (!installerMode) MessageBox.Show("WinImageStudio Lite يعمل بالفعل. استخدم النافذة المفتوحة لحماية جلسة الصورة الحالية.", "WinImageStudio Lite", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(installerMode ? 32 : 0); return;
        }
        if (installerMode)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            _ = InstallBundledAdkAsync();
            return;
        }
        base.OnStartup(e);
        MainWindow = new LiteWindow(); MainWindow.Show();
    }
    private async Task InstallBundledAdkAsync()
    {
        int code;
        try
        {
            var installer = new AdkInstaller(new ProcessRunner(EmergencyLog), ToolSettings.WindowsDefaults().PowerShell,
                Path.Combine(AppContext.BaseDirectory, "Scripts", "VerifyAdk.ps1"), Path.Combine(AppContext.BaseDirectory, "Prerequisites", "adksetup.exe"));
            var result = await installer.EnsureInstalledAsync(CancellationToken.None, verifyDownloadEvenIfInstalled: true);
            EmergencyLog("Bundled ADK ready: " + result.Path); code = result.RebootRequired ? 3010 : 0;
        }
        catch (Exception ex) { EmergencyLog(ex.ToString()); code = 1; }
        Dispatcher.Invoke(() => Shutdown(code));
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
    private void OnDispatcherError(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        EmergencyLog(e.Exception.ToString());
        if (!installerMode) MessageBox.Show("Unexpected application error. The image operation journal is preserved. Reopen the project and use recovery.\n\n" + e.Exception.Message, "WinImageStudio Lite", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true; Shutdown(1);
    }
    internal static void EmergencyLog(string error)
    {
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinImageStudioLite");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "crash.log"), DateTimeOffset.UtcNow + " " + error + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine(ex); }
    }
}
