using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Nook {
 partial class DockWindow {
  const int WebSearchHotkeyId=0x5101;
  SearchWindow webSearchWindow;
  bool webSearchRegistered;

  void ShowWebSearch() {
   if(closing || scenarioEditorOpen) return;
   if(webSearchWindow==null) webSearchWindow=new SearchWindow(prefs,OpenWebResult,prefs.WebSearch);
   if(webSearchWindow.IsVisible) webSearchWindow.Hide();
   else webSearchWindow.Open(null);
  }

  void OpenWebResult(SearchApp app) {
   var options=prefs.WebSearch;
   string browser=options.Browser=="Windows" ? null : string.IsNullOrEmpty(options.Browser) ? WebSearch.ChromePath() : options.Browser;
   if(!File.Exists(browser)) { Launch(app.Target); return; }
   string arguments=QuoteArgument(app.Target);
   if(Path.GetFileName(browser).Equals("chrome.exe",StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(options.ChromeProfile))
    arguments=QuoteArgument("--profile-directory="+options.ChromeProfile.Trim())+" "+arguments;
   Launch(browser,arguments);
  }

  void BuildWebSearchSettings(StackPanel page) {
   var web=prefs.WebSearch;
   var card=SettingsCard(page,"Веб-поиск","Ctrl + Alt + Пробел");
   if(!webSearchRegistered) card.Children.Add(SettingHint("Горячая клавиша занята. Освободите сочетание и перезапустите Nook.",new Thickness(0,0,0,8)));
   var engine=new ComboBox { ItemsSource=new[] { "Google","Яндекс" },SelectedIndex=web.Engine=="Яндекс" ? 1 : 0,Margin=new Thickness(0,0,0,8) };
   card.Children.Add(new TextBlock { Text="Поисковик" }); card.Children.Add(engine);
   engine.SelectionChanged+=delegate { web.Engine=(string)engine.SelectedItem; Save(); };
   var browserLabel=new TextBlock { TextWrapping=TextWrapping.Wrap };
   Action showBrowser=delegate { browserLabel.Text="Браузер: "+(web.Browser=="Windows" ? "по умолчанию в Windows" : string.IsNullOrEmpty(web.Browser) ? "Google Chrome" : web.Browser); };
   showBrowser(); card.Children.Add(browserLabel);
   var browsers=new WrapPanel(); card.Children.Add(browsers);
   browsers.Children.Add(ScenarioButton("Google Chrome",delegate { web.Browser=""; showBrowser(); Save(); }));
   browsers.Children.Add(ScenarioButton("Браузер Windows",delegate { web.Browser="Windows"; showBrowser(); Save(); }));
   browsers.Children.Add(ScenarioButton("Выбрать браузер…",delegate {
    var dialog=new OpenFileDialog { Filter="Программы (*.exe)|*.exe", CheckFileExists=true };
    if(dialog.ShowDialog(settings)==true) { web.Browser=dialog.FileName; showBrowser(); Save(); }
   }));
   card.Children.Add(SettingHint("Если выбранный браузер недоступен, используется браузер Windows по умолчанию.",new Thickness(0,0,0,8)));
   card.Children.Add(new TextBlock { Text="Папка профиля Chrome, например Default или Profile 1" });
   var profile=new TextBox { Text=web.ChromeProfile,MaxLength=80,Margin=new Thickness(0,0,0,8) }; card.Children.Add(profile);
   profile.LostKeyboardFocus+=delegate { web.ChromeProfile=profile.Text.Trim(); Save(); };
   SettingsToggle(card,"Подсказки при наборе",web.Suggestions,delegate(bool value) { web.Suggestions=value; Save(); });
   card.Children.Add(SettingHint("При включённых подсказках набираемый запрос отправляется выбранному поисковику.",new Thickness(0,0,0,8)));
   card.Children.Add(new TextBlock { Text="Цвет фона (#RRGGBB)" });
   var color=new TextBox { Text=web.Color,MaxLength=7,Margin=new Thickness(0,0,0,8) }; card.Children.Add(color);
   color.LostKeyboardFocus+=delegate {
    uint rgb;
    if(color.Text.Length==7 && color.Text[0]=='#' && uint.TryParse(color.Text.Substring(1),System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture,out rgb)) { web.Color=color.Text.ToUpperInvariant(); Save(); }
    color.Text=web.Color;
   };
   SettingsSlider(card,"Непрозрачность",20,100,web.Opacity*100,1,"%",delegate(double v) { web.Opacity=v/100; });
   SettingsSlider(card,"Скругление",0,40,web.Radius,1,"",delegate(double v) { web.Radius=v; });
   SettingsSlider(card,"Ширина окна",280,900,web.Width,10,"px",delegate(double v) { web.Width=v; });
   SettingsSlider(card,"Размер текста",14,32,web.FontSize,1,"",delegate(double v) { web.FontSize=v; });
   SettingsSlider(card,"По горизонтали",0,100,web.X*100,1,"%",delegate(double v) { web.X=v/100; });
   SettingsSlider(card,"По вертикали",0,100,web.Y*100,1,"%",delegate(double v) { web.Y=v/100; });
   card.Children.Add(ScenarioButton("Открыть веб-поиск",ShowWebSearch));
  }
 }
}
