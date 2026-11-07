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
            [pscustomobject]@{ ImageIndex=[int]$_.ImageIndex; ImageName=[string]$_.ImageName; ImageDescription=[string]$_.ImageDescription; Architecture=[string]$detail.Architecture; Version=[string]$detail.Version }
        })
        ConvertTo-Json -InputObject $items -Depth 6 -Compress
    } elseif ($Action -eq 'Mounts') {
        $items = @(Get-WindowsImage -Mounted | ForEach-Object { [pscustomobject]@{ Path=[string]$_.Path; Status=[string]$_.MountStatus } })
        ConvertTo-Json -InputObject $items -Compress
    } elseif ($Action -eq 'Inventory') {
        $features = @(Get-WindowsOptionalFeature -Path $MountPath | ForEach-Object { [pscustomobject]@{ Name=[string]$_.FeatureName; State=[string]$_.State; Details='' } })
        $packages = @(Get-WindowsPackage -Path $MountPath | ForEach-Object { [pscustomobject]@{ Name=[string]$_.PackageName; State=[string]$_.PackageState; Details=[string]$_.ReleaseType } })
        $drivers = @(Get-WindowsDriver -Path $MountPath -All | ForEach-Object { [pscustomobject]@{ Name=[string]$_.Driver; State=if ($_.Inbox) {'Inbox'} else {'ThirdParty'}; Details=([string]$_.ProviderName + ' | ' + [string]$_.OriginalFileName) } })
        $apps = @(Get-AppxProvisionedPackage -Path $MountPath | ForEach-Object { [pscustomobject]@{ Name=[string]$_.PackageName; State='Provisioned'; Details=[string]$_.DisplayName } })
        # Read SYSTEM through an isolated app-owned hive; never inspect host services.
        if (!$ProjectId) { throw 'Project ID is required for offline inventory.' }
        $hive = 'WIS_' + $ProjectId + '_System'
        $hiveFile = Join-Path $MountPath 'Windows\System32\config\SYSTEM'
        & "$env:SystemRoot\System32\reg.exe" load "HKLM\$hive" $hiveFile | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Cannot load offline SYSTEM hive for service inventory.' }
        $services = @()
        try {
            $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
            try {
                $select = $base.OpenSubKey("$hive\Select")
                try { $current = [int]$select.GetValue('Current') } finally { if ($select) { $select.Dispose() } }
                $serviceKey = $base.OpenSubKey(('{0}\ControlSet{1:D3}\Services' -f $hive,$current))
                try {
                    foreach ($name in $serviceKey.GetSubKeyNames()) {
                        $key = $serviceKey.OpenSubKey($name)
                        try { $services += [pscustomobject]@{ Name=[string]$name; State=[string]$key.GetValue('Start'); Details=[string]$key.GetValue('DisplayName') } } finally { if ($key) { $key.Dispose() } }
                    }
                } finally { if ($serviceKey) { $serviceKey.Dispose() } }
            } finally { if ($base) { $base.Dispose() } }
        } finally {
            & "$env:SystemRoot\System32\reg.exe" unload "HKLM\$hive" | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "Cannot unload inventory hive HKLM\$hive. Close registry tools and unload it before unmounting." }
        }
        [pscustomobject]@{ Features=$features; Packages=$packages; Drivers=$drivers; Apps=$apps; Services=$services } | ConvertTo-Json -Depth 7 -Compress
    }
    exit 0
} catch { [Console]::Error.WriteLine($_.Exception.ToString()); exit 1 }
