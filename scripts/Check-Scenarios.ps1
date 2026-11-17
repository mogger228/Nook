$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$testDirectory = [IO.Path]::GetFullPath((Join-Path $temporaryRoot ('NookScenarioTests_' + [Guid]::NewGuid().ToString('N'))))
if (!$testDirectory.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid test directory.' }
New-Item -ItemType Directory -Path $testDirectory | Out-Null
try {
    $harness = @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nook;
class ScenarioChecks {
 static int failures,checks;
 static void Check(string name,bool passed) { checks++; Console.WriteLine((passed?"PASS ":"FAIL ")+name); if(!passed) failures++; }
 static ScenarioDefinition Scenario(ScenarioAction action) { var value=new ScenarioDefinition { Name="Тест" }; value.Actions.Add(action); return value; }
 static string[] Arguments(string text) {
  int count; IntPtr argv=CommandLineToArgvW(text,out count);
  if(argv==IntPtr.Zero) throw new InvalidOperationException("CommandLineToArgvW failed");
  try { var result=new string[count]; for(int i=0;i<count;i++) result[i]=Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv,i*IntPtr.Size)); return result; }
 finally { LocalFree(argv); }
 }
 static string[] WaitForStub(string folder) {
  var watch=Stopwatch.StartNew(); string[] files;
  while((files=Directory.GetFiles(folder,"*.done")).Length==0) { if(watch.ElapsedMilliseconds>10000) throw new TimeoutException("Own temporary stub did not write its marker."); Thread.Sleep(20); }
  if(files.Length!=1) throw new InvalidOperationException("Expected exactly one own stub marker.");
  string[] lines=File.ReadAllLines(files[0]); int pid=int.Parse(lines[0]);
  try { using(var child=Process.GetProcessById(pid)) { if(!child.WaitForExit(10000)) throw new TimeoutException("Own stub did not exit."); } } catch(ArgumentException) { }
  int count=int.Parse(lines[1]); var result=new string[count];
  for(int i=0;i<count;i++) result[i]=Encoding.UTF8.GetString(Convert.FromBase64String(lines[i+2]));
  File.Delete(files[0]); return result;
 }
 static int Main(string[] arguments) {
  SetErrorMode(0x8003); // Own harness only: suppress critical/open-file/native-fault UI.
  string folder=arguments[0], app=Path.Combine(folder,"Dummy browser.exe"), project=Path.Combine(folder,"Проект с пробелом");
  string stub=Path.Combine(folder,"Scenario launch stub.exe"), records=Path.Combine(folder,"Stub markers"); Directory.CreateDirectory(records);
  File.WriteAllText(app,"This is test data; never execute."); Directory.CreateDirectory(project);
  string projectFile=Path.Combine(project,"Example.sln"); File.WriteAllText(projectFile,"Test data");
  string[] values={"","plain","two words",@"C:\folder with space\","a\"b",@"C:\a\\","Русский путь","x\\\"y",@"\\server\share\"};
  foreach(string value in values) { var parsed=Arguments("dummy.exe "+ScenarioLauncher.QuoteArgument(value)); Check("Windows argument round trip: "+value,parsed.Length==2 && parsed[1]==value); }
  var appScenario=Scenario(new ScenarioAction { Kind="Application",Target=app });
  var appPlan=ScenarioLauncher.BuildLaunches(appScenario);
  Check("Application plan uses shell, no arguments",appPlan.Count==1 && appPlan[0].FileName==app && appPlan[0].Arguments=="" && appPlan[0].UseShellExecute);
  Check("GUID default and enabled default",new Guid(appScenario.Id)!=Guid.Empty && appScenario.Enabled);
  var projectScenario=Scenario(new ScenarioAction { Kind="Project",Target=app,ProjectPath=project+Path.DirectorySeparatorChar });
  var projectPlan=ScenarioLauncher.BuildLaunches(projectScenario);
  var projectArgs=Arguments("dummy.exe "+projectPlan[0].Arguments);
  Check("Project directory remains a single argument",projectArgs.Length==2 && projectArgs[1]==Path.GetFullPath(project+Path.DirectorySeparatorChar));
  projectScenario.Actions[0].ProjectPath=projectFile;
  Check("Project file validates",ScenarioLauncher.Validate(projectScenario).Count==0);
  var web=new ScenarioAction { Kind="Web",Target=app,Urls=new List<string> { "https://example.test/a?one=1&two=2","http://example.test/second" } };
  var webScenario=Scenario(web); var browserPlan=ScenarioLauncher.BuildLaunches(webScenario);
  var browserArgs=Arguments("dummy.exe "+browserPlan[0].Arguments);
  Check("Chosen browser receives URL batch",browserPlan.Count==1 && browserArgs.Length==3 && browserArgs[1]==web.Urls[0] && browserArgs[2]==web.Urls[1]);
  web.Urls[0]="https://example.test/changed";
  Check("Built plan is independent of later model edits",browserArgs[1]=="https://example.test/a?one=1&two=2");
  web.Target=""; var defaultPlan=ScenarioLauncher.BuildLaunches(webScenario);
  Check("Default browser has one shell launch per URL",defaultPlan.Count==2 && defaultPlan[0].FileName==web.Urls[0] && defaultPlan[0].Arguments=="" && defaultPlan[1].UseShellExecute);
  foreach(string invalid in new [] {"javascript:alert(1)","file:///C:/Windows","/relative","ftp://example.test/","https://",""}) {
   Check("Reject non-web/malformed URL: "+invalid,ScenarioLauncher.Validate(Scenario(new ScenarioAction { Kind="Web",Urls=new List<string>{invalid} })).Count>0);
  }
  Check("Reject missing target",ScenarioLauncher.Validate(Scenario(new ScenarioAction { Kind="Application",Target=Path.Combine(folder,"missing.exe") })).Count>0);
  Check("Reject missing project",ScenarioLauncher.Validate(Scenario(new ScenarioAction { Kind="Project",Target=app,ProjectPath=Path.Combine(folder,"missing project") })).Count>0);
  Check("Reject unknown action",ScenarioLauncher.Validate(Scenario(new ScenarioAction { Kind="Other" })).Count>0);
  Check("Reject null action",ScenarioLauncher.Validate(Scenario(null)).Count>0);
  Check("Reject null scenario",ScenarioLauncher.Validate(null).Count>0);
  appScenario.HotkeyKey=uint.MaxValue; appScenario.HotkeyModifiers=uint.MaxValue;
  Check("Launcher leaves hotkey validation to runtime",ScenarioLauncher.Validate(appScenario).Count==0);
  bool rejected=false; try { ScenarioLauncher.BuildLaunches(new ScenarioDefinition { Name="Empty" }); } catch(ArgumentException) { rejected=true; }
  Check("Invalid plan fails before launching",rejected);
  // No user application is ever launched: the following runtime cases use only
  // our freshly compiled windowless stub, which records arguments without opening them.
  var invalidScenario=new ScenarioDefinition { Name="",Actions=null,Enabled=false };
  var task=ScenarioLauncher.RunAsync(invalidScenario); invalidScenario.Name="Changed after snapshot";
  var errors=task.GetAwaiter().GetResult();
  Check("RunAsync snapshots caller model and returns validation errors",errors.Exists(x=>x.Contains("название")) && errors.Exists(x=>x.Contains("отключён")));
  var realProject=Scenario(new ScenarioAction { Kind="Project",Target=stub,ProjectPath=project+Path.DirectorySeparatorChar });
  int oldMinWorkers,oldMinIo,oldMaxWorkers,oldMaxIo;
  ThreadPool.GetMinThreads(out oldMinWorkers,out oldMinIo); ThreadPool.GetMaxThreads(out oldMaxWorkers,out oldMaxIo);
  using(var entered=new ManualResetEvent(false)) using(var release=new ManualResetEvent(false)) {
   try {
    if(!ThreadPool.SetMinThreads(1,oldMinIo) || !ThreadPool.SetMaxThreads(1,oldMaxIo)) throw new InvalidOperationException("Cannot configure the isolated test harness worker gate.");
    ThreadPool.QueueUserWorkItem(delegate { entered.Set(); release.WaitOne(); });
    if(!entered.WaitOne(10000)) throw new TimeoutException("Test worker gate did not start.");
    var watch=Stopwatch.StartNew(); var running=ScenarioLauncher.RunAsync(realProject); watch.Stop();
    Check("RunAsync returns promptly while its worker is blocked",watch.ElapsedMilliseconds<1000 && !running.IsCompleted);
    realProject.Actions[0].ProjectPath=Path.Combine(folder,"changed after async snapshot");
    release.Set();
    var projectFailures=running.GetAwaiter().GetResult();
    Check("Own project stub launched without engine errors",projectFailures.Count==0);
   } finally { release.Set(); ThreadPool.SetMaxThreads(oldMaxWorkers,oldMaxIo); ThreadPool.SetMinThreads(oldMinWorkers,oldMinIo); }
  }
  var actualProjectArgs=WaitForStub(records);
  Check("End-to-end project argv preserves one Cyrillic/spaced/trailing-slash argument",actualProjectArgs.Length==1 && actualProjectArgs[0]==Path.GetFullPath(project+Path.DirectorySeparatorChar));
  var applicationFailures=ScenarioLauncher.RunAsync(Scenario(new ScenarioAction { Kind="Application",Target=stub })).GetAwaiter().GetResult();
  Check("Own application stub launches with no arguments",applicationFailures.Count==0 && WaitForStub(records).Length==0);
  var browserStub=new ScenarioAction { Kind="Web",Target=stub,Urls=new List<string> { "https://example.test/a?one=1&two=2","https://example.test/%D0%A2%D0%B5%D1%81%D1%82" } };
  var browserFailures=ScenarioLauncher.RunAsync(Scenario(browserStub)).GetAwaiter().GetResult();
  var actualBrowserArgs=WaitForStub(records);
  Check("Own browser stub receives an intact URL batch without opening URLs",browserFailures.Count==0 && actualBrowserArgs.Length==2 && actualBrowserArgs[0]==new Uri(browserStub.Urls[0]).AbsoluteUri && actualBrowserArgs[1]==new Uri(browserStub.Urls[1]).AbsoluteUri);
  var continuation=new ScenarioDefinition { Name="Expected failure then valid stub" };
  continuation.Actions.Add(new ScenarioAction { Kind="Application",Target=app }); // Existing plain-text .exe: expected ERROR_BAD_EXE_FORMAT, not a real program.
  continuation.Actions.Add(new ScenarioAction { Kind="Project",Target=stub,ProjectPath=projectFile });
  var continuationFailures=ScenarioLauncher.RunAsync(continuation).GetAwaiter().GetResult();
  var continuedArgs=WaitForStub(records);
  Check("Expected launch failure is collected exactly once",continuationFailures.Count==1 && continuationFailures[0].Contains(app));
  Check("Launch sequence continues to own valid stub after expected failure",continuedArgs.Length==1 && continuedArgs[0]==projectFile);
  Check("Own stub markers fully consumed",Directory.GetFiles(records).Length==0);
  Console.WriteLine("Checks: "+checks+"; failures: "+failures+". Only own temporary windowless stubs were executed; no user applications or URLs opened.");
  return failures==0 ? 0 : 1;
 }
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern IntPtr CommandLineToArgvW(string commandLine,out int count);
 [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
 [DllImport("kernel32.dll")] static extern uint SetErrorMode(uint mode);
}
'@
    $harnessPath = Join-Path $testDirectory 'ScenarioChecks.cs'
    [IO.File]::WriteAllText($harnessPath, $harness, [Text.UTF8Encoding]::new($true))
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
    $compiler = Join-Path $framework 'csc.exe'
    if (!(Test-Path -LiteralPath $compiler)) { throw 'System .NET Framework compiler not found.' }
    $stub = @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
class ScenarioLaunchStub {
 static int Main(string[] arguments) {
  string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Stub markers"); Directory.CreateDirectory(folder);
  var lines=new List<string>(); using(var self=Process.GetCurrentProcess()) lines.Add(self.Id.ToString());
  lines.Add(arguments.Length.ToString()); foreach(string value in arguments) lines.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(value)));
  string temporary=Path.Combine(folder,Guid.NewGuid().ToString("N")+".tmp");
  File.WriteAllLines(temporary,lines.ToArray(),Encoding.UTF8); File.Move(temporary,Path.ChangeExtension(temporary,"done"));
  return 0;
 }
}
'@
    $stubSource = Join-Path $testDirectory 'ScenarioLaunchStub.cs'
    [IO.File]::WriteAllText($stubSource, $stub, [Text.UTF8Encoding]::new($true))
    & $compiler /nologo /target:winexe /platform:x64 /optimize+ ('/out:' + (Join-Path $testDirectory 'Scenario launch stub.exe')) $stubSource
    if ($LASTEXITCODE -ne 0) { throw 'Own windowless scenario stub did not compile.' }
    $testExecutable = Join-Path $testDirectory 'ScenarioChecks.exe'
    & $compiler /nologo /target:exe /platform:x64 /optimize+ ('/out:' + $testExecutable) ('/reference:' + (Join-Path $framework 'System.dll')) ('/reference:' + (Join-Path $framework 'System.Core.dll')) (Join-Path $projectRoot 'src\Scenarios.cs') $harnessPath
    if ($LASTEXITCODE -ne 0) { throw 'Scenario test harness did not compile.' }
    & $testExecutable $testDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Scenario checks failed.' }
}
finally {
    $verifiedDirectory = [IO.Path]::GetFullPath($testDirectory)
    if (!$verifiedDirectory.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($verifiedDirectory) -notlike 'NookScenarioTests_*') { throw 'Refusing to remove an unexpected directory.' }
    Remove-Item -LiteralPath $verifiedDirectory -Recurse -Force
}
