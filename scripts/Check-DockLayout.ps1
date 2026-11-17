param([string]$AssemblyPath=(Join-Path $PSScriptRoot '..\bin\Nook.edge-dock.exe'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$assembly=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$dockType=$assembly.GetType('Nook.DockWindow')
$prefsType=$assembly.GetType('Nook.Preferences')
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
$checks=New-Object 'Collections.Generic.List[string]'
function Check([string]$name,[bool]$ok) { if(!$ok) { throw $name }; $checks.Add('PASS '+$name) }
function Field([string]$name) { $dockType.GetField($name,$flags).GetValue($script:dock) }
function Call([string]$name,[object[]]$arguments) {
 $values=New-Object 'Collections.Generic.List[object]'
 foreach($argument in $arguments) { $values.Add($argument.PSObject.BaseObject) }
 $dockType.GetMethod($name,$flags).Invoke($script:dock,$values.ToArray())
}
$oldFolder=$prefsType.GetField('DataFolderOverride').GetValue($null)
$prefsType.GetField('DataFolderOverride').SetValue($null,(Join-Path ([IO.Path]::GetTempPath()) ('Nook.LayoutCheck.'+[Guid]::NewGuid().ToString('N'))))
$dock=$null
try {
 $serializer=New-Object Xml.Serialization.XmlSerializer($prefsType)
 $reader=New-Object IO.StringReader('<Preferences><Top>true</Top></Preferences>')
 $legacy=$serializer.Deserialize($reader); $reader.Dispose()
 Check 'Old XML preserves top and leaves new modes disabled' ($legacy.Top -and !$legacy.Left -and !$legacy.AutoHide)
 $legacy.Left=$true; $legacy.Top=$false; $legacy.AutoHide=$true
 $writer=New-Object IO.StringWriter
 $serializer.Serialize($writer,$legacy)
 $reader=New-Object IO.StringReader($writer.ToString()); $copy=$serializer.Deserialize($reader); $reader.Dispose(); $writer.Dispose()
 Check 'Left and autohide survive XML roundtrip' ($copy.Left -and $copy.AutoHide -and !$copy.Top)
 $dock=[Activator]::CreateInstance($dockType,$true)
 $prefs=Field 'prefs'; $prefs.Animations=$false; $prefs.AutoHide=$true; $prefs.Left=$true
 $row=Field 'row'; $surface=Field 'surface'; $plate=Field 'island'
 $clock=Field 'leftClockIsland'; $status=Field 'rightStatusIsland'
 foreach($size in @(36,72)) { foreach($height in @(1080,540)) {
  $prefs.Size=$size; $prefs.ZoomScale=2; $prefs.ClockStatusSize=32; $prefs.HoverWave=$true
  $row.Children.Clear(); $null=Call 'ResetHoverWave' @($true)
  $row.Orientation=[Windows.Controls.Orientation]::Vertical
  for($i=0;$i -lt 50;$i++) { $button=Call 'BaseButton' @('Layout icon',$true); $null=$row.Children.Add($button) }
  $null=Call 'UpdateSideIslandPresentation' @()
  $padding=[Math]::Max(12,($size+8)*1.5+4)
  $length=50*($size+8)+$padding*2+2
  $monitor=New-Object Windows.Rect(0,0,1920,$height)
  $null=Call 'PlaceVerticalDock' @($monitor,[Windows.Media.Matrix]::Identity,[double]$length,[double]$padding)
  $surface.Measure((New-Object Windows.Size($dock.Width,$dock.Height)))
  $surface.Arrange((New-Object Windows.Rect(0,0,$dock.Width,$dock.Height)))
  $surface.UpdateLayout()
  $mainPoint=$plate.TranslatePoint((New-Object Windows.Point(0,0)),$surface)
  $clockPoint=$clock.TranslatePoint((New-Object Windows.Point(0,0)),$surface)
  $statusPoint=$status.TranslatePoint((New-Object Windows.Point(0,0)),$surface)
  Check "Vertical islands order size=$size height=$height" ($clockPoint.Y+$clock.ActualHeight -le $mainPoint.Y -and $statusPoint.Y -ge $mainPoint.Y+$plate.ActualHeight)
  Check "Vertical dock fits monitor size=$size height=$height" ($dock.Height -le $height -and $plate.ActualHeight -gt 0)
  $scroll=$plate.Child -as [Windows.Controls.ScrollViewer]
  Check "Overflow keeps at least one visible slot size=$size height=$height" ($null -ne $scroll -and $scroll.ViewportHeight -ge $size+8-1 -and $scroll.ExtentHeight -gt $scroll.ViewportHeight)
  $presenter=$scroll.Template.FindName('PART_ScrollContentPresenter',$scroll)
  $button=$row.Children[0]; $rowPoint=$row.TranslatePoint((New-Object Windows.Point(0,0)),$surface)
  $band=$dockType.GetProperty('BandHeight',$flags).GetValue($dock)
  Check "Overflow leaves room for outward zoom size=$size height=$height" ($presenter.ActualWidth -ge $button.Width*2-1)
  Check "Overflow icons stay centered in base band size=$size height=$height" ([Math]::Abs($rowPoint.X+$button.Width/2-$mainPoint.X-$band/2) -lt 1)
  Check "Vertical indicators remain within island size=$size height=$height" ((Field 'sideStatusContent').ActualHeight -le $status.ActualHeight)
 } }
 $prefs.Left=$false; $prefs.Top=$false; $row.Orientation=[Windows.Controls.Orientation]::Horizontal
 $null=Call 'UpdateSideIslandPresentation' @()
 $null=Call 'PlaceHorizontalDock' @((New-Object Windows.Rect(0,0,1920,1080)),[Windows.Media.Matrix]::Identity,500.0,16.0)
 Check 'Returning horizontally clears vertical rows' ($surface.RowDefinitions.Count -eq 0 -and [Windows.Controls.Grid]::GetRow($plate) -eq 0 -and [Windows.Controls.Grid]::GetColumn($plate) -eq 2)
 Check 'Horizontal status orientation restored' ((Field 'sideStatusContent').Orientation -eq [Windows.Controls.Orientation]::Horizontal)
 $prefs.EdgeHighlight=$true; $prefs.LeftClockIslandEnabled=$false; $prefs.RightStatusIslandEnabled=$false
 $null=Call 'UpdateSideIslandPresentation' @()
 Check 'Disabled satellites hide their highlights' ((Field 'leftEdgeHighlight').Visibility -eq [Windows.Visibility]::Collapsed -and (Field 'rightEdgeHighlight').Visibility -eq [Windows.Visibility]::Collapsed)
 Check 'Layout checks never create a dock HWND' ((Field 'handle') -eq [IntPtr]::Zero)
} finally {
 if($null -ne $dock) { $dock.Close() }
 $prefsType.GetField('DataFolderOverride').SetValue($null,$oldFolder)
}
$checks | Write-Output
