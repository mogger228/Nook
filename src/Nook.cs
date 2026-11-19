using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace Nook {
 public class PinDetails {
  public string LaunchPath, TargetPath, Title, IconPath, AppId;
 }
 public class Preferences {
  public bool Top = false;
  public bool Left = false;
  public bool AutoHide = false;
  public bool HideWindowsTaskbar = true;
  public int Size = 36;
  public double BackgroundOpacity = 0.89;
  public string GlassColor = "#1A1A1A";
  public double CornerRadius = 11;
  public double BottomOffset = 6;
  [XmlIgnore] public bool GlassNeedsMigration;
  public bool Zoom = true;
  public bool HoverWave = false;
  public double ZoomScale = 1.35;
  public bool ClockEnabled = true;
  public double ClockSize = 138;
  public double ClockX = .5;
  public double ClockY = .3;
  public bool ClockShowBattery = true;
  public bool ClockShowWifi = true;
  public bool ClockShowBluetooth = true;
  public bool ClockShowDate = true;
  public bool ClockShowWeather = true;
  public double ClockStatusSize = 21;
  public double HorizontalIslandClockSize = 22;
  public double HorizontalIslandStatusSize = 18;
  public double VerticalIslandClockSize = 16;
  public double VerticalIslandStatusSize = 18;
  public bool LeftClockIslandEnabled = true;
  public bool RightStatusIslandEnabled = true;
  public bool SideShowBattery = true;
  public bool SideShowWifi = true;
  public bool SideShowLanguage = true;
  public bool Animations = true;
  public bool EdgeHighlight = true;
  public string SearchColor = "#000000";
  public WebSearchOptions WebSearch = new WebSearchOptions();
  public double SearchOpacity = .88,SearchRadius = 24,SearchWidth = 520,SearchFontSize = 19,SearchX = .5,SearchY = .35;
  public bool InitialSetupComplete = true;
  public string PinImportStatus;
  public List<string> Pins = new List<string>();
  public List<PinDetails> PinDetails = new List<PinDetails>();
  public List<ScenarioDefinition> Scenarios = new List<ScenarioDefinition>();
  public static string DataFolderOverride;
  public static string Folder { get {
   if(DataFolderOverride!=null) return DataFolderOverride;
   if(File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"portable.flag"))) return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data");
   string portable=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","data"));
   if(File.Exists(Path.Combine(portable,"settings.xml"))) return portable;
   string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
   string current=Path.Combine(local,"Nook"),legacy=Path.Combine(local,"IslandDock");
   // Keep settings created before the rename when the portable data folder is absent.
   return !File.Exists(Path.Combine(current,"settings.xml")) && File.Exists(Path.Combine(legacy,"settings.xml")) ? legacy : current;
  } }
  public static Preferences Load() {
   if(!File.Exists(Path.Combine(Folder,"settings.xml"))) return new Preferences { InitialSetupComplete=false };
   try { using (var file = File.OpenRead(Path.Combine(Folder,"settings.xml"))) { var p = (Preferences)new XmlSerializer(typeof(Preferences)).Deserialize(file); if(string.IsNullOrWhiteSpace(p.GlassColor)) { p.GlassColor="#303846"; p.BackgroundOpacity=.55; p.GlassNeedsMigration=true; } p.GlassColor=NormalizeGlassColor(p.GlassColor); p.CornerRadius=Clamp(p.CornerRadius,0,40,18); p.BottomOffset=Clamp(p.BottomOffset,0,80,7); p.Size = Math.Max(32,Math.Min(72,p.Size)); p.BackgroundOpacity=Clamp(p.BackgroundOpacity,.2,1,.55); p.ZoomScale=Clamp(p.ZoomScale,1,2,1.18); p.ClockSize=Clamp(p.ClockSize,24,160,64); p.ClockX=Clamp(p.ClockX,0,1,.78); p.ClockY=Clamp(p.ClockY,0,1,.12); p.ClockStatusSize=Clamp(p.ClockStatusSize,10,32,14); p.HorizontalIslandClockSize=Clamp(p.HorizontalIslandClockSize,12,32,22); p.HorizontalIslandStatusSize=Clamp(p.HorizontalIslandStatusSize>0 ? p.HorizontalIslandStatusSize : p.ClockStatusSize,10,32,14); p.VerticalIslandClockSize=Clamp(p.VerticalIslandClockSize,10,22,16); p.VerticalIslandStatusSize=Clamp(p.VerticalIslandStatusSize,10,20,13); if (p.Pins == null) p.Pins = new List<string>(); if(p.PinDetails==null) p.PinDetails=new List<PinDetails>(); return p; } } catch(Exception error) { throw new InvalidOperationException("Не удалось прочитать настройки. Файл сохранён без изменений: "+Path.Combine(Folder,"settings.xml"),error); }
  }
  public static string NormalizeGlassColor(string value) { uint rgb; return value!=null && value.Length==7 && value[0]=='#' && uint.TryParse(value.Substring(1),System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture,out rgb) ? "#"+rgb.ToString("X6") : "#303846"; }
  public static double Clamp(double value,double min,double max,double fallback) { return double.IsNaN(value)||double.IsInfinity(value) ? fallback : Math.Max(min,Math.Min(max,value)); }
  public void Save() {
   Directory.CreateDirectory(Folder);
   var target = Path.Combine(Folder,"settings.xml"); var temp = target + ".tmp";
   using (var file = File.Create(temp)) new XmlSerializer(typeof(Preferences)).Serialize(file,this);
   if (File.Exists(target)) File.Replace(temp,target,null); else File.Move(temp,target);
  }
 }
 static class Program {
  internal static void RecordStartup(string text) { try { Directory.CreateDirectory(Preferences.Folder); File.AppendAllText(Path.Combine(Preferences.Folder,"startup-state.txt"),DateTimeOffset.Now.ToString("o")+" "+text+Environment.NewLine); } catch { } }
  internal static void RestoreAfterFailure() { try { Native.RestoreTaskbars(); } catch(Exception error) { RecordStartup("Taskbar restoration: "+error.Message); } }
  [STAThread] public static void Main(string[] args) {
   if(TaskTerminator.HandleHelper(args) || TaskTerminationChecks.HandleProbe(args)) return;
   var dataArgument=args.FirstOrDefault(a=>a.StartsWith("--data-dir=",StringComparison.OrdinalIgnoreCase));
   if(dataArgument!=null) Preferences.DataFolderOverride=Path.GetFullPath(dataArgument.Substring(11));
   if(args.Contains("--enable-startup")) {
    DockWindow.SetStartup(true);
    Directory.CreateDirectory(Preferences.Folder);
    using(var key=Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run")) File.WriteAllText(Path.Combine(Preferences.Folder,"startup-status.txt"),Convert.ToString(key.GetValue("Nook")));
    return;
   }
   if(!args.Contains("--status") && !args.Contains("--exit") && !args.Contains("--restore-taskbar")) RecordStartup("Config="+Preferences.Folder);
   if(args.Contains("--restore-taskbar")) { Native.SignalDock(0x8005); try { Native.RestoreTaskbars(); } catch(Exception error) { MessageBox.Show(error.Message,"Nook: восстановление панели Windows"); } return; }
   if(args.Contains("--self-test")) { SelfTest.Run(); return; }
   if(args.Contains("--exit")) { Native.SignalDock(0x8003); return; }
   if(args.Contains("--status")) { Native.SignalDock(0x8004); return; }
   if(Environment.OSVersion.Version.Build<22000) { MessageBox.Show("Эта сборка Nook предназначена для Windows 11 x64.","Nook: совместимость"); return; }
   bool created;
   using (var mutex = new System.Threading.Mutex(true,"Local\\Nook.SingleInstance",out created)) {
    if (!created) { if(args.Contains("--settings")) Native.SignalDock(0x8002); return; }
    var app = new Application(); app.ShutdownMode = ShutdownMode.OnMainWindowClose;
    DockWindow dock=null;
    app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e) { if(dock!=null) dock.PrepareForFatalShutdown(); RestoreAfterFailure(); try { Directory.CreateDirectory(Preferences.Folder); File.WriteAllText(Path.Combine(Preferences.Folder,"error.log"),e.Exception.ToString()); } catch { } if(!args.Contains("--smoke-test")) MessageBox.Show(e.Exception.Message,"Nook: ошибка"); e.Handled = true; app.Shutdown(1); };
    AppDomain.CurrentDomain.UnhandledException += delegate { RestoreAfterFailure(); };
    try { dock=new DockWindow(); } catch(Exception error) { RestoreAfterFailure(); MessageBox.Show(error.Message,"Nook: настройки"); return; }
    if(args.Contains("--settings")) dock.Loaded += delegate { dock.Dispatcher.BeginInvoke(new Action(dock.ShowSettings)); };
    if(args.Contains("--diagnostics")) {
     var probe=new DispatcherTimer { Interval=TimeSpan.FromSeconds(1) };
     probe.Tick += delegate { probe.Stop(); Directory.CreateDirectory(Preferences.Folder); File.WriteAllText(Path.Combine(Preferences.Folder,"diagnostics.txt"),dock.Diagnostics()); };
     probe.Start();
    }
    if(args.Contains("--smoke-test")) {
     var timer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(2) };
     timer.Tick += delegate { timer.Stop(); bool ok=dock.IsLoaded && dock.IsVisible && dock.ActualWidth>50 && dock.ActualHeight>20; File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"smoke-test.txt"),(ok ? "PASS " : "FAIL ")+dock.Diagnostics()); Environment.ExitCode=ok ? 0 : 1; dock.Close(); };
     timer.Start();
    }
    app.Run(dock);
   }
  }
 }
 class AppItem {
  public string Key, Path, Title, IconPath;
  public List<IntPtr> Windows = new List<IntPtr>();
  public Dictionary<IntPtr,uint> WindowProcesses = new Dictionary<IntPtr,uint>();
  public bool Pinned;
  public string IconKey { get { return Pinned ? Path : Key; } }
 }
 partial class DockWindow : Window {
  readonly Preferences prefs = Preferences.Load();
  readonly StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
  readonly Border island = new Border();
  readonly Grid surface = new Grid();
  readonly DispatcherTimer refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
  readonly Dictionary<string, ImageSource> icons = new Dictionary<string,ImageSource>(StringComparer.OrdinalIgnoreCase);
  readonly Dictionary<IntPtr,Border> indicators = new Dictionary<IntPtr,Border>();
  readonly Dictionary<IntPtr,Border> indicatorHalos = new Dictionary<IntPtr,Border>();
  readonly List<IntPtr> hooks = new List<IntPtr>();
  readonly Native.WinEventDelegate eventCallback;
  string signature = "";
  IntPtr handle;
  Window settings;
  bool closing;
  bool taskbarHidden;
  AppBar appbar;
  readonly uint taskbarCreatedMessage=Native.RegisterWindowMessage("TaskbarCreated");
  DesktopClock desktopClock;
  bool IsVertical { get { return prefs.Left; } }
  double HoverHeadroom { get { return Math.Ceiling(Math.Max(16,prefs.Zoom ? (BandHeight-6)*(prefs.ZoomScale-1)+4 : 16)); } }
  double NativeBandHeight { get { var visual=PresentationSource.FromVisual(this); double scale=visual==null ? 1 : visual.CompositionTarget.TransformFromDevice.M22; return Native.TaskbarHeight(handle)*scale; } }
  double BandHeight { get { return Math.Max(NativeBandHeight,prefs.Size); } }
  public string Diagnostics() {
   var fg=Native.GetForegroundWindow(); var monitor=Native.MonitorRect(handle);
   return DateTimeOffset.Now.ToString("o")+" Loaded="+IsLoaded+" Visible="+IsVisible+" NativeVisible="+Native.IsWindowVisible(handle)+" Visibility="+Visibility+" Handle="+handle+" Left="+Left+" Top="+Top+" Width="+ActualWidth+" Height="+ActualHeight+" Foreground="+Native.Title(fg)+" Maximized="+Native.IsZoomed(fg)+" Monitor="+monitor.Left+","+monitor.Top+","+monitor.Right+","+monitor.Bottom+" WorkArea="+SystemParameters.WorkArea+" TaskbarVisible="+Native.TaskbarVisible+" ZoomScale="+prefs.ZoomScale+" Clock="+(desktopClock==null ? "disabled" : desktopClock.Diagnostics+" Visible="+desktopClock.IsVisible+" Rect="+desktopClock.Left+","+desktopClock.Top+","+desktopClock.Width+","+desktopClock.Height)+" Config="+Preferences.Folder+" Color="+prefs.GlassColor+" Opacity="+prefs.BackgroundOpacity+" IconSize="+prefs.Size+" PinCount="+prefs.Pins.Count+" Glass="+"transparent tint; blur paused"+PlacementDiagnostics+ScenarioDiagnostics+SearchDiagnostics;
  }
  public DockWindow() {
   Program.RecordStartup("Preferences loaded: Color="+prefs.GlassColor+" Opacity="+prefs.BackgroundOpacity+" IconSize="+prefs.Size+" Zoom="+prefs.ZoomScale+" Clock="+prefs.ClockSize+" X="+prefs.ClockX+" Y="+prefs.ClockY+" Pins="+prefs.Pins.Count);
   Title = "Nook"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
   AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; Topmost = true; ShowActivated = false;
   if(Environment.GetCommandLineArgs().Contains("--diagnostics")) ShowInTaskbar=true;
   SizeToContent = SizeToContent.Manual; Width = 300; Height=56;
   island.CornerRadius = new CornerRadius(18); island.Padding = new Thickness(12,2,12,2); island.Child = row; island.VerticalAlignment=VerticalAlignment.Bottom;
   island.BorderBrush = new SolidColorBrush(Color.FromArgb(55,255,255,255)); island.BorderThickness = new Thickness(1);
   surface.Children.Add(island); Content=surface; InitializeGlassDesign(surface); InitializeSideIslands(surface); InitializeHoverWave(); InitializeAutoHide();
   eventCallback = OnWinEvent;
   refresh.Tick += delegate { refresh.Stop(); if (!closing) RefreshApps(false); };
   SourceInitialized += delegate { InitializeNative(); };
   Loaded += delegate { if(closing || appbar!=null) return; if(!prefs.InitialSetupComplete) prefs.Save(); InitializeNative(); appbar=new AppBar(HwndSource.FromHwnd(handle),delegate { if(!closing) Place(); }); if(prefs.HideWindowsTaskbar) { taskbarHidden=true; Native.HideTaskbars(); } InstallHooks(); RefreshApps(true); UpdateClock(); StartSideIslands(); RegisterScenarioHotkeys(); InitializeSearch(); InitializeFirstRun(); if(prefs.GlassNeedsMigration) { Save(); prefs.GlassNeedsMigration=false; } Program.RecordStartup(Diagnostics()); };
   Closing += delegate { closing=true; StopSearch(); UnregisterScenarioHotkeys(); StopAutoHide(); ResetHoverWave(true); StopSideIslands(); if(appbar!=null) appbar.Dispose(); if(desktopClock!=null) { desktopClock.Close(); desktopClock=null; } };
   Closed += delegate { closing=true; if(appbar!=null) appbar.Dispose(); if(taskbarHidden) Program.RestoreAfterFailure(); refresh.Stop(); foreach(var hook in hooks) Native.UnhookWinEvent(hook); hooks.Clear(); SystemEvents.DisplaySettingsChanged -= DisplayChanged; SystemEvents.UserPreferenceChanged -= UserPreferenceChanged; if(settings != null) settings.Close(); };
   SystemEvents.DisplaySettingsChanged += DisplayChanged; SystemEvents.UserPreferenceChanged += UserPreferenceChanged;
   ContextMenu = MakeGlobalMenu();
  }
  void InitializeNative() {
   if(handle!=IntPtr.Zero) return;
   var nativeHandle=new WindowInteropHelper(this).EnsureHandle();
   if(handle!=IntPtr.Zero) return;
   handle=nativeHandle;
   if(!ShowInTaskbar) Native.SetWindowLongPtr(handle,-20,new IntPtr(Native.GetWindowLongPtr(handle,-20).ToInt64() | 0x08000000L | 0x80L));
   HwndSource.FromHwnd(handle).AddHook(WndProc);
  }
  internal void PrepareForFatalShutdown() {
   closing=true; refresh.Stop();
   StopSearch();
   UnregisterScenarioHotkeys();
   StopAutoHide(); ResetHoverWave(false);
   if(appbar!=null) appbar.Dispose();
  }
  void DisplayChanged(object sender,EventArgs e) { if(closing) return; Dispatcher.BeginInvoke(new Action(delegate { if(closing) return; if(appbar!=null) appbar.Invalidate(); RefreshApps(true); UpdateClock(); })); }
  void UserPreferenceChanged(object sender,UserPreferenceChangedEventArgs e) { if(closing) return; Dispatcher.BeginInvoke(new Action(delegate { if(!closing) Place(); })); }
  IntPtr WndProc(IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled) {
   if(msg==0x0312 && w.ToInt32()==SearchHotkeyId) { if(!closing) Dispatcher.BeginInvoke(new Action(ShowAppSearch)); handled=true; return IntPtr.Zero; }
   if(msg==0x0312 && w.ToInt32()==WebSearchHotkeyId) { if(!closing) Dispatcher.BeginInvoke(new Action(ShowWebSearch)); handled=true; return IntPtr.Zero; }
   if(msg==0x112 && (w.ToInt64()&0xFFF0)==0xF060) { handled=true; return IntPtr.Zero; }
   if(msg==0x0312 && HandleScenarioHotkey(w,l)) { handled=true; return IntPtr.Zero; }
   if(msg==taskbarCreatedMessage && !closing) Dispatcher.BeginInvoke(new Action(delegate { if(closing) return; if(taskbarHidden) Native.HideTaskbars(); if(appbar!=null) appbar.Invalidate(); Place(); UpdateClock(); }));
   if(msg==0x1E && desktopClock!=null && !closing) desktopClock.RefreshTime();
   if(msg==0x0218 && w.ToInt64()==0x0A && !closing) Dispatcher.BeginInvoke(new Action(PowerStatusChanged));
   if(msg==0x8002) { Dispatcher.BeginInvoke(new Action(ShowSettings)); handled=true; }
   if(msg==0x8003) { Dispatcher.BeginInvoke(new Action(Close)); handled=true; }
   if(msg==0x8004) { Directory.CreateDirectory(Preferences.Folder); File.WriteAllText(Path.Combine(Preferences.Folder,"diagnostics.txt"),Diagnostics()+" HoverWave="+HoverWaveDiagnostics); handled=true; }
   if(msg==0x8005) { Dispatcher.BeginInvoke(new Action(delegate { taskbarHidden=false; prefs.HideWindowsTaskbar=false; Native.RestoreTaskbars(); Place(); Save(); })); handled=true; }
   if(msg == 0x02E0) Dispatcher.BeginInvoke(new Action(delegate { RefreshApps(true); }));
   return IntPtr.Zero;
  }
  void InstallHooks() {
   hooks.Add(Native.SetWinEventHook(3,3,IntPtr.Zero,eventCallback,0,0,2));
   hooks.Add(Native.SetWinEventHook(0x8000,0x8003,IntPtr.Zero,eventCallback,0,0,2));
   hooks.Add(Native.SetWinEventHook(0x800C,0x800C,IntPtr.Zero,eventCallback,0,0,2));
   hooks.Add(Native.SetWinEventHook(0x800B,0x800B,IntPtr.Zero,eventCallback,0,0,2));
   if(hooks.Any(h => h == IntPtr.Zero)) MessageBox.Show("Не удалось подписаться на часть событий Windows. Список окон можно обновить из меню панели.","Nook");
  }
  void OnWinEvent(IntPtr hook,uint evt,IntPtr hwnd,int obj,int child,uint thread,uint time) {
   if(closing || hwnd == IntPtr.Zero || (evt != 3 && obj != 0)) return;
   if(evt==3) Dispatcher.BeginInvoke(new Action(delegate { if(closing) return; if(desktopClock!=null) desktopClock.UpdateDesktopOrder(); CheckFullscreen(Native.GetForegroundWindow()); }));
   if(evt==0x800B) { if(hwnd==Native.GetForegroundWindow()) Dispatcher.BeginInvoke(new Action(delegate { if(!closing) CheckFullscreen(hwnd); })); return; }
   Dispatcher.BeginInvoke(new Action(delegate { if(closing) return; if(!refresh.IsEnabled) refresh.Start(); }));
  }
  List<AppItem> GetApps() {
   var result = new List<AppItem>(); var byKey = new Dictionary<string,AppItem>(StringComparer.OrdinalIgnoreCase);
   var processPaths=new Dictionary<uint,string>(); var processAppIds=new Dictionary<uint,string>();
   foreach(string path in prefs.Pins.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase)) {
    var detail=prefs.PinDetails.FirstOrDefault(x=>x!=null && string.Equals(x.LaunchPath,path,StringComparison.OrdinalIgnoreCase));
    string key=detail!=null && !string.IsNullOrWhiteSpace(detail.AppId) ? "appid:"+detail.AppId : detail!=null && !string.IsNullOrWhiteSpace(detail.TargetPath) ? detail.TargetPath : path;
    var item = new AppItem { Key=key,Path=path,Title=detail!=null && !string.IsNullOrWhiteSpace(detail.Title) ? detail.Title : System.IO.Path.GetFileNameWithoutExtension(path),IconPath=detail==null ? null : detail.IconPath,Pinned=true }; result.Add(item);
    if(!byKey.ContainsKey(key)) byKey[key]=item;
    // Keep executable alias for unpackaged applications whose process exposes no AUMID.
    if(detail!=null && !string.IsNullOrWhiteSpace(detail.TargetPath) && !byKey.ContainsKey(detail.TargetPath)) byKey[detail.TargetPath]=item;
   }
   Native.EnumWindows(delegate(IntPtr hwnd,IntPtr unused) {
    if(!Native.IsTaskWindow(hwnd,handle)) return true;
    string title = Native.Title(hwnd); if(string.IsNullOrWhiteSpace(title)) return true;
    uint pid=Native.ProcessId(hwnd); string path,appId;
    if(!processPaths.TryGetValue(pid,out path)) processPaths[pid]=path=Native.ProcessPath(pid);
    if(!processAppIds.TryGetValue(pid,out appId)) processAppIds[pid]=appId=Native.WindowAppId(pid);
    string key = string.IsNullOrEmpty(path) ? "window:" + hwnd.ToInt64() : path;
    AppItem item;
    if(!string.IsNullOrEmpty(appId) && byKey.TryGetValue("appid:"+appId,out item)) { }
    else if(!byKey.TryGetValue(key,out item)) { item = new AppItem { Key=key,Path=path,Title=title }; result.Add(item); byKey[key]=item; }
    item.Windows.Add(hwnd); item.WindowProcesses[hwnd]=pid; if(!item.Pinned) item.Title = title;
    return true;
   },IntPtr.Zero);
   return ApplySessionOrder(result);
  }
  void RefreshApps(bool force) {
   var apps = GetApps(); var foreground = Native.GetForegroundWindow();
   string next = prefs.Size + ":" + prefs.BackgroundOpacity + ":" + prefs.Top + ":" + prefs.Left + ":" + prefs.Zoom + ":" + prefs.Animations + "|" + string.Join("|",apps.Select(x=>x.Key+":"+x.Title+":"+string.Join(",",x.Windows)));
   if(force || signature != next) {
    signature=next; CancelReorderForRefresh(); ResetHoverWave(true); row.Children.Clear(); indicators.Clear(); indicatorHalos.Clear();
    ApplyGlassAppearance();
    AddSystemButton("⊞","Пуск",delegate { Native.Hotkey(0); });
    foreach(var item in apps) AddApp(item,foreground);
    AddSystemButton("⌕","Поиск",delegate { Native.Hotkey(0x53); });
    AddSystemButton("⋯","Настройки и системные функции",delegate { var menu=MakeGlobalMenu(); menu.PlacementTarget=this; menu.IsOpen=true; });
    Place();
    var alive = new HashSet<string>(apps.Select(x=>x.IconKey),StringComparer.OrdinalIgnoreCase);
    foreach(string key in icons.Keys.Where(x=>!alive.Contains(x)).ToArray()) icons.Remove(key);
   }
   Border activeIndicator;
   indicators.TryGetValue(foreground,out activeIndicator);
   foreach(var pair in indicators) {
    bool active=object.ReferenceEquals(pair.Value,activeIndicator);
    pair.Value.Background=active ? Brushes.LightSkyBlue : new SolidColorBrush(Color.FromRgb(205,211,222));
    Border halo; if(indicatorHalos.TryGetValue(pair.Key,out halo)) halo.Background=active ? new SolidColorBrush(Color.FromArgb(64,125,190,255)) : new SolidColorBrush(Color.FromArgb(30,205,211,222));
   }
   CheckFullscreen(foreground);
  }
  void Place() { PlaceDock(); }
  void Animate(DependencyObject target,DependencyProperty property,double value,int duration) {
   double current=(double)target.GetValue(property);
   var animatable=target as IAnimatable;
   if(double.IsNaN(current)||double.IsInfinity(current)||!prefs.Animations||duration<=0||animatable==null||Math.Abs(current-value)<.001) {
    if(animatable!=null) animatable.BeginAnimation(property,null);
    target.SetValue(property,value); return;
   }
   var animation = new DoubleAnimation(current,value,TimeSpan.FromMilliseconds(duration)) { FillBehavior=FillBehavior.Stop,EasingFunction = new CubicEase { EasingMode=EasingMode.EaseOut } };
   target.SetValue(property,value);
   var clock=(AnimationClock)animation.CreateClock(true);
   clock.Completed+=delegate { if(clock.Controller!=null) clock.Controller.Remove(); };
   animatable.ApplyAnimationClock(property,clock,HandoffBehavior.SnapshotAndReplace);
  }
  Button BaseButton(string title,bool zoomOnHover=true) {
   var button = new Button { Width=IsVertical ? BandHeight-6 : prefs.Size+8,Height=IsVertical ? prefs.Size+8 : BandHeight-6,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Foreground=Brushes.White,ToolTip=title,Cursor=System.Windows.Input.Cursors.Hand,Padding=new Thickness(3),Focusable=false };
   // Explicit template prevents the Windows theme from drawing blue hover/pressed chrome.
   var hitArea=new FrameworkElementFactory(typeof(Border));
   hitArea.SetValue(Border.BackgroundProperty,Brushes.Transparent);
   hitArea.SetValue(Border.PaddingProperty,new TemplateBindingExtension(Control.PaddingProperty));
   var content=new FrameworkElementFactory(typeof(ContentPresenter));
   content.SetValue(ContentPresenter.ContentProperty,new TemplateBindingExtension(ContentControl.ContentProperty));
   content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);
   content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
   hitArea.AppendChild(content);
   button.Template=new ControlTemplate(typeof(Button)) { VisualTree=hitArea };
   AutomationProperties.SetName(button,title);
   var hoverScale = new ScaleTransform(1,1); var pressScale = new ScaleTransform(1,1);
   var waveOffset=new TranslateTransform();
   var transforms=new TransformGroup(); transforms.Children.Add(hoverScale); transforms.Children.Add(pressScale); transforms.Children.Add(waveOffset);
   RegisterHoverWave(button,hoverScale,waveOffset,zoomOnHover);
   button.RenderTransform=transforms; button.RenderTransformOrigin=IsVertical ? new Point(0,.5) : new Point(.5,prefs.Top ? 0 : 1);
   if(zoomOnHover) {
    button.MouseEnter += delegate { if(prefs.Zoom && !prefs.HoverWave) { Animate(hoverScale,ScaleTransform.ScaleXProperty,prefs.ZoomScale,140); Animate(hoverScale,ScaleTransform.ScaleYProperty,prefs.ZoomScale,140); } };
    button.MouseLeave += delegate { if(!prefs.HoverWave) { Animate(hoverScale,ScaleTransform.ScaleXProperty,1,170); Animate(hoverScale,ScaleTransform.ScaleYProperty,1,170); } };
   }
   button.Tag=pressScale;
   return button;
  }
  void PlayClickSpring(Button button) {
   if(!prefs.Animations || button==null) return;
   var scale=button.Tag as ScaleTransform; if(scale==null) return;
   AnimateSpring(scale,ScaleTransform.ScaleXProperty);
   AnimateSpring(scale,ScaleTransform.ScaleYProperty);
  }
  static void AnimateSpring(ScaleTransform scale,DependencyProperty property) {
   double current=(double)scale.GetValue(property);
   var animation=new DoubleAnimationUsingKeyFrames { Duration=TimeSpan.FromMilliseconds(380),FillBehavior=FillBehavior.Stop };
   animation.KeyFrames.Add(new EasingDoubleKeyFrame(current,KeyTime.FromTimeSpan(TimeSpan.Zero)));
   animation.KeyFrames.Add(new EasingDoubleKeyFrame(.91,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(85)),new CubicEase { EasingMode=EasingMode.EaseOut }));
   animation.KeyFrames.Add(new EasingDoubleKeyFrame(1.07,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(190)),new CubicEase { EasingMode=EasingMode.EaseOut }));
   animation.KeyFrames.Add(new EasingDoubleKeyFrame(.985,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(285)),new CubicEase { EasingMode=EasingMode.EaseOut }));
   animation.KeyFrames.Add(new EasingDoubleKeyFrame(1,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(380)),new CubicEase { EasingMode=EasingMode.EaseOut }));
   scale.SetValue(property,1.0);
   var clock=(AnimationClock)animation.CreateClock(true);
   clock.Completed+=delegate { if(clock.Controller!=null) clock.Controller.Remove(); };
   scale.ApplyAnimationClock(property,clock,HandoffBehavior.SnapshotAndReplace);
  }
  void AddSystemButton(string glyph,string title,Action action) {
   bool startButton=glyph=="⊞";
   var button = BaseButton(title,!startButton && glyph!="⋯");
   List<Border> settingsDots=null;
   if(startButton) {
    double size=Math.Min(prefs.Size*.6,BandHeight-20);
    var logo=new Grid { Width=size,Height=size,IsHitTestVisible=false };
    var white=new Image { Source=CreateGlassStartIcon(size,false),Width=size,Height=size,SnapsToDevicePixels=true };
    var dark=new Image { Source=CreateGlassStartIcon(size,true),Width=size,Height=size,SnapsToDevicePixels=true,Opacity=0 };
    logo.Children.Add(white); logo.Children.Add(dark); button.Content=logo;
    button.MouseEnter+=delegate { Animate(dark,UIElement.OpacityProperty,1,180); Animate(white,UIElement.OpacityProperty,0,180); };
    button.MouseLeave+=delegate { Animate(dark,UIElement.OpacityProperty,0,180); Animate(white,UIElement.OpacityProperty,1,180); };
   } else if(glyph=="⋯") {
    settingsDots=new List<Border>();
    var dots=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center };
    for(int i=0;i<3;i++) {
     var dot=new Border { Width=4,Height=4,CornerRadius=new CornerRadius(2),Background=Brushes.White,Opacity=.62,Margin=new Thickness(i==0?0:2,0,0,0),IsHitTestVisible=false };
     settingsDots.Add(dot); dots.Children.Add(dot);
    }
    button.Content=dots;
    var animatedDots=settingsDots;
    button.MouseEnter+=delegate { if(prefs.Animations) AnimateSettingsDots(animatedDots,false); };
    button.MouseLeave+=delegate { if(prefs.Animations) AnimateSettingsDots(animatedDots,true); };
   } else button.Content=new TextBlock { Text=glyph,FontSize=prefs.Size*.62,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center };
   button.Click += delegate { PlayClickSpring(button); if(settingsDots!=null && prefs.Animations) AnimateSettingsDotsClick(settingsDots); action(); }; row.Children.Add(button);
  }
  static void AnimateSettingsDots(List<Border> dots,bool dimInReverse) {
   if(dots==null) return;
   for(int i=0;i<dots.Count;i++) {
    int index=dimInReverse ? dots.Count-1-i : i;
    Border dot=dots[index]; double target=dimInReverse ? .62 : 1.0;
    var animation=new DoubleAnimation(dot.Opacity,target,TimeSpan.FromMilliseconds(95)) {
     BeginTime=TimeSpan.FromMilliseconds(i*48),FillBehavior=FillBehavior.Stop,
     EasingFunction=new CubicEase { EasingMode=EasingMode.EaseOut }
    };
    dot.Opacity=target;
    var clock=(AnimationClock)animation.CreateClock(true);
    clock.Completed+=delegate { if(clock.Controller!=null) clock.Controller.Remove(); };
    dot.ApplyAnimationClock(UIElement.OpacityProperty,clock,HandoffBehavior.SnapshotAndReplace);
   }
  }
  static void AnimateSettingsDotsClick(List<Border> dots) {
   if(dots==null) return;
   for(int i=0;i<dots.Count;i++) {
    Border dot=dots[i];
    var animation=new DoubleAnimationUsingKeyFrames { Duration=TimeSpan.FromMilliseconds(145),FillBehavior=FillBehavior.Stop,BeginTime=TimeSpan.FromMilliseconds(i*42) };
    animation.KeyFrames.Add(new EasingDoubleKeyFrame(.2,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(55)),new CubicEase { EasingMode=EasingMode.EaseOut }));
    animation.KeyFrames.Add(new EasingDoubleKeyFrame(1,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(145)),new CubicEase { EasingMode=EasingMode.EaseOut }));
    dot.Opacity=1;
    var clock=(AnimationClock)animation.CreateClock(true);
    clock.Completed+=delegate { if(clock.Controller!=null) clock.Controller.Remove(); };
    dot.ApplyAnimationClock(UIElement.OpacityProperty,clock,HandoffBehavior.SnapshotAndReplace);
   }
  }
  void AddApp(AppItem item,IntPtr foreground) {
   var button=BaseButton(item.Title); var panel=new Grid { Width=IsVertical ? BandHeight-12 : prefs.Size+2,Height=IsVertical ? prefs.Size+2 : BandHeight-12 };
   double iconSize=Math.Min(prefs.Size*.74,BandHeight-18);
   AddIconImage(panel,item,iconSize);
   var indicatorLayer=new Grid { Width=9,Height=9,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,0,-1),IsHitTestVisible=false };
   var halo=new Border { Width=8,Height=8,CornerRadius=new CornerRadius(4),Background=item.Windows.Count>0 ? new SolidColorBrush(Color.FromArgb(30,205,211,222)) : Brushes.Transparent,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false };
   var indicator=new Border { Width=4,Height=4,CornerRadius=new CornerRadius(2),Background=item.Windows.Contains(foreground) ? Brushes.LightSkyBlue : item.Windows.Count>0 ? new SolidColorBrush(Color.FromRgb(205,211,222)) : Brushes.Transparent,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false };
   indicatorLayer.Children.Add(halo); indicatorLayer.Children.Add(indicator); panel.Children.Add(indicatorLayer);
   foreach(var hwnd in item.Windows) { indicators[hwnd]=indicator; indicatorHalos[hwnd]=halo; }
   button.Content=panel;
   button.Click += delegate {
    PlayClickSpring(button);
    if(item.Windows.Count==0) Launch(item.Path);
    else if(item.Windows.Count==1) Native.Activate(item.Windows[0]);
    else { var menu = CreateDockMenu(); foreach(var hwnd in item.Windows) { var target=hwnd; AddMenu(menu,Native.Title(hwnd),delegate { Native.Activate(target); }); } menu.PlacementTarget=button; menu.IsOpen=true; }
   };
   var context = CreateDockMenu();
   if(!string.IsNullOrEmpty(item.Path)) { AddMenu(context,"Открыть новое окно",delegate { Launch(item.Path); }); AddMenu(context,item.Pinned ? "Открепить" : "Закрепить",delegate { if(item.Pinned) { prefs.Pins.RemoveAll(x=>string.Equals(x,item.Path,StringComparison.OrdinalIgnoreCase)); prefs.PinDetails.RemoveAll(x=>x!=null && string.Equals(x.LaunchPath,item.Path,StringComparison.OrdinalIgnoreCase)); } else prefs.Pins.Add(item.Path); Save(); RefreshApps(true); }); }
   foreach(var hwnd in item.Windows) { var target=hwnd; AddMenu(context,"Переключиться: "+Native.Title(hwnd),delegate { Native.Activate(target); }); }
   if(item.Windows.Count>0) AddEndTask(context,item);
   button.ContextMenu=context;
   button.AllowDrop=true;
   button.DragOver += delegate(object sender,DragEventArgs e) { e.Effects=(!string.IsNullOrEmpty(item.Path) && e.Data.GetDataPresent(DataFormats.FileDrop)) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled=true; };
   button.Drop += delegate(object sender,DragEventArgs e) {
    if(!string.IsNullOrEmpty(item.Path) && e.Data.GetDataPresent(DataFormats.FileDrop)) { var files=(string[])e.Data.GetData(DataFormats.FileDrop); Launch(item.Path,string.Join(" ",files.Select(QuoteArgument))); } e.Handled=true;
   };
   AttachReorder(button,item); row.Children.Add(button);
  }
  public static string QuoteArgument(string argument) {
   var result=new StringBuilder("\""); int slashes=0;
   foreach(char c in argument) { if(c=='\\') { slashes++; continue; } if(c=='\"') { result.Append('\\',slashes*2+1); result.Append(c); } else { result.Append('\\',slashes); result.Append(c); } slashes=0; }
   result.Append('\\',slashes*2); result.Append('"'); return result.ToString();
  }
  static void Launch(string target,string args="") { try { Process.Start(new ProcessStartInfo(target,args) { UseShellExecute=true }); } catch(Exception ex) { MessageBox.Show(ex.Message,"Не удалось открыть"); } }
  void Save() { try { prefs.Save(); } catch(Exception ex) { MessageBox.Show(ex.Message,"Не удалось сохранить настройки"); } }
  static void AddMenu(ContextMenu menu,string label,Action action) { var item=new MenuItem { Header=label }; item.Click += delegate { action(); }; menu.Items.Add(item); }
  ContextMenu MakeGlobalMenu() {
   var menu=CreateDockMenu();
   AddMenu(menu,"Настройки Nook…",ShowSettings);
   AddScenariosMenu(menu);
   AddMenu(menu,"Вернуть панель Windows",delegate { taskbarHidden=false; prefs.HideWindowsTaskbar=false; Native.RestoreTaskbars(); Place(); Save(); });
   AddMenu(menu,"Закрепить программу…",delegate { var dialog=new OpenFileDialog { Filter="Программы и ярлыки|*.exe;*.lnk;*.website;*.url|Все файлы|*.*",Title="Закрепить программу" }; if(dialog.ShowDialog()==true) { if(!prefs.Pins.Contains(dialog.FileName,StringComparer.OrdinalIgnoreCase)) prefs.Pins.Add(dialog.FileName); Save(); RefreshApps(true); } });
   AddMenu(menu,"Обновить список окон",delegate { RefreshApps(true); });
   menu.Items.Add(new Separator());
   AddMenu(menu,"Пуск",delegate { Native.Hotkey(0); }); AddMenu(menu,"Поиск",delegate { Native.Hotkey(0x53); });
   AddMenu(menu,"Звук и сеть (Win+A)",delegate { Native.Hotkey(0x41); });
   AddMenu(menu,"Уведомления и календарь (Win+N)",delegate { Native.Hotkey(0x4E); });
   AddMenu(menu,"Показать рабочий стол",delegate { Native.Hotkey(0x44); });
   AddMenu(menu,"Параметры Windows",delegate { Launch("ms-settings:"); });
   menu.Items.Add(new Separator()); AddMenu(menu,"Выйти",Close); return menu;
  }
  void UpdateClock() {
   if(closing) return;
   UpdateSideIslandPresentation();
   if(IsLoaded && handle!=IntPtr.Zero) Place();
   if(!prefs.ClockEnabled) { if(desktopClock!=null) { desktopClock.Close(); desktopClock=null; } return; }
   if(desktopClock==null) desktopClock=new DesktopClock(prefs);
   desktopClock.ApplyPreferences();
  }
  const string RunKey="Software\\Microsoft\\Windows\\CurrentVersion\\Run";
  static bool StartupEnabled() { using(var key=Registry.CurrentUser.OpenSubKey(RunKey)) { return key!=null && (key.GetValue("Nook")!=null || key.GetValue("IslandDock")!=null); } }
  internal static void SetStartup(bool enabled) {
   using(var key=Registry.CurrentUser.CreateSubKey(RunKey)) {
    if(enabled) key.SetValue("Nook","\""+Process.GetCurrentProcess().MainModule.FileName+"\" \"--data-dir="+Preferences.Folder+"\"",RegistryValueKind.String);
    else key.DeleteValue("Nook",false);
    key.DeleteValue("IslandDock",false);
   }
  }
  void CheckFullscreen(IntPtr foreground) {
   Native.RECT rect; bool full=false;
   if(foreground!=IntPtr.Zero && foreground!=handle && Native.GetWindowRect(foreground,out rect) && Native.IsTaskWindow(foreground,handle)) {
    var screen=Native.MonitorRect(foreground); full=rect.Left<=screen.Left && rect.Top<=screen.Top && rect.Right>=screen.Right && rect.Bottom>=screen.Bottom;
   }
   UpdateFullscreenVisibility(full);
   RestoreDockAboveDesktop(foreground);
  }
 }
 sealed class AppBar : IDisposable {
  [StructLayout(LayoutKind.Sequential)] struct APPBARDATA { public uint Size; public IntPtr Window; public uint Callback,Edge; public Native.RECT Rect; public IntPtr Param; }
  [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint message,ref APPBARDATA data);
  [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
  readonly HwndSource source; readonly Action changed;
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
  readonly uint taskbarCreated=RegisterWindowMessage("TaskbarCreated");
  bool busy,disposed,registered,invalidated=true; uint edge=3; int size;
  public Native.RECT Reserved;
  APPBARDATA Data() { return new APPBARDATA { Size=(uint)Marshal.SizeOf(typeof(APPBARDATA)),Window=source.Handle,Callback=0x8001 }; }
  public AppBar(HwndSource dockSource,Action changed) {
   this.changed=changed;
   source=dockSource; source.AddHook(Hook);
   // Registration is lazy: an overlay/auto-hidden dock reserves no work area.
   Reserved=Native.MonitorRect(source.Handle);
  }
  IntPtr Hook(IntPtr hwnd,int message,IntPtr w,IntPtr l,ref bool handled) {
   if(disposed || source.IsDisposed || source.Handle==IntPtr.Zero) return IntPtr.Zero;
   if(message==taskbarCreated && size>0) { registered=false; invalidated=true; Position(edge,size,true); if(changed!=null) changed(); }
   if(message==0x8001 && w.ToInt32()==1 && registered && size>0 && !busy) { Position(edge,size,true); if(changed!=null) changed(); }
   if(message==0x8001 && w.ToInt32()==2 && registered && size>0) { SetWindowPos(source.Handle,l!=IntPtr.Zero ? new IntPtr(1) : new IntPtr(-1),0,0,0,0,0x13); }
   return IntPtr.Zero;
  }
  public void Position(uint edge,int size,bool force=false) {
   if(edge!=0 && edge!=1 && edge!=3) throw new ArgumentOutOfRangeException("edge","Допустимы левая, верхняя и нижняя стороны экрана.");
   size=Math.Max(0,size);
   if(disposed || source.IsDisposed || source.Handle==IntPtr.Zero || busy || (!force && !invalidated && this.edge==edge && this.size==size)) return;
   this.edge=edge; this.size=size; invalidated=false; busy=true;
   try {
    if(size==0) { if(registered) { var remove=Data(); SHAppBarMessage(1,ref remove); registered=false; } Reserved=Native.MonitorRect(source.Handle); return; }
    if(!registered) { var add=Data(); registered=SHAppBarMessage(0,ref add)!=UIntPtr.Zero; if(!registered) throw new InvalidOperationException("Не удалось зарезервировать место для панели."); }
    var data=Data(); data.Edge=edge; data.Rect=Native.MonitorRect(source.Handle);
    if(SHAppBarMessage(2,ref data)==UIntPtr.Zero) throw new InvalidOperationException("Windows не разрешила определить положение панели.");
    if(edge==0) data.Rect.Right=data.Rect.Left+size;
    else if(edge==1) data.Rect.Bottom=data.Rect.Top+size;
    else data.Rect.Top=data.Rect.Bottom-size;
    if(SHAppBarMessage(3,ref data)==UIntPtr.Zero) throw new InvalidOperationException("Windows не разрешила зарезервировать место для панели.");
    Reserved=data.Rect;
   } catch { invalidated=true; throw; } finally { busy=false; }
  }
  public void Invalidate() { invalidated=true; }
  public void Dispose() { if(disposed) return; disposed=true; if(registered && !source.IsDisposed) { var data=Data(); SHAppBarMessage(1,ref data); } if(!source.IsDisposed) source.RemoveHook(Hook); }
 }
 static class Native {
  static readonly uint ownPid=(uint)Process.GetCurrentProcess().Id;
  public delegate bool EnumDelegate(IntPtr hwnd,IntPtr lparam);
  public delegate void WinEventDelegate(IntPtr hook,uint evt,IntPtr hwnd,int obj,int child,uint thread,uint time);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
  [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int Size; public RECT Monitor,Work; public uint Flags; }
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumDelegate callback,IntPtr lparam);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
  [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder value,int count);
  [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd,uint command);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int count);
  [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
  [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
  [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process,int flags,StringBuilder path,ref int size);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern int GetApplicationUserModelId(IntPtr process,ref uint count,StringBuilder appId);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct SHFILEINFO { public IntPtr Icon; public int Index; public uint Attributes; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string DisplayName; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)] public string TypeName; }
  [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SHGetFileInfo(string path,uint attributes,out SHFILEINFO info,uint size,uint flags);
  [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);
  public static IntPtr FileIcon(string path) { SHFILEINFO info; return SHGetFileInfo(path,0,out info,(uint)Marshal.SizeOf(typeof(SHFILEINFO)),0x100)!=IntPtr.Zero ? info.Icon : IntPtr.Zero; }
  [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd,int attribute,out int value,int size);
  [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint first,uint last,IntPtr module,WinEventDelegate callback,uint process,uint thread,uint flags);
  [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
  [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr hwnd,int command);
  [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
  [StructLayout(LayoutKind.Sequential)] struct FLASHWINFO { public uint Size; public IntPtr Window; public uint Flags,Count,Timeout; }
  [DllImport("user32.dll")] static extern bool FlashWindowEx(ref FLASHWINFO info);
  [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hwnd);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string className,string windowName);
  [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd,int message,IntPtr w,IntPtr l);
  public static void SignalDock(int message) { var window=FindWindow(null,"Nook"); if(window!=IntPtr.Zero) PostMessage(window,message,IntPtr.Zero,IntPtr.Zero); }
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string className,string windowName);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd,out RECT rect);
  [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
  [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor,ref MONITORINFO info);
  [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint Type; public INPUTUNION Data; }
  [StructLayout(LayoutKind.Explicit)] struct INPUTUNION { [FieldOffset(0)] public KEYBDINPUT Keyboard; [FieldOffset(0)] public MOUSEINPUT Mouse; }
  [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort Vk,Scan; public uint Flags,Time; public UIntPtr Extra; }
  [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int X,Y; public uint MouseData,Flags,Time; public UIntPtr Extra; }
  [DllImport("user32.dll")] static extern uint SendInput(uint count,INPUT[] inputs,int size);
  public static int InputSize { get { return Marshal.SizeOf(typeof(INPUT)); } }
  public static void Hotkey(ushort key) {
   var inputs=new List<INPUT>(); inputs.Add(Key(0x5B,false)); if(key!=0) { inputs.Add(Key(key,false)); inputs.Add(Key(key,true)); } inputs.Add(Key(0x5B,true)); SendInput((uint)inputs.Count,inputs.ToArray(),Marshal.SizeOf(typeof(INPUT)));
  }
  static INPUT Key(ushort key,bool up) { return new INPUT { Type=1,Data=new INPUTUNION { Keyboard=new KEYBDINPUT { Vk=key,Flags=up ? 2u : 0u } } }; }
  public static string Title(IntPtr hwnd) { var text=new StringBuilder(512); GetWindowText(hwnd,text,text.Capacity); return text.ToString(); }
  public static bool IsDesktopWindow(IntPtr hwnd) {
   if(hwnd==IntPtr.Zero) return false;
   if(hwnd==GetShellWindow()) return true;
   var name=new StringBuilder(128); GetClassName(hwnd,name,name.Capacity);
   return name.ToString()=="Progman" || name.ToString()=="WorkerW";
  }
  public static void RaiseDesktopSurface(IntPtr hwnd) {
   if(hwnd!=IntPtr.Zero) SetWindowPos(hwnd,new IntPtr(-1),0,0,0,0,0x53); // no move/resize/activation; show at topmost
  }
  [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
  public static uint ProcessId(IntPtr hwnd) { uint pid; GetWindowThreadProcessId(hwnd,out pid); return pid; }
  public static string ProcessPath(uint pid) { var process=OpenProcess(0x1000,false,pid); if(process==IntPtr.Zero) return null; try { var path=new StringBuilder(1024); int count=path.Capacity; if(QueryFullProcessImageName(process,0,path,ref count)) return path.ToString(); path=new StringBuilder(32768); count=path.Capacity; return QueryFullProcessImageName(process,0,path,ref count) ? path.ToString() : null; } finally { CloseHandle(process); } }
  public static string WindowAppId(uint pid) {
   var process=OpenProcess(0x1000,false,pid); if(process==IntPtr.Zero) return null;
   try { uint count=0; int status=GetApplicationUserModelId(process,ref count,null); if(status!=122 || count==0 || count>32768) return null; var appId=new StringBuilder((int)count); return GetApplicationUserModelId(process,ref count,appId)==0 ? appId.ToString() : null; } finally { CloseHandle(process); }
  }
  public static bool IsTaskWindow(IntPtr hwnd,IntPtr self) {
   if(hwnd==self || hwnd==GetShellWindow() || !IsWindowVisible(hwnd)) return false;
   var className=new StringBuilder(128); GetClassName(hwnd,className,className.Capacity); if(className.ToString()=="Progman" || className.ToString()=="WorkerW") return false;
   uint pid; GetWindowThreadProcessId(hwnd,out pid); if(pid==ownPid) return false;
   long style=GetWindowLongPtr(hwnd,-20).ToInt64(); if((style&0x80)!=0 || ((style&0x40000)==0 && GetWindow(hwnd,4)!=IntPtr.Zero)) return false;
   int cloaked; if(DwmGetWindowAttribute(hwnd,14,out cloaked,4)==0 && cloaked!=0) return false; return true;
  }
  public static void Activate(IntPtr hwnd) { if(IsIconic(hwnd)) ShowWindowAsync(hwnd,9); if(!SetForegroundWindow(hwnd)) { var flash=new FLASHWINFO { Size=(uint)Marshal.SizeOf(typeof(FLASHWINFO)),Window=hwnd,Flags=3,Count=2 }; FlashWindowEx(ref flash); } }
  public static void HideTaskbars() { SetTaskbars(false); }
  public static void RestoreTaskbars() { try { SetTaskbarOverlayMode(false); } finally { SetTaskbars(true); } }
  [StructLayout(LayoutKind.Sequential)] struct TASKBARAPPBARDATA { public uint Size; public IntPtr Window; public uint Callback,Edge; public RECT Rect; public IntPtr Param; }
  [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint message,ref TASKBARAPPBARDATA data);
  static readonly object taskbarStateLock=new object();
  static uint? originalTaskbarState;
  static bool taskbarStateBusy,taskbarLeasePersisted;
  static int lastTaskbarHeight=48;
  static string taskbarLeaseResult="No taskbar state lease requested.";
  static string TaskbarLeaseFile { get { return Path.Combine(Preferences.Folder,"taskbar-state.txt"); } }
  static uint ReadTaskbarState() {
   var data=new TASKBARAPPBARDATA { Size=(uint)Marshal.SizeOf(typeof(TASKBARAPPBARDATA)) };
   if(FindWindow("Shell_TrayWnd",null)==IntPtr.Zero) throw new InvalidOperationException("Панель Windows недоступна: невозможно проверить её состояние.");
   return unchecked((uint)SHAppBarMessage(4,ref data).ToUInt64());
  }
  public static bool TaskbarAutoHides { get { return (ReadTaskbarState()&1)!=0; } }
  public static string TaskbarOverlayDiagnostics {
   get { lock(taskbarStateLock) { return "Original="+(originalTaskbarState.HasValue ? originalTaskbarState.Value.ToString() : "none")+" Snapshot="+File.Exists(TaskbarLeaseFile)+" Result="+taskbarLeaseResult; } }
  }
  static uint? ReadTaskbarLease() {
   if(!File.Exists(TaskbarLeaseFile)) return null;
   uint state;
   if(!uint.TryParse(File.ReadAllText(TaskbarLeaseFile).Trim(),out state) || state>3) throw new InvalidOperationException("Повреждён файл восстановления панели Windows: "+TaskbarLeaseFile);
   return state;
  }
  static void SaveTaskbarLease(uint state) {
   if(File.Exists(TaskbarLeaseFile)) {
    var saved=ReadTaskbarLease();
    if(!saved.HasValue || saved.Value!=state) throw new InvalidOperationException("Состояние восстановления панели Windows изменилось. Сначала выполните Restore-Taskbar.cmd.");
    return;
   }
   Directory.CreateDirectory(Preferences.Folder);
   string temporary=TaskbarLeaseFile+".tmp."+ownPid;
   File.WriteAllText(temporary,state.ToString(System.Globalization.CultureInfo.InvariantCulture));
   try { File.Move(temporary,TaskbarLeaseFile); }
   finally { if(File.Exists(temporary)) File.Delete(temporary); }
  }
  static void WriteTaskbarState(uint state) {
   IntPtr taskbar=FindWindow("Shell_TrayWnd",null);
   if(taskbar==IntPtr.Zero) throw new InvalidOperationException("Панель Windows недоступна: невозможно изменить её состояние.");
   var data=new TASKBARAPPBARDATA { Size=(uint)Marshal.SizeOf(typeof(TASKBARAPPBARDATA)),Window=taskbar,Param=new IntPtr((long)(state&3)) };
   // ABM_SETSTATE always returns TRUE; verify the autohide bit with GETSTATE.
   SHAppBarMessage(10,ref data);
   if((ReadTaskbarState()&1)!=(state&1)) throw new InvalidOperationException("Windows не применила состояние автоматического скрытия панели задач.");
  }
  public static void SetTaskbarOverlayMode(bool releaseBottomReservation) {
   lock(taskbarStateLock) {
    if(taskbarStateBusy) return;
    taskbarStateBusy=true;
    try {
     if(releaseBottomReservation) {
      uint current=ReadTaskbarState();
      if(!originalTaskbarState.HasValue) originalTaskbarState=ReadTaskbarLease() ?? current;
      // Persist BEFORE changing Explorer, including when a prior crashed process
      // already left autohide enabled. Never overwrite that original snapshot.
      if(!taskbarLeasePersisted || !File.Exists(TaskbarLeaseFile)) { SaveTaskbarLease(originalTaskbarState.Value); taskbarLeasePersisted=true; }
      if((current&1)==0) WriteTaskbarState(current|1);
      taskbarLeaseResult="Overlay reservation released; Windows autohide enabled.";
     } else {
      uint? saved=originalTaskbarState ?? ReadTaskbarLease();
      if(!saved.HasValue) { taskbarLeaseResult="No taskbar state snapshot to restore."; return; }
      uint current=ReadTaskbarState();
      if((current&1)!=(saved.Value&1)) WriteTaskbarState(saved.Value);
      // Only delete the recovery record after readback confirms restoration.
      if(File.Exists(TaskbarLeaseFile)) File.Delete(TaskbarLeaseFile);
      originalTaskbarState=null; taskbarLeasePersisted=false;
      taskbarLeaseResult="Original Windows taskbar state restored: "+saved.Value;
     }
    } catch(Exception error) {
     taskbarLeaseResult="FAILED: "+error.Message;
     throw new InvalidOperationException("Не удалось "+(releaseBottomReservation ? "освободить место штатной панели Windows" : "восстановить состояние штатной панели Windows")+". "+error.Message,error);
    } finally { taskbarStateBusy=false; }
   }
  }
  public static bool TaskbarVisible { get { var taskbar=FindWindow("Shell_TrayWnd",null); return taskbar!=IntPtr.Zero && IsWindowVisible(taskbar); } }
  public static int TaskbarHeight(IntPtr dock) { RECT rect; var screen=MonitorRect(dock); var taskbar=FindWindow("Shell_TrayWnd",null); if(taskbar!=IntPtr.Zero && GetWindowRect(taskbar,out rect) && rect.Right-rect.Left>screen.Right-screen.Left-32 && rect.Bottom-rect.Top>16 && rect.Bottom-rect.Top<(screen.Bottom-screen.Top)/4) lastTaskbarHeight=rect.Bottom-rect.Top; return lastTaskbarHeight; }
  static void SetTaskbars(bool show) { var main=FindWindow("Shell_TrayWnd",null); if(main!=IntPtr.Zero) ShowWindowAsync(main,show?5:0); var next=IntPtr.Zero; while((next=FindWindowEx(IntPtr.Zero,next,"Shell_SecondaryTrayWnd",null))!=IntPtr.Zero) ShowWindowAsync(next,show?5:0); }
  public static RECT MonitorRect(IntPtr hwnd) { var info=new MONITORINFO { Size=Marshal.SizeOf(typeof(MONITORINFO)) }; GetMonitorInfo(MonitorFromWindow(hwnd,2),ref info); return info.Monitor; }
  public static RECT MonitorWorkRect(IntPtr hwnd) { var info=new MONITORINFO { Size=Marshal.SizeOf(typeof(MONITORINFO)) }; if(!GetMonitorInfo(MonitorFromWindow(hwnd,2),ref info)) throw new InvalidOperationException("Не удалось получить рабочую область монитора."); return info.Work; }
 }
 static class SelfTest {
  public static void Run() {
   var result=new StringBuilder(); int failures=0;
   Action<string,bool> check=delegate(string name,bool ok) { result.AppendLine((ok?"PASS ":"FAIL ")+name); if(!ok) failures++; };
   check("Native INPUT size x64",Native.InputSize==40);
   check("Quote path with spaces",DockWindow.QuoteArgument("C:\\My Files\\a.txt")=="\"C:\\My Files\\a.txt\"");
   check("Quote trailing slash",DockWindow.QuoteArgument("C:\\folder\\")=="\"C:\\folder\\\\\"");
   var serializer=new XmlSerializer(typeof(Preferences));
   using(var input=new StringReader("<Preferences><Pins><string>C:\\App.lnk</string></Pins></Preferences>")) { var old=(Preferences)serializer.Deserialize(input); check("Legacy pins XML remains readable",old.Pins.Count==1 && old.PinDetails!=null); check("Legacy preferences enable hover wave",old.HoverWave); }
   var sample=new Preferences { HoverWave=false }; sample.Pins.Add("C:\\Pins\\App.lnk"); sample.PinDetails.Add(new PinDetails { LaunchPath=sample.Pins[0],TargetPath="C:\\App.exe",Title="Example",AppId="Sample!App",IconPath="C:\\App.png" });
   using(var output=new StringWriter()) { serializer.Serialize(output,sample); using(var input=new StringReader(output.ToString())) { var copy=(Preferences)serializer.Deserialize(input); check("Pin metadata XML round trip",copy.Pins[0]==copy.PinDetails[0].LaunchPath && copy.PinDetails[0].AppId=="Sample!App" && copy.PinDetails[0].Title=="Example"); check("Disabled wave preference survives XML round trip",!copy.HoverWave); } }
   int windows=0; Native.EnumWindows(delegate(IntPtr h,IntPtr p) { if(Native.IsTaskWindow(h,IntPtr.Zero)) windows++; return true; },IntPtr.Zero);
   var wave=new List<HoverWaveItem>();
   for(int i=0;i<20;i++) wave.Add(new HoverWaveItem { Magnifies=i!=0 });
   bool bounds=true,spacing=true,startFixed=true,local=true;
   foreach(double slot in new double[] { 40,44,80 }) foreach(double maximum in new double[] { 1,1.35,2 }) {
    for(int i=0;i<wave.Count;i++) { wave[i].Width=slot; wave[i].Center=(i+.5)*slot; }
    double padding=HoverWaveMath.Padding(slot,maximum);
    for(double x=-padding;x<=slot*wave.Count+padding;x+=slot/8) {
     HoverWaveMath.Targets(wave,x,true,maximum); int enlarged=0;
     startFixed&=wave[0].TargetScale==1;
     bounds&=wave[0].Center+wave[0].TargetX-wave[0].Width*wave[0].TargetScale/2>=-padding-.0001;
     var last=wave[wave.Count-1]; bounds&=last.Center+last.TargetX+last.Width*last.TargetScale/2<=slot*wave.Count+padding+.0001;
     for(int i=0;i<wave.Count;i++) {
      var item=wave[i]; if(item.TargetScale>1.00001) enlarged++;
      bounds&=item.TargetScale>=1 && item.TargetScale<=maximum+.00001;
      if(i>0) { var previous=wave[i-1]; spacing&=item.Center+item.TargetX-item.Width*item.TargetScale/2>=previous.Center+previous.TargetX+previous.Width*previous.TargetScale/2-.0001; }
     }
     local&=enlarged<=5;
    }
   }
   check("Wave stays within reserved space at 100-200%",bounds);
   check("Expanded wave buttons do not overlap",spacing);
   check("Wave never magnifies Start",startFixed);
   check("Wave magnifies at most five neighbors",local);
   HoverWaveMath.Targets(wave,0,false,2); check("Pointer leave restores all wave targets",wave.All(x=>x.TargetScale==1 && Math.Abs(x.TargetX)<.00001));
   double restingScale=2,restingOffset=100; int settleFrames=0;
   for(;settleFrames<120 && (restingScale!=1 || restingOffset!=0);settleFrames++) { restingScale=HoverWaveMath.Approach(restingScale,1,1-Math.Exp(-(1.0/60)/.045),.0005); restingOffset=HoverWaveMath.Approach(restingOffset,0,1-Math.Exp(-(1.0/60)/.045),.025); }
   check("Wave reaches exact rest and can unsubscribe",restingScale==1 && restingOffset==0 && settleFrames<40);
   TaskTerminationChecks.Run(check);
   var themedMenu=DockMenuTheme.Create(new Preferences());
   themedMenu.Items.Add(new MenuItem { Header="Открыть новое окно" }); themedMenu.Items.Add(new Separator()); themedMenu.Items.Add(new MenuItem { Header="Завершить задачу",Tag="danger" });
   themedMenu.ApplyTemplate();
   foreach(var entry in themedMenu.Items) { var control=entry as Control; if(control!=null) control.ApplyTemplate(); }
   check("Rounded transparent context menu templates load",themedMenu.Template!=null && themedMenu.Resources[typeof(MenuItem)] is Style && themedMenu.Resources[typeof(Separator)] is Style);
   try {
    themedMenu.Visibility=Visibility.Visible; themedMenu.Measure(new Size(310,600));
    themedMenu.Arrange(new Rect(0,0,310,themedMenu.DesiredSize.Height)); themedMenu.UpdateLayout();
    var menuRow=(MenuItem)themedMenu.Items[0]; menuRow.ApplyTemplate();
    check("Context menu uses its rounded item template",menuRow.Template.FindName("Row",menuRow)!=null);
    int previewHeight=Math.Max(1,(int)Math.Ceiling(themedMenu.ActualHeight));
    var preview=new RenderTargetBitmap(310,previewHeight,96,96,PixelFormats.Pbgra32);
    var backdrop=new DrawingVisual(); using(var dc=backdrop.RenderOpen()) dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(65,69,79)),null,new Rect(0,0,310,previewHeight));
    preview.Render(backdrop); preview.Render(themedMenu);
    var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(preview));
    using(var file=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"context-menu-preview.png"))) encoder.Save(file);
   } catch(Exception ex) { check("Context menu rendering: "+ex.Message,false); }
   check("Window enumeration",windows>0); result.AppendLine("Task windows: "+windows); result.AppendLine("Failures: "+failures);
   File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test.txt"),result.ToString()); Environment.ExitCode=failures==0 ? 0 : 1;
  }
 }
}
