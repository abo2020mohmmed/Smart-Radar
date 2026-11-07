$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$app = Join-Path $PSScriptRoot '../artifacts/win-x64/WinImageStudio.exe'
$process = Start-Process -FilePath $app -PassThru
try {
    $window = $null
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        Start-Sleep -Milliseconds 500
        $process.Refresh()
        if ($process.HasExited) { throw "Desktop exited before opening ($($process.ExitCode))." }
        if ($process.MainWindowHandle -ne 0) {
            $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
            if ($window.Current.Name -like 'WinImageStudio*') { break }
        }
    }
    if (!$window) { throw 'No desktop window appeared.' }
    function Find-Control([string]$id) {
        $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
        $element = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if (!$element) { throw "Missing control: $id" }
        return $element
    }
    foreach ($id in @('Step1','Step2','Step3','Step4','Step5','NextButton','StrongMode','ManualMode')) {
        $control = Find-Control $id
        if ($control.Current.Name -like '*✓*') { throw "False initial completion: $id" }
    }
    $manual = Find-Control 'ManualMode'
    $manual.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 200
    $components = Find-Control 'ComponentsTab'
    if ($components.Current.IsOffscreen) { throw 'Manual path tabs are hidden.' }
    $strong = Find-Control 'StrongMode'
    $strong.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 200
    $strongTab = Find-Control 'StrongTab'
    if ($strongTab.Current.IsOffscreen) { throw 'Strong path tab is hidden.' }
    if ((Find-Control 'Step2').Current.IsEnabled) { throw 'Preparation available without a project.' }
    Write-Output 'PASS WPF startup, numbered steps, mode switching and initial state.'
}
finally {
    $process.Refresh()
    if (!$process.HasExited) { $null = $process.CloseMainWindow(); if (!$process.WaitForExit(10000)) { $process.Kill() } }
    $process.Dispose()
}
