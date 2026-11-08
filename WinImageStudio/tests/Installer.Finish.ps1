param([Parameter(Mandatory=$true)][string]$SetupPath,[Parameter(Mandatory=$true)][string]$AppName)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$root = Join-Path ([IO.Path]::GetTempPath()) ('WIS Finish Test ' + [Guid]::NewGuid().ToString('N'))
$target = Join-Path $root 'Application'
[IO.Directory]::CreateDirectory($root) | Out-Null
$setup = $null; $app = $null; $finished = $false
try {
    $setup = Start-Process -FilePath ([IO.Path]::GetFullPath($SetupPath)) -ArgumentList @('/SP-','/NORESTART','/LANG=english',('/DIR="'+$target+'"'),'/TASKS="desktopicon,installadk"',('/LOG="'+(Join-Path $root 'setup.log')+'"')) -PassThru
    for ($attempt=0; $attempt -lt 180; $attempt++) {
        Start-Sleep -Milliseconds 500
        $app = @(Get-Process -Name $AppName -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $target ($AppName+'.exe')) -and $_.MainWindowHandle -ne 0 }) | Select-Object -First 1
        if ($app) { break }
        $prefix = [IO.Path]::GetFileNameWithoutExtension($SetupPath)
        $wizardProcesses = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like ($prefix+'*') -and $_.MainWindowHandle -ne 0 })
        foreach ($wizardProcess in $wizardProcesses) {
            $window = [System.Windows.Automation.AutomationElement]::FromHandle($wizardProcess.MainWindowHandle)
            $controls = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
            foreach ($control in $controls) {
                $name = ([string]$control.Current.Name).Replace('&','').Trim()
                if ($name -eq 'I accept the agreement' -and $control.Current.IsEnabled) {
                    try { $control.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
                    catch { $control.GetCurrentPattern([System.Windows.Automation.LegacyIAccessiblePattern]::Pattern).DoDefaultAction() }
                }
            }
            foreach ($control in $controls) {
                $name = ([string]$control.Current.Name).Replace('&','').Trim()
                if ($name -match '^(Next\s*>?|Install|Finish)$' -and $control.Current.IsEnabled -and !$control.Current.IsOffscreen) {
                    if ($name -eq 'Finish') { $finished = $true }
                    $control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); break
                }
            }
        }
    }
    if (!$finished -or !$app) { throw 'Finishing the interactive installer did not open the installed application.' }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle)
    $source = $null
    for ($i=0; $i -lt 40; $i++) {
        $source = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'SourceBox')))
        if ($source) { break }; Start-Sleep -Milliseconds 250
    }
    if (!$source) { throw 'Post-install application did not render its source page.' }
    Write-Output ('PASS Interactive Finish opens '+$AppName+' through the elevated installer context (no CreateProcess 740).')
} catch {
    Write-Host ($_ | Format-List * -Force | Out-String)
    if (Test-Path -LiteralPath (Join-Path $root 'setup.log')) { Write-Host ([IO.File]::ReadAllText((Join-Path $root 'setup.log'))) }
    throw
} finally {
    if ($app) { $null=$app.CloseMainWindow(); if (!$app.WaitForExit(10000)) { $app.Kill(); $app.WaitForExit() }; $app.Dispose() }
    if ($setup) {
        if (!$setup.WaitForExit(3000)) {
            $children = @(Get-CimInstance Win32_Process -Filter ("ParentProcessId="+$setup.Id) | Where-Object { $_.Name -like '*WinImageStudio*Setup*' })
            foreach ($child in $children) { Stop-Process -Id $child.ProcessId -Force -ErrorAction SilentlyContinue }
            $setup.Kill(); $setup.WaitForExit()
        }
        $setup.Dispose()
    }
    $uninstall = Join-Path $target 'unins000.exe'
    if (Test-Path -LiteralPath $uninstall) { $p=Start-Process -FilePath $uninstall -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -PassThru -Wait; $p.Dispose() }
    for($i=0;$i -lt 40 -and (Test-Path -LiteralPath (Join-Path $target ($AppName+'.exe')));$i++){Start-Sleep -Milliseconds 250}
    if (Test-Path -LiteralPath $root) { try { Remove-Item -LiteralPath $root -Recurse -Force } catch { Write-Warning ("Test cleanup retained logs: "+$_.Exception.Message) } }
}
