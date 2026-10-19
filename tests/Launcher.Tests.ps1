[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$scriptPath = Join-Path $repository 'scripts\Start-Treba.ps1'
$tokens = $null; $parseErrors = $null
$syntax = [Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw ($parseErrors.Message -join '; ') }
# Import helpers without starting Tailscale, SSH or the application.
foreach ($definition in $syntax.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    . ([scriptblock]::Create($definition.Extent.Text))
}

function Assert-Launcher($Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
    Write-Host ('PASS ' + $Message)
}

$stateDirectory = Join-Path ([IO.Path]::GetTempPath()) ('treba-launcher-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stateDirectory | Out-Null
$launcherLog = Join-Path $stateDirectory 'launch.log'
$serverJob = $null
try {
    $routed = Get-DatabaseConnectionString 'Host=remote;Port=55432;Database=fixture;Username=fixture;Password="semi;colon";SSL Mode=Require' 55434
    $parsed = New-Object System.Data.Common.DbConnectionStringBuilder
    $parsed.set_ConnectionString($routed)
    Assert-Launcher ($parsed.get_Item('Host') -eq '127.0.0.1' -and $parsed.get_Item('Port') -eq '55434') 'Fallback port reaches the actual connection string'
    Assert-Launcher ($parsed.get_Item('Password') -eq 'semi;colon' -and $parsed.get_Item('SSL Mode') -eq 'Require' -and !$parsed.ContainsKey('ConnectionString')) 'Routing preserves credentials and SSL without adding a ConnectionString key'

    $occupied = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0)
    try {
        $occupied.Start()
        $occupiedPort = $occupied.LocalEndpoint.Port
        Assert-Launcher (!(Test-FreePort $occupiedPort)) 'Occupied loopback port is rejected'
        $available = Free-Port $occupiedPort
        Assert-Launcher ($available -ne $occupiedPort -and (Test-FreePort $available)) 'Port selection skips occupied ports'
    } finally { $occupied.Stop() }

    $serverJob = Start-Job -ArgumentList $available -ScriptBlock {
        param($port)
        $server = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, $port)
        try {
            $server.Start()
            foreach ($code in @(503,200)) {
                $client = $server.AcceptTcpClient()
                try {
                    $client.ReceiveTimeout = 5000
                    $stream = $client.GetStream()
                    $buffer = New-Object byte[] 4096
                    [void]$stream.Read($buffer, 0, $buffer.Length)
                    $reason = if ($code -eq 200) { 'OK' } else { 'Service Unavailable' }
                    $response = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 $code $reason`r`nContent-Length: 2`r`nConnection: close`r`n`r`nok")
                    $stream.Write($response, 0, $response.Length)
                    $code
                } finally { $client.Dispose() }
            }
        } finally { $server.Stop() }
    }
    Wait-Url ('http://127.0.0.1:' + $available + '/health') -Seconds 20
    Wait-Job $serverJob -Timeout 5 | Out-Null
    $statuses = @(Receive-Job $serverJob)
    Assert-Launcher (($statuses -join ',') -eq '503,200') 'Readiness retries an unavailable database until HTTP 200'
    Remove-Job $serverJob -Force; $serverJob = $null

    $child = Start-Process -FilePath (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList '-NoProfile -NonInteractive -Command "exit 23"' -WindowStyle Hidden -PassThru
    [void]$child.WaitForExit(5000)
    $elapsed = [Diagnostics.Stopwatch]::StartNew()
    $failure = ''
    try { Wait-Url 'http://127.0.0.1:1/health' -Seconds 20 -Process $child -OutputLog 'test-output.log' -ErrorLog 'test-error.log' }
    catch { $failure = $_.Exception.Message }
    Assert-Launcher ($failure -like '*exited with code 23*' -and $failure -like '*test-error.log*' -and $elapsed.Elapsed.TotalSeconds -lt 3) 'Exited API fails immediately with its exit code and log path'

    $failure = ''
    try { Wait-Url ('http://127.0.0.1:' + $available + '/health') -Seconds 1 -OutputLog 'test-output.log' -ErrorLog 'test-error.log' }
    catch { $failure = $_.Exception.Message }
    Assert-Launcher ($failure -like '*Last check:*' -and $failure -like '*test-output.log*') 'Readiness timeout retains the connection error and exact log path'
} finally {
    if ($serverJob) { Remove-Job $serverJob -Force }
    # Only the two known files in this run's temporary directory are removed.
    if (Test-Path -LiteralPath $launcherLog) { Remove-Item -LiteralPath $launcherLog }
    [IO.Directory]::Delete($stateDirectory)
}
