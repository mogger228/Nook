using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Nook {
 // Reused for the lifetime of a button. Targets and frame updates allocate no
 // arrays, animation clocks, images or layout elements.
 internal sealed class HoverWaveItem {
  public Button Button;
  public ScaleTransform Scale;
  public TranslateTransform Offset;
  public bool Magnifies;
  // Width/X are scalar positions along the dock axis (Y for a vertical dock).
  public double Center,Width;
  public double TargetScale=1,TargetX,CurrentScale=1,CurrentX;
 }

 internal static class HoverWaveMath {
  public const double RadiusInSlots=2.5;
  public static void Targets(List<HoverWaveItem> items,double pointerX,bool inside,double maximum) {
   double extra=0;
   for(int i=0;i<items.Count;i++) {
    var item=items[i];
    double distance=Math.Abs(item.Center-pointerX)/(item.Width*RadiusInSlots);
    double weight=inside && item.Magnifies && distance<1 ? .5+.5*Math.Cos(Math.PI*distance) : 0;
    item.TargetScale=1+(maximum-1)*weight;
    extra+=item.Width*(item.TargetScale-1);
   }
   // Keep the expanded row/column centered and adjacent transformed hit targets apart.
   // Start participates in spacing but never in magnification.
   double before=-extra/2;
   for(int i=0;i<items.Count;i++) {
    var item=items[i]; double own=item.Width*(item.TargetScale-1);
    item.TargetX=before+own/2; before+=own;
   }
  }
  public static double Approach(double current,double target,double factor,double tolerance) {
   double next=current+(target-current)*factor;
   return Math.Abs(target-next)<=tolerance ? target : next;
  }
  public static double Padding(double slot,double maximum) {
   // The cosine lobe spans five slots; 3 is a conservative bound on their
   // combined weight. Reserve once in Place(), never during the rendering loop.
   return Math.Max(12,slot*(maximum-1)*1.5+4);
  }
 }

 partial class DockWindow {
  readonly List<HoverWaveItem> hoverWaveItems=new List<HoverWaveItem>();
  EventHandler hoverWaveRenderHandler;
  bool hoverWaveRunning,hoverWaveInside,hoverWaveDirty;
  double hoverWavePointerX,hoverWaveRowWidth,hoverWavePadding,hoverWaveTop,hoverWaveBottom,hoverWaveNextFrame=-1,hoverWaveLastFrame=-1;
  long hoverWaveFrames;
  bool HoverWaveEnabled { get { return prefs.Zoom && prefs.HoverWave; } }
  string HoverWaveDiagnostics { get { return (HoverWaveEnabled ? "enabled" : "disabled")+"/"+(hoverWaveRunning ? "running" : "idle")+" frames="+hoverWaveFrames; } }

  void InitializeHoverWave() {
   hoverWaveRenderHandler=RenderHoverWave;
   PreviewMouseMove+=MoveHoverWave;
   MouseLeave+=delegate { ReturnHoverWave(); };
   ContextMenuOpening+=delegate { ReturnHoverWave(); };
   IsVisibleChanged+=delegate { if(!IsVisible) ResetHoverWave(false); };
  }
  void RegisterHoverWave(Button button,ScaleTransform scale,TranslateTransform offset,bool magnifies) {
   hoverWaveItems.Add(new HoverWaveItem { Button=button,Scale=scale,Offset=offset,Magnifies=magnifies });
  }
  void RefreshHoverWaveGeometry() {
   hoverWaveItems.Sort(delegate(HoverWaveItem a,HoverWaveItem b) { return row.Children.IndexOf(a.Button).CompareTo(row.Children.IndexOf(b.Button)); });
   double x=0;
   for(int i=0;i<hoverWaveItems.Count;i++) {
    var item=hoverWaveItems[i]; item.Width=IsVertical ? item.Button.Height : item.Button.Width;
    item.Center=x+item.Width/2; x+=item.Width;
   }
   hoverWaveRowWidth=x;
   hoverWavePadding=HoverWaveMath.Padding(hoverWaveItems.Count>0 ? hoverWaveItems[0].Width : prefs.Size+8,prefs.ZoomScale);
   // These are cross-axis bounds: vertical left-edge icons grow toward the right.
   hoverWaveTop=IsVertical || prefs.Top ? -5 : -HoverHeadroom-5;
   hoverWaveBottom=BandHeight-1+(IsVertical || prefs.Top ? HoverHeadroom : 0);
   if(HoverWaveEnabled && hoverWaveInside && !reordering) { hoverWaveDirty=true; StartHoverWave(); }
  }
  void MoveHoverWave(object sender,MouseEventArgs args) {
   if(closing || !HoverWaveEnabled || reordering || Mouse.LeftButton==MouseButtonState.Pressed || hoverWaveRowWidth<=0) return;
   Point pointer=args.GetPosition(row);
   double along=IsVertical ? pointer.Y : pointer.X;
   double across=IsVertical ? pointer.X : pointer.Y;
   bool inside=along>=-hoverWavePadding && along<=hoverWaveRowWidth+hoverWavePadding && across>=hoverWaveTop && across<=hoverWaveBottom;
   if(!inside) { ReturnHoverWave(); return; }
   if(hoverWaveInside && Math.Abs(along-hoverWavePointerX)<.25) return;
   hoverWaveInside=true; hoverWavePointerX=along; hoverWaveDirty=true; StartHoverWave();
  }
  void ReturnHoverWave() {
   if(!hoverWaveInside) return;
   hoverWaveInside=false; hoverWaveDirty=true; StartHoverWave();
  }
  void StartHoverWave() {
   if(hoverWaveRunning || closing || hoverWaveItems.Count==0) return;
   hoverWaveRunning=true; hoverWaveNextFrame=-1; hoverWaveLastFrame=-1;
   CompositionTarget.Rendering+=hoverWaveRenderHandler;
  }
  void StopHoverWave() {
   if(!hoverWaveRunning) return;
   CompositionTarget.Rendering-=hoverWaveRenderHandler;
   hoverWaveRunning=false;
  }
  void RenderHoverWave(object sender,EventArgs args) {
   if(closing || !IsVisible || !HoverWaveEnabled || reordering) { ResetHoverWave(false); return; }
   double now=((RenderingEventArgs)args).RenderingTime.TotalSeconds;
   if(hoverWaveNextFrame>=0 && now+0.00001<hoverWaveNextFrame) return;
   if(hoverWaveNextFrame<0) hoverWaveNextFrame=now;
   hoverWaveNextFrame+=1.0/60;
   if(hoverWaveNextFrame<now) hoverWaveNextFrame=now+1.0/60;
   double elapsed=hoverWaveLastFrame<0 ? 1.0/60 : Math.Max(0,Math.Min(.05,now-hoverWaveLastFrame));
   hoverWaveLastFrame=now; hoverWaveFrames++;
   if(hoverWaveDirty) { HoverWaveMath.Targets(hoverWaveItems,hoverWavePointerX,hoverWaveInside,prefs.ZoomScale); hoverWaveDirty=false; }
   double factor=prefs.Animations ? 1-Math.Exp(-elapsed/.045) : 1;
   bool moving=false;
   for(int i=0;i<hoverWaveItems.Count;i++) {
    var item=hoverWaveItems[i];
    double scale=HoverWaveMath.Approach(item.CurrentScale,item.TargetScale,factor,.0005);
    double offset=HoverWaveMath.Approach(item.CurrentX,item.TargetX,factor,.025);
    if(scale!=item.CurrentScale) { item.CurrentScale=scale; item.Scale.ScaleX=scale; item.Scale.ScaleY=scale; }
    if(offset!=item.CurrentX) { item.CurrentX=offset; if(IsVertical) item.Offset.Y=offset; else item.Offset.X=offset; }
    moving|=scale!=item.TargetScale || offset!=item.TargetX;
   }
   if(!moving) StopHoverWave();
  }
  void ResetHoverWave(bool clear) {
   StopHoverWave(); hoverWaveInside=false; hoverWaveDirty=false;
   for(int i=0;i<hoverWaveItems.Count;i++) {
    var item=hoverWaveItems[i];
    item.Scale.BeginAnimation(ScaleTransform.ScaleXProperty,null); item.Scale.BeginAnimation(ScaleTransform.ScaleYProperty,null);
    item.Scale.ScaleX=1; item.Scale.ScaleY=1; item.Offset.X=0; item.Offset.Y=0;
    item.CurrentScale=item.TargetScale=1; item.CurrentX=item.TargetX=0;
   }
   if(clear) { hoverWaveItems.Clear(); hoverWaveRowWidth=0; }
  }
  double SuspendHoverWaveForDrag(Button button) {
   double offset=0;
   for(int i=0;i<hoverWaveItems.Count;i++) if(hoverWaveItems[i].Button==button) { offset=IsVertical ? hoverWaveItems[i].Offset.Y : hoverWaveItems[i].Offset.X; break; }
   ResetHoverWave(false); return offset;
  }
 }
}
