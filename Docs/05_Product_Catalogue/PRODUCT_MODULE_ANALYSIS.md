# Product Module — Current State Analysis

> **Document Type:** Existing-system analysis & requirement reference  
> **Status:** Draft — for developer and business review  
> **Date:** June 2026  
> **Purpose:** Capture the full current state of the Product module before any further design or feature work begins.

---

## Table of Contents

1. [Product Module Overview](#1-product-module-overview)
2. [Existing Product Features](#2-existing-product-features)
3. [Category / Subcategory Structure](#3-category--subcategory-structure)
4. [Attributes, Variations, Sizes, Colours, Styles](#4-attributes-variations-sizes-colours-styles)
5. [Supplier and Purchase-Related Product Data](#5-supplier-and-purchase-related-product-data)
6. [Stock-Related Product Data](#6-stock-related-product-data)
7. [Product Status Logic](#7-product-status-logic)
8. [Product Listing, Filtering, Searching and Sorting](#8-product-listing-filtering-searching-and-sorting)
9. [Product Create / Edit / View Flow](#9-product-create--edit--view-flow)
10. [Database Tables and Relationships](#10-database-tables-and-relationships)
11. [Existing API Endpoints](#11-existing-api-endpoints)
12. [Current UI Pages and Components](#12-current-ui-pages-and-components)
13. [Known Gaps, Issues and Inconsistencies](#13-known-gaps-issues-and-inconsistencies)
14. [Recommended Improvement Areas](#14-recommended-improvement-areas)
15. [Questions and Decisions Needed](#15-questions-and-decisions-needed)

---

## 1. Product Module Overview

The Product module is the central catalogue for RetailPOS. Every sellable item tracked in the system — whether at the POS, in a purchase order, in a warehouse transfer, or in a stock adjustment — is ultimately tied to a **ProductVariant**, which belongs to a **Product**.

The module spans:

- A two-level data model: **Product** (master record) → **ProductVariant** (a specific sellable SKU).
- A global **Variation** engine (Color, Size, Material, etc.) that is reusable across products.
- A **Category** hierarchy (unlimited depth, one parent per category).
- A multi-image media system per product (primary + additional images, stored as WEBP at three resolutions).
- Integration points with Inventory, Purchase Orders, GRNs, Sales, Stock Transfers, and Stock Adjustments — all of which operate at the **ProductVariant** level, never directly on the Product.
- A Pricing Engine with outlet-level price overrides (`OutletPriceOverride`) and time-bound campaign/discount rules (`PriceRule`).

---

## 2. Existing Product Features

| Feature | Status | Notes |
|---|---|---|
| Create product (basic fields) | ✅ Implemented | Name, Description, SKU, Barcode, Category, Base Price, Cost Price, Tax Rate, Active flag |
| Edit product | ✅ Implemented | Same fields as create |
| Delete product | ✅ Implemented | Hard delete; blocked if linked to sale items or stock records |
| Product listing with pagination | ✅ Implemented | 10 per page default, configurable |
| Search (name, SKU, barcode, description) | ✅ Implemented | Server-side, case-insensitive |
| Filter by category, status, variant presence, price range | ✅ Implemented | All filters combinable |
| Sort by name, price, category, created date | ✅ Implemented | Asc/Desc |
| URL-preserving filter state | ✅ Implemented | Filters and pagination persist in query params |
| SKU auto-generation | ✅ Implemented | API endpoint generates from product name |
| Product images (multi-image, WEBP, thumbnail/medium/original) | ✅ Implemented | Up to ~20 images; one primary image; stored on local disk |
| Product variants (combinations of variation options) | ✅ Implemented | Variant has own SKU, barcode, price/cost adjustments |
| Global Variations manager (Color, Size, etc.) | ✅ Implemented | Reusable across all products |
| Assign variations to a product | ✅ Implemented | Select variation types + which options apply to this product |
| Auto-generate variant combinations | ✅ Implemented | Cartesian product of selected options |
| Manual variant creation | ✅ Implemented | Single combination created from modal |
| Edit/Delete individual variants | ✅ Implemented | From combination manager |
| Variant-level inventory tracking | ✅ Implemented | Per location (outlet or warehouse) |
| Variant-level pricing (price adjustment from base) | ✅ Implemented | `PriceAdjustment` on variant |
| Outlet-level price override per variant | ✅ Implemented | `OutletPriceOverride` entity (fixed or margin-based) |
| Discount / campaign price rules | ✅ Implemented | `PriceRule` entity — by variant, product, category, outlet, or global |
| Main Product Code (`ProductCode` field) | ✅ Implemented | Optional unique business code at product level; stored as uppercase; partial unique DB index; shown in product list, form, all variant DTOs, and variant search results |
| Product view (detail page) | ✅ Implemented | `/products/:id` — read-only detail page with image gallery, pricing, variant table, and quick links |
| Draft / Pending product status | ❌ Not implemented | Only `IsActive` boolean; no draft workflow |
| Bulk import / export | ❌ Not implemented | |
| Product tags / labels | ❌ Not implemented | |
| Product supplier linkage (direct) | ❌ Not implemented | Supplier is linked via Purchase Orders, not directly on a product |
| Variant-level images | ❌ Not implemented | All images are at the product level; variants inherit the primary product image |
| Low stock indicator on product list | ⚠️ Partial | `low_stock_threshold` field exists on `inventories`; Low Stock Alerts page (`/inventory/low-stock`) and Low Stock Report exist. The product list shows only Out-of-Stock vs in-stock — no per-product low-stock badge based on threshold. |

---

## 3. Category / Subcategory Structure

### Entity: `Category`

```
categories
├── id           bigserial PK
├── name         varchar(100) NOT NULL
├── description  varchar(500)
├── parent_category_id  bigint FK → categories(id)  [self-reference, ON DELETE RESTRICT]
├── image_url    varchar(500)
├── display_order integer DEFAULT 0
├── is_active    boolean DEFAULT true
├── created_at   timestamptz
└── updated_at   timestamptz
```

### Key Behaviours

- **Unlimited depth** — the data model supports unlimited nesting through the `parent_category_id` self-reference.
- **Restrict on delete** — a category cannot be deleted if it has child categories.
- **Flat and tree APIs** — `GET /api/categories` returns a flat list; `GET /api/categories/tree` returns a hierarchical structure.
- In the **product form**, the category dropdown is loaded from the flat list only (the tree API exists but is unused in product UI).
- A product belongs to exactly **one category**. There is no multi-category tagging.
- The `image_url` field exists on the category entity but there is no image-upload UI for categories.
- `display_order` field exists but is not used in any visible sorting in the current UI.

### Gap

The product form shows all categories in a single flat dropdown, with no visual indication of parent/child hierarchy. When there are many categories, this list becomes unwieldy.

---

## 4. Attributes, Variations, Sizes, Colours, Styles

The variation system uses a **three-tier model**:

```
Global Variation Types (e.g. "Color", "Size")
  └── Global Variation Options (e.g. "Red", "Blue", "S", "M", "L")
        └── Product-level selection (which types and options apply to THIS product)
              └── Product Variants (each combination = one SKU row)
```

### 4.1 Global Variation Types — `variations` table

| Column | Type | Notes |
|---|---|---|
| `id` | bigserial PK | |
| `name` | varchar(100) | e.g. "Color", "Size", "Material" |
| `display_order` | integer | |
| `is_active` | boolean | |

Managed via `/variations` page. Variations are **business-level** and shared across all products.

### 4.2 Global Variation Options — `variation_options` table

| Column | Type | Notes |
|---|---|---|
| `id` | bigserial PK | |
| `variation_id` | bigint FK | → `variations(id)` CASCADE |
| `name` | varchar(100) | e.g. "Red", "M", "Cotton" |
| `price_adjustment` | decimal(18,2) | Option-level price delta (currently overridden at variant level) |
| `display_order` | integer | |
| `is_active` | boolean | |

### 4.3 Assigning Variations to a Product — `product_variations` table

Junction table linking a product to which variation types it uses (e.g. Product A uses "Color" and "Size").

| Column | Notes |
|---|---|
| `product_id` | FK → `products` |
| `variation_id` | FK → `variations` |
| `is_required` | boolean (default true) — not currently used in UI logic |

### 4.4 Selected Options per Product — `product_variation_selected_options` table

Stores which specific options a product uses for each of its assigned variation types. This scopes combination generation to the product's subset, not all global options.

| Column | Notes |
|---|---|
| `product_id` | FK → `products` |
| `variation_id` | FK → `variations` |
| `option_id` | FK → `variation_options` |

Unique index on `(product_id, variation_id, option_id)`.

### 4.5 Product Variants / Combinations — `product_variants` table

Each row is one sellable SKU, representing a specific combination of option values.

| Column | Type | Notes |
|---|---|---|
| `id` | bigserial PK | |
| `product_id` | bigint FK | → `products(id)` CASCADE |
| `name` | varchar(255) | e.g. "Red / M" — auto-built from option names |
| `sku` | varchar(50) UNIQUE | Required |
| `barcode` | varchar(50) UNIQUE | Optional |
| `attributes` | jsonb | Legacy JSON snapshot e.g. `{"Color":"Red","Size":"M"}` |
| `price_adjustment` | decimal(10,2) | Added to product's `base_price` to get final price |
| `cost_adjustment` | decimal(10,2) | Added to product's `cost_price` |

### 4.6 Variant ↔ Option Link — `product_variant_options` table

Junction table tracking which `variation_option` rows are part of a specific `product_variant`. This is the structured equivalent of the `attributes` JSON field.

| Column | Notes |
|---|---|
| `variant_id` | FK → `product_variants` CASCADE |
| `option_id` | FK → `variation_options` CASCADE |

### 4.7 Combination Manager UI

The Combination Manager is embedded in the **Edit Product** page (not available in Create mode until after the product is saved). It allows:

1. Selecting which global variation types apply to the product.
2. Selecting which options (within each type) apply.
3. Auto-generating all cartesian-product combinations.
4. Manually adding a single custom combination.
5. Editing an existing combination (SKU, barcode, price/cost adjustments).
6. Deleting an individual combination.
7. Warning the user when existing combinations will become invalid after option changes.

### 4.8 Final Price Calculation

```
FinalPrice = product.BasePrice + variant.PriceAdjustment
           → then optionally overridden by OutletPriceOverride (fixed or margin)
           → then optionally discounted by a matching PriceRule
```

---

## 5. Supplier and Purchase-Related Product Data

Products/variants do **not** store a default supplier directly. The supplier relationship is established through the **Purchase Order** flow:

```
Supplier → PurchaseOrder → PurchaseOrderItem → ProductVariant
```

### Relevant Entities

| Entity | Key Fields | Notes |
|---|---|---|
| `Supplier` | name, contact, address, credit_limit | No direct FK to Product |
| `PurchaseOrder` | po_number, supplier_id, warehouse_id, order_date, expected_delivery, status, total_amount | Status: draft → pending → approved → partially_received / fully_received → completed / cancelled / rejected |
| `PurchaseOrderItem` | po_id, **variant_id**, quantity, unit_price, discount %, tax %, unit | Links to `product_variants` not `products` |
| `Grn` | grn_number, po_id, received_date | Goods Receipt Note |
| `GrnItem` | grn_id, po_item_id, quantity_received | Actual received quantity |

### Key Observations

- Purchase Order Items reference `ProductVariant`, meaning a variant must exist before it can be ordered.
- There is no "preferred supplier" or "supplier product code" field on the product or variant.
- Cost price on the PO item (`unit_price`) is independent from `product_variants.cost_adjustment` — they are not synchronised automatically.
- The PO form uses a **variant search** endpoint (`GET /api/products/variants/search`) to find and add line items.

---

## 6. Stock-Related Product Data

Stock is tracked at the **variant + location** level. There is no product-level stock aggregate stored — it is computed on demand.

### 6.1 Inventory Table

```
inventories
├── variant_id        bigint FK → product_variants
├── location_id       bigint  (outlet or warehouse ID)
├── location_type     varchar(20)  CHECK IN ('outlet', 'warehouse')
├── quantity          integer DEFAULT 0
├── low_stock_threshold  integer DEFAULT 10
├── batch_number      varchar(50)
├── expiry_date       date
└── UNIQUE(variant_id, location_id, location_type)
```

Optimistic concurrency is applied via PostgreSQL's `xmin` system column to prevent double-decrement on concurrent sales.

### 6.2 Stock Ledger

Every inventory balance change is recorded in `stock_ledgers`:

| Field | Notes |
|---|---|
| `variant_id` | Which variant changed |
| `location_id` / `location_type` | Which location |
| `transaction_type` | e.g. sale, grn, transfer_in, transfer_out, adjustment |
| `qty_in` / `qty_out` | Movement quantities |
| `balance_after` | Snapshot of balance after this movement |
| `reference_type` / `reference_id` | Source document (Sale, GRN, StockTransfer, etc.) |
| `remarks` | Free-text note |

### 6.3 Stock Movement Sources

| Source | Effect |
|---|---|
| GRN (Goods Receipt) | Increases variant stock at a warehouse |
| Sale | Decreases variant stock at an outlet |
| Stock Transfer | Decreases source location, increases destination |
| Stock Adjustment | Manual increase or decrease with reason |

### 6.4 ProductDto Stock Fields

The `ProductDto` returned by the API includes:
- `VariantCount` — number of variants
- `TotalStock` — sum of all inventory quantities across all locations for all variants of the product

---

## 7. Product Status Logic

### Current Implementation

The Product entity has a single boolean field:

```csharp
public bool IsActive { get; set; } = true;
```

| State | Behaviour |
|---|---|
| `IsActive = true` | Product appears in product listings; available in POS product search; can be added to purchase orders |
| `IsActive = false` | Product is hidden from default listing (default filter: `isActive = true`); not selectable in POS |

### What Is NOT Implemented

- **Draft state** — There is no "draft" status. All created products are either active or inactive. A product cannot be saved as a draft pending review.
- **Archived state** — There is no archived or discontinued status separate from inactive.
- **Approval workflow** — No multi-step product approval process exists.

### Filter Behaviour

The product list defaults to showing `isActive = true` products. The user can change the filter to show inactive or all products. This is restored from URL query parameters on navigation.

---

## 8. Product Listing, Filtering, Searching and Sorting

### Search Endpoint

`POST /api/products/search` with body `ProductSearchDto`:

| Field | Type | Notes |
|---|---|---|
| `searchQuery` | string? | Matches against name, description, SKU, barcode, product code (case-insensitive, contains) |
| `categoryId` | long? | Exact match |
| `isActive` | bool? | null = all, true = active only, false = inactive only |
| `hasVariants` | bool? | Filter products with or without variants |
| `minPrice` | decimal? | BasePrice >= minPrice |
| `maxPrice` | decimal? | BasePrice <= maxPrice |
| `pageNumber` | int | Default 1 |
| `pageSize` | int | Default 20 (UI default: 10) |
| `sortBy` | string | `name` (default), `price`, `category`, `createdat` |
| `sortOrder` | string | `asc` (default), `desc` |

### Response

`ProductListDto`:
- `Products` — list of `ProductDto` (includes primary image thumb/medium URLs, variant count, total stock)
- `TotalCount`, `PageNumber`, `PageSize`, `TotalPages`, `HasNextPage`, `HasPreviousPage`

### UI Behaviour

- Filter state is preserved in URL query parameters via `ListStateService`.
- Navigating away and back restores the exact search state (including page number and sort).
- Search input has a **500ms debounce** before firing the API call.
- Category and Status dropdowns trigger search immediately on change (no debounce).
- Default view on page load: active products only, sorted by name ascending, page 1.
- "Clear Filters" resets all filters and clears URL params.

### Sort Columns Available in UI

| Column Header | `sortBy` value |
|---|---|
| Name | `name` |
| Category | `category` |
| Price | `price` |
| Created Date | `createdat` |

### Product List Table Columns

Product Code (indigo badge, shown when set), Name, SKU/Barcode, Category, Price, Status (Active badge), Variant count, Stock total, Actions (Edit, Delete)

---

## 9. Product Create / Edit / View Flow

### Routes

| Route | Component | Permission |
|---|---|---|
| `/products` | `ProductList` | `products.view` |
| `/products/create` | `ProductForm` | `products.create` |
| `/products/edit/:id` | `ProductForm` (edit mode) | `products.edit` |
| `/products/:id` | `ProductDetailComponent` | `products.view` |

> **Note:** There is no read-only product detail/view page. The only way to see full product detail is to open the edit form.

### Create Flow

1. User navigates to `/products/create`.
2. Fills in: Name, Description, Category, SKU (or auto-generate), Barcode, Base Price, Cost Price, Tax Rate, Active status.
3. Image uploader is present but **only active after save** (no product ID exists before first save).
4. On submit, `POST /api/products` is called.
5. On success, user is redirected to `/products/edit/:id` so images and combinations can be managed.

### Edit Flow

1. User navigates to `/products/edit/:id`.
2. All basic fields are pre-loaded.
3. Image uploader is active — can upload, delete, and set primary image.
4. **Combination Manager** is shown (only in edit mode, when `productId` is non-null):
   - Assign variation types.
   - Select specific options per type.
   - Save assignment.
   - Generate or manually create combinations/variants.
   - Edit individual variant SKU/barcode/price adjustment.
   - Delete individual variants.
5. On submit, `PUT /api/products/:id` is called.
6. On success, user is navigated back.

### Validation Rules (Product Form)

| Field | Rules |
|---|---|
| Name | Required, 2–255 characters |
| Category | Required (non-zero selection) |
| Base Price | Required, > 0 |
| Tax Rate | 0–100 |
| Product Code | Optional, max 50 chars, must be unique if provided; stored as uppercase |
| SKU | Optional, max 50 chars, must be unique |
| Barcode | Optional, max 50 chars, must be unique |
| Description | Optional, max 1000 chars |
| Cost Price | Optional, 0–999,999,999 |

### Image Upload Rules

- Accepted types: JPEG, PNG, WEBP
- Max size: 5 MB
- Min dimensions: 800×800 px
- Max dimensions: 1200×1200 px
- Up to ~20 images per product
- Exactly one primary image (enforced at DB level via partial unique index)
- Images stored at three resolutions: original, medium (600×600 WEBP), thumbnail (200×200 WEBP)

---

## 10. Database Tables and Relationships

### Product-Domain Tables

| Table | Description |
|---|---|
| `categories` | Product categories with self-referencing hierarchy |
| `products` | Master product records |
| `product_variants` | Individual sellable SKU combinations |
| `variations` | Global variation types (Color, Size, etc.) |
| `variation_options` | Values within a variation type |
| `product_variations` | Which variation types are assigned to a product |
| `product_variation_selected_options` | Which specific options a product uses per variation type |
| `product_variant_options` | Junction: which options compose each variant |
| `product_images` | Multi-resolution images per product |
| `inventories` | Stock levels per variant per location |

### Related Tables (downstream consumers)

| Table | Relationship to Product |
|---|---|
| `purchase_order_items` | FK → `product_variants.id` |
| `grn_items` | FK → `purchase_order_items` → variants |
| `sale_items` | FK → `product_variants.id` |
| `stock_adjustments` | FK → `product_variants.id` |
| `stock_transfer_items` | FK → `product_variants.id` |
| `stock_ledgers` | FK → `product_variants.id` |
| `outlet_price_overrides` | FK → `product_variants.id` |
| `price_rules` | `target_id` references product, variant, or category |

### Entity Relationship Summary

```
Category (self-ref tree)
  └── Product (1:many)
        ├── ProductImage (1:many)
        ├── ProductVariation (M2M → Variation)
        │     └── Variation
        │           └── VariationOption
        ├── ProductVariationSelectedOption (product scoped options)
        └── ProductVariant (1:many)
              ├── ProductVariantOption (M2M → VariationOption)
              ├── Inventory (1:many, per location)
              ├── PurchaseOrderItem (1:many)
              ├── SaleItem (1:many)
              ├── StockAdjustment (1:many)
              ├── StockTransferItem (1:many)
              ├── StockLedger (1:many)
              └── OutletPriceOverride (1:many)
```

### Key DB Constraints

| Table | Constraint |
|---|---|
| `products.product_code` | Partial unique index (`WHERE product_code IS NOT NULL`) |
| `products.barcode` | Unique |
| `product_variants.sku` | Unique |
| `product_variants.barcode` | Unique |
| `inventories` | Unique on `(variant_id, location_id, location_type)` |
| `product_images` | Partial unique index on `(product_id) WHERE is_primary` |
| `product_variation_selected_options` | Unique on `(product_id, variation_id, option_id)` |
| `outlet_price_overrides` | One active override per `(outlet_id, product_variant_id)` |

---

## 11. Existing API Endpoints

### Products — `/api/products`

| Method | Path | Permission | Description |
|---|---|---|---|
| `GET` | `/api/products` | `products.view` | Get all products (no pagination) |
| `POST` | `/api/products/search` | `products.view` | Search with filters and pagination |
| `GET` | `/api/products/{id}` | `products.view` | Get product by ID |
| `GET` | `/api/products/sku/{sku}` | `products.view` | Get product by SKU |
| `GET` | `/api/products/barcode/{barcode}` | `products.view` | Get product by barcode |
| `GET` | `/api/products/category/{categoryId}` | `products.view` | Get products in a category |
| `POST` | `/api/products` | `products.create` | Create a product |
| `PUT` | `/api/products/{id}` | `products.edit` | Update a product |
| `DELETE` | `/api/products/{id}` | `products.delete` | Delete a product |
| `POST` | `/api/products/generate-sku` | `products.view` | Generate a SKU from product name |
| `GET` | `/api/products/variants/search` | `products.view` | Search variants (for PO/GRN forms) |

### Product Images — `/api/products/{productId}/images`

| Method | Path | Permission | Description |
|---|---|---|---|
| `GET` | `/api/products/{productId}/images` | `products.view` | List all images for a product |
| `POST` | `/api/products/{productId}/images` | `products.edit` | Upload an image (multipart, max 6 MB) |
| `DELETE` | `/api/products/{productId}/images/{imageId}` | `products.edit` | Delete an image (removes files from disk) |
| `PUT` | `/api/products/{productId}/images/{imageId}/primary` | `products.edit` | Set image as primary |

### Product Variations — `/api/products/{productId}/variations`

| Method | Path | Permission | Description |
|---|---|---|---|
| `GET` | `/api/products/{productId}/variations` | `products.view` | Get variation types assigned to a product |
| `POST` | `/api/products/{productId}/variations/assign` | `products.edit` | Assign variation types + selected options |
| `GET` | `/api/products/{productId}/variations/combinations` | `products.view` | Get all variants/combinations |
| `POST` | `/api/products/{productId}/variations/combinations/generate-all` | `products.edit` | Auto-generate all combinations |
| `POST` | `/api/products/{productId}/variations/combinations/generate-selected` | `products.edit` | Generate combinations for specified variations |
| `POST` | `/api/products/{productId}/variations/combinations` | `products.edit` | Create a manual combination |
| `PUT` | `/api/products/{productId}/variations/combinations/{variantId}` | `products.edit` | Update a combination |
| `DELETE` | `/api/products/{productId}/variations/combinations/{variantId}` | `products.edit` | Delete a combination |

### Variations (Global) — `/api/variations`

| Method | Path | Permission | Description |
|---|---|---|---|
| `GET` | `/api/variations` | `products.view` | Get all variation types |
| `GET` | `/api/variations/{id}` | `products.view` | Get variation type by ID |
| `POST` | `/api/variations` | `products.edit` | Create a variation type |
| `PUT` | `/api/variations/{id}` | `products.edit` | Update a variation type |
| `DELETE` | `/api/variations/{id}` | `products.edit` | Delete a variation type |
| `POST` | `/api/variations/{variationId}/options` | `products.edit` | Add an option to a variation |
| `PUT` | `/api/variations/options/{optionId}` | `products.edit` | Update a variation option |
| `DELETE` | `/api/variations/options/{optionId}` | `products.edit` | Delete a variation option |

### Categories — `/api/categories`

| Method | Path | Permission | Description |
|---|---|---|---|
| `GET` | `/api/categories` | `categories.view` | Get flat list of all categories |
| `GET` | `/api/categories/tree` | `categories.view` | Get hierarchical category tree |
| `GET` | `/api/categories/{id}` | `categories.view` | Get category by ID |
| `GET` | `/api/categories/{id}/children` | `categories.view` | Get children of a category |
| `POST` | `/api/categories` | `categories.create` | Create a category |
| `PUT` | `/api/categories/{id}` | `categories.edit` | Update a category |
| `DELETE` | `/api/categories/{id}` | `categories.delete` | Delete a category |

### Pricing — `/api/pricing`

| Method | Path | Description |
|---|---|---|
| `GET/POST/PUT/DELETE` | `/api/pricing/rules` | Manage PriceRule records |
| `GET/POST/PUT/DELETE` | `/api/pricing/outlet-overrides` | Manage OutletPriceOverride records |

---

## 12. Current UI Pages and Components

### Product Detail — `ProductDetailComponent` component

**Route:** `/products/:id`

| Section | Detail |
|---|---|
| Header | Breadcrumb (Products › Name) + Back + Edit buttons |
| Image gallery | Active large image (click = lightbox); thumbnail strip when >1 image |
| Identification | Product Code (indigo badge), SKU, Barcode, Category, System ID |
| Pricing | Selling Price, Cost Price, Profit Margin %, Tax Rate |
| Description | Free-text block; hidden when empty |
| Variants table | Shown only when `hasVariants = true`; columns: Variant name, SKU, Barcode, Price Adj., Final Price |
| Status sidebar | Active/Inactive badge; Single SKU or Variant count badge; Total Stock badge |
| Timestamps | Created and Last Updated dates |
| Quick links | Stock Transactions report (filtered by product), Inventory (filtered by product) |

### Product List — `ProductList` component

**Route:** `/products`

| Section | Detail |
|---|---|
| Header | Title + "Add Product" button |
| Filter panel | Search (text, debounced), Category (dropdown), Status (Active/Inactive/All), Variants (With/Without/All), Min Price, Max Price, Clear Filters |
| Loading state | Spinner |
| Error state | Inline error alert |
| Data table | Product image thumbnail (clickable lightbox), Name, Product Code (indigo badge), SKU/Barcode, Category, Price, Active badge, Variant count, Stock total, Action buttons (Edit, Delete) |
| Pagination | Page numbers with Previous/Next; page size selector |
| Sorting | Clickable column headers (Name, Category, Price); sort indicator icons |

The list defaults to **active products** on load. Inactive products must be explicitly selected.

### Product Form — `ProductForm` component

**Routes:** `/products/create` and `/products/edit/:id`

Sections (in order):
1. **Basic Information** — Name, Description, Category, Image Uploader (active in edit mode only), Active checkbox
2. **Identification** — Product Code (unique, auto-uppercased), SKU (with auto-generate button), Barcode
3. **Pricing** — Base Price (with currency symbol), Cost Price, Tax Rate (%)
4. **Combination Manager** — shown only in edit mode (embedded `CombinationManagerComponent`)

### Image Uploader — `ProductImageUploaderComponent`

- Embedded in the Product Form.
- Displays existing images with "Set Primary" and "Delete" buttons.
- Accepts drag-and-drop or click-to-select file upload.
- Shows inline validation (size, dimensions, type) before upload.
- Primary image shown with a highlighted border.

### Image Lightbox — `ImageLightboxComponent`

- Triggered from the product list by clicking a product's thumbnail.
- Displays the medium-resolution image in an overlay.

### Combination Manager — `CombinationManagerComponent`

- Embedded in edit product form.
- Left panel: variation type selection with per-type option checkboxes.
- Right panel: table of existing combinations (SKU, options, price adjustment, cost adjustment, final price).
- Buttons: Save Variation Assignment, Generate All Combinations, Generate Selected, Add Manual Combination.
- Edit modal: update SKU, barcode, price/cost adjustments for a specific combination.
- Delete individual combinations.
- Warning shown if saved assignment would orphan existing combinations.

### Variations Page — `VariationsComponent`

**Route:** `/variations` (guarded by `products.view`)

- Lists all global variation types in a table.
- Inline option list per variation type.
- Modals for creating/editing variation types and their options.
- Shows which products use a given variation type (via "in-use" modal) before deletion.

### Category Pages — `CategoryListComponent`, `CategoryFormComponent`

**Routes:** `/categories`, `/categories/create`, `/categories/edit/:id`

- CRUD for categories.
- Category form allows setting parent category (flat list, no tree picker).
- No image upload UI for categories.

---

## 13. Known Gaps, Issues and Inconsistencies

### 1. ~~No Product View Page~~ — Resolved

A read-only `ProductDetailComponent` has been created at `/products/:id` (June 2026). The route is registered with `products.view` permission, ordered after `/products/edit/:id` to prevent route conflicts.

**Component features:**
- Image gallery with thumbnail strip and click-to-enlarge lightbox.
- Identification panel: Product Code (indigo badge), SKU, Barcode, Category, System ID.
- Pricing panel: Selling Price, Cost Price, Profit Margin %, Tax Rate.
- Description block (shown only if present).
- Variants table: Variant name, SKU, Barcode, Price Adjustment, Final Price (shown only when product has variants).
- Status / Stock summary sidebar card.
- Quick links to Stock Transactions report and Inventory filtered by product.
- Breadcrumb navigation + back button; Edit button navigates to `/products/edit/:id`.
- Product list rows remain click-navigable to detail; a dedicated eye icon (👁) button added to the action column.

### 2. Image Upload Not Available on Create
The image uploader is rendered in the create form but it is disabled (`[productId]="null"`). Images can only be uploaded after the product is first saved. This creates a two-step process that is not explained in the UI.

### 3. Dual Attribute Storage (attributes JSON + ProductVariantOption rows)
Each variant stores its option combination in **two places**:
- `product_variants.attributes` — a legacy JSON blob (`{"Color":"Red","Size":"M"}`)
- `product_variant_options` — normalised rows linking `variant_id` to `option_id`

These can drift out of sync. It is unclear which is the authoritative source.

### 4. SKU Uniqueness at Variant Level Only
A product-level SKU (`products.sku`) is optional and not unique-indexed in the database. Only `product_variants.sku` has a unique constraint. This creates ambiguity: two products could have the same SKU at the product level.

### 5. No Draft / Pending State
All products are either active or inactive immediately. There is no workflow to create a product in a pending-review state, which may matter for businesses where a buyer must approve products before they appear in the POS.

### 6. No Direct Supplier Link on Product
There is no way to see which supplier typically supplies a given product without going through purchase orders. There is no preferred-supplier or default-cost-from-supplier feature.

### 7. Category Dropdown Is a Flat List
The category form and product form both use a flat dropdown. With a large category tree, selecting the right subcategory is difficult. The tree API exists but is not used in the product form.

### 8. No Bulk Operations
There is no bulk activate/deactivate, bulk delete, or bulk import/export for products.

### 9. Combination Manager Only in Edit Mode
Variants cannot be created during product creation. The user must first save the product and then manage combinations. There is no warning about this flow on the create page.

### 10. Variant-Level Images Not Supported
All images are at the product level. A customer-facing feature like "show the red colour variant image" is not possible without variant-level image support.

### 11. `GetAll` Returns All Products Without Pagination
`GET /api/products` returns every product in one response with no pagination. This will become a performance problem as the catalogue grows.

### 12. Tax Rate Is Stored on Product Only
Tax rate is stored on the product. There is no per-variant tax rate override and no outlet-specific tax rate. For multi-jurisdiction businesses, this may be insufficient.

### 13. Category `display_order` Not Used
The `display_order` field on the Category entity is stored but never applied to any list rendering in the current UI.

### 14. `is_required` Flag on `product_variations` Is Unused
The `is_required` column on the `product_variations` table is written (defaulting to `true`) but no UI or business logic reads or enforces it.

### 15. Delete Uses Browser `confirm()`
Product deletion uses the browser's native `confirm()` dialog. The rest of the app uses a custom `AlertService.confirm()` pattern, making this inconsistent.

### 16. ~~Main Product Code Missing in Product/Variant Display~~ — Partially Resolved

> **Status:** Core implementation complete June 2026. Remaining work: surface `ProductCode` in POS, Purchase Order lines, GRN lines, Inventory table, Stock Transfer, Stock Adjustment, and all reports.

A dedicated `product_code` field has been added to the `Product` entity — optional, uppercase, with a partial unique index. All 43 existing products have been seeded with codes in `ABA###P` format (e.g. `ABA001P` → `ABA043P`, ordered by product ID).

**Resolved items:**

- ✅ `product_code VARCHAR(50) NULL` column added to `products` table; partial unique index (`WHERE product_code IS NOT NULL`) applied via EF Core migration.
- ✅ `ProductCode` included in `ProductDto`, `ProductVariantDto`, and `ProductVariantSearchDto` — downstream consumers receive it without a separate lookup.
- ✅ Product form (create and edit) exposes a Product Code input field (auto-uppercased; uniqueness validated on save).
- ✅ Product list shows a `ProductCode` column with an indigo monospace badge.
- ✅ Variant search endpoint (`GET /api/products/variants/search`) returns `ProductCode` on each result row.
- ✅ All existing products seeded with `ABA###P` format codes.

**Still pending:**

- POS product search / sale line items — `ProductCode` not yet surfaced.
- Purchase Order and GRN line items — `ProductCode` not yet shown.
- Inventory table, Stock Transfer items, Stock Adjustment rows — `ProductCode` not yet shown.
- All product-related reports (Sales, Purchase, Inventory, Stock Ledger, etc.) — `ProductCode` column not yet added.

---

## 14. Recommended Improvement Areas

### High Priority

| Item | Reason |
|---|---|
| ✅ Add a read-only Product Detail/View page | Done — `ProductDetailComponent` at `/products/:id`; eye icon added to product list action bar |
| Enable image upload at product creation | Remove two-step friction for new products |
| Deprecate the `attributes` JSON field on variants | Use only the normalised `product_variant_options` as the authority |
| Fix flat category dropdown → tree picker | Usability degrades significantly with >20 categories |
| Add unique index on `products.sku` | Prevent duplicate product-level SKUs |
| Remove `GET /api/products` (no pagination) or add pagination | Performance risk at scale |
| ✅ `ProductCode` field added to `Product` entity | Done — `product_code VARCHAR(50)` with partial unique index; all 43 existing products seeded in `ABA###P` format |
| Surface `ProductCode` in all variant-based screens and reports | 🔄 Partial — product list and form done; POS, PO lines, GRN lines, Inventory, Stock Transfer, Adjustment, and reports still pending |
| ✅ Parent `ProductCode` included in all variant-based API DTOs | Done — `ProductDto`, `ProductVariantDto`, and `ProductVariantSearchDto` all return `ProductCode` |
| ✅ Unique index on `products.product_code` | Done — partial unique index (`WHERE product_code IS NOT NULL`) applied via EF Core migration |

### Medium Priority

| Item | Reason |
|---|---|
| Add Draft / Pending product status | Required for businesses with product approval workflows |
| Add preferred supplier field on product | Common in retail — reduces PO creation time |
| Add bulk product import (CSV/Excel) | High-volume catalogue onboarding |
| Enforce category `display_order` in dropdowns and lists | The field exists but has no effect |
| Replace browser `confirm()` with `AlertService.confirm()` on delete | Consistency with rest of app |
| Move `HasVariants` flag to a computed/derived value | Currently manual — a product could have variants but `HasVariants = false` |
| Update variant search dropdowns (PO, GRN, POS) to show Main Product Code alongside Variant SKU | Improves discoverability when products share similar variant names |

### Lower Priority

| Item | Reason |
|---|---|
| Variant-level images | Needed for colour/style visuals in POS and online |
| Barcode generation (auto, EAN-13) | Currently manual entry only |
| Product tags for cross-cutting filtering | Useful for promotions and reports |
| Per-variant tax rate override | Multi-jurisdiction or mixed-tax catalogues |
| Archived / Discontinued status | Distinguish temporarily inactive from end-of-life products |
| Category image upload UI | The `image_url` field on Category is unused by the UI |
| Remove or use the `is_required` flag on `product_variations` | Dead code — should be either implemented or removed |

---

## 14A. Product Identifier Standard (June 2026 Decision)

This section supersedes earlier open decisions around variant SKU and barcode assignment.

### 1. Main Product Code (Parent Product)

- Field: `products.product_code`
- Purpose: Business-facing parent code for all variants under a product.
- Format:
      - `[BRAND_OR_CATEGORY_PREFIX][SEQUENCE][PRODUCT_TYPE]`
      - Example: `ABA001P`
- Rules:
      - Stored uppercase.
      - Must be unique when provided (already enforced by partial unique index).
      - Required for products that are intended to generate/manage variants in PO/GRN/POS flows.

### 2. Variant SKU (Sellable Variant Identifier)

- Field: `product_variants.sku`
- Purpose: Human-readable unique variant identifier for operations and search.
- Format:
      - `[MAIN_PRODUCT_CODE]-[SIZE]-[COLOR_OR_ATTRIBUTE_CODE]`
      - Example: `ABA001P-56-BLK`
- Rules:
      - Must be unique.
      - Must be generated from the parent `ProductCode` and variant option attributes when not explicitly provided.
      - Must stay searchable in all variant lookup endpoints.

### 3. Variant Barcode (Numeric, EAN-13 Compatible)

- Field: `product_variants.barcode`
- Purpose: Scanner-friendly unique identifier for a specific variant.
- Target format:
      - 13-digit numeric code (EAN-13 compatible)
      - Internal base pattern:
            - `880` + 3-digit category code + 6-digit variant sequence + check digit
      - Example shape: `880101000001X` (where `X` is computed check digit)
- Rules:
      - Must be unique.
      - Must be auto-generated when missing.
      - Check digit must be computed by the system.

### 4. Search Standard (Variant Search)

`GET /api/products/variants/search` must support case-insensitive search by:

- Main Product Code (`products.product_code`)
- Variant SKU (`product_variants.sku`)
- Variant Barcode (`product_variants.barcode`)
- Product Name (`products.name`)
- Variant Name / Attributes (`product_variants.name`, `product_variants.attributes`)

Behavior requirement:

- Searching by Main Product Code returns all active variants under that parent product.

### 5. Backfill Requirement

For existing data:

- Backfill missing variant barcodes.
- Backfill missing/legacy variant SKUs to the standardized format where needed.
- Reject duplicates before update (SKU and barcode uniqueness must be preserved).

---

## 15. Questions and Decisions Needed

Before finalising any product design changes, the following must be confirmed with the business:

### Status and Workflow
1. Is a **Draft → Pending → Active** product workflow required, or is simple Active/Inactive sufficient?
2. Who is allowed to activate a product (any user with edit permission, or only a manager)?
3. Should inactive products still be purchaseable via Purchase Orders, or blocked entirely?

### Variants and Attributes
4. Is the `attributes` JSON field on `product_variants` still needed, or can it be removed in favour of the normalised `product_variant_options` table?
5. Should variants have their own images, or is inheriting the parent product's primary image acceptable for now?
6. What is the expected maximum number of variants per product? (Affects combination generation UX and performance.)
7. Should `HasVariants` be manually settable, or should it be automatically derived from whether variants exist?

### Categories
8. How deep should category nesting go in practice? (2 levels? 3? Unlimited?)
9. Should a product be assignable to multiple categories, or always exactly one?
10. Should category images be supported? (Backend field exists, no UI yet.)

### Pricing
11. Should tax rate be configurable per variant, or is product-level tax rate sufficient?
12. Is there a requirement for pricing per business (multi-tenant catalogue), or is pricing always global?

### Supplier / Purchase Linkage
13. Should a product have a "preferred supplier" or "default supplier" field?
14. Should cost price on the product/variant be automatically updated when a GRN is received at a different price?

### Search and Catalogue
15. Is a full-text search requirement (PostgreSQL `tsvector` or similar) anticipated as the catalogue grows?
16. Are any product catalogue exports (CSV/PDF) required for the business review process?

### Images
17. Is the current image size restriction (800–1200 px) appropriate for all business use cases?
18. Is local disk storage acceptable long-term, or is S3 / cloud storage planned?

### Main Product Code
19. Should **Main Product Code** be a mandatory field on every product, or optional? **→ Decision: Optional** — field is nullable; no mandatory enforcement.
20. Should Main Product Code be **globally unique** across the entire catalogue? **→ Decision: Yes** — partial unique index added (`WHERE product_code IS NOT NULL`); null values are permitted and excluded from the uniqueness check.
21. Is the existing `products.sku` field intended to serve as the Main Product Code, or do we need a **separate dedicated field** alongside SKU? **→ Decision: Separate field** — `product_code` is a dedicated column distinct from `sku`.
22. Should Variant SKU be **auto-generated** based on the Main Product Code (e.g. `PROD-001` → `PROD-001-RED-M`), or remain independently assigned? **→ Decision: Independently assigned** — variant SKUs are not derived from the product code. Initial seeding used `ABA###P` format (`ABA001P` – `ABA043P`).
23. Which specific reports must include Main Product Code as a **separate column** (e.g. Sales Report, Purchase Report, Stock Ledger, Inventory Report, Stock Movement Report)? _(Open — pending business confirmation.)_

---

*End of document. Review with the development team and product/business stakeholders before proceeding with new feature design.*
