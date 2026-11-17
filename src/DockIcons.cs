using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nook {
 partial class DockWindow {
  void AddIconImage(Grid panel,AppItem item,double size) {
   ImageSource icon;
   if(!icons.TryGetValue(item.IconKey,out icon)) {
    icon=GetIcon(item);
    if(icon!=null) icons[item.IconKey]=icon;
   }
   if(icon!=null) panel.Children.Add(new Image { Source=icon,Width=size,Height=size,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center });
   else panel.Children.Add(new TextBlock { Text=string.IsNullOrEmpty(item.Title) ? "?" : item.Title.Substring(0,1).ToUpperInvariant(),FontSize=prefs.Size*.6,TextAlignment=TextAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center });
  }

  ImageSource GetIcon(AppItem item) {
   ImageSource source;
   if(icons.TryGetValue(item.IconKey,out source)) return source;
   if(!string.IsNullOrEmpty(item.IconPath) && File.Exists(item.IconPath)) {
    try {
     string ext=Path.GetExtension(item.IconPath).ToLowerInvariant();
     if(ext==".png" || ext==".ico" || ext==".jpg" || ext==".jpeg" || ext==".bmp") {
      var bitmap=new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption=BitmapCacheOption.OnLoad; bitmap.UriSource=new Uri(Path.GetFullPath(item.IconPath)); bitmap.DecodePixelWidth=64; bitmap.EndInit(); bitmap.Freeze(); icons[item.IconKey]=bitmap; return bitmap;
     }
    } catch { }
   }
   foreach(string candidate in new [] { item.IconPath,item.Path }.Where(x=>!string.IsNullOrEmpty(x) && File.Exists(x))) {
    IntPtr nativeIcon=Native.FileIcon(candidate);
    if(nativeIcon!=IntPtr.Zero) {
     try { source=Imaging.CreateBitmapSourceFromHIcon(nativeIcon,Int32Rect.Empty,BitmapSizeOptions.FromWidthAndHeight(64,64)); source.Freeze(); icons[item.IconKey]=source; return source; }
     catch { } finally { Native.DestroyIcon(nativeIcon); }
    }
   }
   try {
    if(!string.IsNullOrEmpty(item.Path) && File.Exists(item.Path)) using(var drawingIcon=System.Drawing.Icon.ExtractAssociatedIcon(item.Path)) {
     if(drawingIcon!=null) { source=Imaging.CreateBitmapSourceFromHIcon(drawingIcon.Handle,Int32Rect.Empty,BitmapSizeOptions.FromWidthAndHeight(64,64)); source.Freeze(); icons[item.IconKey]=source; return source; }
    }
   } catch { }
   return null;
  }
 }
}
