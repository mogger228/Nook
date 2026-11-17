using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Nook {
 partial class DockWindow {
  public void ShowSettings() {
   if(settings!=null) { settings.Activate(); return; }
   var area=SystemParameters.WorkArea;
   double width=Math.Min(760,Math.Max(320,area.Width-40));
   double height=Math.Min(780,Math.Max(300,area.Height-40));
   settings=new Window {
    Title="Nook — настройки",Width=width,Height=height,
    MinWidth=Math.Min(520,width),MinHeight=Math.Min(420,height),
    MaxWidth=Math.Max(width,area.Width),MaxHeight=Math.Max(height,area.Height),
    ResizeMode=ResizeMode.CanResize,WindowStartupLocation=WindowStartupLocation.CenterScreen
   };
   SettingsTheme.Apply(settings);
   var layout=new Grid { Margin=new Thickness(28,24,28,18) };
   layout.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
   layout.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
   layout.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
   settings.Content=new Border {
    Child=layout,CornerRadius=new CornerRadius(14),Margin=new Thickness(5),
    Background=SettingsBrush("SettingsBackground",Color.FromArgb(204,32,32,32)),
    BorderBrush=new SolidColorBrush(Color.FromArgb(72,255,255,255)),BorderThickness=new Thickness(1)
   };
   var header=new StackPanel { Margin=new Thickness(0,0,0,20) };
   header.Children.Add(new TextBlock { Text="Nook",FontSize=28,FontWeight=FontWeights.SemiBold });
   header.Children.Add(SettingHint("Настройте панель и рабочий стол под себя.",new Thickness(0,5,0,0)));
   layout.Children.Add(header);
   var tabs=new TabControl(); Grid.SetRow(tabs,1); layout.Children.Add(tabs);
   var panel=SettingsPage(tabs,"Панель");
   var clocks=SettingsPage(tabs,"Часы");

   var location=SettingsCard(panel,"Расположение");
   var positions=new Grid { Margin=new Thickness(0,0,0,18) };
   var positionButtons=new Button[3]; string[] positionNames={ "Снизу","Сверху","Слева" };
   Action updatePositions=delegate {
    int selected=prefs.Left ? 2 : (prefs.Top ? 1 : 0);
    for(int i=0;i<positionButtons.Length;i++) {
     positionButtons[i].Background=new SolidColorBrush(i==selected ? Color.FromRgb(78,78,78) : Color.FromRgb(42,42,42));
     positionButtons[i].BorderBrush=new SolidColorBrush(i==selected ? Color.FromRgb(190,190,190) : Color.FromRgb(88,88,88));
    }
   };
   for(int i=0;i<3;i++) {
    int choice=i; positions.ColumnDefinitions.Add(new ColumnDefinition());
    var button=new Button { Content=positionNames[i],Margin=new Thickness(i==0 ? 0 : 8,0,0,0) };
    positionButtons[i]=button; Grid.SetColumn(button,i); positions.Children.Add(button);
    button.Click+=delegate { prefs.Left=choice==2; prefs.Top=choice==1; updatePositions(); RefreshApps(true); Save(); };
   }
   updatePositions(); location.Children.Add(positions);
   SettingsSlider(location,"Отступ от края",0,80,prefs.BottomOffset,1,"px",delegate(double value) { prefs.BottomOffset=value; Place(); });
   SettingsToggle(location,"Автоматически скрывать панель",prefs.AutoHide,delegate(bool enabled) { prefs.AutoHide=enabled; Place(); Save(); });
   location.Children.Add(SettingHint("Подведите курсор к краю экрана, чтобы показать панель. При автоскрытии Nook появляется поверх окон и не резервирует место для себя.",new Thickness(0,0,0,0)));

   var appearance=SettingsCard(panel,"Внешний вид");
   SettingsSlider(appearance,"Размер значков",32,72,prefs.Size,4,"",delegate(double value) { prefs.Size=(int)value; RefreshApps(true); });
   SettingsSlider(appearance,"Скругление углов",0,40,prefs.CornerRadius,1,"",delegate(double value) { prefs.CornerRadius=value; ApplyGlassAppearance(); });
   AddGlassColorSettings(appearance);
   SettingsSlider(appearance,"Непрозрачность стекла",20,100,prefs.BackgroundOpacity*100,1,"%",delegate(double value) { prefs.BackgroundOpacity=value/100; ApplyGlassAppearance(); });
   SettingsToggle(appearance,"Тонкий световой блик по верхнему краю",prefs.EdgeHighlight,delegate(bool enabled) { prefs.EdgeHighlight=enabled; ApplyGlassAppearance(); Save(); });

   var behavior=SettingsCard(panel,"Поведение");
   SettingsToggle(behavior,"Увеличивать значки при наведении",prefs.Zoom,delegate(bool enabled) { prefs.Zoom=enabled; RefreshApps(true); Save(); });
   SettingsToggle(behavior,"Плавная волна соседних значков",prefs.HoverWave,delegate(bool enabled) { prefs.HoverWave=enabled; RefreshApps(true); Save(); });
   SettingsSlider(behavior,"Увеличение при наведении",100,200,prefs.ZoomScale*100,5,"%",delegate(double value) { prefs.ZoomScale=value/100; Place(); });
   SettingsToggle(behavior,"Плавные анимации",prefs.Animations,delegate(bool enabled) { prefs.Animations=enabled; RefreshApps(true); Save(); });
   SettingsToggle(behavior,"Скрыть панель Windows",taskbarHidden,delegate(bool enabled) { taskbarHidden=enabled; prefs.HideWindowsTaskbar=enabled; if(enabled) Native.HideTaskbars(); else Native.RestoreTaskbars(); Place(); Save(); });
   behavior.Children.Add(SettingHint("Вернуть панель Windows можно через меню ⋯ или Restore-Taskbar.cmd. При выходе из Nook она восстанавливается.",new Thickness(0,0,0,16)));
   var startup=new CheckBox { Content="Запускать при входе в Windows",IsChecked=StartupEnabled(),Margin=new Thickness(0,0,0,4) };
   startup.Click+=delegate { try { SetStartup(startup.IsChecked==true); } catch(Exception error) { startup.IsChecked=StartupEnabled(); MessageBox.Show(error.Message,"Автозапуск"); } };
   behavior.Children.Add(startup);
   panel.Children.Add(SettingHint("Перетаскивайте значки, чтобы менять порядок. Правая кнопка открывает окна и закрепление; файлы можно бросать на значки программ.",new Thickness(2,0,2,12)));

   var clock=SettingsCard(clocks,"Часы на рабочем столе","Над обоями, под окнами приложений. Цифры пропускают клики.");
   AddClockToggle(clock,"Показывать часы",prefs.ClockEnabled,delegate(bool enabled) { prefs.ClockEnabled=enabled; });
   AddClockSlider(clock,"Размер часов",24,160,prefs.ClockSize,delegate(double value) { prefs.ClockSize=value; });
   AddClockSlider(clock,"По горизонтали, %",0,100,prefs.ClockX*100,delegate(double value) { prefs.ClockX=value/100; });
   AddClockSlider(clock,"По вертикали, %",0,100,prefs.ClockY*100,delegate(double value) { prefs.ClockY=value/100; });
   var indicators=SettingsCard(clocks,"Под часами рабочего стола","Под временем будут дата и текущая погода для Санкт-Петербурга.");
   AddClockToggle(indicators,"Показывать дату",prefs.ClockShowDate,delegate(bool enabled) { prefs.ClockShowDate=enabled; });
   AddClockToggle(indicators,"Показывать погоду",prefs.ClockShowWeather,delegate(bool enabled) { prefs.ClockShowWeather=enabled; });
   AddClockSlider(indicators,"Размер даты и погоды",10,32,prefs.ClockStatusSize,delegate(double value) { prefs.ClockStatusSize=value; });

   var sideIslands=SettingsCard(clocks,"Дополнительные островки","Часы — слева, индикаторы — справа. У вертикальной панели часы находятся сверху, индикаторы снизу.");
   SettingsToggle(sideIslands,"Показывать островок часов",prefs.LeftClockIslandEnabled,delegate(bool enabled) { prefs.LeftClockIslandEnabled=enabled; UpdateSideIslandPresentation(); Place(); Save(); });
   SettingsToggle(sideIslands,"Показывать островок индикаторов",prefs.RightStatusIslandEnabled,delegate(bool enabled) { prefs.RightStatusIslandEnabled=enabled; UpdateSideIslandPresentation(); Place(); Save(); });
   SettingsToggle(sideIslands,"Батарея",prefs.SideShowBattery,delegate(bool enabled) { prefs.SideShowBattery=enabled; UpdateSideIslandPresentation(); Place(); Save(); });
   SettingsToggle(sideIslands,"Wi-Fi",prefs.SideShowWifi,delegate(bool enabled) { prefs.SideShowWifi=enabled; UpdateSideIslandPresentation(); Place(); Save(); });
   SettingsToggle(sideIslands,"Язык ввода",prefs.SideShowLanguage,delegate(bool enabled) { prefs.SideShowLanguage=enabled; UpdateSideIslandPresentation(); Place(); Save(); });
   if(!string.IsNullOrEmpty(prefs.PinImportStatus)) sideIslands.Children.Add(SettingHint(prefs.PinImportStatus,new Thickness(0)));

   var horizontalIslands=SettingsCard(clocks,"Островки — сверху и снизу","Размеры для горизонтального расположения панели.");
   SettingsSlider(horizontalIslands,"Размер часов",12,32,prefs.HorizontalIslandClockSize,1,"",delegate(double value) { prefs.HorizontalIslandClockSize=value; Place(); });
   SettingsSlider(horizontalIslands,"Размер индикаторов",10,32,HorizontalIslandStatusSize,1,"",delegate(double value) { prefs.HorizontalIslandStatusSize=value; Place(); });
   var verticalIslands=SettingsCard(clocks,"Островки — слева","Отдельные размеры для вертикальной панели. Высота индикаторов подстраивается автоматически.");
   SettingsSlider(verticalIslands,"Размер часов",10,22,prefs.VerticalIslandClockSize,1,"",delegate(double value) { prefs.VerticalIslandClockSize=value; Place(); });
   SettingsSlider(verticalIslands,"Размер индикаторов",10,20,prefs.VerticalIslandStatusSize,1,"",delegate(double value) { prefs.VerticalIslandStatusSize=value; Place(); });

   BuildScenarioSettings(tabs);
   BuildSearchSettings(tabs);
   var footer=SettingHint("Настройки панели сохраняются автоматически. Сценарии — кнопкой «Сохранить сценарий».",new Thickness(2,14,0,0));
   Grid.SetRow(footer,2); layout.Children.Add(footer);
   settings.Closed+=delegate { refreshScenarioSettings=null; Save(); settings=null; };
   settings.Show(); settings.Activate();
  }

  StackPanel SettingsPage(TabControl tabs,string title) {
   var panel=new StackPanel { Margin=new Thickness(0,18,8,4) };
   var scroll=new ScrollViewer { Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,CanContentScroll=false };
   scroll.PreviewMouseWheel+=delegate(object sender,MouseWheelEventArgs e) {
    scroll.ScrollToVerticalOffset(scroll.VerticalOffset-e.Delta*0.20);
    e.Handled=true;
   };
   tabs.Items.Add(new TabItem { Header=title,Content=scroll });
   return panel;
  }
  StackPanel SettingsCard(StackPanel page,string title,string description=null) {
   var body=new StackPanel();
   var border=new Border { Child=body,CornerRadius=new CornerRadius(14),Padding=new Thickness(20,18,20,18),Margin=new Thickness(0,0,0,16),Background=SettingsBrush("SettingsCardBackground",Color.FromArgb(166,43,43,43)),BorderBrush=SettingsBrush("SettingsBorder",Color.FromArgb(63,255,255,255)),BorderThickness=new Thickness(1) };
   body.Children.Add(new TextBlock { Text=title,FontSize=16,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,description==null ? 18 : 6) });
   if(description!=null) body.Children.Add(SettingHint(description,new Thickness(0,0,0,18)));
   page.Children.Add(border); return body;
  }
  Brush SettingsBrush(string key,Color fallback) { return settings.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback); }
  TextBlock SettingHint(string text,Thickness margin) { return new TextBlock { Text=text,Foreground=SettingsBrush("SettingsMuted",Color.FromRgb(155,166,185)),FontSize=12,LineHeight=18,TextWrapping=TextWrapping.Wrap,Margin=margin }; }
  CheckBox SettingsToggle(StackPanel panel,string title,bool value,Action<bool> changed) {
   var toggle=new CheckBox { Content=title,IsChecked=value,Margin=new Thickness(0,0,0,16) };
   AutomationProperties.SetName(toggle,title);
   toggle.Click+=delegate { changed(toggle.IsChecked==true); };
   panel.Children.Add(toggle); return toggle;
  }

  FrameworkElement SettingsSlider(StackPanel panel,string title,double minimum,double maximum,double value,double step,string unit,Action<double> changed) {
   var section=new StackPanel { Margin=new Thickness(0,0,0,18) };
   section.Children.Add(new TextBlock { Text=title,Margin=new Thickness(0,0,0,8) });
   var row=new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(88) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(unit.Length==0 ? 0 : 24) });
   double current=double.IsNaN(value)||double.IsInfinity(value) ? minimum : Math.Max(minimum,Math.Min(maximum,value)); bool syncing=false,numberDirty=false;
   var slider=new Slider { Minimum=minimum,Maximum=maximum,Value=current,TickFrequency=step,IsSnapToTickEnabled=true,IsMoveToPointEnabled=true,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,14,0) };
   var number=new TextBox { Text=current.ToString("0.##",CultureInfo.CurrentCulture),MinHeight=34,VerticalContentAlignment=VerticalAlignment.Center,HorizontalContentAlignment=HorizontalAlignment.Center,MaxLength=12 };
   AutomationProperties.SetName(slider,title); AutomationProperties.SetName(number,title+", точное значение");
   row.Children.Add(slider); Grid.SetColumn(number,1); row.Children.Add(number);
   if(unit.Length>0) { var units=new TextBlock { Text=unit,Margin=new Thickness(6,0,0,0),VerticalAlignment=VerticalAlignment.Center,Foreground=SettingsBrush("SettingsMuted",Color.FromRgb(155,166,185)),FontSize=12 }; Grid.SetColumn(units,2); row.Children.Add(units); }
   Action<double,bool> preview=delegate(double proposed,bool normalize) {
    double next=Math.Max(minimum,Math.Min(maximum,proposed));
    next=Math.Max(minimum,Math.Min(maximum,minimum+Math.Round((next-minimum)/step)*step));
    syncing=true;
    try { slider.Value=next; if(normalize) { number.Text=next.ToString("0.##",CultureInfo.CurrentCulture); numberDirty=false; } }
    finally { syncing=false; }
    if(current!=next) { current=next; changed(next); }
   };
   Action commit=delegate {
    if(!numberDirty) { Save(); return; }
    double entered;
    if(TrySettingNumber(number.Text,out entered)) preview(entered,true);
    else { syncing=true; number.Text=current.ToString("0.##",CultureInfo.CurrentCulture); syncing=false; }
    numberDirty=false; Save();
   };
   slider.ValueChanged+=delegate { if(!syncing) preview(slider.Value,true); };
   slider.PreviewMouseLeftButtonUp+=delegate { Save(); };
   slider.LostKeyboardFocus+=delegate { Save(); };
   number.TextChanged+=delegate { if(syncing) return; numberDirty=true; double entered; if(TrySettingNumber(number.Text,out entered) && entered>=minimum && entered<=maximum) preview(entered,false); };
   number.KeyDown+=delegate(object sender,KeyEventArgs e) { if(e.Key==Key.Enter) { commit(); e.Handled=true; } };
   number.LostKeyboardFocus+=delegate { commit(); };
   section.Children.Add(row); panel.Children.Add(section); return section;
  }

  static bool TrySettingNumber(string text,out double value) {
   bool parsed=double.TryParse(text,NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint|NumberStyles.AllowLeadingWhite|NumberStyles.AllowTrailingWhite,CultureInfo.CurrentCulture,out value) || double.TryParse(text,NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint|NumberStyles.AllowLeadingWhite|NumberStyles.AllowTrailingWhite,CultureInfo.InvariantCulture,out value);
   return parsed && !double.IsNaN(value) && !double.IsInfinity(value);
  }
  void AddClockToggle(StackPanel panel,string title,bool value,Action<bool> changed) { SettingsToggle(panel,title,value,delegate(bool enabled) { changed(enabled); UpdateClock(); Save(); }); }
  void AddClockSlider(StackPanel panel,string title,double minimum,double maximum,double value,Action<double> changed) { SettingsSlider(panel,title,minimum,maximum,value,1,"",delegate(double next) { changed(next); UpdateClock(); }); }
 }
}
