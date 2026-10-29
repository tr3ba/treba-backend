[CmdletBinding()]
param([switch]$NoBrowser, [switch]$NoPause)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
try {
    $url = & (Join-Path (Split-Path $repository -Parent) 'treba-frontend\scripts\start-workspace.ps1') -ApiUrl 'https://treba.duckdns.org'
    Write-Host ('TREBA workspace is ready: ' + $url) -ForegroundColor Green
    if (!$NoBrowser) { Start-Process $url }
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    if (!$NoPause) { Read-Host 'Press Enter to close' | Out-Null }
    exit 1
}
