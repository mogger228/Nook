using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace Nook {
 partial class DockWindow {
  bool placingDock;
  static ControlTemplate verticalOverflowTemplate;
  void PlaceDock() {
   if(closing || placingDock || handle==IntPtr.Zero) return;
   var source=PresentationSource.FromVisual(this); if(source==null) return;
   placingDock=true;
   try {
    var transform=source.CompositionTarget.TransformFromDevice;
    Native.SetTaskbarOverlayMode(taskbarHidden && (IsVertical || prefs.Top || prefs.AutoHide));
    if(taskbarHidden && Native.TaskbarVisible) Native.HideTaskbars();
    var screen=Native.MonitorRect(handle);
    var monitor=new Rect(transform.Transform(new Point(screen.Left,screen.Top)),transform.Transform(new Point(screen.Right,screen.Bottom)));
    double padding=Math.Max(12,prefs.Zoom ? (prefs.Size+8)*(prefs.ZoomScale-1)/2+4 : 12);
    if(HoverWaveEnabled) padding=Math.Max(padding,HoverWaveMath.Padding(prefs.Size+8,prefs.ZoomScale));
    row.Orientation=IsVertical ? Orientation.Vertical : Orientation.Horizontal;
    row.HorizontalAlignment=HorizontalAlignment.Center; row.VerticalAlignment=VerticalAlignment.Center;
    double length=row.Children.Count*(prefs.Size+8)+padding*2+2;
    if(IsVertical) PlaceVerticalDock(monitor,transform,length,padding);
    else PlaceHorizontalDock(monitor,transform,length,padding);
    PlaceSideIslands(); RefreshHoverWaveGeometry();
    ConfigureAutoHide(new Rect(Left,Top,Width,Height),monitor);
   } finally { placingDock=false; }
  }
  void SetDockOverflow(bool overflow) {
   row.HorizontalAlignment=IsVertical && overflow ? HorizontalAlignment.Left : HorizontalAlignment.Center;
   var scroll=island.Child as ScrollViewer;
   if(!overflow && scroll!=null) { scroll.Content=null; island.Child=row; }
   else if(overflow) {
    if(scroll==null) { island.Child=null; scroll=new ScrollViewer { Content=row,CanContentScroll=false }; island.Child=scroll; }
    scroll.HorizontalScrollBarVisibility=IsVertical ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
    scroll.VerticalScrollBarVisibility=IsVertical ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Disabled;
    if(IsVertical) {
     if(verticalOverflowTemplate==null) verticalOverflowTemplate=(ControlTemplate)XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ScrollViewer}'>
 <ScrollContentPresenter x:Name='PART_ScrollContentPresenter' Content='{TemplateBinding Content}' ContentTemplate='{TemplateBinding ContentTemplate}' CanContentScroll='{TemplateBinding CanContentScroll}'/>
</ControlTemplate>");
     scroll.Template=verticalOverflowTemplate; scroll.HorizontalContentAlignment=HorizontalAlignment.Left;
     scroll.ApplyTemplate();
     var presenter=scroll.Template.FindName("PART_ScrollContentPresenter",scroll) as ScrollContentPresenter;
     if(presenter!=null) presenter.Margin=new Thickness(0,0,-HoverHeadroom,0);
    } else { scroll.ClearValue(Control.TemplateProperty); scroll.HorizontalContentAlignment=HorizontalAlignment.Stretch; }
   }
  }
  void PlaceHorizontalDock(Rect monitor,Matrix scale,double length,double padding) {
   UpdateSideIslandPresentation();
   double side=HasSideIslands ? SideIslandWidth+2*GlassShadowInset+SideIslandGap : 0;
   double available=Math.Max(80,monitor.Width-24-2*GlassShadowInset-2*side);
   if(length>available) padding=Math.Min(padding,Math.Max(2,(available-prefs.Size-10)/2));
   double band=BandHeight+(length>available ? SystemParameters.HorizontalScrollBarHeight : 0);
   island.Width=double.NaN; island.Height=band;
   island.HorizontalAlignment=HorizontalAlignment.Stretch;
   island.VerticalAlignment=prefs.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
   island.Padding=new Thickness(padding,2,padding,2); island.Margin=new Thickness(GlassShadowInset,0,GlassShadowInset,0);
   double nativeReservation=taskbarHidden && !prefs.Top && !Native.TaskbarAutoHides ? NativeBandHeight : 0;
   if(appbar!=null) appbar.Position(prefs.Top ? 1u : 3u,prefs.AutoHide ? 0 : (int)Math.Ceiling(Math.Max(0,band+(prefs.Top ? prefs.BottomOffset*scale.M22 : 0)-nativeReservation)/scale.M22));
   var reserved=appbar==null ? Native.MonitorWorkRect(handle) : appbar.Reserved;
   var work=new Rect(scale.Transform(new Point(reserved.Left,reserved.Top)),scale.Transform(new Point(reserved.Right,reserved.Bottom)));
   if(prefs.AutoHide || taskbarHidden && !prefs.Top) work=monitor;
   double center=Math.Min(available,length)+2*GlassShadowInset;
   SetDockOverflow(length>available);
   ApplySideIslandGrid(center,band,center+2*side);
   double left=work.Left+(work.Width-Width)/2;
   double top=prefs.Top ? work.Top+prefs.BottomOffset*scale.M22 : work.Bottom-Height-prefs.BottomOffset*scale.M22;
   // Position is stable during reveal/hide; only the existing surface translates.
   Animate(this,LeftProperty,left,prefs.AutoHide ? 0 : 180); Animate(this,TopProperty,top,prefs.AutoHide ? 0 : 180);
   LayoutDockHighlights(false,0);
  }
  double VerticalClockLength { get { return Math.Max(38,BandHeight); } }
  double VerticalStatusLength {
   get {
    sideStatusContent.Measure(new Size(Math.Max(1,BandHeight-6),double.PositiveInfinity));
    return Math.Ceiling(sideStatusContent.DesiredSize.Height+rightStatusIsland.Padding.Top+rightStatusIsland.Padding.Bottom+rightStatusIsland.BorderThickness.Top+rightStatusIsland.BorderThickness.Bottom+8);
   }
  }
  void PlaceVerticalDock(Rect monitor,Matrix scale,double length,double padding) {
   UpdateSideIslandPresentation();
   double sideLength=Math.Max(VerticalClockLength,VerticalStatusLength);
   double slot=HasSideIslands ? sideLength+2*GlassShadowInset : 0;
   double side=slot+(HasSideIslands ? SideIslandGap : 0);
   var workRect=Native.MonitorWorkRect(handle);
   var work=taskbarHidden || prefs.AutoHide ? monitor : new Rect(scale.Transform(new Point(workRect.Left,workRect.Top)),scale.Transform(new Point(workRect.Right,workRect.Bottom)));
   double available=Math.Max(80,work.Height-24-2*GlassShadowInset-2*side);
   if(length>available) padding=Math.Min(padding,Math.Max(2,(available-prefs.Size-10)/2));
   // The overflow viewer has no visible scrollbar, so it never consumes dock width.
   double band=BandHeight;
   double gap=prefs.BottomOffset*scale.M11;
   if(appbar!=null) appbar.Position(0,prefs.AutoHide ? 0 : (int)Math.Ceiling((band+gap)/scale.M11));
   var reserved=appbar==null ? Native.MonitorWorkRect(handle) : appbar.Reserved;
   island.Width=band; island.Height=Math.Min(length,available);
   island.HorizontalAlignment=HorizontalAlignment.Left; island.VerticalAlignment=VerticalAlignment.Center;
   island.Padding=new Thickness(2,padding,2,padding); island.Margin=new Thickness(0,GlassShadowInset,0,GlassShadowInset);
   SetDockOverflow(length>available);
   surface.ColumnDefinitions.Clear(); surface.RowDefinitions.Clear();
   foreach(double part in new double[] { slot,HasSideIslands ? SideIslandGap : 0,island.Height+2*GlassShadowInset,HasSideIslands ? SideIslandGap : 0,slot }) surface.RowDefinitions.Add(new RowDefinition { Height=new GridLength(part) });
   SetDockCell(glassShadow,2,0); SetDockCell(island,2,0); SetDockCell(mainEdgeHighlight,2,0);
   SetDockCell(leftClockShadow,0,0); SetDockCell(leftClockIsland,0,0); SetDockCell(leftEdgeHighlight,0,0);
   SetDockCell(rightStatusShadow,4,0); SetDockCell(rightStatusIsland,4,0); SetDockCell(rightEdgeHighlight,4,0);
   leftClockIsland.Width=band; leftClockIsland.Height=VerticalClockLength;
   rightStatusIsland.Width=band; rightStatusIsland.Height=VerticalStatusLength;
   leftClockIsland.HorizontalAlignment=rightStatusIsland.HorizontalAlignment=HorizontalAlignment.Left;
   leftClockIsland.VerticalAlignment=VerticalAlignment.Bottom; rightStatusIsland.VerticalAlignment=VerticalAlignment.Top;
   leftClockIsland.Margin=rightStatusIsland.Margin=new Thickness(0,GlassShadowInset,0,GlassShadowInset);
   Width=band+HoverHeadroom; Height=island.Height+2*GlassShadowInset+2*side;
   Animate(this,LeftProperty,(prefs.AutoHide ? monitor.Left : reserved.Left*scale.M11)+gap,0);
   Animate(this,TopProperty,work.Top+(work.Height-Height)/2,0);
   LayoutDockHighlights(true,slot);
  }
  static void SetDockCell(FrameworkElement element,int rowIndex,int column) { Grid.SetRow(element,rowIndex); Grid.SetColumn(element,column); }
  void LayoutDockHighlights(bool vertical,double slot) {
   PlaceDockHighlight(mainEdgeHighlight,island,vertical ? GlassShadowInset : (prefs.Top ? 0 : HoverHeadroom));
   PlaceDockHighlight(leftEdgeHighlight,leftClockIsland,vertical ? slot-VerticalClockLength-GlassShadowInset : (prefs.Top ? 0 : HoverHeadroom));
   PlaceDockHighlight(rightEdgeHighlight,rightStatusIsland,vertical ? GlassShadowInset : (prefs.Top ? 0 : HoverHeadroom));
  }
  void PlaceDockHighlight(Border line,Border plate,double top) {
   line.VerticalAlignment=VerticalAlignment.Top;
   line.HorizontalAlignment=IsVertical ? HorizontalAlignment.Left : HorizontalAlignment.Center;
   line.Margin=new Thickness(IsVertical ? 0 : GlassShadowInset,top,IsVertical ? 0 : GlassShadowInset,0);
  }
 }
}
