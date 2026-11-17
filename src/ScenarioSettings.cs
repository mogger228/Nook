using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Nook {
 partial class DockWindow {
  void AddScenariosMenu(ContextMenu menu) {
   var item=new MenuItem { Header="Сценарии" };
   menu.Opened+=delegate {
    item.Items.Clear();
    foreach(var scenario in (prefs.Scenarios ?? new List<ScenarioDefinition>()).Where(s=>s!=null && s.Enabled)) {
     var current=scenario;
     var entry=new MenuItem { Header=current.Name,IsEnabled=!scenarioRunning,InputGestureText=current.HotkeyKey==0 ? "" : ScenarioHotkeyLabel(current) };
     entry.Click+=delegate { RunScenario(current); }; item.Items.Add(entry);
    }
    if(item.Items.Count>0) item.Items.Add(new Separator());
    var configure=new MenuItem { Header="Настроить сценарии…" };
    configure.Click+=delegate {
     ShowSettings(); var border=settings.Content as Border; var root=border==null ? null : border.Child as Grid;
     if(root!=null) { var tabs=root.Children.OfType<TabControl>().FirstOrDefault(); if(tabs!=null) tabs.SelectedItem=tabs.Items.OfType<TabItem>().FirstOrDefault(t=>Convert.ToString(t.Header)=="Сценарии"); }
    };
    item.Items.Add(configure);
   };
   // Populate again on each menu opening so edits never leave stale commands.
   item.Items.Add(new MenuItem { Header="Настроить сценарии…" }); menu.Items.Add(item);
  }
  void BuildScenarioSettings(TabControl tabs) {
   var page=SettingsPage(tabs,"Сценарии");
   refreshScenarioSettings=delegate {
    page.Children.Clear();
    var intro=SettingsCard(page,"Сценарии","Открывайте приложения, проекты и сайты одним действием. Горячие клавиши работают, пока запущена панель.");
    intro.Children.Add(ScenarioButton("Создать сценарий",delegate { EditScenario(null); }));
    if(prefs.Scenarios==null || !prefs.Scenarios.Any(s=>s!=null)) page.Children.Add(SettingHint("Сценариев пока нет. Создайте первый и добавьте нужные действия.",new Thickness(2,0,2,12)));
    else foreach(var scenario in prefs.Scenarios.Where(s=>s!=null)) {
     var current=scenario;
     var card=SettingsCard(page,current.Name ?? "Без названия",ScenarioHotkeyLabel(current)+(current.Enabled ? "" : " · отключён"));
     card.Children.Add(SettingHint("Действий: "+(current.Actions==null ? 0 : current.Actions.Count),new Thickness(0,0,0,12)));
     string error;
     if(scenarioHotkeyErrors.TryGetValue(current.Id ?? "",out error)) card.Children.Add(new TextBlock { Text=error,TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.FromRgb(239,160,160)),Margin=new Thickness(0,0,0,12) });
     var buttons=new WrapPanel();
     var run=ScenarioButton(scenarioRunning ? "Выполняется…" : "Запустить",delegate { RunScenario(current); });
     run.IsEnabled=current.Enabled && !scenarioRunning; buttons.Children.Add(run);
     buttons.Children.Add(ScenarioButton("Изменить",delegate { EditScenario(current); }));
     buttons.Children.Add(ScenarioButton("Удалить",delegate {
      if(MessageBox.Show(settings,"Удалить сценарий «"+current.Name+"»?","Сценарии",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes) return;
      var before=prefs.Scenarios; prefs.Scenarios=before.Where(s=>!object.ReferenceEquals(s,current)).ToList();
      try { prefs.Save(); } catch(Exception failure) { prefs.Scenarios=before; MessageBox.Show(settings,failure.Message,"Не удалось сохранить сценарии"); return; }
      RegisterScenarioHotkeys();
     }));
     card.Children.Add(buttons);
    }
   };
   RefreshScenarioSettings();
  }
  Button ScenarioButton(string title,Action action) {
   var button=new Button { Content=title,Margin=new Thickness(0,0,8,6),Padding=new Thickness(12,7,12,7) };
   button.Click+=delegate { action(); }; return button;
  }
  static ScenarioDefinition CloneScenario(ScenarioDefinition source) {
   var copy=new ScenarioDefinition { Name=source.Name,Id=source.Id,Enabled=source.Enabled,HotkeyKey=source.HotkeyKey,HotkeyModifiers=source.HotkeyModifiers };
   copy.Actions=(source.Actions ?? new List<ScenarioAction>()).Where(a=>a!=null).Select(a=>new ScenarioAction { Kind=a.Kind,Target=a.Target,ProjectPath=a.ProjectPath,Urls=a.Urls==null ? new List<string>() : new List<string>(a.Urls) }).ToList();
   return copy;
  }
  void EditScenario(ScenarioDefinition original) {
   if(scenarioEditorOpen || closing || settings==null) return;
   var editor=CreateScenarioEditor(original);
   scenarioEditorOpen=true; UnregisterScenarioHotkeys();
   try { editor.ShowDialog(); }
   finally { scenarioEditorOpen=false; if(!closing) RegisterScenarioHotkeys(); }
  }
  Window CreateScenarioEditor(ScenarioDefinition original) {
   var draft=original==null ? new ScenarioDefinition { Name="Новый сценарий" } : CloneScenario(original);
   var work=SystemParameters.WorkArea;
   var editor=new Window { Title=original==null ? "Создать сценарий" : "Изменить сценарий",Owner=settings!=null && settings.IsVisible ? settings : null,Width=Math.Min(650,work.Width-24),Height=Math.Min(760,work.Height-24),MinWidth=Math.Min(420,work.Width-24),MinHeight=Math.Min(360,work.Height-24),WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false };
   SettingsTheme.Apply(editor);
   var root=new Grid { Margin=new Thickness(22) };
   root.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
   root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
   editor.Content=root;
   var body=new StackPanel();
   var scroll=new ScrollViewer { Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,8,0) };
   scroll.PreviewMouseWheel+=delegate(object sender,MouseWheelEventArgs e) { scroll.ScrollToVerticalOffset(scroll.VerticalOffset-e.Delta*.20); e.Handled=true; };
   root.Children.Add(scroll);
   var general=SettingsCard(body,"Сценарий");
   ScenarioTextField(general,"Название",draft.Name,delegate(string value) { draft.Name=value; });
   SettingsToggle(general,"Сценарий включён",draft.Enabled,delegate(bool value) { draft.Enabled=value; });
   general.Children.Add(new TextBlock { Text="Горячая клавиша",Margin=new Thickness(0,0,0,8) });
   var keys=new TextBox { IsReadOnly=true,Text=ScenarioHotkeyLabel(draft),ToolTip="Нажмите сочетание с Ctrl или Alt. Можно добавить Shift.",Margin=new Thickness(0,0,0,8) };
   keys.PreviewKeyDown+=delegate(object sender,KeyEventArgs e) {
    Key key=e.Key==Key.System ? e.SystemKey : e.Key;
    if(key==Key.Tab && (Keyboard.Modifiers==ModifierKeys.None || Keyboard.Modifiers==ModifierKeys.Shift)) return;
    e.Handled=true;
    if(key==Key.Escape) { Keyboard.ClearFocus(); return; }
    int virtualKey=KeyInterop.VirtualKeyFromKey(key);
    if(virtualKey==0 || key==Key.LeftCtrl || key==Key.RightCtrl || key==Key.LeftAlt || key==Key.RightAlt || key==Key.LeftShift || key==Key.RightShift || key==Key.LWin || key==Key.RWin) return;
    var modifiers=Keyboard.Modifiers;
    if((modifiers&(ModifierKeys.Control|ModifierKeys.Alt))==0 || (modifiers&ModifierKeys.Windows)!=0) { keys.Text=ScenarioHotkeyLabel(draft); keys.ToolTip="Нужны Ctrl или Alt + клавиша. Сочетания с Win зарезервированы Windows."; return; }
    draft.HotkeyKey=(uint)virtualKey;
    draft.HotkeyModifiers=((modifiers&ModifierKeys.Alt)!=0 ? 1u : 0u)|((modifiers&ModifierKeys.Control)!=0 ? 2u : 0u)|((modifiers&ModifierKeys.Shift)!=0 ? 4u : 0u);
    keys.Text=ScenarioHotkeyLabel(draft);
   };
   general.Children.Add(keys);
   general.Children.Add(ScenarioButton("Убрать горячую клавишу",delegate { draft.HotkeyKey=draft.HotkeyModifiers=0; keys.Text=ScenarioHotkeyLabel(draft); }));
   general.Children.Add(SettingHint("Нажмите поле и задайте сочетание. Например, Ctrl + Alt + 1. Назначение проверяется при сохранении.",new Thickness(0)));
   var actionsBody=new StackPanel(); body.Children.Add(actionsBody);
   Action renderActions=null;
   renderActions=delegate {
    actionsBody.Children.Clear();
    for(int i=0;i<draft.Actions.Count;i++) {
     int index=i; var action=draft.Actions[i];
     var card=SettingsCard(actionsBody,(i+1)+". "+(action.Kind=="Web" ? "Сайты" : action.Kind=="Project" ? "Проект" : "Приложение"));
     var controls=new WrapPanel();
     var up=ScenarioButton("↑",delegate { var previous=draft.Actions[index-1]; draft.Actions[index-1]=action; draft.Actions[index]=previous; renderActions(); }); up.IsEnabled=index>0;
     var down=ScenarioButton("↓",delegate { var next=draft.Actions[index+1]; draft.Actions[index+1]=action; draft.Actions[index]=next; renderActions(); }); down.IsEnabled=index<draft.Actions.Count-1;
     controls.Children.Add(up); controls.Children.Add(down);
     controls.Children.Add(ScenarioButton("Убрать действие",delegate { draft.Actions.RemoveAt(index); renderActions(); }));
     card.Children.Add(controls);
     var target=ScenarioTextField(card,action.Kind=="Web" ? "Браузер (пусто — стандартный)" : "Программа или ярлык",action.Target,delegate(string value) { action.Target=value; });
     var browseRow=new WrapPanel();
     browseRow.Children.Add(ScenarioButton("Выбрать программу…",delegate {
      var dialog=new OpenFileDialog { Title=action.Kind=="Web" ? "Выберите браузер" : "Выберите программу",Filter="Программы и ярлыки|*.exe;*.lnk;*.appref-ms|Все файлы|*.*",CheckFileExists=true };
      if(dialog.ShowDialog(editor)==true) target.Text=dialog.FileName;
     }));
     if(action.Kind=="Web") browseRow.Children.Add(ScenarioButton("Стандартный браузер",delegate { target.Text=""; }));
     card.Children.Add(browseRow);
     if(action.Kind=="Project") {
      var project=ScenarioTextField(card,"Папка или файл проекта",action.ProjectPath,delegate(string value) { action.ProjectPath=value; });
      var projectButtons=new WrapPanel();
      projectButtons.Children.Add(ScenarioButton("Выбрать папку…",delegate { string folder=ChooseScenarioFolder(editor); if(folder!=null) project.Text=folder; }));
      projectButtons.Children.Add(ScenarioButton("Выбрать файл…",delegate { var dialog=new OpenFileDialog { Title="Файл проекта",Filter="Все файлы|*.*",CheckFileExists=true }; if(dialog.ShowDialog(editor)==true) project.Text=dialog.FileName; }));
      card.Children.Add(projectButtons);
      card.Children.Add(SettingHint("Путь будет передан выбранной программе. Она должна поддерживать открытие этого проекта через командную строку.",new Thickness(0)));
     }
     if(action.Kind=="Web") {
      var urls=ScenarioTextField(card,"Адреса сайтов — каждый с новой строки",string.Join(Environment.NewLine,action.Urls ?? new List<string>()),delegate(string value) { action.Urls=value.Split(new[] { '\r','\n' },StringSplitOptions.RemoveEmptyEntries).Select(s=>s.Trim()).Where(s=>s.Length>0).ToList(); });
      urls.AcceptsReturn=true; urls.TextWrapping=TextWrapping.Wrap; urls.MinHeight=100;
      card.Children.Add(SettingHint("Указывайте полные адреса https://… или http://… . Сайты откроются повторно при каждом запуске сценария.",new Thickness(0)));
     }
    }
   };
   renderActions();
   var add=SettingsCard(body,"Добавить действие");
   var addButtons=new WrapPanel();
   foreach(string kind in new[] { "Application","Project","Web" }) {
    string selected=kind;
    addButtons.Children.Add(ScenarioButton(kind=="Web" ? "Сайты" : kind=="Project" ? "Проект" : "Приложение",delegate { draft.Actions.Add(new ScenarioAction { Kind=selected }); renderActions(); }));
   }
   add.Children.Add(addButtons);
   var footer=new StackPanel { Margin=new Thickness(0,14,0,0) }; Grid.SetRow(footer,1); root.Children.Add(footer);
   var validation=new TextBlock { TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.FromRgb(239,160,160)),Margin=new Thickness(0,0,0,10),MaxHeight=100 };
   footer.Children.Add(validation);
   var buttons=new WrapPanel(); footer.Children.Add(buttons);
   var save=new Button { Content="Сохранить сценарий",Margin=new Thickness(0,0,8,0),IsDefault=true };
   save.Click+=async delegate {
    save.IsEnabled=false; body.IsEnabled=false; validation.Text="";
    try {
     var snapshot=CloneScenario(draft); snapshot.Name=(snapshot.Name ?? "").Trim();
     var errors=await Task.Run(delegate { return ScenarioLauncher.Validate(snapshot); });
     if(closing || !editor.IsVisible) return;
     string hotkeyError=ProbeScenarioHotkey(snapshot,original); if(hotkeyError!=null) errors.Add(hotkeyError);
     if(errors.Count>0) { validation.Text=string.Join(Environment.NewLine,errors.Take(4)); return; }
     var previous=prefs.Scenarios ?? new List<ScenarioDefinition>(); var updated=new List<ScenarioDefinition>(previous);
     if(original==null) updated.Add(snapshot); else { int index=updated.IndexOf(original); if(index<0) { validation.Text="Сценарий уже удалён. Откройте редактор заново."; return; } updated[index]=snapshot; }
     prefs.Scenarios=updated;
     try { prefs.Save(); } catch(Exception failure) { prefs.Scenarios=previous; validation.Text="Не удалось сохранить: "+failure.Message; return; }
     editor.DialogResult=true;
    } catch(Exception failure) { if(editor.IsVisible) validation.Text=failure.Message; }
    finally { save.IsEnabled=true; body.IsEnabled=true; }
   };
   buttons.Children.Add(save);
   var cancel=ScenarioButton("Отмена",delegate { editor.Close(); }); cancel.IsCancel=true; buttons.Children.Add(cancel);
   return editor;
  }
  TextBox ScenarioTextField(StackPanel panel,string title,string value,Action<string> changed) {
   panel.Children.Add(new TextBlock { Text=title,Margin=new Thickness(0,6,0,8) });
   var input=new TextBox { Text=value ?? "",Margin=new Thickness(0,0,0,10) };
   input.TextChanged+=delegate { changed(input.Text); };
   panel.Children.Add(input); return input;
  }
  static string ChooseScenarioFolder(Window owner) {
   var info=new ScenarioBrowseInfo { Owner=new WindowInteropHelper(owner).Handle,Title="Выберите папку проекта",Flags=0x41 };
   info.DisplayName=Marshal.AllocHGlobal(520);
   IntPtr item=IntPtr.Zero;
   try { item=SHBrowseForFolder(ref info); if(item==IntPtr.Zero) return null; var path=new StringBuilder(32768); return SHGetPathFromIDList(item,path) ? path.ToString() : null; }
   finally { if(item!=IntPtr.Zero) Marshal.FreeCoTaskMem(item); Marshal.FreeHGlobal(info.DisplayName); }
  }
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct ScenarioBrowseInfo { public IntPtr Owner,Root,DisplayName; public string Title; public uint Flags; public IntPtr Callback,Parameter; public int Image; }
  [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SHBrowseForFolder(ref ScenarioBrowseInfo info);
  [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern bool SHGetPathFromIDList(IntPtr item,StringBuilder path);
 }
}
