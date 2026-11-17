using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Nook {
 partial class DockWindow {
  const int SearchHotkeyId=0x5100;
  SearchWindow appSearchWindow;
  List<SearchApp> searchApps=new List<SearchApp>();
  DateTime searchCatalogTime=DateTime.MinValue;
  string searchPinsSignature="",searchHotkeyError="";
  bool searchReading,searchHotkeyRegistered;
  string SearchDiagnostics { get { return " SearchHotkey="+searchHotkeyRegistered+" SearchApps="+searchApps.Count+" SearchVisible="+(appSearchWindow!=null && appSearchWindow.IsVisible); } }
  void InitializeSearch() {
   searchHotkeyRegistered=RegisterHotKey(handle,SearchHotkeyId,0x4001,32);
   if(!searchHotkeyRegistered) searchHotkeyError="Alt + Пробел занят. Освободите сочетание и перезапустите панель.";
   RefreshSearchCatalog(true);
  }
  void StopSearch() {
   if(searchHotkeyRegistered) { UnregisterHotKey(handle,SearchHotkeyId); searchHotkeyRegistered=false; }
   if(appSearchWindow!=null) { appSearchWindow.Close(); appSearchWindow=null; }
  }
  void ShowAppSearch() {
   if(closing || scenarioEditorOpen) return;
   if(appSearchWindow==null) appSearchWindow=new SearchWindow(prefs,delegate(SearchApp app) { Launch(app.Target); });
   if(appSearchWindow.IsVisible) { appSearchWindow.Hide(); return; }
   appSearchWindow.Open(searchApps); RefreshSearchCatalog(false);
  }
  async void RefreshSearchCatalog(bool force) {
   string signature=string.Join("|",prefs.Pins ?? new List<string>());
   if(closing || searchReading || !force && signature==searchPinsSignature && DateTime.UtcNow-searchCatalogTime<TimeSpan.FromMinutes(5)) return;
   var pins=(prefs.Pins ?? new List<string>()).Select(path=> {
    var details=(prefs.PinDetails ?? new List<PinDetails>()).FirstOrDefault(p=>p!=null && string.Equals(p.LaunchPath,path,StringComparison.OrdinalIgnoreCase));
    return new SearchApp { Name=details!=null && !string.IsNullOrWhiteSpace(details.Title) ? details.Title : Path.GetFileNameWithoutExtension(path),Target=path };
   }).ToList();
   searchReading=true;
   try {
    var result=await AppSearch.ReadAsync(pins);
    if(closing) return;
    searchApps=result; searchCatalogTime=DateTime.UtcNow; searchPinsSignature=signature;
    if(appSearchWindow!=null && appSearchWindow.IsVisible) appSearchWindow.SetCatalog(searchApps);
   } catch(Exception error) { Program.RecordStartup("Application search catalog: "+error.Message); }
   finally { searchReading=false; }
  }
  void BuildSearchSettings(TabControl tabs) {
   var page=SettingsPage(tabs,"Поиск");
   var appearance=SettingsCard(page,"Поиск приложений","Alt + Пробел · приложения меню «Пуск» и закреплённые на панели. Enter — открыть, Esc — закрыть.");
   if(searchHotkeyError.Length>0) appearance.Children.Add(SettingHint(searchHotkeyError,new Thickness(0,0,0,12)));
   var color=new TextBox { Text=prefs.SearchColor,Margin=new Thickness(0,0,0,14),MaxLength=7 };
   appearance.Children.Add(new TextBlock { Text="Цвет фона (#RRGGBB)",Margin=new Thickness(0,0,0,8) }); appearance.Children.Add(color);
   color.LostKeyboardFocus+=delegate { uint rgb; if(color.Text.Length==7 && color.Text[0]=='#' && uint.TryParse(color.Text.Substring(1),System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture,out rgb)) { prefs.SearchColor=color.Text.ToUpperInvariant(); Save(); } color.Text=prefs.SearchColor; };
   SettingsSlider(appearance,"Непрозрачность",20,100,prefs.SearchOpacity*100,1,"%",delegate(double v) { prefs.SearchOpacity=v/100; });
   SettingsSlider(appearance,"Скругление",0,40,prefs.SearchRadius,1,"",delegate(double v) { prefs.SearchRadius=v; });
   SettingsSlider(appearance,"Ширина окна",280,900,prefs.SearchWidth,10,"px",delegate(double v) { prefs.SearchWidth=v; });
   SettingsSlider(appearance,"Размер текста",14,32,prefs.SearchFontSize,1,"",delegate(double v) { prefs.SearchFontSize=v; });
   SettingsSlider(appearance,"По горизонтали",0,100,prefs.SearchX*100,1,"%",delegate(double v) { prefs.SearchX=v/100; });
   SettingsSlider(appearance,"По вертикали",0,100,prefs.SearchY*100,1,"%",delegate(double v) { prefs.SearchY=v/100; });
   appearance.Children.Add(SettingHint("Оформление применяется при следующем открытии. Высота зависит от числа результатов, максимум три.",new Thickness(0)));
   appearance.Children.Add(ScenarioButton("Открыть поиск",ShowAppSearch));
  }
 }
 sealed class SearchWindow : Window {
  readonly Preferences prefs;
  readonly Action<SearchApp> launch;
  readonly TextBox input=new TextBox();
  readonly ListBox results=new ListBox();
  readonly Border plate=new Border();
  readonly Border resultsPlate=new Border();
  readonly TranslateTransform resultsOffset=new TranslateTransform();
  List<SearchApp> catalog=new List<SearchApp>();
  public SearchWindow(Preferences preferences,Action<SearchApp> open) {
   prefs=preferences; launch=open;
   Title="Nook — поиск приложений"; WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize;
   AllowsTransparency=true; Background=Brushes.Transparent; ShowInTaskbar=false; Topmost=true;
   SizeToContent=SizeToContent.Height; UseLayoutRounding=true;
   var body=new StackPanel(); Content=body;
   plate.Padding=new Thickness(16); plate.BorderThickness=new Thickness(1); plate.BorderBrush=new SolidColorBrush(Color.FromArgb(65,255,255,255)); plate.Child=input;
   resultsPlate.Padding=new Thickness(8); resultsPlate.BorderThickness=new Thickness(1,0,1,1); resultsPlate.BorderBrush=plate.BorderBrush;
   resultsPlate.Margin=new Thickness(28,0,28,0); resultsPlate.Child=results; resultsPlate.Visibility=Visibility.Collapsed; resultsPlate.RenderTransform=resultsOffset;
   input.Background=Brushes.Transparent; input.Foreground=Brushes.White; input.CaretBrush=Brushes.White; input.BorderThickness=new Thickness(0); input.Padding=new Thickness(6); input.MaxLength=256;
   System.Windows.Automation.AutomationProperties.SetName(input,"Поиск приложений");
   body.Children.Add(plate); body.Children.Add(resultsPlate);
   results.Background=Brushes.Transparent; results.BorderThickness=new Thickness(0); results.Foreground=Brushes.White; results.DisplayMemberPath="Name"; results.Focusable=false;
   results.Padding=new Thickness(0); results.Tag=48.0;
   results.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty,ScrollBarVisibility.Disabled);
   results.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty,ScrollBarVisibility.Hidden);
   var style=new Style(typeof(ListBoxItem)); style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10,8,10,8))); style.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White)); style.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.Transparent)); style.Setters.Add(new Setter(UIElement.FocusableProperty,false));
   style.Setters.Add(new Setter(System.Windows.Automation.AutomationProperties.NameProperty,new System.Windows.Data.Binding("Name")));
   style.Setters.Add(new Setter(FrameworkElement.HeightProperty,new System.Windows.Data.Binding("Tag") { RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor,typeof(ListBox),1) }));
   var border=new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty,new CornerRadius(7)); border.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background") { RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
   border.SetBinding(Border.PaddingProperty,new System.Windows.Data.Binding("Padding") { RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) }); border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
   style.Setters.Add(new Setter(Control.TemplateProperty,new ControlTemplate(typeof(ListBoxItem)) { VisualTree=border }));
   var selected=new Trigger { Property=ListBoxItem.IsSelectedProperty,Value=true }; selected.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromArgb(65,255,255,255)))); style.Triggers.Add(selected); results.ItemContainerStyle=style;
   input.TextChanged+=delegate { UpdateResults(); };
   results.SelectionChanged+=delegate { if(results.SelectedItem!=null) results.ScrollIntoView(results.SelectedItem); };
   PreviewKeyDown+=delegate(object sender,KeyEventArgs e) {
    if(e.Key==Key.Escape) { Hide(); e.Handled=true; }
    else if(e.Key==Key.Down || e.Key==Key.Up) { if(results.Items.Count>0) results.SelectedIndex=Math.Max(0,Math.Min(results.Items.Count-1,results.SelectedIndex+(e.Key==Key.Down ? 1 : -1))); e.Handled=true; }
    else if(e.Key==Key.Enter) { OpenSelected(); e.Handled=true; }
   };
   results.PreviewMouseLeftButtonUp+=delegate { OpenSelected(); };
   Deactivated+=delegate { Hide(); };
   SizeChanged+=delegate { Position(); };
  }
  public void SetCatalog(List<SearchApp> apps) {
   var selected=results.SelectedItem as SearchApp; catalog=apps; UpdateResults();
   if(selected!=null) for(int i=0;i<results.Items.Count;i++) if(((SearchApp)results.Items[i]).Target==selected.Target) { results.SelectedIndex=i; break; }
  }
  public void Open(List<SearchApp> apps) {
   var work=SystemParameters.WorkArea;
   Width=Math.Min(Math.Max(1,work.Width-24),Preferences.Clamp(prefs.SearchWidth,280,900,520)); MaxHeight=Math.Max(1,work.Height-24);
   FontFamily=new FontFamily("Segoe UI"); FontSize=Preferences.Clamp(prefs.SearchFontSize,14,32,20);
   results.Tag=Math.Ceiling(FontSize*1.6+16);
   double radius=Preferences.Clamp(prefs.SearchRadius,0,40,16);
   plate.CornerRadius=new CornerRadius(radius);
   resultsPlate.CornerRadius=new CornerRadius(0,0,radius,radius);
   var color=(Color)ColorConverter.ConvertFromString(Preferences.NormalizeGlassColor(prefs.SearchColor)); color.A=(byte)Math.Round(255*Preferences.Clamp(prefs.SearchOpacity,.2,1,.88)); plate.Background=resultsPlate.Background=new SolidColorBrush(color);
   catalog=apps; input.Clear(); UpdateResults(); Show(); Position(); Activate();
   Dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(delegate { if(IsVisible) { input.Focus(); Keyboard.Focus(input); } }));
  }
  void UpdateResults() {
   bool wasVisible=resultsPlate.Visibility==Visibility.Visible;
   results.ItemsSource=AppSearch.Find(catalog,input.Text);
   results.Height=Math.Min(3,results.Items.Count)*(double)results.Tag+2;
   results.Visibility=results.Items.Count>0 ? Visibility.Visible : Visibility.Collapsed;
   resultsPlate.Visibility=results.Visibility;
   results.SelectedIndex=results.Items.Count>0 ? 0 : -1;
   if(!wasVisible && results.Items.Count>0 && IsVisible && prefs.Animations) {
    resultsOffset.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(-6,0,TimeSpan.FromMilliseconds(140)) { FillBehavior=FillBehavior.Stop,EasingFunction=new CubicEase { EasingMode=EasingMode.EaseOut } });
    resultsPlate.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(140)) { FillBehavior=FillBehavior.Stop });
   }
  }
  void OpenSelected() { var app=results.SelectedItem as SearchApp; if(app==null) return; Hide(); launch(app); }
  void Position() {
   var work=SystemParameters.WorkArea;
   Left=work.Left+Math.Max(0,work.Width-Width)*Preferences.Clamp(prefs.SearchX,0,1,.5);
   Top=Math.Max(work.Top,Math.Min(work.Bottom-ActualHeight,work.Top+Math.Max(0,work.Height-plate.ActualHeight)*Preferences.Clamp(prefs.SearchY,0,1,.35)));
  }
 }
}
