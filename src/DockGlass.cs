using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Nook {
 partial class DockWindow {
  const double GlassShadowInset=10;
  Border glassShadow;
  Border mainEdgeHighlight,leftEdgeHighlight,rightEdgeHighlight;

  // The shadow is generated from a thin separate rim: applying the effect to
  // island itself would also process every icon and label. No background blur here.
  void InitializeGlassDesign(Grid surface) {
   if(glassShadow==null) {
    glassShadow=CreateGlassShadow(island);
    surface.Children.Insert(0,glassShadow);
    mainEdgeHighlight=CreateEdgeHighlight(island);
    surface.Children.Add(mainEdgeHighlight);
    Grid.SetColumn(mainEdgeHighlight,2);
   }
   ApplyGlassAppearance();
  }

  internal static Border CreateGlassShadow(Border target) {
   var shadow=new DropShadowEffect { Color=Colors.Black,BlurRadius=14,ShadowDepth=1.5,Direction=270,Opacity=.20,RenderingBias=RenderingBias.Performance };
   shadow.Freeze();
   var border=new Border { IsHitTestVisible=false,Focusable=false,Background=Brushes.Transparent,BorderBrush=new SolidColorBrush(Color.FromArgb(130,12,17,24)),BorderThickness=new Thickness(1),VerticalAlignment=target.VerticalAlignment,HorizontalAlignment=target.HorizontalAlignment,Effect=shadow };
   border.SetBinding(FrameworkElement.WidthProperty,new Binding("ActualWidth") { Source=target });
   border.SetBinding(FrameworkElement.HeightProperty,new Binding("ActualHeight") { Source=target });
   border.SetBinding(Border.CornerRadiusProperty,new Binding("CornerRadius") { Source=target });
   border.SetBinding(FrameworkElement.MarginProperty,new Binding("Margin") { Source=target });
   border.SetBinding(FrameworkElement.VerticalAlignmentProperty,new Binding("VerticalAlignment") { Source=target });
   border.SetBinding(FrameworkElement.HorizontalAlignmentProperty,new Binding("HorizontalAlignment") { Source=target });
   border.SetBinding(UIElement.VisibilityProperty,new Binding("Visibility") { Source=target });
   return border;
  }

  void ApplyGlassAppearance() {
   ApplyGlassAppearance(island,prefs);
   if(mainEdgeHighlight!=null) mainEdgeHighlight.Visibility=prefs.EdgeHighlight ? Visibility.Visible : Visibility.Collapsed;
   if(leftEdgeHighlight!=null) leftEdgeHighlight.Visibility=prefs.EdgeHighlight && prefs.LeftClockIslandEnabled ? Visibility.Visible : Visibility.Collapsed;
   if(rightEdgeHighlight!=null) rightEdgeHighlight.Visibility=prefs.EdgeHighlight && prefs.RightStatusIslandEnabled && HasRightIslandContent ? Visibility.Visible : Visibility.Collapsed;
   UpdateSideIslandAppearance();
  }

  static Border CreateEdgeHighlight(Border target) {
   var brush=new LinearGradientBrush { StartPoint=new Point(0,0),EndPoint=new Point(1,0) };
   brush.GradientStops.Add(new GradientStop(Color.FromArgb(18,255,255,255),0));
   brush.GradientStops.Add(new GradientStop(Color.FromArgb(135,255,255,255),.5));
   brush.GradientStops.Add(new GradientStop(Color.FromArgb(18,255,255,255),1)); brush.Freeze();
   var line=new Border { Height=1,VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Stretch,CornerRadius=new CornerRadius(4,4,0,0),Background=brush,IsHitTestVisible=false,Focusable=false };
   line.SetBinding(FrameworkElement.WidthProperty,new Binding("ActualWidth") { Source=target });
   line.SetBinding(FrameworkElement.MarginProperty,new Binding("Margin") { Source=target });
   line.SetBinding(FrameworkElement.VerticalAlignmentProperty,new Binding("VerticalAlignment") { Source=target });
   return line;
  }

  internal static void ApplyGlassAppearance(Border target,Preferences prefs) {
   target.CornerRadius=new CornerRadius(Preferences.Clamp(prefs.CornerRadius,0,40,18));
   Color tint=Color.FromRgb(48,56,70);
   try { if(!string.IsNullOrWhiteSpace(prefs.GlassColor)) tint=(Color)ColorConverter.ConvertFromString(prefs.GlassColor); }
   catch(FormatException) { } catch(NotSupportedException) { } catch(ArgumentException) { }
   double opacity=prefs.BackgroundOpacity;
   if(double.IsNaN(opacity)||double.IsInfinity(opacity)) opacity=.55;
   opacity=Math.Max(0,Math.Min(1,opacity));
   byte alpha=(byte)Math.Round(opacity*255);
   var background=new LinearGradientBrush { StartPoint=new Point(0,0),EndPoint=new Point(0,1) };
   background.GradientStops.Add(new GradientStop(GlassTone(tint,1.08,4,alpha),0));
   background.GradientStops.Add(new GradientStop(GlassTone(tint,1.01,1,alpha),.22));
   background.GradientStops.Add(new GradientStop(GlassTone(tint,1,0,alpha),.62));
   background.GradientStops.Add(new GradientStop(GlassTone(tint,.94,0,alpha),1));
   background.Freeze();
   target.Background=background;
   var rim=new LinearGradientBrush { StartPoint=new Point(0,0),EndPoint=new Point(0,1) };
   rim.GradientStops.Add(new GradientStop(Color.FromArgb(78,243,248,255),0));
   rim.GradientStops.Add(new GradientStop(Color.FromArgb(31,219,229,244),.38));
   rim.GradientStops.Add(new GradientStop(Color.FromArgb(16,204,218,238),.65));
   rim.GradientStops.Add(new GradientStop(Color.FromArgb(38,224,234,249),1));
   rim.Freeze();
   target.BorderBrush=rim;
   target.BorderThickness=new Thickness(1);
  }

  static Color GlassTone(Color tint,double factor,int lift,byte alpha) {
   return Color.FromArgb(alpha,(byte)Math.Max(0,Math.Min(255,Math.Round(tint.R*factor+lift))),(byte)Math.Max(0,Math.Min(255,Math.Round(tint.G*factor+lift))),(byte)Math.Max(0,Math.Min(255,Math.Round(tint.B*factor+lift))));
  }

  ImageSource CreateGlassStartIcon(double size,bool dark) {
   if(double.IsNaN(size)||double.IsInfinity(size)||size<=0) size=22;
   var drawing=new DrawingGroup();
   double gap=size*.105,pane=(size-gap)/2,radius=size*.035;
   for(int row=0;row<2;row++) for(int column=0;column<2;column++) {
    double x=column*(pane+gap),y=row*(pane+gap);
    var fill=new LinearGradientBrush { StartPoint=new Point(0,0),EndPoint=new Point(0,1) };
    fill.GradientStops.Add(new GradientStop(dark ? Color.FromArgb(255,91,96,104) : Color.FromArgb(253,255,255,255),0));
    fill.GradientStops.Add(new GradientStop(dark ? Color.FromArgb(255,67,72,80) : Color.FromArgb(237,243,249,255),.42));
    fill.GradientStops.Add(new GradientStop(dark ? Color.FromArgb(255,48,52,58) : Color.FromArgb(215,222,233,248),1));
    var outline=new Pen(new SolidColorBrush(dark ? Color.FromArgb(130,35,39,45) : Color.FromArgb(160,250,253,255)),Math.Max(.45,size*.023));
    var geometry=new RectangleGeometry(new Rect(x,y,pane,pane),radius,radius);
    drawing.Children.Add(new GeometryDrawing(fill,outline,geometry));
    var highlight=new Pen(new SolidColorBrush(dark ? Color.FromArgb(80,220,225,235) : Color.FromArgb(210,255,255,255)),Math.Max(.45,size*.025));
    drawing.Children.Add(new GeometryDrawing(null,highlight,new LineGeometry(new Point(x+radius*1.8,y+size*.045),new Point(x+pane-radius*1.8,y+size*.045))));
   }
   drawing.Freeze();
   var image=new DrawingImage(drawing); image.Freeze(); return image;
  }
 }
}
