# Read-only adapter for the observed Windows 11 Taskband Favorites layout.
# Undocumented container: reject unknown flags or broken bounds instead of guessing.
# PIDL/name API: https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-shgetnamefromidlist
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$bytes = (Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband' -Name Favorites).Favorites
if ($bytes -isnot [byte[]]) { throw 'Taskband Favorites is not a binary value.' }
$entries = [Collections.Generic.List[object]]::new()
$offset = 0
while ($offset -lt $bytes.Length) {
    if ($bytes[$offset] -eq 255 -and $offset -eq $bytes.Length - 1) { $offset++; break }
    if ($bytes.Length - $offset -lt 5 -or $bytes[$offset] -ne 0) { throw "Unknown Taskband record at offset $offset." }
    $length = [BitConverter]::ToUInt32($bytes, $offset + 1)
    if ($length -lt 2 -or $length -gt $bytes.Length - $offset - 5) { throw "Invalid PIDL length at offset $offset." }
    $start = $offset + 5
    $end = $start + [int]$length
    $cursor = $start
    $terminated = $false
    while ($cursor + 2 -le $end) {
        $cb = [BitConverter]::ToUInt16($bytes, $cursor)
        if ($cb -eq 0) { $cursor += 2; $terminated = $true; break }
        if ($cb -lt 2 -or $cb -gt $end - $cursor) { throw "Invalid SHITEMID at offset $cursor." }
        $cursor += $cb
    }
    if (-not $terminated -or $cursor -ne $end) { throw "PIDL boundary mismatch at offset $offset." }
    $copy = [byte[]]::new([int]$length)
    [Array]::Copy($bytes, $start, $copy, 0, [int]$length)
    $entries.Add([pscustomobject]@{ Index = $entries.Count; Pidl = $copy })
    $offset = $end
}
if ($offset -ne $bytes.Length -or $bytes.Length -eq 0 -or $bytes[-1] -ne 255) { throw 'Missing Taskband terminator.' }

# The API receives an owned buffer only after every record has passed structural
# validation. No pointers into arbitrary byte offsets or Explorer memory are used.
if (-not ('Nook.TaskbandReader' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace Nook {
 public static class TaskbandReader {
  [DllImport("shell32.dll", PreserveSig=true)] static extern int SHGetNameFromIDList(IntPtr pidl, uint kind, out IntPtr name);
  public static string Name(byte[] pidl, uint kind) {
   IntPtr buffer = Marshal.AllocCoTaskMem(pidl.Length);
   IntPtr name = IntPtr.Zero;
   try {
    Marshal.Copy(pidl, 0, buffer, pidl.Length);
    int result = SHGetNameFromIDList(buffer, kind, out name);
    if (result < 0 || name == IntPtr.Zero) return null;
    return Marshal.PtrToStringUni(name);
   } finally {
    if (name != IntPtr.Zero) Marshal.FreeCoTaskMem(name);
    Marshal.FreeCoTaskMem(buffer);
   }
  }
 }
}
'@
}
foreach ($entry in $entries) {
    $parsingPath = [Nook.TaskbandReader]::Name($entry.Pidl, [uint32]0x80028000L)
    $displayName = [Nook.TaskbandReader]::Name($entry.Pidl, 0)
    if ([string]::IsNullOrWhiteSpace($parsingPath)) { Write-Warning "Unresolved Taskband record $($entry.Index) skipped."; continue }
    # Tombstones are a real parent directory in the resolved shell path.
    if ($parsingPath -match '(?i)(^|[\\/])Tombstones([\\/]|$)') { continue }
    # Packaged items use the AppsFolder shell namespace in the parsing name.
    $appsPrefix = '::{4234d49b-0245-4df3-b780-3893943456e1}\'
    if ($parsingPath.StartsWith($appsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        $parsingPath = 'shell:AppsFolder\' + $parsingPath.Substring($appsPrefix.Length)
    }
    [pscustomobject]@{ Index = $entry.Index; ParsingPath = $parsingPath; DisplayName = $displayName }
}
