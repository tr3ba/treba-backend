[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$LocalSettings,
    [Parameter(Mandatory = $true)][string]$ApiSettings
)

$ErrorActionPreference = 'Stop'
$localPath = [IO.Path]::GetFullPath($LocalSettings)
if (Test-Path -LiteralPath $localPath) {
    $settings = Get-Content -LiteralPath $localPath -Raw | ConvertFrom-Json
} else {
    if (!(Test-Path -LiteralPath $ApiSettings)) { throw ('Missing API development settings: ' + $ApiSettings) }
    $api = Get-Content -LiteralPath $ApiSettings -Raw | ConvertFrom-Json
    $connection = $api.DevelopmentConnection
    $media = $api.ProductMedia
    $settings = [pscustomobject]@{
        SshHost = $(if ($connection.SshHost) { $connection.SshHost } else { $media.SshHost })
        SshUsername = $(if ($connection.SshUsername) { $connection.SshUsername } else { $media.SshUsername })
        SshKeyPath = $(if ($connection.SshKeyPath) { $connection.SshKeyPath } else { $media.SshKeyPath })
        DatabaseHost = $connection.DatabaseHost
        DatabasePort = $(if ($connection.DatabasePort) { $connection.DatabasePort } else { 55432 })
    }
}

foreach ($name in @('SshHost', 'SshUsername', 'SshKeyPath', 'DatabaseHost')) {
    if ([string]::IsNullOrWhiteSpace($settings.$name)) {
        throw ('Missing connection setting ' + $name + '. Set it in ' + $localPath + ' or DevelopmentConnection in ' + $ApiSettings)
    }
}
if ([int]$settings.DatabasePort -lt 1 -or [int]$settings.DatabasePort -gt 65535) { throw 'Invalid local database port.' }

if (!(Test-Path -LiteralPath $localPath)) {
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($localPath)) -Force | Out-Null
    $settings | ConvertTo-Json | Set-Content -LiteralPath $localPath -Encoding UTF8
    Write-Host ('[TREBA] Restored local connection settings: ' + $localPath)
}
$settings
