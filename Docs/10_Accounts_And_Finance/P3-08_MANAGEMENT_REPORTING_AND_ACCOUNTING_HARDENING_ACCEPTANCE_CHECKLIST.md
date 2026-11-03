# P3-08 Management Reporting and Accounting Hardening Acceptance Checklist

Updated: 2026-09-06
Status: P3-08 Implementation Complete - Ready for Final Acceptance Review

## Scope Boundary

P3-08 is limited to the frozen business decisions and three implemented slices documented in `PHASE_3_ACCOUNTING_DEVELOPMENT_PLAN.md`:

1. Slice 1 - Direct Method Cash Flow and comparative reporting.
2. Slice 2 - Profitability reporting and monetary rounding consolidation.
3. Slice 3 - Retry/atomicity hardening and legacy finance controls.

P3-03/P3-04 remain Deferred / Not Required under the approved fully-settled POS model. No Phase 4 scope is included. No migration was created for P3-08.

## Slice 1 - Cash Flow and Comparative Reporting

- [x] Cash Flow Statement uses the Direct Method only.
- [x] Cash Flow is derived from posted Journal Engine/Cash Book liquidity evidence, not operational-table reconstruction.
- [x] Liquidity universe is limited to configured Cash, Petty Cash, and Bank accounts.
- [x] Card Clearing and Mobile/MFS Clearing are excluded from the liquidity universe.
- [x] Card/MFS settlement is counted once when the Bank liquidity movement is posted.
- [x] Classification is backend-owned and deterministic from authoritative journal/source/reference metadata.
- [x] Operating, Investing, Financing, and Unclassified sections are present.
- [x] Unknown external liquidity movements remain visible as Unclassified and are included in reconciliation.
- [x] Cash-to-Bank, Bank-to-Cash, and Bank-to-Bank internal liquidity transfers are eliminated from external cash-flow totals and retained as memo evidence.
- [x] `YearEndClosing`, `OpeningBalance`, and `OpeningBalanceAdjustment` are excluded from current-period Cash Flow and retained as cutover/non-cash evidence.
- [x] Reversals affect the period in which the reversal is posted.
- [x] Reconciliation surfaces Opening Liquidity, section movements, total external movement, Closing Liquidity, GL-derived movement, and reconciliation difference.
- [x] Comparative Cash Flow uses the same backend classification logic as the primary report.
- [x] Cash Flow export uses the same backend result as the UI.
- [x] Cash Flow API and UI are guarded by `accounting.reports.cash_flow`.
- [x] Tenant/location scoping follows existing accounting report authorization patterns.

Evidence:

- Backend: `P308CashFlowReportServiceTests` - 9/9 passed.
- HTTP/export/permission: `FinancialReportsHttpTests.CashFlow_RequiresPermission_ReturnsReport_AndExportsSameScope` - 1/1 passed.
- Related regression set: Cash Book/accounting report/permission tests - 24/24 passed during Slice 1 validation.
- Full backend suite after Slice 1: 469/469 passed.
- Frontend: `cash-flow-report.component.spec.ts` - 3/3 passed.
- Full Angular suite after Slice 1: 49/49 passed.
- Backend Release build and Angular production build passed.

## Slice 2 - Profitability Reporting and Monetary Rounding

- [x] Business profitability reuses the accepted authoritative P&L path.
- [x] Outlet profitability is direct-attribution only from authoritative journal outlet dimensions.
- [x] Outlet activity without reliable outlet attribution is surfaced as explicit `Unallocated`.
- [x] Attributed outlet totals plus Unallocated reconcile to Business-level P&L totals.
- [x] Dimensionless/shared expenses are not fabricated into outlet allocations.
- [x] Product/category reporting is contribution reporting, not fully allocated net profit.
- [x] Product/category contribution uses reliable SaleItem/Product/Category source evidence for posted Sale journals.
- [x] Product/category revenue and COGS reconcile to authoritative Sale-reference journal revenue/COGS.
- [x] Product/category unmapped/unallocated revenue/COGS are surfaced explicitly.
- [x] Shared operating expenses are not allocated to products/categories.
- [x] Comparative profitability uses the same backend report logic as primary profitability.
- [x] Profitability export uses the same backend result as the UI.
- [x] Profitability API and UI are guarded by `accounting.reports.profitability`.
- [x] A shared `MonetaryRounding.ToCurrency(decimal)` abstraction defines 2-decimal `AwayFromZero` currency rounding.
- [x] Selected future 2-decimal monetary/posting paths in Sale, GRN, PurchaseOrder, Pricing, and CashSession use the shared rounding policy.
- [x] Existing Rounding Difference account resolution remains configuration-based.
- [x] Historical posted journals are not recalculated or rewritten.
- [x] Non-currency/precision paths are deliberately retained outside the 2-decimal currency helper.

Changed rounding paths:

- `SaleService`: sale VAT allocation currency amounts, sale posting amounts, payment lines, sale/refund rounding-difference calculations, refund amount/tax/cost/payment reversal currency amounts.
- `GrnService`: GRN taxable/VAT currency amounts, purchase receipt net/VAT totals, GRN rounding-difference calculation.
- `PurchaseOrderService`: PO totals, immediate receive totals, generated GRN taxable/VAT currency amounts, receipt posting total.
- `PricingService`: outlet margin price, discount amount, final price, informational tax amount.
- `CashSessionService`: variance posting amount.

Deliberately retained compatibility/precision paths:

- 4-decimal VAT-rate and historical/weighted-average cost calculations in `SaleService`.
- 4-decimal inventory unit-cost presentation in `AccountingReportService`.
- Inventory percentage-display rounding.
- `BankStatementImportService` running-balance evidence checks.

Evidence:

- Backend: `P308ProfitabilityReportServiceTests` + `MonetaryRoundingTests` - 10/10 passed.
- HTTP/export/permission: `FinancialReportsHttpTests.ProfitabilityReports_RequirePermission_ReturnReports_AndExportAuthoritativeResults` - 1/1 passed.
- Related P&L/COGS/reporting, permission, Sale, GRN, purchase, and Cash Session regression set - 49/49 passed during Slice 2 validation.
- Full backend suite after Slice 2: 480/480 passed.
- Frontend: `profitability-report.component.spec.ts` - 4/4 passed.
- Full Angular suite after Slice 2: 53/53 passed.
- Backend Release build and Angular production build passed.

## Slice 3 - Retry/Atomicity Hardening and Legacy Finance Controls

- [x] GRN completion retry behavior is proven against the relational provider.
- [x] GRN duplicate/retry does not create a second posted GRN journal.
- [x] GRN duplicate/retry does not duplicate inventory quantity.
- [x] GRN duplicate/retry leaves completed GRN state consistent.
- [x] GRN rollback/fault-injection evidence remains covered by existing P3-02 relational tests.
- [x] Cash Session close retry behavior is proven against the relational provider.
- [x] Cash Session duplicate/retry does not create a second shortage/surplus journal.
- [x] Cash Session duplicate/retry leaves closed session state consistent.
- [x] GRN required tests-only evidence; no production row-lock/code fix was required.
- [x] Cash Session required tests-only evidence; no production row-lock/code fix was required.
- [x] Legacy Account writes are blocked server-side.
- [x] Legacy Transaction writes are blocked server-side.
- [x] Legacy Bill writes/status mutation are blocked server-side.
- [x] Legacy permission aliases and canonical accounting permission aliases cannot bypass backend write rejection.
- [x] Historical/read-only legacy Account, Transaction, and Bill reads remain available where retained.
- [x] `SupplierPaymentService` legacy `Bill` read fallback remains preserved for accepted P3-02 compatibility.
- [x] Supplier Payment/AP compatibility regressions pass.
- [x] Active Finance navigation no longer points to legacy Account/Transaction workflows.
- [x] Retained legacy Account, Transaction, and Bill screens are visibly read-only/non-authoritative.
- [x] No legacy tables/data were dropped or deleted.
- [x] No fabricated historical JournalEntry records were created.
- [x] No migration was introduced.

Guarded backend write surfaces:

- `POST /api/accounts`
- `PUT /api/accounts/{id}`
- `DELETE /api/accounts/{id}`
- `POST /api/transactions`
- `POST /api/bills`
- `PUT /api/bills/{id}/status`

Retained legacy read surfaces/dependencies:

- `GET /api/accounts`, `GET /api/accounts/{id}`
- `GET /api/transactions`, `POST /api/transactions/search`, `GET /api/transactions/{id}`, `GET /api/transactions/ledger`
- `GET /api/bills`, `POST /api/bills/search`, `GET /api/bills/{id}`, `GET /api/bills/summary`
- `SupplierPaymentService` legacy `Bill` fallback allocation read.

Evidence:

- Backend: `P308RetryAtomicityTests` + `P308LegacyFinanceGuardHttpTests` + updated `AccountTransactionTenantIsolationHttpTests` - 7/7 passed.
- Supplier Payment/AP compatibility, Journal Engine/idempotency, permission, and tenant-isolation regression set - 60/60 passed.
- Full backend suite after Slice 3: 484/484 passed.
- Frontend: `menu.service.spec.ts` + `legacy-finance-readonly.component.spec.ts` - 16/16 passed.
- Full Angular suite after Slice 3: 57/57 passed.
- Backend Release build and Angular production build passed.

## Final P3-08 Validation Summary

- Focused Slice 1 backend Cash Flow service tests: 9/9 passed.
- Focused Slice 1 Cash Flow HTTP/export/permission test: 1/1 passed.
- Slice 1 related backend report/permission regression set: 24/24 passed.
- Focused Slice 1 Angular Cash Flow tests: 3/3 passed.
- Focused Slice 2 backend profitability/rounding tests: 10/10 passed.
- Focused Slice 2 Profitability HTTP/export/permission test: 1/1 passed.
- Slice 2 related backend report/posting regression set: 49/49 passed.
- Focused Slice 2 Angular Profitability tests: 4/4 passed.
- Focused Slice 3 backend retry/legacy guard tests: 7/7 passed.
- Slice 3 Supplier Payment/AP, Journal Engine/idempotency, permission, and tenant-isolation regression set: 60/60 passed.
- Focused Slice 3 Angular navigation/read-only tests: 16/16 passed.
- Final full backend regression suite: 484/484 passed.
- Final backend Release build: passed.
- Final full Angular test suite: 57/57 passed.
- Final Angular production build: passed.

## Known Non-Blocking Advisories

- Backend test/build output continues to report existing `SQLitePCLRaw.lib.e_sqlite3` NU1903 advisory.
- Backend test/build output continues to report existing xUnit analyzer warnings in `P306AdjustmentRelationalAtomicityTests`.
- Full Angular test output continues to show an existing jsdom download-navigation advisory from the accepted close-workbench export spec.

None of these advisories were introduced by P3-08 Slice 3 and none block final P3-08 acceptance.

## Final Status

P3-08 Slice 3 Complete - P3-08 Ready for Final Acceptance
