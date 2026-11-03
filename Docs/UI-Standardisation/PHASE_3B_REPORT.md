# RetailPOS UI Standardisation — Phase 3B Report (Product Create/Edit Migration)

## Status: PASSED for automated gates; authenticated browser verification NOT performed

The Product Create/Edit screen (`/products/create`, `/products/edit/:id`, component `ProductForm`) and its two feature children — the image uploader and the variant (combination) manager — now use the shared UI kit. All fields, workflows, request payloads, validation rules and permission checks are unchanged. No backend, Flutter, route, guard, service, model, Product List or purchased Able Pro file was touched.

## Results

| Gate | Result |
|---|---|
| New tests | `product-form.spec.ts` **36**, `product-image-uploader.component.spec.ts` **16**, `combination-manager.component.spec.ts` **27**, +1 kit test (`ui-form-controls.spec.ts`) = **80** |
| Full unit suite | **PASS — 63 files / 608 tests** (Phase 3A baseline 60 / 528: +3 files, +80 tests). No existing test weakened; the only edit to an existing spec is one added test. |
| Production build | **PASS**, no warnings |
| Bundle | Initial total **2.59 MB raw / 407.27 kB transfer** (Phase 3A 2.58 MB / 405.67 kB → **+≈1.6 kB transfer**). `main` 394.69 → 396.45 kB; global CSS 81.11 → 79.58 kB (old utility classes no longer used by the templates are purged). |
| `git diff --check` | **PASS** (tracked files). The only trailing-whitespace lines in `product-form.ts` are pre-existing and untouched. |

## Files changed

| File | Change |
|---|---|
| `components/product-form/product-form.html` | Rewritten on the kit (was tracked and clean). Same fields, `name` attributes, `ngModel` signal bindings and order of logic. |
| `components/product-form/product-form.css` | Token-based layout styles (`pf-*`); previously two utility rules. |
| `components/product-form/product-form.ts` | Imports `UI_KIT`; **presentation-level additions only**: duplicate-submit guard at the top of `onSubmit()` and focus of the first invalid field after a failed validation. Create/update requests, validation rules, navigation and messages untouched. |
| `components/product-form/product-image-uploader.component.ts` | Template restyled (tokens, kit alert, Font Awesome icons instead of emoji glyphs) and keyboard/touch access fixed. Logic unchanged. |
| `components/combination-manager/combination-manager.component.{html,css}` | Rebuilt on the kit. |
| `components/combination-manager/combination-manager.component.ts` | Imports `UI_KIT`, adds a `variantColumns` constant for the table. No logic change (comments/encoding untouched). |
| `shared/ui/ui-control.directive.ts`, `ui-form-field.component.ts` | `[error]` on a form field now also marks its control invalid (`aria-invalid`, invalid styling). Needed because this form validates with its own `errors()` signal rather than Angular validators. +1 test. |
| `shared/ui/ui-data-table.component.ts` | CSS only: no empty toolbar bar when search is off and nothing is projected (used by the variants table). |
| 3 new spec files | See Tests. |

## UI components adopted

| Kit piece | Where |
|---|---|
| `app-ui-page-header` | Title, description, breadcrumbs (Dashboard › Products › Create/Edit) |
| `app-ui-card` | Basic Information, Identifiers, Classification, Pricing, Variants, Product Status, Media, Inventory, Setup Progress |
| `app-ui-form-field` + `appUiControl` | Every field: visible label tied to the control, required marker (visible `*` + screen-reader text), hints, live error region |
| `appUiButton` | Cancel, Save Draft, Save Product (loading + duplicate-submit protection), Generate SKU (icon-only, named), all variant actions |
| `app-ui-badge` | Product ID / New Product, status (text), checklist Done/To do, "Not saved yet", variant count in the variant manager |
| `app-ui-alert` | Server/submit error (`role="alert"`), draft note, image validation failures, queued-image notice, variant warnings |
| `app-ui-state` | "Variants unlock after the first save", loading in the variant manager |
| `app-ui-dialog` | Manual combination and Edit variant dialogs (focus trap, focus restore, labelled, Escape, mobile sheet) — replaces hand-built `fixed inset-0` overlays |
| `app-ui-data-table` (client mode, no search/paging) | Variants table with custom cells and named row actions |

Layout: 7/3 two-column grid from `lg`, single column below; Pricing 4 columns on `xl`, 2 on `sm`, 1 on phones; sticky action bar, compacted to one row on phones.

## Preserved functionality

- **Product code / SKU / barcode:** same fields, uppercase product-code presentation, SKU generator (`generateSku`) with its two existing native alerts.
- **Name, category, description, status** (Active/Draft/Inactive, draft note, `Save Draft` forces `draft`).
- **Pricing:** selling price, cost price and margin; **cost and margin only with `products.view_cost`**; tax rate 0–100. Margin is display-only. No pricing rules were added or changed. Size/colour option `priceAdjustment` values are shown exactly as the API returns them (`finalPrice` is never recalculated in the browser).
- **Validation (existing):** name required; category selected; selling price > 0; tax 0–100. Messages unchanged; now shown per field with `aria-invalid`, and focus moves to the first invalid field.
- **Requests unchanged:** create sends `name, description, productCode, sku, barcode, categoryId, basePrice, costPrice, taxRate, hasVariants, status` (empty optionals omitted); update sends the same without `hasVariants`; future-ready fields (brand, tags, inventory) are still **not** sent. Verified by tests.
- **Workflows:** create → upload queued images one by one → warn about failures → open edit with the list's query params; no id → back to `/products`; update → success alert → `Location.back()`; Cancel → `Location.back()`; load failure → alert + back. There is still **no unsaved-changes prompt** (none existed).
- **Images:** same rules (type, 5 MB, 800–1200 px), queued previews in create mode, upload/primary/delete/lightbox in edit mode, native `confirm()` prompts kept.
- **Variants:** assignment save (with invalid-combination confirmation), generate all / selected, manual combination, edit (SKU, barcode, price/cost adjustment), delete with confirmation, stock-transactions link, "auto-select all options" behaviour, per-type "select at least one option" rule. All service calls and payloads are unchanged (tests assert them). Variant barcode uniqueness remains a server rule; its error message is shown in the existing alert.
- Business/outlet scoping and authorisation are untouched (still enforced by the services, guards and API).

## Accessibility and usability gains (presentation only)

- Image upload input is no longer `display: none` — it is reachable by keyboard with a visible focus ring; image action buttons have names, appear on focus as well as hover, and are always visible on touch devices; previews have alt text.
- Variant option chips are real keyboard-operable checkboxes (previously hidden inputs) with a focus ring.
- Required/invalid/hint/error text is programmatically associated (`aria-describedby`, `aria-required`, `aria-invalid`); progress bar has `role="progressbar"`; checklist state is text, not icon only; dialogs trap focus and restore it.
- Icon-only controls all have accessible names.

## Tests (80 new)

- **Product form (36):** create/edit rendering, groups, labels/required markers, hints, ngModel two-way binding to every field, margin, permission-dependent cost fields, every validation rule (incl. boundaries), error display + `aria-invalid` + focus, exact create/update payloads, Save Draft, success navigation, missing id, queued-image upload with partial failure, busy state, **duplicate submit blocked on all paths**, server error + retry, Cancel, no unsaved prompt, SKU generation (3 cases), edit-mode load, update error, load failure, landmarks and responsive grid classes.
- **Image uploader (16):** keyboard-reachable input, queue/preview/remove/clear, validation (type, size, dimensions, mixed batches), edit-mode list, named actions, primary, delete confirmation, upload success/failure and progress.
- **Variant manager (27):** load, saved selection, chips accessibility, auto-select rule, counts, generate gating, assignment (confirm/no-confirm, payload), generate all/selected, API errors, table content (server-side prices), named row actions + stock link, empty state, edit dialog (labelled modal, payload, error stays open, Cancel/Escape/backdrop), cost permission, manual dialog, delete, no-variations state.
- **Kit (+1):** `[error]` marks the control invalid and clears again.

## Browser verification — what was and was not done

- **No authenticated browser verification.** The development API and dev server were not running and no safe account exists; authentication was not bypassed and no business record was created or changed.
- **Static render check only (not the authenticated app):** a temporary spec rendered the real `ProductForm` (create, validation errors, edit with variants, edit-variant dialog) with synthetic data and stubbed services; the resulting component DOM plus the real built CSS was opened in the browser (spec and dumps deleted afterwards). Findings and fixes made from it: duplicate SKU hint removed, empty toolbar bar above the variants table removed, phone action bar compacted from 114 px to 58 px.
- Measured with Playwright (viewport sizes set programmatically; the screenshot tool lags one resize behind, so tablet/mobile conclusions rely on measurements plus one mobile screenshot): **1440×900** — 10-column grid (7/3), pricing 4 columns, no overflow; **768×1024** — single column, pricing 2 columns, no overflow; **390×844** — single column, no page overflow, action bar one row (106 px buttons, no clipping), variants table scrolls inside its own region (508 px content in 299 px), validation messages and red invalid state visible.
- The static dump cannot show control *values* (checked/value are DOM properties), so populated fields, real images, real currency symbols and keyboard behaviour on devices are **unverified**.

## Remaining risks

- Visual regression against the live app is unverified; the first authenticated pass should check populated edit mode, a product with many variants (wide table), long names, many images, and the manual/edit variant dialogs on a phone.
- `ProductImageUploaderComponent` still uses native `confirm()` and has no drag-and-drop handler although its text says "or drop images here" (pre-existing; not changed to avoid new behaviour). Native `alert()`s for load failure and SKU generation are also kept.
- The combination manager still hard-codes the "৳" symbol in labels and prices (pre-existing; not changed).
- Variant edit modal still edits the list object in place (pre-existing), so Cancel after typing leaves the unsaved text in the table until reload.
- Escape now closes the manual/edit variant dialogs (previously they had no keyboard close); backdrop click still does not close them.
- Brand, tags, inventory fields remain "future-ready" and are not persisted; they are labelled "Not saved yet" in the Inventory card.
- Other screens still use legacy classes; nothing outside Product Create/Edit and its two children was migrated (Phase 4 not started).
