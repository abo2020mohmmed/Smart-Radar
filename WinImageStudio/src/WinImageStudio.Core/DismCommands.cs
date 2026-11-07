namespace WinImageStudio.Core;

public static class DismCommands
{
    public static IReadOnlyList<string> Export(string source, int index, string destination)
    {
        if (index < 1) throw new ArgumentOutOfRangeException(nameof(index));
        return ["/English", "/Export-Image", "/SourceImageFile:" + source, "/SourceIndex:" + index, "/DestinationImageFile:" + destination, "/Compress:max", "/CheckIntegrity"];
    }
    public static IReadOnlyList<string> Mount(string wim, string mount) => ["/English", "/Mount-Image", "/ImageFile:" + wim, "/Index:1", "/MountDir:" + mount, "/CheckIntegrity"];
    public static IReadOnlyList<string> Unmount(string mount, bool commit) => commit ? ["/English", "/Unmount-Image", "/MountDir:" + mount, "/Commit", "/CheckIntegrity"] : ["/English", "/Unmount-Image", "/MountDir:" + mount, "/Discard"];
    public static IReadOnlyList<string> Change(string mount, PlannedOperation operation)
    {
        Validation.Operation(operation);
        List<string> args = ["/English", "/Image:" + mount, "/NoRestart"];
        args.AddRange(operation.Kind switch
        {
            OperationKind.EnableFeature => ["/Enable-Feature", "/FeatureName:" + operation.Target, "/All"],
            OperationKind.DisableFeature => ["/Disable-Feature", "/FeatureName:" + operation.Target],
            OperationKind.RemovePackage => ["/Remove-Package", "/PackageName:" + operation.Target],
            OperationKind.AddPackage => ["/Add-Package", "/PackagePath:" + operation.Target],
            OperationKind.AddDriver => ["/Add-Driver", "/Driver:" + operation.Target],
            OperationKind.RemoveDriver => ["/Remove-Driver", "/Driver:" + operation.Target],
            OperationKind.RemoveProvisionedApp => ["/Remove-ProvisionedAppxPackage", "/PackageName:" + operation.Target],
            _ => throw new ArgumentException("This operation is not a DISM command.")
        });
        return args;
    }
    public static IReadOnlyList<string> Iso(string media, string output)
    {
        string bios = Path.Combine(media, "boot", "etfsboot.com");
        string uefi = Path.Combine(media, "efi", "microsoft", "boot", "efisys.bin");
        bool hasBios = File.Exists(bios), hasUefi = File.Exists(uefi);
        if (!hasBios && !hasUefi) throw new FileNotFoundException("Boot images are missing from the installation media.");
        string boot = hasBios && hasUefi ? $"-bootdata:2#p0,e,b{bios}#pEF,e,b{uefi}" : hasBios ? $"-bootdata:1#p0,e,b{bios}" : $"-bootdata:1#pEF,e,b{uefi}";
        return ["-m", "-o", "-u2", "-udfver102", boot, media, output];
    }
}
