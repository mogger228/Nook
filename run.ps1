param([switch]$Replace)
$ErrorActionPreference = 'Stop'
$dockExe = Join-Path $PSScriptRoot 'bin\Nook.exe'
if (!(Test-Path -LiteralPath $dockExe)) { & (Join-Path $PSScriptRoot 'build.ps1') }
$dockData = Join-Path $PSScriptRoot 'data'
$dockEnvironment = [string[]]@([Environment]::GetEnvironmentVariables().GetEnumerator() | ForEach-Object { $_.Key + '=' + $_.Value })
# WMI starts it outside the launching terminal/Codex job. Explicit desktop and
# environment keep the interactive user profile and its settings intact.
$dockStartup = New-CimInstance -ClassName Win32_ProcessStartup -ClientOnly -Property @{
 WinstationDesktop='winsta0\default'; ShowWindow=[uint16]1; EnvironmentVariables=$dockEnvironment
}
$dockCommand = '"' + $dockExe + '" "--data-dir=' + $dockData + '"'
if ($Replace) { $dockCommand += ' --replace' }
$dockResult = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
 CommandLine=$dockCommand; CurrentDirectory=$PSScriptRoot; ProcessStartupInformation=$dockStartup
}
if ($dockResult.ReturnValue -ne 0) { throw ('Не удалось запустить Nook: ' + $dockResult.ReturnValue) }
Write-Output ('Nook PID: ' + $dockResult.ProcessId)
