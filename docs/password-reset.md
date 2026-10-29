# Password reset with contact confirmation

The storefront's **Forgot password?** action now requests an email or SMS code and accepts
a new password only after the code is verified. Deploy the matching backend and storefront together.
Provider settings are shared with [registration verification](registration-verification.md):
`TurboSms__Token` on the backend, fixed sender `MAGAZIN`, and the persisted
`notifications.sms-sending` switch, off by default. Turning SMS off blocks recovery sends too.

Recovery uses the account email as its lookup key. Codes go only to the **verified** email
or **verified** phone already stored on an active account; request bodies cannot supply a replacement
delivery destination. For accounts registered using SMS, use the confirmed phone for recovery.
Unverified profile contacts, blocked/deleted accounts and accounts awaiting registration activation
cannot be used to reset an active account's password. Existing two-factor methods remain enabled.

## API

- `GET /api/auth/password-reset/methods`: configured and enabled delivery methods.
- `POST /api/auth/password-reset/request`: `{ "email": "customer@example.com", "verificationMethod": "email" }`;
  `sms` is the other supported method. Returns `{ challengeId, method, expiresAt, resendAvailableAt, message }`.
- `POST /api/auth/password-reset/complete`: `{ "challengeId": "...", "code": "123456", "newPassword": "..." }`.
  A successful reset returns **204**, with no login tokens. Sign in normally with the new password.

Repeat the request endpoint to resend a code; replace the challenge ID with the new response.
Codes have the configured security challenge lifetime (five minutes by default), five attempts,
a 30-second send cooldown, and a shared five-per-hour account/destination budget with registration.
Requests also use the existing IP limiter. For unknown, unverified, unavailable, throttled or
delivery-failed accounts, the request endpoint returns a generic response that does not reveal
whether an account exists. Codes are not sent for unknown/unverified/unavailable/throttled accounts.

Reset codes use a keyed hash bound to both the challenge ID and the current password hash.
Changing the password or delivery contact invalidates a pending code. A successful reset consumes
all outstanding authentication challenges and revokes the refresh token, which also invalidates
existing access tokens through the existing session binding. Password changes use optimistic
concurrency to reject stale overwrites. Previously sent codes can still be confirmed after SMS
sending is turned off; no additional SMS is sent during confirmation.

New passwords require 8–72 characters, uppercase and lowercase Latin letters and a digit, and
must fit BCrypt's 72-byte UTF-8 input limit. Hashes and code material remain on the server.

The recovery flow reuses the registration migration's challenge destination column; there is
no additional schema change. The password hash concurrency metadata is included in the new model snapshot.

Tests: `dotnet test src/Rozetka-clone/Rozetka-clone.sln -c Release`; from `treba-frontend`,
`npm run build` and `node --test tests/password-reset.test.mjs tests/registration.test.mjs tests/external-auth.test.mjs`.
All delivery tests use fakes and send no paid SMS.
