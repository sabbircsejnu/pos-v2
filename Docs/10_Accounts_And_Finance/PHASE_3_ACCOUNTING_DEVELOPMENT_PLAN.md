# PHASE 3 ACCOUNTING DEVELOPMENT PLAN

Updated: 2026-09-06
Status: Phase 3 Accounting & Finance is signed off. P3-01, P3-02, P3-05, P3-06, and P3-07 are accepted. P3-03 and P3-04 are explicitly deferred/not required under the approved fully-settled POS model. P3-08 decisions are frozen; Slices 1 and 2 are accepted; Slice 3 Retry/Atomicity Hardening and Legacy Finance Controls is implemented. P3-08 Slice 3 Complete – P3-08 Ready for Final Acceptance.
Prerequisites: Phase 1 and Phase 2 Accounting are completed and accepted.

## 1. Purpose

Phase 3 extends the accepted journal-backed accounting foundation into complete financial statements, supplier and customer subledgers, tax reporting, bank reconciliation, controlled close, and management reporting.

This plan is based on verified gaps following the Phase 2 final sign-off. It does not replace the completed implementation evidence in [BASIC_ACCOUNTING_DEVELOPMENT_PLAN.md](Docs/10_Accounts_And_Finance/BASIC_ACCOUNTING_DEVELOPMENT_PLAN.md), [PHASE_2_ACCOUNTING_DEVELOPMENT_PLAN.md](Docs/10_Accounts_And_Finance/PHASE_2_ACCOUNTING_DEVELOPMENT_PLAN.md), or [P2-08_PHASE_2_FINAL_ACCEPTANCE_CHECKLIST.md](Docs/10_Accounts_And_Finance/P2-08_PHASE_2_FINAL_ACCEPTANCE_CHECKLIST.md).

## 2. Phase 3 Objectives

1. Deliver journal-only Balance Sheet, enhanced General Ledger, and comparative financial statements.
2. Complete the supplier-payable reporting layer without creating a parallel AP accounting ledger.
3. Introduce a controlled customer receivable lifecycle only after required business rules are approved.
4. Make VAT/tax, bank/clearing settlement, period close, and control-account reconciliation operationally usable.
5. Retire or explicitly protect transitional legacy accounting surfaces and complete selected Phase 2 hardening.

## 3. Phase 3-Wide Accounting Architecture Principles

- Posted `JournalEntry` and `JournalEntryLine` records remain the financial source of truth.
- Do not create parallel accounting ledgers where the Journal Engine already provides the financial record.
- Operational subledgers may store document state, allocations, due dates, matching state, and audit data, but their balances must reconcile to the configured GL control account.
- Posted journals are immutable. Corrections use controlled linked reversal or adjustment postings; operational records retain their original source linkage.
- Historical amounts, exchange links, and inventory costs must not be silently recalculated from mutable current values.
- Resolve every accounting account from `AccountingConfiguration` and applicable outlet override. Do not hardcode account IDs.
- Tenant/business and authorized outlet/warehouse/location isolation are mandatory on every operational and reporting query.
- Financial-year and accounting-period governance applies to every posting, reversal, close, and adjustment.
- Financially linked operational state changes and journal posting must be committed atomically.
- Posting operations must be idempotent through a document-specific, business-scoped anchor and the existing Journal Engine unique system-journal guard.
- Reports are read-only. Report generation never creates, updates, or repairs accounting journals.
- Reconciliation differences are controls to be surfaced, not values to be silently adjusted away.

## 4. Existing Functionality to Reuse

- Chart of accounts, control-account flags, account hierarchy, global configuration, and outlet Cash/Petty Cash/Bank overrides: [AccountingConfigurationService.cs](src/RetailPOS.API/Services/AccountingConfigurationService.cs).
- Journal lifecycle, balancing, period validation, journal numbering, idempotent system-journal creation, and linked reversals: [JournalEntryService.cs](src/RetailPOS.API/Services/JournalEntryService.cs).
- Financial-year and period lifecycle: [FinancialPeriodService.cs](src/RetailPOS.API/Services/FinancialPeriodService.cs).
- Opening-balance and opening-balance adjustment workflow: [OpeningBalanceService.cs](src/RetailPOS.API/Services/OpeningBalanceService.cs).
- Existing accounting reports and report permissions: [AccountingReportService.cs](src/RetailPOS.API/Services/AccountingReportService.cs) and [AccountingReportsController.cs](src/RetailPOS.API/Controllers/AccountingReportsController.cs).
- Supplier payment, allocation, and supplier advance workflow: [SupplierPaymentService.cs](src/RetailPOS.API/Services/SupplierPaymentService.cs).
- Historical sale cost, COGS, inventory valuation, and Cash Book practices accepted in Phase 2.

## 5. Recommended Milestone Dependency and Delivery Order

1. **P3-01 - Balance Sheet & Enhanced General Ledger** establishes trusted as-of financial statement and account drill-down behavior.
2. **P3-02 - AP Subledger & Aging** uses existing supplier payment/advance allocations and adds required payable reporting controls.
3. **P3-03 - Accounts Receivable Foundation** introduces a new operational lifecycle only after business decisions are approved.
4. **P3-04 - AR Aging** is deferred/not required under the approved current business model because no Accounts Receivable lifecycle exists.
5. **P3-05 - VAT / Tax Reporting** uses posted VAT lines, period governance, and the improved statement/reconciliation conventions from P3-01.
6. **P3-06 - Bank Settlement & Reconciliation** uses configured liquidity accounts, Cash Book, and controlled period behavior; it may proceed after P3-01 but should coordinate with P3-05 where tax settlement affects bank matching.
7. **P3-07 - Period-End & Year-End Closing** depends on P3-01, P3-02, P3-05, and P3-06 because control-account reconciliations are close gates. It does not depend on deferred/not-required P3-03 or P3-04 under the current business model.
8. **P3-08 - Management Reporting & Accounting Hardening** consolidates the accepted statement/subledger/reconciliation model and completes identified hardening work.

**Approved business-scope decision (2026-09-05):** IraniMart does not require customer credit sales. POS sales must remain fully settled at completion using the existing cash, card, mobile, and supported split-payment model. Unpaid sales, partially settled sales, sale-on-account/deferred payment, POS-generated Accounts Receivable, later customer receipts, customer advances/deposits, and AR Aging are not part of the approved current model. Therefore P3-03 and P3-04 are **Deferred / Not Required** and must not be implemented implicitly through unsupported payment-method values. No P3-03/P3-04 acceptance checklists are created. This is not incomplete Phase 3 work.

## 6. Detailed Milestone Plan

### P3-01 - Balance Sheet & Enhanced General Ledger

#### Implementation status
- Implemented: journal-only Balance Sheet, enhanced General Ledger, independently calculated comparative Balance Sheet and P&L endpoints, typed Angular Balance Sheet and enhanced GL pages, and direct account-to-ledger reconciliation tests.
- Historical P3-01 scope boundary: P3-02 through P3-08 were deferred when P3-01 was accepted. Subsequent accepted milestones are recorded in their individual checklists and the Phase 3 sign-off.

#### Business objective
- Give finance users an as-of-date Balance Sheet and a detailed, reconcilable account ledger that explain every statement balance.

#### Existing functionality to reuse
- Chart-account type, hierarchy, normal balance, and control-account metadata.
- Posted Journal Engine rows, financial years, accounting periods, opening balances, P&L, Trial Balance, COGS, inventory valuation, and Cash Book reports.

#### In scope
- Journal-only Balance Sheet as of a selected date.
- Asset, Liability, and Equity hierarchy with configured/current retained earnings presentation.
- Enhanced detailed GL: opening balance, movements, debit, credit, running balance, closing balance, source-document drill-down, and deterministic order.
- Comparative P&L and Balance Sheet where a compatible prior period is selected.
- Financial statement to GL reconciliation by account and section.

#### Out of scope
- New operational postings, budgeting, consolidated multi-business reporting, cash-flow statement, and AP/AR aging.

#### Accounting and journal rules
- Include posted journals only; preserve original and linked reversal effects in their actual posting dates.
- Determine each Balance Sheet account balance using its persisted `NormalBalanceType`: debit-normal accounts are debit less credit and credit-normal accounts are credit less debit. Account type determines the Asset, Liability, or Equity statement section; hierarchy rollups use the signed normal-balance result.
- Current-period earnings are calculated dynamically as of the selected date from posted Income and Expense/COGS journal lines within the selected financial year. Do not use a mutable account-balance cache or post a current-earnings journal merely to render a report.
- The current default chart contains credit-normal Owner Capital and debit-normal Owner Drawings, but no Retained Earnings account and no retained-earnings configuration mapping. Owner Capital is technically an Equity account but is not the recommended retained-earnings presentation because it would conflate owner contributions with accumulated operating results.
- Recommended model: add a dedicated credit-normal Retained Earnings Equity chart account, a dedicated `RetainedEarningsAccountId` configuration mapping, and a separately named P3-07 year-close transfer policy. Before P3-07, prior-year earnings must be displayed as a distinct dynamic "unclosed prior-year earnings" presentation amount derived from posted Income/Expense lines, not labelled as posted retained earnings and not merged into Owner Capital. After an approved P3-07 closing journal transfers a year result, that closed-year dynamic amount must be excluded to avoid double counting.

#### Source of truth
- `JournalEntry` and `JournalEntryLine` joined to `ChartAccount`, with opening/movement/closing computation from posted lines.

#### Required entities/schema changes
- No new accounting ledger.
- Additive report DTOs only.
- Add a dedicated Retained Earnings chart-account default and an explicit `RetainedEarningsAccountId` configuration slot. This is an additive configuration change, not a parallel ledger.

#### Required configuration
- Valid chart-account hierarchy and normal balances.
- Resolved prerequisite: a dedicated credit-normal Equity `Retained Earnings` account and `RetainedEarningsAccountId` configuration mapping are implemented. Owner Capital remains separately mapped for owner contributions and Owner Drawings remains separately mapped for withdrawals. The prerequisite does not create balances or closing journals.

#### API and Angular/UI requirements
- Balance Sheet, enhanced GL, and comparative statement endpoints under accounting reports.
- Date/as-of, comparison date/range, business-context, authorized outlet/location, account, account-group, journal type, and reference filters where applicable.
- Statement hierarchy, expand-to-account, account-to-journal drill-down, reconciliation totals, print/export, loading, empty, and exception states.

#### Permissions and isolation
- Dedicated `accounting.reports.balance_sheet` permission and existing GL permission.
- Tenant context is server-derived. Reuse authorized outlet/location resolution for all query scopes.

#### Idempotency, atomicity, reversal/correction
- Read-only reporting: no posting or idempotency concern.
- Reversal lines appear by posting date; reports do not mutate historical journals.

#### Reconciliation requirements
- Assets equal Liabilities plus Equity.
- Each displayed account total agrees with enhanced GL closing balance for the same scope/as-of date.
- Comparative statement totals reconcile independently for each selected period.

#### Legacy-data/coexistence considerations
- Clearly display journal start-date/cutover limits. Opening balances provide a valid first Balance Sheet only when they are a complete, balanced accounting-start baseline for all Assets, Liabilities, and Equity, including the approved historical-equity/retained-result position. Opening balances alone cannot reconstruct the historical split between owner capital and prior earnings when that split was not captured.
- Treat initial outlet/location Balance Sheets as management views only. The business-level Balance Sheet is the primary financial statement unless legal/statutory entity and location reporting requirements are separately confirmed.

#### Required automated tests
- Asset/liability/equity normal-balance signs and hierarchy rollups.
- Current earnings and retained-earnings presentation.
- Opening/movement/closing detailed GL, deterministic ordering, reversal timing, comparison periods, tenant/location isolation, permissions, and statement-to-GL equality.

#### Acceptance criteria
- Balance Sheet balances and every shown account reconciles to posted GL lines.
- Detailed GL explains opening through closing without operational-table duplication.
- Cross-tenant and unauthorized-location data cannot appear.

#### Dependencies
- Accepted Phase 1 and Phase 2 foundation.

11. **P3-02 AP reporting accepted:** Added typed historical AP subledger, ageing, supplier statement, Supplier Payable GL reconciliation, and Supplier Advance GL reconciliation services and endpoints; dedicated AP report permissions; Angular filters/totals/detail/reconciliation pages; and focused executable evidence in `P302ApReportServiceTests` (8/8). See [P3-02_AP_SUBLEDGER_AND_AGING_ACCEPTANCE_CHECKLIST.md](Docs/10_Accounts_And_Finance/P3-02_AP_SUBLEDGER_AND_AGING_ACCEPTANCE_CHECKLIST.md). Legacy Bills remain excluded from authoritative totals and surfaced separately. P3-03 remains deferred/not required.

#### Decision Required
- Retained Earnings account/mapping decision is resolved: do not reuse Owner Capital. P3-07 year-close transfer, timing, and posting policy are resolved and implemented; see the P3-07 section and acceptance checklist.
- Confirm that outlet/location Balance Sheets are management views only; legal/statutory presentation remains business-level until a separate requirement confirms otherwise.

---

Historical implementation timeline below. It is superseded for status purposes by the Phase 3 plan header, milestone acceptance checklists, and [PHASE_3_ACCOUNTING_SIGN_OFF.md](PHASE_3_ACCOUNTING_SIGN_OFF.md).
### P3-02 - AP Subledger & Aging

#### Historical implementation timeline
- **Approved P3-06 prerequisite design (2026-09-06):** The baseline policies are approved for implementation planning. P3-06 remains unimplemented and no acceptance checklist is created. The Journal Engine remains the financial source of truth; every Bank/Clearing/settlement operational record is evidence/status only.
- **Minimum entities/schema:** add Business-owned `BankAccount` linked one-to-one to a Bank ChartAccount, with optional OutletId, active status, display/reference fields, and account-scope authorization; `BankStatementImport` with file name, checksum, import status, source metadata, and deterministic idempotency; immutable `BankStatementLine` with external reference, posting/value date, signed amount, description, running/balance fields where supplied, and import linkage; `BankReconciliationSession` with BankAccount, cutoff, opening/closing statement balances, status and Finance approval; `BankReconciliationMatch` with explicit one-to-one, one-to-many, or many-to-one match groups, match status, matched GL journal/line references, timestamps, actor and audit reason; `SettlementBatch` only for manual/provider-evidence settlement groups; and statement-linked adjustment proposal/reference records. None may duplicate GL balances.
- **Configuration additions:** add configurable Card Clearing and Mobile/MFS Clearing ChartAccount mappings, permitted payment-method-to-account mapping, bank fee Expense account, bank interest Income account, Reconciliation Start Date, validated Opening Statement Balance, exact initial matching tolerance `0.00`, CSV import policy, and Finance reconciliation approval policy. Preserve existing Cash, Petty Cash, Bank, Vat and other account resolution/outlet override conventions; never hardcode account IDs.
- **Default accounts:** existing Bank Account, Cash on Hand and Petty Cash defaults remain. Add separate clearing defaults only when the approved mapping migration is implemented; clearing accounts must be appropriate Asset/current clearing accounts and must not reuse the Bank account. Each configured BankAccount must reference an existing Business-owned ChartAccount.
- **Safe clearing cutover:** introduce a Business-level P3-06 activation/cutover timestamp or accounting posting date. Posted sales before the cutover retain their historical direct-to-Bank card/mobile/mfs journals and are never rewritten or migrated. Only sales posted on/after the approved cutover use Card Clearing or Mobile/MFS Clearing mappings. The cutover must be immutable/audited once activated or changed only through an approved configuration correction; Cash sales continue posting to Cash.
- **Settlement posting:** a manual settlement batch/evidence group transfers the selected clearing account to the selected BankAccount through Journal Engine without revenue duplication. Fees post separately to the configured Expense account and interest to the configured Income account only after approved statement-linked adjustment. Chargebacks and failed settlements remain explicit exceptions initially.
- **Cash-to-Bank and other transfers:** cash deposit to bank posts Dr selected BankAccount / Cr Cash; bank withdrawal posts Dr Cash / Cr Bank; bank-to-bank transfer posts Dr destination Bank / Cr source Bank. These are controlled transfer journals with source references, business/location scope, period validation, deterministic idempotency, and linked reversal support. They are not inferred from POS cash sales; Cash sales remain Cash until a separate deposit is posted.
- **Statement import and matching:** CSV-first import is protected by source-file checksum/idempotency. Imported lines are immutable external evidence. Matching uses explicit match groups supporting one-to-one, one-to-many and many-to-one; no arbitrary partial-amount matching initially. Initial monetary tolerance is exact `0.00`; date/reference assist candidate selection only and never silently force a match. Manual match/unmatch is fully audited.
- **Reconciliation calculation:** independently compare statement opening/closing and imported statement movements with posted Bank/Clearing GL activity for the same BankAccount, BusinessId, authorized account/location scope and cutoff. Return matched items, unmatched statement lines, unmatched GL items, timing differences, approved adjustments and signed reconciliation difference. Never create an automatic adjustment merely to reach zero.
- **Adjustment workflow:** statement-linked bank fee/interest/approved correction proposals are separate from imported evidence; Finance-authorized approval posts a deterministic Journal Engine adjustment and records the source statement-line linkage. Unmatch/reopen/reversal changes match/session status with linked audit history and never edits posted journals or imported lines.
- **Cash Book behavior after cutover:** Cash Book continues to report separate configured Cash, Petty Cash, Bank, and newly configured Clearing accounts from JournalEntryLines. Historical direct-to-Bank activity remains in Bank; post-cutover card/mobile capture appears in Clearing; later settlement transfer appears in Bank and Clearing with equal/opposite journal effects. Cash Book must not duplicate settlement as revenue or merge Clearing into Bank silently; it should expose each account and reconciliation difference separately.
- **Security and permissions:** BankAccount, statement import, match/unmatch, settlement batch, reconciliation close, adjustment approval and reconciliation report permissions are separate server-side permissions. Every query and transition is BusinessId-scoped and account/location-authorized; optional outlet scope on BankAccount must be enforced alongside authorized outlet/warehouse scope.
- **Idempotency/atomicity:** imports use checksum plus deterministic source identifiers; settlement batches and adjustments use business-scoped deterministic anchors; import persistence, match transitions, settlement posting, adjustment posting and reconciliation close are atomic at their approved transition. Reuse existing Journal Engine/service transaction patterns; no new transaction framework is required.
- **Legacy/cutover:** Bank GL activity before the Reconciliation Start Date remains unreconciled opening history unless a validated Opening Statement Balance/opening reconciliation is loaded. No historical statement matches or clearing migrations are fabricated. The opening balance and start date become the boundary for reconciliation completeness.
- **Decision status:** the approved baseline resolves bank-account ownership, Card/Mobile clearing, manual settlement, CSV-first import, exact matching, partial-match grouping, fee/interest approval, exception handling, reconciliation approval, and cutover policy. No unresolved business/accounting decision remains before implementation; P3-06 technical implementation prerequisites are the additive schema/configuration/permission changes above.
- **Mandatory prerequisite foundation implemented; P3-05 reporting remains unimplemented and not accepted.** Added nullable immutable VAT snapshots for SaleItem, PurchaseOrderItem/GrnItem, Expense, and OtherIncomeEntry; separate `VatReceivableAccountId` / `vat_receivable` Input VAT mapping with Asset validation and default Input VAT Receivable account; VAT-aware GRN, Expense, and Other Income posting; and Input/Output VAT configuration UI fields. Migration `20260905225313_AddVatPrerequisiteSnapshotsAndInputVatAccount` adds only nullable snapshot columns and a nullable restricted FK; it does not backfill historical tax metadata.
- **P3-05 reporting implementation added, pending acceptance:** typed VAT Summary, transaction detail, VAT exception, and independent combined Output/Input/Net reconciliation endpoints use accounting posting dates, persisted VAT snapshots, and independently queried VAT control-account journal lines. Added guarded Angular Summary/Details/Reconciliation/Exceptions pages and `accounting.reports.vat` permission/seed wiring. Acceptance evidence is tracked in [P3-05_VAT_TAX_REPORTING_ACCEPTANCE_CHECKLIST.md](Docs/10_Accounts_And_Finance/P3-05_VAT_TAX_REPORTING_ACCEPTANCE_CHECKLIST.md); the milestone is not accepted.
- Focused validation after reporting implementation: 28 tests passed, including VAT/report/security/AP coverage; backend Release build passed; Angular production build passed. Full backend baseline before final reporting additions was 383/383. Formal acceptance remains blocked by dedicated clean/mismatch reconciliation fixtures, complete later-period refund/void source-event coverage, and relational VAT fault-injection tests.
- Final VAT acceptance fixture set passes 4/4; full backend suite passes 387/387; frontend suite passes 38/38; Backend Release and Angular production builds pass. P3-05 remains unaccepted only because dedicated VAT relational fault-injection evidence and complete endpoint/reversal acceptance coverage are still outstanding.
- Final strict review completed: `ExpenseService.ApproveAsync` now wraps Journal Engine posting, Expense approval state/linkage, audit, and commit in one ReadCommitted transaction. `P305ExpenseVatAtomicityTests` proves fresh-context rollback and exact retry. GRN and Other Income retain their existing source transaction boundaries; endpoint authorization evidence covers all VAT endpoints. Final focused P3-05 suite passes 27/27, full backend suite passes 389/389, frontend suite passes 38/38, and both Release builds pass. P3-05 acceptance checklist is complete and marked ready for acceptance review.
- Existing focused prerequisite/regression evidence after these changes: 17 targeted sale/GRN/AP/Expense/Other Income/configuration tests passed; full backend suite 383/383 passed; frontend suite 38/38 passed; backend Release build passed; Angular production build passed. Existing P2-01 VAT sale/refund/exchange behavior remains green. No P3-05 report API/UI or acceptance checklist was created.
- Remaining before reporting implementation: add dedicated executable VAT tests for VAT/non-VAT GRNs, VAT/non-VAT Expense and Other Income, source atomicity/fault injection, historical snapshot immutability, rounding boundaries, tenant/location isolation, and legacy exceptions; approve and document report-period/reversal policy and any tax classes/rates/rounding rules. Bangladesh statutory filing/Mushak/NBR workflows remain out of scope.
- **P3-02 prerequisite foundation implemented; AP subledger reports, ageing, supplier statements, and acceptance remain deferred.** No P3-02 acceptance checklist has been started.
- Verified payable recognition event: a completed GRN/purchase receipt posts Dr Inventory / Cr configured Supplier Payable when it is not immediately settled through Cash/Bank. This posted purchase-receipt journal is the current financial payable recognition event.
- The legacy `Bill` record is not the authoritative payable document: it is mutable, has no business scope or journal/GRN link, is created independently of GRN completion, and is the current target of supplier-payment allocations. It must not be reused as the P3-02 financial source of truth.

#### Business objective
- Make supplier obligations, due dates, allocations, advances, and ageing visible while reconciling every payable balance to the Supplier Payable GL control account.

#### Existing functionality to reuse
- Purchase/GRN posting, supplier master data, supplier payment allocations, supplier advance balances, and `SupplierPaymentService` workflow.
- Payments and allocations reduce the supplier open balance only when their accounting workflow is posted.
- Supplier advances remain separately visible until allocated; they reconcile to the Supplier Advance GL control account.
- Add payment-term/due-date fields to the purchase/GRN payable source if absent. Current `Supplier`, `PurchaseOrder`, and `Grn` models do not persist payment terms, invoice date, or a derived due date.
- Add an additive, business-scoped payable-document projection keyed to the posted supplier-payable GRN/purchase-receipt journal. It must retain original posted amount, supplier, source reference, document/posting date, due-date basis, status, and journal linkage; it must not duplicate GL as a second ledger.
- Add posted-effective allocation and reversal history. Existing `SupplierPaymentAllocation` records retain an allocation date and snapshots but no posted status/journal linkage, while payment/advance reversal fields are not implemented. Current records therefore cannot reliably reconstruct allocation, outstanding payable, or unapplied advance as of a historical date.
- Add supplier-advance utilisation records and controlled journal-linked application/reversal behavior. Existing `SupplierAdvance.RemainingAmount` and `AppliedAmount` are not updated through an application workflow.

#### Required configuration
- Supplier Payable and Supplier Advance control account mappings; configured business default supplier terms (initially 30 calendar days); supplier-specific term override; approved ageing-bucket definitions.

#### API and Angular/UI requirements
- Supplier subledger, supplier statement, AP ageing, advance history, and reconciliation endpoints/pages with export.
- Filters: supplier, status, due date, ageing date, outlet/warehouse where source supports it, document/reference, and business context.

#### Permissions and isolation
- New AP reporting permissions; reuse supplier and accounting reporting scope rules.
- Tenant and authorized outlet/location filters must apply to both document detail and GL reconciliation.
- Prerequisite security correction: existing supplier payment and advance submit/approve lookups are by record ID without a `BusinessId` predicate, and their controller does not apply the shared outlet/location authorization service. This must be corrected and regression-tested before P3-02 exposes AP balances or actions.

#### Idempotency, atomicity, reversal/correction
- Reuse payment/advance workflow idempotency and journal anchors.
- Allocation, payment status, advance remaining amount, and journal posting must remain atomic.
- Reversals restore document/advance allocations through linked corrections, never by editing posted balances.

#### Reconciliation requirements
- Sum of open supplier payable documents equals Supplier Payable GL balance for identical scope/as-of date.
- Unapplied supplier advances equal Supplier Advance GL balance.
- Differences are returned and highlighted.

#### Legacy-data/coexistence considerations
- Legacy bills without reliable business scope, journal links, original immutable payable amounts, or validated due dates must be marked as legacy/unreconciled until a controlled migration establishes a reliable opening position. Do not revive or mutate legacy bills merely to make AP totals reconcile.

#### Required automated tests
- Terms/due-date calculation, partial payment, one payment across multiple payables, advance creation/use, reversal, ageing buckets, supplier statements, AP/advance GL reconciliation, tenant/location isolation, permissions, and legacy exceptions.

#### Acceptance criteria
- No duplicate AP ledger is created.
- Supplier statement open balances, ageing totals, and GL control balances reconcile.
- Payments and advances are traceable to source documents and journals.

#### Dependencies
- P3-01 reporting conventions; accepted P2-02 and P2-03 workflows.

#### Genuine prerequisites before implementation
1. Resolved: posted supplier-payable GRN/purchase receipt is the authoritative recognition event. `SupplierPayableDocument` is an additive, business-scoped, source-journal-linked projection; legacy `Bill` is not authoritative and is not backfilled.
2. Resolved for new payable documents: supplier invoice date is the due-date basis when captured; otherwise receipt recognition date is used. Supplier-specific terms override the business default of 30 calendar days. Historical records without reliable dates remain legacy/unreconciled exceptions.
3. Resolved foundation: payment allocations are pending until the supplier payment journal posts, then retain effective date/journal linkage; controlled supplier advance utilisation posts Dr Supplier Payable / Cr Supplier Advance and records effective/reversal history.
4. Supplier tenancy foundation resolved: suppliers now carry nullable business ownership, tenant-scoped supplier APIs/repository queries use server-derived business context, and purchase-order supplier validation is scoped to the selected warehouse/tenant business. Migration backfills only a supplier with exactly one determinable warehouse-business ownership and records all ambiguous/unowned suppliers in `supplier_business_ownership_exceptions` for manual resolution.
5. Payment reversal foundation resolved: a posted supplier payment is reversed through the original posted payment journal using the Journal Engine reversal path and the requested later open posting date. The original payment/journal/allocation records remain preserved; original allocation effective dates are retained separately from `ReversedEffectiveDate` and reversal-journal linkage, and repeat reversal is idempotent. A supplier advance created by the payment is reversed only when it has no effective dependent utilisation; dependent utilisations must be reversed first.
6. Verified migrations: `20260905132241_AddSupplierPayableSubledgerFoundation`, `20260905133337_AddSupplierBusinessOwnership`, `20260905150433_AddSupplierPaymentReversalFoundation`, and `20260905151608_PreserveSupplierAllocationReversalHistory` are applied.
7. A reusable relational fault-injection harness is verified in `P302RelationalFaultInjectionTests.CompleteAsync_RollsBackJournalAndGrn_WhenPayableProjectionPersistenceFails_ThenRetriesSafely`. It uses a shared SQLite in-memory connection, fresh verification contexts, and a test-only `SaveChangesInterceptor` that throws when a new `SupplierPayableDocument` is tracked during the GRN outer transaction's final save. The proof confirms rollback of the posted journal and lines, payable projection, GRN completion, and inventory quantity mutation; retry then creates exactly one posted journal, two lines, and one payable projection.
8. Verified: supplier payment and allocation atomicity is proven in `P302SupplierPaymentAllocationAtomicityTests` with three scenarios: (a) `CompleteAsync_RollsBackJournalAndAllocations_WhenAllocationActivationPersistenceFails_ThenRetriesSafely` proves single allocation (50m to 50m payable) rolls back when activation fails, then retries successfully with fresh journal and effective allocation; (b) `ApproveAsync_RollsBackBothAllocations_WhenMultiplePayablesAndActivationFails` proves two allocations (50m to payable 100 and 50m to payable 101) both roll back and retry atomically under one journal; (c) `ApproveAsync_RollsBackPartialPayment_WhenActivationFails` proves partial payment (100m payment, 30m to 50m payable, 70m advance) rolls back and retries atomically. Fresh-context assertions confirm rollback state (payment submitted, no journal lines, allocations pending) and success state (payment posted, one journal with two lines, allocations effective with EffectiveDate linked to payment PostingDate). Injection boundary is SaveChangesInterceptor detecting allocation.Status="effective" during SupplierPaymentService.ApproveAsync outer transaction. All 3 tests pass deterministically; no atomicity defects discovered.
9. Remaining before full P3-02 reporting: comprehensive failure-injection coverage for every projection/allocation/utilisation/payment-reversal boundary must be completed before AP balances or ageing reports are exposed.
10. **P3-02 prerequisite atomicity batch completed:** `P302SupplierPaymentReversalAtomicityTests` uses the reusable shared in-memory SQLite relational harness, real `SupplierPaymentService`/`JournalEntryService` transactions, fresh verification contexts, and four executable boundaries: `ReversalJournal` (reversal journal creation to original/payment state), `OriginalJournalState` (original journal reversal state to allocation restoration), `AllocationRestoration` (allocation restoration to final payment state), and `AdvanceReversal` (originating overpayment advance reversal within the overall payment reversal transaction). The journal-state and allocation/advance-state failures prove the closest meaningful `SaveChanges` boundaries where later mutations share one final save. The suite covers `ReverseAsync_ReversalJournalBoundaryFailure_RollsBackEverything_ThenRetriesWithoutDuplicate`, `ReverseAsync_OriginalJournalStateBoundaryFailure_RollsBackJournalAndPaymentState`, `ReverseAsync_MultiAllocationRestorationBoundaryFailure_RollsBackBothAllocationsAndPayables_ThenRetries`, `ReverseAsync_PartialPaymentAgainstSharedPayable_ReversesOnlySelectedPayment`, `ReverseAsync_AdvanceReversalBoundaryFailure_RollsBackPaymentAllocationAdvanceAndJournals_ThenRetries`, `ReverseAsync_DependentAdvanceUtilisation_IsRejectedBeforeAnyMutation`, and `ReverseAsync_LaterOpenPeriodPreservesOriginalDate_AndClosedPeriodRejectsWithoutMutation`. All 7 pass. Fresh contexts prove failed rollback preserves the posted payment, original journal, effective allocations, original effective dates, payable state, advance state, and absence of reversal journals; successful retry creates one linked reversal, restores payable availability exactly once, reverses only the originating advance, preserves unrelated effective payments, and remains idempotent. The batch exposed and fixed one production defect: `SupplierPaymentService.ReverseAsync` marked allocations reversed but did not recompute affected `SupplierPayableDocument.Status` from surviving effective payment allocations and advance utilisations. Existing regressions remain green: `P302SupplierPaymentAllocationAtomicityTests` 3/3, `P302SupplierAdvanceUtilisationAtomicityTests` 8/8, `P302RelationalFaultInjectionTests` 1/1, and `SupplierPaymentWorkflowServiceTests` 5/5. No AP reporting/UI, P3-02 acceptance checklist, or P3-03 work was started.

#### Decision Required
- Default and supplier-specific payment terms, calendar-day versus business-day handling, and due-date basis (supplier invoice date, GRN receipt date, or another approved payable document date).
- Ageing bucket definitions and treatment of disputed payables.
- Supplier credit/debit-note workflow, approval authority, source-document linkage, and whether it changes inventory/GRN state.
- Approve the authoritative-payable policy described above and the legacy cutover/migration policy for existing Bills.

---

### P3-03 - Accounts Receivable Foundation

#### Historical implementation timeline
- **P3-03 is Deferred / Not Required under the approved current business model.** Customer credit sales are not required. The current POS supports customer selection and full settlement through cash/card/mobile and split payments, but must not be extended with sale-on-account, unpaid or partially paid sales, customer outstanding balances, later customer receipts, customer advances/deposits, or POS-generated Accounts Receivable unless the business decision is formally revisited.
- Verified current POS settlement rule: `SaleService.CreateAsync` requires split payment totals to equal the computed sale total, creates a full-amount `SalePayment` when no split is supplied, and the POS completion guard requires full cash tender or full non-cash/split payment. Supported current methods are cash, card, and mobile; there is no credit/deferred method.
- Verified current sale posting: `PostSaleAccountingAsync` debits the configured cash/bank account for each completed payment, credits Sales Revenue and VAT Payable, and posts COGS/Inventory lines. Customer selection only supplies a `CustomerId` dimension; it does not change the receivable lifecycle. Refund/void posting reverses payment-account, sales-return, VAT, and COGS/inventory effects and does not model an outstanding receivable.
- Verified general tenant/security hardening concern, separate from deferred AR work: `Customer` has no `BusinessId`, outlet, or warehouse ownership. `CustomerRepository.GetByIdAsync`, search, quick search, phone, email, and code lookups are not business-scoped, and `SaleService.CreateAsync` validates a selected customer by ID without a tenant predicate. This issue is recorded for general tenancy/security hardening and is not being implemented as an AR prerequisite in this step.
- Verified receipt/advance gap: `SalePayment` is an immutable-at-completion payment breakdown tied to a sale, not a later receipt/allocation document. No customer receipt, receipt allocation, customer advance transaction, advance application, receipt reversal, effective-date history, or customer credit-note workflow exists. `CustomerAdvanceAccountId` and a default Customer Advances chart account exist, but no Accounts Receivable configuration slot exists; the seed contains an Accounts Receivable chart account without a corresponding configuration mapping.
- Proposed authoritative model remains additive and journal-linked, consistent with P3-02: a business-scoped `CustomerReceivableDocument`/invoice projection, receipt and receipt-allocation history, customer-advance allocation/reversal history, and credit-note records. Journal Engine remains the financial source of truth; no mutable customer balance or second financial ledger should be introduced.
- If the business decision is ever revisited, future prerequisites would include credit-sale eligibility/approval and limits, terms/due dates, partial POS payment policy, later receipt methods and allocation ordering, overpayment/advance treatment, credit-note/return policy, overdue blocking/override, receipt reversal/period policy, AR account mapping and outlet overrides, and customer tenancy migration/isolation. Existing VAT and COGS/Inventory posting could then be retained, with a future credit sale replacing the payment-account debit with Dr Accounts Receivable while preserving revenue, VAT, and COGS/Inventory lines.

#### Business objective
- Introduce controlled customer credit sales, receipts, advances, credit notes, and a customer subledger that reconciles to an AR control account.

#### Existing functionality to reuse
- Customer master data, Sale/SaleItem, split payment infrastructure, Journal Engine, Cash/Bank configuration, customer dimension on journal lines, and Customer Advance account mapping.

#### In scope
- Customer credit-sale lifecycle with receivable/invoice state.
- AR control account, customer payment terms and credit limits.
- Customer receipts, partial receipts, multi-invoice allocation, customer advances/unallocated receipts, credit notes, receipt reversals/corrections, customer subledger, statements, and AR-to-GL reconciliation.

#### Out of scope
- Automatic bad-debt estimation, debt collection automation, external payment gateway settlement, subscription invoicing, and P3-04 ageing reports.

#### Accounting and journal rules
- Approved/completed credit sale: Dr Accounts Receivable / Cr Sales Revenue, with separate VAT and COGS/Inventory lines when applicable.
- Customer receipt: Dr Cash/Bank/Clearing / Cr Accounts Receivable for allocated amount.
- Unallocated receipt or overpayment: Dr Cash/Bank/Clearing / Cr Customer Advance liability.
- Allocation of an existing customer advance: Dr Customer Advance / Cr Accounts Receivable.
- Credit note: controlled reduction of AR, revenue/VAT, and inventory/COGS only when the approved operational return policy requires it.
- Bad debt/write-off must post to a configured approved expense/allowance policy only after the business decision is confirmed.

#### Source of truth
- Journal Engine for financial balances; a new customer receivable/invoice and receipt-allocation subledger for customer-specific document state and allocation history.

#### Required entities/schema changes
- Additive, business-scoped customer invoice/receivable document, receipt, receipt allocation, customer advance allocation, and credit-note records.
- Extend customer profile only with approved credit terms/limit/eligibility fields. Do not store a mutable customer balance as a financial authority.
- Add AR control and, if approved, bad-debt/allowance account configuration slots.

#### Required configuration
- AR control account; Customer Advance account; payment/cash/bank/clearing mappings; approved receipt numbering; credit-note and bad-debt account policy if enabled.

#### API and Angular/UI requirements
- Credit-sale option in POS/sales only after decision approval; customer account view; receipt entry/allocation; customer advance and credit-note workflows; customer statement and subledger APIs/pages.
- Clear invoice state, due date, available credit, allocation, reversal, and reconciliation status.

#### Permissions and isolation
- Dedicated AR/credit-sale/receipt/credit-note/report permissions with separation of cashier, credit approver, and finance authority as required.
- Tenant, customer, outlet/location, and authorized-user scope must be enforced server-side.

#### Idempotency, atomicity, reversal/correction
- Use business-scoped idempotency keys for credit sale, receipt, allocation, advance, and credit-note posting.
- Sale/invoice/stock/journal and receipt/allocation/journal updates must each be atomic.
- Posted credit sales, receipts, and notes use reversal/adjustment journals and linked operational correction states; no direct mutation of posted financial history.

#### Reconciliation requirements
- Customer subledger open receivables equal AR control GL for identical scope/as-of date.
- Unallocated customer advances equal Customer Advance GL.
- Allocation and credit-note totals reconcile to their source journals.

#### Legacy-data/coexistence considerations
- Existing cash sales remain cash sales. Do not reinterpret completed historical sales as credit invoices.
- Customer records without approved credit terms are ineligible for credit sales by default until the policy is decided.

#### Required automated tests
- Credit approval/limit, due-date calculation, credit-sale posting, partial receipt, one receipt across multiple invoices, overpayment advance, advance allocation, credit note, bad-debt decision behavior, reversal, atomic failure, retries, tenant/location isolation, permissions, and AR/advance GL reconciliation.

#### Acceptance criteria
- Credit sale and receipt workflows are financially complete, auditable, idempotent, and reconciled to control accounts.
- Cash and credit sales cannot be double-posted or silently converted between lifecycles.

#### Dependencies
- P3-01. P3-04 depends on this milestone.

#### Decision Required
- Which customers are eligible to buy on credit.
- Credit-limit calculation, whether outstanding orders count, and override authority.
- Payment-term catalog, due-date calculation, and calendar rules.
- Whether credit sales require approval and at which monetary thresholds.
- Whether overdue customers may make additional credit purchases and who can override the rule.
- Whether partial receipts are always allowed.
- Whether one receipt may allocate across multiple invoices and allocation ordering/defaults.
- Overpayment treatment, customer-advance refund rules, and authority to reallocate advances.
- Credit-note initiation/approval rules and whether stock return is mandatory.
- Bad-debt/write-off policy, approvals, and account mappings.
- Receipt reversal/correction process, reason requirements, and period treatment.

---

### P3-04 - AR Aging

#### Readiness review status
- **P3-04 is Deferred / Not Required under the approved current business model.** No Accounts Receivable lifecycle exists or is approved: POS sales remain fully settled at completion, so there are no authoritative receivables, later receipts, customer advances, or AR balances to age. Do not create AR Aging functionality or a P3-04 acceptance checklist unless the business scope is formally changed.

#### Business objective
- Give finance and collections users an accurate current/overdue receivable view and customer statements reconciled to AR control.

#### Existing functionality to reuse
- P3-03 customer invoices, receipts, allocations, advances, credit notes, Journal Engine, report filtering, and statement UI patterns.

#### In scope
- Customer ageing buckets, current/overdue balances, due-date analysis, statements, credit-note treatment, advances/unallocated receipts, and AR Aging to AR control reconciliation.

#### Out of scope
- Collection automation, legal recovery, credit scoring, bad-debt provisioning automation, and customer portal.

#### Accounting and journal rules
- Age only approved/posted receivable documents using approved due-date policy.
- Receipts/credit notes reduce the open amount; unallocated receipts remain customer advances, not negative invoices.
- Credit notes use their approved posting date and source linkage.

#### Source of truth
- P3-03 subledger document/allocation data for ageing detail; posted Journal Engine lines for AR control reconciliation.

#### Required entities/schema changes
- No new accounting ledger. Add only indexed projections or query indexes needed for ageing performance.

#### Required configuration
- Approved ageing bucket boundaries and due-date/credit-note presentation policy.

#### API and Angular/UI requirements
- AR ageing summary/detail, customer statement, as-of date, bucket, customer, outlet/location, collector/status, and export filters.

#### Permissions and isolation
- AR ageing/report permission; tenant and authorized location filtering; restrict sensitive customer balances by role policy.

#### Idempotency, atomicity, reversal/correction
- Read-only reporting. Reuse P3-03 correction records and journal reversals.

#### Reconciliation requirements
- Total open AR from ageing equals AR control GL for identical scope/as-of date.
- Customer advances and credit notes are separately shown and reconciled to their relevant control balances.

#### Legacy-data/coexistence considerations
- Receivables without approved document dates/due dates are legacy exceptions and cannot be bucketed precisely; surface them separately.

#### Required automated tests
- Bucket boundaries, as-of historical state, partial/multi-invoice receipts, advances, credit notes, reversals, control reconciliation, tenant/location isolation, permissions, and legacy exceptions.

#### Acceptance criteria
- Ageing totals, customer statements, and AR GL reconciliation agree without hidden estimates.

#### Dependencies
- P3-03.

#### Decision Required
- Ageing bucket definitions, credit-note ageing treatment, and whether disputed invoices are excluded or separately classified.

---

### P3-05 - VAT / Tax Reporting

#### Readiness review status
- **P3-05 is not ready for implementation as a complete output/input VAT reporting milestone.** The current production path has verified output-VAT posting for POS sales and sale refunds/voids, but purchase/GRN, Expense v2, and Other Income do not currently post separate input/output VAT control lines or persist tax snapshots sufficient for a complete reconciled VAT report.
- POS tax behavior is configuration-driven at the UI boundary: `TaxSettings` contains `TaxEnabled`, `DefaultTaxRate`, `TaxName`, and `PricesIncludeTax`; the current POS calculates tax as `round((subtotal - discount) * taxRate, 2)` and sends only the resulting `Sale.Tax` amount. `Product.TaxRate` exists and pricing exposes tax informationally, but POS completion uses the loaded default tax setting rather than a persisted per-line tax-rate/category snapshot. The current system-settings file has `TaxEnabled=true`, `DefaultTaxRate=0`, `TaxName=VAT`, and `PricesIncludeTax=false`.
- `SaleService.PostSaleAccountingAsync` posts payment-account debits, Sales Revenue credit, configured VAT Payable credit for the persisted sale tax amount, and COGS/Inventory lines. Refund/void posting uses the persisted sale tax amount proportionally, posts VAT Payable debit reversal at the refund/void posting date, and preserves the original journal. Exchange sales retain original/replacement linkage but do not introduce a separate tax document model. P2-01 VAT and exchange tests cover this output path.
- Purchase orders persist item-level `Tax` and include it in PO total calculations, but GRN accounting posts Dr Inventory/Expense versus Cr Supplier Payable/Bank using received quantity and unit cost only. No Input VAT/VAT Receivable line is posted, and no immutable purchase tax amount/rate/category snapshot is carried into the posted GRN journal or payable projection.
- Expense v2 and Other Income persist amount/payment/account/workflow fields but no VAT rate, tax amount, tax class, or tax-control account mapping. Their approval postings are expense/payment and payment/income journals respectively; they cannot currently participate reliably in input/output VAT reporting.
- The configured VAT structure is currently one output control mapping: `AccountingConfiguration.VatPayableAccountId`, resolved through `PostingAccountSlots.VatPayable` and constrained to a Liability account. No `VatReceivable`/Input VAT configuration slot or default input-VAT chart account exists. VAT Payable is not included in the required global configuration slot list, so onboarding/configuration validation does not make it universally mandatory.
- Historical readiness is partial: posted journals retain transaction/posting/business dates, source type/reference, journal linkage, outlet/warehouse dimensions, and reversal linkage/date. Sales retain a mutable aggregate tax amount, but not tax rate, tax code/class, taxable basis, or tax-inclusive/exclusive snapshot. Purchase tax, Expense tax, and Other Income tax metadata are absent from source postings. P3-05 therefore needs immutable tax snapshots and a controlled legacy/pre-tax exception policy before return-ready reporting.
- Reversal semantics are technically available for posted sale/refund/void journals through the Journal Engine and use actual posting dates, but purchase, Expense, and Other Income VAT reversals are not present because those flows do not post VAT lines. The reporting date basis and statutory treatment of later-period VAT reversals are not yet approved.
- Period governance exists through Financial Year/Accounting Period validation for journal posting and reversal. A future VAT report can use posted journal posting dates, but the transaction-date versus posting-date/tax-period policy must be approved before implementation. Opening balances and pre-tax-integration transactions cannot be treated as return-ready VAT without validated tax metadata.
- Tenant and location data is available on posted journal lines and source documents for current sale output VAT. A future VAT report must enforce tenant-derived BusinessId and authorized outlet/warehouse scope server-side. No dedicated VAT report permission currently exists; one is required before exposing the report.
- No Bangladesh-specific approved rate table, VAT class/exemption model, BIN/Mushak workflow, filing frequency, or statutory return/export format was found in the accounting implementation or Phase 3 plan. Existing terminology is limited to generic VAT/tax settings and a company TaxNumber field. No Bangladesh statutory assumptions are being added.
- Mandatory prerequisites before implementation: approve jurisdiction/tax classes/rates and reporting date policy; decide output/input account structure and add Input VAT/VAT Receivable mapping if recoverable input VAT is in scope; define purchase/GRN, Expense, and Other Income tax treatment; persist immutable rate/code/category/taxable-base snapshots for every reportable source; define later-period reversal and rounding policy; and define legacy/pre-tax exception handling.
- Optional enhancements after the boundary is approved: return-ready export/print layouts, filing workflow/status, tax-period lock metadata, and richer tax-code master data. No P3-05 code or acceptance checklist was created during this review.

#### Business objective
- Convert existing VAT control-account posting into reconciled, period-based, return-ready tax reporting.

#### Existing functionality to reuse
- VAT Payable configuration, sale/refund VAT journal lines, product tax rates, reporting service, financial period governance, and journal source references.

#### In scope
- Output VAT and input VAT reporting, tax-period reports, VAT control-account reconciliation, return-ready summaries, and tax-rate/class support where approved.

#### Out of scope
- Tax filing submission, jurisdiction APIs, payroll tax, customs duty, withholding tax, and automatic tax calculation changes without approved tax rules.

#### Accounting and journal rules
- Output VAT derives from posted tax liability lines; refund/credit-note VAT reversals use their actual posting date.
- Input VAT requires a verified purchase/expense tax posting policy and configured input VAT control account before it can be reported as recoverable tax.
- Tax return figures reconcile to VAT control-account movement, with rounding separately visible.

#### Source of truth
- Posted Journal Engine tax-control lines and their source-document references; never operational sales totals alone.

#### Required entities/schema changes
- Add tax code/class/rate snapshots, tax period/return status, and input-VAT mapping only after jurisdiction decisions are approved.

#### Required configuration
- Output VAT and input VAT control accounts, tax classes/rates, filing frequency, registration number, rounding policy, and jurisdiction-specific rules.

#### API and Angular/UI requirements
- Tax-period report, output/input detail, VAT-control reconciliation, exception list, return-ready summary, export, and audit drill-down.

#### Permissions and isolation
- Tax report/return preparation permission with tenant and authorized location filters. Filing/approval authority must be separate if filing is later introduced.

#### Idempotency, atomicity, reversal/correction
- Report generation is read-only. Tax adjustments require controlled source documents and idempotent system journals.
- Reversed source journals appear in their actual tax/posting period per approved statutory policy.

#### Reconciliation requirements
- Output VAT, input VAT, net liability/refund position, and VAT control-account movement reconcile for the same period/scope.

#### Legacy-data/coexistence considerations
- Pre-tax-integration transactions are explicitly outside return-ready reporting unless a controlled migration supplies validated tax metadata.

#### Required automated tests
- Taxable sale/refund, input tax documents, multiple rates/classes, period cutoffs, VAT-control reconciliation, rounding, tenant/location isolation, permissions, and legacy exclusions.

#### Acceptance criteria
- Every return-ready total is traceable to posted journals and reconciles to tax control accounts.

#### Dependencies
- P3-01; input-VAT scope depends on approved purchase/expense tax policy.

#### Decision Required
- Jurisdiction, filing frequency, return format/export requirements, tax registration obligations, allowable input VAT, tax classes/rates/exemptions, and statutory rounding rules.

---

### P3-06 - Bank Settlement & Reconciliation

#### Readiness review status
- **P3-06 foundation slice implemented; operational workflows remain out of scope.** The additive, journal-backed foundation now includes business-owned `BankAccount` metadata, Card and Mobile/MFS clearing mappings, an explicit business cutover date, and nullable `SalePayment.PaymentAccountId` snapshots. Statement import, settlement batches, matching, reconciliation sessions, transfers, adjustments, and reconciliation UI remain unimplemented.
- **P3-06 operational slice implemented (2026-09-06):** `SettlementBatch` and `LiquidityTransfer` are Business-scoped workflow records with journal linkage, reversal linkage/status, audit fields, and no mutable balance columns. Migration `20260906070447_AddP306SettlementBatchesAndLiquidityTransfers` adds the operational tables, tenant/account foreign keys, unique Business/idempotency indexes, and the configured settlement-fee expense mapping.
- **Settlement behavior:** Card and Mobile/MFS settlement is amount-based against posted clearing journal activity minus approved posted settlement activity; provider-level sale inclusion is intentionally not fabricated. Gross settlement is limited to eligible unsettled clearing activity, duplicate Business/idempotency keys return the existing result, and the destination BankAccount plus clearing mapping must belong to the active tenant. No-fee posting is Dr Bank / Cr Clearing. Fee posting is Dr Bank net / Dr configured Settlement Fee Expense / Cr Clearing gross. Settlement journals contain no revenue, VAT, COGS, or inventory lines.
- **Transfer behavior:** Cash-to-Bank, Bank-to-Cash, and Bank-to-Bank create only the approved Dr destination / Cr source lines. Amounts must be positive, source and destination accounts must differ, BankAccounts must be active and Business-owned, and Cash/Bank mappings are resolved through configuration and BankAccount ChartAccounts. Transfer journals contain no revenue, expense, VAT, COGS, or inventory lines.
- **Operational controls and evidence:** Settlement and transfer requests use deterministic Business-scoped idempotency anchors, execute operational persistence plus Journal Engine draft/post inside a relational transaction, and use Journal Engine linked reversal for later-period reversal. Repeated post/reversal requests are idempotent; original operational records and journals remain preserved. Dedicated P3-06 operational tests pass 8/8, combined P3-06 foundation/operational tests pass 27/27, relevant accounting regressions pass 30/30, full backend tests pass 416/416, and the backend Release build passes.
- **Operational hardening evidence completed (2026-09-06):** relational SQLite fault injection with fresh-context verification passes settlement and transfer rollback/retry exactly-once checks; later-open-period and closed-period reversal tests pass; eligibility is restored exactly once after settlement reversal; all Cash/Bank transfer directions preserve original source/destination accounts through reversal; HTTP permission, Business isolation, list/get leakage, and cross-Business resource checks pass. Hardening tests pass 9/9, HTTP isolation tests pass 1/1, the complete P3-06 focused set passes 36/36, relevant accounting regressions pass 35/35, the full backend suite passes 425/425, and the backend Release build passes.
- **Reversal transaction boundary:** settlement and transfer reversal now share one transaction between Journal Engine linked reversal and operational reversal linkage/status persistence, preventing an orphan reversal journal if operational completion fails. The API now depends on the registered `IAccountingConfigurationService`, fixing controller activation in the hosted API path.
- **Remaining P3-06 boundary:** statement import, immutable statement lines, reconciliation sessions/close, statement matching, bank fee/interest adjustments, chargeback accounting, and Angular reconciliation UI are deliberately deferred to the next slice. The current settlement eligibility boundary is account-level posted-Journal activity, not provider-level source allocation.
- **P3-06 statement evidence slice implemented (2026-09-06):** migration `20260906073833_AddP306BankStatementEvidence` adds Business/BankAccount-owned `BankStatementImport` and immutable `BankStatementLine` evidence. The only supported profile is explicit `csv-v1` with required `transaction_date,description,debit,credit` headers and optional `value_date,external_reference,running_balance,opening_balance,closing_balance,currency` fields. Dates use invariant explicit formats; decimals use invariant culture; debit/credit are mutually exclusive and signed amount is `credit - debit`.
- **Statement import controls:** SHA-256 is calculated from file content, with unique `(BusinessId, BankAccountId, SourceFileChecksum)` identity. Exact content retries, including renamed files, return the existing import; same-name different content creates a new import. Active tenant-owned BankAccount and optional outlet authorization are enforced server-side. Header and lines commit atomically; failed line persistence leaves no import or partial lines. Statement lines cannot be modified or deleted through normal DbContext persistence. Supplied balances are preserved and inconsistencies are surfaced as import warnings; absent balances remain null and never overwrite approved reconciliation configuration.
- **Statement evidence validation:** dedicated statement tests pass 10/10, including normalization, date/reference/narrative/running-balance preservation, malformed CSV rejection, checksum behavior, tenant/account checks, immutability, SQLite rollback/retry, and HTTP permissions/isolation. Combined P3-06 plus accounting regressions pass 71/71; the full backend suite and Release build remain the final validation gates.
- **P3-06 reconciliation/matching slice implemented (2026-09-06):** migration `20260906075817_AddP306BankReconciliationSessionsAndMatches` adds Business/BankAccount-owned `BankReconciliationSession`, explicit `BankReconciliationMatch` groups, and historical `BankReconciliationMatchLine` links. Sessions are Open/InProgress/ReadyForClose only; close/approval, adjustments, chargebacks, and UI remain deferred. Active overlapping sessions for the same BankAccount/date range are rejected, and the BankAccount reconciliation start date is enforced.
- **Match semantics:** statement signed amount is `credit - debit`; Bank GL signed amount is `debit - credit`. Manual groups support one-to-one, one-to-many, and many-to-one links only. A group requires exact equality with tolerance `0.00`; no journal is created and neither immutable statement evidence nor JournalEntryLines are mutated. Active filtered uniqueness on statement/GL link IDs prevents effective double consumption while unmatch sets links inactive and preserves history.
- **Reconciliation calculation:** statement opening/movement/closing and GL opening/movement/closing are calculated independently. GL opening is derived from posted Bank ChartAccount activity before the session period; period movement includes posted lines in the session cutoff; reversals appear in their actual posting periods. Matched and unmatched amounts/counts are derived from active links, and difference is `statement closing - GL closing`; no adjustment is generated.
- **Reconciliation evidence:** focused reconciliation/session/matching tests pass 5/5, relational match/unmatch atomicity and retry tests pass 2/2, HTTP authorization/isolation tests pass 1/1, and combined P3-06/import/settlement/foundation/accounting regressions pass 79/79. Full backend and Release build remain the final gates before this slice is accepted.
- **P3-06 close/adjustment/exception slice implemented (2026-09-06):** migration `20260906080824_AddP306ReconciliationCloseAdjustmentsExceptions` adds close/reopen metadata, Bank Fee Expense and Bank Interest Income configuration mappings, `BankStatementAdjustment`, and `BankStatementException`. Readiness requires statement opening/closing evidence, zero unmatched statement/GL lines, zero open chargeback/failed-settlement exceptions, and exact zero reconciliation difference. Close requires ready-for-close state and Finance-authorized close permission; it creates no balancing JournalEntry. Closed sessions reject match/unmatch; reopen requires a reason and preserves close history.
- **Adjustments:** Bank Fee posts `Dr Bank Fee Expense / Cr Bank`; Bank Interest posts `Dr Bank / Cr Bank Interest Income`. Both require a linked immutable statement line, same Business/BankAccount, configured account type/ownership, proposal then approval, deterministic idempotency, Journal Engine period governance, and linked reversal. Adjustment operational rows classify/link the Journal Engine entry and are never added separately to GL calculations, preventing double counting.
- **Exceptions and evidence:** chargeback and failed-settlement classifications are stored against immutable statement evidence without automatic accounting. Open exceptions prevent readiness; resolution is audited. Dedicated close/adjustment tests pass 5/5; full backend and Release build remain the final gates.
- **Close/adjustment hardening completed (2026-09-06):** SQLite fault injection with fresh-context verification proves Bank Fee and Bank Interest approval/posting rollback after Journal Engine persistence begins, exact retry/idempotency, and linked reversal rollback/retry. Open/closed-period posting and later-period reversal governance use Journal Engine validation. Close revalidates unmatched lines, open exceptions, and difference inside its transaction; closed-session match/unmatch is rejected and reopen preserves close history. Close/adjustment HTTP permission and tenant checks pass 2/2. Full focused P3-06 plus accounting regression evidence is green; full backend and Release build are the final gates.
- **Final close/adjustment acceptance evidence completed (2026-09-06):** Explicit fee and interest approved-Journal fixtures prove Bank GL movement/closing derives from JournalEntryLines exactly once and operational adjustment rows do not double count. Authorized HTTP workflow evidence covers close, reopen, adjustment proposal/approval/reversal, exception classification/resolution, and cross-Business action rejection. Adjustment approval/reversal transactions, deterministic Business/idempotency keys, unique indexes, repeated-action guards, and fresh-context rollback/retry evidence provide accepted duplicate-effect protection without synthetic thread tests. Final close/adjustment hardening tests pass 11/11; full backend suite passes 454/454; Backend Release build passes. A duplicate legacy HTTP fixture was removed after isolated reproduction showed it used an invalid close-period setup; the dedicated authorized workflow fixture remains the authoritative evidence.
- Current liquidity mappings are chart-account/configuration mappings only: global `AccountingConfiguration.DefaultBankAccountId`, `CashAccountId`, and `PettyCashAccountId`, plus outlet-scoped `AccountingOutletConfiguration.OutletBankAccountId`, `OutletCashAccountId`, and `OutletPettyCashAccountId`. There is no bank name, account number/reference, opening statement balance, active bank-account entity, or multi-bank operational registry.
- POS posting remains cutover-aware: cash always resolves to Cash and bank always resolves to Bank; card resolves to Bank before the cutover and Card Clearing on/after it; mobile and mfs resolve to Bank before the cutover and Mobile/MFS Clearing on/after it. Each sale payment snapshots the resolved ChartAccount, so refund/void reversal lines use the original account even after configuration changes. Legacy rows without a snapshot retain the historical Cash/Bank fallback.
- Supplier Payments, Expenses, and Other Income use their selected payment/receipt ChartAccount directly. GRN immediate settlement uses configured Bank directly when applicable. Refunds/voids and Journal Engine corrections use linked reversal journals with actual posting dates. No bank-specific settlement reversal workflow exists.
- Cash Sessions are outlet/terminal operational controls: they record opening/closing cash, expected/actual closing amount, variance, cash/card/mobile summaries, and legacy cash movement references. They do not create bank deposits, bank withdrawals, settlement batches, bank statement evidence, or Bank/Clearing reconciliation records.
- P2-07 Cash Book is read-only and journal-backed. It derives opening, inflow, outflow, running, closing, and GL reconciliation per configured Cash/Petty Cash/Bank account, respects outlet overrides and location filters, and exposes differences. It is not a bank statement reconciliation feature.
- No existing bank statement import, CSV/Excel parser, imported-file checksum, statement transaction entity, statement-line match, reconciliation session, settlement batch, bank fee/interest settlement, chargeback, failed settlement, cash deposit-to-bank, bank withdrawal, bank-to-bank transfer, cash-to-bank transfer, or bank-to-cash transfer workflow was found.
- Existing JournalEntry/JournalEntryLine data is sufficient for GL-side activity and matching candidates: amount, posting date, journal number, source/reference, payment-method line metadata, outlet/warehouse, supplier/customer dimensions, cash session, and linked reversals. It is not sufficient to prove external bank settlement because no external statement evidence is stored.
- Minimum justified future operational model: `BankAccount` (business-owned linked ChartAccount, optional outlet scope, active state), immutable `BankStatementImport` with source checksum/idempotency, immutable `BankStatementLine`, `BankReconciliationSession`, auditable `BankReconciliationMatch`/match groups, and settlement-batch references only if the approved processor settlement workflow requires them. Adjustment proposals/approvals should reference statement lines and post through Journal Engine; they must not store duplicate balances or mutate posted journals.
- Reconciliation should independently compare imported statement opening/closing and statement movement evidence against posted Bank/Clearing GL activity for the same account/cutoff. Matched, unmatched GL, unmatched statement, timing differences, approved adjustments, and reconciliation differences must remain visible. Historical bank periods without imported evidence require an explicit unreconciled opening position, not fabricated matches.
- Tenant/security readiness is partial: global configuration is Business-scoped and outlet overrides are Business/Outlet-scoped with account validation, while JournalEntry carries BusinessId and location dimensions. However, there is no BankAccount ownership model or account-level authorization scope for multiple bank accounts. That scope must be defined before exposing bank reconciliation.
- Reuse existing infrastructure: Journal Engine balance/period/idempotency/reversal services, AccountingConfiguration account resolution and outlet overrides, Cash Book, CashSession, source/reference metadata, financial periods, and existing transactional patterns in SupplierPayment/OtherIncome/GRN.
- Mandatory technical prerequisites: approve bank/clearing settlement policy; add the minimum bank/statement/match operational entities; add account-level tenant and authorized-location scoping; implement immutable import checksum/idempotency; define atomic import/match/adjustment transitions; preserve Journal Engine as GL source; and add server-side permissions for import, match, reconcile, adjustment approval, and reporting.
- Required business decisions: number and ownership of bank accounts; whether card/mobile/MFS move from direct Bank posting to clearing; processor settlement timing/files; statement formats; matching tolerance and partial-match policy; settlement fees; chargebacks/failed settlements; bank fees/interest treatment; adjustment approval; reconciliation approval; and historical cutover/opening statement balance policy.
- Recommended defaults for later approval only: separate business-owned BankAccount records linked one-to-one to ChartAccounts; explicit Card and Mobile/MFS clearing accounts if external settlement is delayed; exact-amount/date/reference matching first with controlled tolerance; statement imports immutable by checksum; partial matches represented as match groups; fees/interest as approved Journal Engine adjustments linked to statement lines; and a validated reconciliation start date with opening balances rather than backfilled historical matches.

#### Business objective
- Reconcile configured bank and clearing balances to external bank statements and settle card/mobile/MFS payment flows without altering historical source postings.

#### Existing functionality to reuse
- Cash Book, configured Cash/Petty Cash/Bank mappings and outlet overrides, journal line payment-method metadata, source references, period governance, and reversal service.

#### In scope
- Bank and clearing-account model, card/mobile/MFS settlement lifecycle, bank statement import, matching/reconciliation, unmatched items, bank fees/interest, reconciliation adjustments, multi-bank-account requirements, and Bank/Clearing to GL reconciliation.

#### Out of scope
- Direct bank API initiation, payment gateway processing, treasury forecasting, foreign exchange, and arbitrary payment processor integrations without a confirmed provider.

#### Accounting and journal rules
- Sale/card/mobile/MFS receipts should post to an approved clearing or bank account according to settlement policy; settlement transfers clearing to bank without creating revenue.
- Bank fees and interest post through configured expense/income accounts with statement source linkage.
- Reconciliation adjustments require approval and a linked journal; they cannot mutate source journal lines or statement data.

#### Source of truth
- Posted Journal Engine for GL balances; imported bank-statement and settlement records only for matching status and evidence.

#### Required entities/schema changes
- Additive bank account, clearing-account, statement import, statement line, reconciliation session, match, settlement batch, and adjustment reference entities.
- Bank accounts must map to a chart account and business; statement records must be immutable after import/audited correction.

#### Required configuration
- Multiple bank accounts, permitted payment-method-to-clearing/bank mapping, fee/interest accounts, matching tolerances, statement import formats, and reconciliation approval policy.

#### API and Angular/UI requirements
- Bank-account setup, statement import, reconciliation workbench, match/unmatch, settlement batches, exceptions, adjustment proposal/approval, reconciliation report, and evidence download.

#### Permissions and isolation
- Separate bank statement import, match, reconcile, approve adjustment, and report permissions. Tenant and authorized location/account scope are mandatory.

#### Idempotency, atomicity, reversal/correction
- Imports require source-file checksum/idempotency protection; settlement and adjustment posts require deterministic source identifiers.
- Statement import/match state and any required journal must be atomic at their approved transition.
- Corrections reverse/reopen match state with auditable linkage; no destructive edit of posted reconciliations.

#### Reconciliation requirements
- Reconciled bank/clearing statement closing balance equals GL balance after approved timing differences and adjustments are separately shown.
- Unmatched items are explicit exceptions, not hidden reconciliation adjustments.

#### Legacy-data/coexistence considerations
- Historical bank records without imported statement evidence remain unreconciled opening items unless a validated opening reconciliation is loaded.

#### Required automated tests
- Multi-bank mappings, clearing-to-bank settlement, duplicate import, matching, partial match, unmatched exceptions, fee/interest, adjustment approval/reversal, date cutoff, reconciliation equality, tenant/account/location isolation, and permissions.

#### Acceptance criteria
- Statement balances, clearing balances, and GL are reconcilable with every timing difference and exception visible.

#### Dependencies
- P3-01; coordination with P3-05 for tax-related settlement requirements; P3-07 consumes reconciled balances as a close gate.

#### Decision Required
- Number and ownership of bank accounts, clearing account policy, payment processor settlement timelines/files, import formats, matching tolerance, adjustment approval, and treatment of chargebacks/failed settlements.

---

### P3-07 - Period-End & Year-End Closing

#### Business objective
- Turn existing period locks into a controlled close process that refuses to complete while material reconciliation controls are unresolved.

#### Existing functionality to reuse
- Financial-year/period lifecycle, Journal Engine period validation, opening-balance adjustments, P3-01 statements, P3-02 AP, P3-04 AR, P3-05 VAT, P3-06 bank reconciliation, and inventory valuation.

#### In scope
- Period-close checklist, reconciliation gates, closing/adjustment journals, retained-earnings transfer, year-end carry-forward, controlled reopen/correction, and complete audit trail.
- Period close is manual and Finance-controlled: `Open -> Locked -> reviewed/approved -> Closed`. No automatic calendar or date-driven close is in scope.
- Fiscal-year close is permitted only after the final accounting period completes the approved period-close requirements.

#### Out of scope
- Consolidation, statutory filing submission, budget approval, and automated external auditor workflows.

#### Accounting and journal rules
- Core reconciliation gates use exact zero tolerance. No silent materiality thresholds are introduced in this phase.
- Trial Balance imbalance, Balance Sheet control failure, AP control reconciliation difference, VAT-to-GL difference, Inventory-to-GL difference, and incomplete required Bank reconciliation block close.
- Historical/cutover limitations may proceed only through an explicit Finance-approved close exception with reason and immutable audit evidence. Draft or unapproved operational transactions are warnings unless they create a reconciliation or accounting-integrity failure.
- Closing and year-end transfers use idempotent system journals with source period/year references.
- Year-end uses Option A: one balanced, system-generated `YearEndClosing` journal for the Financial Year directly closes each Income, COGS, and Expense account to configured Retained Earnings. Do not introduce an Income Summary account.
- Owner Drawings is separately closed to configured Owner Capital at year-end. Do not merge Owner Capital and Retained Earnings, and do not include Owner Drawings in the Income/COGS/Expense-to-Retained-Earnings transfer.
- `YearEndClosing` lines remain real posted activity visible in Trial Balance and General Ledger, but are excluded from historical Profit and Loss and dynamic current/prior-year earnings calculations.
- Normal year-to-year Balance Sheet carry-forward derives from the continuous posted ledger. Do not create duplicate opening-balance journals for AP, VAT, Inventory, Cash, Bank, Clearing, Capital, or Retained Earnings merely because a new Financial Year begins. Initial system opening balances remain a separate cutover mechanism.
- Later-open-period adjustment is the default correction method. Period reopen requires authority, a reason, and immutable audit evidence. Financial Year reopen is exceptional, requires dedicated Finance authority and a reason, and is prohibited while its effective `YearEndClosing` journal remains unreversed; reopening therefore requires a linked Journal Engine reversal. Original closing journals and close evidence remain immutable, and a correction is followed by a new audited close run and effective year-end journal.

#### Source of truth
- Posted Journal Engine, reconciliation results from prior milestones, and immutable close-control/audit records.

#### Required entities/schema changes
- Additive period-close checklist, reconciliation snapshot, exception approval, and close-run records keyed to financial period/year. Do not duplicate GL balances as a second ledger.

#### Required configuration
- Required reconciliation gates, Finance approver roles, configured Retained Earnings and Owner Capital accounts, and reopen authority. Core gates use zero tolerance; exception approval is explicit rather than threshold-driven.

#### API and Angular/UI requirements
- Close dashboard/checklist, exception drill-down, preparation, approval, close, controlled reopen, audit timeline, and close package export.

#### Permissions and isolation
- Separate permissions for close preparation, approval, execution, exception override, period reopen, and Financial Year reopen. Different-person segregation is not mandatory; a suitably authorized Finance user may hold multiple permissions. All actions are tenant-scoped; outlet/location sub-reconciliations roll up only under approved business-close policy.

#### Idempotency, atomicity, reversal/correction
- One close run per period/year and deterministic close-journal references.
- Period status update and close-control records must be atomic. Financial Year state, effective year-end JournalEntry and JournalEntryLines, retained-earnings transfer linkage, and close-control records must be atomic.
- Reopen/corrections create auditable linked adjustments or reversals and do not edit posted journal history.

#### Reconciliation requirements
- AP to GL, AR to GL, VAT to GL, Bank/Clearing to GL, Inventory to GL, Trial Balance equality, and Balance Sheet equality are explicit gates.
- Reconciliation exceptions require approved disposition before close, according to configured thresholds.

#### Legacy-data/coexistence considerations
- Legacy unreconciled opening items must be visible as carry-forward exceptions. A period cannot be represented as fully reconciled if such exceptions remain unresolved.

#### Required automated tests
- All gate success, each gate failure, threshold/override behavior, idempotent close, rollback on journal failure, year-end transfer, carry-forward, reopen/correction, audit trail, tenant isolation, and permissions.

#### Acceptance criteria
- No financial period closes silently with failed required control-account reconciliation.
- Closing and year-end entries are balanced, traceable, idempotent, and correctly reflected in financial statements.

#### Dependencies
- P3-01, P3-02, P3-04 where AR is approved, P3-05, and P3-06.

#### Approved implementation decisions (2026-09-06)
- P3-04 AR is not a close gate while AR remains deferred/not required under the approved current business model.
- The year-end transfer, Owner Drawings, gate/materiality, close calendar, role separation, carry-forward, reporting, and post-close correction policies above are approved and frozen for P3-07 implementation planning.

#### Slice 1 implementation status (2026-09-06)
- Implemented: tenant-scoped, versioned `AccountingPeriodCloseRun` records with append-only gate snapshots and Finance-approved historical/cutover exception approvals; dedicated prepare, re-evaluate, approve, execute, read/history, and controlled reopen APIs; separate close permissions; and an atomic normal-period close transition from Locked to Closed.
- Implemented gates use exact zero tolerance for configuration, Trial Balance, Balance Sheet, Supplier Payable, Supplier Advance, VAT, Inventory, posted-ledger, relevant Bank reconciliation, unresolved Bank exceptions, and proposed Bank adjustments. Draft manual journals, unapproved Expenses/Other Income, incomplete VAT snapshots, and historical unvalued inventory evidence remain warnings under the frozen policy.
- Historical/cutover exception approval is structurally limited to current blocking snapshots explicitly marked as historical/cutover limitations; it cannot override current accounting-integrity failures. Repeat preparation returns the active run, while reopen preserves the completed run and enables a later distinct run.
- Slice 1 intentionally does not close Financial Years, create `YearEndClosing` journals, change Retained Earnings/Owner Drawings accounting, change P&L reporting, or add the Angular close workbench/close-package export. Normal journal posting remains prohibited in Locked and Closed periods.

#### Slice 2 implementation status (2026-09-06)
- Implemented: tenant-scoped, versioned `AccountingFinancialYearCloseRun` records linked to the approved Slice 1 final-period close run; dedicated prepare, approve, execute, history, and controlled reopen APIs; journal/reversal linkage; and a filtered effective-run constraint.
- Implemented: a single system-generated `YearEndClosing` journal at Financial Year end using `FinancialYearClose` source identity. It closes nonzero Income, COGS, and Expense account balances directly to configured Retained Earnings and separately transfers Owner Drawings to configured Owner Capital. No Income Summary or annual opening-balance journal is created.
- Implemented: a narrowly authorized Journal Engine path permits only the approved `YearEndClosing` journal to post in the Locked final period. All ordinary Locked/Closed-period posting remains rejected. Non-final periods must already have executed Slice 1 runs; the final period must have an approved run and is closed atomically with the Financial Year and journal linkage.
- Implemented: historical P&L, COGS operating totals, and dynamic current/prior earnings exclude the `FinancialYearClose` reference family, including linked reversal journals. Trial Balance and General Ledger retain all posted close/reversal activity. Financial Year reopen requires a reason and a linked Journal Engine reversal of the effective closing journal; re-close retains historical evidence and produces a new run/journal.
- Slice 2 intentionally does not add the Slice 3 Angular close workbench or close-package export, and does not create the final P3-07 acceptance checklist.

#### Slice 3 implementation status (2026-09-06)
- Implemented: the permission-guarded Angular Close Workbench for authoritative Accounting Period and Financial Year close-run history, gate snapshots, blocking/warning distinction, eligible historical/cutover exception approval, prepare/re-evaluate/approve/execute/reopen actions, closing/reversal journal references, and authoritative refresh after every action.
- Implemented: evidence-only JSON close-package downloads for period and Financial Year close runs. Packages serialize existing tenant-scoped close-run/gate/exception/journal evidence; they do not calculate balances, post journals, or create a parallel ledger.
- Implemented: close-gate responses include immutable exception approver, timestamp, and reason where available. The Angular route/menu requires close-history access; write controls remain individually permission-gated.
- Slice 3 completes the bounded P3-07 implementation. No statutory filing, auditor portal, automated accrual/depreciation, budgeting, additional AR workflow, annual opening journals, or other non-frozen scope was added.

#### Bounded implementation plan (2026-09-06)

**Slice 1 - Close-run foundation and controlled period close**

- **Scope:** Add one immutable close-control aggregate for either an Accounting Period or Financial Year, then deliver the manual period workflow: prepare while Open, lock, evaluate exact-zero control gates, record warning/gate snapshots, approve documented historical/cutover exceptions, approve, execute, and audit the transition to Closed. This slice does not post a year-end journal or close a Financial Year.
- **Entities and constraints:** Add `AccountingCloseRun` with `BusinessId`, target type/id, monotonic run number, lifecycle status, prepared/approved/executed/reopened actor/timestamp/reason fields, and optional close-journal linkage reserved for Slice 2. Add immutable `AccountingCloseGateSnapshot` rows with gate code, evaluated scope/date, expected/actual values, status, and evidence/reference metadata. Add immutable `AccountingCloseExceptionApproval` rows linked to the failed snapshot with Finance approver, reason, and approved disposition. Use a unique `(BusinessId, TargetType, TargetId, RunNumber)` key and a filtered unique key for one active/effective run per target; retain superseded/reopened runs for history.
- **Service responsibilities:** Add an `AccountingCloseService` as the sole orchestration boundary. It calculates Trial Balance and Balance Sheet controls through `AccountingReportService`, AP control and advance reconciliation through `ApReportService`, VAT reconciliation through `VatReportService`, Inventory-to-GL through `AccountingReportService`, and required Bank reconciliation coverage/status through `BankReconciliationService`/tenant-scoped queries. Existing services remain authoritative for their own calculations; close snapshots persist returned evidence rather than maintaining copied balances. `FinancialPeriodService` continues to own status transitions but is invoked by the close service only after revalidation.
- **Gate behavior:** Core Trial Balance, Balance Sheet, AP, VAT, Inventory, and required Bank reconciliation failures block execution at zero tolerance. AR is excluded while P3-04 remains deferred/not required. Operational draft/submitted/unapproved work is returned as warnings only unless it produces a core integrity failure. Historical/cutover limitations are blocking until a Finance exception approval is recorded against the relevant immutable snapshot.
- **APIs and permissions:** Add tenant-scoped close preparation/read/detail endpoints plus `prepare`, `lock`, `approve-exception`, `approve`, `execute-period-close`, and `reopen-period` actions. Add `accounting.close.prepare`, `accounting.close.approve`, `accounting.close.execute`, `accounting.close.override_exception`, and `accounting.close.audit.view`; preserve `accounting.periods.reopen` only as a compatibility alias or replace its action path with the close orchestrator. A caller may hold multiple permissions; different-person segregation is not enforced.
- **Transactions and idempotency:** Preparation may be repeatable and creates a new draft run only when prior data changed; approval/execution re-evaluate gates inside a `ReadCommitted` transaction before mutating status. Period status, final snapshots, execution metadata, and audit record commit atomically. Repeat execution of the same effective run returns its recorded result. Every query and mutation derives `BusinessId` from tenant context; account, outlet, and warehouse evidence is constrained to that business and the approved business-close scope.
- **Angular and tests:** Add typed close-run/gate/exception models and an accounting close service; no Fiscal Year closing UI in this slice. Add a period close dashboard state showing blocking gates, warnings, exception approval, preparation/approval/execution state, and immutable audit history. Test pass/fail gates, zero tolerance, explicit cutover exception approval, warnings, stale-state revalidation, retries, rollback, tenant isolation, permission enforcement, and immutable history.
- **Completion boundary:** A Finance user can close a normal period only through an auditable, approved, zero-tolerance gate run; no period close can silently bypass the run. Financial Year state and year-end journals remain unchanged.

**Slice 2 - Fiscal Year close, year-end journals, reporting, and controlled reversal**

- **Scope:** Build on Slice 1 to close a Financial Year only after its final period has an effective approved close run. Create one balanced, system-generated `YearEndClosing` journal per effective Financial Year run: direct account-by-account closure of Income, COGS, and Expense accounts to configured Retained Earnings, plus a separate Owner Drawings-to-configured-Owner-Capital closure. Do not create an Income Summary account or annual opening-balance journals.
- **Services and integration:** Extend `AccountingCloseService` to load active Business-owned chart accounts and resolve Retained Earnings/Owner Capital through `AccountingConfigurationService`. Build deterministic lines only for nonzero temporary-account balances within the target Financial Year and exclude prior `YearEndClosing` journals. Create the journal through `JournalEntryService.CreateOrGetSystemDraftAsync` using `JournalType = YearEndClosing`, `ReferenceType = FinancialYearClose`, and the Financial Year ID as source reference, then post through the Journal Engine. Reuse `JournalEntryService.ReverseAsync` for the required linked year-end reversal before Fiscal Year reopen. `OpeningBalanceService` is not invoked because ledger carry-forward is continuous.
- **Required controlled-posting extension:** Current `JournalEntryService` rejects all Locked periods. The final-period workflow requires the close journal to be calculated after lock/review but dated in that final period. Implement a narrowly scoped close-orchestrator capability that permits only the approved system `YearEndClosing` journal for its effective close run to post during the final period's controlled close transaction. It must not create a general Locked-period posting bypass, must validate the target period/year and close-run linkage, and must leave ordinary postings rejected. This is an implementation dependency, not a change to the frozen close policy.
- **Entities and constraints:** Reuse `AccountingCloseRun` and its journal linkage. Record the linked reversal journal and reopen metadata on a new immutable reopen/correction history record or a dedicated immutable close-run event row. Enforce one effective close run and one effective `YearEndClosing` journal per Financial Year; historical reversed/superseded runs remain queryable. Reuse the Journal Engine's filtered unique system-journal key as the second idempotency guard.
- **APIs and permissions:** Add Financial Year prepare/approve/execute/read actions and a dedicated `accounting.financial_years.reopen` permission. Reopen requires a reason and the linked Journal Engine reversal; reject reopening while the effective year-end journal is still posted. A subsequent correction uses a later open period by default, and re-close creates a new run plus new effective closing journal after the previous one is reversed.
- **Transactions and idempotency:** In one outer transaction, lock/revalidate the final close run and gates, create/get and post the closing journal, link the journal to the run, close the Financial Year/final period, persist audit/event evidence, and commit. A failed journal/post/link/status mutation rolls back all close state. Retry resolves the deterministic journal and effective run rather than transferring earnings twice. Posted original/reversal journals remain immutable.
- **Reporting changes:** Keep `YearEndClosing` in Trial Balance and enhanced GL. Exclude it from `GetProfitLossAsync`, comparative P&L, COGS operating-result calculations where relevant, and dynamic current/prior-year earnings queries. Balance Sheet retains the posted Retained Earnings and Owner Capital movements while suppressing only the closed year's dynamic prior-year amount. Date/FY filters continue to use the continuous ledger; no carry-forward journal is generated.
- **Tests:** Cover profit and loss, loss, and zero-result years; individual Income/COGS/Expense closure; separate Owner Drawings closure; configured-account validation; journal balance/visibility/idempotency; final-period prerequisite; locked-period narrow exception denial for all non-close journals; rollback and retry; mandatory reversal before reopen; correction/re-close; tenant isolation; and P&L/Balance Sheet/GL/Trial Balance multi-year correctness.
- **Completion boundary:** A Financial Year cannot close without an effective final-period close run and one traceable closing journal. Its reopening cannot occur without linked reversal, and reports preserve both historical operating results and visible posted close activity.

**Slice 3 - Angular close workbench and close package**

- **Scope:** Deliver the Finance-facing close dashboard for Period and Financial Year runs: target selection, gate summary, warning and exception drill-down, prepare/lock/approve/execute/reopen controls, journal links, audit timeline, and close-package export. It consumes the Slice 1/2 APIs and never calculates balances, creates journals, or mutates evidence client-side.
- **Angular surface:** Add close-run, gate snapshot, exception approval, audit event, and package models to `accounting.model.ts`; add API methods to `accounting.service.ts`; add a standalone close workbench component, template, styles, route, and Finance menu entry. Reuse route permission guards and existing accounting workbench patterns. Render period and Fiscal Year flows as separate actions/states, hide controls without their dedicated permission, and make completed run evidence read-only.
- **Export:** The close package is a server-generated, read-only export of the immutable final run, gate snapshots, approved exceptions, linked journals, and audit/reopen history. It is evidence only and does not regenerate calculations or post corrections.
- **Tests and completion boundary:** Add component/service tests for loading, gate/warning rendering, permission-gated actions, exception approval, execution refresh, reopen/reversal status, immutable completed state, and package download. Complete when Finance can inspect and operate the accepted Slice 1/2 workflow end-to-end from the UI without bypassing server authority.

**Implementation order and dependencies:** Slice 1 is first and is independently deployable for normal period close. Slice 2 depends on Slice 1's effective final-period run, immutable snapshots, and orchestration transaction boundary. Slice 3 depends on the final API contracts from Slices 1 and 2; it may begin typed model/service scaffolding late in Slice 2 but must not substitute for server-side close controls.

**Expected implementation files:** Add close-run entities, DTOs, EF configuration/migration, `AccountingCloseService` and interface, close controller, permission/seed wiring, and focused P3-07 tests. Modify `RetailPOSDbContext`, `FinancialPeriodService`/interface/controller action delegation, `JournalEntryService`/interface for the narrowly scoped controlled-close posting path, `AccountingReportService` P&L/dynamic-earnings filtering, and permission aliases/role wiring. Add Angular accounting models/service methods, a close-workbench component, route/menu entry, and focused frontend tests. No existing P3-01 through P3-06 financial source-of-truth service is duplicated or redesigned.

**Known implementation dependency:** The locked-final-period sequencing rule above is the only genuine technical dependency identified. It must be implemented as a tightly authorized `YearEndClosing` path inside the close transaction; without it, the existing universal Locked-period rejection makes a correctly dated final-period closing journal impossible after the approved lock/review step. No unresolved business/accounting decision is required.

---

### P3-08 - Management Reporting & Accounting Hardening

#### Business objective
- Deliver management-level financial insight while resolving non-blocking technical debt identified at Phase 2 sign-off.

#### Existing functionality to reuse
- P3-01 financial statements, P3-02 AP, P3-04 AR where approved, P3-05 tax reporting, P3-06 reconciliation, P3-07 close controls, existing sales/inventory reports, and Phase 2 hardening inventory in [P2-08_PHASE_2_FINAL_ACCEPTANCE_CHECKLIST.md](Docs/10_Accounts_And_Finance/P2-08_PHASE_2_FINAL_ACCEPTANCE_CHECKLIST.md).

#### In scope
- Cash Flow Statement, comparative financial statements, outlet/product profitability where historical allocations support it, centralized rounding policy, strongly typed report contracts, retry-specific GRN/cash-session tests, uniform fault-injection/atomicity tests, and legacy `Account`/`Transaction`/`Bill` cleanup or explicit guarding.

#### Out of scope
- Budgeting/KPIs unless business requirements are approved, predictive analytics, BI warehouse, multi-currency consolidation, and arbitrary external analytics integrations.

#### Accounting and journal rules
- Cash Flow Statement derives from posted cash/bank/clearing journal activity and approved classification logic; it must reconcile to opening/closing liquidity balances.
- Outlet/product profitability must use posted revenue, COGS, and approved allocation sources. It must state excluded/unallocated costs instead of fabricating allocations.
- Centralized rounding policy governs thresholds, precision, account mapping, and report display consistently across posting services.

#### Source of truth
- Posted Journal Engine, accepted stock valuation data, and explicit allocation records. Never legacy account-balance caches or mutable product costs.

#### Required entities/schema changes
- Prefer DTO/service and test changes. Add allocation/configuration entities only if a management-reporting allocation method is approved.

#### Required configuration
- Cash-flow classification, rounding policy, outlet/product allocation policy, profitability dimensions, and any approved KPI/budget configuration.

#### API and Angular/UI requirements
- Cash-flow and comparative statement APIs/pages; profitability dashboards with explicit scope and exceptions; strongly typed client contracts; export and reconciliation views.

#### Permissions and isolation
- Dedicated management-report permissions; tenant/location isolation; masking policy for sensitive financial detail where approved.

#### Idempotency, atomicity, reversal/correction
- Reporting is read-only. Hardening tests must prove retries and posting failures do not leave partial operational/accounting state.
- Legacy guards must prevent accidental parallel financial posting.

#### Reconciliation requirements
- Cash Flow closing liquidity agrees with Cash Book/GL.
- Profitability revenue/COGS totals reconcile to selected P&L/COGS scope before allocations; allocation exceptions remain visible.

#### Legacy-data/coexistence considerations
- Remove dormant models only through an approved migration/decommissioning plan. Until then, explicitly guard them from creating Phase 3 financial records.

#### Required automated tests
- Cash-flow to Cash Book/GL reconciliation, comparative period logic, profitability reconciliation/exclusions, rounding policy, typed API contracts, GRN/cash-session retries, systematic rollback failure injection, legacy-route guarding, tenant/location isolation, and permissions.

#### Acceptance criteria
- Management reports reconcile to the underlying statements/GL and disclose incomplete allocation or legacy limits.
- Phase 2 technical debt is either resolved or protected by explicit runtime/test controls.

#### Dependencies
- P3-01 and P3-07; individual reporting components also depend on their respective subledger/reconciliation milestones.

#### Decision Required
- Resolved 2026-09-06: see "Approved P3-08 decisions (2026-09-06)" below. No unresolved business decision remains from this line.

#### Readiness review (2026-09-06)

**Confirmed intended scope:** P3-08 is limited to Cash Flow reporting, comparative statements, outlet/product profitability only where authoritative dimensions or approved allocations support it, centralized monetary-rounding policy, typed report contracts, focused GRN/cash-session retry/atomicity evidence, and legacy `Account`/`Transaction`/`Bill` cleanup or explicit guards. P3-08 is not a budgeting, forecasting, statutory filing, consolidation, AR, provider-integration, or arbitrary historical-reconstruction milestone.

**Reusable foundation:** `AccountingReportService` already provides posted-journal Trial Balance, P&L, Balance Sheet, enhanced GL, COGS, inventory valuation, and Cash Book rows with source/reference, counterparty, payment method, outlet, warehouse, customer, supplier, and cost-centre metadata. `SettlementTransferService`, `BankReconciliationService`, `SupplierPaymentService`, `ExpenseService`, `OtherIncomeService`, `SaleService`, `GrnService`, and `CashSessionService` retain authoritative operational links to Journal Engine activity. `AccountingConfigurationService`, `JournalEntryService`, and P3-07 close records provide configuration, period governance, audit, idempotency, and immutable reporting boundaries.

**Cash Flow readiness and decision:** The current Cash Book derives Cash/Petty Cash/Bank journal rows, balances, and counterpart accounts but does not include Clearing slots or Operating/Investing/Financing classification metadata. The documented model does not select Direct or Indirect Method. Approval is required for: (1) Direct versus Indirect Method; (2) the cash-and-cash-equivalent scope, including treatment of Card/Mobile/MFS Clearing; and (3) deterministic classification rules for source/reference, counterpart account, and exceptional manual journals. Recommended minimum: a Direct Method statement built from posted liquidity lines, with Cash/Petty Cash/Bank as the reconciled cash-equivalent scope, Clearing used as settlement evidence rather than a second cash-flow total, Cash-to-Bank/Bank-to-Cash/Bank-to-Bank transfers eliminated as internal liquidity movements, and `YearEndClosing` excluded as non-cash. This provides a reconciled operational statement without inventing a broad account-classification engine.

**Profitability readiness and decision:** Business and outlet dimensions are available on posted journal headers/lines; direct revenue/COGS can therefore be reported only where the source lines carry that scope. Journal lines do not carry product or category dimensions, and shared expenses do not have an approved allocation basis. Approval is required for outlet allocation policy, product/category scope, shared-expense treatment, historical dimension policy, and whether the first release is contribution margin only or full allocated profitability. Recommended minimum: Business and outlet profitability from directly attributable posted revenue/COGS/expenses; product/category contribution margin only from reliable sales and historical-cost evidence; show shared or dimensionless expenses as `Unallocated` rather than guessing an allocation. Add allocation configuration/entities only after that policy is approved.

**KPI/budget decision:** Actual-only accounting indicators and comparative/trend metrics can be derived from current posted-report data. There is no authoritative budget or target model. Approval is required whether P3-08 includes actual-only KPIs, trend comparisons, or no KPI surface. Budget-versus-actual or management targets require a separate approved budget model and are out of scope until authorized. Recommended minimum: actual-only summary metrics and existing comparative statements, with no budget subsystem.

**Rounding hardening:** Journal amounts are guarded at two decimal places by `JournalEntryService`; valuation replay retains four-decimal unit cost; reports round presentation percentages separately. Sale and GRN posting each independently calculate two-decimal amounts and a `0.005` rounding-difference threshold, while VAT/purchase/pricing use local `Math.Round` calls without a single documented midpoint rule. P3-08 requires a shared monetary-rounding policy and helper boundary that preserves accepted historical postings, standardizes precision/midpoint behavior for new calculations, resolves the configured Rounding Difference account, and proves reconciliation is not silently altered.

**Retry/atomicity hardening:** The concrete documented gaps are retry-specific duplicate-submission regression coverage for GRN completion and Cash Session close, plus focused relational failure-injection evidence where a financially relevant source/journal state boundary lacks it. Existing accepted transaction, unique-anchor, and rollback/retry evidence remains sufficient for all other workflows unless a specific defect is found. P3-08 does not require an unlimited fault-injection matrix.

**Legacy guarding:** Legacy `Account`, `Transaction`, and `Bill` entities, repositories, services, controllers, and navigation remain active. `TransactionService.CreateAsync` updates a mutable legacy account balance outside the Journal Engine, and `BillService` creates/updates status-only records without Business scope or journal linkage. This can create an ambiguous second financial record and is a genuine P3-08 hardening requirement. Approval is required for decommissioning timing. Recommended minimum: immediately guard legacy write endpoints/services from creating financial records, remove them from active Finance navigation, label permitted legacy reads non-authoritative, retain historical records read-only, and defer physical table removal to an approved migration/decommissioning plan.

**Sensitive-data masking decision:** Current accounting report permissions are report-level and generally expose full financial values. P3-08 reports would expose cash, margin, supplier, and journal detail. Approval is required for whether field-level masking is needed, which roles receive full versus masked values, and whether exports follow the same rule. No masking feature should be implemented without this policy. Recommended minimum: retain existing Finance-only report permissions initially; add field/export masking only if Finance approves a role/value disclosure matrix.

**Dependencies before implementation planning:** Freeze the five decisions above: cash-flow method/classification, profitability/allocation scope, actual-only KPI/budget scope, legacy decommissioning timing, and financial-data masking policy. Also approve the rounding midpoint/threshold policy needed to consolidate existing duplicated behavior. There is no mandatory dependency on deferred P3-03/P3-04 or on optional statutory/consolidation features.

**Preliminary controlled slices:**
1. Cash Flow and comparative-report contracts: approved classification configuration, posted-journal Direct/Indirect calculations, internal transfer elimination, Cash Book/GL reconciliation, typed APIs/UI/export, and tenant/location/period tests.
2. Bounded profitability and rounding: direct Business/outlet and approved contribution/allocation presentation with `Unallocated` disclosure, plus centralized rounding policy/helper and compatibility/reconciliation tests.
3. Hardening and legacy controls: targeted GRN/Cash Session retry/atomicity evidence, legacy write guards/read-only labeling/navigation removal, and only approved financial-data masking.

#### Approved P3-08 decisions (2026-09-06)

The six decisions raised by the readiness review above are approved and frozen. The readiness findings remain unchanged as supporting evidence; the items below are the binding policy for implementation planning.

1. **Cash Flow Statement - Direct Method approved.** Cash Flow reporting is derived from authoritative posted Journal Engine activity involving configured Cash, Petty Cash, and Bank accounts, classified deterministically into Operating, Investing, and Financing using authoritative account/source/reference/counterpart metadata (not frontend inference). Cash-to-Bank, Bank-to-Cash, and Bank-to-Bank internal liquidity movements are eliminated from total external cash inflow/outflow. Card Clearing and Mobile/MFS Clearing are settlement/intermediate evidence and must not double-count cash flow when settlement reaches Bank. `YearEndClosing` and other non-cash journal activity are excluded. Opening/cutover balances are not current-period operating cash flows. Cash Flow must reconcile to the authoritative movement in configured Cash/Petty Cash/Bank accounts for the period, subject to documented cutover limitations. No Indirect Method adjustment model is introduced in P3-08.
2. **Profitability - authoritative direct attribution only, approved.** P3-08 may provide Business profitability, Outlet profitability where posted activity is directly attributable to an Outlet, and Product/category contribution information only where reliable sale/cost evidence already supports that dimension. Shared expenses or activity without a trustworthy approved dimension are presented explicitly as `Unallocated`; shared costs are never spread across outlets/products merely to force totals to allocate. Product/category reporting must not imply fully allocated net profitability where the Journal Engine lacks authoritative product/category expense dimensions. Any future shared-cost allocation methodology requires a separate approved business policy.
3. **KPI/Budget scope - actual-only KPIs and comparative trends, approved.** P3-08 provides management comparisons derived only from authoritative actual posted accounting data. Budget vs Actual is not implemented in P3-08; no authoritative budget/target model exists, and a budgeting subsystem requires separate future business approval and design.
4. **Monetary rounding policy - consolidation approved.** New accounting calculations use a shared monetary rounding policy: 2-decimal journal currency precision, explicit `AwayFromZero` midpoint rounding as the approved rule, use of the configured Rounding Difference account where an accepted posting workflow legitimately requires a rounding-difference line, and elimination of duplicated local rounding-difference logic where safely possible. Historical posted journals are preserved exactly as posted; there is no retrospective recalculation or rewrite. If implementation planning discovers that an already accepted posting workflow genuinely depends on a different explicit midpoint rule for compatibility, that conflict must be documented before changing accepted behavior rather than silently rewritten.
5. **Legacy finance models - staged guarding/decommissioning, approved.** Legacy `Account`, `Transaction`, `Bill`, and equivalent legacy finance records must not remain capable of creating a second authoritative financial record outside the Journal Engine. P3-08 blocks new legacy financial writes that bypass the Journal Engine, removes legacy finance functions from active navigation/workflows where applicable, treats retained historical legacy data as read-only/non-authoritative, and preserves historical evidence. Physical deletion of legacy finance tables/data is not performed in P3-08 and requires a separately approved future migration. The Journal Engine remains the sole financial source of truth.
6. **Sensitive financial data - existing Finance/report-level permissions approved.** P3-08 reports/exports continue to use existing authorization and tenant isolation. No field-level or value masking is introduced without an approved role/value disclosure matrix. Absence of field-level masking is not a P3-08 acceptance gap under this policy; a future differentiated disclosure policy requires separate business approval.

#### Bounded implementation plan (2026-09-06)

Three controlled slices. Each reuses `AccountingReportService`, `AccountingConfigurationService`, `JournalEntryService`, and existing Angular accounting report conventions rather than creating a second reporting framework. No slice introduces a parallel ledger; all figures are projections over posted `JournalEntry`/`JournalEntryLine` data or, for product/category contribution, over the same source-transaction join pattern already used by `GetCogsAsync`.

**Slice 1 - Cash Flow and comparative reporting**

- **Objective:** a journal-backed Direct Method Cash Flow Statement that reconciles to configured Cash/Petty Cash/Bank movement, with actual-only comparative periods.
- **Scope:** Operating/Investing/Financing classification, internal-transfer elimination, Clearing/settlement non-double-counting, non-cash `YearEndClosing`/opening-balance exclusion, and comparative (prior period/prior year/date-range) presentation. No Indirect Method.
- **Reusable implementation:** `AccountingReportService.GetCashBookAsync` already resolves Cash/Petty Cash/Bank via `AccountingConfigurationService.ResolvePostingAccountAsync`, returns opening/inflow/outflow/closing/GL-reconciliation per account, and exposes `JournalType`, `ReferenceType`, `ReferenceId`, and counterpart-account names per row. This is the direct foundation for Cash Flow; Cash Flow is a classified regrouping of the same rows, not a new calculation engine.
- **Classification design:** classify deterministically by `JournalType`/`ReferenceType` using existing constants (`AccountingJournalTypes`, `AccountingReferenceTypes`, `LiquidityTransferTypes`, `SettlementBatchStatuses`/settlement reference type, sale/refund/void reference types, `SupplierPayment`, `Expense`, `OtherIncome`, Owner Capital/Drawings account slot, Bank Fee/Bank Interest adjustment types). This is a code-level mapping table, not an account-name heuristic. `LiquidityTransfer`-sourced Cash-to-Bank/Bank-to-Cash/Bank-to-Bank journals are classified `Internal` and excluded from external totals (shown only as a transparency memo). `YearEndClosing` and `OpeningBalance`/`OpeningBalanceAdjustment` are classified `NonCash`/`Cutover` and excluded from period cash flow. Any Cash/Petty Cash/Bank-touching line whose `JournalType`/`ReferenceType` is not in the mapping (expected only for rare manual journals) is placed in an explicit `Unclassified` bucket rather than guessed - mirroring the frozen `Unallocated` discipline for profitability.
- **Card/MFS non-double-counting:** Card/Mobile/MFS Clearing accounts are never part of the Cash/Petty Cash/Bank universe, so a sale's Clearing debit never appears in Cash Flow. The only Cash Flow event is the later `SettlementBatch` transfer's Bank-side line, classified `Operating` (customer collection), which cannot coincide with the original sale's Clearing entry because they post to different accounts.
- **Backend changes:** add `CashFlowReportFilterDto`, `CashFlowSectionDto`, `CashFlowRowDto`, `CashFlowReportDto`, `ComparativeCashFlowReportDto` to `AccountingReportDtos.cs`; add `GetCashFlowAsync`/`GetComparativeCashFlowAsync` to `IAccountingReportService`/`AccountingReportService`, built from the same journal query pattern as `GetCashBookAsync` plus the classification mapping.
- **Data model/migration:** **none required.** Classification is a pure backend projection over existing `JournalType`/`ReferenceType`/account-slot metadata; no new table or column is needed for the documented sources. If real-world usage later shows a materially large `Unclassified` bucket from manual journals, an optional nullable classification override column may be considered as a separate future fast-follow - not part of this slice.
- **APIs:** `GET /api/accounting/reports/cash-flow` and `GET /api/accounting/reports/cash-flow/comparative` under the existing `AccountingReportsController`, dedicated `accounting.reports.cash_flow` permission (new, narrowly scoped, following the existing `accounting.reports.*` naming convention).
- **Angular:** add `CashFlowReport`/`ComparativeCashFlowReport` models to `accounting.model.ts`; add `getCashFlow`/`getComparativeCashFlow` to `accounting.service.ts`; add a `cash-flow-report` standalone component (filters, category subtotals, `Unclassified`/`Internal` transparency rows, reconciliation-to-Cash-Book line, comparative toggle, export) following the existing `balance-sheet-report.component.ts` pattern; add route + Finance/Reporting menu entry guarded by `accounting.reports.cash_flow`.
- **Permissions/tenant isolation:** new `accounting.reports.cash_flow` permission wired through `PermissionCatalog`, SQL seeds, `DbSeeder`, and route guard; all queries scoped by `_tenantAccess.RequireBusinessId()` exactly as `GetCashBookAsync` already does; outlet/location filters reuse the existing authorized-location pattern.
- **Reconciliation invariant:** Opening (Cash+Petty Cash+Bank) + external inflows − external outflows = Closing (Cash+Petty Cash+Bank), computed independently from the same posted-line query as `GetCashBookAsync`; the report must expose this equality explicitly (not merely assert it) and surface any difference rather than hide it. Internal transfers net to zero in the external total by construction (equal offsetting Dr/Cr across the same Cash/Bank universe).
- **Idempotency/atomicity:** none - read-only reporting; no posting.
- **Tests:** classification correctness per system `JournalType`/`ReferenceType`; internal-transfer elimination for all three transfer directions; settlement non-double-counting; `YearEndClosing`/opening-balance exclusion; reconciliation equality; `Unclassified` bucket for an unmapped manual journal; comparative period calculation; tenant/outlet isolation; permission enforcement.
- **Completion boundary:** Cash Flow is available, reconciles exactly to configured liquidity movement, and never fabricates a classification for an unmapped source.
- **Dependencies:** none beyond the accepted P3-01/P3-06/P3-07 posting model already in place.

**Slice 1 implementation result (2026-09-06):** implemented. The backend adds typed Cash Flow DTOs, primary/comparative service methods, `GET /api/accounting/reports/cash-flow`, `GET /api/accounting/reports/cash-flow/comparative`, and `GET /api/accounting/reports/cash-flow/export`. The service classifies only from authoritative `JournalType`, `ReferenceType`, reversal linkage, and configured counterpart account IDs; display/account names are never used for classification. Cash Flow is projected from the existing Cash Book account resolution and row evidence, with added `CounterpartAccountIds`, `ReversalOfJournalEntryId`, and `ReversedByJournalEntryId` fields on Cash Book rows for source evidence. Internal `LiquidityTransfer` rows are excluded from external totals and retained as memo evidence; `OpeningBalance`, `OpeningBalanceAdjustment`, and `FinancialYearClose`/`YearEndClosing` rows are excluded from current-period Cash Flow and retained as cutover/non-cash evidence. Card/MFS Clearing accounts remain outside the liquidity universe, so settlement is counted only when Bank receives the posted settlement. The frontend adds typed models, AccountingService methods, a guarded `/reports/cash-flow` route, Reporting menu entry, Cash Flow report UI, comparative display, visible Unclassified section, reconciliation display, and CSV export invocation. No migration was introduced.

**Slice 1 validation result (2026-09-06):** focused Cash Flow backend service tests: 9/9 passed; Cash Flow HTTP/permission/export test: 1/1 passed; accounting report/permission/Cash Book regression set: 24/24 passed; full backend regression suite: 469/469 passed; backend Release build passed; focused Angular Cash Flow component tests: 3/3 passed; full Angular test suite: 49/49 passed; Angular production build passed. Non-blocking advisories observed: existing `SQLitePCLRaw.lib.e_sqlite3` NU1903 warning in backend test/build output, existing xUnit analyzer warnings in `P306AdjustmentRelationalAtomicityTests`, and an existing jsdom download-navigation advisory from the accepted close-workbench Angular spec. None are introduced by Slice 1 and none block bounded review.

**Slice 2 - Profitability and monetary rounding**

- **Objective:** authoritative Business/Outlet profitability and reliable Product/category contribution information, plus a centralized monetary-rounding policy for new calculations.
- **Profitability reuse:** `AccountingReportService` already contains `GetProfitLossForScopeAsync(dateFrom, dateTo, locationId, locationType)`, used today for comparative P&L; Outlet profitability reuses this exactly, looped per outlet, with any revenue/COGS/expense line lacking an Outlet dimension reported as a separate `Unallocated` total so the sum of per-outlet + `Unallocated` reconciles to the unscoped P&L. Product/category contribution reuses the existing `GetCogsAsync` pattern of resolving Sale/`SaleItem` by `ProductId`/`VariantId` and matching COGS journal lines by sale `ReferenceId`; Revenue per product is derived the same way from `SaleItem`. Journal lines carry no product/category dimension, so product/category figures are contribution margin (Revenue − COGS) only, never fully allocated net profit, and any journal-only Expense/Other Income activity with no product linkage remains in `Unallocated`.
- **Backend changes:** add `BusinessProfitabilityReportDto`, `OutletProfitabilityReportDto`, `ProductProfitabilityReportDto` (with explicit `UnallocatedAmount`/`UnallocatedReason` fields) and corresponding `GetOutletProfitabilityAsync`/`GetProductProfitabilityAsync` methods; no change to existing P&L/COGS calculation logic.
- **Data model/migration:** **none required.** No product/category dimension is added to historical or new journal lines; reporting queries the existing Sale/SaleItem/GRN source tables already used by `GetCogsAsync`, exactly as instructed (query source evidence and reconcile to Journal Engine totals rather than adding dimensions to journal lines).
- **Rounding inventory (evidence-based):** `AccountingReportService` inventory valuation already uses `Math.Round(value, 2, MidpointRounding.AwayFromZero)` explicitly (2 sites) - this is the accepted precedent to follow. In contrast, `SaleService`, `GrnService`, `PurchaseOrderService`, `PricingService`, and `CashSessionService` call `Math.Round(value, 2)` without an explicit `MidpointRounding` argument, which resolves to the .NET default `ToEven` ("banker's rounding") - not `AwayFromZero`. This is precisely the "framework/default ambiguity" the frozen policy targets, and is flagged here as the documented conflict rather than silently changed: migrating these calculation sites to explicit `AwayFromZero` changes exact-halfway-boundary behavior for **future** postings only (historical posted journals are immutable and are never recalculated, per policy). Recommendation: proceed with migration, since the frozen policy explicitly requires an explicit midpoint rule for new calculations and this is the documented gap it was written to close.
- **Rounding design:** add a single static `MonetaryRounding.ToCurrency(decimal value)` helper (2 decimals, `AwayFromZero`) in a shared accounting utility location; migrate the identified `SaleService`, `GrnService`, `PurchaseOrderService`, `PricingService`, and `CashSessionService` currency-rounding call sites to it. Leave untouched: 4-decimal unit-cost/weighted-average-cost calculations in `SaleService`/`StockAdjustmentService` (valuation precision, not journal currency precision), percentage-display rounding in `InventoryReportService`/`InventoryReportsController` (presentation, not currency), and `BankStatementImportService`'s running-balance comparison tolerance (evidence validation, not a posting calculation). Existing configured Rounding Difference account usage in `SaleService`/`GrnService` posting is unchanged; only the arithmetic call is centralized.
- **APIs:** `GET /api/accounting/reports/business-profitability`, `.../outlet-profitability`, `.../product-profitability` under `AccountingReportsController`; dedicated `accounting.reports.profitability` permission.
- **Angular:** add profitability models/service methods; add a `profitability-report` component with Business/Outlet/Product tabs, explicit `Unallocated` line always visible, and reconciliation-to-P&L display; route + menu entry guarded by `accounting.reports.profitability`.
- **Permissions/tenant isolation:** new `accounting.reports.profitability` permission through the same catalog/seed/route-guard path as Slice 1; all queries scoped by tenant and authorized outlet exactly as existing P&L/COGS filters already are.
- **Reconciliation invariant:** sum(outlet profitability) + Unallocated = unscoped P&L for the same period; sum(product/category contribution) + Unallocated = P&L Revenue/COGS for the same period. Both differences must be surfaced, never hidden.
- **Tests:** outlet reconciliation with a deliberate business-level (no-outlet) expense proving `Unallocated`; product contribution reconciliation with a non-product-linked adjustment proving `Unallocated`; no fabricated allocation of shared costs; rounding midpoint behavior for the migrated call sites at an exact `.005` boundary; rounding-difference posting unaffected; historical-journal immutability (existing posted journals unchanged after the helper migration).
- **Completion boundary:** profitability reports reconcile exactly to P&L/COGS with explicit `Unallocated`, and new currency calculations use one explicit, centrally defined midpoint rule.
- **Dependencies:** none on Slice 1.

**Slice 2 implementation result (2026-09-06):** implemented. Backend profitability reporting now exposes typed Business, Outlet, and Product/category contribution DTOs and endpoints: `GET /api/accounting/reports/business-profitability`, `GET /api/accounting/reports/business-profitability/comparative`, `GET /api/accounting/reports/outlet-profitability`, `GET /api/accounting/reports/outlet-profitability/comparative`, `GET /api/accounting/reports/product-profitability`, `GET /api/accounting/reports/product-profitability/comparative`, and `GET /api/accounting/reports/profitability/export`. Business profitability wraps the accepted P&L path. Outlet profitability groups authoritative posted journal lines by `JournalEntryLine.OutletId ?? JournalEntry.OutletId` and keeps dimensionless lines in an explicit `Unallocated` row. Product/category contribution loads reliable `SaleItem`/`Product`/`Category` evidence only for posted `Sale` journals in the selected accounting period and reconciles attributed revenue/COGS to authoritative Sale-reference journal revenue/COGS, with unallocated/unmapped amounts surfaced. The Angular UI adds typed models, service methods, `/reports/profitability`, Reporting menu entry, report-level permission guard, Business/Outlet/Product Contribution tabs, comparative display, export invocation, explicit `Unallocated`, and contribution-not-net-profit terminology. No migration was introduced.

**Slice 2 rounding result (2026-09-06):** implemented prospectively only. Added shared `MonetaryRounding.ToCurrency(decimal)` with 2-decimal `MidpointRounding.AwayFromZero`. Migrated selected 2-decimal monetary/posting paths in `SaleService`, `GrnService`, `PurchaseOrderService`, `PricingService`, and `CashSessionService`. Existing configured Rounding Difference account behavior in Sale and GRN posting is preserved; only the rounding call policy was centralized. Historical `JournalEntry`/`JournalEntryLine` rows are not recalculated or rewritten. Deliberately retained compatibility/precision paths: `SaleService` 4-decimal VAT-rate and cost/weighted-average calculations; `AccountingReportService` 4-decimal inventory unit-cost presentation; inventory percentage-display rounding; and `BankStatementImportService` running-balance evidence checks.

**Slice 2 validation result (2026-09-06):** focused profitability + monetary rounding backend tests: 10/10 passed; Profitability HTTP/permission/export test: 1/1 passed; related P&L/COGS/reporting, permission, Sale, GRN, purchase, and Cash Session regression set: 49/49 passed; full backend regression suite: 480/480 passed; backend Release build passed; focused Angular profitability component tests: 4/4 passed; full Angular test suite: 53/53 passed; Angular production build passed. Non-blocking advisories observed: existing `SQLitePCLRaw.lib.e_sqlite3` NU1903 warning in backend test/build output, existing xUnit analyzer warnings in `P306AdjustmentRelationalAtomicityTests`, and an existing jsdom download-navigation advisory from the accepted close-workbench Angular spec. None are introduced by Slice 2 and none block bounded review.

**Slice 3 - Retry/atomicity hardening and legacy finance controls**

- **GRN completion:** `GrnService.CompleteAsync` checks `grn.Status == "completed"` before `BeginTransactionAsync`, with no row lock and no DB-level uniqueness on the status transition itself. However, the GRN's system journal already posts through `JournalEntryService.CreateOrGetSystemDraftAsync`/`PostAsync`, which is protected by the existing filtered unique system-journal index (`BusinessId`, `JournalType`, `ReferenceType`, `ReferenceId`); a concurrent duplicate completion attempt cannot create a second posted journal for the same GRN. The genuine remaining risk is therefore narrower than "duplicate financial posting": a race could still attempt duplicate inventory-quantity mutation before the journal guard is reached. Plan: add a bounded relational retry/duplicate-submission regression test (following the existing accepted Purchase-and-Receive `(WarehouseId, IdempotencyKey)` unique-index precedent as the model). If the test proves inventory can double-mutate, the smallest correction is to move the status check inside the transaction under a row lock (consistent with `JournalEntryService`'s existing `LockJournalRowAsync` pattern) rather than a new schema element.
- **Cash Session close:** `CashSessionService.CloseAsync` already performs an idempotent early-return when `session.Status == "closed"`, but that check also occurs before `BeginTransactionAsync` without a row lock. Plan: add the equivalent bounded retry/duplicate-close regression test; if it proves a real double-variance-journal risk, apply the same minimal in-transaction row-lock correction. `PostVarianceJournalAsync` should be confirmed to use a deterministic system-journal reference (session ID) so the existing Journal Engine uniqueness guard already provides a second line of defense.
- **No unlimited fault-injection matrix:** only these two named, already-identified gaps are in scope; all other accepted relational transaction/rollback/retry evidence remains valid as-is.
- **Legacy finance guarding - concrete findings:** `AccountsController` (`accounts.view`/`accounts.create`/`accounts.edit`/`accounts.delete` policies) and `TransactionsController` (`transactions.view`/`transactions.create`) expose live write endpoints for the legacy `Account`/`Transaction` entities. Per `PermissionCatalog` aliases, `accounts.*` and `transactions.*` claims are granted implicitly to any user holding the corresponding accepted `accounting.chart_accounts.*`/`accounting.journals.*` permissions, so Finance users can reach these legacy write endpoints today without a dedicated legacy permission. `AccountService.CreateAsync` writes a mutable `Account.Balance` entirely outside the Journal Engine - this is the concrete second-financial-record risk the frozen policy targets. `BillsController`/`BillService` similarly expose bill creation under `accounts.view`/`accounts.create`. `SupplierPaymentService.CreateAsync` still reads `_db.Bills` as a fallback allocation target when a payment line has no `SupplierPayableDocumentId`, so `Bill` **reads** remain a genuine internal dependency and must not be broken by guarding.
- **Guarding plan:** (1) block legacy write endpoints - `AccountsController` POST/PUT/DELETE, `TransactionsController` POST, and `BillsController` POST/PUT for **new** bills - by returning a clear rejection (consistent with the P3-07 pattern already used to retire the legacy direct Financial Year close/reopen routes) rather than deleting the controllers/services outright; existing `GET` reads on `Account`/`Transaction`/`Bill` remain available and are labeled non-authoritative in API responses/UI. (2) `SupplierPaymentService`'s existing read-only `Bill` lookup for allocation continuity is explicitly preserved and excluded from the write-block. (3) Remove `/accounts`, `/transactions`, `/bills` from the Finance navigation menu (`menu.model.ts`) and retire `AccountsListComponent`/`TransactionsListComponent`/`BillsListComponent` create/edit actions, keeping read-only history views only if a genuine read dependency remains. (4) No physical table/data deletion; historical rows are preserved read-only, consistent with the frozen policy.
- **Data model/migration:** **none required** for the row-lock hardening (code-only) or for legacy guarding (controller/service/navigation changes only, no schema change). A migration would only follow if the GRN/Cash Session regression tests prove a genuine defect requiring a new relational uniqueness constraint - this is conditional and not committed upfront.
- **APIs:** no new endpoints; existing legacy endpoints are narrowed (write actions rejected) rather than replaced.
- **Angular:** remove/adjust legacy menu entries and disable create/edit UI in the three legacy list components; no new components.
- **Permissions/tenant isolation:** no new permissions; existing tenant scoping on GRN/Cash Session/legacy read paths is unchanged.
- **Tests:** GRN duplicate-completion regression (inventory mutation and journal uniqueness); Cash Session duplicate-close regression (variance journal uniqueness); legacy write-endpoint rejection; legacy read endpoints still function; `SupplierPaymentService` Bill-allocation fallback continues to work unmodified; Journal Engine remains the only path that can create a posted financial record.
- **Completion boundary:** GRN/Cash Session retry behavior is proven safe (by test, or by the smallest necessary correction), and no legacy endpoint can create a financial record outside the Journal Engine, without breaking the existing supplier-payment Bill-allocation dependency.
- **Dependencies:** none on Slices 1-2; independent hardening work.

**Slice 3 implementation result (2026-09-06):** implemented. GRN completion required **tests-only evidence**, not a production code fix: bounded relational retry evidence proves a successful completion followed by a duplicate/retry leaves exactly one posted `Grn` journal, one supplier payable document, consistent completed GRN state, and non-duplicated inventory quantity. Existing P3-02 relational fault-injection evidence continues to prove rollback of GRN/journal/payable/inventory state when payable projection persistence fails and retry posts exactly once. Cash Session close required **tests-only evidence**, not a production code fix: bounded relational retry evidence proves a successful surplus close followed by a duplicate/retry returns the already closed session and leaves exactly one posted `CashSessionClose` variance journal with consistent session state. Legacy write controls were implemented backend-side by rejecting obsolete writes through `AccountsController` POST/PUT/DELETE, `TransactionsController` POST, and `BillsController` POST/PUT status. These guards apply regardless of legacy permission aliases or canonical permission aliases. Retained legacy reads/searches remain available for historical/non-authoritative evidence: `AccountsController` GET, `TransactionsController` GET/search/ledger, and `BillsController` GET/search/summary. The required `SupplierPaymentService` legacy `Bill` read fallback remains intact for accepted P3-02 Supplier Payment compatibility. No legacy data or tables were deleted, no historical journals were fabricated, and no migration was introduced.

**Slice 3 Angular result (2026-09-06):** implemented. Active Finance navigation no longer points to legacy `/accounts` or `/transactions` workflows. Retained legacy Account, Transaction, and Bill screens are labeled as read-only historical/non-authoritative evidence; create/edit/delete/pay controls are removed from the rendered UI. Backend rejection remains the authoritative guard.

**Slice 3 validation result (2026-09-06):** focused GRN/Cash Session retry + legacy write-guard backend tests: 7/7 passed; Supplier Payment/AP compatibility, Journal Engine/idempotency, permission, and tenant-isolation regression set: 60/60 passed; full backend regression suite: 484/484 passed; backend Release build passed; focused Angular legacy navigation/read-only tests: 16/16 passed; full Angular test suite: 57/57 passed; Angular production build passed. Non-blocking advisories observed: existing `SQLitePCLRaw.lib.e_sqlite3` NU1903 warning in backend test/build output, existing xUnit analyzer warnings in `P306AdjustmentRelationalAtomicityTests`, and an existing jsdom download-navigation advisory from the accepted close-workbench Angular spec. None are introduced by Slice 3 and none block final P3-08 acceptance.

**File/component impact inventory (indicative):**
- Backend: `AccountingReportDtos.cs`, `IAccountingReportService.cs`/`AccountingReportService.cs` (new methods only), `AccountingReportsController.cs` (new routes), `PermissionCatalog.cs` + SQL seeds + `DbSeeder.cs` (two new report permissions), a new shared rounding helper, `SaleService.cs`/`GrnService.cs`/`PurchaseOrderService.cs`/`PricingService.cs`/`CashSessionService.cs` (rounding call-site migration only), `GrnService.cs`/`CashSessionService.cs` (conditional row-lock hardening), `AccountsController.cs`/`TransactionsController.cs`/`BillsController.cs` (write-endpoint rejection).
- Frontend: `accounting.model.ts`, `accounting.service.ts`, new `cash-flow-report` and `profitability-report` components, `app.routes.ts`, `menu.model.ts` (add two report entries, remove three legacy entries), `accounts-list.component.ts`/`transactions-list.component.ts`/`bills-list.component.ts` (disable create/edit).

**Genuine implementation dependencies/blockers:** none discovered that would prevent starting Slice 1. The only conditional item is the GRN/Cash Session row-lock correction in Slice 3, which is contingent on regression-test evidence, not a pre-existing confirmed defect, and does not block Slices 1-2. The `SupplierPaymentService` → `Bill` read dependency is a required guarding constraint, not a blocker, and is explicitly planned for in Slice 3.

**Recommended starting point:** Slice 1 (Cash Flow), because it requires no migration, no legacy-guarding risk analysis, and reuses `GetCashBookAsync` most directly, giving the fastest concrete validation of the classification design before Slice 2/3 work begins.

## 7. Phase 3-Wide Definition of Done


A milestone is complete only when:

1. Its financial source of truth and any approved operational subledger are explicit and do not duplicate each other.
2. Required account mappings are configuration-resolved and validated for the tenant and applicable outlet.
3. Every posting is balanced, idempotent, period-governed, atomic with its operational state, and auditable.
4. Reversal/correction behavior preserves source references, history, posting date semantics, and immutable posted journals.
5. Tenant and authorized outlet/warehouse/location isolation is tested for APIs and reporting.
6. Reconciliation to each configured GL control account is automated, independently calculated, returned by the API, and visible in the UI.
7. Legacy/unreliable data is explicitly identified; it is never silently estimated from mutable values.
8. Backend build, focused tests, related regression suites, full backend suite, Angular production build, and frontend tests pass.
9. The milestone has a separate approved acceptance checklist. This planning document is not an acceptance checklist.

## 8. Final Sign-Off and Reconciliation Expectations

Phase 3 final sign-off requires:

- Balance Sheet equality: Assets = Liabilities + Equity.
- Detailed GL opening plus movements equals closing for every account/scope.
- AP, AR, VAT, Bank/Clearing, Inventory, COGS, and Cash Book control reconciliation differences are zero or explicitly approved documented exceptions.
- Year-end close cannot complete without the required approved reconciliation gates.
- Every financial statement total can drill down to posted journal lines and originating business references where available.
- No Phase 3 report may treat operational records, current product cost, or mutable account balance caches as its financial source of truth.

## 9. Remaining Future Decisions

The following are not Phase 3 sign-off blockers:

1. P3-08's Cash Flow method, profitability/allocation scope, KPI/budget scope, monetary rounding policy, legacy-model decommissioning timing, and sensitive-data masking policy are resolved; see "Approved P3-08 decisions (2026-09-06)" in the P3-08 section. Only the future items explicitly named there (a different accepted-workflow rounding exception if discovered, a future shared-cost allocation methodology, a future budget/target model, physical legacy-table migration timing, and a future role/value disclosure matrix) remain open and are not P3-08 implementation blockers.
2. Only if the fully-settled POS policy is formally changed: customer credit eligibility, receipts/allocations, customer advances, credit notes, write-offs, and AR ageing policy.
3. Only if statutory reporting is separately authorized: tax jurisdiction, filing frequency, VAT return format, input-VAT eligibility, tax classes/rates/exemptions, and statutory rounding rules.

## 10. Genuine Prerequisites and Current Blockers

- P3-01, P3-02, P3-05, P3-06, and P3-07 have completed their bounded implementation and acceptance scope.
- P3-03/P3-04 remain deferred/not required, not blocked work, unless the approved fully-settled POS policy changes.
- P3-08 business decisions are frozen (2026-09-06); Slices 1, 2, and 3 are implemented. P3-08 is ready for final acceptance review using the dedicated P3-08 acceptance checklist.
- Historical movements without reliable cost remain a preserved legacy-data limitation. They must remain unvalued and must never be silently estimated using current product cost.

P3-08 implementation is complete. No Phase 4 application code or implementation activity is authorized by this planning document alone.
