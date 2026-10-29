[CmdletBinding()]
param(
    [string]$LocalSettings = (Join-Path $env:LOCALAPPDATA 'Treba\launcher.json'),
    [switch]$LocalApi,
    [switch]$NoBrowser,
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'
if (!$LocalApi) {
    & (Join-Path $PSScriptRoot 'Start-TrebaHostedAdmin.ps1') -NoBrowser:$NoBrowser -NoPause:$NoPause
    return
}
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$stateDirectory = Join-Path $env:LOCALAPPDATA 'Treba\launcher'
$agentFile = Join-Path $env:LOCALAPPDATA 'Treba\dev-connection\ssh-agent.json'
New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
$runId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $PID
$launcherLog = Join-Path $stateDirectory ('launch-' + $runId + '.log')
$appLogs = @{}
$mutex = New-Object Threading.Mutex($false, ('Local\TrebaLauncher_' + $env:USERNAME))
$locked = $false

function Write-LaunchLog([string]$Message) {
    Add-Content -LiteralPath $launcherLog -Value ((Get-Date -Format 'o') + ' ' + $Message) -Encoding UTF8
}
function Write-Step([string]$Message) { Write-LaunchLog $Message; Write-Host ('[TREBA] ' + $Message) -ForegroundColor Cyan }
function Get-SourceHash([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '') }
    finally { $algorithm.Dispose(); $stream.Dispose() }
}
function Test-Port([int]$Port) { return [bool](Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue) }
function Test-FreePort([int]$Port) {
    # Windows can reserve a port even when no process is listening on it.
    $listener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, $Port)
    try { $listener.ExclusiveAddressUse = $true; $listener.Start(); return $true }
    catch [Net.Sockets.SocketException] { return $false }
    finally { $listener.Stop() }
}
function Free-Port([int]$Start) {
    for ($port = $Start; $port -le [Math]::Min(65535, $Start + 1000); $port++) { if (Test-FreePort $port) { return $port } }
    throw 'No free local port was found.'
}
function Get-DatabaseConnectionString([string]$Value, [int]$Port) {
    $builder = New-Object System.Data.Common.DbConnectionStringBuilder
    # Explicit accessors avoid PowerShell's IDictionary adapter treating ConnectionString as a key.
    $builder.set_ConnectionString($Value)
    $builder.set_Item('Host', '127.0.0.1')
    $builder.set_Item('Port', $Port)
    return $builder.get_ConnectionString()
}
function Wait-Url([string]$Url, [int]$Seconds = 90, [Diagnostics.Process]$Process, [string]$OutputLog, [string]$ErrorLog) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    $lastResult = 'No response received.'
    do {
        if ($Process) {
            $Process.Refresh()
            if ($Process.HasExited) {
                throw ('Service process ' + $Process.Id + ' exited with code ' + $Process.ExitCode + ' before becoming ready: ' + $Url + '. Output: ' + $OutputLog + '. Errors: ' + $ErrorLog)
            }
        }
        try {
            $requestTimeout = [Math]::Max(1, [Math]::Min(10, [int][Math]::Ceiling(($deadline - (Get-Date)).TotalSeconds)))
            $response = Invoke-WebRequest -Uri $Url -TimeoutSec $requestTimeout -UseBasicParsing
            if ([int]$response.StatusCode -eq 200) { Write-LaunchLog ('Ready: ' + $Url); return }
            $lastResult = 'HTTP ' + [int]$response.StatusCode
        } catch {
            $lastResult = if ($_.Exception.Response) { 'HTTP ' + [int]$_.Exception.Response.StatusCode } else { $_.Exception.Message }
        }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw ('Service did not become ready within ' + $Seconds + ' seconds: ' + $Url + '. Last check: ' + $lastResult + '. Output: ' + $OutputLog + '. Errors: ' + $ErrorLog)
}
function Protect-File([string]$Path) {
    & (Join-Path $PSScriptRoot 'Protect-TrebaLocalFile.ps1') -Path $Path
}
function Save-State($Value, [string]$Path) {
    $newFile = !(Test-Path -LiteralPath $Path)
    $Value | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $Path -Encoding UTF8
    if ($newFile) { Protect-File $Path }
}
function New-ControlToken {
    $bytes = New-Object byte[] 32
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $random.GetBytes($bytes) } finally { $random.Dispose() }
    return [Convert]::ToBase64String($bytes)
}
function Get-AgentKeys([string]$SshAdd) {
    # Exit 1 means an empty agent; exit 2 means a stale/unavailable agent.
    # Windows PowerShell otherwise turns the expected native stderr into a terminating error.
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $keys = @(& $SshAdd -L 2>$null)
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previousPreference }
    return [pscustomobject]@{ Ready = $code -in @(0,1); PublicKeys = ($keys -join "`n") }
}
function Own-Service($Previous, [int]$Port) {
    if (!$Previous -or !$Port) { return $false }
    try {
        $status = Invoke-RestMethod -Uri ('http://127.0.0.1:' + $Port + '/__local/status') -Headers @{'X-Treba-Launcher'=$Previous.Token} -TimeoutSec 3
        return $status.launchId -eq $Previous.SourceHash
    } catch { return $false }
}
function Test-ExistingTunnel([int]$Port, $Settings) {
    try {
        $listener = Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue | Select-Object -First 1
        if (!$listener) { return $false }
        $owner = Get-CimInstance Win32_Process -Filter ('ProcessId=' + $listener.OwningProcess)
        if ($owner.Name -ne 'ssh.exe' -or $owner.CommandLine -notlike ('*' + $Settings.DatabaseHost + '*') -or $owner.CommandLine -notlike ('*' + $Settings.SshHost + '*')) { return $false }
        return & (Join-Path $PSScriptRoot 'Test-TrebaDatabaseTunnel.ps1') -Port $Port
    } catch { return $false }
}
function Start-App([string]$Dll, [string]$WorkingDirectory, [string[]]$Arguments, [string]$Name) {
    if (!(Test-Path -LiteralPath $Dll -PathType Leaf)) { throw ('Application build is missing: ' + $Dll + '. Start TREBA again to rebuild it.') }
    $stdout = Join-Path $stateDirectory ($Name + '-' + $runId + '-output.log')
    $stderr = Join-Path $stateDirectory ($Name + '-' + $runId + '-error.log')
    $appLogs[$Name] = @{ Output = $stdout; Error = $stderr }
    $argumentsList = @('exec', ('"' + $Dll + '"')) + $Arguments
    $process = Start-Process -FilePath (Get-Command dotnet.exe).Source -ArgumentList $argumentsList -WorkingDirectory $WorkingDirectory -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    # Keep a process handle before Refresh; Windows PowerShell otherwise loses the exit code.
    $null = $process.Handle
    Write-LaunchLog ('Started ' + $Name + ': pid=' + $process.Id + ', dll=' + $Dll + ', output=' + $stdout + ', errors=' + $stderr)
    return $process
}

try {
    try { $locked = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $locked = $true }
    if (!$locked) { Write-Host 'TREBA is already starting. Please wait.'; exit 0 }
    Write-LaunchLog ('Launcher: PowerShell ' + $PSVersionTable.PSVersion + ', repository=' + $repository + ', settings=' + $LocalSettings)
    $apiRoot = Join-Path $repository 'src\Rozetka-clone\WebApi'
    $apiSettings = Join-Path $apiRoot 'appsettings.Development.json'
    $settings = & (Join-Path $PSScriptRoot 'Get-TrebaLocalSettings.ps1') -LocalSettings $LocalSettings -ApiSettings $apiSettings
    $apiConfiguration = Get-Content -LiteralPath $apiSettings -Raw | ConvertFrom-Json
    $stateFile = Join-Path $stateDirectory 'session.json'
    $previous = $null
    if (Test-Path -LiteralPath $stateFile) { $previous = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json }

    Write-Step 'Connecting Tailscale...'
    $tailscale = Join-Path $env:ProgramFiles 'Tailscale\tailscale.exe'
    if (!(Get-Process -Name tailscale-ipn -ErrorAction SilentlyContinue)) { Start-Process -FilePath (Join-Path $env:ProgramFiles 'Tailscale\tailscale-ipn.exe') -WindowStyle Hidden }
    & $tailscale up --timeout=20s
    $network = (& $tailscale status --json 2>$null) | ConvertFrom-Json
    if ($network.BackendState -ne 'Running') { throw 'Sign in to your Treba Marketplace account in Tailscale and try again.' }

    Write-Step 'Opening the database tunnel...'
    $requestedPort = [int]$settings.DatabasePort
    Write-LaunchLog ('Requested database port: ' + $requestedPort)
    $databasePort = $requestedPort
    $sshLog = Join-Path $stateDirectory 'ssh-error.log'
    $databaseReady = $false
    foreach ($candidate in @($requestedPort, [int]$previous.DatabasePort, [int]$apiConfiguration.DevelopmentConnection.DatabasePort) | Select-Object -Unique) {
        if ($candidate -gt 0 -and (Test-ExistingTunnel $candidate $settings)) { $databasePort = $candidate; $databaseReady = $true; break }
    }
    $gitBin = Join-Path $env:ProgramFiles 'Git\usr\bin'
    if (!$databaseReady) {
        Write-Step 'Preparing the SSH key...'
        $agentReady = $false
        if (Test-Path -LiteralPath $agentFile) {
            $savedAgent = Get-Content -LiteralPath $agentFile -Raw | ConvertFrom-Json
            $env:SSH_AUTH_SOCK = $savedAgent.SSH_AUTH_SOCK; $env:SSH_AGENT_PID = $savedAgent.SSH_AGENT_PID
            $agentReady = (Get-AgentKeys "$gitBin\ssh-add.exe").Ready
        }
        if (!$agentReady) {
            $agentOutput = (& "$gitBin\ssh-agent.exe" -s) -join "`n"
            if ($agentOutput -notmatch 'SSH_AUTH_SOCK=([^;]+)') { throw 'Could not start the SSH agent.' }; $env:SSH_AUTH_SOCK = $Matches[1]
            if ($agentOutput -notmatch 'SSH_AGENT_PID=([^;]+)') { throw 'Could not read the SSH agent process.' }; $env:SSH_AGENT_PID = $Matches[1]
            New-Item -ItemType Directory -Path (Split-Path -Parent $agentFile) -Force | Out-Null
            Save-State @{SSH_AUTH_SOCK=$env:SSH_AUTH_SOCK;SSH_AGENT_PID=$env:SSH_AGENT_PID} $agentFile
        }
        $publicKey = ((Get-Content -LiteralPath ($settings.SshKeyPath + '.pub') -Raw).Trim() -split '\s+')[0..1] -join ' '
        $loadedKeys = (Get-AgentKeys "$gitBin\ssh-add.exe").PublicKeys
        if (!$loadedKeys.Contains($publicKey)) {
            Write-Host 'Enter the SSH key passphrase once. It will be kept in memory for up to 8 hours.' -ForegroundColor Yellow
            & "$gitBin\ssh-add.exe" -t 8h $settings.SshKeyPath
            if ($LASTEXITCODE -ne 0) { throw 'SSH key was not unlocked.' }
        }
    }
    if (!$databaseReady) { $databasePort = Free-Port $requestedPort }
    if ($databasePort -ne $requestedPort) { Write-Step ('Local port ' + $requestedPort + ' is unavailable; using ' + $databasePort + ' for the database.') }
    for ($attempt = 1; !$databaseReady -and $attempt -le 3; $attempt++) {
        if (!(Test-Port $databasePort)) {
            $sshArguments = @('-N','-T','-o','BatchMode=yes','-o','IdentitiesOnly=yes','-o','StrictHostKeyChecking=yes','-o','ConnectTimeout=10','-o','ExitOnForwardFailure=yes','-o','ServerAliveInterval=30','-o','ServerAliveCountMax=3','-L',('127.0.0.1:' + $databasePort + ':' + $settings.DatabaseHost + ':5432'),'-i',('"' + $settings.SshKeyPath + '"'),($settings.SshUsername + '@' + $settings.SshHost))
            $tunnel = Start-Process -FilePath "$gitBin\ssh.exe" -ArgumentList $sshArguments -WindowStyle Hidden -RedirectStandardError $sshLog -PassThru
            $deadline = (Get-Date).AddSeconds(15)
            while (!(Test-Port $databasePort) -and (Get-Date) -lt $deadline -and !$tunnel.HasExited) { Start-Sleep -Milliseconds 500; $tunnel.Refresh() }
        }
        if (Test-Port $databasePort) {
            $listener = Get-NetTCPConnection -State Listen -LocalPort $databasePort | Select-Object -First 1
            $owner = Get-CimInstance Win32_Process -Filter ('ProcessId=' + $listener.OwningProcess)
            if ($owner.Name -ne 'ssh.exe' -or $owner.CommandLine -notlike ('*' + $settings.DatabaseHost + '*')) { throw 'The database port is occupied by another application.' }
            $databaseReady = & (Join-Path $PSScriptRoot 'Test-TrebaDatabaseTunnel.ps1') -Port $databasePort
            if ($databaseReady) { break }
        }
        $details = if (Test-Path -LiteralPath $sshLog) { (Get-Content -LiteralPath $sshLog -Tail 4) -join ' ' } else { '' }
        if ($details -match 'Permission denied \(publickey|Authentication failed|Host key verification failed|REMOTE HOST IDENTIFICATION HAS CHANGED|UNPROTECTED PRIVATE KEY') {
            throw ('SSH authentication or host verification failed: ' + $details)
        }
        if ($attempt -lt 3) { Write-Step ('Connection interrupted or database not ready; retrying (' + ($attempt + 1) + '/3)...'); Start-Sleep -Seconds $attempt }
    }
    if (!$databaseReady) { throw ('Database connection failed after 3 attempts. ' + $details + ' Check ' + $sshLog) }
    Write-LaunchLog ('PostgreSQL SSL is ready on port ' + $databasePort)
    if ($databasePort -ne $requestedPort) {
        $settings.DatabasePort = $databasePort
        $settings | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $LocalSettings -Encoding UTF8
    }
    $routedConnection = Get-DatabaseConnectionString $apiConfiguration.ConnectionStrings.DefaultConnection $databasePort
    if ($apiConfiguration.ConnectionStrings.DefaultConnection -ne $routedConnection -or
        ($apiConfiguration.DevelopmentConnection -and $apiConfiguration.DevelopmentConnection.DatabasePort -ne $databasePort)) {
        $apiConfiguration.ConnectionStrings.DefaultConnection = $routedConnection
        if ($apiConfiguration.DevelopmentConnection) { $apiConfiguration.DevelopmentConnection.DatabasePort = $databasePort }
        $apiConfiguration | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $apiSettings -Encoding UTF8
    }

    Write-Step 'Checking the application build...'
    Push-Location $repository
    try {
        $files = @(& git ls-files --cached --others --exclude-standard -- src/Rozetka-clone) | Sort-Object -Unique
        $fingerprint = New-Object Text.StringBuilder
        foreach ($file in $files) {
            if ([IO.Path]::GetExtension($file) -in @('.cs','.razor','.csproj','.css','.js','.html','.svg','.json','.resx','.props','.targets')) {
                [void]$fingerprint.Append($file).Append((Get-SourceHash (Join-Path $repository $file)))
            }
        }
        foreach ($configurationFile in @((Join-Path $apiRoot 'appsettings.Development.json'), $LocalSettings)) {
            if (Test-Path -LiteralPath $configurationFile) { [void]$fingerprint.Append((Get-SourceHash $configurationFile)) }
        }
        [xml]$apiProject = Get-Content -LiteralPath (Join-Path $apiRoot 'WebApi.csproj') -Raw
        $secretsId = @($apiProject.Project.PropertyGroup.UserSecretsId | Where-Object { $_ })[0]
        if ($secretsId) {
            $secretsFile = Join-Path $env:APPDATA ('Microsoft\UserSecrets\' + $secretsId + '\secrets.json')
            if (Test-Path -LiteralPath $secretsFile) { [void]$fingerprint.Append((Get-SourceHash $secretsFile)) }
        }
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $sourceHash = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($fingerprint.ToString())))).Replace('-','').Substring(0,16) } finally { $sha.Dispose() }
        $build = Join-Path $stateDirectory ('builds\' + $sourceHash)
        $apiDll = Join-Path $build 'bin\WebApi\debug\WebApi.dll'
        if (!(Test-Path -LiteralPath $apiDll)) {
            & dotnet build (Join-Path $apiRoot 'WebApi.csproj') --artifacts-path $build --nologo --verbosity quiet
            if ($LASTEXITCODE -ne 0) { throw 'API build failed.' }
        }
    } finally { Pop-Location }

    $apiOwned = Own-Service $previous $previous.ApiPort
    Write-LaunchLog ('Previous API: ' + $apiOwned)
    if ($previous -and $previous.SourceHash -eq $sourceHash -and $previous.DatabasePort -eq $databasePort -and $apiOwned) {
        Wait-Url ('http://127.0.0.1:' + $previous.ApiPort + '/health') -OutputLog $previous.ApiOutputLog -ErrorLog $previous.ApiErrorLog
        $url = & (Join-Path (Split-Path $repository -Parent) 'treba-frontend\scripts\start-workspace.ps1') -ApiUrl ('http://127.0.0.1:' + $previous.ApiPort)
        Write-Host ('TREBA is ready: ' + $url) -ForegroundColor Green
        if (!$NoBrowser) { Start-Process $url }
        return
    }

    # Only instances started by this launcher can be stopped; IDE sessions are left alone.
    foreach ($port in @($previous.ApiPort, $previous.AdminPort)) {
        if (Own-Service $previous $port) {
            Invoke-RestMethod -Method Post -Uri ('http://127.0.0.1:' + $port + '/__local/stop') -Headers @{'X-Treba-Launcher'=$previous.Token} -TimeoutSec 5 | Out-Null
            $deadline = (Get-Date).AddSeconds(10)
            while ((Test-Port $port) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }
        }
    }
    $apiPort = Free-Port 5290
    Write-LaunchLog ('Selected ports: database=' + $databasePort + ', api=' + $apiPort)
    $token = New-ControlToken
    $env:ASPNETCORE_ENVIRONMENT = 'Development'; $env:DOTNET_ENVIRONMENT = 'Development'
    $env:LocalLaunch__Token = $token; $env:LocalLaunch__Id = $sourceHash
    $env:ConnectionStrings__DefaultConnection = $routedConnection
    Write-Step 'Starting the API...'
    $apiProcess = Start-App $apiDll $apiRoot @(('--urls=http://127.0.0.1:' + $apiPort),'--ApplyMigrations=false','--BootstrapAdmin:Email=') 'api'
    $launchState = @{SourceHash=$sourceHash;Token=$token;DatabasePort=$databasePort;ApiPort=$apiPort;AdminPort=0;ApiPid=$apiProcess.Id;AdminPid=0;LauncherLog=$launcherLog;ApiOutputLog=$appLogs.api.Output;ApiErrorLog=$appLogs.api.Error}
    Save-State $launchState $stateFile
    Wait-Url ('http://127.0.0.1:' + $apiPort + '/health') -Process $apiProcess -OutputLog $appLogs.api.Output -ErrorLog $appLogs.api.Error
    Write-Step 'Starting the frontend workspace...'
    $url = & (Join-Path (Split-Path $repository -Parent) 'treba-frontend\scripts\start-workspace.ps1') -ApiUrl ('http://127.0.0.1:' + $apiPort)
    Write-Host ('TREBA is ready: ' + $url) -ForegroundColor Green
    if (!$NoBrowser) { Start-Process $url }
} catch {
    # An error window waiting for Enter must not block the next launch.
    if ($locked) { $mutex.ReleaseMutex(); $locked = $false }
    Write-LaunchLog ('Failed: ' + $_.Exception.Message)
    Write-Host ('TREBA could not start: ' + $_.Exception.Message) -ForegroundColor Red
    Write-Host ('Logs: ' + $stateDirectory)
    Write-Host ('Launch details: ' + $launcherLog)
    if (!$NoPause) { Read-Host 'Press Enter to close' | Out-Null }
    exit 1
} finally {
    Remove-Item Env:LocalLaunch__Token -ErrorAction SilentlyContinue
    Remove-Item Env:ConnectionStrings__DefaultConnection -ErrorAction SilentlyContinue
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
