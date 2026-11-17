param([string]$AssemblyPath=(Join-Path $PSScriptRoot '..\bin\Nook.scenarios.exe'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ScenarioNativeChecks {
 [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
 [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr window,int id);
}
'@
$assembly=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$dockType=$assembly.GetType('Nook.DockWindow'); $prefsType=$assembly.GetType('Nook.Preferences')
$scenarioType=$assembly.GetType('Nook.ScenarioDefinition'); $actionType=$assembly.GetType('Nook.ScenarioAction')
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
function Field([string]$name) { $dockType.GetField($name,$flags).GetValue($script:dock) }
function Call([string]$name,[object[]]$arguments) {
 $values=New-Object 'Collections.Generic.List[object]'
 foreach($argument in $arguments) { if($null -eq $argument) { $values.Add($null) } else { $values.Add($argument.PSObject.BaseObject) } }
 $dockType.GetMethod($name,$flags).Invoke($script:dock,$values.ToArray())
}
function Check([string]$name,[bool]$ok) { if(!$ok) { throw $name }; Write-Output ('PASS '+$name) }
function Buttons([Windows.DependencyObject]$root) {
 if($root -is [Windows.Controls.Button]) { Write-Output $root }
 foreach($child in [Windows.LogicalTreeHelper]::GetChildren($root)) { if($child -is [Windows.DependencyObject]) { Buttons $child } }
}
$oldFolder=$prefsType.GetField('DataFolderOverride').GetValue($null)
$prefsType.GetField('DataFolderOverride').SetValue($null,(Join-Path ([IO.Path]::GetTempPath()) ('Nook.ScenarioIntegration.'+[Guid]::NewGuid().ToString('N'))))
$dock=$null; $source=$null; $settings=$null; $editor=$null; $externalKey=0
try {
 $serializer=New-Object Xml.Serialization.XmlSerializer($prefsType)
 $reader=New-Object IO.StringReader('<Preferences><Size>36</Size><GlassColor>#1A1A1A</GlassColor></Preferences>')
 $legacy=$serializer.Deserialize($reader); $reader.Dispose()
 Check 'Legacy settings load with empty scenarios' ($legacy.Scenarios.Count -eq 0 -and $legacy.Size -eq 36)
 $scenario=[Activator]::CreateInstance($scenarioType); $scenario.Name='Работа'; $scenario.HotkeyModifiers=7
 $action=[Activator]::CreateInstance($actionType); $action.Kind='Web'; $action.Urls.Add('https://example.com/')
 $scenario.Actions.Add($action); $legacy.Scenarios.Add($scenario)
 $writer=New-Object IO.StringWriter; $serializer.Serialize($writer,$legacy)
 $reader=New-Object IO.StringReader($writer.ToString()); $copy=$serializer.Deserialize($reader); $reader.Dispose(); $writer.Dispose()
 Check 'Scenarios roundtrip without losing original preferences' ($copy.Scenarios.Count -eq 1 -and $copy.Scenarios[0].Actions[0].Urls[0] -eq 'https://example.com/' -and $copy.GlassColor -eq '#1A1A1A' -and $copy.Size -eq 36)
 $dock=[Activator]::CreateInstance($dockType,$true); $prefs=Field 'prefs'; $prefs.Scenarios.Add($scenario)
 $clone=$dockType.GetMethod('CloneScenario',[Reflection.BindingFlags]'Static,NonPublic').Invoke($null,@($scenario))
 $clone.Actions[0].Urls[0]='https://example.org/'; $clone.Name='Changed'
 Check 'Editing draft cannot mutate saved scenario' ($scenario.Name -eq 'Работа' -and $scenario.Actions[0].Urls[0] -eq 'https://example.com/')
 $parameters=New-Object Windows.Interop.HwndSourceParameters('Nook.ScenarioChecks.'+[Guid]::NewGuid().ToString('N'))
 $parameters.ParentWindow=[IntPtr](-3); $parameters.Width=0; $parameters.Height=0; $parameters.WindowStyle=0
 $source=New-Object Windows.Interop.HwndSource($parameters)
 $dockType.GetField('handle',$flags).SetValue($dock,$source.Handle)
 foreach($key in 0x7C..0x87) { if([ScenarioNativeChecks]::RegisterHotKey([IntPtr]::Zero,0x5200,0x4007,$key)) { $externalKey=$key; break } }
 if($externalKey -eq 0) { throw 'No free hotkey available for temporary native checks.' }
 $scenario.HotkeyKey=$externalKey
 Check 'Busy OS hotkey rejected before Save' ($null -ne (Call 'ProbeScenarioHotkey' @($scenario,$scenario)))
 $null=[ScenarioNativeChecks]::UnregisterHotKey([IntPtr]::Zero,0x5200); $externalKey=0
 Check 'Available hotkey passes registration probe' ($null -eq (Call 'ProbeScenarioHotkey' @($scenario,$scenario)))
 $null=Call 'RegisterScenarioHotkeys' @()
 Check 'Scenario hotkey registered' ((Field 'scenarioHotkeys').Count -eq 1)
 $registered=[ScenarioNativeChecks]::RegisterHotKey([IntPtr]::Zero,0x5201,0x4007,$scenario.HotkeyKey)
 if($registered) { $null=[ScenarioNativeChecks]::UnregisterHotKey([IntPtr]::Zero,0x5201) }
 Check 'Registered scenario owns its native combination' (!$registered)
 $null=Call 'UnregisterScenarioHotkeys' @()
 $registered=[ScenarioNativeChecks]::RegisterHotKey([IntPtr]::Zero,0x5201,0x4007,$scenario.HotkeyKey)
 if($registered) { $null=[ScenarioNativeChecks]::UnregisterHotKey([IntPtr]::Zero,0x5201) }
 Check 'Cleanup releases native combination' $registered
 $duplicate=[Activator]::CreateInstance($scenarioType); $duplicate.HotkeyKey=$scenario.HotkeyKey; $duplicate.HotkeyModifiers=$scenario.HotkeyModifiers
 Check 'Duplicate scenario combination rejected' ($null -ne (Call 'ValidateScenarioHotkey' @($duplicate,$null)))
 $scenario.Enabled=$false; $null=Call 'RegisterScenarioHotkeys' @()
 Check 'Disabled scenario leaves no registered hotkeys' ((Field 'scenarioHotkeys').Count -eq 0)
 $settings=New-Object Windows.Window; $dockType.GetField('settings',$flags).SetValue($dock,$settings)
 $tabs=New-Object Windows.Controls.TabControl; $null=Call 'BuildScenarioSettings' @($tabs)
 Check 'Dedicated scenarios settings tab builds without showing windows' ($tabs.Items.Count -eq 1 -and $tabs.Items[0].Header -eq 'Сценарии' -and !$settings.IsVisible -and !$dock.IsVisible)
 $editor=Call 'CreateScenarioEditor' @($scenario)
 $buttons=@(Buttons $editor)
 Check 'Editor builds Save and Cancel without showing windows' (!$editor.IsVisible -and ($buttons | Where-Object Content -eq 'Сохранить сценарий').Count -eq 1 -and ($buttons | Where-Object Content -eq 'Отмена').Count -eq 1)
 foreach($kind in @('Приложение','Проект','Сайты')) { $button=$buttons | Where-Object Content -eq $kind | Select-Object -First 1; $button.RaiseEvent((New-Object Windows.RoutedEventArgs([Windows.Controls.Button]::ClickEvent))) }
 $buttons=@(Buttons $editor)
 Check 'Editor adds all three action types to its draft' (($buttons | Where-Object Content -eq 'Убрать действие').Count -eq 4)
 Check 'Draft edits leave saved actions unchanged' ($scenario.Actions.Count -eq 1 -and $scenario.Actions[0].Kind -eq 'Web')
} finally {
 if($null -ne $editor) { $editor.Close() }
 if($externalKey -ne 0) { $null=[ScenarioNativeChecks]::UnregisterHotKey([IntPtr]::Zero,0x5200) }
 if($null -ne $dock) {
  $null=Call 'UnregisterScenarioHotkeys' @()
  $dockType.GetField('handle',$flags).SetValue($dock,[IntPtr]::Zero)
  $dockType.GetField('settings',$flags).SetValue($dock,$null)
  $dock.Close()
 }
 if($null -ne $source) { $source.Dispose() }
 if($null -ne $settings) { $settings.Close() }
 $prefsType.GetField('DataFolderOverride').SetValue($null,$oldFolder)
}
