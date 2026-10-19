[CmdletBinding()]
param([string]$BotUsername, [uri]$MiniAppUrl, [Security.SecureString]$BotToken, [switch]$Enable)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$apiDirectory = Join-Path $repository 'src\Rozetka-clone\WebApi'
$apiSettingsPath = Join-Path $apiDirectory 'appsettings.Development.json'
if (!(Test-Path -LiteralPath $apiSettingsPath)) { throw 'Configure the local API database connection first.' }
$apiSettings = Get-Content -LiteralPath $apiSettingsPath -Raw | ConvertFrom-Json
if (!$BotUsername) { $BotUsername = $apiSettings.Telegram.BotUsername }
if (!$MiniAppUrl -and $apiSettings.Telegram.MiniAppUrl) { $MiniAppUrl = [uri]$apiSettings.Telegram.MiniAppUrl }
if (!$BotUsername) { throw 'Supply the bot username from BotFather.' }
$BotUsername = $BotUsername.TrimStart('@')
if ($BotUsername -notmatch '^[A-Za-z0-9_]{2,29}bot$') { throw 'Supply the bot username from BotFather, without @.' }
if (!$MiniAppUrl -or $MiniAppUrl.Scheme -ne 'https' -or !$MiniAppUrl.IsDefaultPort -or $MiniAppUrl.UserInfo -or $MiniAppUrl.Query -or $MiniAppUrl.Fragment -or !$MiniAppUrl.AbsolutePath.EndsWith('/telegram/')) {
    throw 'MiniAppUrl must be the HTTPS address of this API ending in /telegram/, without query parameters.'
}
[xml]$project = Get-Content -LiteralPath (Join-Path $apiDirectory 'WebApi.csproj') -Raw
$secretsId = @($project.Project.PropertyGroup.UserSecretsId | Where-Object { $_ })[0]
if (!$secretsId) { throw 'The API project does not define a UserSecretsId.' }
$secretDirectory = Join-Path $env:APPDATA ('Microsoft\UserSecrets\' + $secretsId)
$secretPath = Join-Path $secretDirectory 'secrets.json'
$secrets = if (Test-Path -LiteralPath $secretPath) { Get-Content -LiteralPath $secretPath -Raw | ConvertFrom-Json } else { [pscustomobject]@{} }
if (!$BotToken -and !$secrets.'Telegram:BotToken') { $BotToken = Read-Host 'BotFather token (stored only in local .NET user secrets)' -AsSecureString }
if ($BotToken) {
    $secretPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($BotToken)
    try {
        $plainToken = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($secretPointer)
        if ([string]::IsNullOrWhiteSpace($plainToken)) { throw 'The bot token must not be empty.' }
        $secrets | Add-Member -NotePropertyName 'Telegram:BotToken' -NotePropertyValue $plainToken -Force
        New-Item -ItemType Directory -Path $secretDirectory -Force | Out-Null
        $secrets | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $secretPath -Encoding UTF8
    } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($secretPointer); $plainToken = $null }
}
$telegramSettings = [pscustomobject]@{ Enabled = [bool]$Enable; LongPollingEnabled = [bool]$Enable; BotUsername = $BotUsername; MiniAppUrl = $MiniAppUrl.AbsoluteUri; InitDataMaxAgeMinutes = 30; EnforceSellerCategories = $true }
$apiSettings | Add-Member -NotePropertyName Telegram -NotePropertyValue $telegramSettings -Force
$apiSettings | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $apiSettingsPath -Encoding UTF8
Write-Host ('Telegram settings saved for @' + $BotUsername + '. The token is not stored in the repository.')
if ($Enable) { Write-Host 'Telegram is enabled. Start TREBA Admin to launch the API and bot worker.' }
else { Write-Host 'Integration is prepared but disabled. Apply the Telegram SQL migration, configure the Mini App in BotFather, then rerun this script with -Enable.' }
