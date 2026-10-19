[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$settings = Get-Content (Join-Path $env:LOCALAPPDATA 'Treba\launcher.json') -Raw | ConvertFrom-Json
$reportDirectory = Join-Path $env:LOCALAPPDATA 'Treba\deployment'
$reportPath = Join-Path $reportDirectory 'telegram-server-report.txt'
New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
$ssh = Join-Path $env:ProgramFiles 'Git\usr\bin\ssh.exe'
$remoteCheck = @'
printf 'SSH_USER='; id -un
for path in /ping /telegram/ /api/telegram/configuration; do
    printf 'DIRECT_API %s ' "$path"
    curl -s -o /dev/null -w '%{http_code}\n' --max-time 5 "http://127.0.0.1:5000$path"
done
printf 'PUBLIC_TELEGRAM_CONFIGURATION\n'
curl -fsS --max-time 5 http://127.0.0.1:5000/api/telegram/configuration || true
printf '\nPROXY_ROUTES\n'
for file in /etc/nginx/sites-enabled/* /etc/nginx/conf.d/*.conf; do
    if test -f "$file" && test -r "$file"; then
        printf 'FILE %s\n' "$file"
        grep -nE '^[[:space:]]*(listen|server_name|location|proxy_pass|root|include|return)[[:space:]]' "$file" || true
    fi
done
printf 'CONTAINER_ACCESS\n'
if command -v docker >/dev/null; then docker ps --format '{{.Names}} {{.Image}} {{.Ports}}' 2>&1; fi
printf 'SUDO_ACCESS\n'
sudo -n -l 2>&1 || true
'@
# Preserve Bash quoting and LF line endings when launched by Windows PowerShell 5.1.
$remotePayload = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($remoteCheck.Replace("`r`n", "`n")))
$remoteCommand = 'printf %s ' + $remotePayload + ' | base64 --decode | bash'
# Let SSH ask directly in this interactive console. No passphrase is stored or passed as an argument.
Write-Host 'Read-only hosting check. SSH may ask for the key passphrase in this window.'
$previousPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = 'Continue'
    $result = @(& $ssh -T -o BatchMode=no -o IdentityAgent=none -o IdentitiesOnly=yes -o StrictHostKeyChecking=yes -o PasswordAuthentication=no -o NumberOfPasswordPrompts=1 -o ConnectTimeout=10 -i $settings.SshKeyPath ($settings.SshUsername + '@' + $settings.SshHost) $remoteCommand 2>&1)
    $sshExit = $LASTEXITCODE
} finally { $ErrorActionPreference = $previousPreference }
$report = 'SSH_EXIT=' + $sshExit + "`r`n" + (($result | ForEach-Object { $_.ToString() }) -join "`r`n")
# Redact any URI credentials if an upstream happened to use them.
$report = [regex]::Replace($report, '(?i)(https?://)[^/\s@]+@', '$1[redacted]@')
Set-Content -LiteralPath $reportPath -Value $report -Encoding UTF8
Write-Host ('Report saved: ' + $reportPath)
if ($sshExit -ne 0) { throw 'SSH did not complete. The report contains the connection error.' }
Write-Host 'Read-only server check completed.' -ForegroundColor Green
