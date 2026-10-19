[CmdletBinding()]
param([switch]$NoBrowser, [switch]$NoPause)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$adminRoot = Join-Path $repository 'src\Rozetka-clone\AdminPanel'
$apiUrl = 'https://treba.duckdns.org/'
$stateDirectory = Join-Path $env:LOCALAPPDATA 'Treba\hosted-admin'
$stateFile = Join-Path $stateDirectory 'session.json'
New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
$mutex = New-Object Threading.Mutex($false, ('Local\TrebaHostedAdmin_' + $env:USERNAME))
$locked = $false

function File-Hash([string]$Path) {
    $stream = [IO.File]::OpenRead($Path); $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
    finally { $stream.Dispose(); $sha.Dispose() }
}
function Own-Panel($Session) {
    if (!$Session -or !$Session.Token -or !$Session.Port) { return $false }
    try {
        $status = Invoke-RestMethod -Uri ('http://127.0.0.1:' + $Session.Port + '/__local/status') -Headers @{'X-Treba-Launcher'=$Session.Token} -TimeoutSec 3
        return $status.launchId -eq $Session.SourceHash
    } catch { return $false }
}

try {
    try { $locked = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $locked = $true }
    if (!$locked) { Write-Host 'TREBA Admin Online is already starting.'; return }
    Write-Host '[TREBA] Checking the hosted API...'
    $ping = Invoke-RestMethod -Uri ($apiUrl + 'ping') -TimeoutSec 15
    if ($ping.status -ne 'ok') { throw 'The hosted Treba API is unavailable.' }
    $source = New-Object Text.StringBuilder
    $files = @(& git -C $repository ls-files --cached --others --exclude-standard -- src/Rozetka-clone/AdminPanel src/Rozetka-clone/Contracts src/Rozetka-clone/Shared) | Sort-Object -Unique
    if ($LASTEXITCODE -ne 0) { throw 'Unable to read the admin source files.' }
    foreach ($file in $files) { if (Test-Path -LiteralPath (Join-Path $repository $file)) { [void]$source.Append($file).Append((File-Hash (Join-Path $repository $file))) } }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $sourceHash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($source.ToString()))).Replace('-', '').Substring(0,16) }
    finally { $sha.Dispose() }
    $previous = if (Test-Path -LiteralPath $stateFile) { Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json } else { $null }
    $owned = Own-Panel $previous
    if ($owned -and $previous.SourceHash -eq $sourceHash -and $previous.ApiUrl -eq $apiUrl) {
        $url = 'http://localhost:' + $previous.Port + '/'
        Write-Host ('TREBA Admin Online is ready: ' + $url) -ForegroundColor Green
        if (!$NoBrowser) { Start-Process $url }
        return
    }

    Write-Host '[TREBA] Checking the admin build...'
    $build = Join-Path $stateDirectory ('builds\' + $sourceHash)
    $dll = Join-Path $build 'bin\AdminPanel\release\AdminPanel.dll'
    if (!(Test-Path -LiteralPath $dll)) {
        & dotnet build (Join-Path $adminRoot 'AdminPanel.csproj') -c Release --artifacts-path $build --nologo --verbosity quiet
        if ($LASTEXITCODE -ne 0) { throw 'The admin panel build failed.' }
    }
    if ($owned) { Invoke-RestMethod -Method Post -Uri ('http://127.0.0.1:' + $previous.Port + '/__local/stop') -Headers @{'X-Treba-Launcher'=$previous.Token} -TimeoutSec 5 | Out-Null }
    $port = 5220
    while ($port -lt 6220) {
        $listener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, $port)
        try { $listener.ExclusiveAddressUse = $true; $listener.Start(); break }
        catch [Net.Sockets.SocketException] { $port++ }
        finally { $listener.Stop() }
    }
    if ($port -ge 6220) { throw 'No free local admin port was found.' }
    $random = [Security.Cryptography.RandomNumberGenerator]::Create(); $bytes = New-Object byte[] 32
    try { $random.GetBytes($bytes); $token = [Convert]::ToBase64String($bytes) } finally { $random.Dispose() }
    $stdout = Join-Path $stateDirectory ('admin-' + $port + '-output.log')
    $stderr = Join-Path $stateDirectory ('admin-' + $port + '-error.log')
    $savedEnvironment = @{}
    foreach ($name in @('ASPNETCORE_ENVIRONMENT','DOTNET_ENVIRONMENT','LocalLaunch__Token','LocalLaunch__Id')) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
    try {
        $env:ASPNETCORE_ENVIRONMENT = 'Development'; $env:DOTNET_ENVIRONMENT = 'Development'
        $env:LocalLaunch__Token = $token; $env:LocalLaunch__Id = $sourceHash
        $process = Start-Process -FilePath (Get-Command dotnet.exe).Source -ArgumentList @(('"' + $dll + '"'),('--urls=http://127.0.0.1:' + $port),('--ApiBaseUrl=' + $apiUrl)) -WorkingDirectory $adminRoot -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    } finally { foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') } }
    @{SourceHash=$sourceHash;Token=$token;Port=$port;ApiUrl=$apiUrl;ProcessId=$process.Id} | ConvertTo-Json | Set-Content -LiteralPath $stateFile -Encoding UTF8
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $acl = New-Object Security.AccessControl.FileSecurity; $acl.SetOwner($identity.User); $acl.SetAccessRuleProtection($true,$false)
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($identity.User,'FullControl','Allow')))
    Set-Acl -LiteralPath $stateFile -AclObject $acl
    $deadline = (Get-Date).AddSeconds(60); $ready = $false
    do {
        $process.Refresh()
        if ($process.HasExited) { throw ('The admin panel exited. Logs: ' + $stateDirectory) }
        try { $response = Invoke-WebRequest -UseBasicParsing -Uri ('http://127.0.0.1:' + $port + '/auth/login') -TimeoutSec 3; $ready = [int]$response.StatusCode -eq 200 } catch { }
        if (!$ready) { Start-Sleep -Milliseconds 500 }
    } while (!$ready -and (Get-Date) -lt $deadline)
    if (!$ready) { throw ('The admin panel did not become ready. Logs: ' + $stateDirectory) }
    $url = 'http://localhost:' + $port + '/'
    Write-Host ('TREBA Admin Online is ready: ' + $url) -ForegroundColor Green
    Write-Host ('API: ' + $apiUrl)
    if (!$NoBrowser) { Start-Process $url }
} catch {
    if ($locked) { $mutex.ReleaseMutex(); $locked = $false }
    Write-Host ('TREBA Admin Online could not start: ' + $_.Exception.Message) -ForegroundColor Red
    if (!$NoPause) { Read-Host 'Press Enter to close' | Out-Null }
    exit 1
} finally { if ($locked) { $mutex.ReleaseMutex() }; $mutex.Dispose() }
