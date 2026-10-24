# Admin workspace

## Sellers

Administrators and managers can approve a seller, suspend an active seller after confirmation, and reactivate a suspended seller. The profile reads its status from the API and uses the returned record after a successful decision. Returning to the list reloads the summary counts. The dashboard opens pending applications with `/sellers?status=0`.

API routes:

- `GET /api/v1/admin/sellers/{id}`
- `POST /api/v1/admin/sellers/{id}/approve`
- `POST /api/v1/admin/sellers/{id}/suspend`

These actions change the seller profile status using the existing seller service.

## Review moderation

Administrators, managers and moderators can search reviews by product, author, email, title or comment; filter pending, published and rejected reviews; and page through the results. Deleted reviews are excluded. Publishing or rejecting a review updates its status and the product's published-review rating in one `SaveChangesAsync` call. Deleted reviews cannot be republished. Rejection requires confirmation.

API routes:

- `GET /api/v1/admin/reviews?page=1&size=12&status=PENDING&search=...`
- `GET /api/v1/admin/reviews/pending-count`
- `PUT /api/v1/admin/reviews/{id}/moderate` with `{"status":"PUBLISHED"}` or `{"status":"REJECTED"}`

The navigation badge refreshes periodically and after decisions. API validation rejects unknown statuses and invalid pagination. Customer reviews are linked to an order item and the buyer's account.

## Dashboard

`GET /api/v1/admin/dashboard?days=30` accepts 7, 30 or 90 days. Queue counts are current totals. The period applies to new users and recently changed records.

- Administrators see seller and review queues, total/new users, and recent product, seller, review and user records.
- Managers see seller/review data and product changes. User counts and user activity are omitted.
- Moderators see review data and product changes. Seller and user data are omitted.
- Sellers retain the existing catalog dashboard and cannot call the new administrative dashboard or review routes.

Recent changes show the latest state of up to eight records; they are not a historical audit log. Order management, promotions, support/returns and operational metrics are described in [admin-operations.md](admin-operations.md).

The original seller/review dashboard required no database migration. The operational modules require `AddAdminOperations`; see the linked deployment instructions. Deploy the WebApi and AdminPanel changes together.

## Verification

```powershell
dotnet build src/Rozetka-clone/Rozetka-clone.sln --configuration Release
dotnet test src/Rozetka-clone/WebApi.Tests/WebApi.Tests.csproj --configuration Release
```

Tests cover seller decisions, review search/filter/pagination, rating changes, deleted-review protection, dashboard scope/periods, HTTP body validation and endpoint authorization. Database-independent tests use EF Core InMemory; also verify the workflow against PostgreSQL when changing its queries.
