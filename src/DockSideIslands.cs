using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nook {
 partial class DockWindow {
  const double SideIslandGap=8;
  readonly Border leftClockIsland=new Border();
  readonly Border rightStatusIsland=new Border();
  Border leftClockShadow,rightStatusShadow;
  readonly TextBlock sideClockText=new TextBlock();
  readonly StackPanel sideStatusContent=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center };
  readonly Button sideBatteryButton=new Button(),sideWifiButton=new Button();
  readonly TextBlock sideBatteryLabel=new TextBlock(),sideLanguageLabel=new TextBlock();
  readonly SideGlyph sideBatteryGlyph=new SideGlyph(SideGlyph.Kind.Battery);
  readonly SideGlyph sideWifiGlyph=new SideGlyph(SideGlyph.Kind.Wifi);
  readonly DispatcherTimer sideClockTimer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(20) };
  readonly DispatcherTimer sideStatusTimer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(30) };
  readonly DispatcherTimer sideLanguageTimer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(1) };
  StatusSnapshot sideStatus;
  bool sideStatusBusy,sideIslandsInitialized;
  string currentInputLanguage="";

  void InitializeSideIslands(Grid root) {
   sideIslandsInitialized=true;
   leftClockIsland.Child=sideClockText;
   leftClockIsland.Padding=new Thickness(10,0,10,0); leftClockIsland.Margin=new Thickness(GlassShadowInset,0,GlassShadowInset,0);
   leftClockIsland.VerticalAlignment=prefs.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
   sideClockText.FontFamily=new FontFamily("Segoe UI Light"); sideClockText.FontSize=22; sideClockText.Foreground=Brushes.White;
   sideClockText.HorizontalAlignment=HorizontalAlignment.Center; sideClockText.VerticalAlignment=VerticalAlignment.Center;
   sideClockText.TextAlignment=TextAlignment.Center; sideClockText.TextWrapping=TextWrapping.NoWrap; sideClockText.TextTrimming=TextTrimming.CharacterEllipsis; sideClockText.IsHitTestVisible=false;

   sideStatusContent.Children.Add(MakeSideStatusButton(sideBatteryButton,sideBatteryGlyph,sideBatteryLabel,"Батарея",delegate { Launch("ms-settings:powersleep"); }));
   sideStatusContent.Children.Add(MakeSideStatusButton(sideWifiButton,sideWifiGlyph,null,"Wi-Fi",delegate { Launch("ms-settings:network-wifi"); }));
   sideLanguageLabel.Foreground=Brushes.White; sideLanguageLabel.FontWeight=FontWeights.SemiBold;
   sideLanguageLabel.VerticalAlignment=VerticalAlignment.Center; sideLanguageLabel.HorizontalAlignment=HorizontalAlignment.Center;
   sideLanguageLabel.Margin=new Thickness(5,0,5,0); sideLanguageLabel.ToolTip="Раскладка клавиатуры активного приложения";
   sideLanguageLabel.IsHitTestVisible=false;
   sideStatusContent.Children.Add(sideLanguageLabel);
   rightStatusIsland.Child=sideStatusContent;
   rightStatusIsland.Padding=new Thickness(7,0,7,0); rightStatusIsland.Margin=new Thickness(GlassShadowInset,0,GlassShadowInset,0);
   rightStatusIsland.VerticalAlignment=prefs.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
   leftClockShadow=CreateGlassShadow(leftClockIsland); rightStatusShadow=CreateGlassShadow(rightStatusIsland);
   root.Children.Insert(0,leftClockShadow); root.Children.Insert(1,rightStatusShadow);
   root.Children.Add(leftClockIsland); root.Children.Add(rightStatusIsland);
   leftEdgeHighlight=CreateEdgeHighlight(leftClockIsland); rightEdgeHighlight=CreateEdgeHighlight(rightStatusIsland);
   leftEdgeHighlight.Visibility=prefs.EdgeHighlight ? Visibility.Visible : Visibility.Collapsed;
   rightEdgeHighlight.Visibility=prefs.EdgeHighlight ? Visibility.Visible : Visibility.Collapsed;
   root.Children.Add(leftEdgeHighlight); root.Children.Add(rightEdgeHighlight);
   UpdateSideIslandAppearance();
   UpdateSideIslandPresentation(); UpdateSideClock(); UpdateInputLanguage();
   sideClockTimer.Tick+=delegate { UpdateSideClock(); };
   sideStatusTimer.Tick+=delegate { RequestSideStatus(); };
   sideLanguageTimer.Tick+=delegate { UpdateInputLanguage(); };
  }

  FrameworkElement MakeSideStatusButton(Button button,FrameworkElement glyph,TextBlock label,string title,Action action) {
   var content=new StackPanel { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center };
   glyph.VerticalAlignment=VerticalAlignment.Center; glyph.IsHitTestVisible=false;
   content.Children.Add(glyph);
   if(label!=null) { label.Foreground=Brushes.White; label.VerticalAlignment=VerticalAlignment.Center; label.Margin=new Thickness(3,0,0,0); label.IsHitTestVisible=false; content.Children.Add(label); }
   button.Content=content; button.Background=Brushes.Transparent; button.BorderThickness=new Thickness(0); button.Padding=new Thickness(3,4,3,4);
   button.Margin=new Thickness(1,0,1,0); button.Focusable=false; button.IsTabStop=false; button.Cursor=System.Windows.Input.Cursors.Hand; button.ToolTip=title;
   var chrome=new FrameworkElementFactory(typeof(Border));
   chrome.SetValue(Border.BackgroundProperty,new SolidColorBrush(Color.FromArgb(1,255,255,255)));
   chrome.SetValue(Border.PaddingProperty,new Thickness(3,4,3,4));
   var presenter=new FrameworkElementFactory(typeof(ContentPresenter)); chrome.AppendChild(presenter);
   button.Template=new ControlTemplate(typeof(Button)) { VisualTree=chrome };
   button.MouseEnter+=delegate { button.Opacity=.72; }; button.MouseLeave+=delegate { button.Opacity=1; };
   button.Click+=delegate { action(); };
   AutomationProperties.SetName(button,title);
   return button;
  }

  void StartSideIslands() {
   if(closing || !sideIslandsInitialized) return;
   UpdateSideIslandPresentation(); UpdateSideClock(); UpdateInputLanguage(); RequestSideStatus();
   sideClockTimer.Start(); sideStatusTimer.Start(); sideLanguageTimer.Start();
  }
  void StopSideIslands() {
   sideClockTimer.Stop(); sideStatusTimer.Stop(); sideLanguageTimer.Stop();
  }
  double SideIslandWidth {
   get { return Math.Max(112,Math.Min(190,88+HorizontalIslandStatusSize*3.2)); }
  }
  double HorizontalIslandStatusSize { get { return Preferences.Clamp(prefs.HorizontalIslandStatusSize>0 ? prefs.HorizontalIslandStatusSize : 14,10,32,14); } }
  double VerticalClockFontSize {
   get {
    double requested=Preferences.Clamp(prefs.VerticalIslandClockSize,10,22,16);
    var typeface=new Typeface(sideClockText.FontFamily,sideClockText.FontStyle,sideClockText.FontWeight,sideClockText.FontStretch);
    var sample=new FormattedText("88:88",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,typeface,requested,Brushes.White,VisualTreeHelper.GetDpi(sideClockText).PixelsPerDip);
    return Math.Min(requested,requested*Math.Max(1,BandHeight-10)/Math.Max(1,sample.WidthIncludingTrailingWhitespace));
   }
  }
  bool HasRightIslandContent { get { return prefs.SideShowBattery || prefs.SideShowWifi || prefs.SideShowLanguage; } }
  bool HasSideIslands { get { return prefs.LeftClockIslandEnabled || (prefs.RightStatusIslandEnabled && HasRightIslandContent); } }

  void UpdateSideIslandPresentation() {
   if(!sideIslandsInitialized) return;
   leftClockIsland.Visibility=prefs.LeftClockIslandEnabled ? Visibility.Visible : Visibility.Collapsed;
   rightStatusIsland.Visibility=prefs.RightStatusIslandEnabled && HasRightIslandContent ? Visibility.Visible : Visibility.Collapsed;
   leftEdgeHighlight.Visibility=prefs.EdgeHighlight && prefs.LeftClockIslandEnabled ? Visibility.Visible : Visibility.Collapsed;
   rightEdgeHighlight.Visibility=prefs.EdgeHighlight && prefs.RightStatusIslandEnabled && HasRightIslandContent ? Visibility.Visible : Visibility.Collapsed;
   sideBatteryButton.Visibility=prefs.SideShowBattery ? Visibility.Visible : Visibility.Collapsed;
   sideWifiButton.Visibility=prefs.SideShowWifi ? Visibility.Visible : Visibility.Collapsed;
   sideLanguageLabel.Visibility=prefs.SideShowLanguage ? Visibility.Visible : Visibility.Collapsed;
   sideStatusContent.Orientation=IsVertical ? Orientation.Vertical : Orientation.Horizontal;
   var batteryContent=sideBatteryButton.Content as StackPanel;
   if(batteryContent!=null) { batteryContent.Orientation=IsVertical ? Orientation.Vertical : Orientation.Horizontal; batteryContent.HorizontalAlignment=HorizontalAlignment.Center; }
   var wifiContent=sideWifiButton.Content as StackPanel;
   if(wifiContent!=null) { wifiContent.Orientation=IsVertical ? Orientation.Vertical : Orientation.Horizontal; wifiContent.HorizontalAlignment=HorizontalAlignment.Center; }
   sideBatteryLabel.Margin=IsVertical ? new Thickness(0,2,0,0) : new Thickness(3,0,0,0);
   sideLanguageLabel.Margin=IsVertical ? new Thickness(0,1,0,1) : new Thickness(5,0,5,0);
   leftClockIsland.Padding=IsVertical ? new Thickness(3,0,3,0) : new Thickness(10,0,10,0);
   rightStatusIsland.Padding=IsVertical ? new Thickness(2,3,2,3) : new Thickness(7,0,7,0);
   sideBatteryButton.Padding=sideWifiButton.Padding=IsVertical ? new Thickness(0) : new Thickness(3,4,3,4);
   sideBatteryButton.Margin=sideWifiButton.Margin=IsVertical ? new Thickness(0) : new Thickness(1,0,1,0);
   sideClockText.FontSize=IsVertical ? VerticalClockFontSize : Preferences.Clamp(prefs.HorizontalIslandClockSize,12,32,22);
   double size=IsVertical ? Preferences.Clamp(prefs.VerticalIslandStatusSize,10,20,13) : HorizontalIslandStatusSize;
   sideBatteryGlyph.Width=size; sideBatteryGlyph.Height=size; sideBatteryLabel.FontSize=Math.Max(IsVertical ? 9 : 10,size*.72);
   sideWifiGlyph.Width=size; sideWifiGlyph.Height=size; sideLanguageLabel.FontSize=Math.Max(10,size*.76);
   UpdateSideStatusPresentation(); UpdateSideIslandAppearance();
  }
  void UpdateSideIslandAppearance() {
   if(!sideIslandsInitialized) return;
   double radius=Preferences.Clamp(prefs.CornerRadius,0,40,18);
   leftClockIsland.CornerRadius=new CornerRadius(radius); rightStatusIsland.CornerRadius=new CornerRadius(radius);
   leftClockShadow.CornerRadius=new CornerRadius(radius); rightStatusShadow.CornerRadius=new CornerRadius(radius);
   leftClockIsland.VerticalAlignment=IsVertical ? VerticalAlignment.Bottom : (prefs.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom);
   rightStatusIsland.VerticalAlignment=IsVertical ? VerticalAlignment.Top : (prefs.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom);
   ApplyGlassAppearance(leftClockIsland,prefs); ApplyGlassAppearance(rightStatusIsland,prefs);
  }
  void ApplySideIslandGrid(double centerWidth,double surfaceHeight,double totalWidth) {
   double slot=HasSideIslands ? SideIslandWidth+2*GlassShadowInset : 0;
   double gap=HasSideIslands ? SideIslandGap : 0;
   surface.ColumnDefinitions.Clear(); surface.RowDefinitions.Clear();
   foreach(FrameworkElement child in surface.Children) Grid.SetRow(child,0);
   surface.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(slot) });
   surface.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(gap) });
   surface.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(centerWidth) });
   surface.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(gap) });
   surface.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(slot) });
   Grid.SetColumn(leftClockShadow,0); Grid.SetColumn(leftClockIsland,0);
   Grid.SetColumn(glassShadow,2); Grid.SetColumn(island,2);
   Grid.SetColumn(rightStatusShadow,4); Grid.SetColumn(rightStatusIsland,4);
   Grid.SetColumn(leftEdgeHighlight,0); Grid.SetColumn(rightEdgeHighlight,4);
   Grid.SetColumn(mainEdgeHighlight,2);
   leftClockIsland.Width=SideIslandWidth; rightStatusIsland.Width=SideIslandWidth;
   leftClockIsland.Height=surfaceHeight; rightStatusIsland.Height=surfaceHeight;
   leftClockIsland.HorizontalAlignment=rightStatusIsland.HorizontalAlignment=HorizontalAlignment.Stretch;
   leftClockIsland.VerticalAlignment=rightStatusIsland.VerticalAlignment=prefs.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
   leftClockIsland.Margin=new Thickness(GlassShadowInset,0,GlassShadowInset,0); rightStatusIsland.Margin=new Thickness(GlassShadowInset,0,GlassShadowInset,0);
   Height=surfaceHeight+HoverHeadroom;
   Width=totalWidth;
  }
  void PlaceSideIslands() {
   if(!sideIslandsInitialized) return;
   double sideWidth=SideIslandWidth; sideClockText.Text=DateTime.Now.ToString("HH:mm",CultureInfo.CurrentCulture);
   if(HasSideIslands && !IsVertical) { leftClockIsland.Width=sideWidth; rightStatusIsland.Width=sideWidth; }
  }

  void UpdateSideClock() {
   if(sideClockText!=null) sideClockText.Text=DateTime.Now.ToString("HH:mm",CultureInfo.CurrentCulture);
  }
  void UpdateInputLanguage() {
   if(!prefs.SideShowLanguage || closing) return;
   try {
    IntPtr foreground=GetForegroundWindow(); if(foreground==IntPtr.Zero) return;
    uint pid; uint thread=GetWindowThreadProcessId(foreground,out pid); if(thread==0) return;
    IntPtr layout=GetKeyboardLayout(thread); int language=unchecked((int)layout.ToInt64())&0xffff;
    string next;
    try { var culture=CultureInfo.GetCultureInfo(language); string code=culture.TwoLetterISOLanguageName; next=code=="en" ? "ENG" : code=="ru" ? "RUS" : culture.ThreeLetterISOLanguageName.ToUpperInvariant(); }
    catch { next=language.ToString("X4",CultureInfo.InvariantCulture); }
    if(next!=currentInputLanguage) { currentInputLanguage=next; sideLanguageLabel.Text=next; }
   } catch(EntryPointNotFoundException) { sideLanguageLabel.Text="—"; } catch(DllNotFoundException) { sideLanguageLabel.Text="—"; }
  }
  bool sidePowerRefreshPending;
  void PowerStatusChanged() {
   if(closing) return;
   if(sideStatusBusy) sidePowerRefreshPending=true;
   else RequestSideStatus();
  }
  void RequestSideStatus() {
   if(closing || sideStatusBusy || !prefs.RightStatusIslandEnabled || !prefs.SideShowBattery && !prefs.SideShowWifi) return;
   sideStatusBusy=true;
   Task.Run(delegate { return DesktopStatus.ReadDock(); }).ContinueWith(delegate(Task<StatusSnapshot> task) {
    StatusSnapshot result=task.Status==TaskStatus.RanToCompletion ? task.Result : null;
    if(Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
    try { Dispatcher.BeginInvoke(new Action(delegate {
     sideStatusBusy=false;
     if(closing) return;
     if(result!=null) sideStatus=result;
     UpdateSideStatusPresentation();
     if(sidePowerRefreshPending) { sidePowerRefreshPending=false; RequestSideStatus(); }
    })); }
    catch(InvalidOperationException) { }
   });
  }
  void UpdateSideStatusPresentation() {
   if(sideBatteryButton==null) return;
   sideBatteryLabel.Text=sideStatus==null ? "—" : sideStatus.BatteryText;
   sideBatteryGlyph.SetBatteryOnAC(sideStatus!=null && sideStatus.BatteryOnAC);
   sideBatteryButton.ToolTip=sideStatus==null ? "Проверка батареи…" : sideStatus.BatteryHint+"\nОткрыть параметры питания";
   sideWifiButton.ToolTip=sideStatus==null ? "Проверка Wi-Fi…" : sideStatus.WifiText+"\n"+sideStatus.WifiHint+"\nОткрыть настройки Wi-Fi";
   sideBatteryButton.Opacity=sideStatus!=null && sideStatus.BatteryAvailable ? 1 : .55;
   sideWifiButton.Opacity=sideStatus!=null && sideStatus.WifiConnected ? 1 : .55;
  }

  [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
  [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint threadId);

  sealed class SideGlyph : FrameworkElement {
   public enum Kind { Battery,Wifi }
   readonly Kind kind;
   static readonly Geometry chargingBolt=(Geometry)Geometry.Parse("M 12,7 L 7,13 L 11,13 L 9,17 L 15,11 L 11,11 Z").GetAsFrozen();
   bool batteryOnAC;
   public SideGlyph(Kind value) { kind=value; IsHitTestVisible=false; }
   public void SetBatteryOnAC(bool connected) {
    if(batteryOnAC==connected) return;
    batteryOnAC=connected;
    InvalidateVisual();
   }
   protected override void OnRender(System.Windows.Media.DrawingContext dc) {
    double scale=Math.Min(ActualWidth,ActualHeight)/24; if(scale<=0) return;
    dc.PushTransform(new ScaleTransform(scale,scale));
    var pen=new Pen(Brushes.White,1.7) { StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round };
    if(kind==Kind.Battery) {
     dc.DrawRoundedRectangle(null,pen,new Rect(2,6,18,12),2,2);
     dc.DrawLine(pen,new Point(22,10),new Point(22,14));
     if(batteryOnAC) dc.DrawGeometry(Brushes.White,null,chargingBolt);
     else dc.DrawRectangle(Brushes.White,null,new Rect(4,8,4,8));
    }
    else {
     DrawCurve(dc,pen,new Point(2,8),new Point(12,-1),new Point(22,8));
     DrawCurve(dc,pen,new Point(5.5,12),new Point(12,6),new Point(18.5,12));
     DrawCurve(dc,pen,new Point(9,16),new Point(12,13),new Point(15,16)); dc.DrawEllipse(Brushes.White,null,new Point(12,20),1.3,1.3);
    }
    dc.Pop();
   }
   static void DrawCurve(System.Windows.Media.DrawingContext dc,Pen pen,Point start,Point control,Point end) {
    var geometry=new StreamGeometry(); using(var path=geometry.Open()) { path.BeginFigure(start,false,false); path.QuadraticBezierTo(control,end,true,false); }
    geometry.Freeze(); dc.DrawGeometry(null,pen,geometry);
   }
  }
 }
}
