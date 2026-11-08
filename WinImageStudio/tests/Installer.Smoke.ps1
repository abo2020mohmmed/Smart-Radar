$ErrorActionPreference = 'Stop'
$installer = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../artifacts/setup/WinImageStudio-Setup-0.6.1-x64.exe'))
$root = Join-Path ([IO.Path]::GetTempPath()) ('WIS Installer Test ' + [Guid]::NewGuid().ToString('N'))
$installDir = Join-Path $root 'Program Files/WinImageStudio'
$saved = Join-Path $root 'Saved Projects'
[IO.Directory]::CreateDirectory($saved) | Out-Null
$project = Join-Path $saved 'project.json'
$iso = Join-Path $saved 'User-Windows.iso'
[IO.File]::WriteAllText($project, 'user project sentinel')
[IO.File]::WriteAllText($iso, 'user ISO sentinel')
function Run-Setup([string]$log) {
    $parameters = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/RESTARTEXITCODE=3010',('/DIR="' + $installDir + '"'),'/TASKS="desktopicon,installadk"',('/LOG="' + $log + '"'))
    $process = Start-Process -FilePath $installer -ArgumentList $parameters -PassThru -Wait
    try {
        if ($process.ExitCode -notin @(0,3010)) {
            $appLog = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'WinImageStudio/crash.log'
            if (Test-Path -LiteralPath $appLog) { Write-Host ([IO.File]::ReadAllText($appLog)) }
            $details = [IO.File]::ReadAllText($log)
            if ($details.Length -gt 8000) { $details = $details.Substring($details.Length - 8000) }
            throw ('Setup failed: ' + $process.ExitCode + "`n" + $details)
        }
    }
    finally { $process.Dispose() }
}
function Uninstall-TestApp {
    $uninstall = Join-Path $installDir 'unins000.exe'
    if (Test-Path -LiteralPath $uninstall) {
        $process = Start-Process -FilePath $uninstall -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $root 'uninstall.log') + '"')) -PassThru -Wait
        try { if ($process.ExitCode -ne 0) { throw ('Uninstall failed: ' + $process.ExitCode) } } finally { $process.Dispose() }
        for ($i = 0; $i -lt 60 -and (Test-Path -LiteralPath (Join-Path $installDir 'WinImageStudio.exe')); $i++) { Start-Sleep -Milliseconds 500 }
    }
}
try {
    Run-Setup (Join-Path $root 'install.log')
    foreach ($file in @('WinImageStudio.exe','Scripts/ImageTools.ps1','Scripts/VerifyAdk.ps1','Prerequisites/adksetup.exe','unins000.exe')) {
        if (!(Test-Path -LiteralPath (Join-Path $installDir $file))) { throw "Missing installed file $file" }
    }
    $bundled = Join-Path $installDir 'Prerequisites/adksetup.exe'
    if ((Get-FileHash -LiteralPath $bundled -Algorithm SHA256).Hash -ne 'AC6A930FDB5C2980BA5FEFE606D47EDAAFCF5F647B4337411500D158EA77300F') { throw 'Installed ADK differs from supplied bytes.' }
    $desktop = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'WinImageStudio.lnk'
    $menu = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'WinImageStudio/WinImageStudio.lnk'
    if (!(Test-Path -LiteralPath $desktop) -or !(Test-Path -LiteralPath $menu)) { throw 'Shortcuts are missing.' }
    $ownFiles = Join-Path $installDir 'UserProjects'
    [IO.Directory]::CreateDirectory($ownFiles) | Out-Null
    $ownProject = Join-Path $ownFiles 'my-project.json'
    [IO.File]::WriteAllText($ownProject,'preserve files not installed by setup')
    $backup = $bundled + '.original'
    Move-Item -LiteralPath $bundled -Destination $backup
    try {
        [IO.File]::WriteAllText($bundled,'inert tampered fixture')
        $process = Start-Process -FilePath (Join-Path $installDir 'WinImageStudio.exe') -ArgumentList '--install-bundled-adk' -PassThru -Wait
        try { if ($process.ExitCode -eq 0) { throw 'Tampered bundled setup was accepted.' } } finally { $process.Dispose() }
    } finally {
        if (Test-Path -LiteralPath $bundled) { Remove-Item -LiteralPath $bundled -Force }
        Move-Item -LiteralPath $backup -Destination $bundled
    }
    Write-Output 'PASS Setup installs supplied ADK bytes, shared shortcuts, and rejects a tampered prerequisite.'
    & (Join-Path $PSScriptRoot 'Desktop.Smoke.ps1') -ApplicationPath (Join-Path $installDir 'WinImageStudio.exe')
    Run-Setup (Join-Path $root 'upgrade.log')
    if ([IO.File]::ReadAllText($ownProject) -ne 'preserve files not installed by setup') { throw 'Upgrade changed user files.' }
    Uninstall-TestApp
    if (Test-Path -LiteralPath (Join-Path $installDir 'WinImageStudio.exe')) { throw 'Application remained installed.' }
    if ((Test-Path -LiteralPath $desktop) -or (Test-Path -LiteralPath $menu)) { throw 'Owned shortcuts remained after uninstall.' }
    if ([IO.File]::ReadAllText($project) -ne 'user project sentinel' -or [IO.File]::ReadAllText($iso) -ne 'user ISO sentinel' -or [IO.File]::ReadAllText($ownProject) -ne 'preserve files not installed by setup') { throw 'Uninstall removed or changed user files.' }
    $oscdimg = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/Assessment and Deployment Kit/Deployment Tools/amd64/Oscdimg/oscdimg.exe'
    if (!(Test-Path -LiteralPath $oscdimg)) { throw 'Shared Microsoft ADK was removed by uninstall.' }
    Write-Output 'PASS Installed UI/native WIM workflow, upgrade, uninstall, project/ISO preservation and shared ADK preservation.'
} finally {
    Uninstall-TestApp
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
