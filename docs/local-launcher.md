# Local TREBA workspace

Run `scripts/Install-TrebaShortcut.ps1` to place **TREBA Admin** on the Windows desktop.
The shortcut runs `scripts/Start-Treba.ps1` from this checkout.

The launcher connects Tailscale, unlocks the configured SSH key through the Git for Windows SSH agent,
opens the PostgreSQL tunnel, builds the current source into an isolated directory, starts WebApi and
AdminPanel in the background, checks their health and opens the browser. The first unlock requires the
SSH passphrase; it stays in the agent's memory for up to eight hours and is never written by this script.
After a Windows restart or expiry, the key must be unlocked again.

Repeated clicks reuse healthy instances of the current build. After source/configuration changes, the
launcher stops only its own instances through authenticated loopback-only development endpoints.
Processes started in an IDE are not stopped. Default launcher ports are API `5290` and AdminPanel `5218`;
the next free port is selected when occupied. The database tunnel defaults to `55432`.

Local, untracked configuration:

- `%LOCALAPPDATA%/Treba/launcher.json`: `SshHost`, `SshUsername`, `SshKeyPath`, `DatabaseHost`, `DatabasePort`.
- `WebApi/appsettings.Development.json`: database credentials, local JWT/security configuration and `ProductMedia`.
- `%LOCALAPPDATA%/Treba/dev-connection/ssh-agent.json`: SSH agent socket metadata, without the key/passphrase.
- `%LOCALAPPDATA%/Treba/launcher/session.json`: local process/port metadata and the generated launcher control token,
  restricted to the current Windows user.

Logs and isolated builds are under `%LOCALAPPDATA%/Treba/launcher`.
Each launch writes `launch-<timestamp>-<pid>.log` with its settings path, selected ports and child
process ids. API and admin output use the same run id, so a failed start cannot overwrite the logs of
an already running instance. `session.json` records the exact log paths without recording database
credentials or the SSH passphrase.
Readiness allows up to 90 seconds and checks the database through `/health`. If a child exits early,
the launcher reports its exit code immediately; timeouts include the last HTTP/connection error.
For a terminal smoke check, run `scripts/Start-Treba.ps1 -NoBrowser -NoPause`.
Run `tests/Launcher.Tests.ps1` in Windows PowerShell to check port routing, occupied-port fallback,
HTTP readiness retries, early process exit and timeout diagnostics without accessing the shared server.

Keep a `DevelopmentConnection` section in the untracked API development settings with `SshHost`,
`SshUsername`, `SshKeyPath`, `DatabaseHost` (the remote RDS hostname) and `DatabasePort` (the local tunnel
port). The installer and launcher recreate a missing `launcher.json` from these non-secret connection
settings. SSH values can also come from `ProductMedia`. Existing launcher settings take precedence.
An error window releases the startup lock before waiting for Enter, allowing another launch.
SSH resets are retried up to three times. Before starting or reusing the apps, the launcher checks that
PostgreSQL accepts SSL through the tunnel; an open local port alone is not treated as a working database.
Authentication and host-key failures stop immediately with the SSH error instead of repeated retries.
Port selection tests an actual loopback bind, including Windows reserved port ranges. If the database
port is unavailable, the launcher selects a free port and updates the local launcher and API settings.
Healthy existing tunnels can be reused; unrelated processes are never stopped to free a port.

## Shared product photographs

`ProductMedia:Provider=Local` preserves server/container behavior. Set `RootPath` to the persistent
product-media directory; the default is `wwwroot/uploads/products`.

Local developers can use `Provider=Sftp` with `RootPath`, `SftpExecutable`, `SshHost`, `SshPort`,
`SshUsername`, `SshKeyPath` and `AgentEnvironmentFile`. The configured path is the directory containing
product-id folders. Reads/uploads/deletions use the existing SSH account; API product ownership checks
still apply before mutations. Asset reads do not forward API bearer tokens to other servers.
Only generated photo filenames and product GUIDs are accepted. Transfers use argument arrays and SFTP
batch input, with host-key validation enabled and a bounded transfer timeout.

The existing development host uses `/opt/treba-backend/uploads/products`. Its owner can run
`scripts/server/enable-dev-media-access.sh` to grant the developer access only to this directory.
Without write access existing photos can be read, but uploads will fail explicitly.

## Settings: staff and activity log

`/settings` contains **Staff and roles** and **Activity log**, accessible only to administrators.
Staff accounts use Administrator, Manager and Moderator roles; seller profiles remain in Sellers.
Creation does not take over an existing email account. The current administrator cannot change their
own role or block themselves. Database persistence also protects the last active administrator, including
legacy user-block routes. PostgreSQL mutations are serialized with a transaction-scoped advisory lock.

Access tokens are bound to the current refresh-session hash. Blocking or changing roles revokes the
refresh session, so unblocking/restoring a role does not revive an old access token. A new sign-in or
refresh replaces the previous session, consistent with the single refresh token stored per account.
Deploying this validation requires users to sign in again once.

Migration `20261017110000_AddAdminAuditLog` adds only `admin_audit_logs`. The database owner can apply
`scripts/sql/20261017-admin-audit-log.sql` for the existing development database, or apply the EF migration
using its normal deployment procedure. The API database user needs SELECT and INSERT on the table.
The launcher does not apply migrations or create accounts on the shared database.

Tracked changes include staff access, users, catalog/product prices and stock, images, categories,
brands, stores, seller status and review moderation. Audit rows are committed with the business change.
Passwords, hashes, authentication secrets, session tokens and customer/order activity are excluded.
Existing records are not retroactively reconstructed. The log is read-only through the API.

Filters cover actor/name search, entity type, action, actor id and date range. The interface uses
`DisplayTimeZone` (default `Europe/Vilnius`) for timestamps and day boundaries.

## Verification

Run the normal WebApi.Tests suite. The PostgreSQL concurrency/rollback test is opt-in via
`TREBA_TEST_POSTGRES`. It creates three uniquely named `treba_qa_*` tables by copying schema only,
modifies no existing application rows and removes only its own tables. It requires CREATE permission
and an initialized audit table. It tests concurrent administrator blocking and rollback when audit
insertion fails.
