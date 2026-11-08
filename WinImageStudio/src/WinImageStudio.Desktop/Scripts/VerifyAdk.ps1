param([Parameter(Mandatory=$true)][string]$Installer)
$ErrorActionPreference = 'Stop'
try {
    Import-Module (Join-Path $PSHOME 'Modules/Microsoft.PowerShell.Security/Microsoft.PowerShell.Security.psd1') -Force -ErrorAction Stop
    $item = Get-Item -LiteralPath $Installer -Force
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Installer must be a regular file.' }
    $signature = Get-AuthenticodeSignature -LiteralPath $item.FullName
    if ($signature.Status -ne 'Valid' -or !$signature.SignerCertificate) { throw ('Invalid ADK signature: ' + $signature.Status) }
    $publisher = $signature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName,$false)
    if ($publisher -ne 'Microsoft Corporation') { throw ('Unexpected ADK publisher: ' + $publisher) }
    Write-Output ('Verified Microsoft ADK bootstrapper: ' + $publisher)
    exit 0
} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
