#pragma warning disable CA1416
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using WinImageStudio.Core;

internal static class OfflineSetupTests
{
    public static IEnumerable<(string Name, Action Body)> Cases()
    {
        yield return ("Storage tags accept WIM/WOF but refuse links, cloud and directories", () =>
        {
            foreach (uint tag in new uint[] { 0x80000008, 0x80000017 })
                Check(OfflineSetupFile.AllowedStorageTag(0x400, tag), "storage tag");
            foreach (uint tag in new uint[] { 0xA000000C, 0xA0000003, 0x9000001A, 0, 0x8000001B })
                Check(!OfflineSetupFile.AllowedStorageTag(0x400, tag), "unexpected tag accepted");
            Check(OfflineSetupFile.AllowedStorageTag(0x20, 0), "plain file");
            Check(!OfflineSetupFile.AllowedStorageTag(0x410, 0x80000017), "directory accepted");
        });
        if (!OperatingSystem.IsWindows()) yield break;
        yield return ("Windows deletes only offline readonly OneDrive installer", () => Fixture(root =>
        {
            string file = Installer(root); File.WriteAllText(file, "inert fixture"); File.SetAttributes(file, FileAttributes.ReadOnly);
            string neighbor = Path.Combine(Path.GetDirectoryName(file)!, "Keep.exe"); File.WriteAllText(neighbor, "keep");
            OfflineSetupFile.DeleteOneDrive(root, "SysWOW64");
            Check(!File.Exists(file) && File.Exists(neighbor), "wrong deletion");
            OfflineSetupFile.DeleteOneDrive(root, "System32");
        }));
        yield return ("Windows refuses leaf symbolic link and preserves its target", () => Fixture(root =>
        {
            string outside = Path.Combine(root, "outside.exe"); File.WriteAllText(outside, "keep");
            string file = Installer(root); File.CreateSymbolicLink(file, outside);
            try { OfflineSetupFile.DeleteOneDrive(root, "SysWOW64"); throw new Exception("Accepted link"); }
            catch (IOException) { }
            Check(File.ReadAllText(outside) == "keep" && File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint), "followed link");
            File.Delete(file);
        }));
        yield return ("Windows refuses a linked installer parent", () => Fixture(root =>
        {
            string outside = Path.Combine(root, "outside"); Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "OneDriveSetup.exe"), "keep");
            Directory.CreateDirectory(Path.Combine(root, "Windows"));
            string link = Path.Combine(root, "Windows", "SysWOW64"); Directory.CreateSymbolicLink(link, outside);
            try { OfflineSetupFile.DeleteOneDrive(root, "SysWOW64"); throw new Exception("Accepted parent link"); }
            catch (IOException) { }
            Check(File.Exists(Path.Combine(outside, "OneDriveSetup.exe")), "followed parent"); Directory.Delete(link);
        }));
        yield return ("Windows commits OneDrive deletion in a real mounted WIM", MountedWim);
        yield return ("Windows deletes a real WOF compacted installer", () => Fixture(root =>
        {
            string file = Installer(root);
            byte[] data = new byte[1024 * 1024]; for (int i = 0; i < data.Length; i++) data[i] = (byte)(i % 251);
            File.WriteAllBytes(file, data);
            using (var handle = File.OpenHandle(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var info = new CompressionInfo { Algorithm = 1, Flags = 0 }; // WOF LZX, FILE provider
                int result = WofSetFileDataLocation(handle, 2, ref info, 8);
                Marshal.ThrowExceptionForHR(result);
            }
            Check(File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint), "compact did not create WOF fixture");
            OfflineSetupFile.DeleteOneDrive(root, "SysWOW64"); Check(!File.Exists(file), "WOF installer remains");
        }));
    }
    [StructLayout(LayoutKind.Sequential)] private struct CompressionInfo { public uint Algorithm; public uint Flags; }
    [DllImport("WofUtil.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int WofSetFileDataLocation(SafeFileHandle file, uint provider, ref CompressionInfo info, uint size);
    private static void MountedWim()
    {
        string root = Path.Combine(Path.GetTempPath(), "WIS-WimTest-" + Guid.NewGuid().ToString("N"));
        string capture = Path.Combine(root, "capture"), mount = Path.Combine(root, "mount"), wim = Path.Combine(root, "fixture.wim");
        Directory.CreateDirectory(capture); Directory.CreateDirectory(mount);
        string source = Installer(capture); File.WriteAllBytes(source, new byte[65536]);
        bool mounted = false;
        try
        {
            Run("dism.exe", "/English", "/Capture-Image", "/ImageFile:" + wim, "/CaptureDir:" + capture, "/Name:OfflineSetupFixture", "/Compress:fast", "/CheckIntegrity");
            Run("dism.exe", "/English", "/Mount-Image", "/ImageFile:" + wim, "/Index:1", "/MountDir:" + mount); mounted = true;
            OfflineSetupFile.DeleteOneDrive(mount, "SysWOW64");
            Check(!File.Exists(Path.Combine(mount, "Windows", "SysWOW64", "OneDriveSetup.exe")), "mounted installer remains");
            Run("dism.exe", "/English", "/Unmount-Image", "/MountDir:" + mount, "/Commit"); mounted = false;
            Run("dism.exe", "/English", "/Mount-Image", "/ImageFile:" + wim, "/Index:1", "/MountDir:" + mount); mounted = true;
            Check(!File.Exists(Path.Combine(mount, "Windows", "SysWOW64", "OneDriveSetup.exe")), "deletion did not persist");
            Check(File.Exists(source), "capture source changed");
        }
        finally
        {
            if (mounted) { Run("dism.exe", "/English", "/Unmount-Image", "/MountDir:" + mount, "/Discard"); mounted = false; }
            if (!mounted) Directory.Delete(root, true);
        }
    }
    private static void Fixture(Action<string> action)
    {
        string root = Path.Combine(Path.GetTempPath(), "WIS-SetupTest-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try { action(root); } finally { Directory.Delete(root, true); }
    }
    private static string Installer(string root)
    {
        string dir = Path.Combine(root, "Windows", "SysWOW64"); Directory.CreateDirectory(dir); return Path.Combine(dir, "OneDriveSetup.exe");
    }
    internal static void Run(string executable, params string[] args)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Cannot start " + executable);
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit(); Task.WaitAll(output, error);
        if (process.ExitCode != 0) throw new IOException(executable + ": " + output.Result + error.Result);
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
