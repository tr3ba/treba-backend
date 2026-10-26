# Marketplace workflows

## Seller applications

Authenticated customers submit company, store, tax and contact details at `/sell`.
Applications are stored in `seller_applications`; rejected applications can be corrected and resubmitted.
Administrators and managers review the queue at `/sellers/applications`.
Approval atomically creates a seller, store and grants for active catalog roots, then assigns the Seller role.
The role change revokes the former customer session: the applicant signs in again to enter the seller workspace.
Staff accounts use their existing workspace rather than applying to replace their role.
Review requests carry an optimistic concurrency version; a rejection requires a reason.

## Search and customer account

Public catalog endpoints accept search, category, brand, price range, stock, sorting, pagination and up to 20 filterable attributes.
`GET /api/v1/catalog/facets` supplies brands and attribute options from published products in the selected category and search scope.
The storefront keeps filters and pagination in the URL, including empty categories.

Authenticated `/api/v1/account/preferences` GET/PATCH stores profile and delivery preferences on the server.
PATCH accepts allowlisted fields and a version, updates authoritative names/phone/profile, and rejects stale edits.
`/api/v1/account/orders` and `/orders/{id}` return only the signed-in customer's orders, line prices and status timeline, excluding private staff notes.
Order details link to support with the selected order attached.
These reads do not initialize the payment provider; online payment integration remains a separate feature.

## Notifications

`GET /api/v1/account/notifications` returns up to 300 recent events from the last 30 days.
Sources are public support messages, new orders, customer order status changes, current moderation outcomes and seller applications.
The feed applies each role's visibility: seller product questions, staff support, and customer-owned events.
It does not expose internal notes. Application and moderation entries represent their current state rather than a full event archive.
`POST /api/v1/account/notifications/read` persists per-user receipts, validates event visibility and tolerates repeated requests.
Storefront and admin bells poll every 30 seconds; links open the relevant order, conversation or review queue.
Admin handoff preserves an allowlisted local destination; the independent development login remains available.
The in-app feed remains available. Optional order/support email and Telegram delivery is described in [delivery and notifications](delivery-notifications.md).

## Deployment and validation

Migration `20261022084526_AddMarketplaceWorkflows` adds only `seller_applications`, `customer_preferences` and `notification_receipts`.
The existing operational deployment command applies this reviewed migration before switching HTTP, then verifies table access.
Publish the backend before the dependent storefront.

Run `dotnet test src/Rozetka-clone/Rozetka-clone.sln -c Release`.
Set `TREBA_TEST_POSTGRES` to a disposable test PostgreSQL instance to enable relational integration checks.
Tests cover application approval, role/session changes, ownership, stale writes, private-message isolation, filters and read receipts.
The frontend additionally runs `node --test tests/*.test.mjs` against its production build and a controlled backend fixture.
