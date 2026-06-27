# Product Media (Image Upload) — Implementation Specification

Status: Draft v1
Owner: Backend (RetailPOS.API / RetailPOS.Infrastructure) + Frontend (retailpos-frontend)
Replaces: free-text `Product.ImageUrl` field on the `products` table.

---

## 1. Overview

Replace the single text `image_url` column on `products` with a real product-media subsystem:

- A normalized `product_images` table (multiple images per product, one always primary).
- File upload pipeline: validate dimensions/format, resize to `thumbnail` (200×200) and `medium` (600×600), encode as WEBP, store on local disk under `wwwroot/uploads/products/`, serve with cache headers.
- API for upload, list, delete, set-primary.
- Frontend media uploader on the Product form, lightbox preview on the Product list, primary-image rendering everywhere products appear (sales, POS, purchase).
- Performance: lists return only the primary image's thumbnail/medium URLs — never the full image set.

## 2. Goals

- Support up to ~20 images per product, multi-resolution, WEBP-compressed.
- One — and only one — primary image per product, enforced in DB.
- Image upload is fast (resize + encode in-process via SixLabors.ImageSharp).
- Listing pages stay fast: list payloads carry only primary thumbnail URLs; full image lists load on demand.
- Deleting a product cascades to its images; deleting an image removes its files from disk.
- Existing `image_url` data is **dropped** (per user decision — fresh start).

## 3. Non-Goals (v1)

- S3 / CDN integration. (Local disk under `wwwroot` for v1; structure picked so swap-out is trivial.)
- Image cropping UI / focus point.
- Re-encoding the user-uploaded "original" — we keep the original bytes alongside resized variants.
- Server-side AVIF (we do WEBP only; browser support is universal for this app's target audience).
- Variant-level images (a `ProductVariant` will inherit its parent product's primary image in v1).

## 4. Storage Layout

```
src/RetailPOS.API/wwwroot/uploads/
  products/
    {productId}/
      {imageId}-original.{jpg|png|webp}     # exact bytes uploaded
      {imageId}-medium.webp                  # 600×600, q=80
      {imageId}-thumb.webp                   # 200×200, q=70
```

- `wwwroot` is served by `app.UseStaticFiles()` with a 30-day public cache header (set explicitly in Program.cs for the `/uploads` path).
- File names use the DB-assigned `imageId` (long) so collisions are impossible and records are easy to reconcile.
- The public URL pattern stored in `product_images.url` is the `wwwroot`-relative path, e.g. `/uploads/products/42/137-thumb.webp`. The frontend builds full URLs by prefixing `environment.apiOrigin` (or via a request-host helper).

## 5. Data Model

### 5.1 `product_images` table

| column          | type                 | notes |
|-----------------|----------------------|-------|
| `id`            | bigserial PK         | also the file-naming key |
| `product_id`    | bigint NOT NULL FK   | `products.id` ON DELETE CASCADE |
| `original_name` | varchar(260) NOT NULL| user's filename |
| `mime_type`     | varchar(40) NOT NULL | of the original |
| `size`          | integer NOT NULL     | bytes of original |
| `width`         | integer NOT NULL     | of original |
| `height`        | integer NOT NULL     | of original |
| `original_path` | varchar(300) NOT NULL| `/uploads/products/{pid}/{id}-original.ext` |
| `medium_path`   | varchar(300) NOT NULL| `/uploads/products/{pid}/{id}-medium.webp` |
| `thumb_path`    | varchar(300) NOT NULL| `/uploads/products/{pid}/{id}-thumb.webp` |
| `is_primary`    | boolean NOT NULL DEFAULT false | exactly one true per product |
| `sort_order`    | integer NOT NULL DEFAULT 0 | display order |
| `created_at`    | timestamptz NOT NULL |
| `updated_at`    | timestamptz NOT NULL |

**Indexes / constraints:**
- `(product_id, sort_order)` — list rendering.
- Unique partial index `(product_id) WHERE is_primary` — guarantees at most one primary per product.

### 5.2 Changes to `products`
- Drop column `image_url`.

## 6. Validation Rules

Server (single source of truth — frontend mirrors for UX):
- Allowed MIME: `image/jpeg`, `image/jpg`, `image/png`, `image/webp`.
- File size: ≤ 5 MB.
- Decoded dimensions: **800×800 ≤ W,H ≤ 1200×1200**. Reject outside this range with explicit error.
- Square aspect not required (any rectangle within the bounds is fine; resized variants are letterboxed via `ResizeMode.Max`).

Frontend mirrors these rules and shows inline validation before sending the request.

## 7. Image Pipeline

When a file arrives at `POST /api/products/{productId}/images`:

1. Read into `MemoryStream` (cap at 5 MB).
2. Decode header with ImageSharp to learn `width × height`.
3. Apply validation (§6). On failure: return 400 with a structured error.
4. Persist a row to `product_images` (without paths) → get `id`.
5. Create folder `wwwroot/uploads/products/{productId}/`.
6. Write `{id}-original.{ext}` (exact bytes).
7. Resize → `{id}-medium.webp` at 600×600 max, q=80.
8. Resize → `{id}-thumb.webp` at 200×200 max, q=70.
9. Update the row with the three paths.
10. If this is the product's first image OR `is_primary=true` flag was sent → set this row primary; clear any other primaries in the same product first (single transaction).
11. Return the saved DTO.

## 8. Primary-image rules

- First image uploaded is automatically primary.
- Setting an image primary clears all others on the same product (single transaction; the unique partial index would otherwise reject the second `true`).
- Deleting the primary image: pick the next image by `sort_order, id` and promote it. If no images remain, no primary is set.
- Frontend warns the user before deleting the primary if there is more than one image, offering to pick the new primary first (UX nicety; backend handles the auto-promotion regardless).

## 9. API

All product-image endpoints live under `/api/products/{productId}/images`. All require authentication; `Products.Edit` permission required for write operations, `Products.View` for GET.

### 9.1 List images for a product

```
GET /api/products/{productId}/images
→ 200: ProductImageDto[]
```

### 9.2 Upload an image

```
POST /api/products/{productId}/images
Content-Type: multipart/form-data; field name "file"
Optional form field "isPrimary=true"
→ 201: ProductImageDto
→ 400 INVALID_DIMENSIONS / INVALID_TYPE / TOO_LARGE
```

### 9.3 Delete an image

```
DELETE /api/products/{productId}/images/{imageId}
→ 204
→ Cascades: deletes DB row + files; auto-promotes another primary if needed.
```

### 9.4 Set primary

```
PUT /api/products/{productId}/images/{imageId}/primary
→ 200: { primaryImageId }
```

### 9.5 ProductDto changes

- Remove `imageUrl`.
- Add `primaryImageThumb: string | null` and `primaryImageMedium: string | null`.
- The full `images: ProductImageDto[]` collection is **not** included on list responses — only on single-product GETs and the dedicated images endpoint.

```ts
export interface ProductImageDto {
  id: number;
  productId: number;
  originalName: string;
  mimeType: string;
  size: number;
  width: number;
  height: number;
  thumbUrl: string;     // /uploads/products/.../thumb.webp
  mediumUrl: string;
  originalUrl: string;
  isPrimary: boolean;
  sortOrder: number;
  createdAt: string;
  updatedAt: string;
}
```

## 10. Static-File Serving

In `Program.cs`, after `app.UseStaticFiles()` is added, add an explicit one for `/uploads`:

```csharp
app.UseStaticFiles(new StaticFileOptions {
    FileProvider = new PhysicalFileProvider(
        Path.Combine(builder.Environment.WebRootPath, "uploads")),
    RequestPath = "/uploads",
    OnPrepareResponse = ctx => {
        ctx.Context.Response.Headers["Cache-Control"] =
            "public,max-age=2592000,immutable"; // 30 days
    },
});
```

`wwwroot/uploads/` is created on first upload; we don't ship empty folders. `.gitignore` excludes it.

## 11. Frontend

### 11.1 New files

```
retailpos-frontend/src/app/
  models/product-image.model.ts
  services/product-image.service.ts
  components/product-form/
    product-image-uploader.component.ts   # drag-drop + thumbnails + lightbox + actions
    image-lightbox.component.ts           # modal preview (medium/original)
```

### 11.2 Product form changes

- Remove the `Image URL` text input.
- Add a `Media` section with:
  - drag-drop zone + file picker (multi-select, `accept="image/jpeg,image/png,image/webp"`).
  - thumbnail grid for already-uploaded images.
  - per-thumbnail actions: View (opens lightbox with medium URL; "Open original" link inside), Delete, Set Primary (radio-style: clicking promotes; primary chip shown on the active one).
  - inline error messages for size / type / dimension violations.
  - upload progress indicator per file.

### 11.3 Product list

- Add a leading column showing `primaryImageThumb` (lazy-loaded `<img loading="lazy">`).
- Placeholder: a small inline SVG icon when `primaryImageThumb` is null.
- Click thumbnail → opens the lightbox with the medium URL.

### 11.4 POS / Sales

- Product search dropdown row: thumbnail (24×24), name, SKU, price.
- Cart rows: thumbnail (32×32), name, qty, price.
- Product detail panel: medium URL, with click-to-zoom to original.
- All thumbnails use `loading="lazy"` and the placeholder fallback.

### 11.5 Purchase flow

- PO line-item search dropdown: same thumbnail row pattern.
- PO items table: 32×32 thumbnail leading column.

### 11.6 Lightbox

- Full-screen overlay, centered image, dismiss on Esc / click-outside.
- Loads `mediumUrl` first; offers a "View original (W×H)" link that swaps in `originalUrl` on demand (don't pre-load).

## 12. Performance & Caching

- List APIs return only primary thumb/medium URLs. No image arrays. No base64.
- Static files served with `Cache-Control: public,max-age=2592000,immutable`. File names contain the image id, so a "new" image gets a different URL — safe to cache aggressively.
- All `<img>` tags use `loading="lazy"` and `decoding="async"`.
- Thumbnails are WEBP at q=70 (~10–25 KB typical). Medium WEBP at q=80 (~50–120 KB).
- Frontend uses an `<img>` with `(error)` handler that swaps in the placeholder SVG instead of a broken-image icon.
- Product search/dropdown calls don't touch image collections — they read only `primaryImageThumb` from the existing product list endpoint.

## 13. Security

- File extension and MIME both validated server-side; mismatches rejected.
- Decoded dimensions checked **before** writing files.
- File names are server-generated (image id) — no path traversal possible from user input.
- The original filename is stored only as text metadata, never used in path.
- Anonymous reads of `/uploads/**` are fine (images are public). Writes/deletes require `Products.Edit`.

## 14. Migration Plan

1. **`AddProductImagesTable`** — creates `product_images` with all columns and the unique-primary partial index.
2. **`DropProductImageUrl`** — drops `products.image_url`. (Per user decision: no backfill.)
3. **`.gitignore`** — add `src/RetailPOS.API/wwwroot/uploads/` to keep upload directory out of source control.

Existing seeded products simply lose their previously-shown image URL. Re-uploads fix them.

## 15. Testing Plan

### Unit

- `ProductMediaService.ValidateAsync` accepts in-range dimensions, rejects out-of-range, rejects invalid MIME/type, rejects oversized files.
- `SetPrimaryAsync` clears prior primary in same product and sets new one (single transaction).
- `DeleteAsync` removes DB row, removes 3 files from disk, auto-promotes next primary when deleting the current one.

### Integration

- POST upload of a 1000×1000 PNG → 201 with valid `thumbUrl`/`mediumUrl`/`originalUrl`. Files exist on disk. Row exists in DB. `is_primary=true` for first upload.
- POST upload of a 500×500 image → 400 with structured error.
- POST upload of a 5.1 MB image → 400.
- DELETE primary → next image becomes primary (verify row update + DB partial index satisfied).
- Concurrent set-primary calls → DB unique partial index serializes; no two rows are primary.

### UI

- Upload flow: drag-drop and file picker both work, error states render inline, primary chip moves on Set Primary, deleting primary auto-shifts.
- Product list shows primary thumbnail; clicking opens lightbox; placeholder shows when no primary.
- Sales cart row shows thumbnail; lazy-loading works (verify via Network tab when scrolling).

## 16. Implementation Checklist

- [ ] Create `ProductImage` entity in `RetailPOS.Core/Entities/`.
- [ ] Add `DbSet<ProductImage>` + mapping in `RetailPOSDbContext`.
- [ ] Migration `AddProductImagesTable` (with unique partial index).
- [ ] Migration `DropProductImageUrl`.
- [ ] Add `SixLabors.ImageSharp` NuGet to `RetailPOS.API`.
- [ ] `IProductMediaService` + impl in `RetailPOS.API/Services/`.
- [ ] `ProductImagesController` with the four endpoints.
- [ ] Update `ProductDto`, `ProductService.MapToDto`, `ProductService.GetAll`/`GetById` to populate `primaryImageThumb` / `primaryImageMedium`.
- [ ] Static-file serving with cache headers in `Program.cs`.
- [ ] `wwwroot/uploads/` excluded from git.
- [ ] Frontend: `product-image.model.ts`, `product-image.service.ts`.
- [ ] Frontend: `ProductImageUploaderComponent` and `ImageLightboxComponent`.
- [ ] Update `product-form.ts` to use the uploader (remove URL input).
- [ ] Update `product-list.ts` to show primary thumbnail + lightbox.
- [ ] Update POS/sales screens (search dropdown, cart rows).
- [ ] Update purchase flow (search dropdown, items table).
- [ ] Add tests per §15.
- [ ] End-to-end smoke test against running stack.
