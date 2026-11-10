# amonezeer: standalone security tasks

This change implements the standalone cards in the personal audit package. Cards from the joint DevOps section are outside this change. Infrastructure workflows, S3 migration, Telegram relinking, legacy admin handoff, proxy configuration and avatar concurrency are separate tasks.

| Cards | Enforcement boundary |
| --- | --- |
| TREBA-12 | Telegram Guard requires a live credential-backed login challenge belonging to the approving account. Unbound requests and old unbound approvals cannot issue tokens. |
| SEC-09, SEC-15 | OAuth never automatically links a provider to an existing account by email. Profile PATCH retains its email; explicit changes require contact confirmation and the current MFA when enabled. |
| SEC-33, SEC-34, SEC-12, SEC-35 | Registration passwords are bound to the contact challenge and applied on verification. Email owners can restart pending registration with a new password. SMS cannot take over someone else's pending email registration. Registration and recovery have independent bounded quotas. Shared send reservations are serialized in PostgreSQL. Public registration and resend responses conceal account availability. |
| SEC-02, SEC-50, SEC-51 | Failed MFA checks share a five-attempt account budget over 15 minutes. Resends retain failures and the original deadline and allow at most five sends. A TOTP time step is accepted once per account, including concurrent requests. |
| SEC-08, SEC-36, SEC-37 | Suspended sellers cannot edit, submit, archive or change photos. Public catalog, facets, variant attributes and stock queries share visibility rules for active products, brands, categories, stores and sellers. |
| SEC-24, SEC-21, SEC-27 | Mixed-order seller details redact shared contacts and address; search does not match private contacts. Seller support search excludes email. Customer details require customer activity, including retained deleted conversations. |
| SEC-43, TREBA-14, SEC-46 | Import text processing has per-fragment and cumulative budgets. Add and Retry enforce the same 50-job quota under the same seller lock. Job claims exclude sellers with a live processing lease and prioritize sellers served least recently. Retry retains scheduling history. |
| SEC-38, SEC-48 | Checkout rechecks the current free-delivery entitlement. A background task removes up to 1,000 unused quotes per minute once they have been expired for 24 hours; current and order-linked quotes remain. |
| SEC-39, SEC-45, TREBA-15 | Product photos and avatars are re-encoded without metadata, including existing files on read. External product photos are fetched through the existing SSRF-protected downloader. Product media checks current visibility and access, bypasses static file serving and uses private/no-store responses. |

## Account API changes

- `PATCH /api/users/me` accepts profile edits with the current email. A changed email must use the explicit flow.
- `POST /api/users/me/email-change`: `{ "target": "new@example.test", "secondFactor": { "challengeId": "...", "method": "email", "code": "..." } }`. The second factor is required when configured. Obtain the login challenge with a fresh password/OAuth login. The server sends independent codes to the current and new email addresses.
- `POST /api/users/me/email-change/confirm`: `{ "challengeId": "...", "currentEmailCode": "...", "newEmailCode": "..." }`. Until confirmation, the current contact and factor remain active. Confirmation verifies the new contact, preserves email MFA, consumes outstanding challenges and revokes the session. Sign in again afterward.
- `POST /api/users/me/external-link-confirmation`: same start body with `target` equal to `google` or `facebook`. The server sends the confirmation to the current verified email address.
- `POST /api/auth/external/link`: existing `provider` and `accessToken` plus `confirmationChallengeId` and `confirmationCode`. An access token alone cannot create a provider link. Existing provider-subject logins still work.
- `POST /api/auth/reauthenticate`: authenticated `{ "password": "..." }` returns the current account's MFA challenge without issuing tokens. `POST /api/auth/external/reauth/code` provides the same operation for an already linked OAuth identity using the existing provider code/PKCE contract.
- `POST /api/auth/external/link/code`: authenticated `{ "login": { "provider": "google", "code": "...", "redirectUri": "...", "codeVerifier": "..." }, "confirmationChallengeId": "...", "confirmationCode": "..." }` exchanges the provider code and creates the confirmed link while preserving the current session. It never runs automatic provider registration/login.
- Registration resend returns opaque `method` and `maskedDestination` fields. Keep the original display metadata from the registration form, respect `resendAvailableAt`, and use the returned challenge ID. Expired or unavailable attempts can be restarted through registration.

Account contact changes require a verified current email. An account registered by SMS can first prove its current email through the email-factor setup flow. Telegram-only accounts can use that verified email factor for a fresh confirmation.

## Media clients

Use image URLs returned by the API without dropping their query strings. Staff/seller responses include short-lived signed image links, allowing ordinary browser image elements to load permitted drafts. Tickets are bound to the user, path and expiry; each read still checks the user's current role and product ownership. Public URLs carry no private access ticket. Mini App photo fetches additionally support signed Telegram authorization.

External source URLs remain server-side. New image metadata accepts HTTPS sources or canonical uploaded files belonging to the product. Legacy local image paths retain their existing behavior; new product uploads and external proxy URLs pass through visibility checks.

## Database and validation

Apply `20261110110220_SecureStandaloneAccountChallenges` before deploying the API. It adds challenge password snapshots, contact-change proofs, send counters, account MFA state, TOTP replay state and the delivery-discount snapshot. Existing pending registration codes need registration to be restarted because they have no password snapshot. Existing discounted delivery quotes may require a new calculation.

Focused regression coverage is in `WebApi.Tests/StandaloneSecurityTests.cs` and `WebApi.Tests/PostgresStandaloneSecurityTests.cs`; existing registration, OAuth, Guard, media and historical migration fixtures were updated for these boundaries.

Run `dotnet test src/Rozetka-clone/WebApi.Tests/WebApi.Tests.csproj` with `TREBA_TEST_POSTGRES` pointing to a disposable test database to include PostgreSQL query, quota and concurrency checks. Production migration application, real email/SMS delivery and browser staging acceptance remain release checks.
