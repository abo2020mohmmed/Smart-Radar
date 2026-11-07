using System.Diagnostics;
using System.Text;

namespace WinImageStudio.Core;

public sealed class ProcessRunner(Action<string>? log = null) : ICommandRunner
{
    public async Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        executable = Validation.ExistingFile(executable);
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string arg in arguments) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        SafeLog("Starting " + Path.GetFileName(executable));
        if (!process.Start()) throw new IOException("Could not start " + executable);
        // A servicing process is never killed on cancellation: interrupting DISM
        // can corrupt its mount. Cancellation is observed between completed tools.
        Task<string> output = DrainAsync(process.StandardOutput);
        Task<string> error = DrainAsync(process.StandardError);
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
    }
    private async Task<string> DrainAsync(StreamReader reader)
    {
        var capture = new StringBuilder();
        const int captureLimit = 32 * 1024 * 1024;
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            SafeLog(line);
            if (capture.Length < captureLimit) capture.AppendLine(line);
        }
        return capture.ToString();
    }
    private void SafeLog(string line)
    {
        try { log?.Invoke(line); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { Trace.WriteLine(ex); }
    }
}
