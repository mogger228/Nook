$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$testRoot = Join-Path $tempRoot ('NookPinImportTests_' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $harness = @'
using System;
using System.Collections.Generic;
namespace Nook {
 public class PinDetails { public string LaunchPath,TargetPath,Title,IconPath,AppId; }
 public class Preferences { public List<string> Pins=new List<string>(); public List<PinDetails> PinDetails=new List<PinDetails>(); public static string Folder { get { throw new Exception("Tests must never write user settings."); } } }
 class PinImportChecks {
  static int count,failed;
  static void Check(string label,bool valid) { count++; Console.WriteLine((valid?"PASS ":"FAIL ")+label); if(!valid) failed++; }
  static bool Parse(byte[] bytes) { List<byte[]> output; return TaskbarPinImport.TryParse(bytes,out output); }
  static int Main() {
   Check("Null rejected",!Parse(null)); Check("Empty rejected",!Parse(new byte[0]));
   Check("Empty terminated container",Parse(new byte[]{255}));
   byte[] valid={0,5,0,0,0,3,0,42,0,0,255};
   List<byte[]> records; Check("Valid bounded PIDL",TaskbarPinImport.TryParse(valid,out records)&&records.Count==1&&records[0].Length==5);
   valid[7]=99; Check("PIDL buffer is an owned copy",records[0][2]==42); valid[7]=42;
   Check("Missing container terminator",!Parse(new byte[]{0,2,0,0,0,0,0}));
   Check("Unknown flag rejected",!Parse(new byte[]{1,2,0,0,0,0,0,255}));
   Check("u32 length overflow rejected",!Parse(new byte[]{0,255,255,255,255,255}));
   Check("Length too small rejected",!Parse(new byte[]{0,1,0,0,0,0,255}));
   Check("SHITEMID below two bytes rejected",!Parse(new byte[]{0,4,0,0,0,1,0,0,0,255}));
   Check("SHITEMID beyond PIDL rejected",!Parse(new byte[]{0,4,0,0,0,8,0,0,0,255}));
   Check("PIDL early terminator rejected",!Parse(new byte[]{0,4,0,0,0,0,0,0,0,255}));
   Check("PIDL missing terminator rejected",!Parse(new byte[]{0,2,0,0,0,2,0,255}));
   Check("Trailing garbage rejected",!Parse(new byte[]{255,255}));
   var broken=new List<byte>(valid); broken.RemoveAt(broken.Count-1); broken.AddRange(new byte[]{1,2,0,0,0,0,0,255});
   Check("Whole container validation exposes no partial records",!TaskbarPinImport.TryParse(broken.ToArray(),out records)&&records.Count==0);
   string app=TaskbarPinImport.NormalizeShellPath(@"::{4234d49b-0245-4df3-b780-3893943456e1}\Package!App");
   Check("AppsFolder normalization",app==@"shell:AppsFolder\Package!App");
   Check("Desktop path preserved",TaskbarPinImport.NormalizeShellPath(@"C:\Folder\App.lnk")==@"C:\Folder\App.lnk");
   Check("Bare packaged app identifier normalized",TaskbarPinImport.NormalizeShellPath("OpenAI.Codex_2p2nqsd0c76g0!App")==@"shell:AppsFolder\OpenAI.Codex_2p2nqsd0c76g0!App");
   Check("Path with exclamation is not a packaged app",TaskbarPinImport.NormalizeShellPath(@"C:\Folder!Name\App.exe")==@"C:\Folder!Name\App.exe");
   Check("Tombstone directory skipped",TaskbarPinImport.IsTombstone(@"C:\TOMBSTONES\App.lnk"));
   Check("Substring is not a tombstone directory",!TaskbarPinImport.IsTombstone(@"C:\MyTombstones\App.lnk"));
   var prefs=new Preferences(); prefs.Pins.Add("existing-user-pin"); prefs.PinDetails.Add(new PinDetails{Title="Keep"});
   string result=TaskbarPinImport.Import(prefs);
   Check("Existing pins are not read or replaced",prefs.Pins.Count==1&&prefs.Pins[0]=="existing-user-pin"&&prefs.PinDetails[0].Title=="Keep"&&result.Contains("пропущен"));
   Check("Missing preferences reported",TaskbarPinImport.Import(null).Contains("недоступны"));
   var random=new Random(1234); bool safe=true;
   for(int i=0;i<10000;i++) { var bytes=new byte[random.Next(0,100)]; random.NextBytes(bytes); List<byte[]> parsed; if(TaskbarPinImport.TryParse(bytes,out parsed)) foreach(var pidl in parsed) if(pidl.Length<2) safe=false; }
   Check("Ten thousand malformed byte arrays stay bounded",safe);
   Console.WriteLine("Checks: "+count+"; failures: "+failed+". No shell PIDL API, imports, registry writes or user settings writes executed."); return failed==0?0:1;
  }
 }
}
'@
    $source = Join-Path $testRoot 'PinImportChecks.cs'
    [IO.File]::WriteAllText($source,$harness,[Text.UTF8Encoding]::new($true))
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $exe = Join-Path $testRoot 'PinImportChecks.exe'
    & $compiler /nologo /target:exe /platform:x64 /optimize+ ('/out:' + $exe) (Join-Path $repo 'src\TaskbarPinImport.cs') $source
    if ($LASTEXITCODE -ne 0) { throw 'Pin import harness compilation failed.' }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw 'Pin import checks failed.' }
}
finally {
    $verified = [IO.Path]::GetFullPath($testRoot)
    if (!$verified.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($verified) -notlike 'NookPinImportTests_*') { throw 'Refusing unsafe temporary cleanup.' }
    Remove-Item -LiteralPath $verified -Recurse -Force
}
