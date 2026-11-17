using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Nook {
 internal sealed class TaskIdentity {
  public uint Id,Session,Parent;
  public long Created;
  public string Image;
 }
 internal sealed class TaskEndResult {
  public bool Success,NeedsElevation,Cancelled;
  public string Error;
 }
 // No persistent process enumeration or retained process handles. Snapshot and
 // tree traversal happen only for an explicit End task action.
 internal static class TaskTerminator {
  const uint Query=0x1000,Terminate=1,Synchronize=0x100000;
  static readonly uint ownId=GetCurrentProcessId();
  static readonly uint ownSession=CurrentSession();
  static readonly HashSet<string> sharedHosts=new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
   "explorer.exe","ApplicationFrameHost.exe","RuntimeBroker.exe","ShellExperienceHost.exe",
   "StartMenuExperienceHost.exe","SearchHost.exe","SearchApp.exe","TextInputHost.exe",
   "svchost.exe","dllhost.exe","rundll32.exe","conhost.exe","taskhostw.exe",
   "dwm.exe","sihost.exe","winlogon.exe","csrss.exe","lsass.exe","services.exe","smss.exe"
  };
  sealed class OpenTask : IDisposable {
   public IntPtr Handle;
   public TaskIdentity Identity;
   public void Dispose() { if(Handle!=IntPtr.Zero) { CloseHandle(Handle); Handle=IntPtr.Zero; } }
  }
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct ProcessEntry {
   public uint Size,Usage,Id; public UIntPtr Heap; public uint Module,Threads,Parent;
   public int Priority; public uint Flags;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string Exe;
  }
  [StructLayout(LayoutKind.Sequential)] struct BasicInformation {
   public IntPtr ExitStatus,Peb,Affinity,Priority;
   public UIntPtr Id,Parent;
  }
  [DllImport("kernel32.dll")] static extern uint GetCurrentProcessId();
  [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr process,int informationClass,out BasicInformation information,int size,out int length);
  [DllImport("ntdll.dll")] static extern uint RtlNtStatusToDosError(int status);
  [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,uint id);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetProcessTimes(IntPtr handle,out long created,out long exited,out long kernel,out long user);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool QueryFullProcessImageName(IntPtr handle,int flags,StringBuilder name,ref int count);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool ProcessIdToSessionId(uint id,out uint session);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool IsProcessCritical(IntPtr handle,out bool critical);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool TerminateProcess(IntPtr handle,uint code);
  [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
  [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint id);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool Process32First(IntPtr snapshot,ref ProcessEntry entry);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool Process32Next(IntPtr snapshot,ref ProcessEntry entry);

  static uint CurrentSession() { uint session; ProcessIdToSessionId(ownId,out session); return session; }
  static TaskIdentity Identity(IntPtr handle,uint id,out int error) {
   error=0;
   long created,exited,kernel,user; uint session;
   if(!GetProcessTimes(handle,out created,out exited,out kernel,out user) || !ProcessIdToSessionId(id,out session)) { error=Marshal.GetLastWin32Error(); return null; }
   var name=new StringBuilder(1024); int count=name.Capacity;
   if(!QueryFullProcessImageName(handle,0,name,ref count)) {
    error=Marshal.GetLastWin32Error(); if(error!=122) return null;
    name=new StringBuilder(32768); count=name.Capacity;
    if(!QueryFullProcessImageName(handle,0,name,ref count)) { error=Marshal.GetLastWin32Error(); return null; }
   }
   BasicInformation basic; int length;
   int status=NtQueryInformationProcess(handle,0,out basic,Marshal.SizeOf(typeof(BasicInformation)),out length);
   if(status<0) { error=(int)RtlNtStatusToDosError(status); return null; }
   if(length<Marshal.SizeOf(typeof(BasicInformation)) || basic.Id.ToUInt64()!=id) { error=87; return null; }
   // Read the parent from THIS handle, not the earlier Toolhelp PID entry.
   // A recycled child PID must never turn into a foreign termination target.
   return new TaskIdentity { Id=id,Created=created,Session=session,Image=name.ToString(),Parent=(uint)basic.Parent.ToUInt64() };
  }
  internal static TaskIdentity ReadIdentity(uint id) {
   int error; return ReadIdentity(id,out error);
  }
  static TaskIdentity ReadIdentity(uint id,out int error) {
   IntPtr handle=OpenProcess(Query,false,id); if(handle==IntPtr.Zero) { error=Marshal.GetLastWin32Error(); return null; }
   try { return Identity(handle,id,out error); } finally { CloseHandle(handle); }
  }
  static bool Blocked(TaskIdentity identity,uint protectedDock) {
   return identity==null || identity.Id<=4 || identity.Id==ownId || identity.Id==protectedDock || identity.Session!=ownSession || sharedHosts.Contains(Path.GetFileName(identity.Image));
  }
  internal static List<TaskIdentity> Capture(AppItem item,out string explanation) {
   var roots=new List<TaskIdentity>(); var seen=new HashSet<uint>(); explanation="";
   foreach(IntPtr window in item.Windows) {
    uint expected; uint id=Native.ProcessId(window);
    if(!item.WindowProcesses.TryGetValue(window,out expected) || id!=expected || !seen.Add(id)) continue;
    var identity=ReadIdentity(id); if(identity==null) { explanation="Windows не разрешила прочитать сведения о процессе."; continue; }
    // An old window handle must not resolve to a different application.
    if(item.Key.StartsWith("appid:",StringComparison.OrdinalIgnoreCase)) {
     if(!string.Equals(Native.WindowAppId(id),item.Key.Substring(6),StringComparison.OrdinalIgnoreCase)) continue;
    } else if(!string.Equals(item.Key,identity.Image,StringComparison.OrdinalIgnoreCase)) continue;
    if(Blocked(identity,ownId)) { explanation="Это общий процесс Windows. Принудительное завершение из панели недоступно."; return new List<TaskIdentity>(); }
    roots.Add(identity);
   }
   if(roots.Count==0 && explanation.Length==0) explanation="У приложения больше нет запущенных окон.";
   return roots;
  }
  static OpenTask Acquire(TaskIdentity expected,uint protectedDock,out bool denied) {
   denied=false;
   IntPtr handle=OpenProcess(Query|Terminate|Synchronize,false,expected.Id);
   if(handle==IntPtr.Zero) {
    int error=Marshal.GetLastWin32Error(); denied=error==5;
    if(!denied && error!=87 && error!=1168) throw new Win32Exception(error);
    return null;
   }
   try {
    int error; var actual=Identity(handle,expected.Id,out error); bool critical;
    if(actual==null) { if(WaitForSingleObject(handle,0)==0) return null; denied=error==5; if(denied) return null; throw new Win32Exception(error); }
    if(actual.Created!=expected.Created) return null;
    if(Blocked(actual,protectedDock)) throw new InvalidOperationException("Завершение этого процесса Windows из панели недоступно.");
    if(!IsProcessCritical(handle,out critical)) { error=Marshal.GetLastWin32Error(); denied=error==5; if(denied) return null; throw new Win32Exception(error); }
    if(critical) throw new InvalidOperationException("Критический процесс Windows нельзя завершить из панели.");
    if(!string.IsNullOrEmpty(expected.Image) && !string.Equals(expected.Image,actual.Image,StringComparison.OrdinalIgnoreCase)) return null;
    var task=new OpenTask { Handle=handle,Identity=actual }; handle=IntPtr.Zero; return task;
   } finally { if(handle!=IntPtr.Zero) CloseHandle(handle); }
  }
  static List<ProcessEntry> Snapshot() {
   IntPtr handle=CreateToolhelp32Snapshot(2,0);
   if(handle==new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
   try {
    var entries=new List<ProcessEntry>(); var entry=new ProcessEntry { Size=(uint)Marshal.SizeOf(typeof(ProcessEntry)) };
    bool exists=Process32First(handle,ref entry);
    if(!exists && Marshal.GetLastWin32Error()!=18) throw new Win32Exception(Marshal.GetLastWin32Error());
    while(exists) { entries.Add(entry); entry.Size=(uint)Marshal.SizeOf(typeof(ProcessEntry)); exists=Process32Next(handle,ref entry); if(!exists && Marshal.GetLastWin32Error()!=18) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    return entries;
   } finally { CloseHandle(handle); }
  }
  internal static bool BelongsToParent(TaskIdentity child,TaskIdentity parent) { return child.Parent==parent.Id && child.Created>=parent.Created && child.Session==parent.Session; }
  static bool CollectChildren(List<OpenTask> tasks,HashSet<uint> seen,uint protectedDock,Stopwatch budget,ref bool skippedShared) {
   var snapshot=Snapshot();
   for(int i=0;i<tasks.Count;i++) {
    var parent=tasks[i].Identity;
    foreach(var entry in snapshot) {
     if(entry.Parent!=parent.Id || seen.Contains(entry.Id)) continue;
     int error; var child=ReadIdentity(entry.Id,out error);
     if(child==null) { if(error==5) return false; if(error==87 || error==1168) continue; throw new Win32Exception(error); }
     if(!BelongsToParent(child,parent)) continue;
     if(Blocked(child,protectedDock)) { skippedShared=true; continue; }
     bool denied; var task=Acquire(child,protectedDock,out denied);
     if(denied) return false;
     if(task==null) continue;
     if(!BelongsToParent(task.Identity,parent)) { task.Dispose(); continue; }
     seen.Add(task.Identity.Id); tasks.Add(task);
     if(tasks.Count>4096 || budget.ElapsedMilliseconds>8000) throw new InvalidOperationException("Сбор связанных процессов занял слишком много времени. Проверьте приложение в диспетчере задач.");
    }
   }
   return true;
  }
  internal static TaskEndResult End(List<TaskIdentity> roots,uint protectedDock=0) { return EndCore(roots,protectedDock==0 ? ownId : protectedDock,false); }
  static TaskEndResult PermissionFailure(List<TaskIdentity> roots,uint protectedDock,bool elevate) {
   return elevate ? Elevate(roots,protectedDock) : new TaskEndResult { NeedsElevation=true,Error="Для завершения приложения и связанных процессов нужны права администратора." };
  }
  static TaskEndResult EndCore(List<TaskIdentity> roots,uint protectedDock,bool elevate,bool requireAnchors=false) {
   var tasks=new List<OpenTask>(); var seen=new HashSet<uint>();
   var budget=Stopwatch.StartNew();
   bool skippedShared=false,missingAnchors=false;
   try {
    foreach(var root in roots) {
     if(!seen.Add(root.Id)) continue;
     bool denied; var task=Acquire(root,protectedDock,out denied);
     if(denied) return PermissionFailure(roots,protectedDock,elevate);
     if(task!=null) tasks.Add(task); else if(requireAnchors) missingAnchors=true;
    }
    if(tasks.Count==0) return new TaskEndResult { Success=!missingAnchors,Error=missingAnchors ? "Процессы изменились до предоставления доступа. Проверьте оставшиеся задачи в диспетчере задач." : null };
    // Preflight access before killing anything. All accepted parent/child IDs
    // stay pinned, including while the one-shot elevated helper is running.
    if(!CollectChildren(tasks,seen,protectedDock,budget,ref skippedShared)) return PermissionFailure(roots,protectedDock,elevate);
    int stopped=0;
    for(int pass=0;pass<32;pass++) {
     for(int i=stopped;i<tasks.Count;i++) {
      var task=tasks[i]; if(WaitForSingleObject(task.Handle,0)==0) continue;
      if(!TerminateProcess(task.Handle,1)) { int error=Marshal.GetLastWin32Error(); if(WaitForSingleObject(task.Handle,0)!=0) throw new Win32Exception(error); }
     }
     stopped=tasks.Count;
     foreach(var task in tasks) {
      uint remaining=(uint)Math.Max(0,Math.Min(2500,8000-budget.ElapsedMilliseconds));
      if(WaitForSingleObject(task.Handle,remaining)!=0) throw new InvalidOperationException("Windows ещё не завершила один из процессов приложения.");
     }
     // Once roots are gone, passing only those roots to a new helper cannot
     // safely rediscover denied late children. Report the partial result.
     if(!CollectChildren(tasks,seen,protectedDock,budget,ref skippedShared)) return new TaskEndResult { Error="Основные процессы завершены, но Windows отказала в доступе к новому связанному процессу. Завершите оставшиеся задачи в диспетчере задач." };
     if(tasks.Count==stopped) return new TaskEndResult {
      Success=!skippedShared && !missingAnchors,
      Error=skippedShared ? "Приложение завершено. Связанный общий процесс Windows и его дочерние процессы оставлены работающими." : missingAnchors ? "Часть процессов изменилась во время запроса доступа. Проверьте оставшиеся задачи в диспетчере задач." : null
     };
     if(budget.ElapsedMilliseconds>8000) break;
    }
    return new TaskEndResult { Error="Часть связанных процессов ещё работает. Проверьте приложение в диспетчере задач." };
   } catch(Exception ex) { return new TaskEndResult { Error=ex.Message }; }
   finally { foreach(var task in tasks) task.Dispose(); }
  }
  internal static TaskEndResult EndWithElevation(List<TaskIdentity> roots) {
   return EndCore(roots,ownId,true);
  }
  static TaskEndResult Elevate(List<TaskIdentity> roots,uint protectedDock) {
   var arguments=new StringBuilder("--end-task-admin "+protectedDock.ToString(CultureInfo.InvariantCulture));
   foreach(var root in roots) arguments.Append(" ").Append(root.Id.ToString(CultureInfo.InvariantCulture)).Append(":").Append(root.Created.ToString(CultureInfo.InvariantCulture));
   try {
    using(var helper=Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,arguments.ToString()) { UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden })) {
     helper.WaitForExit();
     return new TaskEndResult { Success=helper.ExitCode==0,Error=helper.ExitCode==0 ? null : "Windows не разрешила завершить приложение или один из связанных процессов." };
    }
   } catch(Win32Exception ex) { return new TaskEndResult { Cancelled=ex.NativeErrorCode==1223,Error=ex.Message }; }
  }
  internal static bool HandleHelper(string[] args) {
   if(args.Length==0 || args[0]!="--end-task-admin") return false;
   uint protectedDock;
   if(args.Length<3 || !uint.TryParse(args[1],NumberStyles.None,CultureInfo.InvariantCulture,out protectedDock)) { Environment.ExitCode=1; return true; }
   var roots=new List<TaskIdentity>();
   for(int i=2;i<args.Length && i<=65;i++) {
    var parts=args[i].Split(':'); uint id; long created;
    if(parts.Length!=2 || !uint.TryParse(parts[0],NumberStyles.None,CultureInfo.InvariantCulture,out id) || !long.TryParse(parts[1],NumberStyles.None,CultureInfo.InvariantCulture,out created) || created<=0) { Environment.ExitCode=1; return true; }
    roots.Add(new TaskIdentity { Id=id,Created=created });
   }
   if(roots.Count==0 || args.Length>66) { Environment.ExitCode=1; return true; }
   var result=EndCore(roots,protectedDock,false,true); Environment.ExitCode=result.Success ? 0 : 1; return true;
  }
 }
}
