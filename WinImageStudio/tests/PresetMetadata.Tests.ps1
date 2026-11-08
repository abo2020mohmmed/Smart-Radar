$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '../src/WinImageStudio.Desktop/Scripts/PresetMetadata.ps1')
$root = Join-Path ([IO.Path]::GetTempPath()) ('WisMetadata-' + [Guid]::NewGuid().ToString('N'))
$folder = [IO.Path]::Combine($root,'Windows','servicing','Packages')
[void][IO.Directory]::CreateDirectory($folder)
$script:passed = 0; $script:failed = 0
function Case([string]$Name,[scriptblock]$Body) {
    try { & $Body; $script:passed++; Write-Output ('PASS ' + $Name) }
    catch { $script:failed++; [Console]::Error.WriteLine(('FAIL ' + $Name + ': ' + $_.Exception.ToString())) }
}
function Assert($Condition) { if (!$Condition) { throw 'Metadata assertion failed.' } }
function Manifest([string]$Name,[string]$Xml) { [IO.File]::WriteAllText([IO.Path]::Combine($folder,$Name+'.mum'),$Xml) }
$language = 'Microsoft-Windows-Client-LanguagePack-Package~token~amd64~fr-FR~10.0.1.0'
$defender = 'Microsoft-Windows-Defender-Client-Package~token~amd64~~10.0.1.0'
try {
    Case 'Permanent language pack is blocked' {
        Manifest $language '<assembly xmlns="urn:schemas-microsoft-com:asm.v3"><package permanence="permanent"/></assembly>'
        $m = Get-WisPackageMetadata $root $language
        Assert (!$m.Removable); Assert ($m.Language -eq 'fr-FR'); Assert ($m.Category -eq 'LanguagePack')
    }
    Case 'Cataloged nonpermanent package is eligible for DISM, not force removal' {
        Manifest $language '<assembly><package identifier="LanguagePack"/></assembly>'
        $m = Get-WisPackageMetadata $root $language
        Assert ($m.Removable); Assert ($m.Reason.Contains('DISM'))
    }
    Case 'Permanent Defender package is reported as protected' {
        Manifest $defender '<assembly><package permanence="permanent"/></assembly>'
        $m = Get-WisPackageMetadata $root $defender
        Assert (!$m.Removable); Assert ($m.Category -eq 'Defender')
    }
    Case 'Missing Defender manifest is not guessed removable' {
        [IO.File]::Delete([IO.Path]::Combine($folder,$defender+'.mum'))
        Assert (!(Get-WisPackageMetadata $root $defender).Removable)
    }
    Case 'Unknown package is outside the automatic removal catalog' {
        $m = Get-WisPackageMetadata $root 'Microsoft-Windows-ServicingStack-Package~token~amd64~~10.0.1.0'
        Assert (!$m.Removable); Assert ($m.Category -eq 'Other')
    }
    Case 'Malformed XML is blocked' {
        Manifest $language '<assembly><package'
        Assert (!(Get-WisPackageMetadata $root $language).Removable)
    }
    Case 'DTD and external entity declarations are prohibited' {
        Manifest $language '<!DOCTYPE assembly [<!ENTITY external SYSTEM "file:///not-a-real-file">]><assembly><package>&external;</package></assembly>'
        Assert (!(Get-WisPackageMetadata $root $language).Removable)
    }
    Case 'Ambiguous multiple package nodes are blocked' {
        Manifest $language '<assembly><package/><package/></assembly>'
        Assert (!(Get-WisPackageMetadata $root $language).Removable)
    }
    Case 'Unrecognized metadata structure is blocked' {
        Manifest $language '<assembly><other/></assembly>'
        Assert (!(Get-WisPackageMetadata $root $language).Removable)
    }
    Case 'A package path cannot escape the metadata directory' {
        $threw = $false
        try { Get-WisPackageMetadata $root 'Microsoft-Windows-Client-LanguagePack-Package~token~amd64~fr-FR~../../outside' | Out-Null } catch { $threw = $true }
        Assert ($threw)
    }
    Case 'Invalid project IDs are rejected before loading any hive' {
        $threw = $false
        try { Read-WisOfflineHive -MountPath $root -ProjectId 'invalid/id' -Hive System -Reader {} | Out-Null } catch { $threw = $true }
        Assert ($threw)
    }
    if ($env:OS -eq 'Windows_NT') {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        try { $admin = ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) }
        finally { $identity.Dispose() }
        if ($admin) {
            Case 'Native offline hive query uses selected control set and unloads both fixture hives' {
                $id = [Guid]::NewGuid().ToString('N')
                $userPath = 'Software\WinImageStudio_PresetTest_' + $id
                $config = [IO.Path]::Combine($root,'Windows','System32','config')
                [void][IO.Directory]::CreateDirectory($config)
                $reg = Join-Path $env:SystemRoot 'System32/reg.exe'
                $hkcu = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryView]::Registry64)
                try {
                    $system = $hkcu.CreateSubKey($userPath + '\System')
                    try {
                        $key = $system.CreateSubKey('Select'); try { $key.SetValue('Current',2,[Microsoft.Win32.RegistryValueKind]::DWord) } finally { $key.Dispose() }
                        foreach ($service in @(@{Name='Spooler';Start=2;Type=16},@{Name='KernelFixture';Start=0;Type=1})) {
                            $key = $system.CreateSubKey('ControlSet002\Services\' + $service.Name)
                            try { $key.SetValue('Start',$service.Start,[Microsoft.Win32.RegistryValueKind]::DWord); $key.SetValue('Type',$service.Type,[Microsoft.Win32.RegistryValueKind]::DWord); $key.SetValue('DisplayName',$service.Name) } finally { $key.Dispose() }
                        }
                        $key = $system.CreateSubKey('ControlSet002\Control\Nls\Language'); try { $key.SetValue('InstallLanguage','0409') } finally { $key.Dispose() }
                        foreach ($tag in @('en-US','ar-SA')) { $key = $system.CreateSubKey('ControlSet002\Control\MUI\UILanguages\' + $tag); $key.Dispose() }
                        $system.Flush()
                    } finally { $system.Dispose() }
                    $software = $hkcu.CreateSubKey($userPath + '\Software')
                    try {
                        $key = $software.CreateSubKey('Microsoft\Windows NT\CurrentVersion')
                        try { $key.SetValue('EditionID','Professional'); $key.SetValue('InstallationType','Client'); $key.SetValue('CurrentBuildNumber','19045') } finally { $key.Dispose() }
                        foreach ($prefix in @('Microsoft\Windows\CurrentVersion\Uninstall','Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall')) {
                            $program = $software.CreateSubKey($prefix + '\FixtureApp')
                            try { $program.SetValue('DisplayName','Fixture program'); $program.SetValue('DisplayVersion','1.2'); $program.SetValue('Publisher','Fixture publisher') } finally { $program.Dispose() }
                        }
                        $software.Flush()
                    } finally { $software.Dispose() }
                    foreach ($hive in @('System','Software')) {
                        & $reg save ('HKCU\' + $userPath + '\' + $hive) ([IO.Path]::Combine($config,$hive.ToUpperInvariant())) /y | Out-Null
                        if ($LASTEXITCODE -ne 0) { throw 'Cannot create isolated fixture hive.' }
                    }
                    $profile = Get-WisOfflineRegistryProfile -MountPath $root -ProjectId $id
                    Assert ($profile.EditionId -eq 'Professional'); Assert ($profile.InstallationType -eq 'Client'); Assert ($profile.Build -eq '19045')
                    Assert ($profile.BaseLanguage -eq 'en-US'); Assert (@($profile.InstalledLanguages).Contains('ar-SA'))
                    Assert (@($profile.Win32Programs).Count -eq 2)
                    Assert (@($profile.Win32Programs | Where-Object Name -eq 'Fixture program').Count -eq 2)
                    Assert (@($profile.Services).Count -eq 2)
                    Assert ([bool](@($profile.Services | Where-Object Name -eq 'Spooler')[0].Removable))
                    Assert (!([bool](@($profile.Services | Where-Object Name -eq 'KernelFixture')[0].Removable)))
                    $hklm = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryView]::Registry64)
                    try {
                        foreach ($hive in @('System','Software')) {
                            $check = $hklm.OpenSubKey('WIS_' + $id + '_' + $hive)
                            try { Assert ($null -eq $check) } finally { if ($check) { $check.Dispose() } }
                        }
                    } finally { $hklm.Dispose() }
                } finally {
                    $cleanup = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryView]::Registry64)
                    try {
                        foreach ($hive in @('System','Software')) {
                            $mountName = 'WIS_' + $id + '_' + $hive
                            $remaining = $cleanup.OpenSubKey($mountName)
                            $exists = $null -ne $remaining
                            if ($remaining) { $remaining.Dispose() }
                            if ($exists) { & $reg unload ('HKLM\' + $mountName) | Out-Null; if ($LASTEXITCODE -ne 0) { throw 'Fixture hive cleanup failed.' } }
                        }
                    } finally { $cleanup.Dispose(); $hkcu.DeleteSubKeyTree($userPath,$false); $hkcu.Dispose() }
                }
            }
        } else { Write-Output 'SKIP native fixture-hive integration: Administrator is required.' }
    }
    if ($env:OS -eq 'Windows_NT') {
        Case 'Hidden System manifest is read without PowerShell provider filtering' {
            Manifest $language '<assembly><package/></assembly>'
            $path = [IO.Path]::Combine($folder,$language+'.mum')
            [IO.File]::SetAttributes($path,([IO.FileAttributes]::Hidden -bor [IO.FileAttributes]::System -bor [IO.FileAttributes]::ReadOnly))
            Assert ((Get-WisPackageMetadata $root $language).Removable)
        }
    }
} finally {
    foreach ($file in [IO.Directory]::EnumerateFiles($root,'*',[IO.SearchOption]::AllDirectories)) { [IO.File]::SetAttributes($file,[IO.FileAttributes]::Normal) }
    [IO.Directory]::Delete($root,$true)
}
Write-Output ('Package metadata: {0} passed; {1} failed.' -f $script:passed,$script:failed)
if ($script:failed -gt 0) { exit 1 }
exit 0
