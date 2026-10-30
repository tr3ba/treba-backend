# Review replies and shared templates

The review queue offers pending/published/rejected/all filters and a moderated reply editor. Publishing a review with a reply saves the status, reply and product rating in one `SaveChangesAsync` call. Editing a reply on an already published review does not increment the rating count. Rejected/deleted reviews remain absent from the public product feed.

`PUT /api/v1/admin/reviews/{id}/moderate` accepts the existing `status` and an optional `reply` (up to 2000 characters). Omitting `reply` preserves the stored reply for older clients; an explicit empty string clears it. A reply can only accompany `PUBLISHED`. The authenticated actor is recorded internally; public DTOs expose only `staffReply` and `staffRepliedAt`. Product reviews and the customer's review list render this saved reply as a TREBA response.

Administrators manage templates in Settings → Features:

- `GET/POST /api/v1/admin/settings/review-replies`
- `PUT /api/v1/admin/settings/review-replies/{id}` with `expectedVersion`
- Administrators, managers and moderators read enabled templates at `GET /api/v1/admin/reviews/reply-templates`.

Templates have a name, Ukrainian/English text and an enabled flag. EF concurrency tokens and submitted versions reject conflicting edits. Disabling a template keeps replies already published. `{customer}` inserts the customer's first name; `{product}` inserts the product title. Substitution happens once, followed by an editable preview and explicit publication. Draft text survives request errors. Template changes and published replies are audited.

Migration `20261030094652_AddReviewRepliesAndTemplates` adds nullable reply fields and `review_reply_templates`, with three editable bilingual starter templates. It is included in the deployment migration allowlist and schema privilege checks. For a later release, apply the backend migration and deploy the matching API before this frontend. No production migration has been run during local development. Existing clients that send status alone remain compatible. Rolling the migration down removes saved replies/templates; retain the schema when rolling back only application code.

Validation: `ReviewRepliesTests` covers persistence across contexts, versions, allowed template variables, staff/administrator boundaries, atomic reply publication, reply edits, clearing, public feed visibility and rating counts. The frontend proxy suite checks template versions and edited reply payloads. PostgreSQL integration tests require `TREBA_TEST_POSTGRES`; they are skipped when that disposable test database is not configured.
