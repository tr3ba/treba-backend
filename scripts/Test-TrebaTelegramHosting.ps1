[CmdletBinding()]
param(
    [uri]$MiniAppUrl = 'https://treba.duckdns.org/telegram/',
    [string]$BotUsername = 'treba_marketplace_bot'
)

$ErrorActionPreference = 'Stop'
if (!$MiniAppUrl.IsAbsoluteUri -or $MiniAppUrl.Scheme -ne 'https' -or !$MiniAppUrl.IsDefaultPort -or $MiniAppUrl.UserInfo -or $MiniAppUrl.Query -or $MiniAppUrl.Fragment -or !$MiniAppUrl.AbsolutePath.EndsWith('/telegram/')) {
    throw 'Use the full HTTPS Mini App URL ending in /telegram/. Do not append a message timestamp, query or fragment.'
}
$origin = $MiniAppUrl.GetLeftPart([UriPartial]::Authority)
function Get-HostingProbe([string]$Url) {
    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri $Url -MaximumRedirection 5 -TimeoutSec 15
        return [pscustomobject]@{ Url = $Url; Status = [int]$response.StatusCode; Body = [string]$response.Content; ContentType = ($response.Headers['Content-Type'] -join ',') }
    } catch {
        $status = 0
        if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
        return [pscustomobject]@{ Url = $Url; Status = $status; Body = ''; ContentType = '' }
    }
}

$page = Get-HostingProbe $MiniAppUrl.AbsoluteUri
$configuration = Get-HostingProbe ($origin + '/api/telegram/configuration')
$isMiniApp = $page.Status -eq 200 -and $page.Body.Contains('telegram-web-app.js') -and $page.Body.Contains('app-shell')
$checks = @([pscustomobject]@{ Check = 'Mini App HTML'; Status = $page.Status; Passed = $isMiniApp; Url = $page.Url })
foreach ($asset in @('app.js', 'ui.js', 'mini.css', 'brand-mark.png', '../js/editor-drafts.js')) {
    $probe = Get-HostingProbe ([uri]::new($MiniAppUrl, $asset).AbsoluteUri)
    $valid = $probe.Status -eq 200
    if ($asset.EndsWith('.png')) { $valid = $valid -and $probe.ContentType -like 'image/png*' }
    # A storefront fallback can return HTTP 200 instead of a missing asset.
    if ($asset.EndsWith('.js') -or $asset.EndsWith('.css')) { $valid = $valid -and $probe.Body -notmatch '(?is)^\s*(?:<!doctype html|<html)' }
    $checks += [pscustomobject]@{ Check = $asset; Status = $probe.Status; Passed = $valid; Url = $probe.Url }
}
$configurationFound = $false; $configured = $false; $usernameMatches = $false; $urlMatches = $false
if ($configuration.Status -eq 200) {
    try {
        $settings = $configuration.Body | ConvertFrom-Json
        $configurationFound = $null -ne $settings.configured
        $configured = $configurationFound -and [bool]$settings.configured
        $usernameMatches = $settings.botUsername -eq $BotUsername.TrimStart('@')
        $urlMatches = $settings.miniAppUrl -eq $MiniAppUrl.AbsoluteUri
    } catch { $configurationFound = $false }
}
$checks += [pscustomobject]@{ Check = 'Telegram API'; Status = $configuration.Status; Passed = $configurationFound; Url = $configuration.Url }
$checks | Format-Table -AutoSize | Out-Host
$hostingReady = @($checks | Where-Object { !$_.Passed }).Count -eq 0
if (!$hostingReady) {
    Write-Host 'NOT READY: publish the updated WebApi and route /telegram/ and /api/telegram/ to that API. The bot must not point to the storefront root.' -ForegroundColor Yellow
    Write-Output ([pscustomobject]@{ HostingReady = $false; BotConfigured = $configured; BotUsernameMatches = $usernameMatches; MiniAppUrlMatches = $urlMatches })
    exit 1
}
if (!$configured -or !$usernameMatches -or !$urlMatches) {
    Write-Host 'HOSTING READY; BOT SETTINGS INCOMPLETE: check Telegram__Enabled, BotUsername, MiniAppUrl and the secret token on the server.' -ForegroundColor Yellow
    Write-Output ([pscustomobject]@{ HostingReady = $true; BotConfigured = $configured; BotUsernameMatches = $usernameMatches; MiniAppUrlMatches = $urlMatches })
    exit 2
}
Write-Host 'READY: hosting and server bot settings match. Check both the Main Mini App URL and the chat Menu Button URL in BotFather.' -ForegroundColor Green
Write-Output ([pscustomobject]@{ HostingReady = $true; BotConfigured = $true; BotUsernameMatches = $true; MiniAppUrlMatches = $true })
