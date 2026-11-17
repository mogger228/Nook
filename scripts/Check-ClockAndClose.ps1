param([string]$AssemblyPath=(Join-Path $PSScriptRoot '..\bin\Nook.clock-close.exe'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$assembly=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
$dockType=$assembly.GetType('Nook.DockWindow')
$prefsType=$assembly.GetType('Nook.Preferences')
$clockType=$assembly.GetType('Nook.DesktopClock')
$oldFolder=$prefsType.GetField('DataFolderOverride').GetValue($null)
$prefsType.GetField('DataFolderOverride').SetValue($null,(Join-Path ([IO.Path]::GetTempPath()) ('Nook.ClockCloseCheck.'+[Guid]::NewGuid().ToString('N'))))
$dock=$null; $clock=$null; $failures=0
function Check([string]$name,[bool]$ok) { if(!$ok) { $script:failures++; Write-Output ('FAIL '+$name) } else { Write-Output ('PASS '+$name) } }
try {
 if($null -eq [Windows.Application]::Current) { $app=New-Object Windows.Application; $app.ShutdownMode=[Windows.ShutdownMode]::OnExplicitShutdown }
 $dock=[Activator]::CreateInstance($dockType,$true)
 foreach($command in @(0xF060,0xF063)) {
  $arguments=[object[]]@([IntPtr]::Zero,0x112,[IntPtr]$command,[IntPtr]::Zero,$false)
  $null=$dockType.GetMethod('WndProc',$flags).Invoke($dock,$arguments)
  Check "Dock consumes system close command $command" ([bool]$arguments[4])
  $arguments=[object[]]@([IntPtr]::Zero,0x112,[IntPtr]$command,[IntPtr]::Zero,$false)
  $null=$assembly.GetType('Nook.DockEdgeTrigger').GetMethod('NoActivate',[Reflection.BindingFlags]'Static,NonPublic').Invoke($null,$arguments)
  Check "Auto-hide trigger consumes system close command $command" ([bool]$arguments[4])
 }
 $arguments=[object[]]@([IntPtr]::Zero,0x10,[IntPtr]::Zero,[IntPtr]::Zero,$false)
 $null=$dockType.GetMethod('WndProc',$flags).Invoke($dock,$arguments)
 Check 'Normal WM_CLOSE remains available for explicit exit' (!$arguments[4])
 $prefs=[Activator]::CreateInstance($prefsType); $prefs.ClockEnabled=$false; $prefs.ClockShowWeather=$false; $prefs.ClockShowDate=$false
 $clock=[Activator]::CreateInstance($clockType,[object[]]@($prefs))
 # A disposed host prevents any Explorer child windows being created by this layout test.
 $hostObject=$clockType.GetField('desktopHost',$flags).GetValue($clock); $hostObject.Dispose()
 $text=$clockType.GetField('text',$flags).GetValue($clock); $prefs.ClockEnabled=$true
 foreach($fontSize in @(32,64,150,160)) {
  $text.FontSize=$fontSize
  $clockType.GetProperty('Width').SetValue($clock,10.0)
  $clockType.GetProperty('Height').SetValue($clock,1.0)
  $null=$clockType.GetMethod('MinuteTick',$flags).Invoke($clock,[object[]]@($null,[EventArgs]::Empty))
  $text.Measure((New-Object Windows.Size([double]::PositiveInfinity,[double]::PositiveInfinity)))
  Check "Minute update resizes stale clock bounds at font $fontSize (width=$($clock.Width), text=$($text.DesiredSize.Width), height=$($clock.Height), textHeight=$($text.DesiredSize.Height))" ($clock.Width -ge [Math]::Min([Windows.SystemParameters]::WorkArea.Width,$text.DesiredSize.Width+8) -and $clock.Height -ge $text.DesiredSize.Height+4)
 }
 Check 'Clock checks created no desktop surfaces' ($null -eq $clockType.GetField('source',$flags).GetValue($clock))
 Check 'Dock checks created no HWND' ($dockType.GetField('handle',$flags).GetValue($dock) -eq [IntPtr]::Zero)
} finally {
 if($null -ne $clock) { $clock.Close() }
 if($null -ne $dock) { $dock.Close() }
 $prefsType.GetField('DataFolderOverride').SetValue($null,$oldFolder)
}
if($failures -gt 0) { throw "$failures regression checks failed" }
