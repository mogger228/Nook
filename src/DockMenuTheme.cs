using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace Nook {
 internal static class DockMenuTheme {
  // Shared templates are parsed once. No blur, shader effects or animation loop.
  static ResourceDictionary resources;
  internal static ContextMenu Create(Preferences preferences) {
   if(resources==null) resources=(ResourceDictionary)XamlReader.Parse(Theme);
   var menu=new ContextMenu { Style=(Style)resources[typeof(ContextMenu)] };
   menu.Resources[typeof(MenuItem)]=resources[typeof(MenuItem)];
   menu.Resources[typeof(Separator)]=resources[typeof(Separator)];
   menu.Opened+=delegate {
    Color tint=(Color)ColorConverter.ConvertFromString(Preferences.NormalizeGlassColor(preferences.GlassColor));
    byte alpha=(byte)Math.Round(255*Math.Max(.80,Math.Min(.94,preferences.BackgroundOpacity)));
    var background=new SolidColorBrush(Color.FromArgb(alpha,tint.R,tint.G,tint.B)); background.Freeze(); menu.Background=background;
    menu.MaxHeight=Math.Max(120,SystemParameters.WorkArea.Height-32);
   };
   return menu;
  }
  const string Theme=@"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
 <Style TargetType='{x:Type ContextMenu}'>
  <Setter Property='OverridesDefaultStyle' Value='True'/>
  <Setter Property='Background' Value='#E61A1A1A'/><Setter Property='Foreground' Value='#EFF2F7'/>
  <Setter Property='BorderBrush' Value='#40FFFFFF'/><Setter Property='BorderThickness' Value='1'/>
  <Setter Property='FontFamily' Value='Segoe UI'/><Setter Property='FontSize' Value='12'/>
  <Setter Property='MinWidth' Value='210'/><Setter Property='MaxWidth' Value='410'/>
  <Setter Property='Padding' Value='5'/><Setter Property='SnapsToDevicePixels' Value='True'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type ContextMenu}'>
   <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='12' Padding='{TemplateBinding Padding}'>
    <ScrollViewer CanContentScroll='True' VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled'>
     <ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle' KeyboardNavigation.TabNavigation='Cycle'/>
    </ScrollViewer>
   </Border>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='{x:Type MenuItem}'>
  <Setter Property='OverridesDefaultStyle' Value='True'/>
  <Setter Property='Foreground' Value='#EFF2F7'/><Setter Property='Background' Value='Transparent'/>
  <Setter Property='Padding' Value='11,8'/><Setter Property='Margin' Value='0,1'/>
  <Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='Cursor' Value='Hand'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type MenuItem}'>
   <Border x:Name='Row' CornerRadius='7' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'>
    <TextBlock Text='{TemplateBinding Header}' Foreground='{TemplateBinding Foreground}' TextTrimming='CharacterEllipsis' VerticalAlignment='Center'/>
   </Border>
   <ControlTemplate.Triggers>
    <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Row' Property='Background' Value='#20FFFFFF'/></Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter TargetName='Row' Property='Opacity' Value='.42'/></Trigger>
    <MultiTrigger><MultiTrigger.Conditions><Condition Property='Tag' Value='danger'/><Condition Property='IsHighlighted' Value='True'/></MultiTrigger.Conditions>
     <Setter TargetName='Row' Property='Background' Value='#30EF7180'/>
    </MultiTrigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
  <Style.Triggers><Trigger Property='Tag' Value='danger'><Setter Property='Foreground' Value='#FFA6AE'/></Trigger></Style.Triggers>
 </Style>
 <Style TargetType='{x:Type Separator}'>
  <Setter Property='Margin' Value='10,5'/><Setter Property='Height' Value='1'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Separator}'><Border Background='#26FFFFFF'/></ControlTemplate></Setter.Value></Setter>
 </Style>
</ResourceDictionary>";
 }
}
