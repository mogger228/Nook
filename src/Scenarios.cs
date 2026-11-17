using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;
using System.Threading.Tasks;

namespace Nook {
 public sealed class ScenarioDefinition {
  public string Id=Guid.NewGuid().ToString("D");
  public string Name;
  public bool Enabled=true;
  public uint HotkeyModifiers,HotkeyKey;
  public List<ScenarioAction> Actions=new List<ScenarioAction>();
 }

 public sealed class ScenarioAction {
  public string Kind;
  public string Target;
  public string ProjectPath;
  public List<string> Urls=new List<string>();
 }

 public static class ScenarioLauncher {
  // Hotkey ownership, conflicts and persistence belong to the application.
  public static List<string> Validate(ScenarioDefinition scenario) {
   return ValidateCore(scenario,Environment.CurrentDirectory);
  }
  static List<string> ValidateCore(ScenarioDefinition scenario,string baseDirectory) {
   var errors=new List<string>();
   if(scenario==null) { errors.Add("Сценарий не выбран."); return errors; }
   if(string.IsNullOrWhiteSpace(scenario.Name)) errors.Add("Укажите название сценария.");
   if(scenario.Actions==null || scenario.Actions.Count==0) { errors.Add("Добавьте хотя бы одно действие."); return errors; }
   for(int i=0;i<scenario.Actions.Count;i++) {
    var action=scenario.Actions[i]; string prefix="Действие "+(i+1)+": ";
    if(action==null) { errors.Add(prefix+"не задано."); continue; }
    if(IsKind(action,"Application") || IsKind(action,"Project")) {
     if(!PathExists(action.Target,baseDirectory,false)) errors.Add(prefix+"файл программы или ярлыка не найден.");
     if(IsKind(action,"Project") && !PathExists(action.ProjectPath,baseDirectory,true)) errors.Add(prefix+"файл или папка проекта не найдены.");
    } else if(IsKind(action,"Web")) {
     if(!string.IsNullOrWhiteSpace(action.Target) && !PathExists(action.Target,baseDirectory,false)) errors.Add(prefix+"файл выбранного браузера не найден.");
     if(action.Urls==null || action.Urls.Count==0) errors.Add(prefix+"добавьте хотя бы один адрес сайта.");
     else for(int j=0;j<action.Urls.Count;j++) { Uri address; if(!TryWebUrl(action.Urls[j],out address)) errors.Add(prefix+"адрес "+(j+1)+" должен быть полным URL с http:// или https://."); }
    } else errors.Add(prefix+"неизвестный тип действия.");
   }
   return errors;
  }

  public static List<ProcessStartInfo> BuildLaunches(ScenarioDefinition scenario) {
   var snapshot=Snapshot(scenario);
   string baseDirectory=Environment.CurrentDirectory;
   var errors=ValidateCore(snapshot,baseDirectory);
   if(errors.Count!=0) throw new ArgumentException(string.Join(Environment.NewLine,errors),"scenario");
   return BuildValidatedLaunches(snapshot,baseDirectory);
  }

  static List<ProcessStartInfo> BuildValidatedLaunches(ScenarioDefinition scenario,string baseDirectory) {
   var launches=new List<ProcessStartInfo>();
   foreach(var action in scenario.Actions) {
    if(IsKind(action,"Application")) launches.Add(StartInfo(FullPath(action.Target,baseDirectory),""));
    else if(IsKind(action,"Project")) launches.Add(StartInfo(FullPath(action.Target,baseDirectory),QuoteArgument(FullPath(action.ProjectPath,baseDirectory))));
    else {
     var urls=new List<string>();
     foreach(string text in action.Urls) { Uri address; TryWebUrl(text,out address); urls.Add(address.AbsoluteUri); }
     if(string.IsNullOrWhiteSpace(action.Target)) foreach(string url in urls) launches.Add(StartInfo(url,""));
     else {
      var arguments=new StringBuilder();
      foreach(string url in urls) { if(arguments.Length!=0) arguments.Append(' '); arguments.Append(QuoteArgument(url)); }
      launches.Add(StartInfo(FullPath(action.Target,baseDirectory),arguments.ToString()));
     }
    }
   }
   return launches;
  }

  static ProcessStartInfo StartInfo(string target,string arguments) { return new ProcessStartInfo(target,arguments) { UseShellExecute=true }; }

  public static async Task<List<string>> RunAsync(ScenarioDefinition scenario) {
   // Capture only DTO data and the path base on the caller before the first await.
   // Slow/network path checks and planning run off UI; they read only this copy.
   var snapshot=Snapshot(scenario);
   string baseDirectory=Environment.CurrentDirectory;
   return await Task.Run(delegate {
    var errors=ValidateCore(snapshot,baseDirectory);
    if(snapshot!=null && !snapshot.Enabled) errors.Add("Сценарий отключён.");
    if(errors.Count!=0) return errors;
    var launches=BuildValidatedLaunches(snapshot,baseDirectory);
    var failures=new List<string>();
    foreach(var launch in launches) {
     try {
      // Shell activation can return null when it reuses an existing application.
      // Disposing the Process object releases our handle; it does not close the app.
      using(var process=Process.Start(launch)) { }
     } catch(Exception error) {
      if(!ExpectedLaunchFailure(error)) throw;
      failures.Add("Не удалось открыть «"+launch.FileName+"»: "+error.Message);
     }
    }
    return failures;
   }).ConfigureAwait(false);
  }

  static bool ExpectedLaunchFailure(Exception error) {
   return error is Win32Exception || error is InvalidOperationException || error is IOException || error is UnauthorizedAccessException || error is SecurityException || error is NotSupportedException || error is ArgumentException;
  }

  public static string DescribeAction(ScenarioAction action) {
   if(action==null) return "Действие не задано";
   if(IsKind(action,"Application")) return "Программа: "+(action.Target??"");
   if(IsKind(action,"Project")) return "Проект: "+(action.ProjectPath??"")+" · "+(action.Target??"");
   if(IsKind(action,"Web")) return "Сайты: "+(action.Urls==null ? 0 : action.Urls.Count)+" · "+(string.IsNullOrWhiteSpace(action.Target) ? "браузер по умолчанию" : action.Target);
   return "Неизвестное действие";
  }

  // Windows CommandLineToArgvW/CRT quoting: n trailing slashes become 2n before
  // the closing quote; slashes preceding an embedded quote become 2n+1.
  public static string QuoteArgument(string argument) {
   if(argument==null) throw new ArgumentNullException("argument");
   var result=new StringBuilder("\""); int slashes=0;
   foreach(char character in argument) {
    if(character=='\\') { slashes++; continue; }
    result.Append('\\',character=='"' ? slashes*2+1 : slashes); result.Append(character); slashes=0;
   }
   result.Append('\\',slashes*2); result.Append('"'); return result.ToString();
  }

  static bool IsKind(ScenarioAction action,string kind) { return string.Equals(action.Kind,kind,StringComparison.OrdinalIgnoreCase); }
  static string FullPath(string path,string baseDirectory) { string value=path.Trim(); return Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(baseDirectory,value)); }
  static bool PathExists(string path,string baseDirectory,bool allowDirectory) {
   if(string.IsNullOrWhiteSpace(path)) return false;
   try { string full=FullPath(path,baseDirectory); return File.Exists(full) || (allowDirectory && Directory.Exists(full)); }
   catch(ArgumentException) { return false; } catch(NotSupportedException) { return false; } catch(PathTooLongException) { return false; } catch(SecurityException) { return false; }
  }
  static bool TryWebUrl(string text,out Uri address) {
   address=null;
   if(string.IsNullOrWhiteSpace(text) || !Uri.TryCreate(text.Trim(),UriKind.Absolute,out address)) return false;
   return (address.Scheme==Uri.UriSchemeHttp || address.Scheme==Uri.UriSchemeHttps) && !string.IsNullOrEmpty(address.Host);
  }
  static ScenarioDefinition Snapshot(ScenarioDefinition scenario) {
   if(scenario==null) return null;
   var result=new ScenarioDefinition { Id=scenario.Id,Name=scenario.Name,Enabled=scenario.Enabled,HotkeyModifiers=scenario.HotkeyModifiers,HotkeyKey=scenario.HotkeyKey };
   if(scenario.Actions==null) { result.Actions=null; return result; }
   foreach(var action in scenario.Actions) result.Actions.Add(action==null ? null : new ScenarioAction { Kind=action.Kind,Target=action.Target,ProjectPath=action.ProjectPath,Urls=action.Urls==null ? null : new List<string>(action.Urls) });
   return result;
  }
 }
}
