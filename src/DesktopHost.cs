using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace Nook {
 // UI-thread-owned desktop surfaces. No top-level Window, SetParent, global DPI
 // changes, private Explorer messages, window activation, timers or injection.
 internal sealed class DesktopHost : IDisposable {
  const uint WS_CHILD=0x40000000,WS_CLIPSIBLINGS=0x04000000,WS_CLIPCHILDREN=0x02000000;
  const uint SWP_NOACTIVATE=0x10,SWP_SHOWWINDOW=0x40;
  readonly Dictionary<HwndSource,IntPtr> surfaces=new Dictionary<HwndSource,IntPtr>();
  IntPtr desktopParent;
  bool disposed;
  string failure;

  public bool IsHostAvailable { get { return !disposed && FindHost()!=IntPtr.Zero; } }
  public string Diagnostics { get { return "Desktop parent="+desktopParent+" Surfaces="+surfaces.Count+" LastFailure="+(failure??"none"); } }

  // Failure returns null with no visible native window. Retry on shell/display
  // events; detach the old RootVisual before assigning it to a replacement source.
  public HwndSource CreateSurface(string title,FrameworkElement content,bool clickThrough) {
   if(disposed || content==null) { failure="Desktop host disposed or content missing."; return null; }
   IntPtr parent=FindHost();
   if(parent==IntPtr.Zero) { failure="Desktop icon container not found."; return null; }
   IntPtr hostContext=GetWindowDpiAwarenessContext(parent);
   if(hostContext==IntPtr.Zero) { failure="Desktop DPI awareness context unavailable."; return null; }
   IntPtr previousContext=SetThreadDpiAwarenessContext(hostContext);
   if(previousContext==IntPtr.Zero) { failure="Cannot enter desktop DPI awareness context."; return null; }
   HwndSource surface=null;
   try {
    var options=new HwndSourceParameters(title??"Nook desktop surface") {
     ParentWindow=parent,
     WindowStyle=unchecked((int)(WS_CHILD|WS_CLIPSIBLINGS|WS_CLIPCHILDREN)),
     ExtendedWindowStyle=0x08000000|0x80|(clickThrough ? 0x20 : 0),
     UsesPerPixelTransparency=true,
     PositionX=0,PositionY=0,Width=1,Height=1
    };
    surface=new HwndSource(options);
    if(surface.Handle==IntPtr.Zero || GetParent(surface.Handle)!=parent) { failure="Desktop child creation failed."; surface.Dispose(); return null; }
    surface.CompositionTarget.BackgroundColor=System.Windows.Media.Colors.Transparent;
    surface.RootVisual=content;
    if(clickThrough) surface.AddHook(ClickThroughHook);
    surfaces[surface]=parent; failure=null;
    return surface;
   } catch(Exception error) {
    failure="Desktop child creation: "+error.Message;
    if(surface!=null && !surface.IsDisposed) { try { surface.RootVisual=null; } catch(InvalidOperationException) { } finally { surface.Dispose(); } }
    return null;
   } finally { SetThreadDpiAwarenessContext(previousContext); }
  }

  public bool IsAttached(HwndSource surface) {
   IntPtr parent;
   return !disposed && surface!=null && !surface.IsDisposed && surfaces.TryGetValue(surface,out parent) && parent!=IntPtr.Zero && IsWindow(parent) && IsWindow(surface.Handle) && GetParent(surface.Handle)==parent;
  }

  // Rect is PHYSICAL screen pixels, not DIPs. Position the native child only;
  // do not use WPF Window.Left/Top, which assume a top-level window.
  public bool Place(HwndSource surface,Rect screenPixels,bool show) {
   if(!IsAttached(surface)) { failure="Desktop child or Explorer parent was destroyed; recreate surface."; Hide(surface); return false; }
   if(screenPixels.IsEmpty || !Finite(screenPixels.X) || !Finite(screenPixels.Y) || !Finite(screenPixels.Width) || !Finite(screenPixels.Height) || screenPixels.Width<=0 || screenPixels.Height<=0) { failure="Invalid physical desktop rectangle."; Hide(surface); return false; }
   IntPtr previousContext=SetThreadDpiAwarenessContext(new IntPtr(-4));
   if(previousContext==IntPtr.Zero) { failure="Cannot map physical desktop coordinates."; Hide(surface); return false; }
   try {
    var origin=new POINT { X=(int)Math.Round(screenPixels.X),Y=(int)Math.Round(screenPixels.Y) };
    SetLastError(0);
    int delta=MapWindowPoints(IntPtr.Zero,surfaces[surface],ref origin,1);
    if(delta==0 && Marshal.GetLastWin32Error()!=0) { failure="Cannot map desktop coordinates."; Hide(surface); return false; }
    // HWND_TOP changes sibling order INSIDE DefView, above the icon ListView.
    // Ordinary application windows stay above the desktop child by construction.
    if(!SetWindowPos(surface.Handle,IntPtr.Zero,origin.X,origin.Y,(int)Math.Ceiling(screenPixels.Width),(int)Math.Ceiling(screenPixels.Height),SWP_NOACTIVATE|(show ? SWP_SHOWWINDOW : 0))) { failure="Cannot place desktop child."; Hide(surface); return false; }
    if(!show) Hide(surface);
    failure=null; return true;
   } finally { SetThreadDpiAwarenessContext(previousContext); }
  }

  public void DestroySurface(HwndSource surface) {
   if(surface==null) return;
   surfaces.Remove(surface);
   if(!surface.IsDisposed) { Hide(surface); try { surface.RootVisual=null; } catch(InvalidOperationException) { } finally { surface.Dispose(); } }
  }
  public void Dispose() {
   if(disposed) return;
   foreach(var surface in surfaces.Keys.ToArray()) DestroySurface(surface);
   disposed=true; desktopParent=IntPtr.Zero;
  }
  static void Hide(HwndSource surface) { if(surface!=null && !surface.IsDisposed && surface.Handle!=IntPtr.Zero && IsWindow(surface.Handle)) ShowWindow(surface.Handle,0); }
  static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value) && value>int.MinValue && value<int.MaxValue; }
  IntPtr FindHost() {
   if(disposed) return IntPtr.Zero;
   IntPtr shell=GetShellWindow(); uint shellProcess=0;
   if(shell!=IntPtr.Zero) GetWindowThreadProcessId(shell,out shellProcess);
   if(shellProcess==0) { desktopParent=IntPtr.Zero; return IntPtr.Zero; }
   if(IsDesktopView(desktopParent,shellProcess)) return desktopParent;
   desktopParent=IntPtr.Zero;
   EnumWindows(delegate(IntPtr top,IntPtr unused) {
    string topClass=WindowClass(top);
    if(topClass!="Progman" && topClass!="WorkerW") return true;
    IntPtr view=FindWindowEx(top,IntPtr.Zero,"SHELLDLL_DefView",null);
    if(IsDesktopView(view,shellProcess)) { desktopParent=view; return false; }
    return true;
   },IntPtr.Zero);
   return desktopParent;
  }
  static bool IsDesktopView(IntPtr view,uint shellProcess) {
   if(view==IntPtr.Zero || !IsWindow(view) || WindowClass(view)!="SHELLDLL_DefView") return false;
   uint process; GetWindowThreadProcessId(view,out process); if(process!=shellProcess) return false;
   string parentClass=WindowClass(GetParent(view));
   return (parentClass=="Progman" || parentClass=="WorkerW") && FindWindowEx(view,IntPtr.Zero,"SysListView32",null)!=IntPtr.Zero;
  }
  static string WindowClass(IntPtr window) { var name=new StringBuilder(128); return window!=IntPtr.Zero && GetClassName(window,name,name.Capacity)>0 ? name.ToString() : ""; }
  static IntPtr ClickThroughHook(IntPtr window,int message,IntPtr w,IntPtr l,ref bool handled) {
   if(message==0x84) { handled=true; return new IntPtr(-1); }
   return IntPtr.Zero;
  }

  delegate bool EnumCallback(IntPtr window,IntPtr parameter);
  [StructLayout(LayoutKind.Sequential)] struct POINT { public int X,Y; }
  [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] static extern bool EnumWindows(EnumCallback callback,IntPtr parameter);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string className,string title);
  [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] static extern bool IsWindow(IntPtr window);
  [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr window);
  [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder name,int count);
  [DllImport("user32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
  [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] static extern bool ShowWindow(IntPtr window,int command);
  [DllImport("user32.dll",SetLastError=true)] static extern int MapWindowPoints(IntPtr from,IntPtr to,ref POINT point,uint count);
  [DllImport("kernel32.dll")] static extern void SetLastError(uint error);
  [DllImport("user32.dll")] static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
  [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 }
}
