using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Nook {
 partial class DockWindow {
  readonly TranslateTransform autoHideOffset=new TranslateTransform();
  readonly DispatcherTimer autoHideDelay=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(450) };
  DockEdgeTrigger edgeTrigger;
  bool autoHideConfigured,autoHideHidden,dockFullscreen;
  int autoHideGeneration,dockMenuDepth;
  string PlacementDiagnostics { get { return " Position="+(IsVertical ? "left" : (prefs.Top ? "top" : "bottom"))+" AutoHide="+prefs.AutoHide+" Hidden="+autoHideHidden+" HideTimer="+autoHideDelay.IsEnabled+" EdgeTrigger="+(edgeTrigger!=null && edgeTrigger.IsVisible)+" TaskbarLease="+Native.TaskbarOverlayDiagnostics; } }
  void InitializeAutoHide() {
   surface.RenderTransform=autoHideOffset;
   autoHideDelay.Tick+=delegate { autoHideDelay.Stop(); HideDockAtEdge(); };
   MouseEnter+=delegate { if(prefs.AutoHide) RevealDockAtEdge(); };
   MouseLeave+=delegate { ScheduleAutoHide(); };
   DragEnter+=delegate { if(prefs.AutoHide) RevealDockAtEdge(); };
   DragLeave+=delegate { ScheduleAutoHide(); };
  }
  void ConfigureAutoHide(Rect shown,Rect monitor) {
   if(!prefs.AutoHide) {
    autoHideConfigured=false; autoHideHidden=false; autoHideGeneration++; autoHideDelay.Stop();
    if(edgeTrigger!=null) { edgeTrigger.Close(); edgeTrigger=null; }
    autoHideOffset.BeginAnimation(TranslateTransform.XProperty,null); autoHideOffset.BeginAnimation(TranslateTransform.YProperty,null);
    autoHideOffset.X=autoHideOffset.Y=0;
    Visibility=dockFullscreen ? Visibility.Hidden : Visibility.Visible;
    return;
   }
   if(edgeTrigger==null) edgeTrigger=new DockEdgeTrigger(RevealDockAtEdge,ScheduleAutoHide);
   var source=PresentationSource.FromVisual(this); var scale=source.CompositionTarget.TransformFromDevice;
   double stripX=Math.Max(1,2*scale.M11),stripY=Math.Max(1,2*scale.M22);
   Rect trigger;
   if(IsVertical) trigger=new Rect(monitor.Left,Math.Max(monitor.Top,shown.Top),stripX,Math.Min(monitor.Bottom,shown.Bottom)-Math.Max(monitor.Top,shown.Top));
   else trigger=new Rect(Math.Max(monitor.Left,shown.Left),prefs.Top ? monitor.Top : monitor.Bottom-stripY,Math.Min(monitor.Right,shown.Right)-Math.Max(monitor.Left,shown.Left),stripY);
   edgeTrigger.SetBounds(trigger);
   if(!edgeTrigger.IsVisible) edgeTrigger.Show();
   if(autoHideHidden) {
    autoHideGeneration++; autoHideOffset.BeginAnimation(TranslateTransform.XProperty,null); autoHideOffset.BeginAnimation(TranslateTransform.YProperty,null);
    autoHideOffset.X=IsVertical ? -Width : 0;
    autoHideOffset.Y=IsVertical ? 0 : (prefs.Top ? -Height : Height);
    Visibility=Visibility.Hidden;
   }
   if(!autoHideConfigured) { autoHideConfigured=true; ScheduleAutoHide(); }
  }
  void ScheduleAutoHide() {
   if(closing || !prefs.AutoHide || autoHideHidden) return;
   autoHideDelay.Stop(); autoHideDelay.Start();
  }
  void RevealDockAtEdge() {
   if(closing || !prefs.AutoHide) return;
   autoHideDelay.Stop();
   if(!autoHideHidden && IsVisible) return;
   autoHideHidden=false; autoHideGeneration++;
   Visibility=Visibility.Visible;
   Animate(autoHideOffset,TranslateTransform.XProperty,0,160);
   Animate(autoHideOffset,TranslateTransform.YProperty,0,160);
  }
  void HideDockAtEdge() {
   if(closing || !prefs.AutoHide || autoHideHidden || IsMouseOver || dockMenuDepth>0 || edgeTrigger!=null && edgeTrigger.IsMouseOver) return;
   if(reordering || Mouse.LeftButton==MouseButtonState.Pressed || Mouse.Captured!=null) { ScheduleAutoHide(); return; }
   ResetHoverWave(false);
   autoHideHidden=true; int generation=++autoHideGeneration;
   var property=IsVertical ? TranslateTransform.XProperty : TranslateTransform.YProperty;
   double target=IsVertical ? -Width : (prefs.Top ? -Height : Height);
   if(!prefs.Animations) { autoHideOffset.SetValue(property,target); Visibility=Visibility.Hidden; return; }
   double current=(double)autoHideOffset.GetValue(property);
   var animation=new DoubleAnimation(current,target,TimeSpan.FromMilliseconds(160)) { FillBehavior=FillBehavior.Stop,EasingFunction=new CubicEase { EasingMode=EasingMode.EaseIn } };
   autoHideOffset.SetValue(property,target);
   var clock=(AnimationClock)animation.CreateClock(true);
   clock.Completed+=delegate {
    if(clock.Controller!=null) clock.Controller.Remove();
    if(!closing && prefs.AutoHide && autoHideHidden && generation==autoHideGeneration) Visibility=Visibility.Hidden;
   };
   autoHideOffset.ApplyAnimationClock(property,clock,HandoffBehavior.SnapshotAndReplace);
  }
  void HoldDockMenu(bool opened) {
   dockMenuDepth=Math.Max(0,dockMenuDepth+(opened ? 1 : -1));
   if(opened) { autoHideDelay.Stop(); if(prefs.AutoHide) RevealDockAtEdge(); }
   else ScheduleAutoHide();
  }
  void UpdateFullscreenVisibility(bool full) {
   bool wasFullscreen=dockFullscreen; dockFullscreen=full;
   if(!prefs.AutoHide) Visibility=full ? Visibility.Hidden : Visibility.Visible;
   else if(full && !wasFullscreen) ScheduleAutoHide();
  }
  void RestoreDockAboveDesktop(IntPtr foreground) {
   if(closing || !Native.IsDesktopWindow(foreground)) return;
   // Explorer can raise the desktop above topmost tool windows without hiding them.
   // Reassert only the surfaces we intend to show; never reveal an auto-hidden dock.
   if(!dockFullscreen && !autoHideHidden && Visibility==Visibility.Visible) Native.RaiseDesktopSurface(handle);
   if(edgeTrigger!=null && edgeTrigger.IsVisible) Native.RaiseDesktopSurface(new WindowInteropHelper(edgeTrigger).Handle);
  }
  void StopAutoHide() {
   autoHideGeneration++; autoHideDelay.Stop();
   if(edgeTrigger!=null) { edgeTrigger.Close(); edgeTrigger=null; }
   autoHideOffset.BeginAnimation(TranslateTransform.XProperty,null); autoHideOffset.BeginAnimation(TranslateTransform.YProperty,null);
  }
 }
 // A two-device-pixel input surface replaces global mouse hooks and idle polling.
 sealed class DockEdgeTrigger : Window {
  public DockEdgeTrigger(Action enter,Action leave) {
   Title="Nook — край экрана"; WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize;
   AllowsTransparency=true; Background=new SolidColorBrush(Color.FromArgb(1,0,0,0));
   ShowInTaskbar=false; ShowActivated=false; Topmost=true; Focusable=false; AllowDrop=true;
   MouseEnter+=delegate { enter(); }; MouseLeave+=delegate { leave(); };
   DragEnter+=delegate { enter(); }; DragLeave+=delegate { leave(); };
   SourceInitialized+=delegate {
    IntPtr hwnd=new WindowInteropHelper(this).Handle;
    Native.SetWindowLongPtr(hwnd,-20,new IntPtr(Native.GetWindowLongPtr(hwnd,-20).ToInt64()|0x08000000L|0x80L));
    HwndSource.FromHwnd(hwnd).AddHook(NoActivate);
   };
  }
  static IntPtr NoActivate(IntPtr hwnd,int msg,IntPtr w,IntPtr l,ref bool handled) {
   if(msg==0x112 && (w.ToInt64()&0xFFF0)==0xF060) { handled=true; return IntPtr.Zero; }
   if(msg==0x21) { handled=true; return new IntPtr(3); } return IntPtr.Zero;
  }
  public void SetBounds(Rect bounds) { Left=bounds.Left; Top=bounds.Top; Width=Math.Max(1,bounds.Width); Height=Math.Max(1,bounds.Height); }
 }
}
