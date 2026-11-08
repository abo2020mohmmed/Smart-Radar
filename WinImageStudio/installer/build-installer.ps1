$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$payload = Join-Path $root 'artifacts/win-x64'
$adk = Join-Path $payload 'Prerequisites/adksetup.exe'
$expected = 'AC6A930FDB5C2980BA5FEFE606D47EDAAFCF5F647B4337411500D158EA77300F'
if (!(Test-Path -LiteralPath $adk)) { throw 'Publish the application with the supplied ADK first.' }
if ((Get-FileHash -LiteralPath $adk -Algorithm SHA256).Hash -ne $expected) { throw 'Bundled ADK differs from the supplied file.' }
& (Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe') -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $payload 'Scripts/VerifyAdk.ps1') -Installer $adk
if ($LASTEXITCODE -ne 0) { throw 'Bundled ADK Microsoft signature validation failed. Installer build stopped.' }
$compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
if (!$compiler) {
    foreach ($version in @('6','7')) {
        $candidate = Join-Path ${env:ProgramFiles(x86)} ('Inno Setup ' + $version + '/ISCC.exe')
        if (Test-Path -LiteralPath $candidate) { $compiler = $candidate; break }
    }
}
if (!$compiler) { throw 'Inno Setup compiler is required to build the installer.' }
$compilerLicense = Join-Path ([IO.Path]::GetDirectoryName($compiler)) 'License.txt'
if (Test-Path -LiteralPath $compilerLicense) {
    [IO.Directory]::CreateDirectory((Join-Path $payload 'third-party')) | Out-Null
    Copy-Item -LiteralPath $compilerLicense -Destination (Join-Path $payload 'third-party/InnoSetup-LICENSE.txt')
}
& $compiler /Qp (Join-Path $PSScriptRoot 'WinImageStudio.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
$installer = Join-Path $root 'artifacts/setup/WinImageStudio-Setup-0.6.1-x64.exe'
if (!(Test-Path -LiteralPath $installer)) { throw 'Installer output is missing.' }
$litePayload = Join-Path $root 'artifacts/lite-win-x64'
$liteAdk = Join-Path $litePayload 'Prerequisites/adksetup.exe'
if ((Get-FileHash -LiteralPath $liteAdk -Algorithm SHA256).Hash -ne $expected) { throw 'Lite bundled ADK differs from the supplied file.' }
if (Test-Path -LiteralPath $compilerLicense) { Copy-Item -LiteralPath $compilerLicense -Destination (Join-Path $litePayload 'third-party/InnoSetup-LICENSE.txt') }
& $compiler /Qp (Join-Path $PSScriptRoot 'WinImageStudio.Lite.iss')
if ($LASTEXITCODE -ne 0) { throw 'Lite installer compilation failed.' }
$lines = foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root 'artifacts/setup') -Filter '*.exe') {
    (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $file.Name
}
[IO.File]::WriteAllText((Join-Path $root 'artifacts/setup/SHA256SUMS.txt'), (($lines -join "`n") + "`n"))
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'INSTALL.ar.txt') -Destination (Join-Path $root 'artifacts/setup/INSTALL.ar.txt')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LITE.ar.txt') -Destination (Join-Path $root 'artifacts/setup/LITE.ar.txt')
Write-Output ('Installer: ' + $installer)
