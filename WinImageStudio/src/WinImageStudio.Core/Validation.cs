using System.Globalization;
using System.Text.RegularExpressions;

namespace WinImageStudio.Core;

public static partial class Validation
{
    [GeneratedRegex(@"^[A-Za-z0-9_.-]{1,256}$", RegexOptions.CultureInvariant)] private static partial Regex IdentifierPattern();
    [GeneratedRegex(@"^(?=.*[A-Za-z])[A-Za-z0-9][A-Za-z0-9-]{0,14}$", RegexOptions.CultureInvariant)] private static partial Regex ComputerPattern();
    public static string ExistingFile(string path, params string[] extensions)
    {
        string full = Path.GetFullPath(path);
        if (!File.Exists(full)) throw new FileNotFoundException("File was not found.", full);
        if (extensions.Length > 0 && !extensions.Contains(Path.GetExtension(full), StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Unsupported file type: " + full);
        RejectReparse(full);
        return full;
    }
    public static string Identifier(string value)
    {
        if (!IdentifierPattern().IsMatch(value)) throw new ArgumentException("Invalid feature, service or driver identifier.");
        return value;
    }
    public static string PackageIdentity(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512 || value.Any(c => char.IsControl(c) || c is '/' or '\\' or ':')) throw new ArgumentException("Invalid package identity.");
        return value;
    }
    public static string UnderRoot(string root, string path)
    {
        string r = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string p = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!p.StartsWith(r + Path.DirectorySeparatorChar, comparison)) throw new ArgumentException("Path must be a descendant of the project directory.");
        RejectReparseAncestors(p);
        return p;
    }
    public static void RejectReparse(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Reparse points are not allowed: " + path);
    }
    public static void RejectReparseAncestors(string path)
    {
        string? p = Path.GetFullPath(path);
        while (p is not null)
        {
            if (File.Exists(p) || Directory.Exists(p)) RejectReparse(p);
            p = Path.GetDirectoryName(p);
        }
    }
    public static void CheckInstallerArguments(string args)
    {
        if (args.Length > 2048 || args.Any(c => char.IsControl(c) || c > 126 || "%!&|<>^()".Contains(c))) throw new ArgumentException("Installer arguments contain unsupported command-shell characters.");
        if (args.Count(c => c == '"') % 2 != 0) throw new ArgumentException("Unbalanced quotes in installer arguments.");
    }
    public static void CheckRegistry(RegistryChange change)
    {
        if (!Enum.IsDefined(change.Hive) || !Enum.IsDefined(change.Kind)) throw new ArgumentException("Invalid registry hive or type.");
        if (string.IsNullOrWhiteSpace(change.Key) || change.Key.StartsWith('\\') || change.Key.Split('\\').Any(x => x is "" or "." or "..") || change.Key.Any(char.IsControl) || change.Name.Any(char.IsControl)) throw new ArgumentException("Invalid offline registry key or value name.");
        switch (change.Kind)
        {
            case ValueKind.DWord: _ = uint.Parse(change.Value, CultureInfo.InvariantCulture); break;
            case ValueKind.QWord: _ = ulong.Parse(change.Value, CultureInfo.InvariantCulture); break;
            case ValueKind.Binary: _ = Convert.FromHexString(change.Value.Replace(" ", "", StringComparison.Ordinal)); break;
        }
    }
    public static void CheckUnattend(UnattendOptions options)
    {
        if (!ComputerPattern().IsMatch(options.ComputerName)) throw new ArgumentException("Computer name: 1–15 letters/digits/hyphens, starting with a letter/digit and containing a letter.");
        if (string.IsNullOrWhiteSpace(options.Language)) throw new ArgumentException("Language is required.");
        _ = CultureInfo.GetCultureInfo(options.Language);
        if (options.Architecture is not ("amd64" or "arm64" or "x86")) throw new ArgumentException("Invalid architecture.");
        if (string.IsNullOrWhiteSpace(options.TimeZone) || options.TimeZone.Any(char.IsControl)) throw new ArgumentException("Invalid Windows time-zone ID.");
        if (string.IsNullOrWhiteSpace(options.InputLocale) || options.InputLocale.Any(char.IsControl)) throw new ArgumentException("Invalid input locale.");
    }
    public static void Operation(PlannedOperation operation)
    {
        if (operation.PresetId is not null && operation.PresetId != AggressivePreset.Id) throw new ArgumentException("Unknown preset identifier.");
        switch (operation.Kind)
        {
            case OperationKind.EnableFeature:
            case OperationKind.DisableFeature:
            case OperationKind.RemoveFeaturePayload:
            case OperationKind.SetServiceStart:
                Identifier(operation.Target); break;
            case OperationKind.RemoveDriver:
                Identifier(operation.Target);
                if (!operation.Target.StartsWith("oem", StringComparison.OrdinalIgnoreCase) || !operation.Target.EndsWith(".inf", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Only third-party oem*.inf drivers may be removed.");
                break;
            case OperationKind.RemovePackage:
            case OperationKind.RemoveProvisionedApp:
                PackageIdentity(operation.Target); break;
            case OperationKind.RemoveCapability:
                if (AggressivePreset.CapabilityLanguage(operation.Target) == "") throw new ArgumentException("Invalid language capability name.");
                break;
            case OperationKind.RemoveOneDrive:
                if (operation.Target != "OneDrive" || operation.Arguments != "") throw new ArgumentException("Invalid scoped OneDrive operation.");
                break;
            case OperationKind.AddPackage: ExistingFile(operation.Target, ".cab", ".msu"); break;
            case OperationKind.AddDriver: ExistingFile(operation.Target, ".inf"); break;
            case OperationKind.SetRegistry:
                CheckRegistry(operation.Registry ?? throw new ArgumentException("Registry change is missing.")); break;
            case OperationKind.StageInstaller:
                ExistingFile(operation.Target, ".exe", ".msi"); CheckInstallerArguments(operation.Arguments); break;
            case OperationKind.WriteUnattend:
                CheckUnattend(operation.Unattend ?? throw new ArgumentException("Unattended options are missing.")); break;
            default: throw new ArgumentException("Unsupported operation.");
        }
        if (operation.Kind == OperationKind.SetServiceStart && operation.Arguments is not ("2" or "3" or "4")) throw new ArgumentException("Service start must be 2 (automatic), 3 (manual), or 4 (disabled).");
    }
}
