using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Nook {
 public sealed class SearchApp {
  public string Name { get; set; }
  public string Target { get; set; }
 }
 public static class AppSearch {
  public static string SwapLayout(string value) {
   const string en="`qwertyuiop[]asdfghjkl;'zxcvbnm,./";
   const string ru="ёйцукенгшщзхъфывапролджэячсмитьбю.";
   value=(value ?? "").ToLowerInvariant(); bool russian=value.Any(c=>c>='а' && c<='я' || c=='ё');
   string from=russian ? ru : en,to=russian ? en : ru;
   return new string(value.Select(c=>from.IndexOf(c)<0 ? c : to[from.IndexOf(c)]).ToArray());
  }
  static int Score(string name,string query) {
   if(name.Equals(query,StringComparison.OrdinalIgnoreCase)) return 0;
   if(name.StartsWith(query,StringComparison.OrdinalIgnoreCase)) return 1;
   return name.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0 ? 2 : 10;
  }
  public static List<SearchApp> Find(IEnumerable<SearchApp> apps,string query) {
   query=(query ?? "").Trim(); if(query.Length==0) return new List<SearchApp>();
   string alternate=SwapLayout(query);
   return apps.Select(a=>new { App=a,Rank=Math.Min(Score(a.Name,query),Score(a.Name,alternate)) }).Where(a=>a.Rank<10)
    .OrderBy(a=>a.Rank).ThenBy(a=>a.App.Name,StringComparer.CurrentCultureIgnoreCase).Select(a=>a.App)
    .GroupBy(a=>a.Name,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToList();
  }
  static object Member(object target,string name,params object[] args) { return target.GetType().InvokeMember(name,BindingFlags.GetProperty|BindingFlags.InvokeMethod,null,target,args); }
  static void Release(object value) { if(value!=null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
  public static Task<List<SearchApp>> ReadAsync(List<SearchApp> pins) {
   var completion=new TaskCompletionSource<List<SearchApp>>();
   var thread=new Thread(delegate() {
    var apps=new List<SearchApp>(pins); object shell=null,folder=null,items=null;
    try {
     try {
      shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")); folder=Member(shell,"NameSpace","shell:AppsFolder");
      if(folder!=null) {
       items=Member(folder,"Items"); int count=Convert.ToInt32(Member(items,"Count"));
       for(int i=0;i<count;i++) {
        object item=null;
        try { item=Member(items,"Item",i); string name=Convert.ToString(Member(item,"Name")),id=Convert.ToString(Member(item,"ExtendedProperty","System.AppUserModel.ID"));
         if(!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(id)) apps.Add(new SearchApp { Name=name,Target="shell:AppsFolder\\"+id });
        } catch(COMException) { } catch(TargetInvocationException) { }
        finally { Release(item); }
       }
      }
     } catch(COMException) { } catch(TargetInvocationException) { }
     finally { Release(items); Release(folder); Release(shell); }
     foreach(var location in new[] { Environment.SpecialFolder.StartMenu,Environment.SpecialFolder.CommonStartMenu }) ReadShortcuts(Environment.GetFolderPath(location),apps);
     completion.SetResult(apps.Where(a=>!string.IsNullOrWhiteSpace(a.Name) && !string.IsNullOrWhiteSpace(a.Target)).GroupBy(a=>a.Target,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToList());
    } catch(Exception error) { completion.SetException(error); }
   });
   thread.IsBackground=true; thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
  }
  static void ReadShortcuts(string directory,List<SearchApp> apps) {
   if(string.IsNullOrEmpty(directory)) return;
   try {
    foreach(string path in Directory.GetFiles(directory)) if(new[] { ".lnk",".appref-ms",".url" }.Contains(Path.GetExtension(path).ToLowerInvariant())) apps.Add(new SearchApp { Name=Path.GetFileNameWithoutExtension(path),Target=path });
    foreach(string child in Directory.GetDirectories(directory)) if((File.GetAttributes(child)&FileAttributes.ReparsePoint)==0) ReadShortcuts(child,apps);
   } catch(UnauthorizedAccessException) { } catch(IOException) { }
  }
 }
}
