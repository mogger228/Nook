param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version='0.1.0')
$ErrorActionPreference='Stop'
$projectRoot=$PSScriptRoot
$stage=Join-Path $projectRoot ('artifacts\release-stage-'+[Guid]::NewGuid().ToString('N'))
$packageName='Nook-'+$Version+'-win-x64'
$package=Join-Path $stage $packageName
New-Item -ItemType Directory -Path $package -Force | Out-Null
& (Join-Path $projectRoot 'build.ps1') -OutputFile (Join-Path $package 'Nook.exe')
if($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
$binaryVersion=[Reflection.AssemblyName]::GetAssemblyName((Join-Path $package 'Nook.exe')).Version.ToString(3)
if($binaryVersion -ne $Version) { throw 'Package version must match src/AssemblyInfo.cs and release documentation.' }
$files=@('Nook.exe.config','portable.flag','Start-Nook.cmd','Settings.cmd','Restore-Taskbar.cmd','README.md','COMPATIBILITY.md','THIRD-PARTY-NOTICES.txt')
foreach($file in $files) { Copy-Item -LiteralPath (Join-Path $projectRoot ('release\'+$file)) -Destination (Join-Path $package $file) }
$actual=@(Get-ChildItem -LiteralPath $package -File | ForEach-Object Name | Sort-Object)
$expected=@(@('Nook.exe')+$files | Sort-Object)
if(Compare-Object $expected $actual) { throw 'Unexpected package files.' }
foreach($file in Get-ChildItem -LiteralPath $package -File | Where-Object Extension -ne '.exe') {
 $content=[IO.File]::ReadAllText($file.FullName)
 if($content -match 'C:\\Users\\|data\\Pins\\|--data-dir=C:|CODEX_HOME') { throw ('Personal/development path in '+$file.Name) }
}
$dist=Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$archive=Join-Path $dist ($packageName+'.zip')
Compress-Archive -LiteralPath $package -DestinationPath $archive -Force
$hash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($archive+'.sha256',$hash+'  '+[IO.Path]::GetFileName($archive)+[Environment]::NewLine,(New-Object Text.UTF8Encoding($false)))
[IO.File]::WriteAllText((Join-Path $projectRoot 'artifacts\release-stage-path.txt'),$package,(New-Object Text.UTF8Encoding($false)))
Write-Output $archive
Write-Output ('SHA256: '+$hash)
