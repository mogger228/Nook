param([Parameter(Mandatory=$true)][string]$ArchivePath)
$ErrorActionPreference='Stop'
if(Get-Process Nook -ErrorAction SilentlyContinue) { throw 'Закройте Nook перед проверкой отдельной копии.' }
$projectRoot=Split-Path -Parent $PSScriptRoot
$testRoot=Join-Path $projectRoot ('artifacts\Чистая установка с пробелами '+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
Expand-Archive -LiteralPath $ArchivePath -DestinationPath $testRoot
$package=Get-ChildItem -LiteralPath $testRoot -Directory | Select-Object -First 1
$exe=Join-Path $package.FullName 'Nook.exe'
$settings=Join-Path $package.FullName 'data\settings.xml'
$diagnostics=Join-Path $package.FullName 'data\diagnostics.txt'
$process=$null
$expectedEntries=@(& (Join-Path $PSScriptRoot 'Read-Taskband.ps1'))
function Check([string]$name,[bool]$ok) { if(!$ok) { throw $name }; Write-Output ('PASS '+$name) }
function Wait-Settings {
 for($i=0;$i -lt 40;$i++) {
  Start-Sleep -Milliseconds 500
  if($process.HasExited) { throw 'Тестовая копия завершилась до готовности.' }
  if(Test-Path -LiteralPath $settings) { try { [xml]$saved=[IO.File]::ReadAllText($settings); if($saved.Preferences.InitialSetupComplete -eq 'true') { return $saved } } catch [System.Xml.XmlException] { } }
 }
 throw 'Первый запуск не завершился за 20 секунд.'
}
function Stop-TestProcess {
 if($null -ne $script:process -and !$script:process.HasExited) { & $exe --exit | Out-Null; if(!$script:process.WaitForExit(10000)) { throw 'Тестовая копия не завершилась штатно.' } }
}
try {
 Check 'Clean archive contains no pre-existing user settings' (!(Test-Path -LiteralPath $settings))
 $process=Start-Process -FilePath $exe -WorkingDirectory $package.FullName -WindowStyle Hidden -PassThru
 $saved=Wait-Settings
 Check 'First run creates settings in adjacent portable data directory' (Test-Path -LiteralPath $settings)
 Check 'First run uses bottom position' ($saved.Preferences.Left -eq 'false' -and $saved.Preferences.Top -eq 'false')
 Check 'First run uses requested appearance defaults' ($saved.Preferences.Size -eq '36' -and $saved.Preferences.GlassColor -eq '#1A1A1A' -and $saved.Preferences.ClockSize -eq '138' -and $saved.Preferences.SearchRadius -eq '24')
 Check 'No personal scenarios or hotkeys are seeded' (@($saved.Preferences.Scenarios.ScenarioDefinition | Where-Object {$_}).Count -eq 0)
 $pins=@($saved.Preferences.Pins.string | Where-Object {$_})
 Check 'Current Windows taskbar pins actually imported' ($pins.Count -gt 0)
 Check 'All resolved current Windows taskbar entries imported' ($pins.Count -eq $expectedEntries.Count)
 Check 'Imported launch shortcuts belong to this portable copy' (@($pins | Where-Object { !$_.StartsWith((Join-Path $package.FullName 'data\Pins\'),[StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath $_) }).Count -eq 0)
 'Imported pin count: '+$pins.Count
 'Import summary: '+$saved.Preferences.PinImportStatus
 & $exe --status | Out-Null; Start-Sleep -Milliseconds 250
 $state=[IO.File]::ReadAllText($diagnostics)
 Check 'Runtime is loaded, bottom-positioned, search hotkey active' ($state -match 'Loaded=True' -and $state -match 'Position=bottom' -and $state -match 'SearchHotkey=True')
 Stop-TestProcess
 # User changes, including intentionally empty pins, must survive future starts.
 [xml]$saved=[IO.File]::ReadAllText($settings)
 $saved.Preferences.Pins.RemoveAll(); $saved.Preferences.PinDetails.RemoveAll()
 $saved.Preferences.GlassColor='#234567'; $saved.Preferences.HideWindowsTaskbar='false'
 $saved.Save($settings)
 $process=Start-Process -FilePath $exe -WorkingDirectory $package.FullName -WindowStyle Hidden -PassThru
 $null=Wait-Settings
 Start-Sleep -Seconds 2
 [xml]$reopened=[IO.File]::ReadAllText($settings)
 Check 'Second run does not re-import intentionally empty pins' (@($reopened.Preferences.Pins.string | Where-Object {$_}).Count -eq 0)
 Check 'Second run preserves changed appearance' ($reopened.Preferences.GlassColor -eq '#234567')
 & $exe --status | Out-Null; Start-Sleep -Milliseconds 250
 $state=[IO.File]::ReadAllText($diagnostics)
 Check 'Saved Windows taskbar visibility respected' ($state -match 'TaskbarVisible=True')
 Stop-TestProcess
 Check 'Explicit exit shuts down cleanly' $process.HasExited
 'Test directory retained for inspection: '+$package.FullName
} finally { Stop-TestProcess }
