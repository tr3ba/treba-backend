# Registration verification and TurboSMS

Password registration requires an explicit `verificationMethod`: `email` or `sms`.
The API no longer verifies an address automatically or issues tokens during registration.
Password recovery also requires a code; see [password reset](password-reset.md).
The storefront in `treba-frontend` includes the method selector, code entry, resend cooldown and verification step.
Deploy the backend and storefront changes together.

## Server setup

Set this environment variable on the **backend server/container**:

```dotenv
TurboSms__Token=YOUR_TURBOSMS_HTTP_API_TOKEN
```

This binds to `TurboSms:Token`. The token stays on the server and is sent only in the
provider's `Authorization: Bearer` header. Never use a `NEXT_PUBLIC_` variable for it.
Docker Compose passes `TurboSms__Token` through from `.env`; the production deployment
already passes the server environment file to the backend container.

SMS sender is fixed to **MAGAZIN**. Enable HTTP API access in TurboSMS and ensure this
sender is available on your account. Provider setup and response format follow the
[official HTTP API documentation](https://turbosms.ua/api.html).

In the admin panel open **Settings → Features → SMS sending** and turn it on when ready.
The persisted key is `notifications.sms-sending`; it is **off by default**.
Only Administrators can edit it, and edits use the existing audit and version checks.
Both a configured token and the enabled feature are required for SMS to be offered.
The SMS sender checks the persisted flag again before every outgoing provider request.
Switching it off blocks new SMS and resends; already submitted messages cannot be recalled,
and already delivered codes remain usable until they expire. There is no automatic SMS fallback or retry.

Email verification uses the existing SMTP setup (`Email__Smtp__Enabled=true`,
`Email__Smtp__Host`, `Email__Smtp__FromAddress` and applicable port/credentials).
At least one delivery method must be configured before password registration is available.

Migration `20261029143639_AddRegistrationVerification` adds a nullable delivery address
to authentication challenges. Existing accounts are preserved. The operational deployment
preflight applies the migration before the new backend is started.

## API

1. `GET /api/auth/registration/methods` returns `{ "methods": ["email", "sms"] }`, filtered by availability.
2. `POST /api/auth/register` accepts the existing registration fields plus required
   `verificationMethod`. Phone is required for `sms`; local Ukrainian numbers such as
   `0671234567` are normalized to `380671234567`. Email remains required for either method.
3. Registration returns `{ challengeId, method, maskedDestination, expiresAt, resendAvailableAt }`.
   The account stays `PendingActivation`, with no access or refresh token.
4. `POST /api/auth/registration/verify` with `{ "challengeId": "...", "code": "123456" }`
   verifies only the chosen contact, activates the account and returns the existing `AuthResponse`.
5. `POST /api/auth/registration/resend` with `{ "challengeId": "..." }` sends a new code
   and returns a replacement challenge. Clients must replace the previous challenge ID.

Codes default to five minutes (`Security:ChallengeLifetimeMinutes`, clamped to 2–15),
allow five attempts and are stored as keyed hashes. Sends are limited to once per 30 seconds
and five per hour per account or destination, including failed delivery attempts. Endpoints
also have an IP request limit. A new challenge invalidates prior registration codes.
Activation and challenge consumption commit together, preventing replay.

Pending accounts cannot log in or refresh a session. After delivery failure or an interrupted
registration, submit registration again with the same email/password; existing active accounts
and pending accounts with a different password cannot be overwritten.

## Verification

Run `dotnet test src/Rozetka-clone/Rozetka-clone.sln -c Release`.
The registration tests use a fake HTTP provider and SMTP sender, so they send no paid SMS.
In `treba-frontend`, run `npm run build` then
`node --test tests/registration.test.mjs tests/external-auth.test.mjs`.
