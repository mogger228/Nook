param([string]$AssemblyPath=(Join-Path $PSScriptRoot '..\bin\Nook.island-profiles.exe'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$assembly=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$dockType=$assembly.GetType('Nook.DockWindow')
$prefsType=$assembly.GetType('Nook.Preferences')
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
function Field([string]$name) { $dockType.GetField($name,$flags).GetValue($script:dock) }
function Call([string]$name,[object[]]$arguments) {
 $values=New-Object 'Collections.Generic.List[object]'
 foreach($argument in $arguments) { $values.Add($argument.PSObject.BaseObject) }
 $dockType.GetMethod($name,$flags).Invoke($script:dock,$values.ToArray())
}
function Check([string]$name,[bool]$ok) { if(!$ok) { throw $name }; Write-Output ('PASS '+$name) }
$oldFolder=$prefsType.GetField('DataFolderOverride').GetValue($null)
$prefsType.GetField('DataFolderOverride').SetValue($null,(Join-Path ([IO.Path]::GetTempPath()) ('Nook.ProfileCheck.'+[Guid]::NewGuid().ToString('N'))))
$dock=$null
try {
 $dock=[Activator]::CreateInstance($dockType,$true)
 $prefs=Field 'prefs'; $prefs.Animations=$false; $prefs.AutoHide=$true; $prefs.Size=36
 $prefs.HorizontalIslandClockSize=26; $prefs.HorizontalIslandStatusSize=18
 $prefs.VerticalIslandClockSize=16; $prefs.VerticalIslandStatusSize=13
 $surface=Field 'surface'; $clock=Field 'sideClockText'; $status=Field 'rightStatusIsland'
 $verticalFont=$null
 foreach($position in @('left','bottom','top','left','top','bottom')) {
  $prefs.Left=$position -eq 'left'; $prefs.Top=$position -eq 'top'
  $method=if($prefs.Left) { 'PlaceVerticalDock' } else { 'PlaceHorizontalDock' }
  # Deliberately do not call UpdateSideIslandPresentation: placement must reset the profile itself.
  $null=Call $method @((New-Object Windows.Rect(0,0,1920,1080)),[Windows.Media.Matrix]::Identity,500.0,16.0)
  $surface.Measure((New-Object Windows.Size($dock.Width,$dock.Height)))
  $surface.Arrange((New-Object Windows.Rect(0,0,$dock.Width,$dock.Height))); $surface.UpdateLayout()
  if($prefs.Left -and $null -eq $verticalFont) { $verticalFont=$clock.FontSize }
  $font=if($prefs.Left) { $verticalFont } else { 26 }
  $size=if($prefs.Left) { 13 } else { 18 }
  $orientation=if($prefs.Left) { [Windows.Controls.Orientation]::Vertical } else { [Windows.Controls.Orientation]::Horizontal }
  Check "$position restores independent clock and icon sizes" ($clock.FontSize -eq $font -and (Field 'sideWifiGlyph').Width -eq $size)
  Check "$position restores indicator direction" ((Field 'sideStatusContent').Orientation -eq $orientation)
  Check "$position indicators fit island" ((Field 'sideStatusContent').ActualHeight -le $status.ActualHeight -and (Field 'sideStatusContent').ActualWidth -le $status.ActualWidth)
  $prefs.ClockStatusSize=32
 }
 $prefs.Left=$true; $prefs.VerticalIslandStatusSize=20
 $null=Call 'PlaceVerticalDock' @((New-Object Windows.Rect(0,0,1920,1080)),[Windows.Media.Matrix]::Identity,500.0,16.0)
 $surface.Measure((New-Object Windows.Size($dock.Width,$dock.Height))); $surface.Arrange((New-Object Windows.Rect(0,0,$dock.Width,$dock.Height))); $surface.UpdateLayout()
 Check 'Largest vertical indicators fit measured island height' ((Field 'sideStatusContent').ActualHeight+8 -le $status.ActualHeight)
 Check 'Horizontal sizes remain unchanged by vertical and desktop changes' ($prefs.HorizontalIslandClockSize -eq 26 -and $prefs.HorizontalIslandStatusSize -eq 18)
 Check 'No dock HWND created during checks' ((Field 'handle') -eq [IntPtr]::Zero)
} finally {
 if($null -ne $dock) { $dock.Close() }
 $prefsType.GetField('DataFolderOverride').SetValue($null,$oldFolder)
}
