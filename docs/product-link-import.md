# Local product link import update

This update remains local. It does not apply a migration, enable a production feature or send SMS.

The frontend proxy now accepts the existing `PUT settings/features/notifications.sms-sending` endpoint, with an explicit escaped resource pattern. Feature settings show separate UK/EN copy and Enabled/Disabled states for TurboSMS and chat deletion. Sending SMS still requires the existing server-side `TurboSms:Token`; the configured sender in this codebase is `MAGAZIN`. The provider is never called while toggling the feature itself.

Product previews now open immediately in a responsive modal with loading, retryable errors, a scrollable body and persistent header/actions. Queue status matching handles the API's title-case values, including Expired → Retry. Failed product submissions retain edited inputs. Source SKU, matching attribute options, booleans and compatible numeric values are prefilled; incompatible units or ambiguous options require review.

## Source handling

The existing Rozetka adapter now recognizes storefront subdomains, including `auto.rozetka.com.ua`. It retains public API loading and tries a short optional HTML enrichment when the API has no description. Blocked HTML never discards usable API data. Missing descriptions produce a warning in the preview. The original characteristics remain available, and multiple source EANs no longer get concatenated into one invalid barcode.

Other public HTTPS stores use a generic parser for schema.org Product/ProductGroup JSON-LD, graph references, microdata, Open Graph product metadata, embedded product JSON, product descriptions, specification tables and definition lists. Product fields, identifiers, brand, up to eight source images and specifications enter the existing draft/review flow. Variant information is retained as source specifications; it is not silently converted into sellable variants. UAH offers may prefill the price. Foreign amounts/currencies remain source information and require the seller's own UAH price. Source availability is informational; the seller enters their own stock.

Generic product/category identities include the source hostname, fit existing column lengths and avoid cross-store collisions. Existing category access, explicit mapping, duplicate checks, draft ownership/expiry, moderation, transactional image storage and idempotent submit remain in effect. Category mapping accepts `web:<12 hex digits>` alongside existing providers. New optional preview fields (`sourceSku`, `sourceCurrency`, `sourceWarnings`) require no database migration.

The downloader keeps HTTPS/default-port/no-credentials rules, validates each redirect and resolves only public DNS addresses at the socket connection. Internal, loopback, link-local and reserved destinations are rejected for pages and images. Request deadlines, response size bounds, media types and image signatures remain enforced. No remote JavaScript or CAPTCHA solving is introduced. Sites requiring sign-in, anti-bot verification or data available only after JavaScript may require manual completion; no universal success guarantee is made.

## Verification

`dotnet test WebApi.Tests/WebApi.Tests.csproj --no-restore` covers generic extraction, currency handling, variants, malicious markup, private addresses/redirects, external-image submit, permissions, ownership and existing adapters. The frontend production/proxy suites cover feature persistence/version conflicts and import value matching; browser QA uses disposable `--imports` fixture data.

A read-only smoke check against the user-supplied `https://auto.rozetka.com.ua/ua/rubeg_op2/p2119937/` retrieved product 2119937, brand Рубіж, a UAH price, two photos and ten specifications; the first photo downloaded with a valid image signature. At verification time the API description was empty and HTML returned 403, which is reported as a missing-description warning. The smoke check does not create a product or contact a production database.
