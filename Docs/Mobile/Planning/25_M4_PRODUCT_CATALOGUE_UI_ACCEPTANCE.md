# IraniMart Business Mobile — M4 Product Catalogue UI Acceptance

**Date:** 2026-10-14  
**Scope:** Presentation-only Catalogue search, filters and Product/Variant
identity components  
**Verdict:** **PARTIALLY PASSED** — implementation and automated validation are
complete; design, filter behavior, barcode policy and device accessibility
approvals remain open.

## 1. Scope and boundaries

M4 replaces the generic Catalogue destination preview with a responsive,
reviewable UI for catalogue search and filtering, plus reusable product and
variant detail presentations. The authenticated Owner and Manager route policy
is unchanged; the existing role guard continues to exclude Cashier/Salesperson
and unsupported roles.

This is presentation-only. The screen does not call product, inventory,
barcode, stock-count or other business-data APIs. Search, filter and dropdown
controls do not query or filter business records. No local database, cache,
catalogue synchronization, scan, stock adjustment, pricing, cost or inventory
behavior was added. No product records are supplied by the app.

**Product Owner Mobile role clarification (2026-10-15):** The live Mobile
Catalogue, Product/Variant Details and Barcode Lookup modules are restricted
to Business Owner and Sales Manager (including the authorized backend
`SalesManager`, `OutletManager` and `Manager` mappings). Salesperson and Cashier
must not receive Catalogue navigation or route access. Flutter must enforce
the role-navigation contract, and the backend API independently enforces
`products.view` plus this role allowlist. This clarification does not change
ERP/web POS permissions. This backend workstream does not modify Flutter code.

## 2. Implemented screens and components

| Screen/component | Implementation |
|---|---|
| Catalogue | Responsive scrollable page at the existing authenticated `/catalogue` destination. |
| Search | Search-field selector for Main Product Code, product name, SKU and barcode, with a single text-entry control. |
| Filters | Separate category, size and colour inputs, plus arbitrary attribute-name and attribute-value inputs. Values are not enumerated or inferred. |
| Session context | Displays the business and selected authorized outlet from the restored ERP session. A visible note clarifies that displayed context is not an authorization grant. |
| Product details | Reusable presentation for 64-bit Product ID, Product Name, Main Product Code, and a separate optional Product-level barcode. |
| Variant details | Reusable per-variant card with 64-bit Variant ID, optional SKU, distinct variant barcode, and any supplied attribute/value pairs. |
| Barcode exceptions | Explicit unknown and ambiguous barcode states; any supplied raw barcode is rendered as a string without parsing, trimming, padding or reformatting. Neither state selects a product or variant. |
| Catalogue states | Explicit loading, empty, unavailable, stale, error and unauthorized states. Product details also show unavailable until an authorized catalogue selection exists. |

No stock quantities, sales prices, costs, margins or other sensitive business
values are displayed.

## 3. Identity and barcode representation

- `Product.ProductCode` is presented as **Main Product Code**; no second
  product-code concept or authoritative product master was introduced.
- Product ID and Product Variant ID are represented with Dart `int` fields.
  They are displayed directly without narrowing or conversion to 32-bit
  identifiers.
- Main Product Code, Product-level barcode and sellable Variant barcode have
  separate fields and labels. A parent product barcode is never treated as a
  variant identifier.
- Barcode values remain `String?` presentation values. Widget coverage
  verifies distinct leading-zero product- and variant-level barcodes survive
  unchanged.
- Variant attributes are a variable list of name/value pairs; the UI does not
  enforce a Colour/Size schema or reconcile competing ERP attribute sources.
- Product/variant presentation models are display-only. They do not claim to
  validate live ERP IDs, barcode uniqueness, variant eligibility or business
  ownership.

## 4. Figma and SRS/LLD evidence

The published prototype
([Figma published preview](https://merge-help-64706861.figma.site/)) was
previously inspected. It does **not** expose a verified Catalogue, Product
Details or Variant screen. The editable Figma Make project was not available
for screen/token inspection. Therefore this implementation is not claimed to
be Figma-faithful.

The proposed layout is grounded in SRS v1.4 requirements CAT/PROD, particularly
PROD-001..012, and the corresponding LLD v1.4 catalogue projection and identity
sections. The implementation follows the requirements to keep Main Product
Code separate from variant barcode, preserve raw barcode strings, show
variable attributes and avoid selecting an arbitrary result for unknown or
ambiguous barcodes. The illustrative catalogue contract in the LLD was not
implemented as a live data contract.

## 5. Security and authorization

- GoRouter continues to authorize `/catalogue` using the authenticated
  server-derived role and existing `RoleNavigation` policy.
- The page receives business and outlet labels from the authenticated session;
  those labels are contextual display only and do not grant access.
- No role selection, account/session behavior, token handling or authorization
  policy changed.
- The app does not make any business-data request, even when controls are
  changed.
- The no-data screen and emulator capture contain no live business records.

## 6. Automated validation and device review

| Validation | Result |
|---|---|
| Focused Catalogue widget and authenticated-route tests | **Passed: 6 tests** |
| Final role-navigation and Catalogue regression selection | **Passed: 10 tests** |
| Complete Flutter test suite | **Passed: 74 tests** |
| Clean Architecture import-boundary tests | **Passed: 2 tests** |
| `flutter analyze` | **Passed: no issues found** |
| `dart format --output=none --set-exit-if-changed lib test` | **Passed: 61 files, 0 changed** |
| Development Android debug build | **Passed:** `flutter build apk --debug --flavor development --dart-define=APP_ENV=development` |
| Staging Android debug build | **Passed:** `flutter build apk --debug --flavor staging --dart-define=APP_ENV=staging` |
| Android emulator preview | **Captured:** [m4-catalogue-preview.png](./m4-catalogue-preview.png). A temporary, no-network entry point marked the screenshot as synthetic/presentation-only; that source file was removed and the normal development APK restored afterward. |

Widget tests cover large Product and Variant IDs, raw leading-zero barcode
preservation, distinct product-/variant-barcode display, variable attributes,
all catalogue content states, unknown and ambiguous barcode states, and a
320-logical-pixel layout at 2x text scaling. Search-field and filter controls
are also verified as present. The emulator capture uses a conspicuous
presentation-only/synthetic-context banner and contains no ERP session or
business data.

An all-directory `dart format .` attempt encountered a transient file-listing
error in a Gradle-generated `build` path while the build output was changing.
Formatting was then verified successfully for all Dart source and test files
(`lib test`).

Automated layout tests do not replace manual TalkBack, contrast, touch-target,
keyboard/focus and device-scale accessibility review.

## 7. Design and Product Owner approvals still required

1. Approve the proposed Catalogue search/filter and Product/Variant detail
   layouts; no corresponding Figma screen is verified.
2. Confirm whether search should select one field at a time or support
   simultaneous multi-field search, and whether filters should be dropdowns,
   chips or another interaction.
3. Confirm the ERP meaning and display policy for SKU versus Variant ID.
4. Approve Product-level barcode visibility and the exact unknown/ambiguous
   barcode recovery wording and actions.
5. Resolve ERP attribute/value projection and barcode uniqueness/symbology
   policies before any data or scan integration.
6. Complete manual Android accessibility review, including TalkBack,
   contrast, keyboard traversal and minimum touch targets.

## 8. Acceptance boundary

This report accepts only the UI-only M4 implementation and automated checks.
It does not accept product catalogue APIs, synchronization, offline storage,
barcode scanning/resolution, inventory data, Product ownership changes or
Stock Take integration. Those remain blocked on authorized backend contracts,
ERP data/permission review and the outstanding approvals above.
