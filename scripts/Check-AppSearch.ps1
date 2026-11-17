param([string]$AssemblyPath=(Join-Path $PSScriptRoot '..\bin\Nook.search.exe'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$assembly=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$appType=$assembly.GetType('Nook.SearchApp'); $search=$assembly.GetType('Nook.AppSearch'); $prefsType=$assembly.GetType('Nook.Preferences')
$apps=[Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($appType))
foreach($name in @('Visual Studio Code','Code','Discord','Code Editor','Code Editor','Code Other')) { $item=[Activator]::CreateInstance($appType); $item.Name=$name; $item.Target='test:'+ $apps.Count; $apps.Add($item) }
function Check([string]$name,[bool]$ok) { if(!$ok) { throw $name }; 'PASS '+$name }
function Find([string]$text) { ,$search.GetMethod('Find').Invoke($null,[object[]]@($apps,$text)) }
Check 'Empty input gives no suggestions' ((Find '').Count -eq 0)
$found=Find 'code'
Check 'All matches retained with exact before prefix before substring' ($found.Count -eq 4 -and $found[0].Name -eq 'Code' -and $found[1].Name -eq 'Code Editor' -and $found[3].Name -eq 'Visual Studio Code')
Check 'Duplicates removed' (($found | Where-Object Name -eq 'Code Editor').Count -eq 1)
Check 'Wrong Russian keyboard layout resolves Discord' ((Find 'вшысщкв')[0].Name -eq 'Discord')
Check 'Case ignored and substring matched' ((Find 'STUDIO')[0].Name -eq 'Visual Studio Code')
Check 'No fuzzy transliteration aliases' ((Find 'Дискорд').Count -eq 0)
$reader=New-Object IO.StringReader('<Preferences><Size>36</Size></Preferences>')
$serializer=New-Object Xml.Serialization.XmlSerializer($prefsType); $prefs=$serializer.Deserialize($reader); $reader.Dispose()
Check 'Legacy settings receive search defaults' ($prefs.SearchColor -eq '#000000' -and $prefs.SearchWidth -eq 520 -and $prefs.Size -eq 36)
if($null -eq [Windows.Application]::Current) { $application=New-Object Windows.Application; $application.ShutdownMode=[Windows.ShutdownMode]::OnExplicitShutdown }
$windowType=$assembly.GetType('Nook.SearchWindow'); $flags=[Reflection.BindingFlags]'Instance,NonPublic'
Add-Type -ReferencedAssemblies $assembly.Location -TypeDefinition @'
public static class SearchLaunchCheck {
 public static int Count;
 public static string Target;
 public static void Launch(Nook.SearchApp app) { Count++; Target=app.Target; }
}
'@
$callbackType=[Action``1].MakeGenericType($appType)
$callback=[Delegate]::CreateDelegate($callbackType,[SearchLaunchCheck].GetMethod('Launch'))
$window=[Activator]::CreateInstance($windowType,[object[]]@($prefs,$callback))
try {
 $windowType.GetMethod('SetCatalog').Invoke($window,[object[]]@(,$apps)) | Out-Null
 $input=$windowType.GetField('input',$flags).GetValue($window); $list=$windowType.GetField('results',$flags).GetValue($window)
 $input.Text='code'
 Check 'Typing preselects first result and retains matches beyond third' ($list.Items.Count -eq 4 -and $list.SelectedIndex -eq 0)
 $body=$window.Content
 $body.Measure((New-Object Windows.Size(520,[double]::PositiveInfinity)))
 $body.Arrange((New-Object Windows.Rect(0,0,520,$body.DesiredSize.Height))); $body.UpdateLayout()
 Check 'Dropdown narrower than text input' ($list.ActualWidth -lt $input.ActualWidth)
 $inputPlate=$windowType.GetField('plate',$flags).GetValue($window)
 $resultPlate=$windowType.GetField('resultsPlate',$flags).GetValue($window)
 Check 'Suggestions attach below input without gap and keep narrower width' ($inputPlate.Parent -eq $resultPlate.Parent -and $inputPlate.Child -eq $input -and $resultPlate.Child -eq $list -and $resultPlate.Margin.Top -eq 0 -and $resultPlate.ActualWidth -lt $inputPlate.ActualWidth -and $resultPlate.BorderThickness.Top -eq 0)
 Check 'Viewport constrained to three rows' ($list.Height -eq 3*[double]$list.Tag+2)
 $list.SelectedIndex=1
 $windowType.GetMethod('SetCatalog').Invoke($window,[object[]]@(,$apps)) | Out-Null
 Check 'Catalog refresh preserves keyboard selection' ($list.SelectedIndex -eq 1)
 $list.SelectedIndex=2; $input.Text='Discord'
 Check 'New input resets selection to first' ($list.SelectedIndex -eq 0 -and $list.SelectedItem.Name -eq 'Discord')
 $input.Text='code'; $list.SelectedIndex=3; $selectedTarget=$list.SelectedItem.Target
 $windowType.GetMethod('OpenSelected',$flags).Invoke($window,@()) | Out-Null
 Check 'Enter action launches selected result exactly once without real apps' ([SearchLaunchCheck]::Count -eq 1 -and [SearchLaunchCheck]::Target -eq $selectedTarget)
 $input.Clear()
 Check 'Empty input clears selection and hides list' ($list.SelectedIndex -eq -1 -and $list.Visibility -eq [Windows.Visibility]::Collapsed)
 Check 'Empty input hides whole suggestions island' ($resultPlate.Visibility -eq [Windows.Visibility]::Collapsed)
 $windowType.GetMethod('OpenSelected',$flags).Invoke($window,@()) | Out-Null
 Check 'Enter with no result is harmless' (!$window.IsVisible -and [SearchLaunchCheck]::Count -eq 1)
 Check 'Headless checks never show window' (!$window.IsVisible)
} finally { $window.Close() }
$empty=[Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($appType))
$task=$search.GetMethod('ReadAsync').Invoke($null,[object[]]@(,$empty))
if(!$task.Wait(30000)) { throw 'Catalog read timed out' }
$catalog=$task.Result
Check 'Real Windows app catalog populated' ($catalog.Count -gt 10)
'Catalog entries: '+$catalog.Count
$catalog | Select-Object -First 8 Name,Target | Format-Table -AutoSize
