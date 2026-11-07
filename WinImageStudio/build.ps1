param([switch]$Publish)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
dotnet run --project tests/WinImageStudio.Tests/WinImageStudio.Tests.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
dotnet build src/WinImageStudio.Desktop/WinImageStudio.Desktop.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if ($Publish) {
    dotnet publish src/WinImageStudio.Desktop/WinImageStudio.Desktop.csproj -c Release -r win-x64 --self-contained true -o artifacts/win-x64
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
}
