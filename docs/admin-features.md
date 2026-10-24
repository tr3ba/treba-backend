# Admin features and chat trash

Administrators manage system-wide features at **Settings → Features** (`/settings/features`).
The first switch is **Chat deletion** (`support.chat-deletion`), disabled until an administrator enables it.
Values persist in PostgreSQL, take effect on subsequent API requests, and use versions to reject stale edits.
There are no browser-only flags or configuration-file overrides for this switch.

When enabled, administrators and managers can move a conversation from their existing TREBA support queue to trash.
The action is available in both the inbox and conversation details, with confirmation and a version check.
It does not grant access to private product questions belonging to sellers.
Active returns must be completed before their conversation can be removed.

Deleted conversations disappear from ordinary support reads, customer history, metrics and notification feeds.
Messages remain in the database; customers cannot fetch deleted details/messages or append to a deleted thread.
Administrators and managers can restore conversations at `/support/trash`, even after deletion is disabled.
Restoration preserves the prior status and messages. If the customer has a newer active personal chat, complete that chat first.
The database enforces uniqueness and rejects concurrent writes instead of resurrecting a deleted thread.
Feature changes, deletion and restoration are recorded in the existing audit log.
There is no permanent deletion or automatic purge.

## API

- `GET /api/v1/admin/settings/features` — Administrator only.
- `PUT /api/v1/admin/settings/features/support.chat-deletion` — `{ enabled, expectedVersion }`, Administrator only.
- `GET /api/v1/support/capabilities` — per-user deletion/restoration capabilities.
- `GET /api/v1/support/deleted?page=1&search=...` — Administrator/Manager, scoped to their support queue.
- `DELETE /api/v1/support/{id}` — `{ expectedVersion }`, Administrator/Manager and enabled feature required.
- `POST /api/v1/support/{id}/restore` — `{ expectedVersion }`, Administrator/Manager.

## Deployment and tests

Migration `20261022132542_AddAdminFeaturesAndChatTrash` adds the feature table and deletion metadata,
and replaces three active-thread indexes to exclude deleted records. It preserves existing threads and messages.
The existing operational deployment preflight applies it before switching the API and admin image.
An application rollback can retain the additive schema. Do not run a schema downgrade with conflicting active/deleted threads.

Run `dotnet test src/Rozetka-clone/Rozetka-clone.sln -c Release`.
Set `TREBA_TEST_POSTGRES` to enable relational checks.
Coverage includes role gates, defaults/stale feature updates, privacy, notification exclusion, restore conflicts,
in-flight message rollback, localized UI controls and the actual migration SQL applied to an isolated test schema.
