[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$settingsPath = Join-Path $env:LOCALAPPDATA 'Treba\launcher.json'
$agentPath = Join-Path $env:LOCALAPPDATA 'Treba\dev-connection\ssh-agent.json'
if (!(Test-Path -LiteralPath $settingsPath) -or !(Test-Path -LiteralPath $agentPath)) {
    throw 'Start TREBA Admin once before unlocking its SSH key.'
}
$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
$agent = Get-Content -LiteralPath $agentPath -Raw | ConvertFrom-Json
$env:SSH_AUTH_SOCK = $agent.SSH_AUTH_SOCK
$env:SSH_AGENT_PID = $agent.SSH_AGENT_PID
$sshAdd = Join-Path $env:ProgramFiles 'Git\usr\bin\ssh-add.exe'
Write-Host 'Unlock the existing TREBA SSH key. Enter the passphrase here; typing will be hidden.'
& $sshAdd -t 8h $settings.SshKeyPath
if ($LASTEXITCODE -ne 0) { throw 'The SSH key was not unlocked.' }
$expectedKey = ((Get-Content -LiteralPath ($settings.SshKeyPath + '.pub') -Raw).Trim() -split '\s+')[0..1] -join ' '
$loadedKeys = (& $sshAdd -L) -join "`n"
if ($LASTEXITCODE -ne 0 -or !$loadedKeys.Contains($expectedKey)) {
    throw 'The key is not available in the TREBA SSH agent.'
}
Write-Host 'TREBA SSH key is available for up to 8 hours.' -ForegroundColor Green
