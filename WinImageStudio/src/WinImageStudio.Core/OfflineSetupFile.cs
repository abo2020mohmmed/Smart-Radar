using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace WinImageStudio.Core;

/// <summary>Deletes only the two known offline OneDrive installers, without following leaf links.</summary>
[SupportedOSPlatform("windows")]
public static class OfflineSetupFile
{
    public static bool AllowedStorageTag(uint attributes, uint tag) =>
        (attributes & 0x10) == 0 && ((attributes & 0x400) == 0 || tag is 0x80000008 or 0x80000017);

    public static void DeleteOneDrive(string mount, string directory)
    {
        if (directory is not ("System32" or "SysWOW64")) throw new ArgumentException("Unknown setup directory.");
        string parent = Validation.UnderRoot(mount, Path.Combine(mount, "Windows", directory));
        string file = Path.Combine(parent, "OneDriveSetup.exe");
        using var handle = CreateFileW(file, 0x10000 | 0x80, 3, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            if (error is 2 or 3) return;
            throw new IOException("Cannot open offline installer: " + file, new Win32Exception(error));
        }
        var resolved = new StringBuilder(32768);
        uint length = GetFinalPathNameByHandleW(handle, resolved, (uint)resolved.Capacity, 0);
        if (length == 0 || length >= resolved.Capacity) ThrowNative(file);
        string actual = resolved.ToString();
        if (actual.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) actual = @"\\" + actual[8..];
        else if (actual.StartsWith(@"\\?\", StringComparison.Ordinal)) actual = actual[4..];
        if (!Path.GetFullPath(actual).Equals(Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Offline installer resolved outside its expected location.");
        if (!GetFileInformationByHandleEx(handle, 9, out AttributeTag info, 8)) ThrowNative(file);
        if (!AllowedStorageTag(info.Attributes, info.Tag)) throw new IOException("تم تجاوز ملف رابط أو نوع غير مدعوم دون اتباعه: " + file);
        // Delete by the same no-follow handle that was inspected; never remove reparse metadata.
        uint disposition = 1 | 16; // DELETE | IGNORE_READONLY_ATTRIBUTE
        if (!SetFileInformationByHandle(handle, 21, ref disposition, 4)) ThrowNative(file);
    }
    private static void ThrowNative(string file) => throw new IOException("تعذر حذف ملف الإعداد داخل الصورة: " + file, new Win32Exception(Marshal.GetLastWin32Error()));
    [StructLayout(LayoutKind.Sequential)] private struct AttributeTag { public uint Attributes; public uint Tag; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, StringBuilder path, uint size, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int kind, out AttributeTag info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int kind, ref uint info, uint size);
}
