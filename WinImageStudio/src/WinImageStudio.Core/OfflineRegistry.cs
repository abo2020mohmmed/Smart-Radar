using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace WinImageStudio.Core;

[SupportedOSPlatform("windows")]
public sealed class OfflineRegistry(ICommandRunner runner, string regTool)
{
    public async Task EditAsync(ProjectSession project, HiveKind hive, Action<RegistryKey> edit, CancellationToken token)
    {
        string file = Validation.UnderRoot(project.MountPath, Path.Combine(project.MountPath, "Windows", "System32", "config", hive == HiveKind.Software ? "SOFTWARE" : "SYSTEM"));
        Validation.ExistingFile(file);
        string name = KeyName(project, hive);
        var load = await runner.RunAsync(regTool, ["load", "HKLM\\" + name, file], token).ConfigureAwait(false);
        load.EnsureSuccess("Load offline hive");
        Exception? primary = null;
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(name, true) ?? throw new IOException("Loaded hive is unavailable.");
            edit(key);
            key.Flush();
        }
        catch (Exception ex) { primary = ex; throw; }
        finally
        {
            var unload = await runner.RunAsync(regTool, ["unload", "HKLM\\" + name], CancellationToken.None).ConfigureAwait(false);
            if (unload.ExitCode != 0)
            {
                var failure = new ToolException("Unload offline hive " + name, unload.ExitCode, unload.StandardOutput, unload.StandardError);
                if (primary is not null) throw new AggregateException(primary, failure);
                throw failure;
            }
        }
    }
    public Task SetValueAsync(ProjectSession project, RegistryChange change, CancellationToken token)
    {
        Validation.CheckRegistry(change);
        return EditAsync(project, change.Hive, root =>
        {
            string keyPath = ResolveControlSet(root, change.Hive, change.Key);
            using var key = root.CreateSubKey(keyPath, true);
            object value = change.Kind switch
            {
                ValueKind.DWord => unchecked((int)uint.Parse(change.Value, CultureInfo.InvariantCulture)),
                ValueKind.QWord => unchecked((long)ulong.Parse(change.Value, CultureInfo.InvariantCulture)),
                ValueKind.MultiString => change.Value.Split('\n').Select(v => v.TrimEnd('\r')).ToArray(),
                ValueKind.Binary => Convert.FromHexString(change.Value.Replace(" ", "", StringComparison.Ordinal)),
                _ => change.Value
            };
            RegistryValueKind kind = change.Kind switch
            {
                ValueKind.DWord => RegistryValueKind.DWord, ValueKind.QWord => RegistryValueKind.QWord,
                ValueKind.MultiString => RegistryValueKind.MultiString, ValueKind.Binary => RegistryValueKind.Binary,
                ValueKind.ExpandString => RegistryValueKind.ExpandString, _ => RegistryValueKind.String
            };
            key.SetValue(change.Name, value, kind);
        }, token);
    }
    public Task SetServiceAsync(ProjectSession project, string service, int start, CancellationToken token)
    {
        Validation.Identifier(service);
        if (start is < 2 or > 4) throw new ArgumentOutOfRangeException(nameof(start));
        return EditAsync(project, HiveKind.System, root =>
        {
            string path = ResolveControlSet(root, HiveKind.System, "CurrentControlSet\\Services\\" + service);
            using var key = root.OpenSubKey(path, true) ?? throw new ArgumentException("Service does not exist in the offline image.");
            int existing = Convert.ToInt32(key.GetValue("Start", 3), CultureInfo.InvariantCulture);
            int type = Convert.ToInt32(key.GetValue("Type", 0), CultureInfo.InvariantCulture);
            if (existing is 0 or 1 || (type & 3) != 0) throw new InvalidOperationException("Boot/system-start services and kernel drivers are protected from service editing.");
            key.SetValue("Start", start, RegistryValueKind.DWord);
        }, token);
    }
    public async Task RecoverUnloadAsync(ProjectSession project)
    {
        foreach (HiveKind hive in Enum.GetValues<HiveKind>())
        {
            string name = KeyName(project, hive);
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using (var key = root.OpenSubKey(name)) { if (key is null) continue; }
            var result = await runner.RunAsync(regTool, ["unload", "HKLM\\" + name], CancellationToken.None).ConfigureAwait(false);
            result.EnsureSuccess("Recover offline hive");
        }
    }
    private static string KeyName(ProjectSession project, HiveKind hive)
    {
        if (!Guid.TryParseExact(project.Id, "N", out _)) throw new InvalidDataException("Invalid project ID.");
        return "WIS_" + project.Id + "_" + hive;
    }
    private static string ResolveControlSet(RegistryKey root, HiveKind hive, string path)
    {
        const string prefix = "CurrentControlSet\\";
        bool exact = path.Equals("CurrentControlSet", StringComparison.OrdinalIgnoreCase);
        if (hive != HiveKind.System || (!exact && !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) return path;
        using var select = root.OpenSubKey("Select") ?? throw new InvalidDataException("Offline SYSTEM hive has no Select key.");
        int current = Convert.ToInt32(select.GetValue("Current") ?? throw new InvalidDataException("Current control set is missing."), CultureInfo.InvariantCulture);
        if (current is < 1 or > 999) throw new InvalidDataException("Invalid control-set index.");
        return "ControlSet" + current.ToString("D3", CultureInfo.InvariantCulture) + (exact ? "" : "\\" + path[prefix.Length..]);
    }
}
