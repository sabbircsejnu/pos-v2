# Mobile Sales Dashboard API — Acceptance and Deployment Gate

**Decision basis:** Product Owner decision dated 2026-10-15: current release is single-business, multi-outlet, with future customers on separate deployments and databases.  
**Status:** Endpoint calculation and authorization are implemented, but Mobile Net Sales financial acceptance is **blocked**. Source trace found that existing refund postings do not consistently reconcile to tax-inclusive sale totals and do not apply invoice-level discounts to partial returns. No financial formula correction is safe without a Product Owner decision and additional authoritative persisted data.  
**Scope:** Read-only Mobile Sales Dashboard API only. No catalogue, notification, forecasting, stock-take, Angular or Flutter implementation is included.

## Product Owner architecture decision recorded

- Current release is a single-business, multi-outlet deployment. The runtime guard below verifies a strict database condition at request time; it does not presume deployment isolation from this decision alone.
- Product/Category multi-tenant ownership work B1–B4 is **DEFERRED**, not completed, migrated, or security-tested. No `Product.BusinessId` migration, legacy ownership backfill, identifier restructuring or cross-tenant isolation infrastructure was added.
- Existing authorization, Business context, Outlet access, permissions, product/variant IDs, ProductCode and barcode semantics were not removed or weakened.
- No persistent Development or Production database was accessed or changed. No database schema or migration was added.

## API contract

`GET /api/mobile/v1/dashboard/sales`

Optional query parameters:

| Parameter | Behavior |
|---|---|
| `startDate` | Inclusive start instant; defaults to 30 days before the effective end. |
| `endDate` | Inclusive end instant; defaults to current UTC time. |

Both supplied/defaulted instants are normalized using the existing SalesReports UTC conversion convention. A start later than the end returns `400`. No client-supplied role, Business ID, Outlet ID, user ID or cashier ID is accepted as authorization or filter scope.

Response data is a purpose-built `MobileSalesDashboardDto` containing only:

- `startDateUtc`, `endDateUtc`
- `totalRevenue`: sum of eligible `Sale.TotalAmount` values in the sale-time window. `TotalAmount` is persisted after line-level and sale-level discounts and includes tax.
- `netRevenue`: current provisional implementation subtracts the debit amount on linked, posted `Sales return` lines in the refund-posting window. This is **not financially accepted as Net Sales**: the base sales total includes tax, the refund debit excludes VAT, and the separate VAT reversal and invoice-level discount treatment do not provide a generally reconcilable refund amount. Do not present this field as a finalized Net Sales KPI.
- `invoiceCount`: count of eligible completed-origin sales in the sale-time window. A refunded sale remains the original completed invoice; voided, cancelled and other non-completed statuses are excluded.
- `unitCount`: units on eligible sales in the sale-time window less linked sale-return stock-ledger `QtyIn` movements in the return-creation window.
- `averageInvoiceValue`: `netRevenue / invoiceCount`; zero when there are no eligible invoices.

Report bounds are instants normalized to UTC by the controller and applied inclusively: `timestamp >= startDateUtc && timestamp <= endDateUtc`. Sale recognition uses `Sale.SaleDateTime`, falling back to legacy `Sale.SaleDate`; refund recognition uses the actual `JournalEntry.PostedAt`; returned-unit recognition uses `StockLedger.CreatedAt`. These are three event timestamps, not one synthetic sale date.

The Sale model has no currency field; this deployment's business rule identifies the KPI as BDT, but the API does not return a currency code or independently validate account currency. No customer records, cost, profit, margin, product details or inventory values are returned. Business-local day grouping and Asia/Dhaka display conversion are not performed by this endpoint.

## Server-side authorization and scope

- Authentication is required.
- Effective role is read from the validated server principal/context. Role-switched sessions are denied on this endpoint to avoid inheriting an acting user's broader underlying Outlet-access scope.
- **BusinessOwner:** requires `reports.sales` (or the server-issued wildcard grant); report rows are limited to Outlets authorized by the existing context service in the authenticated Business.
- **OutletManager / Manager:** requires `reports.sales`; report rows are limited to all Outlets returned by the existing authorized-Outlet service for that principal.
- **SalesPerson / legacy Salesman:** requires `sales.view`; the authenticated real user ID is applied as `Sale.CashierId` for both invoice/revenue and unit queries, in addition to authorized Outlet scope.
- **Cashier and all other roles:** denied, including a Cashier who happens to hold `reports.sales`.
- Missing Business context and Super Admin/global context are denied. A caller cannot select another Business or Outlet through query parameters.

### Single-business fail-closed runtime guard

Before querying reports, the API checks the current database using the server-derived `EffectiveBusinessId`:

1. There must be exactly one `Business` row.
2. That row must be active and have the same ID as the authenticated Business context.
3. Every `Outlet` row must have `BusinessId` equal to that sole Business ID; null-owned or other-Business Outlets fail the gate.

Any mismatch returns `403` and logs a warning without returning Business/Outlet data. This strict gate intentionally denies even when an additional Business row is inactive. It is a local, read-only deployment-readiness check for this mobile endpoint, not a general tenant-isolation framework. The reporting query is additionally scoped through the existing authorized-Outlet read scope.

## Verification

The focused test fixture:

- Uses only the documented A2 disposable PostgreSQL 16 instance at `127.0.0.1:55432`, database `a2_test_guard`, user `a2_test_runner`.
- Performs a read-only connection identity/version check before creating a uniquely named test database.
- Runs migrations and seed data only in that owned disposable test database; the repository test lifecycle removes that exact database on fixture disposal.
- Covers completed, voided, cancelled, partial-refund, full-refund and replacement sales; posted return values; discounts without duplicate deduction; returned stock-ledger quantities; UTC sale/refund boundaries; zero eligible sales; Owner, Manager and SalesPerson scopes; exact response field allowlisting; permission denials; unowned Outlet denial; conflicting Business-record denial; and fail-closed behavior for a refunded sale with only a draft/missing posted refund journal.

**Observed results:**

- Focused dashboard PostgreSQL HTTP acceptance test plus `SaleServiceVatAndExchangeAccountingTests`: **passed (4 passed, 0 failed)**, using the disposable A2 PostgreSQL 16 fixture for the dashboard HTTP test. These tests characterize current behavior; they do not resolve or validate the blocked tax-inclusive KPI semantics.
- Full solution Release build (`dotnet build RetailPOS.slnx --configuration Release --no-restore`): **passed (exit code 0)**.
- The prior selected SalesReports/refund/void regression run also passed **8 tests, 0 failed**; it predates this financial semantics review.
- The build/test output included existing ImageSharp vulnerability advisories and unrelated payroll nullability warnings.

The test proves the behavior against a controlled PostgreSQL fixture. It does **not** establish the row count or Business/Outlet ownership state of any deployed database.

## Verified transaction mapping and financial acceptance blocker

- Sale creation persists `Sale.TotalAmount = sum(SaleItem.Subtotal) - Sale.Discount + Sale.Tax`. `SaleItem.Subtotal` already deducts that line's `DiscountAmount`; `Sale.Discount` is an additional invoice-level discount. Thus `TotalAmount` is line-discounted and invoice-discounted, then tax-inclusive. Existing SalesReports behavior is intentionally unchanged.
- The sale posting debits the payment accounts for `Sale.TotalAmount`, debits Sales discount for line discounts plus invoice discount, credits Sales revenue for the gross item value, and credits VAT payable for `Sale.Tax`. This is the sale-side accounting representation; `Sale.TotalAmount` is not a tax-exclusive sales-revenue measure.
- Sale creation computes the effective VAT rate from `sum(item.Subtotal) - sale.Discount`, but persists each item's `TaxableAmount` as its full `Subtotal` and allocates `VatAmount` by each item's share of the pre-invoice-discount subtotal. These line snapshots do not persist an invoice-discount allocation per line. The refund poster does not use those item VAT snapshots to derive its VAT reversal.
- Eligible sale states are `completed` and `refunded`. The refund command transitions a previously completed sale to `refunded`, so excluding that state would drop the original invoice before applying its return. `voided`, `cancelled` and all other statuses are excluded from revenue, invoice counts and units. The void command separately writes `SaleVoid`; its accounting posting uses the same `SalesReturnPosting` metadata as refunds, so void postings are ignored by joining to sales whose current status is `refunded`.
- `RefundSaleCommand` writes no refund entity. It posts a system journal with `JournalType=SalesReturnPosting`, `ReferenceType=SaleRefund`, and `ReferenceId=Sale.Id`. The poster's `Sales return` debit is computed from `SaleItem.Subtotal / Quantity * returned Quantity`; this includes line discounts but does not allocate or deduct `Sale.Discount`.
- The poster computes its refund tax as `Sale.Tax * refundAmount / sum(SaleItem.Subtotal)`, with a denominator that does not subtract the invoice-level discount. It posts that value separately as a debit to VAT payable with description `VAT payable reversal`.
- Refund payment-account reversal credits are apportioned from `refundAmount` only; the amount does not add the separately posted VAT reversal. A `Rounding difference` line is then used to balance the journal. Therefore the `Sales return` debit alone is tax-exclusive, and the sum of `Sales return` plus `VAT payable reversal` is not proof of the cash/customer refund amount. Posted refund entries cannot generally be reconciled to `Sale.TotalAmount` on a consistent tax-inclusive basis from these lines alone.
- A full refund still uses the same poster formula: it returns the sum of line subtotals without applying the sale-level discount, while VAT is reversed separately. The resulting journal lines therefore do not establish that the invoice's tax-inclusive `TotalAmount` was refunded. A partial return has the additional ambiguity of how to apportion the invoice-level discount over returned lines/quantities.
- `Sale.Discount` persists only the invoice-level aggregate; there is no persisted per-line allocation or per-refund-line amount. The refund request's item quantities are not persisted as refund details. Stock-ledger return rows can record variant quantities when outlet inventory exists, but the workflow skips those rows when it does not; they are not a complete financial/refund record.
- The dashboard currently subtracts only the `Sales return` debit and excludes the VAT reversal. Changing it to subtract both journal debits would assume that both describe the customer refund, despite the payment reversal being only `refundAmount`, and could double-count or apply the wrong tax/discount basis. No correction is justified from current persisted data without a business decision.
- Every `refunded` sale found in the authorized Outlet scope must have a matching posted refund journal, even if that journal's posting timestamp is outside the requested report window. A missing or draft-only posting fails the request with a server error and is logged; the API does not silently report an unadjusted amount for inconsistent refund data.
- The refund command can process multiple returned item lines in one posting but marks the whole sale `refunded` and rejects subsequent refund commands. The system journal's unique business/type/reference key also permits only one such posting per sale. Per the Product Owner's choice, this KPI-only change supports the current one-posting workflow; it does not expand refund persistence or authorize multiple separate partial-refund transactions.
- For unit counting, the stock ledger writes a `return` movement with `ReferenceType=sale` and the source `Sale.Id`; returned units are summed from `QtyIn`. Because voids use the same stock-ledger reference/type, only sale rows still in `refunded` state contribute return units. Refund command behavior skips stock-ledger writes when the outlet inventory row is absent, so historical refunds without such movements cannot be fully represented in Units Sold.
- Replacement sales use the existing `OriginalSaleId`/`ReplacementSaleId` links, but have no separate exchange/settlement model. The current creation/accounting path creates a normal completed sale and `SalePosting` for the replacement. The KPI therefore includes that completed replacement sale as a sale and does not synthesize an additional refund or exchange value.

### Required Product Owner / Accounting decisions and data

Before a financially accepted Mobile Net Sales formula can be implemented, decide:

1. Whether Mobile Net Sales is tax-exclusive sales revenue or a tax-inclusive collection/refund measure.
2. Which posted values define a refund for that KPI: customer/payment amount, Sales return amount, VAT reversal, or a specified combination, including the intended treatment of rounding lines. The decision must ensure each economic amount is deducted exactly once.
3. How invoice-level `Sale.Discount` is allocated to partially returned items/quantities and how the corresponding VAT reversal is calculated. Current data stores the invoice discount only as a sale aggregate and does not persist its allocation to each returned line.
4. The authoritative record of the actual amount returned to the customer when it differs from the accounting Sales return or payment-reversal lines. The current command persists neither a refund transaction nor per-refund-line financial amounts.

Do not infer these policies from journal balance or derive a guessed proportional allocation. The existing one-refund-posting-per-sale workflow remains unchanged. Existing ERP sales and accounting posting formulas must not be changed as part of a mobile-only KPI correction.

### Requested tax scenario coverage status

No new tax/refund PostgreSQL assertions are added in this change because there is no approved expected KPI result for these scenarios; asserting a guessed result would falsely certify the unresolved accounting semantics. Existing `SaleServiceVatAndExchangeAccountingTests` characterizes a VAT reversal for a partial refund, but is not PostgreSQL-backed. The mobile PostgreSQL fixture's sale helper leaves `Sale.Tax` at zero, while its refund-journal helper inserts a fixed VAT reversal line; its current dashboard test does not assert a tax reconciliation. Thus the existing passing mobile test does **not** cover taxable-sale/refund financial correctness, invoice-level-discount partial refunds, or a tax-inclusive refund.

Other unverified/deployment-specific assumptions:

- Deployed JWT expiry, revocation window and native authentication approval remain separate P2-B1/P2-B2 gates.
- The deployment's BDT policy and Asia/Dhaka display/day-boundary policy still require production configuration verification. This API currently reports UTC instants and tax-inclusive `Sale.TotalAmount`.
- The endpoint's runtime guard is tested with one valid Business plus mismatched Outlet and additional Business fixtures; actual deployment data still needs a controlled verification. If the guard fails, this endpoint returns `403` until the conflicting data is resolved through an approved process.
- Manager and SalesPerson role names are limited to the explicit current/legacy names in the controller. Other local role aliases require an approved mapping and permission decision.
- No general SalesReports route was changed; no Angular or Flutter files were changed.

## Changed implementation surfaces

- [MobileSalesDashboardController.cs](../../../src/RetailPOS.API/Controllers/MobileSalesDashboardController.cs)
- [MobileSalesDashboardDto.cs](../../../src/RetailPOS.API/DTOs/Mobile/MobileSalesDashboardDto.cs)
- [MobileSingleBusinessReadinessGuard.cs](../../../src/RetailPOS.API/Services/MobileSingleBusinessReadinessGuard.cs)
- [SalesReportQueries.cs](../../../src/RetailPOS.Application/SalesReports/SalesReportQueries.cs)
- [SalesReportReadModels.cs](../../../src/RetailPOS.Application/SalesReports/SalesReportReadModels.cs)
- [ISalesReportReadStore.cs](../../../src/RetailPOS.Application/SalesReports/Abstractions/ISalesReportReadStore.cs)
- [SalesReportReadStore.cs](../../../src/RetailPOS.Infrastructure/SalesReports/SalesReportReadStore.cs)
- [MobileSalesDashboardPostgresHttpTests.cs](../../../tests/RetailPOS.IntegrationTests/Sales/MobileSalesDashboardPostgresHttpTests.cs)

**Release gate:** Financial acceptance is blocked pending the decisions and authoritative refund/discount data above. Do not use `netRevenue` for a production Mobile Net Sales KPI or controlled Flutter integration that presents it as financially meaningful. A non-financial wire/contract preview is only safe if this field is clearly marked unavailable/provisional and the single-Business guard passes. Existing Outlet/role authorization and that runtime guard remain unchanged. No other mobile API work is authorized by this acceptance.
