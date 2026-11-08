param([string]$ApplicationPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$app = if ($ApplicationPath) { $ApplicationPath } else { [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../artifacts/lite-win-x64/WinImageStudio.Lite.exe')) }
$root = Join-Path ([IO.Path]::GetTempPath()) ('WIS Lite UI '+[Guid]::NewGuid().ToString('N'))
$capture=Join-Path $root 'capture'; $mount=Join-Path $root 'mount'; $media=Join-Path $root 'media'; $wim=Join-Path $root 'install.wim'; $journal=Join-Path $root 'project.json'
$process=$null; $disk=$null; $iso=$null
function Dism-Run([string[]]$parameters){$text=& (Join-Path $env:SystemRoot 'System32/dism.exe') @parameters 2>&1; if($LASTEXITCODE -ne 0){throw "DISM failed: $text"}}
function Find-Ui([string]$id){$item=$script:window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$id))); return $item}
try {
    foreach($dir in @($root,$capture,$mount,(Join-Path $root 'logs'),(Join-Path $media 'boot'),(Join-Path $media 'sources'))){[IO.Directory]::CreateDirectory($dir)|Out-Null}
    [IO.File]::WriteAllText((Join-Path $capture 'fixture.txt'),'inert Lite archive fixture, not a Windows installation')
    Dism-Run @('/English','/Capture-Image',('/ImageFile:'+$wim),('/CaptureDir:'+$capture),'/Name:LiteFixture','/Compress:fast')
    [IO.File]::WriteAllText((Join-Path $root 'source.iso'),'inert source marker for a saved fixture')
    $boot=New-Object byte[] 4096; $boot[510]=0x55; $boot[511]=0xAA; [IO.File]::WriteAllBytes((Join-Path $media 'boot/etfsboot.com'),$boot)
    Copy-Item -LiteralPath $wim -Destination (Join-Path $media 'sources/install.wim')
    $data=@{SchemaVersion=1;Id=[Guid]::NewGuid().ToString('N');Root=$root;Source=(Join-Path $root 'source.iso');WorkingWim=$wim;MountPath=$mount;MediaPath=$media;State='Committed';WorkflowStage=4;WorkflowMode='Lite';Image=@{ImageIndex=1;ImageName='Home fixture';ImageDescription='Inert fixture';Architecture='amd64';Version='10.0.19045';EditionId='Core';DefaultLanguage='en-US'}}
    [IO.File]::WriteAllText($journal,($data|ConvertTo-Json -Depth 5))
    $process=Start-Process -FilePath $app -ArgumentList @('--project',('"'+$root+'"')) -PassThru
    $script:window=$null; $button=$null
    for($i=0;$i -lt 80;$i++){
        Start-Sleep -Milliseconds 250; $process.Refresh()
        if($process.HasExited){throw 'Lite exited early.'}
        if($process.MainWindowHandle -ne 0){$script:window=[System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle);$button=Find-Ui 'NextButton';$edition=Find-Ui 'EditionBox';if($button -and $button.Current.IsEnabled -and $edition -and !$edition.Current.IsOffscreen){break}}
    }
    if(!$button -or !$button.Current.IsEnabled){throw 'Lite resume action is missing.'}
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $done=$false
    for($i=0;$i -lt 240;$i++){
        Start-Sleep -Milliseconds 500
        $state=[IO.File]::ReadAllText($journal)|ConvertFrom-Json
        if($state.WorkflowStage -eq 5 -and $state.OutputIso){$iso=$state.OutputIso;$done=$true;break}
        $errorElement=Find-Ui 'ErrorText';if($errorElement -and !$errorElement.Current.IsOffscreen){throw ('Lite failed: '+$errorElement.Current.Name)}
    }
    if(!$done -or !(Test-Path -LiteralPath $iso)){throw 'Lite did not automatically finish the saved fixture.'}
    $disk=Mount-DiskImage -ImagePath $iso -PassThru
    $volume=@($disk|Get-Volume|Where-Object DriveLetter)[0]
    if(!$volume){throw 'Final ISO has no readable volume.'}
    $payload=Join-Path ($volume.DriveLetter+':\') 'sources/install.esd'
    if(!(Test-Path -LiteralPath $payload)){throw 'Final ISO lacks ESD payload.'}
    Dism-Run @('/English','/Get-WimInfo',('/WimFile:'+$payload))
    Write-Output 'PASS Lite actual UI single action -> native recovery ESD -> oscdimg ISO -> mounted ISO payload validation (inert saved-image fixture).'
} finally {
    if($disk){Dismount-DiskImage -ImagePath $iso|Out-Null}
    if($process){if(!$process.HasExited){$null=$process.CloseMainWindow();if(!$process.WaitForExit(10000)){$process.Kill();$process.WaitForExit()}};$process.Dispose()}
    if($iso -and (Test-Path -LiteralPath $iso)){Remove-Item -LiteralPath $iso -Force}
    $marker=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'WinImageStudioLite/last-project.txt'
    if((Test-Path -LiteralPath $marker) -and [IO.File]::ReadAllText($marker) -eq $root){Remove-Item -LiteralPath $marker -Force}
    if(Test-Path -LiteralPath $root){Remove-Item -LiteralPath $root -Recurse -Force}
}
