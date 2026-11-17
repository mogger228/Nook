using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Nook {
 partial class DockWindow {
  // Only pointer activity drives reordering. There is no rendering loop or idle timer.
  readonly Dictionary<Button,AppItem> reorderItems=new Dictionary<Button,AppItem>();
  readonly Dictionary<Button,TranslateTransform> reorderOffsets=new Dictionary<Button,TranslateTransform>();
  readonly List<string> sessionOrder=new List<string>();
  Button reorderCandidate;
  Point reorderStart;
  bool reordering,reorderVertical;
  int reorderOrigin,reorderTarget;
  List<Button> reorderButtons;
  bool reorderEscapeRegistered,reorderCleanupInstalled;
  [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
  [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd,int id);

  static string ReorderKey(AppItem item) { return (item.Pinned ? "pin:"+item.Path : "app:"+item.Key); }

  List<AppItem> ApplySessionOrder(List<AppItem> items) {
   if(sessionOrder.Count==0) return items;
   var positions=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
   for(int i=0;i<sessionOrder.Count;i++) if(!positions.ContainsKey(sessionOrder[i])) positions[sessionOrder[i]]=i;
   return items.OrderBy(item=>positions.ContainsKey(ReorderKey(item)) ? positions[ReorderKey(item)] : int.MaxValue).ToList();
  }

  void CancelReorderForRefresh() {
   EndReorder(false);
   reorderItems.Clear(); reorderOffsets.Clear();
  }

  void AttachReorder(Button button,AppItem item) {
   if(!reorderCleanupInstalled) { reorderCleanupInstalled=true; Closing += delegate { EndReorder(false); }; }
   var transforms=new TransformGroup();
   if(button.RenderTransform!=null) transforms.Children.Add(button.RenderTransform);
   var offset=new TranslateTransform(); transforms.Children.Add(offset); button.RenderTransform=transforms;
   reorderItems[button]=item; reorderOffsets[button]=offset;
   button.PreviewMouseLeftButtonDown += delegate(object sender,MouseButtonEventArgs e) {
    if(reorderCandidate!=null) EndReorder(false);
    reorderCandidate=button; reorderStart=e.GetPosition(row); reordering=false; reorderVertical=IsVertical;
   };
   button.PreviewMouseMove += delegate(object sender,MouseEventArgs e) {
    if(reorderCandidate!=button) return;
    if(e.LeftButton!=MouseButtonState.Pressed) { EndReorder(false); return; }
    Point point=e.GetPosition(row); double delta=reorderVertical ? point.Y-reorderStart.Y : point.X-reorderStart.X;
    if(!reordering) {
     double crossDelta=reorderVertical ? point.X-reorderStart.X : point.Y-reorderStart.Y;
     if(Math.Abs(delta)<(reorderVertical ? SystemParameters.MinimumVerticalDragDistance : SystemParameters.MinimumHorizontalDragDistance) && Math.Abs(crossDelta)<(reorderVertical ? SystemParameters.MinimumHorizontalDragDistance : SystemParameters.MinimumVerticalDragDistance)) return;
     reorderButtons=row.Children.OfType<Button>().Where(reorderItems.ContainsKey).ToList();
     reorderOrigin=reorderButtons.IndexOf(button); reorderTarget=reorderOrigin;
     if(reorderOrigin<0 || reorderButtons.Count<2) { EndReorder(false); return; }
     reordering=true;
     double waveOffset=SuspendHoverWaveForDrag(button);
     if(reorderVertical) reorderStart.Y-=waveOffset; else reorderStart.X-=waveOffset;
     delta+=waveOffset;
     button.CaptureMouse();
     Panel.SetZIndex(button,10); button.Opacity=.88;
     button.PreviewKeyDown += ReorderKeyDown;
     // The dock does not activate on click; an application-wide key hook is unnecessary.
     InputManager.Current.PreProcessInput += ReorderInput;
     reorderEscapeRegistered=RegisterHotKey(handle,0x4D1,0x4000,0x1B);
     var source=HwndSource.FromHwnd(handle); if(source!=null) source.AddHook(ReorderMessage);
    }
    double slot=reorderVertical ? button.ActualHeight : button.ActualWidth;
    if(slot<=0) return;
    delta=Math.Max(-reorderOrigin*slot,Math.Min((reorderButtons.Count-1-reorderOrigin)*slot,delta));
    reorderTarget=Math.Max(0,Math.Min(reorderButtons.Count-1,(int)Math.Round(reorderOrigin+delta/slot)));
    var own=reorderOffsets[button]; var axis=ReorderAxis; own.BeginAnimation(axis,null); own.SetValue(axis,delta);
    for(int i=0;i<reorderButtons.Count;i++) {
     if(i==reorderOrigin) continue;
     double destination=(reorderTarget>reorderOrigin && i>reorderOrigin && i<=reorderTarget) ? -slot : (reorderTarget<reorderOrigin && i>=reorderTarget && i<reorderOrigin) ? slot : 0;
     Slide(reorderOffsets[reorderButtons[i]],destination);
    }
    e.Handled=true;
   };
   button.PreviewMouseLeftButtonUp += delegate(object sender,MouseButtonEventArgs e) {
    if(reorderCandidate!=button) return;
    if(reordering) { e.Handled=true; EndReorder(true); }
    else reorderCandidate=null;
   };
   button.LostMouseCapture += delegate { if(reorderCandidate==button) EndReorder(false); };
  }

  void ReorderKeyDown(object sender,KeyEventArgs e) { if(e.Key==Key.Escape && reordering) { e.Handled=true; EndReorder(false); } }
  IntPtr ReorderMessage(IntPtr hwnd,int message,IntPtr w,IntPtr l,ref bool handled) { if(message==0x312 && w.ToInt32()==0x4D1 && reordering) { handled=true; EndReorder(false); } return IntPtr.Zero; }
  void ReorderInput(object sender,PreProcessInputEventArgs e) {
   var key=e.StagingItem.Input as KeyEventArgs;
   if(key!=null && key.IsDown && key.Key==Key.Escape && reordering) { key.Handled=true; EndReorder(false); }
  }

  void Slide(TranslateTransform transform,double destination) {
   // Repeated pointer events at the same gap must not restart the neighbor animation.
   var axis=ReorderAxis;
   var clock=transform.GetAnimationBaseValue(axis);
   if((double)clock==destination) return;
   double current=(double)transform.GetValue(axis);
   transform.BeginAnimation(axis,null); transform.SetValue(axis,destination);
   if(prefs.Animations) transform.BeginAnimation(axis,new DoubleAnimation(current,destination,TimeSpan.FromMilliseconds(150)) { EasingFunction=new CubicEase { EasingMode=EasingMode.EaseOut } });
  }
  DependencyProperty ReorderAxis { get { return reorderVertical ? TranslateTransform.YProperty : TranslateTransform.XProperty; } }

  void EndReorder(bool commit) {
   var dragged=reorderCandidate; bool wasDragging=reordering;
   reorderCandidate=null; reordering=false;
   InputManager.Current.PreProcessInput -= ReorderInput;
   if(reorderEscapeRegistered) { UnregisterHotKey(handle,0x4D1); reorderEscapeRegistered=false; }
   if(handle!=IntPtr.Zero) { var source=HwndSource.FromHwnd(handle); if(source!=null && !source.IsDisposed) source.RemoveHook(ReorderMessage); }
   if(dragged==null) return;
   dragged.PreviewKeyDown -= ReorderKeyDown;
   Panel.SetZIndex(dragged,0); dragged.Opacity=1;
   if(wasDragging && commit && reorderButtons!=null && reorderTarget!=reorderOrigin) {
    double slot=reorderVertical ? dragged.ActualHeight : dragged.ActualWidth;
    var axis=ReorderAxis;
    var positions=new Dictionary<Button,double>();
    for(int i=0;i<reorderButtons.Count;i++) positions[reorderButtons[i]]=i*slot+(double)reorderOffsets[reorderButtons[i]].GetValue(axis);
    int first=row.Children.IndexOf(reorderButtons[0]);
    row.Children.Remove(dragged); row.Children.Insert(first+reorderTarget,dragged);
    var ordered=row.Children.OfType<Button>().Where(reorderItems.ContainsKey).ToList();
    sessionOrder.Clear(); foreach(var button in ordered) sessionOrder.Add(ReorderKey(reorderItems[button]));
    var pins=ordered.Where(button=>reorderItems[button].Pinned).Select(button=>reorderItems[button].Path).ToList();
    // Missing executable/shortcut pins stay stored, so a temporarily unavailable path is not deleted.
    pins.AddRange(prefs.Pins.Where(path=>!pins.Contains(path,StringComparer.OrdinalIgnoreCase)));
    prefs.Pins=pins; Save();
    for(int i=0;i<ordered.Count;i++) { var offset=reorderOffsets[ordered[i]]; offset.BeginAnimation(axis,null); offset.SetValue(axis,positions[ordered[i]]-i*slot); Slide(offset,0); }
   } else foreach(var offset in reorderOffsets.Values) Slide(offset,0);
   if(dragged.IsMouseCaptured) dragged.ReleaseMouseCapture();
   reorderButtons=null;
   if(wasDragging) RefreshHoverWaveGeometry();
  }
 }
}
