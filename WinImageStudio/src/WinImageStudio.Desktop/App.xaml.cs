using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace WinImageStudio.Desktop;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherError;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => EmergencyLog(args.ExceptionObject.ToString() ?? "Unknown exception");
        TaskScheduler.UnobservedTaskException += (_, args) => { EmergencyLog(args.Exception.ToString()); args.SetObserved(); };
        base.OnStartup(e);
    }
    private void OnDispatcherError(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        EmergencyLog(e.Exception.ToString());
        MessageBox.Show("Unexpected application error. The image operation journal is preserved. Reopen the project and use recovery.\n\n" + e.Exception.Message, "WinImageStudio", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true; Shutdown(1);
    }
    internal static void EmergencyLog(string error)
    {
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinImageStudio");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "crash.log"), DateTimeOffset.UtcNow + " " + error + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine(ex); }
    }
}
