# Google and Facebook website sign-in

Both the login and registration buttons use the same authorization code flow. New identities create Customer accounts without a password; existing identities sign into their account. Configured account 2FA is required before a website session is created.

Set these values in the existing backend deployment environment (never in Git or frontend public variables):

```text
ExternalAuth__StoreOrigin=https://treba.duckdns.org
ExternalAuth__Google__Enabled=true
ExternalAuth__Google__ClientId=<Google Web application client ID>
ExternalAuth__Google__ClientSecret=<Google client secret>
ExternalAuth__Facebook__Enabled=true
ExternalAuth__Facebook__AppId=<Meta app ID>
ExternalAuth__Facebook__AppSecret=<Meta app secret>
```

Facebook `ClientId` / `ClientSecret` remain supported as aliases. Placeholder `YOUR_...` values do not enable a provider. `/api/auth/external/providers` exposes only configured providers and their public client IDs, scopes and redirect URIs.

Register these exact redirect URIs in Google Cloud and Meta Facebook Login respectively:

```text
https://treba.duckdns.org/api/auth/external/google/callback
https://treba.duckdns.org/api/auth/external/facebook/callback
```

These preserve the callback addresses from the original backend configuration. The API relays only the code, state and error to the matching storefront callback on the configured origin. Next.js checks the random state against a short-lived HttpOnly host cookie, then sends the authorization code to `/api/auth/external/code` over the server connection. Google also uses S256 PKCE. The API exchanges the code with the provider using the server-held client secret and the exact configured redirect URI. Codes, provider tokens and secrets never become website sessions directly; JWTs and refresh tokens remain in HttpOnly website cookies.

Enable Facebook Login for the website domain and make the Meta app available to its intended users. Grant the email permission; Facebook identities without an email receive a request to enable that permission. Google consent audience/test users must also include the intended users while its app is in testing.

For local development, use the same loopback origin in backend `ExternalAuth:StoreOrigin` and frontend `TREBA_STORE_ORIGIN`, and add the corresponding `/api/auth/external/{provider}/callback` redirect URIs to the provider consoles. The public origin routes `/api/` to WebApi and `/shop-api/` to Next.js; configure that routing for a local combined environment as well.

The operational deployment check resolves the external login table from EF metadata and quotes its name. The existing migration creates `public."UserExternalLogins"`; a lookup for `public.user_external_logins` falsely reports missing privileges.

Provider protocol references: [Google web server authorization](https://developers.google.com/identity/protocols/oauth2/web-server), [Facebook manual login flow](https://developers.facebook.com/docs/facebook-login/guides/advanced/manual-flow/).
