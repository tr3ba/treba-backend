# Administrative frontend migration

The administrative UI now belongs to the sibling `treba-frontend` repository, at `/admin`. This repository no longer contains or publishes the `AdminPanel` Blazor project. Administrative APIs, authentication services, seller import, audit, order rules and PostgreSQL schema stay in the backend.

The frontend uses a dedicated `/admin-api` allowlist and HttpOnly staff-session cookies. Customer and workspace credentials are separate. Existing users and 2FA methods are preserved; existing Blazor browser sessions require a new login.

## Changed delivery paths

- The solution and backend Docker image publish only backend services.
- The frontend build validates its workspace and storefront before publishing its image.
- Deploy frontend first. Backend deployment checks `/admin-api/session` and the new workspace HTML before stopping/replacing anything.
- On success, the old admin host redirects into `/admin`; the old admin container is stopped and retained for recovery.
- Desktop launchers now start the sibling Next.js workspace. `-LocalApi` retains the existing database tunnel and API launch workflow.
- The Telegram Mini App and its shared editor-draft JavaScript remain part of the backend; they are independent of the removed administrative UI.

No database migration is required by this change. Previous data, role policies and API contracts are retained.

## Tests

Run `dotnet test src/Rozetka-clone/Rozetka-clone.sln --configuration Release`.
Blazor-only rendering tests were removed with their components. Mixed test files retain their API/domain tests. The frontend has transport/session/role tests and browser verification for its replacement UI.

The backend suite passed 354 tests after removal. Nineteen PostgreSQL tests were skipped because `TREBA_TEST_POSTGRES` was not configured; no production database was used for verification.

See `docs/admin-workspace.md` in `treba-frontend` for the routes, session model, frontend verification commands and coordinated rollback procedure.
