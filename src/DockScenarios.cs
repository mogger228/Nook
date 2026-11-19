using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace Nook {
 partial class DockWindow {
  readonly Dictionary<int,ScenarioDefinition> scenarioHotkeys=new Dictionary<int,ScenarioDefinition>();
  readonly Dictionary<string,string> scenarioHotkeyErrors=new Dictionary<string,string>();
  Action refreshScenarioSettings;
  bool scenarioRunning,scenarioEditorOpen;
  const int ScenarioHotkeyBase=0x6100;
  string ScenarioDiagnostics { get { return " Scenarios="+(prefs.Scenarios==null ? 0 : prefs.Scenarios.Count)+" ScenarioHotkeys="+scenarioHotkeys.Count+" ScenarioHotkeyErrors="+scenarioHotkeyErrors.Count+" ScenarioRunning="+scenarioRunning; } }

  void RegisterScenarioHotkeys() {
   UnregisterScenarioHotkeys(); scenarioHotkeyErrors.Clear();
   if(closing || handle==IntPtr.Zero || scenarioEditorOpen) return;
   if(prefs.Scenarios==null) prefs.Scenarios=new List<ScenarioDefinition>();
   int id=ScenarioHotkeyBase;
   foreach(var scenario in prefs.Scenarios.Where(s=>s!=null && s.Enabled && s.HotkeyKey!=0)) {
    string error=ValidateScenarioHotkey(scenario,scenario);
    if(error==null && id<=0xBFFF) {
     if(RegisterHotKey(handle,id,scenario.HotkeyModifiers|0x4000,scenario.HotkeyKey)) scenarioHotkeys.Add(id,scenario);
     else error="Сочетание занято Windows или другой программой. Выберите другое.";
    } else if(error==null) error="Достигнут предел горячих клавиш.";
    if(error!=null) scenarioHotkeyErrors[scenario.Id ?? ""]=error;
    id++;
   }
   RefreshScenarioSettings();
  }
  void UnregisterScenarioHotkeys() {
   foreach(int id in scenarioHotkeys.Keys) UnregisterHotKey(handle,id);
   scenarioHotkeys.Clear();
  }
  bool HandleScenarioHotkey(IntPtr identifier,IntPtr keyData) {
   ScenarioDefinition scenario;
   if(!scenarioHotkeys.TryGetValue(identifier.ToInt32(),out scenario)) return false;
   uint data=unchecked((uint)keyData.ToInt64());
   if((data>>16)!=scenario.HotkeyKey || (data&0xFu)!=scenario.HotkeyModifiers) return true;
   if(!closing && !scenarioEditorOpen) RunScenario(scenario);
   return true;
  }
  string ValidateScenarioHotkey(ScenarioDefinition candidate,ScenarioDefinition original) {
   if(candidate.HotkeyKey==0) return candidate.HotkeyModifiers==0 ? null : "Выберите основную клавишу сочетания.";
   uint modifiers=candidate.HotkeyModifiers;
   if(modifiers==1 && candidate.HotkeyKey==32) return "Alt + Пробел используется поиском приложений панели.";
   if(modifiers==3 && candidate.HotkeyKey==32) return "Ctrl + Alt + Пробел используется веб-поиском панели.";
   if((modifiers&~7u)!=0 || (modifiers&3u)==0) return "Используйте Ctrl или Alt вместе с клавишей; можно добавить Shift.";
   uint key=candidate.HotkeyKey;
   if(key<8 || key>254 || key==0x10 || key==0x11 || key==0x12 || key==0x5B || key==0x5C || key>=0xA0 && key<=0xA5 || key==0x7B) return "Эта клавиша зарезервирована. Выберите другое сочетание.";
   if(modifiers==1 && (key==9 || key==27 || key==0x73) || modifiers==2 && key==27 || modifiers==3 && key==0x2E) return "Это системное сочетание. Выберите другое.";
   if(prefs.Scenarios!=null && prefs.Scenarios.Any(s=>s!=null && !object.ReferenceEquals(s,original) && s.Enabled && s.HotkeyKey==key && s.HotkeyModifiers==modifiers)) return "Сочетание уже назначено другому сценарию.";
   return null;
  }
  string ProbeScenarioHotkey(ScenarioDefinition candidate,ScenarioDefinition original) {
   string error=ValidateScenarioHotkey(candidate,original);
   if(error!=null || !candidate.Enabled || candidate.HotkeyKey==0) return error;
   if(!RegisterHotKey(handle,0x6000,candidate.HotkeyModifiers|0x4000,candidate.HotkeyKey)) return "Сочетание занято Windows или другой программой. Выберите другое.";
   UnregisterHotKey(handle,0x6000); return null;
  }
  static string ScenarioHotkeyLabel(ScenarioDefinition scenario) {
   if(scenario.HotkeyKey==0) return "Без горячей клавиши";
   var parts=new List<string>();
   if((scenario.HotkeyModifiers&2)!=0) parts.Add("Ctrl");
   if((scenario.HotkeyModifiers&1)!=0) parts.Add("Alt");
   if((scenario.HotkeyModifiers&4)!=0) parts.Add("Shift");
   parts.Add(new KeyConverter().ConvertToString(KeyInterop.KeyFromVirtualKey((int)scenario.HotkeyKey)));
   return string.Join(" + ",parts);
  }
  async void RunScenario(ScenarioDefinition scenario) {
   if(closing || scenarioRunning || scenario==null || !scenario.Enabled) return;
   scenarioRunning=true; RefreshScenarioSettings();
   try {
    var errors=await ScenarioLauncher.RunAsync(scenario);
    if(!closing && errors.Count>0) MessageBox.Show(settings ?? this,string.Join(Environment.NewLine,errors),"Сценарий: "+scenario.Name,MessageBoxButton.OK,MessageBoxImage.Warning);
   } catch(Exception error) {
    if(!closing) MessageBox.Show(settings ?? this,error.Message,"Не удалось запустить сценарий",MessageBoxButton.OK,MessageBoxImage.Warning);
   } finally { scenarioRunning=false; if(!closing) RefreshScenarioSettings(); }
  }
  void RefreshScenarioSettings() { if(refreshScenarioSettings!=null) refreshScenarioSettings(); }
 }
}
