# Catalog workspace

The catalog supports search, status, category (including descendants), brand, seller, price range and
stock filters. Low stock means a total of 1–5 units across active variants; price ranges match an active
variant. Sorting supports creation/update time, name, price and total stock. Product cards show the
lowest active price and total active stock. Search also matches seller and store names.

Filters and the page number are stored in `/products` query parameters. Editor links carry a validated
local return URL so saving or returning preserves the catalog selection. Seller scope is applied by the
API independently of filters. Filter options expose company names only and sellers receive their own
seller option.

Categories have a searchable, collapsible tree. Results retain ancestor context, and the editor and
parent selector display full paths. Existing category editing behavior is preserved.

## Moderation

`POST /api/v1/admin/products/{id}/reject` now requires JSON `{ "reason": "..." }` with a nonblank reason
of at most 2000 characters. Submission, approval and rejection write `AdminAuditLog` events with action
`Moderation`, entity type `Product`, the status transition, decision and optional reason. The event and
status update commit together. The normal audit interceptor avoids creating a duplicate product event.
No additional table or schema permission is needed beyond the existing audit table.

`GET /api/v1/admin/catalog/products/{id}/moderation-history` returns the latest 100 decisions. Sellers
can access only their own product histories. The editor shows the latest rejection until the product
is resubmitted. Earlier decisions remain visible in the history and in Settings → Activity log.

## Product editor

The catalog save request can carry complete `variants` and `attributes` collections. A single save
validates and commits the product, variants and characteristic values. Variant ids and SKUs must be
unique and cannot belong to another product. Omitted existing variants are deactivated, preserving ids
referenced by other records. Legacy requests without variant collections still edit their main variant.

Characteristics are typed (text, number, boolean, date, selection or multiple selections), belong to the
selected category or an ancestor, and may apply to the whole product or a particular variant. Required
characteristics must cover the whole product or every active variant. Administrators can create text
characteristics for the selected category directly in the editor. Other staff choose existing definitions.

Copying opens an unsaved editor with independent variant ids and SKUs, zero stock and copied
characteristics. Photos are added separately. The source product is not changed.

The editor warns before leaving with unsaved data. `ExpectedUpdatedAt` detects an outdated editor,
and the existing product update timestamp is also an EF concurrency token so simultaneous updates
roll back with HTTP 409. This is model metadata only and does not change the database column.

## Verification

The normal suite covers filter combinations, seller isolation, category tree presentation, typed and
inherited characteristics, variant ownership/identity, moderation reasons/history and stale edits.
Set `TREBA_TEST_POSTGRES` to run the PostgreSQL checks. They use uniquely named schema copies, verify
query translation, concurrent editing and rollback on audit failure, and remove only their own tables.
Shared application data is not changed by these integration tests.
