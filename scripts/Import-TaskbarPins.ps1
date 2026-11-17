[CmdletBinding()]
param([switch]$Apply,[string]$DataDirectory=(Join-Path (Split-Path -Parent $PSScriptRoot) 'data'))
$ErrorActionPreference = 'Stop'
$entries = @(& (Join-Path $PSScriptRoot 'Read-Taskband.ps1'))
if (!$entries.Count) { throw 'No active taskbar pins were resolved.' }
$dockFolder = [IO.Path]::GetFullPath($DataDirectory)
$destination = Join-Path $dockFolder 'Pins'
$settingsPath = Join-Path $dockFolder 'settings.xml'
$shortcutReader = New-Object -ComObject WScript.Shell
$shellReader = New-Object -ComObject Shell.Application
$installedApps = @($shellReader.Namespace('shell:AppsFolder').Items())
$packages = @(Get-AppxPackage)
$resolved = [Collections.Generic.List[object]]::new()
foreach ($entry in $entries) {
    $source = $entry.ParsingPath
    $title = $entry.DisplayName
    $target = ''; $appId = ''; $icon = ''; $packaged = $false
    $sourceAppId = $source -replace '^shell:AppsFolder\\', ''
    $app = $installedApps | Where-Object { $_.Path -eq $sourceAppId } | Select-Object -First 1
    if ($sourceAppId -match '^[^\\]+![^\\]+$' -and $app) {
        $packaged = $true; $appId = $sourceAppId; $title = $app.Name
        $family, $applicationId = $appId -split '!', 2
        $package = $packages | Where-Object PackageFamilyName -eq $family | Select-Object -First 1
        if ($package) {
            $manifest = Get-AppxPackageManifest -Package $package.PackageFullName
            $application = @($manifest.Package.Applications.Application) | Where-Object Id -eq $applicationId | Select-Object -First 1
            if ($application.Executable) {
                $candidate = Join-Path $package.InstallLocation $application.Executable
                if (Test-Path -LiteralPath $candidate) { $target = $candidate }
            }
            if ($application.VisualElements.Square44x44Logo) {
                $logoPath = Join-Path $package.InstallLocation $application.VisualElements.Square44x44Logo
                if (Test-Path -LiteralPath $logoPath) { $icon = $logoPath }
                else {
                    $stem = [IO.Path]::GetFileNameWithoutExtension($logoPath)
                    $logo = Get-ChildItem -LiteralPath (Split-Path $logoPath) -Filter ($stem + '*.png') -ErrorAction SilentlyContinue |
                        Sort-Object @{Expression={if($_.Name -match 'targetsize-48.*unplated') {0} elseif($_.Name -match 'targetsize-48') {1} elseif($_.Name -match 'scale-100') {2} else {3}}},Name | Select-Object -First 1
                    if ($logo) { $icon = $logo.FullName }
                }
            }
        }
        $extension = '.lnk'
    } else {
        if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Cannot resolve pinned shortcut: $source" }
        $extension = [IO.Path]::GetExtension($source)
        if ($extension -ieq '.lnk') {
            $shortcut = $shortcutReader.CreateShortcut($source)
            $target = $shortcut.TargetPath
            $namespace = $shellReader.Namespace((Split-Path $source))
            $shellItem = $namespace.ParseName([IO.Path]::GetFileName($source))
            $appId = $shellItem.ExtendedProperty('System.AppUserModel.ID')
            if ($appId -eq 'Microsoft.Windows.Explorer') { $target = Join-Path $env:WINDIR 'explorer.exe' }
            # Discord's updater launches the actual UI in an app-version subdirectory.
            # Keep the original shortcut for launch; this path is only a grouping alias.
            if ($shortcut.Arguments -match '--processStart\s+Discord\.exe' -and $target) {
                $discordExe = Get-ChildItem -LiteralPath (Split-Path $target) -Directory -Filter 'app-*' -ErrorAction SilentlyContinue |
                    Sort-Object LastWriteTime -Descending | ForEach-Object { Join-Path $_.FullName 'Discord.exe' } |
                    Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
                if ($discordExe) { $target = $discordExe }
            }
            $iconFile = [Environment]::ExpandEnvironmentVariables(($shortcut.IconLocation -replace ',-?\d+$',''))
            if ($iconFile -and [IO.Path]::GetExtension($iconFile) -in @('.ico','.png','.bmp','.jpg') -and (Test-Path -LiteralPath $iconFile)) { $icon=$iconFile }
            elseif ($target -and (Test-Path -LiteralPath $target)) { $icon=$target }
        }
        if (!$title) { $title = [IO.Path]::GetFileNameWithoutExtension($source) }
    }
    $safeName = [regex]::Replace($title, '[<>:"/\\|?*\x00-\x1f]', '_')
    $filename = ('{0:D2}-{1}{2}' -f ($resolved.Count + 1), $safeName, $extension)
    $launchPath = Join-Path $destination $filename
    $resolved.Add([pscustomobject]@{ Index=$entry.Index; Title=$title; Source=$source; LaunchPath=$launchPath; TargetPath=$target; AppId=$appId; IconPath=$icon; Packaged=$packaged })
}
if (!$Apply) { $resolved | Select-Object Index,Title,Source,TargetPath,AppId; return }
if (Get-Process -Name Nook -ErrorAction SilentlyContinue) { throw 'Close Nook before importing to avoid overwriting live settings.' }
if (!(Test-Path -LiteralPath $settingsPath)) { throw 'Run Nook once and save settings before import.' }
[xml]$settings = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8
$backup = Join-Path $dockFolder ('settings.before-import.' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.xml')
Copy-Item -LiteralPath $settingsPath -Destination $backup
New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($pin in $resolved) {
    if ($pin.Packaged) {
        $shortcut = $shortcutReader.CreateShortcut($pin.LaunchPath)
        $shortcut.TargetPath = Join-Path $env:WINDIR 'explorer.exe'
        $shortcut.Arguments = 'shell:AppsFolder\' + $pin.AppId
        $shortcut.Description = $pin.Title
        $shortcut.Save()
    } else { Copy-Item -LiteralPath $pin.Source -Destination $pin.LaunchPath -Force }
    if ($pin.IconPath -and [IO.Path]::GetExtension($pin.IconPath) -in @('.png','.ico','.bmp','.jpg')) {
        $iconCopy = [IO.Path]::ChangeExtension($pin.LaunchPath, [IO.Path]::GetExtension($pin.IconPath))
        Copy-Item -LiteralPath $pin.IconPath -Destination $iconCopy -Force
        $pin.IconPath = $iconCopy
    }
}
$oldPins = @($settings.Preferences.Pins.string | Where-Object { $_ })
$oldDetails = @($settings.Preferences.PinDetails.PinDetails | Where-Object { $_ })
$identities = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($pin in $resolved) { foreach ($identity in @($pin.LaunchPath,$pin.Source,$pin.TargetPath,$pin.AppId)) { if ($identity) { [void]$identities.Add($identity) } } }
$keepOld = @($oldPins | Where-Object {
    $oldPath = [string]$_
    # Preserve custom shortcuts even when they launch another browser profile or
    # document through an already imported executable. Only exact identities and
    # copies managed by this importer are replaced.
    $managedCopy = [string]::Equals([IO.Path]::GetDirectoryName($oldPath),$destination,[StringComparison]::OrdinalIgnoreCase)
    !$identities.Contains($oldPath) -and !$managedCopy
})
foreach ($name in @('Pins','PinDetails')) {
    $old = $settings.Preferences.SelectSingleNode($name)
    if ($old) { [void]$settings.Preferences.RemoveChild($old) }
}
$pinsNode = $settings.CreateElement('Pins')
$detailsNode = $settings.CreateElement('PinDetails')
[void]$settings.Preferences.AppendChild($pinsNode)
[void]$settings.Preferences.AppendChild($detailsNode)
foreach ($path in @($resolved.LaunchPath) + $keepOld) { $node=$settings.CreateElement('string'); $node.InnerText=$path; [void]$pinsNode.AppendChild($node) }
foreach ($pin in $resolved) {
    $node = $settings.CreateElement('PinDetails')
    foreach ($field in @('LaunchPath','TargetPath','Title','IconPath','AppId')) {
        $property=$settings.CreateElement($field); $property.InnerText=[string]$pin.$field; [void]$node.AppendChild($property)
    }
    [void]$detailsNode.AppendChild($node)
}
foreach ($detail in $oldDetails) { if ($keepOld -contains [string]$detail.LaunchPath) { [void]$detailsNode.AppendChild($settings.ImportNode($detail,$true)) } }
$tempPath = $settingsPath + '.import.tmp'
$settings.Save($tempPath)
[IO.File]::Replace($tempPath,$settingsPath,[System.Management.Automation.Language.NullString]::Value)
[pscustomobject]@{Imported=$resolved.Count;PreservedCustom=$keepOld.Count;Backup=$backup;Settings=$settingsPath}
