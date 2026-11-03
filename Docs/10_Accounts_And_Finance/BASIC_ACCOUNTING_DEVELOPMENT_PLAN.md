# BASIC_ACCOUNTING_DEVELOPMENT_PLAN

Updated: 2026-08-09
Status: Final Phase 1 closure complete — Accepted

## Final Phase 1 Closure Decision

Final Phase 1 Decision: Accepted

Closure summary:
- RM-01 through RM-09 applicable milestones are completed and accepted.
- All milestone acceptance checklists are consistent and have no unresolved blocking items.
- Journal Engine is accepted.
- Full backend regression is green at 296/296 passed.
- Accounting permission regression is green at 5/5 passed.
- Frontend production build is green.
- Required migrations are present and consistent for the implemented Phase 1 accounting scope.
- No Phase 1 functionality remains partially implemented; remaining work is intentionally deferred to Phase 2.

### Non-blocking technical debt
- The NuGet vulnerability warning for SQLitePCLRaw.lib.e_sqlite3 remains non-blocking for this closure.
- Legacy finance routes and permissions remain transitional compatibility surfaces and should be retired after consumer migration and telemetry review.

### Functionality explicitly deferred to Phase 2
- Operational posting integrations for POS, purchase/GRN, inventory, and cash-session workflows.
- AP/AR ledger-specific tables and workflows.
- VAT return and bank-reconciliation modules.
- Advanced reporting and export enhancements beyond the accepted Phase 1 scope.
- Legacy endpoint decommissioning after migration and regression sign-off.

### Recommended production deployment and smoke-test checklist
1. Take a database backup and verify the migration history includes the accepted accounting foundation and workflow migrations.
2. Apply the pending migrations in staging first and confirm no migration drift or data issues.
3. Smoke-test chart accounts, financial years/periods, opening balances, expenses, other income, manual journals, and financial reports.
4. Verify role/permission assignments and frontend route access for accounting modules.
5. Confirm journal posting, reversal, and report outputs against seeded baseline data.
6. Review production logs for authentication, authorization, and migration warnings after deployment.

## 0. Architecture Review Addendum (Final Pre-Implementation Check)

This addendum re-validates the plan against the current POS v2 codebase before Phase 1 starts.

### 0.1 Best-practice compliance for retail POS accounting

Validated as aligned, with the following mandatory refinements:

1. Use strict double-entry journals as the only financial source of truth.
2. Do not update account balances directly in business logic.
3. Keep operational calculations in operational services; accounting service consumes those results.
4. Keep posting synchronous inside operational DB transactions for high-integrity events.
5. Make posted journals immutable and correction-only through reversal.
6. Add posting idempotency at DB unique-constraint level.

### 0.2 Unnecessary table/entity/abstraction elimination

Refinement applied: reduce Phase 1 to minimum viable accounting schema.

Keep in Phase 1:
1. chart_accounts
2. accounting_configuration
3. accounting_outlet_configuration
4. journal_entries
5. journal_entry_lines
6. financial_years
7. accounting_periods
8. other_income_entries
9. Extend existing expenses table for workflow and posting fields

Explicitly defer (do not create in Phase 1):
1. Separate opening-balance helper table (use journal type OpeningBalance instead)
2. Generic event bus/outbox specifically for accounting posting
3. Separate cash transfer table unless an operational need appears during Phase 2
4. AR/AP ledger-specific tables
5. VAT return-specific tables
6. Bank reconciliation tables

### 0.3 Major refactor avoidance check

Design is confirmed to avoid major refactors by integrating at existing service transaction boundaries:

1. Sales: SaleService create/void/refund flow remains operational owner.
2. Purchase/GRN: PurchaseOrderService and GrnService remain operational owners.
3. Inventory/Stock: StockAdjustmentService and StockCountService remain operational owners.
4. Cash sessions: CashSessionService remains operational owner.
5. Reports: existing report/export patterns are reused.

Integration point is service-level call to IAccountingPostingService within current transaction scopes.

### 0.4 Future-proofing check (AP/AR/VAT/Bank Reconciliation)

The model is revised to avoid breaking changes later:

1. Journal lines keep optional party dimensions (SupplierId, CustomerId).
2. Journal headers keep generic reference identity (ReferenceType, ReferenceId, ReferenceNumber).
3. Journal headers keep location and terminal context (OutletId, WarehouseId, TerminalId, CashSessionId).
4. Journal lines allow optional metadata fields for future tax or reconciliation keys.
5. Chart accounts support control-account behavior for future AP/AR modules.

This allows future AP, AR, VAT, and reconciliation features to be added as additive tables/services without changing posted journal semantics.

### 0.5 Hidden risks identified (must be controlled)

1. Dual-source risk: legacy Account/Transaction + new journals can diverge if both remain writeable.
2. Costing risk: current inventory valuation is mostly derived from mutable master cost fields.
3. Date semantics risk: PostingDate vs BusinessDate mismatch can distort period reports.
4. Numbering concurrency risk: JournalNumber allocation under load.
5. Closed-period risk: operational backdated events can conflict with accounting period lock rules.
6. Refund valuation risk: partial returns must use original sale cost basis when available.
7. Permission drift risk: backend policy, frontend guard, menu, and seed scripts can diverge.

Controls are incorporated in sections below.

### 0.6 Approved pre-journal design decisions

These decisions are approved and must be treated as the baseline before continuing beyond the foundation milestone.

1. Journal numbers must use configurable sequential numbering, not high-entropy identifiers.
2. Journal numbering must be business-scoped, concurrency-safe, never reused, and optionally reset by financial year.
3. Journal numbering must remain human-readable for audit, reconciliation, and printed reports.
4. Financial year and accounting period governance must be fully modeled before operational posting begins.
5. Accounting permissions must expand from one canonical vocabulary shared by backend authorization, seeds, route guards, menu visibility, and role UI.
6. Journal metadata must be preserved on every accounting-v2 journal, with draft-time nullability allowed only where posting-time assignment is intentional.

---

## 1. Current-State Assessment

### 1.1 Existing modules and reusable architecture

Backend layering and conventions are already in place:

1. Core entities: src/RetailPOS.Core/Entities/
2. Infrastructure (EF Core, repositories, migrations): src/RetailPOS.Infrastructure/
3. API (services/controllers/DTOs): src/RetailPOS.API/

Reference docs:

1. Docs/01_Architecture/PROJECT_STRUCTURE.md
2. Docs/02_Security/PERMISSION_MODEL.md
3. Docs/02_Security/AUDIT_LOG_IMPLEMENTATION.md
4. Docs/01_Architecture/LIST_STATE_PRESERVATION.md
5. Docs/01_Architecture/LIST_PAGE_FILTER_STANDARDS.md

### 1.2 Existing entities and tables relevant to accounting

Current financial and operational entities:

1. Account: src/RetailPOS.Core/Entities/Account.cs
2. Transaction: src/RetailPOS.Core/Entities/Transaction.cs
3. Bill: src/RetailPOS.Core/Entities/Bill.cs
4. Expense: src/RetailPOS.Core/Entities/Expense.cs
5. Sale: src/RetailPOS.Core/Entities/Sale.cs
6. SalePayment: src/RetailPOS.Core/Entities/SalePayment.cs
7. SaleVoid: src/RetailPOS.Core/Entities/SaleVoid.cs
8. PurchaseOrder: src/RetailPOS.Core/Entities/PurchaseOrder.cs
9. Grn and GrnItem: src/RetailPOS.Core/Entities/Grn.cs, src/RetailPOS.Core/Entities/GrnItem.cs
10. Inventory: src/RetailPOS.Core/Entities/Inventory.cs
11. StockLedger: src/RetailPOS.Core/Entities/StockLedger.cs
12. StockAdjustment and StockAdjustmentLine: src/RetailPOS.Core/Entities/StockAdjustment.cs, src/RetailPOS.Core/Entities/StockAdjustmentLine.cs
13. StockCount and StockCountLine: src/RetailPOS.Core/Entities/StockCount.cs, src/RetailPOS.Core/Entities/StockCountLine.cs
14. CashSession: src/RetailPOS.Core/Entities/CashSession.cs
15. PosTerminal: src/RetailPOS.Core/Entities/PosTerminal.cs

EF mappings and constraints:

1. src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs
2. src/RetailPOS.Infrastructure/Data/Migrations/20260725181340_AddCashSessionsFoundation.cs

### 1.3 Current transaction flows

Sales completion and split payment:

1. POST /api/sales: src/RetailPOS.API/Controllers/SalesController.cs
2. Service flow and stock update transaction: src/RetailPOS.API/Services/SaleService.cs
3. Idempotency check: ISaleRepository.FindByIdempotencyKeyAsync and unique index on OutletId+IdempotencyKey

Sales void and refund:

1. POST /api/sales/{id}/void and /refund: src/RetailPOS.API/Controllers/SalesController.cs
2. Service methods: SaleService.VoidAsync and SaleService.RefundAsync

Purchase and GRN:

1. Purchase order flow: src/RetailPOS.API/Controllers/PurchaseOrdersController.cs
2. Immediate purchase-receive flow: POST /api/purchase-orders/purchase-receive
3. GRN flow: src/RetailPOS.API/Controllers/GrnsController.cs and src/RetailPOS.API/Services/GrnService.cs

Supplier payment:

1. Not implemented as a dedicated source-document workflow.
2. Current Bill model is status-only and insufficient for allocation/advance logic.

Stock adjustment and stock count:

1. Stock adjustments: src/RetailPOS.API/Controllers/StockAdjustmentsController.cs and service
2. Stock counts and approval: src/RetailPOS.API/Controllers/StockCountsController.cs and service

Cash opening and closing:

1. POS cash session endpoints: src/RetailPOS.API/Controllers/PosController.cs
2. Cash session logic and 15-hour safeguard: src/RetailPOS.API/Services/CashSessionService.cs
3. Max duration setting: src/RetailPOS.API/Settings/AppSettings.cs

### 1.4 Inventory costing method (current)

Current valuation/cost pattern is primarily:

1. Product.CostPrice + ProductVariant.CostAdjustment
2. GRN line UnitCost is captured but not fully used as immutable historical costing source across all downstream financial logic

References:

1. src/RetailPOS.API/Services/InventoryReportService.cs
2. src/RetailPOS.Core/Entities/Product.cs
3. src/RetailPOS.Core/Entities/ProductVariant.cs
4. src/RetailPOS.Core/Entities/GrnItem.cs

### 1.5 Existing audit and permissions

Audit:

1. Audit v2 infrastructure with event/entity/field-change model
2. Immutable audit table strategy already documented and implemented

References:

1. Docs/02_Security/AUDIT_LOG_IMPLEMENTATION.md
2. src/RetailPOS.Infrastructure/Audit/AuditService.cs
3. src/RetailPOS.API/Controllers/AuditEventsController.cs

Permissions and role seeds:

1. Permission catalog: src/RetailPOS.API/Authorization/PermissionCatalog.cs
2. Role management validation: src/RetailPOS.API/Services/RoleService.cs
3. Seed roles: database/seed-v2-roles.sql and database/seed-data.sql

### 1.6 Existing reporting and export framework

1. Sales report APIs exist but are not journal-driven financial statements
2. Inventory reports include export (CSV/XLSX) patterns reusable for accounting reports

References:

1. src/RetailPOS.API/Controllers/SalesReportsController.cs
2. src/RetailPOS.API/Services/SalesReportService.cs
3. src/RetailPOS.API/Controllers/InventoryReportsController.cs

### 1.7 Existing frontend accounting and permission routing

Current finance pages:

1. accounts list
2. transactions list
3. expenses list/form
4. bills list

References:

1. retailpos-frontend/src/app/app.routes.ts
2. retailpos-frontend/src/app/models/menu.model.ts
3. retailpos-frontend/src/app/services/accounting.service.ts
4. retailpos-frontend/src/app/pages/accounting/

List-state preservation compliance is partial:

1. Expenses list uses ListStateService
2. Accounts, transactions, bills lists need alignment

Reference:

1. retailpos-frontend/src/app/services/list-state.service.ts

---

## 2. Gap Analysis

Classification set:

1. Already Available
2. Partially Available
3. Missing
4. Requires Modification
5. Requires Clarification
6. High Risk

### 2.1 Accounting foundation

1. Double-entry JournalEntry + JournalEntryLine: Missing
2. Journal posting lifecycle (Draft/Posted/Reversed/Cancelled): Missing
3. Centralized posting service abstraction: Missing
4. Idempotent posting by ReferenceType+ReferenceId+JournalType: Missing
5. Posted journal immutability and reversal-only correction: Missing

### 2.2 Chart of Accounts and mapping

1. Existing Account table: Partially Available
2. Hierarchy and account code uniqueness by business: Missing
3. System/control/manual-posting flags: Missing
4. Account mapping for operational flows: Missing
5. Outlet-level override mapping fallback: Missing

### 2.3 Operational integrations

1. Sale, split payment, refund, void operational events: Partially Available
2. GRN and purchase receive events: Partially Available
3. Supplier payment source-doc workflow: Missing
4. Expense approval-posting workflow: Missing (expense exists, but no approval states)
5. Other income source-doc workflow: Missing
6. Stock shortage/gain accounting posting: Missing
7. Cash closing difference posting: Missing

### 2.4 Financial periods and control

1. FinancialYear and AccountingPeriod entities: Missing
2. Closed/locked period posting guard: Missing
3. Backdated posting policy and permission model: Requires Clarification

### 2.5 Reporting

1. General Ledger: Missing
2. Trial Balance: Missing
3. P&L from journals: Missing
4. Balance Sheet: Missing
5. Cash Book: Missing
6. COGS report tied to journal + cost source: Missing
7. Inventory valuation report tied to accounting policy: Requires Modification

### 2.6 Permissions and role mapping

1. Existing accounts/transactions/expenses permissions: Partially Available
2. Accounting namespace permissions: Missing
3. User requested Accountant role mapping: Requires Clarification
4. Decision now: use existing AccountsAdmin role for Phase 1

### 2.7 Hidden risk checkpoints

1. Direct account balance update pattern in TransactionService: High Risk
2. Cost-source mutability risk for COGS: High Risk
3. Posting date vs business date ambiguity: High Risk
4. Legacy and v2 financial writes in parallel: High Risk

---

## 3. Scope Validation

### 3.1 Phase allocation

Phase 1 (approved planning scope):

1. Chart of Accounts
2. Accounting Configuration
3. Journal Engine
4. Financial Year and Accounting Period
5. Opening Balances
6. Expense v2 workflow
7. Other Income
8. Manual Journal
9. General Ledger
10. Trial Balance
11. Basic Profit and Loss

Phase 2:

1. POS sale and return posting
2. Purchase/GRN posting
3. Supplier payment posting
4. Stock count/adjustment posting
5. Cash closing difference posting
6. Cash book
7. COGS report
8. Inventory valuation report

Phase 3:

1. Balance Sheet
2. Accounting dashboard
3. Period close hardening and advanced reporting
4. Bank reconciliation
5. AP enhancements and optional AR

Future scope:

1. Full AR document lifecycle
2. VAT return and filing features
3. Multi-ledger advanced reconciliations

### 3.2 Safety review of phase sequence

Refined safest order:

1. Permission/catalog foundation and feature-flag strategy
2. DB schema foundation (CoA, mapping, journal, periods)
3. Journal engine with validation and idempotency
4. Chart/configuration APIs and UI
5. Financial year and period controls
6. Opening balance flow
7. Expense v2 and Other Income posting
8. Manual journal
9. GL/TB/P&L reports
10. Legacy coexistence hardening and regression gate
11. Phase 2 operational integrations only after above is stable

---

## 4. Proposed Architecture

### 4.1 Service boundaries

New services:

1. IChartOfAccountService
2. IAccountingConfigurationService
3. IJournalService
4. IAccountingPostingService
5. IFinancialYearService
6. IAccountingReportService
7. IOtherIncomeService
8. IOpeningBalanceService

Expense should evolve from current ExpenseService to workflow-capable accounting expense service rather than creating disconnected duplicate logic.

### 4.2 Journal engine design

JournalEntry fields:

1. Id
2. BusinessId
3. JournalNumber
4. JournalType
5. TransactionDate
6. PostingDate
7. BusinessDate
8. FinancialYearId
9. AccountingPeriodId
10. ReferenceType
11. ReferenceId
12. ReferenceNumber
13. Description
14. OutletId
15. WarehouseId
16. TerminalId
17. CashSessionId
18. Status
19. IsSystemGenerated
20. ReversalOfJournalEntryId
21. PostedAt
22. PostedBy
23. CreatedAt
24. CreatedBy
25. UpdatedAt
26. UpdatedBy

JournalEntryLine fields:

1. Id
2. JournalEntryId
3. AccountId
4. Description
5. DebitAmount
6. CreditAmount
7. OutletId
8. WarehouseId
9. SupplierId
10. CustomerId
11. PaymentMethodId
12. CostCenterId
13. CreatedAt
14. CreatedBy

Validation rules:

1. Debit and Credit cannot both be non-zero on same line.
2. Debit and Credit cannot both be zero.
3. Journal total debit must equal total credit.
4. Posted journals cannot be edited.
5. Posted journals cannot be hard-deleted.
6. Reversal requires reason.
7. Manual journals require at least two lines and support a maximum of 200 lines.
8. Manual journals require narration/description.

Manual journal workflow policy:

1. RM-07 uses configurable workflow per Business.
2. Mode A: `Draft -> Post`.
3. Mode B: `Draft -> Submit -> Approve / Reject -> Post`.
4. Workflow mode is configured through Accounting Configuration.
5. When approval workflow is enabled, maker-checker policy applies.
6. Maker-checker rule:
  - the journal creator cannot approve their own journal
  - approver must be a different user
  - Business Owner or Super Admin may bypass this only when `Allow Self Approval` is explicitly enabled in Accounting Configuration
  - maker-checker policy is configurable per Business
7. Manual journals support multiple attachments.
8. Attachment policy for RM-07:
  - configurable maximum file size
  - allowed file types: PDF, JPG, JPEG, PNG, DOCX, XLSX
  - every attachment must be visible from Journal Details
  - upload, delete, and download actions must be fully audited
9. Manual journal reference policy:
  - narration/description is mandatory
  - external reference number is optional
  - reference type is optional
  - reference ID is optional
  - external reference numbers are not unique
  - journal number remains the only system-generated unique accounting identifier
10. Manual journal dimensions:
  - each journal line may include Outlet, Warehouse, Cost Center, Supplier, and Customer
  - dimensions remain optional unless required by account configuration

Journal numbering strategy:

1. Format: `JR-{financial year label}-{sequence}` by default, for example `JR-2026-000001`.
2. Prefix must be configurable per business.
3. Numeric portion must be zero-padded and configurable in length.
4. Sequence scope must be unique per business.
5. Reset policy must support:
  - continuous sequence across all years
  - reset per financial year
6. Numbers are allocated only once and never reused, including reversed or cancelled journals.
7. Allocation must occur inside the same DB transaction as journal creation/posting.
8. Concurrency safety must use row-level locking or equivalent DB-safe increment semantics.
9. Posted journal number must remain immutable.

Recommended implementation model:

1. Add an `accounting_number_sequences` table to hold current issued values.
2. Keep the rendered final number on `journal_entries.journal_number`.
3. Resolve financial-year token from the assigned financial year, not from client-supplied text.

### 4.3 Posting orchestration

Pattern:

1. Operational services remain source-of-truth calculators.
2. IAccountingPostingService receives pre-validated operational payloads.
3. Posting is executed in same DbContext transaction as operational persistence.
4. Posting failures rollback operation.

No controller should create journal lines directly.

### 4.4 Reversal strategy

1. Reverse by creating new posted journal with opposite line directions.
2. Link reversal to original via ReversalOfJournalEntryId.
3. Preserve both entries for auditability.

### 4.5 Idempotency strategy

1. Unique key on BusinessId + JournalType + ReferenceType + ReferenceId
2. Pre-check existing posted journal on retry
3. Handle unique-constraint race by re-query and return existing journal

Numbering and idempotency interaction:

1. Do not allocate a new sequential number when an idempotent retry resolves to an existing journal.
2. Sequential numbering must advance only for successfully created journal rows within the committed transaction.
3. Rolled-back transactions must not expose reused numbers to later committed journals.
4. Manual journals do not use external reference numbers as unique identifiers.

### 4.6 Compatibility strategy

Side-by-side transition:

1. Keep legacy accounts/transactions/bills endpoints operational initially.
2. Introduce v2 accounting endpoints under /api/accounting/*.
3. Move each function to journal-backed behavior behind feature flags.
4. Convert legacy endpoints to read-only once v2 is validated per business.

---

## 5. Database Plan

No migrations are created yet in this step.

### 5.1 New tables

1. chart_accounts
- Purpose: hierarchical chart for all posting and reporting
- Key columns: BusinessId, ParentAccountId, AccountCode, AccountName, AccountType, NormalBalanceType, IsSystemAccount, IsControlAccount, AllowManualPosting, IsActive, DisplayOrder, audit columns

2. accounting_configuration
- Purpose: business-level account mapping
- Key columns: BusinessId and required account mapping IDs (cash, bank, inventory, sales, discount, return, cogs, shortages, gains, payable, advance, owner capital/drawings, rounding, cash diff gain/loss)

3. accounting_outlet_configuration
- Purpose: outlet-level account override mapping
- Key columns: BusinessId, OutletId, OutletCashAccountId, OutletPettyCashAccountId, OutletBankAccountId

4. journal_entries
- Purpose: journal header

5. journal_entry_lines
- Purpose: journal lines

6. financial_years
- Purpose: fiscal boundary and current/closed control

7. accounting_periods
- Purpose: period lock/reopen controls

8. other_income_entries
- Purpose: source document for non-sales income

9. accounting_number_sequences
- Purpose: durable sequential number allocator for accounting documents
- Key columns: BusinessId, SequenceType, FinancialYearId nullable, Prefix, Padding, LastIssuedNumber, ResetPolicy, audit columns
- Behavior: one row per business/sequence scope/(optional) financial year; incremented transactionally

### 5.2 Existing tables to modify

1. expenses
- Add workflow and accounting linkage fields:
  - ExpenseNumber
  - BusinessId
  - ExpenseCategoryId
  - ExpenseAccountId
  - PaymentMethodId
  - PaymentAccountId
  - SupplierId
  - ReferenceNumber
  - Attachment
  - Status
  - SubmittedBy/SubmittedAt
  - ApprovedBy/ApprovedAt
  - RejectedBy/RejectedAt
  - RejectionReason
  - CancelledBy/CancelledAt

2. Optional traceability links for operational tables (phase-dependent, additive only)
- Sales, GRN, StockAdjustment, CashSession may include AccountingJournalEntryId or a linking table if required for fast lookup

3. financial_years
- Add fields needed for numbering/governance:
  - YearCode or YearLabel
  - SequenceResetMode if business-specific override is required later

4. accounting_periods
- Add governance audit fields if not already present in implementation:
  - LockedAt
  - LockedBy
  - ReopenedAt
  - ReopenedBy
  - StatusReason optional

5. accounting_configuration
- Add journal numbering configuration fields:
  - JournalNumberPrefix
  - JournalNumberPadding
  - JournalSequenceResetPolicy

### 5.3 Constraints

1. chart_accounts unique (BusinessId, AccountCode)
2. accounting_outlet_configuration unique (BusinessId, OutletId)
3. journal_entries unique (BusinessId, JournalNumber)
4. journal_entries unique idempotency key (BusinessId, JournalType, ReferenceType, ReferenceId)
5. journal_entry_lines check constraints for debit/credit validity
6. FK constraints to protect referential integrity
7. accounting_number_sequences unique (BusinessId, SequenceType, FinancialYearId)
8. financial_years unique (BusinessId, YearCode) if YearCode is added

### 5.4 Indexing and performance

JournalEntry indexes:

1. (BusinessId, PostingDate)
2. (BusinessId, ReferenceType, ReferenceId)
3. (BusinessId, JournalNumber)
4. (BusinessId, OutletId, PostingDate)
5. (Status, PostingDate)

JournalEntryLine indexes:

1. (JournalEntryId)
2. (AccountId)
3. (AccountId, OutletId)
4. (SupplierId)
5. (CustomerId)

Additional indexes:

1. chart_accounts (BusinessId, AccountType, IsActive)
2. accounting_periods (FinancialYearId, Status)
3. accounting_number_sequences (BusinessId, SequenceType, FinancialYearId) unique/indexed for allocator lookup

### 5.5 Normalization review

Normalization is sufficient and practical for POS throughput:

1. Separate header and lines for journals.
2. Separate account master from mapping configuration.
3. Keep mapping denormalized by account role columns for fast validation and simpler administration.
4. Avoid over-normalized generic key-value mapping table in Phase 1 to reduce operational complexity.

---

## 6. Posting Event Matrix

Columns:

1. Operational Event
2. Posting Trigger
3. Debit Account
4. Credit Account
5. Posting Date
6. Business Date
7. Reference Type
8. Reference ID
9. Reversal Event
10. Idempotency Key
11. Implementation Phase

### 6.1 Matrix

1. Cash sale | Sale completed | Cash/Clearing | Sales | SaleDateTime | Sale.BusinessDate | Sale | Sale.Id | Void/Refund | BusinessId+Sale+Sale+SaleId | Phase 2
2. Card sale | Sale completed | Card Clearing/Bank | Sales | SaleDateTime | Sale.BusinessDate | Sale | Sale.Id | Void/Refund | BusinessId+Sale+Sale+SaleId | Phase 2
3. Bank-transfer sale | Sale completed | Bank | Sales | SaleDateTime | Sale.BusinessDate | Sale | Sale.Id | Void/Refund | BusinessId+Sale+Sale+SaleId | Phase 2
4. Split-payment sale | Sale completed | Multi payment accounts | Sales | SaleDateTime | Sale.BusinessDate | Sale | Sale.Id | Void/Refund | BusinessId+Sale+Sale+SaleId | Phase 2
5. Sales discount | Sale completed | Sales Discount | Sales | SaleDateTime | Sale.BusinessDate | Sale | Sale.Id | Refund/Void | BusinessId+Sale+Sale+SaleId | Phase 2
6. Full return | Refund completed | Sales Return | Cash/Bank/Clearing | RefundTime | Refund business date | SaleRefund | RefundId or SaleId | Reverse refund | BusinessId+SaleReturn+SaleRefund+RefId | Phase 2
7. Partial return | Refund completed | Sales Return (proportional) | Cash/Bank/Clearing | RefundTime | Refund business date | SaleRefund | RefundId | Reverse refund | BusinessId+SaleReturn+SaleRefund+RefId | Phase 2
8. Purchase receipt through GRN | GRN completed | Inventory | Supplier Payable or Cash/Bank | Grn.ReceivedDate | GRN business date | Grn | Grn.Id | Return/correction | BusinessId+PurchaseReceipt+Grn+GrnId | Phase 2
9. Immediate paid purchase | Purchase receive completed | Inventory | Cash/Bank | Receive time | Receive business date | PurchaseReceive | PoId/GrnId | correction | BusinessId+PurchaseReceipt+PurchaseReceive+RefId | Phase 2
10. Supplier payment | Payment approved | Supplier Payable | Cash/Bank | PaymentDate | Payment business date | SupplierPayment | Payment.Id | payment reversal | BusinessId+SupplierPayment+SupplierPayment+Id | Phase 2
11. Supplier advance | Payment approved unallocated | Supplier Advance | Cash/Bank | PaymentDate | Payment business date | SupplierPayment | Payment.Id | reallocation/reversal | BusinessId+SupplierAdvance+SupplierPayment+Id | Phase 2
12. Expense | Expense approved | Expense Account | Cash/Bank/Payable | ExpenseDate | Expense business date | Expense | Expense.Id | expense reverse/cancel | BusinessId+Expense+Expense+ExpenseId | Phase 1
13. Other income | Income approved | Cash/Bank | Other Income | IncomeDate | Income business date | OtherIncome | Income.Id | cancel/reverse | BusinessId+OtherIncome+OtherIncome+IncomeId | Phase 1
14. Stock shortage | Adjustment approved | Inventory Shortage Expense | Inventory | ApproveDate | Stock count business date | StockAdjustment | Adjustment.Id | reversal adjustment | BusinessId+StockAdjustment+StockAdjustment+Id | Phase 2
15. Stock gain | Adjustment approved | Inventory | Inventory Gain | ApproveDate | Stock count business date | StockAdjustment | Adjustment.Id | reversal adjustment | BusinessId+StockAdjustment+StockAdjustment+Id | Phase 2
16. Cash-to-bank transfer | Transfer approved | Bank | Cash | TransferDate | transfer business date | CashTransfer | Transfer.Id | reversal transfer | BusinessId+CashTransfer+CashTransfer+Id | Phase 2
17. Bank-to-cash transfer | Transfer approved | Cash | Bank | TransferDate | transfer business date | CashTransfer | Transfer.Id | reversal transfer | BusinessId+CashTransfer+CashTransfer+Id | Phase 2
18. Capital injection | Capital entry approved | Cash/Bank | Owner Capital | TxnDate | business date | Capital | Capital.Id | reverse | BusinessId+Capital+Capital+Id | Phase 2
19. Owner withdrawal | Drawings approved | Owner Drawings | Cash/Bank | TxnDate | business date | Drawings | Drawings.Id | reverse | BusinessId+Drawings+Drawings+Id | Phase 2
20. Cash closing surplus | Session close approved, positive diff | Cash | Cash Difference Gain | ClosedAt | Session.BusinessDate | CashSession | Session.Id | correction | BusinessId+CashCloseDiff+CashSession+SessionId | Phase 2
21. Cash closing shortage | Session close approved, negative diff | Cash Difference Loss | Cash | ClosedAt | Session.BusinessDate | CashSession | Session.Id | correction | BusinessId+CashCloseDiff+CashSession+SessionId | Phase 2

---

## 7. Transaction Boundary Plan

### 7.1 Required atomic boundaries

1. Sale + inventory + stock ledger + journal posting
2. GRN + inventory + stock ledger + journal posting
3. Stock adjustment approval + inventory + stock ledger + journal posting
4. Cash session close + expected/actual difference + journal posting
5. Expense approval + expense status update + journal posting
6. Other income approval + status update + journal posting

### 7.2 Architecture support assessment

Current architecture supports same-transaction posting because:

1. Existing services already use DbContext transactions.
2. Posting service can write using same DbContext scope.
3. Existing stock ledger pattern already follows transaction-bound writes.

### 7.3 Retry and duplicate protection

1. Enforce DB-level unique idempotency on journal headers.
2. Posting service must be retry-safe and return existing posted journal on duplicate key detection.

### 7.4 Failure behavior

1. Any posting failure throws and rolls back operational transaction.
2. No partial operational-complete/accounting-missing state allowed.

---

## 8. Inventory and COGS Plan

### 8.1 Confirmed current method

Current implementation uses primarily base product cost plus variant adjustment in report/service logic.

### 8.2 Phase-safe COGS strategy

1. Do not recalculate COGS from selling price.
2. Capture cost snapshot per sale line at posting time.
3. Reverse COGS on returns using original sale cost snapshot when possible.
4. Maintain variant-level cost basis.

### 8.3 Historical consistency protection

1. Journal lines or linked posting metadata should store unit cost used.
2. Avoid historical report recomputation from mutable master cost values.

### 8.4 Risks and mitigation

Risk:

1. Profit and valuation mismatch if mutable cost fields are used for historic periods.

Mitigation:

1. Cost snapshot fields in posting payload.
2. Clear rule hierarchy: original sale cost snapshot > fallback current effective cost with audit note.

---

## 9. Permission Matrix

Role strategy decision for Phase 1:

1. Use existing AccountsAdmin role for accountant responsibilities.

### 9.1 Permission groups to add

1. Accounting.Dashboard.View
2. Accounting.ChartOfAccounts.View
3. Accounting.ChartOfAccounts.Create
4. Accounting.ChartOfAccounts.Update
5. Accounting.Configuration.View
6. Accounting.Configuration.Update
7. Accounting.Journal.View
8. Accounting.Journal.Create
9. Accounting.Journal.Post
10. Accounting.Journal.Reverse
11. Accounting.ManualJournals.View
12. Accounting.ManualJournals.Create
13. Accounting.ManualJournals.Update
14. Accounting.ManualJournals.Submit
15. Accounting.ManualJournals.Approve
16. Accounting.ManualJournals.Reject
17. Accounting.ManualJournals.Cancel
18. Accounting.ManualJournals.Post
19. Accounting.ManualJournals.Reverse
20. Accounting.Expense.View
21. Accounting.Expense.Create
22. Accounting.Expense.Update
23. Accounting.Expense.Submit
24. Accounting.Expense.Approve
25. Accounting.Expense.Reject
26. Accounting.Expense.Cancel
27. Accounting.Income.View
28. Accounting.Income.Create
29. Accounting.Income.Update
30. Accounting.Income.Cancel
31. Accounting.Reports.ProfitLoss
32. Accounting.Reports.TrialBalance
33. Accounting.Reports.GeneralLedger
34. Accounting.Reports.BalanceSheet
35. Accounting.Reports.CashBook
36. Accounting.Reports.Expense
37. Accounting.Reports.Income
38. Accounting.Reports.COGS
39. Accounting.Reports.InventoryValuation
40. Accounting.Reports.CashClosing
41. Accounting.FinancialYear.View
42. Accounting.FinancialYear.Manage
43. Accounting.Period.Close
44. Accounting.Period.Reopen
45. Accounting.OpeningBalances.Post
46. Accounting.OpeningBalances.Adjust

Canonicalization decision:

1. Implementation will use lower-case dot-separated keys as canonical values, for example `accounting.journals.post`.
2. The canonical registry is the backend permission catalog.
3. Role validation, SQL seeds, onboarding defaults, frontend route guards, menu visibility, and role-form groupings must all consume the same canonical vocabulary.
4. New accounting permissions must be added only through the canonical registry first, then propagated outward.
5. Avoid mixed casing or duplicate alias permissions for accounting modules.
6. RM-07 must use the dedicated `accounting.manual_journals.*` permission family for manual-journal actions and must not introduce overlapping legacy permissions.

### 9.2 Role vs access summary

1. Super Admin: full platform access; business context required for financial operations.
2. Business Owner: full business accounting access.
3. AccountsAdmin: full accounting module access for business.
4. Warehouse Manager: inventory and relevant operational reports; no manual journal by default.
5. Outlet Manager: outlet-limited expense/cash/approved report views.
6. Cashier: session operations only; no chart/journal/financial statements.
7. SalesPerson: no accounting pages and no manual posting.

Outlet scope and business scope controls must stay server-side using current access services.

---

## 10. API Plan

All accounting v2 APIs should be namespaced under /api/accounting/*.

### 10.1 Chart of Accounts

1. GET /api/accounting/chart-accounts
- Purpose: list with filters and hierarchy
- Permission: Accounting.ChartOfAccounts.View
- Phase: 1

2. POST /api/accounting/chart-accounts
- Purpose: create account
- Permission: Accounting.ChartOfAccounts.Create
- Validation: unique code, valid type
- Phase: 1

3. PUT /api/accounting/chart-accounts/{id}
- Purpose: update account
- Permission: Accounting.ChartOfAccounts.Update
- Validation: prevent invalid disable/delete behavior
- Phase: 1

### 10.2 Accounting Configuration

1. GET /api/accounting/configuration
- Permission: Accounting.Configuration.View
- Phase: 1

2. PUT /api/accounting/configuration
- Permission: Accounting.Configuration.Update
- Validation: mapped account ownership and active status
- Phase: 1

3. GET/PUT /api/accounting/configuration/outlets/{outletId}
- Permission: Accounting.Configuration.Update
- Phase: 1

### 10.3 Journals and Manual Journals

1. GET /api/accounting/journals
- Permission: Accounting.Journal.View
- Phase: 1

2. GET /api/accounting/journals/{id}
- Permission: Accounting.Journal.View
- Phase: 1

3. POST /api/accounting/journals
- Purpose: create draft manual journal
- Permission: Accounting.Journal.Create and Accounting.ManualJournals.Create
- Validation: line structure and balance
- Phase: 1

Manual journal create policy:

1. Narration/description is mandatory.
2. External reference number is optional and not unique.
3. Reference type is optional.
4. Reference ID is optional.
5. Attachments are supported.
6. Minimum lines: 2.
7. Maximum lines: 200.
8. Only accounts with `AllowManualPosting = true` may be used.

4. POST /api/accounting/journals/{id}/post
- Permission: Accounting.Journal.Post or Accounting.ManualJournals.Post
- Validation: period open, mapping valid, balanced
- Phase: 1

Manual journal workflow actions required by RM-07:

1. `POST /api/accounting/journals/{id}/submit`
- Permission: Accounting.ManualJournals.Submit
- Used only when approval workflow mode is enabled

2. `POST /api/accounting/journals/{id}/approve`
- Permission: Accounting.ManualJournals.Approve
- Used only when approval workflow mode is enabled
- Maker-checker policy applies

3. `POST /api/accounting/journals/{id}/reject`
- Permission: Accounting.ManualJournals.Reject
- Used only when approval workflow mode is enabled
- Reason is required

4. `POST /api/accounting/journals/{id}/cancel`
- Permission: Accounting.ManualJournals.Cancel

5. `PUT /api/accounting/journals/{id}`
- Permission: Accounting.ManualJournals.Update or Accounting.Journal.Update
- Editable only while the journal is still in a pre-posted editable state allowed by workflow mode

5. POST /api/accounting/journals/{id}/reverse
- Permission: Accounting.Journal.Reverse or Accounting.ManualJournals.Reverse
- Validation: reason required, already posted, no double reversal
- Phase: 1

Numbering behavior for journal APIs:

1. Draft journals must not receive permanent journal numbers.
2. Permanent journal numbers are allocated only when a journal is posted.
3. Abandoned drafts therefore do not consume permanent sequence values.
4. Reversal journals consume new permanent sequential numbers at the moment the reversal is posted.
5. Draft UI must use draft identity separate from final journal number.
6. Final numbering behavior must remain consistent across API, reports, audit views, and printed output.

### 10.4 Financial years and periods

1. GET /api/accounting/financial-years
- Permission: Accounting.FinancialYear.View
- Phase: 1

2. POST/PUT /api/accounting/financial-years
- Permission: Accounting.FinancialYear.Manage
- Phase: 1

3. POST /api/accounting/periods/{id}/close
- Permission: Accounting.Period.Close
- Phase: 1

4. POST /api/accounting/periods/{id}/reopen
- Permission: Accounting.Period.Reopen
- Phase: 1

Governance rules:

1. `Open` means normal posting allowed.
2. `Closed` means routine posting blocked; reopen allowed only by authorized users.
3. `Locked` means hard stop for posting and should represent stronger administrative freeze than `Closed`.
4. Posting service must resolve financial year and accounting period from posting date before allowing post.
5. Draft creation may be allowed before post even if final target period will later be blocked, but post must always validate current state.
6. Posted journals must carry resolved FinancialYearId and AccountingPeriodId whenever matching records exist.
7. BusinessDate and PostingDate rules must be validated explicitly once financial year APIs are implemented.

### 10.5 Expense v2

1. GET /api/accounting/expenses
- Permission: Accounting.Expense.View
- Phase: 1

2. POST /api/accounting/expenses
- Permission: Accounting.Expense.Create
- Phase: 1

3. PUT /api/accounting/expenses/{id}
- Permission: Accounting.Expense.Update
- Phase: 1

4. POST /api/accounting/expenses/{id}/submit
- Permission: Accounting.Expense.Submit
- Phase: 1

5. POST /api/accounting/expenses/{id}/approve
- Permission: Accounting.Expense.Approve
- Phase: 1

6. POST /api/accounting/expenses/{id}/reject
- Permission: Accounting.Expense.Reject
- Phase: 1

7. POST /api/accounting/expenses/{id}/cancel
- Permission: Accounting.Expense.Cancel
- Phase: 1

### 10.6 Other income

1. GET /api/accounting/incomes
- Permission: Accounting.Income.View
- Phase: 1

2. POST /api/accounting/incomes
- Permission: Accounting.Income.Create
- Phase: 1

3. PUT /api/accounting/incomes/{id}
- Permission: Accounting.Income.Update
- Phase: 1

4. POST /api/accounting/incomes/{id}/cancel
- Permission: Accounting.Income.Cancel
- Phase: 1

### 10.7 Reports

1. GET /api/accounting/reports/general-ledger
- Permission: Accounting.Reports.GeneralLedger
- Phase: 1

2. GET /api/accounting/reports/trial-balance
- Permission: Accounting.Reports.TrialBalance
- Phase: 1

3. GET /api/accounting/reports/profit-loss
- Permission: Accounting.Reports.ProfitLoss
- Phase: 1

Balance sheet, cash book, COGS, inventory valuation, cash closing reports are in Phase 2/3 as per dependencies.

---

## 11. UI Plan

### 11.1 New accounting menu structure

Add Accounts module menu entries:

1. Accounting Dashboard
2. Chart of Accounts
3. Expenses
4. Other Income
5. Journal Entries
6. Manual Journal
7. Cash and Bank Transfers
8. Financial Years
9. Accounting Configuration
10. Reports

### 11.2 Phase 1 pages

1. Chart of Accounts list and editor
2. Accounting configuration (business + outlet override)
3. Journal list and detail
4. Manual journal create/edit/post/reverse
5. Manual journal submit/approve/reject/cancel when approval mode is enabled
5. Financial year and period pages
6. Expense v2 list/detail/workflow
7. Other income list/detail/workflow
8. Reports pages: GL, Trial Balance, P&L

### 11.3 UX standards and constraints

1. Enforce list-state preservation for all new list/report pages.
2. Enforce filter standards (basic + advanced drawer where needed).
3. Use route guards and permission checks consistent with current app.
4. Ensure outlet-level data restriction in all queries and data sources.

### 11.4 Legacy coexistence UX

1. Existing finance pages remain available during transition.
2. Mark legacy screens as transitional to avoid user confusion.

---

## 12. Reporting Plan

Journal ledger is the single source of financial truth.

### 12.1 Profit and Loss (Phase 1 basic)

1. Data source: journal_entries + journal_entry_lines + chart_accounts
2. Structure: Sales - Sales Return - Sales Discount = Net Sales, then COGS, Other Income, Expenses
3. Filters: date range, business date, outlet, account, expense category, comparison period
4. Export: CSV/XLSX

### 12.2 Trial Balance (Phase 1)

1. Data source: journal lines grouped by account
2. Opening + period + closing logic
3. Constraint: total debit == total credit

### 12.3 General Ledger (Phase 1)

1. Data source: account-filtered journal lines with header context
2. Running balance based on normal balance type
3. Filters: account, date range, outlet, journal type, reference type

### 12.4 Balance Sheet (Phase 3)

1. Data source: ledger balances by Asset/Liability/Equity as-of date

### 12.5 Cash Book (Phase 2)

1. Data source: journals affecting mapped cash accounts
2. Include outlet/terminal/cash session context

### 12.6 Expense and Other Income reports

1. Expense report views: summary, details, category, outlet, payment method
2. Other income filters: date, outlet, category, payment method

### 12.7 COGS report and inventory valuation

1. COGS report from sale-linked cost journal data and snapshots
2. Inventory valuation from inventory quantities with approved costing source policy

### 12.8 Performance

1. Use indexed date/account/business filters
2. Paginate large detail reports
3. Stream export for large result sets

---

## 13. Historical Data and Cutover Plan

Recommended and selected strategy:

1. Set accounting start date
2. Enter approved opening balances as balanced journal
3. Auto-post all new transactions after start date

### 13.1 Opening balances scope

1. Opening balance baseline is business-level.
2. Cash opening balances may be tracked per outlet where applicable.
3. Bank opening balances may be tracked per business bank account where multiple accounts exist.
4. Inventory opening balances are tracked per warehouse/location ownership.
5. Supplier payables and supplier advances are supported as opening-balance classes.
6. Customer advances are supported when customer-advance feature/mapping is enabled.
7. Owner capital is supported; owner drawings are supported when applicable.
8. Additional balance-sheet accounts may be included as required.
9. Income and expense accounts must never accept opening balances.

### 13.2 Pre-start behavior

1. Financial reports from journal should clearly indicate start-date boundary.
2. Historical operational reports remain available as-is.

### 13.3 Backfill policy

1. No automatic historical backfill in initial rollout.
2. Controlled backfill may be considered later if required.

---

## 14. Testing Plan

### 14.1 Unit tests

1. Journal line validation
2. Journal balancing
3. Posting idempotency resolution
4. Reversal rules
5. Period lock checks
6. Mapping resolution and fallback behavior

### 14.2 Integration tests

1. Expense approval posting
2. Other income posting
3. Manual journal lifecycle
4. Opening balance posting
5. Report aggregation correctness

### 14.3 Permission tests

1. Accounting.* endpoint authorization by role
2. Outlet-scope restrictions
3. Business-scope isolation

### 14.4 Phase 2/3 deferred tests prepared now

1. Sale cash/card/split journal balancing
2. Full/partial return journals
3. GRN and immediate purchase postings
4. Supplier payment and advance postings
5. Stock shortage/gain postings
6. Cash closing surplus/shortage postings
7. Duplicate retry no duplicate journal

### 14.5 Regression and reconciliation

1. Trial balance debit/credit equality
2. P&L totals match ledger aggregates
3. Inventory valuation consistency checks
4. COGS consistency checks

---

## 15. Risks and Open Questions

### 15.1 Business-rule ambiguities

1. Backdated posting rule precedence when period is open but cash session date differs
2. Treatment of damaged returns not restocked
3. Supplier advance allocation rules and approval authority

### 15.2 Technical risks

1. Legacy financial writes and v2 writes in parallel
2. Sequential journal number allocation contention or gap misunderstanding
3. Timezone and business-date cross-midnight behavior

### 15.3 Data migration risks

1. Incorrect opening balances produce systemic reporting errors
2. Incomplete account mapping blocks posting

### 15.4 Performance risks

1. Ledger report scans without proper indexes on large data volume
2. Export memory pressure for long date ranges

### 15.5 Security risks

1. Missing scope filters in financial report endpoints
2. Permission drift between API, UI, and seed definitions

### 15.8 Additional approved recommendations

1. Keep journal metadata complete even for manual journals: BusinessId, OutletId, WarehouseId nullable, TerminalId nullable, CashSessionId nullable, BusinessDate, TransactionDate, PostingDate, FinancialYearId, AccountingPeriodId, ReferenceType, ReferenceId, ReferenceNumber.
2. Treat BusinessDate as logically required for all posted accounting-v2 journals, even if draft-time schema keeps it nullable for compatibility.
3. Period-governance API work should be completed before expense/other-income posting milestones.
4. Permission synchronization should be validated by targeted regression tests before frontend accounting navigation is activated.
5. RM-07 manual-journal workflow mode and maker-checker behavior are business-configurable through Accounting Configuration.
6. RM-07 manual-journal attachments are part of the approved design and must be audit-visible from Journal Details.

### 15.6 Existing defects to address before Phase 1 activation

1. TransactionService currently updates Account.Balance directly
2. Legacy account balance and computed ledger balance can diverge

### 15.7 Decisions required before development

1. Legacy endpoints write policy after v2 rollout per business
2. Backdated posting behavior and permission semantics
3. Cost snapshot fallback behavior when original sale-cost context is missing

---

## 16. Micro-Task Breakdown (Phase 1)

Task 1
- Task Title: Accounting Foundation Schema and Contracts
- Objective: add minimum new accounting schema and contracts
- Backend Work: entities, enums, DTOs, service interfaces, policy constants
- Frontend Work: none
- Database Work: migration design for new tables and constraints
- Permission Work: add Accounting.* permissions and seed updates
- Testing Work: entity validation tests
- Dependencies: none
- Acceptance Criteria: compile-safe foundation, no operational behavior change
- Estimated Complexity: Large
- Implementation Order: 1

Task 2
- Task Title: Journal Engine Core
- Objective: implement draft/post/reverse/idempotent journal lifecycle
- Backend Work: journal service, posting primitives, sequential journal numbering allocator integration
- Frontend Work: none
- Database Work: idempotency and journal indexes, accounting number sequence support if approved in migration set
- Permission Work: journal and manual journal policies
- Testing Work: balancing, immutability, reversal, retry tests
- Dependencies: Task 1
- Acceptance Criteria: robust journal lifecycle API
- Estimated Complexity: Large
- Implementation Order: 2

Task 3
- Task Title: Chart and Configuration Module
- Objective: operational mapping and account governance
- Backend Work: chart and mapping services/controllers
- Frontend Work: chart/config pages
- Database Work: chart and mapping tables
- Permission Work: chart/config permissions
- Testing Work: code uniqueness and mapping validation tests
- Dependencies: Task 1
- Acceptance Criteria: mapping completeness validation works
- Estimated Complexity: Large
- Implementation Order: 3

Task 4
- Task Title: Financial Year and Period Controls
- Objective: period gatekeeping for postings
- Backend Work: year/period services + posting guard integration + explicit open/close/lock/reopen governance audit
- Frontend Work: year/period management screens
- Database Work: year/period governance fields if required
- Permission Work: period close/reopen permissions
- Testing Work: closed/locked period tests
- Dependencies: Task 2
- Acceptance Criteria: blocked posting into closed/locked periods
- Estimated Complexity: Medium
- Implementation Order: 4

Task 5
- Task Title: Opening Balance Workflow
- Objective: safe cutover entry point
- Backend Work: opening journal validation/post endpoints
- Frontend Work: opening balance wizard/page
- Database Work: none beyond journal tables
- Permission Work: journal post + year manage controls
- Testing Work: balanced opening journal and one-time control tests
- Dependencies: Tasks 2, 3, 4
- Acceptance Criteria: approved opening balances posted and auditable
- Estimated Complexity: Medium
- Implementation Order: 5

Task 6
- Task Title: Expense v2 Workflow and Posting
- Objective: draft-submit-approve workflow with posting at approval
- Backend Work: expense entity/service/controller enhancements
- Frontend Work: expense workflow updates
- Database Work: expense table extension
- Permission Work: Accounting.Expense.*
- Testing Work: workflow and posting tests
- Dependencies: Tasks 2, 3, 4
- Acceptance Criteria: only approved expense posts journal
- Estimated Complexity: Medium
- Implementation Order: 6

Task 7
- Task Title: Other Income Module
- Objective: source-doc-driven income posting
- Backend Work: other income module
- Frontend Work: other income screens
- Database Work: other_income_entries
- Permission Work: Accounting.Income.*
- Testing Work: posting and permission tests
- Dependencies: Tasks 2, 3, 4
- Acceptance Criteria: approved income posts journal
- Estimated Complexity: Medium
- Implementation Order: 7

Task 8
- Task Title: Manual Journal UX/API
- Objective: controlled manual postings for authorized users
- Backend Work: manual endpoints on journal engine, configurable workflow mode, maker-checker enforcement, attachment handling
- Frontend Work: manual journal create/update/submit/approve/reject/cancel/post/reverse
- Database Work: none beyond journal model
- Permission Work: Accounting.ManualJournals.*
- Testing Work: allow-manual-posting enforcement, maker-checker, workflow-mode branching, attachment auditability, line-limit validation
- Dependencies: Tasks 2, 3
- Acceptance Criteria:
  - configurable workflow supports both `Draft -> Post` and `Draft -> Submit -> Approve / Reject -> Post`
  - maker cannot approve own journal unless explicit self-approval bypass is enabled for Business Owner or Super Admin in Accounting Configuration
  - narration is mandatory and external reference metadata remains optional/non-unique
  - attachments support upload/delete/download with audit visibility
  - manual journals enforce minimum 2 and maximum 200 lines
  - manual-journal permissions are enforced without introducing overlapping legacy permissions
- Estimated Complexity: Medium
- Implementation Order: 8

Task 9
- Task Title: GL/TB/P&L Reports (Phase 1)
- Objective: financial reporting baseline
- Backend Work: report services/controllers
- Frontend Work: report screens + exports
- Database Work: report indexes review and tuning
- Permission Work: Accounting.Reports.*
- Testing Work: reconciliation tests
- Dependencies: Tasks 2, 3, 4
- Acceptance Criteria: reports reconcile with journals
- Estimated Complexity: Large
- Implementation Order: 9

Task 10
- Task Title: Legacy Coexistence and Stabilization Gate
- Objective: ensure no regression while v2 rolls out
- Backend Work: feature flags and compatibility policy
- Frontend Work: legacy vs v2 navigation strategy
- Database Work: none
- Permission Work: synchronized role assignments
- Testing Work: regression suites for existing modules
- Dependencies: Tasks 1 to 9
- Acceptance Criteria: stable coexistence with rollback path
- Estimated Complexity: Medium
- Implementation Order: 10

---

## 17. Recommended Implementation Sequence

### 17.1 Foundation tasks

1. Task 1
2. Task 2
3. Task 3
4. Task 4
5. Task 5

### 17.2 Parallelizable tasks (after foundation)

1. Task 6 and Task 7 can run in parallel.
2. Task 8 can run in parallel once Task 2 and Task 3 are stable.

### 17.3 Blocking dependencies

1. No source-document posting (expense/income/manual) before journal engine and mapping are stable.
2. No reporting before posting and period controls are complete.
3. No Phase 2 integrations before Phase 1 reconciliation gate passes.

### 17.4 High-risk integration checkpoints

1. Checkpoint A: journal immutability and idempotency behavior
2. Checkpoint B: period lock and backdated rule behavior
3. Checkpoint C: mapping resolution failure handling and user messaging
4. Checkpoint D: report reconciliation against sample datasets

### 17.5 Migration checkpoints

1. Migration Set A: core accounting schema, including number-sequence support if numbering is finalized before journal rollout
2. Migration Set B: expense extension + other income
3. Migration Set C: reporting/performance indexes

### 17.6 Test checkpoints

1. Build gate after each migration set
2. Integration gate after each service milestone
3. Permission gate before frontend route/menu activation

---

## Readiness Assessment

The plan is ready for Phase 1 implementation approval.

Strengths:

1. Uses existing service transaction boundaries and avoids major module refactors.
2. Introduces only necessary Phase 1 tables and abstractions.
3. Preserves future extension path for AP/AR/VAT/reconciliation without breaking journal contracts.
4. Includes explicit idempotency, immutability, and period-control safeguards.

Remaining pre-implementation decisions:

1. Legacy endpoint write behavior during transition.
2. Final backdated posting policy.
3. Cost snapshot fallback rule for edge-case returns.
4. Permanent journal numbers are allocated at post time only; no additional decision remains on draft numbering behavior.

Approved design readiness update:

1. Journal metadata coverage is sufficient, with BusinessDate/FinancialYearId/AccountingPeriodId treated as posting-time required fields.
2. Financial year and period model is conceptually sufficient, but governance audit fields and numbering support should be finalized before more journal behavior is expanded.
3. Permission model is extensible if synchronization is enforced from the canonical backend registry.
4. Accounting foundation remains ready to continue journal-engine work once sequential numbering support and permission propagation are aligned.
5. Opening-balance date policy is fixed: opening balance business date and posting date equal accounting start date; adjustment business date follows adjustment date and adjustment posting date follows actual posting date.
6. Opening-balance adjustments must be rejected in closed/locked periods and may proceed only in open periods with authorized adjustment permission.

## Recommended Phase 1 Scope (Final)

1. Accounting foundation schema and policies
2. Journal engine with idempotency and reversal
3. Chart and account mapping
4. Financial years and periods
5. Opening balances
6. Expense v2 and other income
7. Manual journal
8. GL/TB/P&L
9. Coexistence hardening and regression sign-off

## 18. Remaining Phase 1 Milestones (Post Journal Engine Acceptance)

This section supersedes the earlier execution order for remaining Phase 1 work and breaks the backlog into smaller reviewable milestones.

Assumption baseline:

1. Journal Engine milestone is accepted and considered closed.
2. Remaining work starts from accepted journal contracts, permissions, outlet-scope authorization, and idempotency behavior.

### 18.1 Milestone matrix

Milestone RM-01
- Milestone Name: Chart of Accounts Hardening and Governance
- Scope:
  - finalize chart-account CRUD behavior and invariants
  - enforce parent-child hierarchy validation and cycle prevention
  - enforce system/control/manual-posting flag governance
  - complete account deactivation safety checks when accounts are referenced
  - enforce posted-account immutability governance:
    - accounts referenced by posted journals must never be merged
    - account codes for posted accounts must never be changed
    - account type must never change after a posted journal exists
    - system accounts remain immutable except explicitly approved fields/actions
- Dependencies:
  - accepted accounting foundation schema
  - accepted journal engine contracts
- Acceptance Criteria:
  - account code uniqueness and hierarchy rules are enforced server-side
  - accounts referenced by posted journals cannot be invalidly mutated/deleted
  - posted-account immutability constraints (merge/code/type) are enforced and test-covered
  - system-account immutability policy is explicitly codified and test-covered
  - permission gates for chart account operations are fully aligned across API and UI
  - targeted unit/integration tests pass for hierarchy and protection rules
- Estimated Complexity: Medium

Milestone RM-02
- Milestone Name: Accounting Configuration and Outlet Overrides
- Scope:
  - complete business-level accounting mapping endpoints and validation
  - complete outlet-level override mapping with fallback behavior
  - validate mapped accounts are active, same-business, and type-compatible
  - add clear validation/error messages for incomplete mapping states
- Dependencies:
  - RM-01
- Acceptance Criteria:
  - configuration can be read/updated per business and per outlet
  - posting preconditions fail fast when required mappings are missing
  - override fallback behavior is deterministic and test-covered
  - permission and role exposure are synchronized with canonical accounting permissions
- Estimated Complexity: Medium

Milestone RM-04
- Milestone Name: Opening Balance Workflow
- Scope:
  - implement opening balance input and validation flow
  - enforce balanced opening journal generation and one-time governance rules
  - validate opening balances as the first real accounting transaction against accepted journal and chart contracts
  - bind opening balance posting to currently available financial-year foundation; RM-03 may extend governance later without RM-04 redesign
  - provide audit and traceability for opening entries
  - enforce approved opening-balance granularity:
    - opening balance is maintained at business level as the primary baseline
    - cash opening balances may be dimensioned per outlet
    - bank opening balances may be dimensioned per business bank account
    - inventory opening balances are dimensioned per warehouse/location ownership
    - supplier/customer/capital/equity and other balance-sheet accounts remain business-level unless future requirements extend this
  - enforce approved minimum opening-balance coverage and account constraints:
    - required support: cash, bank, inventory, supplier payables, supplier advances, customer advances (when enabled), owner capital, owner drawings (when applicable), and additional balance-sheet accounts
    - income and expense accounts must never accept opening-balance entries
  - enforce opening-balance lifecycle governance:
    - opening balance is intended to be the first accounting transaction for a business
    - lock trigger is the first successfully posted operational accounting journal
    - operational journals include POS sale, purchase/GRN, expense, other income, manual journal, supplier payment, inventory adjustment, cash closing, and future operational posting types
    - opening balance and opening balance adjustment journals do not trigger opening-balance lock
    - after lock, original opening balance cannot be edited, cannot be deleted, and new opening-balance entries cannot be created
    - correction path is only an authorized Opening Balance Adjustment Journal
    - opening balance adjustment requires dedicated journal type and dedicated permission
    - opening balance adjustment requires mandatory reason, full audit trail, and bidirectional linkage to original opening-balance journal
    - original opening balance cannot be directly modified or deleted
    - all opening balance adjustments must be fully audited
  - enforce opening-balance and adjustment date policy:
    - opening balance business date equals accounting start date
    - opening balance posting date equals accounting start date
    - opening balance adjustment business date follows adjustment business date
    - opening balance adjustment posting date follows actual posting date
    - closed/locked periods must reject opening-balance adjustments
    - open periods may allow authorized opening-balance adjustments by accounting permission
- Dependencies:
  - RM-02
  - accepted journal engine reversal/audit controls
  - current financial year foundation (full RM-03 completion is not required before RM-04 starts)
- Acceptance Criteria:
  - opening balance can be posted only through controlled workflow
  - generated journals are balanced and immutable after post
  - one-time or governed re-entry behavior is enforced per business policy
  - automatic lock is triggered once first operational accounting journal is posted
  - direct edit/delete of original opening balance is blocked
  - locked state blocks creation of new opening-balance entries
  - opening-balance coverage supports required balance-sheet classes and rejects income/expense accounts
  - opening-balance adjustment journals require dedicated permission, reason, linkage, and audit completeness
  - opening-balance and adjustment date-policy rules are enforced with period-state checks
  - adjustment journals are authorization-gated and audit-complete
  - opening balance test suite passes (positive and rejection paths)
- Estimated Complexity: Medium

Milestone RM-03
- Milestone Name: Financial Year and Period Governance APIs
- Scope:
  - implement/complete financial year lifecycle APIs
  - implement/complete period lifecycle APIs including open/closed/locked/reopen behaviors
  - enforce period governance audit fields and reason capture where applicable
  - finalize posting-date and business-date governance checks against period state
- Dependencies:
  - RM-04
- Acceptance Criteria:
  - year/period management endpoints enforce authorization and business isolation
  - posting into closed/locked periods is blocked consistently
  - reopen and lock transitions are auditable and deterministic
  - period-governance integration tests pass
- Estimated Complexity: Medium

Milestone RM-05
- Milestone Name: Expense v2 Workflow and Posting
- Scope:
  - implement Expense Category Management with default expense-account mapping
  - complete draft-submit-approve-reject-cancel expense lifecycle
  - post accounting journal only at approved state
  - determine posting account automatically from expense-category mapping by default
  - allow manual posting-account override only for authorized accounting users
  - enforce account mapping and period checks at approval time
  - align UI workflow states, actions, and permission visibility
- Dependencies:
  - RM-01
  - RM-02
  - RM-03
  - RM-04
- Acceptance Criteria:
  - only approved expenses create posted journals
  - rejected/cancelled expenses do not create posted journals
  - each active expense category has a valid mapped account before approval is allowed
  - expense approval auto-resolves posting account from category mapping when override is not authorized
  - manual account selection is restricted to authorized accounting users and is audit-visible
  - journal references are traceable back to expense source document
  - workflow, permission, and posting integration tests pass
- Estimated Complexity: Medium

Milestone RM-06
- Milestone Name: Other Income Workflow and Posting
- Scope:
  - implement/complete other-income source-document workflow
  - enforce create/update/cancel rules and posting controls
  - post journals using approved configuration and period governance
  - ensure traceability and reversibility behavior is policy-compliant
- Dependencies:
  - RM-02
  - RM-03
  - RM-04
- Acceptance Criteria:
  - other-income entries produce correct posted journals under valid conditions
  - cancelled or invalid entries do not post
  - reporting source traceability is preserved through reference metadata
  - permission and integration tests pass
- Estimated Complexity: Medium

Milestone RM-07
- Milestone Name: Manual Journal UX and Operational Controls
- Scope:
  - complete manual-journal UI workflow aligned to accepted journal API behavior
  - enforce client UX constraints for draft/update/post/reverse/cancel flows
  - support configurable business workflow modes:
    - Mode A: `Draft -> Post`
    - Mode B: `Draft -> Submit -> Approve / Reject -> Post`
  - enforce maker-checker policy when approval mode is enabled
  - implement attachment handling and audit-visible journal-detail access
  - implement high-signal validation UX for line balancing and account eligibility
  - add role-based visibility and action controls for manual journal operations
- Dependencies:
  - accepted journal engine milestone
  - RM-01
  - RM-02
  - RM-03
- Acceptance Criteria:
  - authorized users can complete the full manual journal lifecycle allowed by configured workflow mode from UI
  - unauthorized actions are blocked in both route and action level checks
  - maker-checker restrictions are enforced correctly, including self-approval bypass only when explicitly enabled in Accounting Configuration
  - draft identity and final journal numbering behavior match backend rules
  - attachments are available from Journal Details and upload/delete/download actions are fully audited
  - minimum 2 lines and maximum 200 lines are enforced with standard Journal Engine validation
  - mandatory narration is enforced and external reference metadata remains optional/non-unique
  - end-to-end tests pass for core manual journal user journeys
- Estimated Complexity: Medium
- Status: Completed
- Acceptance Evidence: See [Docs/10_Accounts_And_Finance/RM-07_MANUAL_JOURNAL_ACCEPTANCE_CHECKLIST.md](Docs/10_Accounts_And_Finance/RM-07_MANUAL_JOURNAL_ACCEPTANCE_CHECKLIST.md)

Milestone RM-08
- Milestone Name: Phase 1 Financial Reports (GL, Trial Balance, Profit and Loss)
- Scope:
  - enforce General Ledger as the single financial source of truth
  - implement report APIs and filters for GL, TB, and P&L
  - implement corresponding UI pages with list/filter/export standards
  - reconcile report outputs against posted journal data and sample datasets
  - enforce report computation rules:
    - TB, P&L, and future Balance Sheet are generated exclusively from posted journal entries
    - operational tables (Sale, Expense, Purchase, Inventory, etc.) are drill-down/reference only
    - operational tables must never independently compute financial balances
  - tune report query/index paths for expected Phase 1 volumes
- Dependencies:
  - RM-01
  - RM-02
  - RM-03
  - RM-05
  - RM-06
  - RM-07
- Acceptance Criteria:
  - GL, TB, and P&L endpoints return correct aggregated results
  - all financial balances in GL/TB/P&L are derived from posted journal lines only
  - operational-source drill-down links reconcile to journal references without introducing alternate balance calculations
  - trial balance debit and credit totals reconcile
  - report filters and exports meet architecture standards
  - reconciliation and performance baseline tests pass
- Estimated Complexity: Large
- Status: Completed
- Acceptance Evidence: See [Docs/10_Accounts_And_Finance/RM-08_FINANCIAL_REPORTS_ACCEPTANCE_CHECKLIST.md](Docs/10_Accounts_And_Finance/RM-08_FINANCIAL_REPORTS_ACCEPTANCE_CHECKLIST.md)

Milestone RM-09
- Milestone Name: Legacy Coexistence Hardening and Phase 1 Sign-Off Gate
- Scope:
  - finalize coexistence rules between legacy finance surfaces and accounting-v2 surfaces
  - enforce feature flags and read/write policy boundaries
  - run consolidated regression across permissions, postings, and reports
  - produce Phase 1 closure evidence and rollback-readiness checklist
- Dependencies:
  - RM-08
- Acceptance Criteria:
  - no dual-write divergence for approved Phase 1 scope
  - canonical permission consistency verified across backend, seeds, and frontend
  - full required test suites pass with no critical accounting regressions
  - Phase 1 sign-off checklist marked complete
- Estimated Complexity: Medium
- Status: Completed
- Acceptance Evidence: See [Docs/10_Accounts_And_Finance/RM-09_LEGACY_COHERENCE_HARDENING_ACCEPTANCE_CHECKLIST.md](Docs/10_Accounts_And_Finance/RM-09_LEGACY_COHERENCE_HARDENING_ACCEPTANCE_CHECKLIST.md)

### 18.2 Review cadence recommendation

1. Treat each milestone as a separate approval gate before starting the next milestone.
2. For RM-05 through RM-08, allow sub-deliverables inside each milestone but keep one merge gate per milestone.
3. Keep acceptance evidence in milestone-specific checklists for auditability.

### 18.3 Suggested implementation order

1. RM-01
2. RM-02
3. RM-04
4. RM-03
5. RM-05 and RM-06 in parallel when RM-04 is accepted
6. RM-07
7. RM-08
8. RM-09

## 19. Approval Gate

No additional implementation beyond the currently approved milestone should start until explicit approval is provided for the next milestone.

## 20. RM-01 Completion Update (Chart of Accounts Hardening and Governance)

Updated: 2026-08-01
Status: Completed

Implemented scope summary:

1. Hardened chart-account governance service rules:
  - case-insensitive account-code uniqueness enforcement
  - parent account validation requires same-business, active parent, and same account type
  - hierarchy cycle prevention during re-parenting updates
  - system-account mutation guardrails for re-parent/control/manual-posting behavior
  - deactivation guardrails for active children, posted journal references, and accounting configuration references
2. Added dedicated chart-account API surface:
  - `GET /api/accounting/chart-accounts`
  - `GET /api/accounting/chart-accounts/{id}`
  - `POST /api/accounting/chart-accounts`
  - `PUT /api/accounting/chart-accounts/{id}`
  - `POST /api/accounting/chart-accounts/{id}/deactivate`
3. Added/expanded RM-01 governance tests in accounting foundation test suite.

Validation evidence:

1. Build:
  - `dotnet build RetailPOS.slnx -c Release` passed
2. Relevant tests:
  - `AccountingFoundationServiceTests` and `AccountingPermissionRegressionTests` passed
  - Result summary: 9 passed, 0 failed

Out-of-scope confirmation:

1. RM-02 and later milestones were not started.
2. Sequencing now reflects approved order: RM-04 precedes RM-03.

## 21. RM-02 Completion Update (Accounting Configuration and Outlet Overrides)

Updated: 2026-08-01
Status: Completed

Implemented scope summary:

1. Added dedicated accounting configuration API surface with canonical policies:
  - `GET /api/accounting/configuration`
  - `PUT /api/accounting/configuration`
  - `GET /api/accounting/configuration/outlets`
  - `GET /api/accounting/configuration/outlets/{outletId}`
  - `PUT /api/accounting/configuration/outlets/{outletId}`
  - `GET /api/accounting/configuration/resolve?slot={slot}&outletId={outletId?}`
2. Implemented deterministic posting-account resolver contract:
  - `Outlet Override -> Business (Global) Configuration -> Validation Failure (Reject)`
3. Hardened RM-02 validation rules:
  - required global mappings enforced
  - account existence constrained to current business
  - active-account enforcement
  - slot-to-account-type compatibility enforcement
4. Enforced outlet authorization on outlet-specific configuration APIs.
5. Aligned canonical permission usage across backend/frontend/seed assets:
  - canonical `accounting.configuration.view` and `accounting.configuration.edit` used consistently
  - frontend route/menu coverage added for configuration permission visibility
6. Expanded RM-02 tests (service + HTTP + permission regression).

Validation evidence:

1. Build:
  - `dotnet build RetailPOS.slnx -c Release` passed.
2. Relevant tests:
  - `AccountingFoundationServiceTests`
  - `AccountingConfigurationHttpTests`
  - `AccountingPermissionRegressionTests`
  - Result summary: 14 passed, 0 failed.

Out-of-scope confirmation:

1. RM-03 and later milestones were not started in this implementation step.
2. Implementation remains aligned to approved sequence (next gated milestone remains RM-04).

## 22. RM-04 Completion Update (Opening Balance Workflow)

Updated: 2026-08-01
Status: Completed

Implemented scope summary:

1. Added dedicated opening balance API workflow:
  - `GET /api/accounting/opening-balances/status`
  - `POST /api/accounting/opening-balances`
  - `POST /api/accounting/opening-balances/{openingBalanceJournalEntryId}/adjustments`
2. Enforced RM-04 pre-posting governance:
  - opening balance posting is blocked when mandatory accounting configuration mappings are incomplete
  - required Balance Sheet mapping validation is explicit and returns clear slot-level errors
  - opening balance lines reject income/expense/COGS account types and allow only Asset/Liability/Equity accounts
3. Enforced opening balance lifecycle lock behavior:
  - lock trigger is first posted operational journal (all posted journals except opening-balance types)
  - opening balance posting is blocked once lock is active
  - opening balance and opening balance adjustment journals are excluded from lock trigger set
4. Enforced correction governance:
  - opening balance reversal is blocked
  - correction path is opening balance adjustment journal with dedicated permission and mandatory reason
  - adjustment date checks reject closed/locked periods for both adjustment business date and posting date
5. Synchronized permissions and compatibility layers:
  - added canonical `accounting.opening_balances.adjust`
  - propagated to backend catalog, onboarding defaults, SQL seed assets, frontend role form, and frontend alias map
  - retained legacy compatibility via transactions permission alias expansion
6. Added RM-04 test coverage:
  - unit tests for opening balance service governance/validation
  - integration HTTP tests for posting, adjustment, lock behavior, permission behavior, and period-state rejection
  - audit tests confirming accounts-module journal posting audit events
  - updated permission regression expectations for new canonical permissions

Validation evidence:

1. Build:
  - `dotnet build RetailPOS.slnx -c Release` passed.
2. Relevant tests:
  - `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj -c Release --filter "FullyQualifiedName~RetailPOS.Tests.Accounting.OpeningBalance|FullyQualifiedName~RetailPOS.Tests.Accounting.JournalEntryServiceTests|FullyQualifiedName~RetailPOS.Tests.Accounting.JournalEntryWorkflowHttpTests|FullyQualifiedName~RetailPOS.Tests.Accounting.AccountingPermissionRegressionTests|FullyQualifiedName~RetailPOS.Tests.Accounting.AccountingConfigurationHttpTests"`
  - Result summary: 30 passed, 0 failed.
3. Full suite baseline:
  - `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj -c Release`
  - Result summary: 258 passed, 0 failed.
4. Frontend validation:
  - `npm run -s build` (from `retailpos-frontend`) passed.

Acceptance artifact:

1. RM-04 checklist with evidence and final decision:
  - `Docs/10_Accounts_And_Finance/RM-04_OPENING_BALANCE_WORKFLOW_ACCEPTANCE_CHECKLIST.md`

Out-of-scope confirmation:

1. RM-03 implementation was not started.
2. No milestones after RM-04 were implemented in this step.

## 23. RM-03 Completion Update (Financial Year and Accounting Period Governance APIs)

Updated: 2026-08-01
Status: Completed

Implemented scope summary:

1. Completed financial year governance API lifecycle under canonical policies:
  - `GET /api/accounting/financial-years`
  - `POST /api/accounting/financial-years`
  - `PUT /api/accounting/financial-years/{id}`
  - `POST /api/accounting/financial-years/{id}/activate`
  - `POST /api/accounting/financial-years/{id}/close`
  - `POST /api/accounting/financial-years/{id}/reopen`
2. Completed accounting period governance API lifecycle under canonical policies:
  - `GET /api/accounting/financial-years/{financialYearId}/periods`
  - `POST /api/accounting/financial-years/{financialYearId}/periods`
  - `PUT /api/accounting/periods/{id}`
  - `POST /api/accounting/periods/{id}/close`
  - `POST /api/accounting/periods/{id}/lock`
  - `POST /api/accounting/periods/{id}/reopen`
3. Hardened deterministic period transition governance:
  - explicit transition guard rejects `Locked -> Closed` direct path
  - reopen requires reason
  - lock requires reason
4. Finalized strict posting governance in journal posting path:
  - posting requires one matching financial year and one matching accounting period for posting date
  - financial year and accounting period must belong to the same business scope
  - posting rejects when no matching year/period exists, year is closed, or period status is not Open
  - posting assigns resolved `FinancialYearId` and `AccountingPeriodId` on posted journals
5. Added explicit accounts-module audit events for RM-03 mutations:
  - financial year create/update/activate/close/reopen
  - accounting period create/update/status transitions
  - failure audit capture on rejected operations
6. Preserved compatibility boundaries:
  - no RM-01, RM-02, RM-04 redesign
  - no RM-05+ implementation
  - retained existing Journal Engine lifecycle behavior outside RM-03 governance checks

Validation evidence:

1. Build:
  - `dotnet build RetailPOS.slnx -c Release` passed.
2. Database migrations:
  - `dotnet ef migrations list --project src/RetailPOS.Infrastructure --startup-project src/RetailPOS.API` succeeded.
  - `dotnet ef database update --project src/RetailPOS.Infrastructure --startup-project src/RetailPOS.API` succeeded (`database is already up to date`).
3. Relevant RM-03 and regression tests:
  - `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj -c Release --filter "FullyQualifiedName~RetailPOS.Tests.Accounting.FinancialPeriod|FullyQualifiedName~RetailPOS.Tests.Accounting.JournalEntryServiceTests|FullyQualifiedName~RetailPOS.Tests.Accounting.JournalEntryWorkflowHttpTests|FullyQualifiedName~RetailPOS.Tests.Accounting.AccountingPermissionRegressionTests|FullyQualifiedName~RetailPOS.Tests.Accounting.OpeningBalanceWorkflowHttpTests|FullyQualifiedName~RetailPOS.Tests.Accounting.OpeningBalanceServiceTests|FullyQualifiedName~RetailPOS.Tests.Accounting.OpeningBalanceAuditTests"`
  - Result summary: 42 passed, 0 failed.
4. Full backend regression baseline:
  - `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj -c Release`
  - Result summary: 273 passed, 0 failed.
5. Build warning note:
  - NU1903 remains tracked as non-blocking for this milestone gate.

Acceptance artifact:

1. RM-03 checklist with acceptance evidence and decision:
  - `Docs/10_Accounts_And_Finance/RM-03_FINANCIAL_YEAR_PERIOD_ACCEPTANCE_CHECKLIST.md`

Out-of-scope confirmation:

1. RM-05 implementation was not started.
2. No milestones after RM-03 were implemented in this step.

## 24. RM-05 Completion Update (Expense v2 Workflow and Posting)

Updated: 2026-08-01
Status: Completed

Implemented scope summary:

1. Added canonical RM-05 accounting expense API surface:
  - `GET /api/accounting/expenses`
  - `POST /api/accounting/expenses/search`
  - `GET /api/accounting/expenses/{id}`
  - `POST /api/accounting/expenses`
  - `PUT /api/accounting/expenses/{id}`
  - `POST /api/accounting/expenses/{id}/submit`
  - `POST /api/accounting/expenses/{id}/approve`
  - `POST /api/accounting/expenses/{id}/reject`
  - `POST /api/accounting/expenses/{id}/cancel`
2. Added expense-category management with default expense-account mapping:
  - `GET /api/accounting/expenses/categories`
  - `POST /api/accounting/expenses/categories`
  - `PUT /api/accounting/expenses/categories/{id}`
3. Implemented draft-submit-approve-reject-cancel workflow on expenses:
  - create/update limited to editable states
  - approval is the only path that creates a journal
  - reject/cancel paths do not create journals
4. Enforced approval-time posting controls:
  - expense account resolves from mapped category by default
  - approver may provide explicit expense-account override through approve action
  - payment account must be a same-business active Asset/Liability chart account
  - expense date must resolve to an open financial year and open accounting period before any draft journal is created
5. Added schema support and migration safety for RM-05:
  - workflow/accounting fields added to `expenses`
  - new `expense_categories` table added
  - existing expense rows are backfilled for `business_id` and workflow defaults during migration
  - EF snapshot/default parity cleanup migration added so database update runs cleanly
6. Updated existing expense frontend surfaces to the accounting-v2 expense contract:
  - service now targets `/api/accounting/expenses`
  - form now uses mapped category selection and workflow-compatible request shape
  - list now exposes workflow status and submit/approve/reject/cancel actions

Validation evidence:

1. Build:
  - `dotnet build RetailPOS.slnx -c Release` passed.
2. Focused RM-05 workflow tests:
  - `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj -c Release --filter "FullyQualifiedName~RetailPOS.Tests.Accounting.ExpenseWorkflow"`
  - Result summary: 5 passed, 0 failed.
3. Frontend validation:
  - `npm run -s build` (from `retailpos-frontend`) passed.
4. Database migrations:
  - `dotnet ef migrations add AddExpenseWorkflowPhase1 --project src/RetailPOS.Infrastructure --startup-project src/RetailPOS.API` succeeded.
  - `dotnet ef migrations add TempExpenseWorkflowDriftCheck --project src/RetailPOS.Infrastructure --startup-project src/RetailPOS.API` succeeded.
  - `dotnet ef database update --project src/RetailPOS.Infrastructure --startup-project src/RetailPOS.API` succeeded.
5. Expanded accounting regression:
  - `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj -c Release --filter "FullyQualifiedName~RetailPOS.Tests.Accounting.ExpenseWorkflow|FullyQualifiedName~RetailPOS.Tests.Accounting.FinancialPeriod|FullyQualifiedName~RetailPOS.Tests.Accounting.JournalEntryServiceTests|FullyQualifiedName~RetailPOS.Tests.Accounting.JournalEntryWorkflowHttpTests|FullyQualifiedName~RetailPOS.Tests.Accounting.AccountingPermissionRegressionTests|FullyQualifiedName~RetailPOS.Tests.Accounting.OpeningBalanceWorkflowHttpTests|FullyQualifiedName~RetailPOS.Tests.Accounting.OpeningBalanceServiceTests|FullyQualifiedName~RetailPOS.Tests.Accounting.OpeningBalanceAuditTests"`
  - Result summary: 47 passed, 0 failed.
6. Full backend regression baseline:
  - `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj -c Release`
  - Result summary: 278 passed, 0 failed.
7. Build warning note:
  - NU1903 remains tracked as non-blocking for this milestone gate.

Acceptance artifact:

1. RM-05 checklist with acceptance evidence and decision:
  - `Docs/10_Accounts_And_Finance/RM-05_EXPENSE_WORKFLOW_ACCEPTANCE_CHECKLIST.md`

Out-of-scope confirmation:

1. RM-06 implementation was not started.
2. No milestones after RM-05 were implemented in this step.

## 25. RM-06 Completion Update (Other Income Workflow and Posting)

Updated: 2026-08-01
Status: Completed

Implemented scope summary:

1. Added canonical RM-06 other income API surface:
  - `GET /api/accounting/incomes`
  - `POST /api/accounting/incomes/search`
  - `GET /api/accounting/incomes/{id}`
  - `POST /api/accounting/incomes`
  - `PUT /api/accounting/incomes/{id}`
  - `POST /api/accounting/incomes/{id}/submit`
  - `POST /api/accounting/incomes/{id}/approve`
  - `POST /api/accounting/incomes/{id}/reject`
  - `POST /api/accounting/incomes/{id}/cancel`
2. Implemented full RM-06 workflow state machine:
  - Draft
  - Submitted
  - Approved
  - Rejected
  - Cancelled
3. Enforced posting behavior and traceability rules:
  - only approval creates a journal
  - journal reference metadata uses `ReferenceType = OtherIncome` and source-document `ReferenceId`
  - rejected/cancelled entries do not create journals
  - approved entries persist `JournalEntryId` linkage
4. Reused approved accounting controls at approval time:
  - period governance pre-validation requires one open financial year and one open accounting period for income date
  - default posting account resolves from `accounting_configuration.other_income_account_id`
  - explicit income-account override is supported when supplied on the document or approve action
  - receipt account must be an active same-business Asset chart account
5. Prevented orphan journals and partial states:
  - posting prerequisites are validated before draft creation
  - failed approval cleanup removes any unresolved draft journal created during the attempt
6. Completed canonical permission and frontend exposure:
  - added `accounting.other_income.reject`
  - propagated new/updated other-income permissions across backend catalog, SQL seed assets, role form, auth aliases, routes, and menu visibility
7. Added schema support for RM-06:
  - workflow metadata fields extended on `other_income_entries`
  - journal linkage (`journal_entry_id`) added
  - migration backfills `updated_at` and normalizes blank status values for existing rows
8. Added minimal frontend screens for RM-06 workflow:
  - other income list with workflow actions
  - other income create/edit form

Validation evidence:

1. Focused RM-06 workflow and permission tests:
  - `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj -c Release --filter "FullyQualifiedName~RetailPOS.Tests.Accounting.OtherIncomeWorkflow|FullyQualifiedName~RetailPOS.Tests.Accounting.OtherIncomeAtomicity|FullyQualifiedName~RetailPOS.Tests.Accounting.AccountingPermissionRegressionTests"`
  - Result summary: 11 passed, 0 failed.
2. Frontend validation:
  - `npm run -s build` (from `retailpos-frontend`) passed.
3. Database migration:
  - `dotnet ef migrations add AddOtherIncomeWorkflowPhase1 --project src/RetailPOS.Infrastructure --startup-project src/RetailPOS.API` succeeded.
  - `dotnet ef database update --project src/RetailPOS.Infrastructure --startup-project src/RetailPOS.API` succeeded.
4. Expanded accounting regression:
  - `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj -c Release --filter "FullyQualifiedName~RetailPOS.Tests.Accounting.OtherIncomeWorkflow|FullyQualifiedName~RetailPOS.Tests.Accounting.OtherIncomeAtomicity|FullyQualifiedName~RetailPOS.Tests.Accounting.ExpenseWorkflow|FullyQualifiedName~RetailPOS.Tests.Accounting.FinancialPeriod|FullyQualifiedName~RetailPOS.Tests.Accounting.JournalEntryServiceTests|FullyQualifiedName~RetailPOS.Tests.Accounting.JournalEntryWorkflowHttpTests|FullyQualifiedName~RetailPOS.Tests.Accounting.AccountingPermissionRegressionTests|FullyQualifiedName~RetailPOS.Tests.Accounting.OpeningBalanceWorkflowHttpTests|FullyQualifiedName~RetailPOS.Tests.Accounting.OpeningBalanceServiceTests|FullyQualifiedName~RetailPOS.Tests.Accounting.OpeningBalanceAuditTests"`
  - Result summary: 53 passed, 0 failed.
5. Backend build:
  - `dotnet build RetailPOS.slnx -c Release` passed.
6. Full backend regression baseline:
  - `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj -c Release`
  - Result summary: 284 passed, 0 failed.
7. Build warning note:
  - NU1903 remains tracked as non-blocking for this milestone gate.

Acceptance artifact:

1. RM-06 checklist with acceptance evidence and decision:
  - `Docs/10_Accounts_And_Finance/RM-06_OTHER_INCOME_ACCEPTANCE_CHECKLIST.md`

Out-of-scope confirmation:

1. RM-07 implementation was not started.
2. No milestones after RM-06 were implemented in this step.

---

## Appendix A. Historical Pre-Journal Foundation Snapshot (Archived)

Updated: 2026-08-01
Status: Historical notes retained for traceability only. Superseded by accepted Journal Engine state and Section 18 milestone plan.

### 18.1 Final sequential allocation design

1. Permanent journal numbers are allocated only at posting time.
2. Draft journals never consume permanent sequence values.
3. Sequence scope is `BusinessId + SequenceType + FinancialYear scope`.
4. Sequence records are stored in `accounting_number_sequences` and updated using relational `INSERT ... ON CONFLICT ... DO UPDATE ... RETURNING` semantics.
5. Allocation requires an active database transaction and runs in the same transaction as posting.
6. Journal number uniqueness is enforced by DB unique index on `(BusinessId, JournalNumber)`.
7. Allocator does not rely on in-memory locks as primary protection.

### 18.2 Transaction and concurrency strategy

1. Journal posting uses relational transactions (`Serializable`) in the posting service.
2. Number allocation executes through the relational provider inside the posting transaction.
3. Sequence mutation rolls back when transaction rollback occurs.
4. Concurrency safety is achieved by database conflict/update semantics and unique constraints.
5. Idempotent draft creation remains reference-based; permanent numbers are created at post/reversal post only.

### 18.3 Financial year and accounting period API design

Implemented API namespace: `/api/accounting/*`

Financial year APIs:

1. `GET /api/accounting/financial-years`
2. `POST /api/accounting/financial-years`
3. `PUT /api/accounting/financial-years/{id}`
4. `POST /api/accounting/financial-years/{id}/activate`
5. `POST /api/accounting/financial-years/{id}/close`
6. `POST /api/accounting/financial-years/{id}/reopen`

Accounting period APIs:

1. `GET /api/accounting/financial-years/{financialYearId}/periods`
2. `POST /api/accounting/financial-years/{financialYearId}/periods`
3. `PUT /api/accounting/periods/{id}`
4. `POST /api/accounting/periods/{id}/close`
5. `POST /api/accounting/periods/{id}/lock`
6. `POST /api/accounting/periods/{id}/reopen`

Governance rules implemented:

1. Prevent overlapping financial years within a business.
2. Prevent overlapping accounting periods within a financial year.
3. Enforce single current financial year per business (service + DB unique filtered index).
4. Enforce `Open`, `Closed`, `Locked` period states.
5. Reopen operations require reason and are logged.
6. Journal posting blocks closed financial years and closed/locked accounting periods.

### 18.4 Permission synchronization approach and regression automation

1. Backend `PermissionCatalog` remains canonical registry for accounting permissions.
2. Controller `[Authorize]` accounting policies are validated against catalog in automated tests.
3. SQL permission assets (`add-accounting-v2-permissions.sql`, `seed-v2-roles.sql`, `seed-data.sql`) are validated for accounting permission coverage.
4. Angular route and menu accounting permissions are validated against backend catalog.
5. Frontend guard usage is validated to ensure accounting routes use permission guard checks.
6. Legacy alias mappings in frontend/backend are validated for compatibility paths.

### 18.5 Legacy finance transition strategy (approved)

1. Keep legacy finance endpoints temporarily for backward compatibility.
2. Map legacy-compatible claims to canonical accounting permissions where safe.
3. Do not add new functionality using legacy finance permissions.
4. All new accounting endpoints use canonical `accounting.*` permissions.
5. Legacy finance routes and permissions are now marked as deprecated transition surfaces.
6. Removal plan: remove legacy routes/permissions after frontend and API consumer migration plus regression sign-off.

Deprecated transition surfaces:

1. Legacy permissions: `accounts.*`, `transactions.*`, `expenses.*`
2. Legacy controllers remain operational only during transition.

### 18.6 Tests added for this pre-journal foundation pass

1. Relational concurrency integration tests for sequence allocation and concurrent posting behavior.
2. Relational rollback test for sequence mutation safety on failed transactions.
3. Permission regression tests spanning backend catalog, controller policies, SQL seeds, Angular routes/menu, and frontend alias/guard alignment.

### 18.7 Remaining risks before Journal Engine milestone

1. Backdated posting policy precedence across operational date contexts still needs formal business sign-off.
2. Legacy endpoint decommissioning depends on consumer migration status and telemetry.
3. Operational posting integrations (POS/Purchase/Inventory/Cash Session) are intentionally deferred and unstarted in this milestone.

## Confirmation

Pre-Journal-Engine foundation tasks are implemented and validated. Journal Engine milestone and operational accounting integrations have not been started in this completion pass.
