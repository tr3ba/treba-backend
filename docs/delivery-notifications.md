# Nova Poshta delivery and customer notifications

## Delivery

Checkout offers **Нова пошта** (city, branch or parcel locker) and **Кур’єр TREBA** (delivery address).
Courier delivery costs 50 UAH unless a promotion makes delivery free. Nova Poshta uses a server-side carrier quote.
The server calls only directory methods (`Address/getCities`, `Address/getWarehouses`) and the cost calculator
(`InternetDocument/getDocumentPrice`) at `https://api.novaposhta.ua/v2.0/json/`.
It never calls `InternetDocument/save`, registers a parcel, or creates a carrier waybill.
See the [official integration entrypoint](https://novaposhta.ua/for-business/cooperation/integration/) and
[developer portal](https://developers.novaposhta.ua/documentation).

The destination and selected carrier are saved at checkout. The unique internal `TRB-<UUID>` shipment reference
is assigned atomically when staff confirms payment (`AWAITING_PAYMENT → PAID`), together with stock confirmation,
payment status and notification records. Unpaid or cancelled-before-payment orders do not receive a reference.
Repeated or stale confirmations cannot issue a second reference. The existing admin order workflow controls
shipment status through confirmation, processing, ready for shipment, shipped and delivered.
The carrier API is used for directory and price reads; shipment registration and tracking remain internal to TREBA.
Customer-facing screens use regular carrier names, shipment numbers and status history.

In the admin order list, **Керувати доставкою** opens the order's fulfillment section. Each button advances one
permitted step: paid → confirmed → processing → ready for shipment → shipped → delivered → completed.
These manual steps can be used to simulate delivery; they do not contact the carrier. Confirm **Підтвердити отримання товарів**
only when all items have been received. Existing role, mixed-seller and optimistic concurrency checks apply to every step.

## Receipt and product reviews

The delivered transition creates an in-app receipt notification with an optional review invitation. Customers with order
email or Telegram enabled receive the same invitation through the existing outbox. Repeated/stale status writes cannot
create another receipt event. In-app notifications do not depend on external channel subscriptions.

The received order shows a review form for each eligible item; `/account/reviews` lists items awaiting review and the
customer's own reviews, including moderation status. Product-page review links lead to this account section.
The API enforces ownership, matching product/order item, successful payment, and `DELIVERED` or `COMPLETED` status.
Unpaid, unreceived, cancelled and returned orders are ineligible. One review is allowed per order item, including reviews
already pending, rejected or deleted; the existing unique database index also prevents concurrent duplicate inserts.
New reviews require a rating of 1–5, a nonblank comment up to 10000 characters, and an optional title up to 255 characters.
Reviews start pending and become public after moderation. No schema migration is required for this workflow.

- `GET /api/v1/reviews/awaiting?page=1` — only the signed-in customer's eligible items without reviews, 20 per page.
- `GET /api/v1/reviews/mine?page=1` — only their own nondeleted reviews, 20 per page.
- `POST /api/v1/reviews` — `{ productId, orderItemId, rating, title, comment }`.

Customer order items include `canReview` and `reviewId`; the server rechecks eligibility when the form is submitted.
The storefront forwards these private operations through `/shop-api/reviews`, using its server session and same-origin
write checks. Deploy the API/admin and storefront updates together; the API should be published first.

API keys remain on the backend. Configure runtime environment variables (or local .NET user secrets):

```text
NovaPoshta__ApiKey=<private API key>
```

This is the AWS/container variable already supported by the application. `Delivery__NovaPoshta__ApiKey`
is also supported and takes precedence when nonempty. A key enables the integration automatically;
`Delivery__NovaPoshta__Enabled=false` explicitly disables it. A missing key leaves courier checkout available.
The default sender city is Kyiv, resolved through the carrier directory. Override it with
`NovaPoshta__SenderCityRef` or `Delivery__NovaPoshta__SenderCityRef` for another origin.
`Delivery__NovaPoshta__DefaultWeightKg` defaults to 1. Product variant weights are kilograms.
Missing weights use the configured per-item default and the UI labels the result as approximate.
The quote assumes one parcel, honors pickup-point weight limits, and requires a separate arrangement for weight above 1000 kg.
Real multi-origin or multi-parcel shipments require a separate implementation.

Authenticated endpoints under `/api/v1/checkout-delivery`:

- `GET capabilities` — availability and shipment mode, without credentials.
- `GET cities?query=Київ` — up to 20 matching cities, minimum two characters.
- `GET pickup-points?cityRef=<UUID>&query=&kind=all&page=1` — up to 50 entries per page; kind is `all`, `branch` or `postomat`.
- `POST quotes` — `{ cityRef, pickupPointRef, items: [{ variantId, quantity }], couponCode? }`.

A quote lasts ten minutes and is bound to the authenticated user, cart, coupon and server prices.
Send `deliveryMethod: "NovaPoshta"` and its ID as `deliveryQuoteId` to `POST /api/v1/orders`.
For courier checkout send `deliveryMethod: "TrebaCourier"`, an address and no quote.
Unknown methods and a carrier/quote mismatch are rejected. Legacy requests without a method infer it from the quote.
The quote is consumed once in the order transaction.
Checkout recalculates product prices, checks current stock and coupon limits, and saves canonical carrier destination data.
A rejected or concurrent checkout cannot leave an order, stock change or notification behind.
Customer `/account/parcels` lists only their own shipments with an assigned reference and searches references/order numbers through
`GET /api/v1/account/shipments?page=1&search=...`. Order details show the local status history without private staff comments.

Requests without a quote retain the existing manual address delivery fee (50 UAH, or free with an applicable promotion).
Manual checkout does not resolve a payment provider; online payment remains a separate explicit operation.

## Customer notifications

Customers opt in at `/account/subscriptions`, independently for order email, order Telegram, support email and support Telegram.
Preferences persist on the backend and start disabled. In-app notifications continue as before.
`GET/PUT /api/v1/account/notification-settings` reads/saves those four booleans and a concurrency `version`.
Availability reflects server configuration, verified account email, and Telegram binding; selections can be saved before a channel is available.

```text
Notifications__Enabled=true
Notifications__EmailEnabled=true
Notifications__TelegramEnabled=true
Notifications__StoreOrigin=https://your-store.example
Email__Smtp__Enabled=true
Email__Smtp__Host=<SMTP host>
Email__Smtp__Port=587
Email__Smtp__UseSsl=true
Email__Smtp__Username=<SMTP username>
Email__Smtp__Password=<SMTP password>
Email__Smtp__FromAddress=<verified sender>
```

Telegram additionally requires the existing `Telegram` bot configuration and a running bot update consumer.
Customers can link their private Telegram chat from subscriptions, start the bot, then confirm the displayed identity on the website.
Linking a customer does not grant staff Mini App or Guard access. Unlinking prevents pending Telegram delivery.

Order placement/status changes and public support replies stage outbox records in the same database save as the business change.
A separate worker claims records using a lease and optimistic concurrency, then sends them with a 30-second timeout.
Transient failures retry up to five attempts with exponential backoff. Telegram HTTP 400/403 stops retries.
Events older than seven days are skipped. Delivery is at least once: a lost provider acknowledgement can cause a duplicate.
Disabling the worker or a channel preserves pending events for later processing.

Before sending, the worker rechecks account status, subscription, ownership, confirmed email or current Telegram link,
and whether a support conversation is still visible. Internal notes and private message text never enter external messages.
Email goes to the account's verified address, not an arbitrary recipient email supplied at checkout.
No existing events are backfilled. Production messages are never sent by tests.

## Migration and validation

Migration `20261024192735_AddDeliveryAndCustomerNotifications` adds `delivery_quotes`, `order_shipments`,
`customer_notification_settings` and `customer_notifications`. Existing orders are preserved.
It copies existing non-PUBLIC CRUD grants from `orders` to the new tables. The operational deployment preflight
allows this migration and verifies actual table access before switching containers.
Migration `20261026142406_AddManagedShipmentLifecycle` adds the carrier, permits pending shipments without a reference,
clears premature references on unpaid orders, and converts existing `TREBA-DEMO-` references to `TRB-`.
It preserves destinations and existing paid shipment identifiers apart from that prefix conversion.
Publish the backend migration/API/admin before the dependent storefront. An application rollback can retain the new tables;
avoid a schema downgrade after shipments or notification preferences exist.

```powershell
$env:TREBA_TEST_POSTGRES = '<dedicated local/test PostgreSQL connection>'
dotnet test src/Rozetka-clone/Rozetka-clone.sln -c Release
# In the frontend checkout:
npm run build
node --test tests/*.test.mjs
```

Tests use controlled carrier/SMTP/Telegram clients and isolated PostgreSQL tables/schemas. Live carrier verification
requires the configured API key and sender city; successful fixtures do not verify credentials or carrier availability.
