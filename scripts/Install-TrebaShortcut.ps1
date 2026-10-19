[CmdletBinding()]
param([string]$Name = 'TREBA Admin')
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$localSettings = Join-Path $env:LOCALAPPDATA 'Treba\launcher.json'
& (Join-Path $PSScriptRoot 'Get-TrebaLocalSettings.ps1') -LocalSettings $localSettings -ApiSettings (Join-Path $repository 'src\Rozetka-clone\WebApi\appsettings.Development.json') | Out-Null
$desktop = [Environment]::GetFolderPath('Desktop')
$path = Join-Path $desktop ($Name + '.lnk')
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($path)
$shortcut.TargetPath = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$shortcut.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $repository 'scripts\Start-Treba.ps1') + '" -LocalSettings "' + $localSettings + '"'
$shortcut.WorkingDirectory = $repository
$shortcut.IconLocation = Join-Path $repository 'src\Rozetka-clone\AdminPanel\wwwroot\favicon.ico'
$shortcut.Description = 'Start TREBA: Tailscale, database connection, API and admin panel'
$shortcut.WindowStyle = 1
$shortcut.Save()
Write-Output $path
