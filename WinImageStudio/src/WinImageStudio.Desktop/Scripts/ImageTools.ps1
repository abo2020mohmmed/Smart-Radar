param(
    [Parameter(Mandatory=$true)][ValidateSet('Images','Inventory','ExtractIso','Mounts')][string]$Action,
    [string]$Source, [string]$Destination, [string]$MountPath,
    [ValidatePattern('^[a-fA-F0-9]{32}$')][string]$ProjectId
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$WarningPreference = 'SilentlyContinue'
$VerbosePreference = 'SilentlyContinue'
$InformationPreference = 'SilentlyContinue'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
try {
    if ($Action -eq 'ExtractIso') {
        if (Test-Path -LiteralPath $Destination) { throw 'Extraction destination already exists.' }
        $disk = Get-DiskImage -ImagePath $Source -ErrorAction SilentlyContinue
        $owned = $false
        try {
            if (!$disk -or !$disk.Attached) { $disk = Mount-DiskImage -ImagePath $Source -PassThru; $owned = $true }
            $volume = @($disk | Get-Volume | Where-Object DriveLetter)[0]
            if (!$volume) { throw 'Mounted ISO has no readable volume.' }
            $root = $volume.DriveLetter + ':\'
            . (Join-Path $PSScriptRoot 'MediaCopy.ps1')
            $copied = Copy-WisMediaDirectory -SourceRoot $root -DestinationRoot $Destination
            [pscustomobject]@{ extracted=$true; files=[long]$copied.FilesCopied; bytes=[long]$copied.BytesCopied } | ConvertTo-Json -Compress
        } finally { if ($owned) { Dismount-DiskImage -ImagePath $Source | Out-Null } }
        exit 0
    }
    Import-Module Dism -ErrorAction Stop
    if ($Action -eq 'Images') {
        $items = @(Get-WindowsImage -ImagePath $Source | ForEach-Object {
            $detail = Get-WindowsImage -ImagePath $Source -Index $_.ImageIndex
            [pscustomobject]@{ ImageIndex=[int]$_.ImageIndex; ImageName=[string]$_.ImageName; ImageDescription=[string]$_.ImageDescription; Architecture=[string]$detail.Architecture; Version=[string]$detail.Version; EditionId=[string]$detail.EditionId; DefaultLanguage=[string]$detail.DefaultLanguage }
        })
        ConvertTo-Json -InputObject $items -Depth 6 -Compress
    } elseif ($Action -eq 'Mounts') {
        $items = @(Get-WindowsImage -Mounted | ForEach-Object { [pscustomobject]@{ Path=[string]$_.Path; Status=[string]$_.MountStatus } })
        ConvertTo-Json -InputObject $items -Compress
    } elseif ($Action -eq 'Inventory') {
        . (Join-Path $PSScriptRoot 'PresetMetadata.ps1')
        $features = @(Get-WindowsOptionalFeature -Path $MountPath | ForEach-Object {
            [pscustomobject]@{ Name=[string]$_.FeatureName; State=[string]$_.State; Details=''; Removable=(!([string]$_.FeatureName).StartsWith('Windows-Defender',[StringComparison]::OrdinalIgnoreCase)) }
        })
        $packages = @(Get-WindowsPackage -Path $MountPath | ForEach-Object {
            $meta = Get-WisPackageMetadata -MountPath $MountPath -PackageName $_.PackageName
            [pscustomobject]@{ Name=[string]$_.PackageName; State=[string]$_.PackageState; Details=([string]$_.ReleaseType + ' | ' + $meta.Reason); Removable=[bool]$meta.Removable; Category=[string]$meta.Category; Language=[string]$meta.Language }
        })
        $drivers = @(Get-WindowsDriver -Path $MountPath -All | ForEach-Object { [pscustomobject]@{ Name=[string]$_.Driver; State=if ($_.Inbox) {'Inbox'} else {'ThirdParty'}; Details=([string]$_.ProviderName + ' | ' + [string]$_.OriginalFileName) } })
        $apps = @(Get-AppxProvisionedPackage -Path $MountPath | ForEach-Object {
            $framework = $false; $removable = $true
            if ($_.PSObject.Properties['IsFramework']) { $framework = [bool]$_.IsFramework }
            if ($_.PSObject.Properties['NonRemovable']) { $removable = ![bool]$_.NonRemovable }
            [pscustomobject]@{ Name=[string]$_.PackageName; State='Provisioned'; Details=[string]$_.DisplayName; Removable=$removable; IsFramework=$framework }
        })
        $capabilities = @(Get-WindowsCapability -Path $MountPath | ForEach-Object { [pscustomobject]@{ Name=[string]$_.Name; State=[string]$_.State; Details='' } })
        $registry = Get-WisOfflineRegistryProfile -MountPath $MountPath -ProjectId $ProjectId
        $services = @($registry.Services)
        $nativeEdition = [string](Get-WindowsEdition -Path $MountPath).Edition
        $edition = if ($nativeEdition -eq $registry.EditionId) { $nativeEdition } else { 'Mismatch:' + $nativeEdition + '/' + $registry.EditionId }
        $languages = @($registry.InstalledLanguages)
        foreach ($package in $packages) { if ($package.Category -eq 'LanguagePack' -and $package.Language -and $package.State -eq 'Installed') { $languages += $package.Language } }
        $defenderPresent = @($services | Where-Object Name -eq 'WinDefend').Count -gt 0
        foreach ($folder in @('Program Files','Program Files (x86)')) {
            if ([IO.File]::Exists((Join-Path $MountPath ($folder + '\Windows Defender\MsMpEng.exe')))) { $defenderPresent = $true }
        }
        $platform = Join-Path $MountPath 'ProgramData\Microsoft\Windows Defender\Platform'
        if ([IO.Directory]::Exists($platform)) {
            if (([IO.File]::GetAttributes($platform) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { $defenderPresent = $true }
            else {
                foreach ($subdirectory in [IO.Directory]::EnumerateDirectories($platform)) {
                    if (([IO.File]::GetAttributes($subdirectory) -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or [IO.File]::Exists([IO.Path]::Combine($subdirectory,'MsMpEng.exe'))) { $defenderPresent = $true }
                }
            }
        }
        $oneDrivePresent = @($apps | Where-Object { $_.Details -in @('Microsoft.OneDriveSync','Microsoft.OneDrive') }).Count -gt 0
        foreach ($folder in @('System32','SysWOW64')) {
            if ([IO.File]::Exists((Join-Path $MountPath ('Windows\' + $folder + '\OneDriveSetup.exe')))) { $oneDrivePresent = $true }
        }
        foreach ($folder in @('Program Files','Program Files (x86)')) {
            if ([IO.Directory]::Exists((Join-Path $MountPath ($folder + '\Microsoft OneDrive')))) { $oneDrivePresent = $true }
        }
        $profile = [pscustomobject]@{
            EditionId=$edition; InstallationType=[string]$registry.InstallationType; Build=[string]$registry.Build;
            BaseLanguage=[string]$registry.BaseLanguage; InstalledLanguages=@($languages | Sort-Object -Unique);
            PendingActions=[IO.File]::Exists((Join-Path $MountPath 'Windows\WinSxS\pending.xml'));
            DefenderPresent=$defenderPresent; OneDrivePresent=$oneDrivePresent
        }
        [pscustomobject]@{ Features=$features; Packages=$packages; Drivers=$drivers; Apps=$apps; Services=$services; Profile=$profile; Capabilities=$capabilities } | ConvertTo-Json -Depth 8 -Compress
    }
    exit 0
} catch { [Console]::Error.WriteLine($_.Exception.ToString()); exit 1 }
