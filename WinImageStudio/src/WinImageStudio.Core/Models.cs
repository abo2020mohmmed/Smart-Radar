using System.Text.Json.Serialization;

namespace WinImageStudio.Core;

public enum OperationKind { EnableFeature, DisableFeature, RemovePackage, AddPackage, AddDriver, RemoveDriver, RemoveProvisionedApp, SetServiceStart, SetRegistry, StageInstaller, WriteUnattend }
public enum HiveKind { Software, System }
public enum ValueKind { String, ExpandString, DWord, QWord, MultiString, Binary }
public enum ImageState { Prepared, Mounted, RecoveryRequired, Committed, Discarded }
public sealed record ImageInfo(int ImageIndex, string ImageName, string ImageDescription, string Architecture, string Version);
public sealed record InventoryItem(string Name, string State, string Details);
public sealed record Inventory(IReadOnlyList<InventoryItem> Features, IReadOnlyList<InventoryItem> Packages, IReadOnlyList<InventoryItem> Drivers, IReadOnlyList<InventoryItem> Apps, IReadOnlyList<InventoryItem> Services);
public sealed record RegistryChange(HiveKind Hive, string Key, string Name, ValueKind Kind, string Value);
public sealed record UnattendOptions(string ComputerName = "WINPC", string Language = "en-US", string InputLocale = "0409:00000409", string TimeZone = "UTC", string Architecture = "amd64");
public sealed record PlannedOperation(OperationKind Kind, string Target, string Arguments = "", RegistryChange? Registry = null, UnattendOptions? Unattend = null)
{
    [JsonIgnore] public string Description => $"{Kind}: {Target}";
}
public sealed class ProjectSession
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string Root { get; set; }
    public required string Source { get; set; }
    public required string WorkingWim { get; set; }
    public required string MountPath { get; set; }
    public string? MediaPath { get; set; }
    public string? ImageSource { get; set; }
    public ImageInfo? Image { get; set; }
    public ImageState State { get; set; } = ImageState.Prepared;
    public string? PendingOperation { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
public sealed record ToolSettings(string PowerShell, string Dism, string Reg, string? Oscdimg = null, string? Wimlib = null)
{
    public static ToolSettings WindowsDefaults() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "dism.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "reg.exe"));
}
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool RebootRequired => ExitCode == 3010;
    public void EnsureSuccess(string operation)
    {
        if (ExitCode is not (0 or 3010)) throw new ToolException(operation, ExitCode, StandardOutput, StandardError);
    }
}
public sealed class ToolException(string operation, int exitCode, string output, string error)
    : Exception($"{operation} failed (exit {exitCode}).\n{error}\n{output}")
{
    public int ExitCode { get; } = exitCode;
}
public interface ICommandRunner
{
    Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}
public interface IProjectStore
{
    Task SaveAsync(ProjectSession session, CancellationToken token = default);
    Task<ProjectSession> LoadAsync(string root, CancellationToken token = default);
}
