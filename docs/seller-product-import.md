# Seller product import

The magic-wand button in `/products/new` opens `/products/import` for sellers.
It has an accessible label and tooltip. Sellers can import up to 20 product
links from Rozetka, Prom.ua and Epicentr without connecting Telegram.
The server queue survives page refreshes and shares the source restrictions
and category permissions used by the Telegram workspace.

Product pages use Ukrainian URLs and request Ukrainian content. Rozetka API
requests use `lang=ua`. The admin panel supports Ukrainian and English;
Telegram uses Ukrainian. The profile-language migration normalizes legacy
preferences to these supported languages.

## Categories

Existing administrator mappings take precedence. Otherwise a seller with
full-catalog access can classify an import using an allowed category.
Automatic matching uses Ukrainian and English category names, then the
closest existing breadcrumb. Resistance bands can fall back to Fitness,
Sports goods or the Sports section. Ambiguous matches need a manual choice.
Mobile phones require smartphone evidence before matching Smartphones.

The preview allows changing an unmapped category and creating a missing child
under an allowed parent. Creation reuses an existing normalized name under
that parent, rejects disabled or ambiguous categories, and rechecks the
seller, preview, category grants and administrator mappings. PostgreSQL locks
serialize concurrent creation of the same child. Required attributes are
refreshed when the category changes. The new category appears in subsequent
imports and in category selectors with its full path.

A manually selected category applies to the current batch. Adding links
resets the next batch to automatic classification. Each failed or expired
job also has a category selector. Choosing automatic explicitly resets an
old override; older API callers that omit the reset flag retain their
previous category.

## Brands and duplicates

The preview reloads available brands each time it opens. Brand extraction
uses structured brand data, manufacturer characteristics or a manufacturer
part-number prefix present in the product title. If the source omits a brand,
a unique existing brand mentioned in the title can be selected. Sellers can
create a missing brand; normalized existing names are reused.

Products are created only when the seller submits the preview for moderation.
Submitting an already completed preview returns the existing product id.
Importing an existing source product opens the existing catalog entry through
the queue rather than producing a failed job.

The catalog and duplicate panel offer a confirmed delete action. It archives
the product and deactivates its variants in one save. Orders, photos and audit
history retain their references. Archived products disappear from default
catalog results, counts and duplicate matches; the source product can then
be imported again. Sellers can remove only their own products; managers and
administrators can remove catalog products. Moderators cannot delete them.

## API

JWT seller endpoints under `/api/v1/seller/imports`:

- `GET options`, `GET queue`, `POST queue`.
- `POST queue/{id}/retry?categoryId={id}` selects a category.
- `POST queue/{id}/retry?resetCategory=true` restores automatic classification.
- `GET {id}`, `POST {id}/brand`, `PUT {id}/category`, `POST {id}/category`.
- `POST {id}/submit`.

Catalog deletion uses `DELETE /api/v1/admin/catalog/products/{id}`. Equivalent
seller actions are available under `/api/telegram/mini` with Telegram auth.

Local editor recovery preserves preview fields. `PublicApiBaseUrl` supplies
browser-accessible image URLs when `ApiBaseUrl` is internal to Docker. The
header storefront link uses `WebsiteLogin:StoreOrigin`.
