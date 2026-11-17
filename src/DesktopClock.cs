using System;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nook {
 sealed class DesktopClock {
  readonly Preferences preferences;
  readonly TextBlock text,dateLabel,weatherGlyph,weatherLabel;
  readonly StackPanel layout,statusRow;
  readonly Button dateButton;
  readonly DesktopHost desktopHost=new DesktopHost();
  readonly Dispatcher Dispatcher;
  readonly DispatcherTimer minuteTimer,weatherTimer;
  HwndSource source,statusSource;
  bool closed,ordering,recoveryQueued,surfacesShown,weatherBusy;
  Rect statusBounds;
  HttpWebRequest weatherRequest;
  DateTime lastWeatherAttempt=DateTime.MinValue;
  WeatherReading weather;
  string weatherError="";
  public double Left { get; private set; }
  public double Top { get; private set; }
  public double Width { get; private set; }
  public double Height { get; private set; }
  public bool IsVisible { get { return !closed && surfacesShown && desktopHost.IsAttached(source); } }
  public string Diagnostics { get; private set; }

  public DesktopClock(Preferences prefs) {
   preferences=prefs; Dispatcher=Application.Current.Dispatcher;
   text=new TextBlock { FontFamily=new FontFamily("Segoe UI Light"),Foreground=new SolidColorBrush(Color.FromArgb(235,255,255,255)),TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,IsHitTestVisible=false };
   dateLabel=new TextBlock { Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false };
   weatherGlyph=new TextBlock { Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,3,0),FontFamily=new FontFamily("Segoe UI Symbol"),IsHitTestVisible=false };
   weatherLabel=new TextBlock { Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(2,0,0,0),IsHitTestVisible=false };
   dateButton=new Button { Content=dateLabel,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(3,4,3,4),Focusable=false,IsTabStop=false,Cursor=System.Windows.Input.Cursors.Hand };
   var chrome=new FrameworkElementFactory(typeof(Border)); chrome.SetValue(Border.BackgroundProperty,new SolidColorBrush(Color.FromArgb(1,255,255,255))); chrome.SetValue(Border.PaddingProperty,new Thickness(3,4,3,4)); chrome.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter))); dateButton.Template=new ControlTemplate(typeof(Button)) { VisualTree=chrome };
   dateButton.Click+=delegate { Native.Hotkey(0x4E); };
   statusRow=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,2,0,3) };
   statusRow.Children.Add(dateButton); statusRow.Children.Add(weatherGlyph); statusRow.Children.Add(weatherLabel);
   // Clock and the date/weather strip are separate desktop child visuals.
   // A WPF visual can only have one parent, so do not also parent statusRow here.
   layout=new StackPanel { HorizontalAlignment=HorizontalAlignment.Center }; layout.Children.Add(text);
   minuteTimer=new DispatcherTimer(DispatcherPriority.Background); minuteTimer.Tick+=MinuteTick;
   weatherTimer=new DispatcherTimer(DispatcherPriority.Background) { Interval=TimeSpan.FromMinutes(30) }; weatherTimer.Tick+=delegate { RequestWeather(); };
   ApplyPreferences();
  }
  public void Close() {
   if(closed) return; closed=true; minuteTimer.Stop(); weatherTimer.Stop();
   minuteTimer.Tick-=MinuteTick; if(weatherRequest!=null) weatherRequest.Abort(); weatherRequest=null;
   desktopHost.Dispose(); source=null; statusSource=null;
  }
  static double Clamp(double value,double min,double max,double fallback) { if(double.IsNaN(value)||double.IsInfinity(value)) value=fallback; return Math.Max(min,Math.Min(max,value)); }
  bool HasStatusRow() { return preferences.ClockShowDate || preferences.ClockShowWeather; }
  void UpdateStatusPresentation() {
   double size=Clamp(preferences.ClockStatusSize,10,32,14);
   dateButton.Visibility=preferences.ClockShowDate ? Visibility.Visible : Visibility.Collapsed;
   weatherGlyph.Visibility=preferences.ClockShowWeather ? Visibility.Visible : Visibility.Collapsed;
   weatherLabel.Visibility=preferences.ClockShowWeather ? Visibility.Visible : Visibility.Collapsed;
   statusRow.Visibility=HasStatusRow() ? Visibility.Visible : Visibility.Collapsed;
   dateLabel.FontSize=size; weatherGlyph.FontSize=size; weatherLabel.FontSize=size;
   UpdateDate();
   weatherGlyph.Text=weather==null ? "☁" : WeatherSymbol(weather.Code);
   weatherLabel.Text=weather==null ? "—°" : weather.Temperature.ToString("+#;-#;0",CultureInfo.CurrentCulture)+"°";
   string attribution="Погода: Санкт-Петербург • Open-Meteo (open-meteo.com)";
   weatherGlyph.ToolTip=weatherLabel.ToolTip=weather==null ? (string.IsNullOrEmpty(weatherError) ? "Загрузка погоды…" : "Погода временно недоступна: "+weatherError)+"\nПовторная попытка через 5 минут. "+attribution : attribution+"\nОбновлено "+weather.Updated.ToString("HH:mm",CultureInfo.CurrentCulture);
  }
  static string WeatherSymbol(int code) {
   if(code==0) return "☀"; if(code<=3) return code==1 ? "🌤" : "☁";
   if(code==45||code==48) return "≋"; if(code>=51&&code<=67 || code>=80&&code<=82) return "☂";
   if(code>=71&&code<=77 || code>=85&&code<=86) return "❄"; if(code>=95) return "⚡"; return "☁";
  }
  void UpdateDate() {
   DateTime now=DateTime.Now; dateLabel.Text=now.ToString("dd.MM.yyyy",CultureInfo.CurrentCulture);
   dateButton.ToolTip=now.ToString("D",CultureInfo.CurrentCulture)+"\nОткрыть календарь";
  }
  void PlaceClock() {
   Rect work=SystemParameters.WorkArea; layout.InvalidateMeasure(); layout.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity)); statusRow.InvalidateMeasure(); statusRow.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
   double rowWidth=HasStatusRow()?statusRow.DesiredSize.Width:0, rowHeight=HasStatusRow()?statusRow.DesiredSize.Height:0;
   double groupWidth=Math.Min(work.Width,Math.Max(100,Math.Max(layout.DesiredSize.Width+8,rowWidth)));
   double clockHeight=Math.Min(work.Height,Math.Max(40,text.DesiredSize.Height+4)); double totalHeight=Math.Min(work.Height,clockHeight+rowHeight);
   double left=work.Left+Math.Max(0,work.Width-groupWidth)*Clamp(preferences.ClockX,0,1,.78);
   double top=work.Top+Math.Max(0,work.Height-totalHeight)*Clamp(preferences.ClockY,0,1,.12);
   Width=groupWidth; Height=clockHeight; Left=left; Top=top;
   statusBounds=new Rect(left+(groupWidth-Math.Min(work.Width,rowWidth))/2,top+clockHeight,Math.Max(1,Math.Min(work.Width,rowWidth)),Math.Max(1,Math.Min(Math.Max(1,work.Bottom-top-clockHeight),rowHeight)));
   UpdateDesktopOrder();
  }
  void RequestWeather() {
   if(closed||weatherBusy||!preferences.ClockEnabled||!preferences.ClockShowWeather) return;
   DateTime now=DateTime.UtcNow; TimeSpan refreshAfter=weather==null ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(30);
   if(now>=lastWeatherAttempt&&now-lastWeatherAttempt<refreshAfter) return;
   lastWeatherAttempt=now; weatherBusy=true;
   Task.Run(delegate { return ReadWeather(); }).ContinueWith(delegate(Task<WeatherReading> task) {
    WeatherReading result=task.Status==TaskStatus.RanToCompletion?task.Result:null;
    if(Dispatcher.HasShutdownStarted||Dispatcher.HasShutdownFinished) return;
    try { Dispatcher.BeginInvoke(new Action(delegate {
     weatherBusy=false; weatherRequest=null; if(closed||!preferences.ClockEnabled) return;
     if(result!=null) { weather=result; weatherError=""; } UpdateStatusPresentation(); PlaceClock();
    })); } catch(InvalidOperationException) { }
   });
  }
  WeatherReading ReadWeather() {
   try {
    // .NET Framework versions on some Windows installations still default to
    // older TLS protocols; Open-Meteo requires TLS 1.2.
    ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
    var request=(HttpWebRequest)WebRequest.Create("https://api.open-meteo.com/v1/forecast?latitude=59.9386&longitude=30.3141&current=temperature_2m,weather_code&timezone=Europe%2FMoscow");
    request.Method="GET"; request.Timeout=8000; request.ReadWriteTimeout=8000; request.UserAgent="Nook/1.0 (personal desktop widget)"; weatherRequest=request;
    using(var response=(HttpWebResponse)request.GetResponse()) using(var stream=response.GetResponseStream()) using(var reader=new System.IO.StreamReader(stream)) {
     string json=reader.ReadToEnd(); var current=Regex.Match(json,"\"current\"\\s*:\\s*\\{([^}]*)\\}"); if(!current.Success) return null;
     var temp=Regex.Match(current.Groups[1].Value,"\"temperature_2m\"\\s*:\\s*(-?[0-9]+(?:\\.[0-9]+)?)");
     var code=Regex.Match(current.Groups[1].Value,"\"weather_code\"\\s*:\\s*([0-9]+)"); double c; int w;
     if(!temp.Success||!code.Success||!double.TryParse(temp.Groups[1].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out c)||!int.TryParse(code.Groups[1].Value,out w)) return null;
     return new WeatherReading { Temperature=(int)Math.Round(c,MidpointRounding.AwayFromZero),Code=w,Updated=DateTime.Now };
    }
   } catch(Exception ex) { weatherError=ex.Message; return null; }
  }
  sealed class WeatherReading { public int Temperature,Code; public DateTime Updated; }
  public void ApplyPreferences() {
   if(closed) return; minuteTimer.Stop(); weatherTimer.Stop();
   if(!preferences.ClockEnabled) { surfacesShown=false; desktopHost.DestroySurface(source); desktopHost.DestroySurface(statusSource); source=null; statusSource=null; return; }
   text.FontSize=Clamp(preferences.ClockSize,24,220,64); text.Text=DateTime.Now.ToString("HH:mm",CultureInfo.CurrentCulture);
   UpdateStatusPresentation(); PlaceClock(); ScheduleNextMinute();
   if(preferences.ClockShowWeather) { weatherTimer.Start(); RequestWeather(); }
  }
  void MinuteTick(object sender,EventArgs args) {
   RefreshTime();
  }
  void ScheduleNextMinute() {
   if(closed||!preferences.ClockEnabled) return; DateTime now=DateTime.Now;
   DateTime next=new DateTime(now.Year,now.Month,now.Day,now.Hour,now.Minute,0).AddMinutes(1);
   minuteTimer.Interval=TimeSpan.FromMilliseconds(Math.Max(20,(next-now).TotalMilliseconds+20)); minuteTimer.Start();
  }
  IntPtr Hook(IntPtr window,int message,IntPtr w,IntPtr l,ref bool handled) {
   if(message==0x84) { handled=true; return new IntPtr(-1); } if(message==0x21) { handled=true; return new IntPtr(3); } if(message==0x1E&&preferences.ClockEnabled) RefreshTime(); return IntPtr.Zero;
  }
  IntPtr StatusHook(IntPtr window,int message,IntPtr w,IntPtr l,ref bool handled) { if(message==0x21) { handled=true; return new IntPtr(3); } return IntPtr.Zero; }
  public void RefreshTime() { if(closed||!preferences.ClockEnabled) return; minuteTimer.Stop(); text.Text=DateTime.Now.ToString("HH:mm",CultureInfo.CurrentCulture); UpdateDate(); PlaceClock(); ScheduleNextMinute(); }
  void QueueRecovery(object sender,EventArgs args) { if(closed||recoveryQueued||Dispatcher.HasShutdownStarted) return; recoveryQueued=true; Dispatcher.BeginInvoke(new Action(delegate { recoveryQueued=false; if(!closed) UpdateDesktopOrder(); })); }
  void EnsureSurface(ref HwndSource surface,FrameworkElement visual,string title,bool clickThrough) {
   if(desktopHost.IsAttached(surface)) return; if(surface!=null) desktopHost.DestroySurface(surface);
   surface=desktopHost.CreateSurface(title,visual,clickThrough);
   if(surface!=null) { surface.AddHook(clickThrough?(HwndSourceHook)Hook:StatusHook); surface.Disposed+=QueueRecovery; }
  }
  Rect DeviceBounds(HwndSource surface,Rect logicalBounds) { if(surface==null||surface.IsDisposed||surface.CompositionTarget==null) return logicalBounds; var matrix=surface.CompositionTarget.TransformToDevice; return new Rect(matrix.Transform(logicalBounds.TopLeft),matrix.Transform(logicalBounds.BottomRight)); }
  public void UpdateDesktopOrder() {
   if(closed||!preferences.ClockEnabled||ordering) return; ordering=true;
   try {
    EnsureSurface(ref source,layout,"Nook Desktop Clock",true);
    bool placed=source!=null&&desktopHost.Place(source,DeviceBounds(source,new Rect(Left,Top,Math.Max(1,Width),Math.Max(1,Height))),true);
    if(HasStatusRow()) { EnsureSurface(ref statusSource,statusRow,"Nook Desktop Status",false); placed=statusSource!=null&&desktopHost.Place(statusSource,DeviceBounds(statusSource,statusBounds),true)&&placed; }
    else if(statusSource!=null) { statusSource.Disposed-=QueueRecovery; desktopHost.DestroySurface(statusSource); statusSource=null; }
    surfacesShown=placed; Diagnostics=(placed?"Desktop child surfaces attached below application windows. ":"Desktop surfaces unavailable. ")+desktopHost.Diagnostics;
   } finally { ordering=false; }
  }
 }
}
