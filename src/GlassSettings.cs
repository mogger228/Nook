using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Nook {
 partial class DockWindow {
  readonly int[] customGlassColors=Enumerable.Repeat(0xFFFFFF,16).ToArray();
  void AddGlassColorSettings(StackPanel panel) {
   panel.Children.Add(new TextBlock { Text="Цвет стекла",Margin=new Thickness(0,0,0,8) });
   var controls=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,16) };
   var preview=new Border { Width=26,Height=26,CornerRadius=new CornerRadius(7),BorderBrush=new SolidColorBrush(Color.FromArgb(95,255,255,255)),BorderThickness=new Thickness(1),Margin=new Thickness(0,0,10,0) };
   var value=new TextBox { Text=Preferences.NormalizeGlassColor(prefs.GlassColor),Width=92,VerticalContentAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,0),Padding=new Thickness(6,3,6,3),Foreground=Brushes.White,Background=new SolidColorBrush(Color.FromRgb(40,45,56)),MaxLength=7 };
   AutomationProperties.SetName(value,"Цвет стекла, HEX");
   var choose=new Button { Content="Выбрать цвет…",Padding=new Thickness(10,4,10,4) };
   Action updatePreview=delegate { preview.Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString(Preferences.NormalizeGlassColor(prefs.GlassColor))); };
   Action commit=delegate {
    string normalized=Preferences.NormalizeGlassColor(value.Text);
    if(!string.Equals(normalized,value.Text,StringComparison.OrdinalIgnoreCase)) { value.Text=Preferences.NormalizeGlassColor(prefs.GlassColor); return; }
    prefs.GlassColor=normalized; value.Text=normalized; updatePreview(); ApplyGlassAppearance(); Save();
   };
   value.LostKeyboardFocus+=delegate { commit(); };
   value.KeyDown+=delegate(object sender,System.Windows.Input.KeyEventArgs e) { if(e.Key==System.Windows.Input.Key.Enter) { commit(); e.Handled=true; } };
   choose.Click+=delegate {
    Color selected=(Color)ColorConverter.ConvertFromString(Preferences.NormalizeGlassColor(prefs.GlassColor));
    if(TryChooseGlassColor(selected,out selected)) { value.Text=string.Format("#{0:X2}{1:X2}{2:X2}",selected.R,selected.G,selected.B); commit(); }
   };
   controls.Children.Add(preview); controls.Children.Add(value); controls.Children.Add(choose); panel.Children.Add(controls); updatePreview();
  }
  bool TryChooseGlassColor(Color initial,out Color selected) {
   selected=initial;
   IntPtr custom=Marshal.AllocHGlobal(16*sizeof(int));
   try {
    Marshal.Copy(customGlassColors,0,custom,16);
    var dialog=new CHOOSECOLOR { Size=(uint)Marshal.SizeOf(typeof(CHOOSECOLOR)),Owner=new WindowInteropHelper(settings).Handle,Result=(uint)(initial.R | initial.G<<8 | initial.B<<16),CustomColors=custom,Flags=3 };
    if(!ChooseColor(ref dialog)) return false;
    Marshal.Copy(custom,customGlassColors,0,16);
    selected=Color.FromRgb((byte)(dialog.Result&255),(byte)((dialog.Result>>8)&255),(byte)((dialog.Result>>16)&255));
    return true;
   } finally { Marshal.FreeHGlobal(custom); }
  }
  [StructLayout(LayoutKind.Sequential)] struct CHOOSECOLOR {
   public uint Size; public IntPtr Owner,Instance; public uint Result; public IntPtr CustomColors; public uint Flags; public IntPtr CustomData,Hook,Template;
  }
  [DllImport("comdlg32.dll",CharSet=CharSet.Unicode,EntryPoint="ChooseColorW")] [return:MarshalAs(UnmanagedType.Bool)] static extern bool ChooseColor(ref CHOOSECOLOR color);
 }
}
