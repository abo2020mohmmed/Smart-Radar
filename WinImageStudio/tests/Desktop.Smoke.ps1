$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$app = Join-Path $PSScriptRoot '../artifacts/win-x64/WinImageStudio.exe'
$process = $null
$window = $null
function Start-Ui([string]$projectFolder = '') {
    if ($projectFolder) { $script:process = Start-Process -FilePath $app -ArgumentList @('--project', ('"' + $projectFolder + '"')) -PassThru }
    else { $script:process = Start-Process -FilePath $app -PassThru }
    $script:window = $null
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        Start-Sleep -Milliseconds 500
        $script:process.Refresh()
        if ($script:process.HasExited) { throw "Desktop exited early ($($script:process.ExitCode))." }
        if ($script:process.MainWindowHandle -ne 0) {
            $script:window = [System.Windows.Automation.AutomationElement]::FromHandle($script:process.MainWindowHandle)
            if ($script:window.Current.Name -like 'WinImageStudio*') { return }
        }
    }
    throw 'No desktop window appeared.'
}
function Stop-Ui {
    if (!$script:process) { return }
    $script:process.Refresh()
    if (!$script:process.HasExited) {
        $null = $script:process.CloseMainWindow()
        if (!$script:process.WaitForExit(10000)) { $script:process.Kill(); $script:process.WaitForExit() }
    }
    $script:process.Dispose(); $script:process = $null
}
function Find-Control([string]$id) {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    $element = $script:window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (!$element) { throw "Missing control: $id" }
    return $element
}
function Wait-Control([string]$id) {
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        try { $control = Find-Control $id; if (!$control.Current.IsOffscreen) { return $control } } catch { }
        Start-Sleep -Milliseconds 500
    }
    throw "Visible control did not appear: $id"
}
function Invoke-Primary {
    $control = $null
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        $candidate = Find-Control 'NextButton'
        if ($candidate.Current.IsEnabled) { $control = $candidate; break }
        Start-Sleep -Milliseconds 500
    }
    if (!$control) { throw 'Primary action remained disabled.' }
    $control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Dism-Run([string[]]$parameters) {
    $dism = Join-Path $env:SystemRoot 'System32/dism.exe'
    $output = & $dism @parameters 2>&1
    if ($LASTEXITCODE -ne 0) { throw "DISM $LASTEXITCODE : $output" }
}
try {
    Start-Ui
    foreach ($id in @('Step1','Step2','Step3','Step4','Step5','NextButton','BackButton','StrongMode','ManualMode','SourceBox')) {
        $control = Find-Control $id
        if ($control.Current.Name.Contains([string][char]0x2713)) { throw "False initial completion: $id" }
    }
    if ((Find-Control 'NextButton').Current.IsEnabled) { throw 'Empty source permits execution.' }
    if ((Find-Control 'BackButton').Current.IsEnabled) { throw 'Back enabled on first page.' }
    $manual = Find-Control 'ManualMode'
    $manual.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    if (!$manual.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) { throw 'Manual mode not selected.' }
    Write-Output 'PASS Wizard startup, single primary footer, no false completions and source validation.'
} finally { Stop-Ui }

# A tiny inert WIM fixture, not a Windows installation. Only this test-owned mount is serviced.
$root = Join-Path ([IO.Path]::GetTempPath()) ('WIS Wizard UI ' + [Guid]::NewGuid().ToString('N'))
$capture = Join-Path $root 'capture'
$mount = Join-Path $root 'mount'
$wim = Join-Path $root 'install.wim'
$mounted = $false
try {
    [IO.Directory]::CreateDirectory($capture) | Out-Null
    [IO.Directory]::CreateDirectory($mount) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $root 'logs')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $capture 'fixture.txt'), 'inert wizard test')
    Dism-Run @('/English','/Capture-Image',('/ImageFile:' + $wim),('/CaptureDir:' + $capture),'/Name:WizardFixture','/Compress:fast')
    Dism-Run @('/English','/Mount-Image',('/ImageFile:' + $wim),'/Index:1',('/MountDir:' + $mount))
    $mounted = $true
    $journal = Join-Path $root 'project.json'
    $project = @{
        SchemaVersion = 1; Id = [Guid]::NewGuid().ToString('N'); Root = $root; Source = $wim; ImageSource = $wim
        WorkingWim = $wim; MountPath = $mount; MediaPath = $null; State = 'Mounted'; WorkflowStage = 3; WorkflowMode = 'Strong'
        Image = @{ImageIndex=1; ImageName='Wizard fixture'; ImageDescription='Inert UI fixture'; Architecture='amd64'; Version='10.0'; EditionId='Professional'; DefaultLanguage='en-US'}
    }
    [IO.File]::WriteAllText($journal, ($project | ConvertTo-Json -Depth 5))
    Start-Ui $root
    $null = Wait-Control 'ReviewResultBox'
    Invoke-Primary
    $saved = $false
    for ($attempt = 0; $attempt -lt 120; $attempt++) {
        Start-Sleep -Milliseconds 500
        $state = [IO.File]::ReadAllText($journal) | ConvertFrom-Json
        if ($state.State -eq 'Committed' -and $state.WorkflowStage -eq 4) { $saved = $true; break }
    }
    if (!$saved) { throw "Primary did not commit WIM: $([IO.File]::ReadAllText($journal))" }
    $mounted = $false
    $null = Wait-Control 'OutputHint'
    Invoke-Primary # WIM-only output finishes explicitly; it never claims an ISO exists.
    $null = Wait-Control 'OutputPathBox'
    if (!(Test-Path -LiteralPath $wim)) { throw 'Saved WIM is missing.' }
    Write-Output 'PASS Primary Next commits a real mounted WIM, persists Committed, opens output and finishes WIM-only result.'
} finally {
    Stop-Ui
    if ($mounted) {
        $ownMount = @(Get-WindowsImage -Mounted | Where-Object { $_.Path -eq $mount })
        if ($ownMount.Count) { Dism-Run @('/English','/Unmount-Image',('/MountDir:' + $mount),'/Discard') }
        $mounted = $false
    }
    if (!$mounted -and (Test-Path -LiteralPath $root)) { Remove-Item -LiteralPath $root -Recurse -Force }
}
