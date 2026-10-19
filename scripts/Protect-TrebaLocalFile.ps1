[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$Path)

$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath $Path)) { New-Item -ItemType File -Path $Path | Out-Null }
$file = Get-Item -LiteralPath $Path
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
# Load and persist only the DACL. Changing the owner or audit rules needs extra Windows privileges.
$acl = New-Object Security.AccessControl.FileSecurity($file.FullName, [Security.AccessControl.AccessControlSections]::Access)
$acl.SetAccessRuleProtection($true, $false)
foreach ($rule in @($acl.GetAccessRules($true, $false, [Security.Principal.SecurityIdentifier]))) {
    $acl.RemoveAccessRuleSpecific($rule)
}
$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($identity.User, 'FullControl', 'Allow')))
if ($PSVersionTable.PSEdition -eq 'Desktop') {
    [IO.File]::SetAccessControl($file.FullName, $acl)
} else {
    [IO.FileSystemAclExtensions]::SetAccessControl($file, $acl)
}
