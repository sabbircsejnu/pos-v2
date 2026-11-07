# UOM v1 — Screen Access Matrix

**Date:** 2026-11-07. **Branch:** `staging`.

## Why UOM looked missing

1. **No way into the unit setup from the product form (fixed).** Units & Pricing is set per variant on `products/:productId/variants/:variantId/units`. Only the product **detail** page linked there. The product create/edit form, where users actually set products up, had no link.
2. **Every other screen shows units only for a configured variant (by design).** PO, GRN, POS, transfers, counts, adjustments and refunds behave exactly as before, in pcs, until a variant has a unit setup (D01–D12 keep pcs products unchanged). With no setup reachable from the form, no variant had one, so nothing appeared anywhere.
3. **Pack State and Repacking sit under INVENTORY, not Products.** They are not under the Units of Measurement menu.
4. **Development database:** the three UOM migrations were applied on 2026-11-07, which also seeded the 12 global units. Before that, the API and these screens could not work on Development.

**To see UOM end to end:**
1. Open Products › Edit, or Product details.
2. Choose **Units & Pricing**, on the variant row or in the side card for a simple product.
3. Set a base unit such as ml, a purchase unit such as a 500 ml jar, and selling packs or a custom price.

The PO, GRN, POS and inventory screens then show unit choices for that variant.

## Matrix

"Visible to BusinessOwner" assumes the BusinessOwner role's `*` wildcard.

| Screen | Expected UOM functionality | Where it is | How to reach it | Visible to BusinessOwner | Root cause if missing | Fix | Verification |
|---|---|---|---|---|---|---|---|
| Units of Measurement | Units, conversions | `pages/uom/units-page` | Menu: Products › Units of Measurement (`uom.view`) | Yes | — | — | Existing Vitest |
| Product create | Units & Pricing | Product form, side card | Card says units unlock after the first save, as Variants already does | Yes | No entry point | Card added | `product-form.spec.ts` (create) |
| Product edit, simple product | Base unit, purchase units, packs, custom price, promotion eligibility | `pages/uom/variant-units-page` | Product form › **Units & Pricing** card → its standard variant | Yes (`products.edit`) | No link on the form | Link added | `product-form.spec.ts` |
| Product edit, with variants | Same, per variant | Same page | Variants section › row action (ruler icon) **Units & Pricing** | Yes (`products.edit`) | No row action | Row action added | `combination-manager.component.spec.ts` |
| Product details | Same | Same page | Variants table › **Units & Pricing**, or the header button for a simple product | Yes (`products.edit`) | Already present | — | Existing |
| 2-level categories | Unchanged | Product form | — | Yes | — | Not touched | Product form specs pass |
| Purchase order and purchase-and-receive | Purchase unit choice, base preview, cost per unit | `components/po-form` | Procurement › Purchase Orders › New | Yes, once the variant has units | Needs a configured variant | — | Existing specs and PostgreSQL tests |
| GRN | Accepted, damaged and rejected quantities; sealed/loose split | `pages/grn/grn-create`, `grn-details` | Procurement › GRN | Yes, configured variants | Same | — | Existing |
| POS sale | Pack and custom-quantity picker, pack state, promotions | `pages/pos/pos-uom-picker` | POS, when adding a configured variant | Yes | Same | — | `pos-uom*.spec.ts`, PostgreSQL tests |
| Sale details: refund | One refund dialog for every sale, with history | `pages/sales/sale-details` | Sales › a sale › Refund (`sales.refund`) | Yes | — | — | `sale-details.component.spec.ts` |
| Stock transfer | Entered units, pack-state allocations | `pages/stock-transfers` | Inventory › Stock Transfer | Yes, configured variants | Needs a configured variant | — | Existing |
| Stock count | Count in any unit, sealed and loose | `pages/stock-counts/stock-count-unit-entry` | Inventory › Stock Counts | Yes | Same | — | Existing |
| Stock adjustment | Adjust in units | `pages/stock-adjustments` | Inventory › Stock Adjustment | Yes | Same | — | Existing |
| Pack State, incl. opening sealed or prepared packs | Balances; open | `pages/uom/pack-state-page` | Menu: **Inventory › Pack State** | Yes | Under Inventory, not UOM | — | Existing |
| Repacking | Jobs; execute; reverse | `pages/uom/repacking-page` | Menu: **Inventory › Repacking** | Yes | Same | — | Existing |
| Packaging inventory | Bottles as ordinary products, used as pack components | Product + Units & Pricing › pack components | Product form | Yes | — | — | Existing |
| Inventory list and reports | Base unit shown; mixed-unit totals separated; cost per U-13 | `components/inventory`, `pages/reports` | Inventory, Reports | Yes (cost via `*`) | — | — | Report specs, CostVisibility tests |
| Owner dashboard | Unit-aware slow movers, transfers, valuation | `pages/owner-dashboard` | Dashboard | Yes | Works in the background, so nothing new is visible | — | Unit tests |

## Permissions
- BusinessOwner holds every key through `*`, so all entry points above are visible.
- Other roles get the six UOM keys in Role Edit, plus `products.edit` for Units & Pricing.
- POS, purchasing and inventory staff read unit setups through their own permissions; this is unchanged and tested.

## Verification
- **Angular:** 1,988 passed in 139 files, including 5 new specs for the entry points. The production build succeeds with the existing POS CSS budget warning.
- **Browser:** NOT verified. No browser tool is available in this session, so the new links and every screen above still need a manual check at 390, 768 and 1440 px.
