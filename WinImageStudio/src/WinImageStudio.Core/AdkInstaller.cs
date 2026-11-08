using System.Net;
using System.Security.Cryptography;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace WinImageStudio.Core;

[SupportedOSPlatform("windows")]
public sealed class AdkInstaller(ICommandRunner runner, string powerShell, string verificationScript, string? bundledInstaller = null)
{
    // Microsoft Learn ADK 10.1.26100.9457 (September 2026), compatible with Windows 10/11.
    public const string BundledSha256 = "AC6A930FDB5C2980BA5FEFE606D47EDAAFCF5F647B4337411500D158EA77300F";
    public const long BundledSize = 2233776;
    public static void ValidateBundledBytes(Stream source)
    {
        if (!source.CanSeek || source.Length != BundledSize) throw new InvalidDataException("The bundled ADK setup does not match the supplied file size.");
        source.Position = 0;
        string hash = Convert.ToHexString(SHA256.HashData(source)); source.Position = 0;
        if (!hash.Equals(BundledSha256, StringComparison.Ordinal)) throw new InvalidDataException("The bundled ADK setup hash does not match the supplied file. Installation was refused.");
    }
    public static Uri DownloadUri { get; } = new("https://go.microsoft.com/fwlink/?linkid=2289980");
    public static bool TrustedDownload(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443 && string.IsNullOrEmpty(uri.UserInfo) &&
        (uri.Host.Equals("go.microsoft.com", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("download.microsoft.com", StringComparison.OrdinalIgnoreCase));
    public static string? FindOscdimg()
    {
        var roots = new List<string> { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10") };
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows Kits\Installed Roots");
            if (key?.GetValue("KitsRoot10") is string value && !string.IsNullOrWhiteSpace(value)) roots.Add(value);
        }
        foreach (string root in roots)
        {
            string candidate = Path.Combine(root, "Assessment and Deployment Kit", "Deployment Tools", "amd64", "Oscdimg", "oscdimg.exe");
            if (File.Exists(candidate)) { Validation.RejectReparseAncestors(candidate); return candidate; }
        }
        return null;
    }
    public async Task<(string Path, bool RebootRequired)> EnsureInstalledAsync(CancellationToken token, bool verifyDownloadEvenIfInstalled = false)
    {
        string? existing = FindOscdimg();
        if (existing is not null && !verifyDownloadEvenIfInstalled) return (existing, false);
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinImageStudio", "Downloads", Guid.NewGuid().ToString("N"));
        Validation.RejectReparseAncestors(folder); Directory.CreateDirectory(folder);
        string setup = Path.Combine(folder, "adksetup.exe");
        try
        {
            if (bundledInstaller is not null)
            {
                string original = Validation.ExistingFile(bundledInstaller, ".exe"); Validation.RejectReparseAncestors(original);
                await using var input = new FileStream(original, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
                await using var output = new FileStream(setup, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
                await input.CopyToAsync(output, token).ConfigureAwait(false);
            }
            else
            using (var handler = new HttpClientHandler { AllowAutoRedirect = false })
            using (var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) })
            {
                Uri uri = DownloadUri;
                for (int hop = 0; ; hop++)
                {
                    if (!TrustedDownload(uri) || hop > 6) throw new IOException("ADK redirect is outside approved Microsoft HTTPS download hosts.");
                    using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                    if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect or HttpStatusCode.SeeOther)
                    {
                        var location = response.Headers.Location ?? throw new IOException("ADK redirect has no location.");
                        uri = location.IsAbsoluteUri ? location : new Uri(uri, location); continue;
                    }
                    response.EnsureSuccessStatusCode();
                    const long limit = 32 * 1024 * 1024;
                    if (response.Content.Headers.ContentLength > limit) throw new IOException("ADK bootstrapper exceeds the download limit.");
                    await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                    await using var output = new FileStream(setup, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
                    byte[] buffer = new byte[65536]; long total = 0; int read;
                    while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                    {
                        total += read; if (total > limit) throw new IOException("ADK bootstrapper exceeds the download limit.");
                        await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    }
                    if (total == 0) throw new IOException("ADK download is empty.");
                    break;
                }
            }
            Validation.RejectReparse(setup);
            // Keep the downloaded executable locked against modification/deletion during verification and execution.
            using var locked = File.Open(setup, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (bundledInstaller is not null) ValidateBundledBytes(locked);
            (await runner.RunAsync(powerShell, ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Validation.ExistingFile(verificationScript, ".ps1"), "-Installer", setup], token).ConfigureAwait(false)).EnsureSuccess("Verify Microsoft ADK Authenticode signature");
            if (existing is not null) return (existing, false);
            var installed = await runner.RunAsync(setup, ["/quiet", "/norestart", "/ceip", "off", "/features", "OptionId.DeploymentTools"], token).ConfigureAwait(false);
            installed.EnsureSuccess("Install Microsoft ADK Deployment Tools");
            string path = FindOscdimg() ?? throw new IOException("ADK completed but oscdimg was not found. Restart if requested, then use Detect installed tool.");
            return (path, installed.RebootRequired);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
