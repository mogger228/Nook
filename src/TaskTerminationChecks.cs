using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace Nook {
 internal static class TaskTerminationChecks {
  static string Exe { get { return Assembly.GetExecutingAssembly().Location; } }
  internal static bool HandleProbe(string[] args) {
   if(args.Length!=2 || args[0]!="--task-probe") return false;
   if(args[1]=="child") { Thread.Sleep(20000); return true; }
   string file=Encoding.UTF8.GetString(Convert.FromBase64String(args[1]));
   using(var child=Process.Start(new ProcessStartInfo(Exe,"--task-probe child") { UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden })) {
    var identity=TaskTerminator.ReadIdentity((uint)child.Id);
    File.WriteAllText(file,identity.Id+":"+identity.Created);
    Thread.Sleep(20000);
   }
   return true;
  }
  static bool Alive(TaskIdentity identity) {
   var actual=TaskTerminator.ReadIdentity(identity.Id);
   return actual!=null && actual.Created==identity.Created;
  }
  internal static void Run(Action<string,bool> check) {
   string file=Path.Combine(Path.GetTempPath(),"Nook-task-test-"+Guid.NewGuid().ToString("N")+".txt");
   Process parent=null,peer=null; TaskIdentity child=null;
   try {
    string argument=Convert.ToBase64String(Encoding.UTF8.GetBytes(file));
    parent=Process.Start(new ProcessStartInfo(Exe,"--task-probe "+argument) { UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden });
    var wait=Stopwatch.StartNew(); while(!File.Exists(file) && wait.ElapsedMilliseconds<5000 && !parent.HasExited) Thread.Sleep(20);
    var parts=File.ReadAllText(file).Split(':'); child=new TaskIdentity { Id=uint.Parse(parts[0]),Created=long.Parse(parts[1]) };
    var root=TaskTerminator.ReadIdentity((uint)parent.Id);
    peer=Process.Start(new ProcessStartInfo(Exe,"--task-probe child") { UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden });
    var peerIdentity=TaskTerminator.ReadIdentity((uint)peer.Id);
    check("Child parent is read from the actual process handle",child!=null && TaskTerminator.BelongsToParent(TaskTerminator.ReadIdentity(child.Id),root));
    check("Recycled foreign child PID is rejected",!TaskTerminator.BelongsToParent(new TaskIdentity { Id=child.Id,Parent=root.Id+1,Created=root.Created+1,Session=root.Session },root));
    var stale=new TaskIdentity { Id=root.Id,Created=root.Created+1 };
    var ignored=TaskTerminator.End(new List<TaskIdentity> { stale });
    check("Stale process identity cannot terminate a reused PID",ignored.Success && Alive(root) && Alive(child));
    var own=TaskTerminator.ReadIdentity((uint)Process.GetCurrentProcess().Id);
    var protectedSelf=TaskTerminator.End(new List<TaskIdentity> { own });
    check("End task cannot terminate the calling process",!protectedSelf.Success && Alive(own));
    var protectedDock=TaskTerminator.End(new List<TaskIdentity> { root },root.Id);
    check("One-shot helper policy protects the original dock PID",!protectedDock.Success && Alive(root));
    var ended=TaskTerminator.End(new List<TaskIdentity> { root });
    check("Force termination ends disposable parent and child",ended.Success && parent.WaitForExit(3000) && !Alive(child));
    check("Unrelated process with the same executable stays alive",Alive(peerIdentity));
    check("Already terminated task is handled without error",TaskTerminator.End(new List<TaskIdentity> { root }).Success);
   } catch(Exception ex) { check("Task termination checks: "+ex.Message,false); }
   finally {
    if(parent!=null) { if(!parent.HasExited) parent.Kill(); parent.Dispose(); }
    if(peer!=null) { if(!peer.HasExited) peer.Kill(); peer.Dispose(); }
    if(child!=null && Alive(child)) TaskTerminator.End(new List<TaskIdentity> { child });
    if(File.Exists(file)) File.Delete(file);
   }
  }
 }
}
