using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace Nook {
 internal static class TaskbarPinImport {
  public static string Import(Preferences prefs) {
   if(prefs==null) return "Импорт не выполнен: настройки недоступны.";
   if(prefs.Pins!=null && prefs.Pins.Count!=0) return "Закреплённые приложения уже настроены; импорт пропущен.";
   var entries=new List<Entry>(); string warning=""; int skipped=0;
   try {
    byte[] bytes;
    using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband")) bytes=key==null ? null : key.GetValue("Favorites") as byte[];
    List<byte[]> pidls;
    if(!TryParse(bytes,out pidls)) throw new InvalidDataException("Неизвестный формат Taskband.");
    foreach(var pidl in pidls) {
     string path=NormalizeShellPath(PidlName(pidl,0x80028000));
     if(string.IsNullOrWhiteSpace(path) || IsTombstone(path)) { skipped++; continue; }
     entries.Add(new Entry { Path=path,Title=PidlName(pidl,0) });
    }
   } catch(Exception error) { if(!Expected(error)) throw; warning="Порядок панели Windows недоступен. "; }
   if(entries.Count==0) {
    warning="Порядок панели Windows недоступен; ярлыки добавлены по имени. ";
    try {
     string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),@"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
     if(Directory.Exists(folder)) {
      string[] files=Directory.GetFiles(folder); Array.Sort(files,StringComparer.OrdinalIgnoreCase);
      foreach(string file in files) if(IsShortcut(file) && !IsTombstone(file)) entries.Add(new Entry { Path=file,Title=Path.GetFileNameWithoutExtension(file) });
     }
    } catch(Exception error) { if(!Expected(error)) throw; return warning+"Не удалось прочитать закреплённые ярлыки: "+error.Message; }
   }
   if(entries.Count==0) return "Закреплённые приложения Windows не найдены. "+warning;
   object script=null,shell=null;
   var pins=new List<string>(); var details=new List<PinDetails>();
   try {
    string destination=Path.Combine(Preferences.Folder,"Pins"); Directory.CreateDirectory(destination);
    try { script=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell",true)); } catch(Exception error) { if(!Expected(error)) throw; }
    try { shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application",true)); } catch(Exception error) { if(!Expected(error)) throw; }
    var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach(var entry in entries) {
     if(!seen.Add(entry.Path)) continue;
     try {
      bool packaged=entry.Path.StartsWith("shell:AppsFolder\\",StringComparison.OrdinalIgnoreCase);
      if(!packaged && !File.Exists(entry.Path)) { skipped++; continue; }
      string title=string.IsNullOrWhiteSpace(entry.Title) ? (packaged ? entry.Path.Substring(17) : Path.GetFileNameWithoutExtension(entry.Path)) : entry.Title;
      bool executable=!packaged && string.Equals(Path.GetExtension(entry.Path),".exe",StringComparison.OrdinalIgnoreCase);
      string extension=packaged || executable ? ".lnk" : Path.GetExtension(entry.Path);
      if(!packaged && !executable && !IsShortcut(entry.Path)) { skipped++; continue; }
      string launch=Path.Combine(destination,SafeName(title)+"-"+Guid.NewGuid().ToString("N")+extension);
      var detail=new PinDetails { LaunchPath=launch,Title=title };
      if(packaged || executable) {
       if(script==null) { skipped++; continue; }
       object shortcut=null;
       try { shortcut=Call(script,"CreateShortcut",launch); Set(shortcut,"TargetPath",packaged ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe") : entry.Path); if(packaged) Set(shortcut,"Arguments",entry.Path); else Set(shortcut,"WorkingDirectory",Path.GetDirectoryName(entry.Path)); Set(shortcut,"Description",title); Call(shortcut,"Save"); }
       finally { Release(shortcut); }
       if(packaged) detail.AppId=entry.Path.Substring("shell:AppsFolder\\".Length);
       else detail.TargetPath=detail.IconPath=entry.Path;
      } else {
       File.Copy(entry.Path,launch,false);
       ReadMetadata(entry.Path,detail,script,shell);
      }
      if(File.Exists(launch)) { pins.Add(launch); details.Add(detail); } else skipped++;
     } catch(Exception error) { if(!Expected(error)) throw; skipped++; }
    }
   } catch(Exception error) { if(!Expected(error)) throw; warning+="Часть ярлыков недоступна: "+error.Message+". "; }
   finally { Release(script); Release(shell); }
   if(prefs.Pins==null) prefs.Pins=new List<string>();
   if(prefs.PinDetails==null) prefs.PinDetails=new List<PinDetails>();
   prefs.Pins.AddRange(pins); prefs.PinDetails.AddRange(details);
   return warning+"Импортировано приложений: "+pins.Count+(skipped>0 ? "; пропущено: "+skipped : "")+".";
  }

  // shortcut: this private Windows container supports only the observed flag-0
  // records; unknown layouts fall back to the public pinned-shortcut directory.
  internal static bool TryParse(byte[] bytes,out List<byte[]> pidls) {
   pidls=new List<byte[]>();
   if(bytes==null || bytes.Length==0 || bytes[bytes.Length-1]!=255) return false;
   var valid=new List<byte[]>(); int offset=0;
   while(offset<bytes.Length) {
    if(bytes[offset]==255 && offset==bytes.Length-1) { pidls=valid; return true; }
    if(bytes.Length-offset<5 || bytes[offset]!=0) return false;
    uint length=(uint)(bytes[offset+1]|bytes[offset+2]<<8|bytes[offset+3]<<16|bytes[offset+4]<<24);
    if(length<2 || length>(uint)(bytes.Length-offset-5)) return false;
    int start=offset+5,end=start+(int)length,cursor=start; bool terminated=false;
    while(end-cursor>=2) {
     int count=bytes[cursor]|bytes[cursor+1]<<8;
     if(count==0) { cursor+=2; terminated=true; break; }
     if(count<2 || count>end-cursor) return false;
     cursor+=count;
    }
    if(!terminated || cursor!=end) return false;
    var copy=new byte[(int)length]; Array.Copy(bytes,start,copy,0,copy.Length); valid.Add(copy); offset=end;
   }
   return false;
  }
  internal static string NormalizeShellPath(string path) {
   const string prefix="::{4234d49b-0245-4df3-b780-3893943456e1}\\";
   if(path!=null && path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)) return "shell:AppsFolder\\"+path.Substring(prefix.Length);
   if(path!=null && System.Text.RegularExpressions.Regex.IsMatch(path,@"^[A-Za-z0-9_.-]+![A-Za-z0-9_.-]+$")) return "shell:AppsFolder\\"+path;
   return path;
  }
  internal static bool IsTombstone(string path) {
   if(path==null) return false;
   foreach(string part in path.Split('\\','/')) if(string.Equals(part,"Tombstones",StringComparison.OrdinalIgnoreCase)) return true;
   return false;
  }
  static bool IsShortcut(string path) { string extension=Path.GetExtension(path); return string.Equals(extension,".lnk",StringComparison.OrdinalIgnoreCase)||string.Equals(extension,".url",StringComparison.OrdinalIgnoreCase)||string.Equals(extension,".website",StringComparison.OrdinalIgnoreCase); }
  static string SafeName(string title) {
   foreach(char character in Path.GetInvalidFileNameChars()) title=title.Replace(character,'_');
   title=title.Trim().TrimEnd('.'); if(title.Length>60) title=title.Substring(0,60);
   return "pin-"+(title.Length==0 ? "Application" : title);
  }
  static void ReadMetadata(string path,PinDetails detail,object script,object shell) {
   object shortcut=null,folder=null,item=null;
   try {
    if(script!=null && string.Equals(Path.GetExtension(path),".lnk",StringComparison.OrdinalIgnoreCase)) {
     shortcut=Call(script,"CreateShortcut",path);
     string target=Get(shortcut,"TargetPath") as string;
     if(!string.IsNullOrWhiteSpace(target) && File.Exists(target)) { detail.TargetPath=target; detail.IconPath=target; }
    } else if(string.Equals(Path.GetExtension(path),".exe",StringComparison.OrdinalIgnoreCase)) detail.IconPath=detail.TargetPath=path;
    if(shell!=null) {
     folder=Call(shell,"Namespace",Path.GetDirectoryName(path));
     if(folder!=null) { item=Call(folder,"ParseName",Path.GetFileName(path)); if(item!=null) detail.AppId=Call(item,"ExtendedProperty","System.AppUserModel.ID") as string; }
    }
   } catch(Exception error) { if(!Expected(error)) throw; }
   finally { Release(item); Release(folder); Release(shortcut); }
  }
  static string PidlName(byte[] bytes,uint kind) {
   IntPtr buffer=Marshal.AllocCoTaskMem(bytes.Length),name=IntPtr.Zero;
   try { Marshal.Copy(bytes,0,buffer,bytes.Length); return SHGetNameFromIDList(buffer,kind,out name)>=0 && name!=IntPtr.Zero ? Marshal.PtrToStringUni(name) : null; }
   finally { if(name!=IntPtr.Zero) Marshal.FreeCoTaskMem(name); Marshal.FreeCoTaskMem(buffer); }
  }
  static object Call(object target,string member,params object[] args) { return target.GetType().InvokeMember(member,BindingFlags.InvokeMethod,null,target,args); }
  static object Get(object target,string member) { return target.GetType().InvokeMember(member,BindingFlags.GetProperty,null,target,null); }
  static void Set(object target,string member,object value) { target.GetType().InvokeMember(member,BindingFlags.SetProperty,null,target,new [] {value}); }
  static void Release(object value) { if(value!=null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
  static bool Expected(Exception error) {
   var invoked=error as TargetInvocationException; if(invoked!=null && invoked.InnerException!=null) return Expected(invoked.InnerException);
   return error is IOException || error is UnauthorizedAccessException || error is SecurityException || error is COMException || error is ArgumentException || error is NotSupportedException || error is InvalidOperationException || error is MissingMemberException || error is TypeLoadException;
  }
  sealed class Entry { public string Path,Title; }
  [DllImport("shell32.dll",PreserveSig=true)] static extern int SHGetNameFromIDList(IntPtr pidl,uint kind,out IntPtr name);
 }
}
