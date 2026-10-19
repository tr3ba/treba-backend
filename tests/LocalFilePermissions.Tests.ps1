[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$protect = Join-Path (Split-Path -Parent $PSScriptRoot) 'scripts\Protect-TrebaLocalFile.ps1'
$directory = Join-Path ([IO.Path]::GetTempPath()) ('treba launcher permissions ' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory | Out-Null
$path = Join-Path $directory 'session fixture.json'
try {
    & $protect -Path $path
    Set-Content -LiteralPath $path -Value 'fixture content' -Encoding UTF8
    $owner = (Get-Acl -LiteralPath $path).Owner
    & icacls.exe $path /grant '*S-1-1-0:(R)' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not prepare the explicit read rule for the fixture.' }
    & $protect -Path $path
    & $protect -Path $path
    $acl = Get-Acl -LiteralPath $path
    $rules = @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
    if (!$acl.AreAccessRulesProtected -or $rules.Count -ne 1 -or $rules[0].IdentityReference -ne $sid -or
        $rules[0].AccessControlType -ne 'Allow' -or $rules[0].FileSystemRights -ne 'FullControl') {
        throw 'The launcher file must allow only the current user, without inherited or other explicit rules.'
    }
    if ($acl.Owner -ne $owner) { throw 'Protecting a file must not change its owner.' }
    if ((Get-Content -LiteralPath $path -Raw).Trim() -ne 'fixture content') { throw 'Protecting a file changed its content.' }
    Set-Content -LiteralPath $path -Value 'updated fixture' -Encoding UTF8
    if ((Get-Content -LiteralPath $path -Raw).Trim() -ne 'updated fixture') { throw 'Protected launcher state cannot be updated.' }
    Write-Host ('PASS launcher state protection on PowerShell ' + $PSVersionTable.PSVersion + ': current user only, owner and content preserved, repeated writes work')
} finally {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    [IO.Directory]::Delete($directory)
}
