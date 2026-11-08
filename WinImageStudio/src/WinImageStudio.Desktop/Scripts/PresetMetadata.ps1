function Get-WisPackageMetadata {
    [CmdletBinding()]
    param([Parameter(Mandatory=$true)][string]$MountPath, [Parameter(Mandatory=$true)][string]$PackageName)
    $parts = $PackageName.Split('~')
    $category = 'Other'
    $language = ''
    if ($parts.Length -ge 5 -and $parts[0] -in @('Microsoft-Windows-Client-LanguagePack-Package','Microsoft-Windows-LanguagePack-Package')) {
        $category = 'LanguagePack'
        if ($parts[3] -match '^[a-z]{2,3}(-[a-z0-9]{2,8})+$') { $language = $parts[3] }
    } elseif ($parts[0] -in @('Microsoft-Windows-Defender-Client-Package','Microsoft-Windows-Defender-Default-Definitions-Package')) {
        $category = 'Defender'
    }
    if ($category -eq 'Other') { return [pscustomobject]@{ Category=$category; Language=$language; Removable=$false; Reason='Not in the automatic package-removal catalog.' } }
    if ([IO.Path]::GetFileName($PackageName) -ne $PackageName -or $PackageName.Contains('\') -or $PackageName.Contains('/') -or $PackageName.Contains(':')) {
        throw 'Package metadata name must be a filename, not a path.'
    }
    $folder = [IO.Path]::Combine($MountPath, 'Windows', 'servicing', 'Packages')
    $manifest = [IO.Path]::Combine($folder, $PackageName + '.mum')
    $removable = $false
    $reason = 'Package manifest is missing; auto-removal is not allowed.'
    if ([IO.File]::Exists($manifest)) {
        $reader = $null
        try {
            if (([IO.File]::GetAttributes($manifest) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Package manifest is a reparse point.' }
            $settings = New-Object Xml.XmlReaderSettings
            $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
            $settings.XmlResolver = $null
            $settings.MaxCharactersInDocument = 4194304
            $reader = [Xml.XmlReader]::Create($manifest, $settings)
            $document = New-Object Xml.XmlDocument
            $document.XmlResolver = $null
            $document.Load($reader)
            $nodes = $document.SelectNodes("//*[local-name()='package']")
            if ($nodes.Count -eq 1) {
                $permanence = $nodes[0].GetAttribute('permanence')
                $removable = $permanence -ne 'permanent'
                $reason = if ($removable) { 'Manifest does not mark this cataloged package permanent; DISM has final authority.' } else { 'Permanent package; auto-removal is blocked.' }
            } else { $reason = 'Unrecognized package metadata; auto-removal is blocked.' }
        } catch { $removable = $false; $reason = 'Cannot safely inspect package metadata: ' + $_.Exception.Message }
        finally { if ($reader) { $reader.Dispose() } }
    }
    [pscustomobject]@{ Category=$category; Language=$language; Removable=$removable; Reason=$reason }
}

function Read-WisOfflineHive {
    [CmdletBinding()]
    param([string]$MountPath, [ValidatePattern('^[a-fA-F0-9]{32}$')][string]$ProjectId, [ValidateSet('System','Software')][string]$Hive, [scriptblock]$Reader)
    if (!$ProjectId) { throw 'Project ID is required for offline hive queries.' }
    $name = 'WIS_' + $ProjectId + '_' + $Hive
    $folder = [IO.Path]::Combine($MountPath, 'Windows', 'System32', 'config')
    $file = [IO.Path]::Combine($folder, $Hive.ToUpperInvariant())
    $reg = Join-Path $env:SystemRoot 'System32/reg.exe'
    & $reg load "HKLM\$name" $file | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Cannot load offline $Hive hive." }
    try {
        $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
        try {
            $key = $base.OpenSubKey($name)
            if (!$key) { throw 'Loaded hive cannot be queried.' }
            try { & $Reader $key } finally { $key.Dispose() }
        } finally { $base.Dispose() }
    } finally {
        & $reg unload "HKLM\$name" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Cannot unload HKLM\$name. Close registry tools, then recover this project." }
    }
}

function Get-WisWin32Programs {
    param([Microsoft.Win32.RegistryKey]$Root)
    $programs = New-Object 'System.Collections.Generic.List[object]'
    foreach ($path in @('Microsoft\Windows\CurrentVersion\Uninstall','Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall')) {
        $parent = $Root.OpenSubKey($path)
        if (!$parent) { continue }
        try {
            foreach ($name in $parent.GetSubKeyNames()) {
                $item = $parent.OpenSubKey($name)
                if (!$item) { continue }
                try {
                    $display = [string]$item.GetValue('DisplayName')
                    if (!$display) { continue }
                    $programs.Add([pscustomobject]@{Name=$display; State=[string]$item.GetValue('DisplayVersion'); Details=([string]$item.GetValue('Publisher') + ' | ' + [string]$item.GetValue('InstallLocation') + ' | ' + $path); Removable=$false})
                } finally { $item.Dispose() }
            }
        } finally { $parent.Dispose() }
    }
    $programs.ToArray()
}

function Get-WisOfflineRegistryProfile {
    [CmdletBinding()]
    param([string]$MountPath, [string]$ProjectId)
    $system = Read-WisOfflineHive -MountPath $MountPath -ProjectId $ProjectId -Hive System -Reader {
        param($root)
        $select = $root.OpenSubKey('Select')
        if (!$select) { throw 'SYSTEM Select is missing.' }
        try { $current = [int]$select.GetValue('Current') } finally { $select.Dispose() }
        if ($current -lt 1 -or $current -gt 999) { throw 'Invalid current control set.' }
        $control = 'ControlSet{0:D3}' -f $current
        $services = New-Object 'System.Collections.Generic.List[object]'
        $serviceKey = $root.OpenSubKey($control + '\Services')
        if (!$serviceKey) { throw 'Offline services key is missing.' }
        try {
            foreach ($name in $serviceKey.GetSubKeyNames()) {
                $key = $serviceKey.OpenSubKey($name)
                try {
                    $start = [int]$key.GetValue('Start', 3)
                    $type = [int]$key.GetValue('Type', 0)
                    $services.Add([pscustomobject]@{ Name=[string]$name; State=[string]$start; Details=[string]$key.GetValue('DisplayName'); Removable=($start -ge 2 -and $start -le 4 -and ($type -band 3) -eq 0) })
                } finally { if ($key) { $key.Dispose() } }
            }
        } finally { $serviceKey.Dispose() }
        $baseLanguage = ''
        $nls = $root.OpenSubKey($control + '\Control\Nls\Language')
        try {
            if ($nls) {
                $raw = [string]$nls.GetValue('InstallLanguage')
                if ($raw) {
                    try { $baseLanguage = [Globalization.CultureInfo]::GetCultureInfo([Convert]::ToInt32($raw,16)).Name } catch { $baseLanguage = '' }
                }
            }
        } finally { if ($nls) { $nls.Dispose() } }
        $languages = @()
        $mui = $root.OpenSubKey($control + '\Control\MUI\UILanguages')
        try { if ($mui) { $languages = @($mui.GetSubKeyNames()) } } finally { if ($mui) { $mui.Dispose() } }
        if ($baseLanguage) { $languages += $baseLanguage }
        [pscustomobject]@{ Services=@($services.ToArray()); BaseLanguage=$baseLanguage; InstalledLanguages=@($languages | Sort-Object -Unique) }
    }
    $software = Read-WisOfflineHive -MountPath $MountPath -ProjectId $ProjectId -Hive Software -Reader {
        param($root)
        $key = $root.OpenSubKey('Microsoft\Windows NT\CurrentVersion')
        if (!$key) { throw 'Offline Windows version key is missing.' }
        try { [pscustomobject]@{ EditionId=[string]$key.GetValue('EditionID'); InstallationType=[string]$key.GetValue('InstallationType'); Build=[string]$key.GetValue('CurrentBuildNumber'); Win32Programs=@(Get-WisWin32Programs -Root $root) } }
        finally { $key.Dispose() }
    }
    [pscustomobject]@{ Services=$system.Services; BaseLanguage=$system.BaseLanguage; InstalledLanguages=$system.InstalledLanguages; EditionId=$software.EditionId; InstallationType=$software.InstallationType; Build=$software.Build; Win32Programs=@($software.Win32Programs) }
}
