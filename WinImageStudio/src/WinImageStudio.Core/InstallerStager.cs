using System.Text;

namespace WinImageStudio.Core;

public static class InstallerStager
{
    public static async Task StageAsync(string mountedWindows, string source, string arguments, CancellationToken token)
    {
        source = Validation.ExistingFile(source, ".exe", ".msi");
        Validation.CheckInstallerArguments(arguments);
        string scriptFolder = Validation.UnderRoot(mountedWindows, Path.Combine(mountedWindows, "Windows", "Setup", "Scripts"));
        string installerFolder = Validation.UnderRoot(mountedWindows, Path.Combine(mountedWindows, "WinImageStudio", "Installers"));
        Directory.CreateDirectory(scriptFolder); Directory.CreateDirectory(installerFolder);
        string setup = Path.Combine(scriptFolder, "SetupComplete.cmd");
        string marker = "rem WinImageStudio managed setup script";
        if (File.Exists(setup) && !(await File.ReadAllTextAsync(setup, token).ConfigureAwait(false)).Contains(marker, StringComparison.Ordinal)) throw new IOException("An existing SetupComplete.cmd belongs to another tool; refusing to overwrite it.");
        string fileName = Guid.NewGuid().ToString("N") + Path.GetExtension(source).ToLowerInvariant();
        string destination = Path.Combine(installerFolder, fileName);
        await using (var input = File.OpenRead(source))
        await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true))
            await input.CopyToAsync(output, token).ConfigureAwait(false);
        string batch = Path.Combine(scriptFolder, "WinImageStudio-Install.cmd");
        if (!File.Exists(batch)) await File.WriteAllTextAsync(batch, "@echo off\r\n" + marker + "\r\n", Encoding.ASCII, token).ConfigureAwait(false);
        string target = "%SystemDrive%\\WinImageStudio\\Installers\\" + fileName;
        string command = fileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
            ? $"start /wait \"\" \"%SystemRoot%\\System32\\msiexec.exe\" /i \"{target}\" {arguments}"
            : $"start /wait \"\" \"{target}\" {arguments}";
        await File.AppendAllTextAsync(batch, command + " >>\"%SystemRoot%\\Temp\\WinImageStudio-install.log\" 2>&1\r\necho Exit code: %errorlevel% >>\"%SystemRoot%\\Temp\\WinImageStudio-install.log\"\r\n", Encoding.ASCII, token).ConfigureAwait(false);
        await File.WriteAllTextAsync(setup, "@echo off\r\n" + marker + "\r\ncall \"%~dp0WinImageStudio-Install.cmd\"\r\nexit /b 0\r\n", Encoding.ASCII, token).ConfigureAwait(false);
    }
}
