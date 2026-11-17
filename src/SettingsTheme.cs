using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;

namespace Nook {
 static class SettingsTheme {
  public static void Apply(Window window) {
   if(window==null) throw new ArgumentNullException("window");
   if(window.Resources.Contains("Nook.SettingsTheme.Applied")) return;
   var resources=(ResourceDictionary)XamlReader.Parse(StylesXml);
   window.Resources.MergedDictionaries.Add(resources);
   window.Resources["Nook.SettingsTheme.Applied"]=true;
   window.Background=(Brush)resources["SettingsWindowFallback"];
   window.Foreground=(Brush)resources["SettingsText"];
   window.FontFamily=new FontFamily("Segoe UI"); window.FontSize=13;
   window.UseLayoutRounding=true; window.SnapsToDevicePixels=true;
   TextOptions.SetTextFormattingMode(window,TextFormattingMode.Display);
   window.SourceInitialized+=delegate { SetTitlebar(window); };
   if(new WindowInteropHelper(window).Handle!=IntPtr.Zero) SetTitlebar(window);
  }
  static void SetTitlebar(Window window) {
   try {
    IntPtr hwnd=new WindowInteropHelper(window).Handle;
    if(hwnd!=IntPtr.Zero) {
     int dark=1,backdrop=3;
     DwmSetWindowAttribute(hwnd,20,ref dark,4);
     int result=DwmSetWindowAttribute(hwnd,38,ref backdrop,4);
     if(result>=0) window.Background=Brushes.Transparent;
    }
   } catch(DllNotFoundException) { } catch(EntryPointNotFoundException) { }
  }
  [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);

  // Templates keep the standard WPF controls, routed commands and PART_Track /
  // PART_SelectedContentHost contracts; no custom input handlers replace them.
  const string StylesXml=@"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
 <SolidColorBrush x:Key='SettingsWindowFallback' Color='#202020'/>
 <SolidColorBrush x:Key='SettingsBackground' Color='#CC202020'/>
 <SolidColorBrush x:Key='SettingsCardBackground' Color='#A62B2B2B'/>
 <SolidColorBrush x:Key='SettingsBorder' Color='#3FFFFFFF'/>
 <SolidColorBrush x:Key='SettingsText' Color='#F0F0F0'/>
 <SolidColorBrush x:Key='SettingsMuted' Color='#B0B0B0'/>
 <SolidColorBrush x:Key='SettingsAccent' Color='#D6D6D6'/>
 <SolidColorBrush x:Key='SettingsAccentSurface' Color='#484848'/>
 <SolidColorBrush x:Key='SettingsInputBackground' Color='#B5222222'/>
 <Style x:Key='SettingsCard' TargetType='{x:Type Border}'>
  <Setter Property='Background' Value='{StaticResource SettingsCardBackground}'/>
  <Setter Property='BorderBrush' Value='{StaticResource SettingsBorder}'/>
  <Setter Property='BorderThickness' Value='1'/><Setter Property='CornerRadius' Value='16'/>
  <Setter Property='Padding' Value='20'/><Setter Property='Margin' Value='0,0,0,14'/>
 </Style>
 <Style x:Key='SettingsTitle' TargetType='{x:Type TextBlock}'>
  <Setter Property='Foreground' Value='{StaticResource SettingsText}'/><Setter Property='FontSize' Value='26'/><Setter Property='FontWeight' Value='SemiBold'/>
 </Style>
 <Style x:Key='SettingsSectionTitle' TargetType='{x:Type TextBlock}'>
  <Setter Property='Foreground' Value='{StaticResource SettingsText}'/><Setter Property='FontSize' Value='16'/><Setter Property='FontWeight' Value='SemiBold'/><Setter Property='Margin' Value='0,0,0,8'/>
 </Style>
 <Style x:Key='SettingsHint' TargetType='{x:Type TextBlock}'>
  <Setter Property='Foreground' Value='{StaticResource SettingsMuted}'/><Setter Property='FontSize' Value='12'/><Setter Property='TextWrapping' Value='Wrap'/><Setter Property='LineHeight' Value='18'/>
 </Style>

 <Style TargetType='{x:Type Button}'>
  <Setter Property='Foreground' Value='{StaticResource SettingsText}'/><Setter Property='Background' Value='#383838'/><Setter Property='BorderBrush' Value='#686868'/>
  <Setter Property='BorderThickness' Value='1'/><Setter Property='Padding' Value='14,9'/><Setter Property='MinHeight' Value='36'/><Setter Property='Cursor' Value='Hand'/>
  <Setter Property='HorizontalContentAlignment' Value='Center'/><Setter Property='VerticalContentAlignment' Value='Center'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Button}'>
   <Border x:Name='Chrome' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='9' Padding='{TemplateBinding Padding}'>
    <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}' RecognizesAccessKey='True'/>
   </Border>
   <ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Chrome' Property='Background' Value='#484848'/><Setter TargetName='Chrome' Property='BorderBrush' Value='#A0A0A0'/></Trigger>
    <Trigger Property='IsDefaulted' Value='True'><Setter TargetName='Chrome' Property='BorderBrush' Value='{StaticResource SettingsAccent}'/></Trigger>
    <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Chrome' Property='BorderBrush' Value='{StaticResource SettingsAccent}'/></Trigger>
    <Trigger Property='IsPressed' Value='True'><Setter TargetName='Chrome' Property='Background' Value='#555555'/></Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter TargetName='Chrome' Property='Opacity' Value='.42'/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>

 <Style TargetType='{x:Type CheckBox}'>
  <Setter Property='Foreground' Value='{StaticResource SettingsText}'/><Setter Property='VerticalContentAlignment' Value='Center'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Padding' Value='0,7'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type CheckBox}'>
   <Border x:Name='ToggleRoot' Padding='{TemplateBinding Padding}' Background='Transparent'>
    <DockPanel LastChildFill='True'>
     <Border x:Name='Rail' DockPanel.Dock='Left' Width='36' Height='21' CornerRadius='10.5' Background='#393939' BorderBrush='#666666' BorderThickness='1' Margin='0,0,12,0'>
      <Ellipse x:Name='SwitchThumb' Width='13' Height='13' Fill='#D8E0ED' HorizontalAlignment='Left' VerticalAlignment='Center' Margin='3,0'/>
     </Border>
     <ContentPresenter VerticalAlignment='{TemplateBinding VerticalContentAlignment}' HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' RecognizesAccessKey='True'/>
    </DockPanel>
   </Border>
   <ControlTemplate.Triggers>
    <Trigger Property='IsChecked' Value='True'><Setter TargetName='Rail' Property='Background' Value='{StaticResource SettingsAccent}'/><Setter TargetName='Rail' Property='BorderBrush' Value='{StaticResource SettingsAccent}'/><Setter TargetName='SwitchThumb' Property='Fill' Value='#252525'/><Setter TargetName='SwitchThumb' Property='HorizontalAlignment' Value='Right'/></Trigger>
    <Trigger Property='IsChecked' Value='{x:Null}'><Setter TargetName='Rail' Property='Background' Value='{StaticResource SettingsAccentSurface}'/><Setter TargetName='SwitchThumb' Property='HorizontalAlignment' Value='Center'/></Trigger>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Rail' Property='BorderBrush' Value='#BDBDBD'/></Trigger>
    <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Rail' Property='BorderBrush' Value='White'/><Setter TargetName='Rail' Property='BorderThickness' Value='2'/></Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter TargetName='ToggleRoot' Property='Opacity' Value='.42'/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>

 <Style x:Key='SettingsTrackButton' TargetType='{x:Type RepeatButton}'>
  <Setter Property='Focusable' Value='False'/><Setter Property='IsTabStop' Value='False'/><Setter Property='Background' Value='#555555'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Background='Transparent'><Border x:Name='Bar' Background='{TemplateBinding Background}' Height='4' VerticalAlignment='Center' CornerRadius='2'/></Border><ControlTemplate.Triggers><DataTrigger Binding='{Binding Orientation,RelativeSource={RelativeSource AncestorType={x:Type Slider}}}' Value='Vertical'><Setter TargetName='Bar' Property='Height' Value='Auto'/><Setter TargetName='Bar' Property='Width' Value='4'/><Setter TargetName='Bar' Property='VerticalAlignment' Value='Stretch'/></DataTrigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style x:Key='SettingsSliderThumb' TargetType='{x:Type Thumb}'>
  <Setter Property='Width' Value='18'/><Setter Property='Height' Value='18'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Thumb}'><Ellipse x:Name='Dot' Fill='{StaticResource SettingsAccent}' Stroke='#F0F0F0' StrokeThickness='1'/><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Dot' Property='Fill' Value='#E7E7E7'/></Trigger><Trigger Property='IsDragging' Value='True'><Setter TargetName='Dot' Property='Fill' Value='White'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='{x:Type Slider}'>
  <Setter Property='Foreground' Value='{StaticResource SettingsAccent}'/><Setter Property='MinHeight' Value='30'/><Setter Property='IsMoveToPointEnabled' Value='False'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Slider}'>
   <Border x:Name='SliderRoot' Background='Transparent' Padding='2' BorderThickness='1' BorderBrush='Transparent' CornerRadius='8'>
    <Track x:Name='PART_Track' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{Binding Value,RelativeSource={RelativeSource TemplatedParent},Mode=TwoWay}' Orientation='{TemplateBinding Orientation}' IsDirectionReversed='{TemplateBinding IsDirectionReversed}'>
     <Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource SettingsTrackButton}' Background='{StaticResource SettingsAccent}' Command='{x:Static Slider.DecreaseLarge}'/></Track.DecreaseRepeatButton>
     <Track.Thumb><Thumb Style='{StaticResource SettingsSliderThumb}'/></Track.Thumb>
     <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource SettingsTrackButton}' Command='{x:Static Slider.IncreaseLarge}'/></Track.IncreaseRepeatButton>
    </Track>
   </Border>
   <ControlTemplate.Triggers><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='SliderRoot' Property='BorderBrush' Value='#A0A0A0'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='SliderRoot' Property='Opacity' Value='.4'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>

 <Style TargetType='{x:Type TextBox}'>
  <Setter Property='Background' Value='{StaticResource SettingsInputBackground}'/><Setter Property='Foreground' Value='{StaticResource SettingsText}'/><Setter Property='BorderBrush' Value='#707070'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Padding' Value='10,7'/><Setter Property='CaretBrush' Value='White'/><Setter Property='SelectionBrush' Value='#888888'/><Setter Property='SelectionOpacity' Value='.7'/><Setter Property='MinHeight' Value='34'/>
  <Style.Triggers><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter Property='BorderBrush' Value='{StaticResource SettingsAccent}'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.45'/></Trigger></Style.Triggers>
 </Style>

 <Style TargetType='{x:Type TabItem}'>
  <Setter Property='Foreground' Value='{StaticResource SettingsMuted}'/><Setter Property='Background' Value='Transparent'/><Setter Property='BorderBrush' Value='Transparent'/><Setter Property='Padding' Value='15,10'/><Setter Property='Margin' Value='3'/><Setter Property='Cursor' Value='Hand'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type TabItem}'>
   <Border x:Name='Pill' CornerRadius='9' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' Padding='{TemplateBinding Padding}'><ContentPresenter ContentSource='Header' RecognizesAccessKey='True' HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>
   <ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Pill' Property='Background' Value='#353535'/></Trigger>
    <Trigger Property='IsSelected' Value='True'><Setter TargetName='Pill' Property='Background' Value='{StaticResource SettingsAccentSurface}'/><Setter TargetName='Pill' Property='BorderBrush' Value='#858585'/><Setter Property='Foreground' Value='{StaticResource SettingsText}'/></Trigger>
    <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Pill' Property='BorderBrush' Value='{StaticResource SettingsAccent}'/></Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter TargetName='Pill' Property='Opacity' Value='.4'/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='{x:Type TabControl}'>
  <Setter Property='Background' Value='Transparent'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Foreground' Value='{StaticResource SettingsText}'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type TabControl}'>
   <Grid KeyboardNavigation.TabNavigation='Local'>
    <Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='*'/></Grid.RowDefinitions>
    <Border Grid.Row='0' Background='#60242424' CornerRadius='12' Padding='3' BorderBrush='#36FFFFFF' BorderThickness='1' Margin='0,0,0,18'>
     <TabPanel x:Name='HeaderPanel' IsItemsHost='True' KeyboardNavigation.TabIndex='1'/>
    </Border>
    <ContentPresenter Grid.Row='1' x:Name='PART_SelectedContentHost' ContentSource='SelectedContent' Margin='{TemplateBinding Padding}' KeyboardNavigation.TabIndex='2'/>
   </Grid>
  </ControlTemplate></Setter.Value></Setter>
 </Style>

 <Style x:Key='SettingsScrollPage' TargetType='{x:Type RepeatButton}'>
  <Setter Property='Focusable' Value='False'/><Setter Property='IsTabStop' Value='False'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Background='Transparent'/></ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style x:Key='SettingsScrollThumb' TargetType='{x:Type Thumb}'>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Thumb}'><Border x:Name='Grip' Margin='2' Background='#555555' CornerRadius='4'/><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Grip' Property='Background' Value='#888888'/></Trigger><Trigger Property='IsDragging' Value='True'><Setter TargetName='Grip' Property='Background' Value='#B0B0B0'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='{x:Type ScrollBar}'>
  <Setter Property='Width' Value='12'/><Setter Property='MinWidth' Value='12'/><Setter Property='Background' Value='Transparent'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type ScrollBar}'>
   <Grid Background='{TemplateBinding Background}'>
    <Track x:Name='PART_Track' Orientation='{TemplateBinding Orientation}' IsDirectionReversed='True' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' ViewportSize='{TemplateBinding ViewportSize}' Value='{Binding Value,RelativeSource={RelativeSource TemplatedParent},Mode=TwoWay}'>
     <Track.DecreaseRepeatButton><RepeatButton x:Name='PageBefore' Style='{StaticResource SettingsScrollPage}' Command='{x:Static ScrollBar.PageUpCommand}'/></Track.DecreaseRepeatButton>
     <Track.Thumb><Thumb Style='{StaticResource SettingsScrollThumb}' MinHeight='18' MinWidth='8'/></Track.Thumb>
     <Track.IncreaseRepeatButton><RepeatButton x:Name='PageAfter' Style='{StaticResource SettingsScrollPage}' Command='{x:Static ScrollBar.PageDownCommand}'/></Track.IncreaseRepeatButton>
    </Track>
   </Grid>
   <ControlTemplate.Triggers><Trigger Property='Orientation' Value='Horizontal'><Setter TargetName='PART_Track' Property='IsDirectionReversed' Value='False'/><Setter TargetName='PageBefore' Property='Command' Value='{x:Static ScrollBar.PageLeftCommand}'/><Setter TargetName='PageAfter' Property='Command' Value='{x:Static ScrollBar.PageRightCommand}'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
  <Style.Triggers><Trigger Property='Orientation' Value='Horizontal'><Setter Property='Width' Value='Auto'/><Setter Property='MinWidth' Value='0'/><Setter Property='Height' Value='12'/><Setter Property='MinHeight' Value='12'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.35'/></Trigger></Style.Triggers>
 </Style>
</ResourceDictionary>";
 }
}
