param([string]$OutputFile)
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'Не найден системный компилятор .NET Framework 4.x. Ничего не установлено.' }
$output = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Xaml.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$references += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework ('WPF\' + $_)) }
if (!$OutputFile) { $OutputFile = Join-Path $output 'Nook.exe' }
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /warn:4 ('/win32manifest:' + (Join-Path $PSScriptRoot 'src\app.manifest')) ('/out:' + $OutputFile) $references $sources
if ($LASTEXITCODE -ne 0) { throw 'Сборка завершилась с ошибкой.' }
Write-Output $OutputFile
