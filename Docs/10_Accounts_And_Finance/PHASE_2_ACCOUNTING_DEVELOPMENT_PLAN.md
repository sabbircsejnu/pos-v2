# PHASE 2 ACCOUNTING DEVELOPMENT PLAN

Updated: 2026-08-09
Status: Draft for review — P2-04 and P2-05 implementation and verification complete

## 1. Purpose

This document turns the accepted Phase 1 accounting foundation into a practical Phase 2 rollout plan for operational accounting integration.

The plan assumes that the accepted journal engine, chart of accounts, accounting configuration, financial-year/period controls, and reporting foundation remain the base architecture. Phase 2 adds operational posting hooks to the existing POS, purchase, inventory, supplier, and cash-session workflows without redesigning Phase 1.

## 2. Guiding principles

- Use the accepted journal engine as the only financial source of truth.
- Keep operational services as the owners of business events and inventory updates.
- Post accounting journals synchronously inside the same database transaction as the operational step where the business event is committed.
- Never create a journal before the approved business event occurs.
- Use historical cost, not current product cost or selling price, for COGS and return reversals.
- Preserve idempotency at the database level for retries and duplicate submissions.
- Reuse existing controller, service, and reporting patterns rather than introducing a parallel accounting workflow.
- Treat cash-session opening, normal close, and zero-variance close as control events rather than new revenue/expense postings. Only a non-zero cash difference should create a variance journal.
- For P2-05, do not create duplicate sale, refund, or cash-movement postings; the session close logic should rely on the already-posted POS cash sales and refunds from P2-01 and only post the variance difference.

## 3. Current codebase gaps that must be resolved before Phase 2

The current implementation already has the accounting foundation, but the operational flows still need explicit accounting integration points:

- [src/RetailPOS.API/Services/SaleService.cs](src/RetailPOS.API/Services/SaleService.cs) completes sales, voids, and refunds, but it does not create accounting journals and it does not preserve a historical cost basis for COGS reversal.
- [src/RetailPOS.API/Services/GrnService.cs](src/RetailPOS.API/Services/GrnService.cs) and [src/RetailPOS.API/Services/PurchaseOrderService.cs](src/RetailPOS.API/Services/PurchaseOrderService.cs) update inventory and stock ledger entries, but they do not create accounting journals for purchase receipts or supplier liability impact.
- There is no dedicated supplier-payment or supplier-advance workflow in the current codebase; the existing bill model is transactionally shallow and not sufficient for allocation-style accounting.
- [src/RetailPOS.API/Services/StockAdjustmentService.cs](src/RetailPOS.API/Services/StockAdjustmentService.cs) and [src/RetailPOS.API/Services/StockCountService.cs](src/RetailPOS.API/Services/StockCountService.cs) have approval workflows and inventory updates, but they do not post accounting entries for shortage or gain.
- [src/RetailPOS.API/Services/CashSessionService.cs](src/RetailPOS.API/Services/CashSessionService.cs) computes expected and actual cash closing variance, but it does not create a journal for surplus or shortage.
- The accounting configuration and defaults already exist under [src/RetailPOS.API/Services/AccountingDefaults.cs](src/RetailPOS.API/Services/AccountingDefaults.cs) and [src/RetailPOS.API/Services/AccountingConfigurationService.cs](src/RetailPOS.API/Services/AccountingConfigurationService.cs), but the Phase 2 plan must add the missing mapping slots needed for supplier advance and cash-session difference handling.

## 4. Recommended milestone sequence

1. P2-01 — POS Sale, Return, Refund and Exchange Accounting
2. P2-02 — Purchase / GRN Accounting
3. P2-03 — Supplier Payment and Supplier Advance Accounting
4. P2-04 — Stock Adjustment / Stock Count Accounting
5. P2-05 — Cash Session Closing, Shortage and Surplus Accounting
6. P2-06 — COGS and Inventory Valuation
7. P2-07 — Cash Book Report
8. P2-08 — Phase 2 Reconciliation, Legacy Coexistence and Final Sign-Off

This order is recommended because the POS sale flow provides the cleanest end-to-end validation of the posting service before the purchase, inventory, and cash-session milestones build on the same pattern.

## 5. Cross-cutting implementation requirements

### Shared service layer

Introduce a dedicated accounting posting service that can be called from the operational service transaction boundary. The operational service remains the owner of the business event, and the posting service only receives a validated payload and writes journal header/line rows.

### Shared payload contract

Each posting call should carry:

- business and outlet context
- posting date and business date
- reference type and reference id
- transaction amount details
- cost snapshot details for inventory-related entries
- optional warehouse, terminal, and cash-session context

### Shared validation rules

- The posting service must resolve the current accounting period and financial year before posting.
- The posting service must reject posts for closed or locked periods.
- The posting service must fail the whole operation if the journal cannot be created.
- The posting service must use the idempotency key from the operational event if one exists; otherwise it must derive an internal deterministic key from the business event.

## 6. Detailed milestone plan

### P2-01 business-rule clarifications and pre-implementation data-model requirements

The P2-01 plan must explicitly define the following POS accounting rules before implementation starts:

- Normal sale posting must include a cost-of-goods-sold entry: Dr COGS / Cr Inventory, using a historical sale-line cost snapshot captured at the moment the sale line is created.
- Partial returns must reverse COGS using the original sale-line cost snapshot and the returned quantity, not the current product cost or the current inventory valuation.
- When tax is applicable, the sale and refund postings must also create a tax line to a configured VAT Payable account. If tax is disabled or zero, no tax line is posted.
- Payment postings must be mapped by payment method. Cash uses the cash account, bank transfers use the bank account, card and MFS payments should use the configured card/MFS payment account or, when settlement is delayed, a dedicated clearing account until settlement is reconciled.
- Item-level discounts must be treated as a reduction to revenue and posted to the Sales Discount account. Overall header discounts should be posted as a single sales-discount line at the sale level.
- Split payments must create one debit line per payment method against the appropriate mapped payment account.
- Rounding differences must be posted to the configured Rounding Difference account so that the journal remains balanced.
- Exchange should remain a refund/return plus a new sale, but both sides must carry an explicit exchange linkage for audit and traceability.
- The operational Sale/Refund/Void action is authorized by the POS user. The internal system-generated journal posting must not require the cashier or salesperson to hold generic manual-accounting journal-post permissions.
- Sale + inventory movement + stock ledger + revenue journal + COGS journal must be committed atomically in a single transaction.

Required schema change before implementation:

- The current POS data model does not preserve enough historical unit-cost information for every sale line to support accurate COGS and partial-return reversal. The minimum required change is to add a historical cost snapshot field to the sale-line model, for example `HistoricalUnitCost` (decimal) on `SaleItem`, and persist it at the moment the sale line is created.
- If exchange traceability is required at the operational level rather than only in journal metadata, add a lightweight linkage field or table such as `OriginalSaleId`/`RelatedSaleId` or a dedicated `SaleLink` table for refund/return and exchange events.
- Existing sale rows created before the migration will remain non-recoverable for historical COGS unless a one-time backfill is executed; that backfill is out of scope for the first implementation slice and should be treated as a migration follow-up.

### P2-01 — POS Sale, Return, Refund and Exchange Accounting

#### Scope
- Add accounting posting for completed sales, refunds, voids, and exchange-style correction flows.
- Ensure sale postings are generated only when the operational sale is committed.
- Ensure refund and void flows reverse revenue, cash/bank impact, tax, and cost impact correctly.
- Ensure the sale posting includes both the revenue journal and the COGS journal as part of the same atomic operational transaction.

#### Existing functionality to reuse
- Existing sale creation, refund, and void service methods in [src/RetailPOS.API/Services/SaleService.cs](src/RetailPOS.API/Services/SaleService.cs)
- Existing stock-ledger movement entries and inventory reduction logic
- Existing sale idempotency handling and cash-session validation

#### Dependencies
- Phase 1 journal engine and accounting configuration
- Accounting period and financial-year resolution
- Mapping slots for sales, sales discount, sales return, VAT Payable, payment accounts, clearing accounts, rounding difference, inventory, and COGS

#### Required account mappings
- Sales revenue account
- Sales discount account
- Sales return account
- VAT Payable account when tax is applicable
- Cash account, bank account, card account, and MFS account by payment method
- Optional card/MFS clearing account for delayed settlement scenarios
- Rounding difference account
- Inventory account and COGS account

#### Journal types and exact debit/credit rules
- Sale posting journal type: SalePosting
  - Debit: payment account(s) for the net amount received, one line per payment method row
  - Debit: sales discount account for the discount amount, if any
  - Debit: VAT Payable account for the tax amount collected, if tax is applicable
  - Credit: sales revenue account for the gross sales value, net of discount and tax as required by the business rule set
  - Debit: COGS account
  - Credit: inventory account
- Refund / return posting journal type: SalesReturnPosting
  - Debit: sales return account for the returned sales value
  - Credit: payment account(s) for the cash/bank/card/MFS reversal, one line per payment method row
  - Credit: VAT Payable account for the tax reversal, if tax had been collected
  - Debit: inventory account
  - Credit: COGS account for the returned quantity, using the original sale-line historical cost snapshot and the returned quantity
- Void handling
  - Use the same reversal pattern as refund when the voided sale has been fully completed and the event is approved
- Rounding treatment
  - Post any residual rounding difference to the configured Rounding Difference account so the journal remains balanced
- Exchange handling
  - Create a refund/return posting plus a new sale posting and carry an explicit exchange linkage in the operational event and the journal metadata for audit and traceability

#### Posting trigger
- Triggered when a sale is committed as completed.
- Triggered when a refund or void action is committed.
- For exchange-style flows, treat the exchange as a refund plus a new sale posting if the UI does not yet expose a dedicated exchange document.

#### Transaction/atomicity boundary
- The posting must happen inside the same database transaction as the sale mutation, stock update, and stock-ledger write.
- Rollback the whole operation if posting fails.

#### Idempotency strategy
- Reuse the sale idempotency key when present.
- Add a unique constraint on business + reference type + reference id + journal type so duplicate writes are safely resolved.

#### Reversal/cancellation behavior
- Refunds and voids should be posted as new journals that reverse the original posting rather than mutating the original journal.
- The original journal should remain immutable.

#### Business/outlet/warehouse scope
- Scoped to the sale outlet and applicable warehouse context.
- Sales must carry outlet and terminal metadata into the journal header.

#### Permissions
- Existing sales-related permissions plus new accounting posting permission family for posting and reversal.

#### Audit requirements
- Record the operational event, journal id, posting result, and user id.
- Audit the reversal reason and the original reference id.

#### Database/API/frontend changes
- Backend: new posting payload contract and posting hook inside the sale service.
- API: internal service call only; no direct controller-level journal creation.
- Frontend: no UI changes in the first slice beyond the existing sale detail/receipt context if needed for later reporting visibility.

#### Acceptance criteria
- A completed sale creates a balanced journal.
- A refund or void creates a reversal journal that preserves the original journal.
- The posting is atomic with the sale transaction.
- Duplicate retries return the existing journal instead of creating a second one.

#### Required automated tests
- Sale posting success path
- Refund posting success path
- Void posting success path
- Duplicate submission idempotency test
- Period-closed rejection test
- COGS reversal test for partial refund

#### Estimated complexity
- High

---

### P2-02 — Purchase / GRN Accounting

#### Scope
- Post purchase receipts and GRNs through the accepted journal engine.
- Link purchase receipt postings to inventory, supplier payable, and cash/bank impact.

#### Existing functionality to reuse
- Purchase order creation and approval in [src/RetailPOS.API/Services/PurchaseOrderService.cs](src/RetailPOS.API/Services/PurchaseOrderService.cs)
- GRN creation and completion in [src/RetailPOS.API/Services/GrnService.cs](src/RetailPOS.API/Services/GrnService.cs)
- Inventory update and stock-ledger flow already present in the GRN service

#### Dependencies
- P2-01 posting pattern and service contract
- GRN item unit cost data must be available at the moment of posting
- Supplier payable and inventory account mappings

#### Required account mappings
- Inventory account
- Supplier payable account
- Cash or bank account for paid receipts
- Purchase discount account if the business uses trade discount handling

#### Journal types and exact debit/credit rules
- Purchase receipt posting journal type: PurchaseReceiptPosting
  - Debit: inventory account for the received stock value
  - Credit: supplier payable account for purchase on credit
  - Credit: cash/bank account for immediate paid receipts
- GRN correction or reversal posting
  - Reverse the original purchase receipt entries from the same reference id

#### Posting trigger
- Triggered when a GRN is completed.
- Triggered when the immediate purchase-receive path finishes successfully.

#### Transaction/atomicity boundary
- Post within the same transaction as inventory movement and PO status update.
- If posting fails, the GRN and inventory changes must rollback.

#### Idempotency strategy
- Use the GRN id or a deterministic business-event key based on PO/GRN and business context.
- Treat duplicate completion requests as safe replays.

#### Reversal/cancellation behavior
- Reversal journals should be created for any cancellation or correction rather than mutating the original receipt journal.

#### Business/outlet/warehouse scope
- Scoped to the warehouse and business context of the purchase event.

#### Permissions
- Existing purchase and GRN permissions plus accounting posting permission for purchase receipts.

#### Audit requirements
- Record GRN id, PO id, posted amount, user id, and posting status.

#### Database/API/frontend changes
- Backend: posting service call inside GRN completion flow and immediate receive flow.
- Database: no new core tables required; the plan should reuse the existing journal tables and optional metadata fields for cost snapshot information.
- Frontend: no mandatory UI changes beyond reporting visibility.

#### Acceptance criteria
- A completed GRN creates a balanced journal with inventory and payable or cash impact.
- Immediate purchase-receive creates the same journal pattern as a normal GRN.
- Duplicate completion requests do not create duplicate posted journals.

#### Required automated tests
- GRN completion creates correct inventory and liability entries
- Immediate purchase receive uses the correct posting path
- Duplicate request returns existing posting
- Period-close rejection test

#### Estimated complexity
- High

---

### P2-03 — Supplier Payment and Supplier Advance Accounting

#### P2-03 readiness review

Status: Ready for Implementation.

Current readiness assessment:
- The approved business rules are now sufficiently defined to begin implementation.
- The accounting foundation already provides the required journal engine, financial-year/period controls, and posting-account resolution.
- The remaining work is expected implementation scope rather than a pre-implementation blocker:
  - introduce the SupplierPayment, SupplierPaymentAllocation, and SupplierAdvance domain and persistence model;
  - add allocation, advance-balance, posting, reversal, and approval workflow logic;
  - add permissions, API endpoints, UI workflow, and attachment handling;
  - wire payment-method/account resolution through accounting configuration.
- No genuine pre-implementation blocker remains on business rules, architecture, or dependency decisions:
  - multi-document and partial allocation are defined;
  - supplier advance and overpayment treatment are defined;
  - maker-checker, optional attachments, cash/bank resolution, journal rules, reversal behavior, permissions, API/UI expectations, idempotency, and atomicity are all defined.

Conclusion: P2-03 is ready to begin implementation. The remaining items are standard build tasks and should be planned as implementation scope rather than treated as readiness blockers.

#### Recommended workflow
1. Create a supplier-payment or supplier-advance draft record (maker).
2. Choose payment type:
   - Payment against payable: select one or more open supplier liabilities (for example outstanding bills/GRNs/invoices).
   - Supplier advance/prepayment: create a prepayment to the supplier without immediately linking it to a bill.
3. For a payable payment, allocate the amount across one or more open payable documents. Partial payments and partial allocations are allowed. A single payment can be applied to multiple documents, and a payable document can receive multiple payments over time.
4. Allocation must never exceed the outstanding amount of the target payable. If the payment exceeds the selected payable amount, the excess must become supplier advance rather than silently over-allocating a payable.
5. Submit for approval if maker-checker is enabled. Approval moves the record to Approved/Posted state. When maker-checker is enabled, the maker cannot approve their own payment unless self-approval is explicitly enabled for the business.
6. Posting creates the journal, updates balances, and locks the payment from further mutation except reversal/cancellation.
7. Reversal or cancellation creates a new reversal journal and restores impacted payable allocations and/or supplier advance balances atomically.

#### Recommended data model
Introduce three dedicated operational records with explicit relationships:
- SupplierPayment
  - BusinessId, OutletId, SupplierId
  - PaymentDate, PostingDate, BusinessDate
  - PaymentMethod (cash/bank), PaymentAccountId
  - Amount, Currency
  - Status (draft, submitted, approved, posted, cancelled, reversed)
  - ReferenceNumber, ExternalReference, Notes
  - AttachmentPath or AttachmentId
  - CreatedBy, SubmittedBy, ApprovedBy, PostedBy, ReversedBy
  - ReversalOfPaymentId, IdempotencyKey
  - Relationships: one supplier payment has many SupplierPaymentAllocations; one payment may create or consume one or many supplier advances via allocation events.
- SupplierPaymentAllocation
  - SupplierPaymentId
  - PayableDocumentType (bill / grn / invoice / other payable document)
  - PayableDocumentId
  - AllocatedAmount
  - OutstandingBeforeAllocation, OutstandingAfterAllocation
  - AllocationDate
  - IsReversed, ReversalOfAllocationId
  - Source/traceability metadata
  - Relationship: each allocation belongs to exactly one SupplierPayment and targets exactly one payable document; allocations are immutable once posted and remain auditable.
- SupplierAdvance
  - BusinessId, OutletId, SupplierId
  - Amount, RemainingAmount, AppliedAmount
  - PaymentDate, PostingDate, BusinessDate
  - PaymentMethod, PaymentAccountId
  - Status (draft, submitted, approved, posted, applied, cancelled, reversed)
  - ReferenceNumber, Notes, Attachment metadata
  - SourcePaymentId, PostedJournalId, RelatedPaymentId, IdempotencyKey
  - Relationship: one supplier advance belongs to one supplier and may be partially or fully allocated across one or many SupplierPaymentAllocations; advance may exist without a GRN/payable.

A dedicated allocation table is required. The existing Bill table is not enough because it lacks allocation history, payment linkage, supplier-advance balance, approval state, and reversal metadata.

#### Recommended journal rules
- Payment against payable
  - Dr Supplier Payable
  - Cr Cash / Bank
- Supplier advance / prepayment
  - Dr Supplier Advance
  - Cr Cash / Bank
- Allocation of existing supplier advance against future payable
  - Dr Supplier Payable
  - Cr Supplier Advance
- Full and partial payments
  - Each allocation line should be posted against the selected payable documents and the outstanding balance should be recalculated after posting.
- Multi-document allocation
  - One payment may create multiple allocation lines against multiple payable documents.
- Cash vs bank
  - Use the payment method to resolve the correct cash or bank account through accounting configuration/payment-method mapping. Do not hardcode account IDs.
- Overpayment handling
  - If payment exceeds the allocated payable amount, the excess must become supplier advance. It must not silently over-allocate a payable.
- Reference, date, notes, attachment
  - All of these should be persisted on the payment or advance header and echoed into the journal description and audit record.

#### Posting trigger and atomicity
- Trigger only when the payment/advance is approved and committed.
- Create the journal in the same database transaction as the payment/advance persistence and allocation updates.
- If the journal cannot be created, the whole payment operation must roll back.

#### Idempotency and reversal behavior
- Use the payment or advance id as the canonical reference id and additionally persist a deterministic idempotency key (business + supplier + payment date + reference number).
- Reversal/cancellation should create a new journal entry rather than mutating the original journal.

#### Business/outlet scope and balance calculation
- Payments and advances should be scoped to the business and outlet context that initiated the transaction.
- Supplier payable outstanding must be derived from posted purchase liabilities and valid allocations/reversals, not from a simple status field on the bill.
- A payment record by itself must not reduce a payable unless an allocation is made against it.
- A bill should remain a payable document, while the payment allocation table should carry the actual settlement history.
- Supplier advance balances should be supplier-specific and must not be allocated to another supplier.

#### Permissions and maker-checker requirements
- Minimum permissions:
  - supplier payments view
  - supplier payments create
  - supplier payments approve
  - supplier payments cancel/reverse
  - supplier advances view/create/approve/cancel
  - accounting posting for supplier payments and supplier advances
- Recommended workflow policy:
  - Use the same configurable Accounting Configuration pattern as Manual Journals for maker-checker.
  - Support direct authorized posting or Submit → Approve/Reject workflow.
  - When maker-checker is enabled, the maker cannot approve their own payment unless self-approval is explicitly enabled for the business.

#### Audit trail and traceability requirements
- Persist the source document reference (bill/GRN/invoice), payment id, allocation id, journal id, posted amount, user id, approval history, and reversal reason.
- Attachments should be stored with the payment or advance record and linked to the journal metadata for auditability.
- The source-document trail should allow a user to trace every payment back to the originating payable document, supplier advance, and the posted journal line.
- Allocation history must be immutable and auditable.

#### API and UI requirements
- Backend
  - Add dedicated endpoints for supplier-payment create, submit, approve, post, cancel, reverse, and allocation listing.
  - Add server-side validation for outstanding-balance checks, overpayment handling, duplicate idempotency, and supplier-specific advance allocation constraints.
- Frontend
  - Add a supplier-payment screen with payment type selection, payable-document selection, multi-document allocation grid, reference/notes/attachment inputs, and approval status visibility.
  - Add supplier-advance creation and allocation views to support prepayments and later allocation to future payables.
  - Reuse the existing accounting attachment infrastructure where technically appropriate.

#### Dependencies
- P2-02 purchase/GRN accounting must be complete so payable balances can be referenced correctly.
- The accounting configuration layer must gain a dedicated supplier-advance mapping slot and the ability to resolve cash vs bank accounts by payment method.
- The journal engine must support a supplier-payment posting type and a supplier-advance posting type.
- Attachment storage and approval workflow hooks must align with the existing accounting attachment and manual-journal configuration patterns.

#### Approved business decisions
- Multi-document allocation is required; one supplier payment may be allocated across multiple outstanding payable documents, with partial payments and partial allocations allowed.
- Supplier advance is a supplier-specific prepayment balance that may exist without a GRN/payable and may later be allocated against one or many payables for the same supplier.
- Overpayment becomes supplier advance rather than over-allocating a payable.
- Maker-checker uses the same configurable Accounting Configuration pattern as Manual Journals, with self-approval only when explicitly enabled for the business.
- Attachments are optional and should reuse the existing accounting attachment infrastructure where technically appropriate.
- Payment methods initially support cash and bank through accounting configuration/payment-method mapping.
- Supplier payable outstanding must be derived from posted purchase liabilities and valid allocations/reversals; a payment record alone does not reduce a payable until an allocation exists.
- Posted payments and advances are immutable; cancellation or correction creates reversal transactions/journals that restore affected balances atomically.

#### Final verdict
- P2-03 Ready for Implementation

---

### P2-04 — Stock Adjustment / Stock Count Accounting

#### Scope
- Post stock shortages and gains arising from stock adjustments and stock-count approvals.
- Ensure the posting uses the approved quantity change and the correct reason mapping.

#### Existing functionality to reuse
- Existing adjustment workflow in [src/RetailPOS.API/Services/StockAdjustmentService.cs](src/RetailPOS.API/Services/StockAdjustmentService.cs)
- Existing stock-count workflow in [src/RetailPOS.API/Services/StockCountService.cs](src/RetailPOS.API/Services/StockCountService.cs)
- Existing inventory updates and stock-ledger entries

#### Dependencies
- P2-01 and P2-02 logic for inventory and COGS handling
- Reason mapping between operational adjustment reasons and accounting accounts

#### Required account mappings
- Inventory account
- Inventory shortage account
- Inventory gain account

#### Journal types and exact debit/credit rules
- Stock gain posting journal type: StockGainPosting
  - Debit: inventory account
  - Credit: inventory gain account
- Stock shortage posting journal type: StockShortagePosting
  - Debit: inventory shortage account
  - Credit: inventory account

#### Posting trigger
- Triggered when an adjustment or stock count is approved and the quantity change is committed.

#### Transaction/atomicity boundary
- The accounting journal must be created in the same transaction as the inventory adjustment and stock-ledger update.

#### Idempotency strategy
- Use the adjustment id or stock-count approval event as the reference id and derive a deterministic business key for retries.

#### Reversal/cancellation behavior
- If the adjustment is cancelled or rejected after posting, create a reversal journal instead of modifying the original journal.

#### Business/outlet/warehouse scope
- Scoped to the adjustment location and its warehouse or outlet inventory context.

#### Permissions
- Existing stock adjustment and stock count permissions plus posting permission.

#### Approved business decisions
- Stock Adjustment approval is the accounting posting trigger.
- Stock Count itself does not post directly when it generates a linked Stock Adjustment; the linked approved Stock Adjustment is the single posting event to avoid duplicate journals.
- Stock Count → Stock Adjustment → Journal linkage must be preserved through the source stock-count reference and the resulting journal linkage metadata.
- Stock gain/shortage financial value must use the approved weighted-average inventory cost for the affected product variant and location at approval time; selling price is not used.
- The cost snapshot used for the adjustment must be persisted so later product-cost changes cannot alter historical accounting.
- Multi-item adjustments must calculate value per line and aggregate those line values into the journal.

#### Audit requirements
- Record reason, measured quantity difference, approved-by user, journal id, and location context.

#### Database/API/frontend changes
- Backend: posting call from the approval workflow.
- Frontend: no UI change required for the first slice unless accounting visibility is needed.

#### Acceptance criteria
- Approved stock gain creates a balanced journal.
- Approved stock shortage creates a balanced journal.
- Rejected or cancelled adjustments do not leave orphan postings.

#### Required automated tests
- Gain posting test
- Shortage posting test
- Rejected adjustment does not post
- Duplicate approval does not create duplicate journals

#### Estimated complexity
- Medium

#### Final verdict
- P2-04 Ready for Implementation

---

### P2-05 — Cash Session Closing, Shortage and Surplus Accounting

#### Scope
- Post cash-session closing differences as accounting journals only when the variance is non-zero.
- Treat opening float, normal closing, and zero-variance close as session-control operations rather than new revenue or expense entries.
- Recognize both positive and negative cash variance as accounting events without double-posting the underlying POS cash sales or refunds.

#### Existing functionality to reuse
- [src/RetailPOS.API/Services/CashSessionService.cs](src/RetailPOS.API/Services/CashSessionService.cs)
- Existing sale payment and session-close summary logic
- Existing P2-01 sale posting so the cash-session close can rely on already-posted cash sales and refunds

#### Dependencies
- P2-01 sale posting so cash-session close can use the same payment and session context
- Cash difference gain/loss mapping slots

#### Required account mappings
- Cash on hand account
- Cash difference gain account
- Cash difference loss account

#### Journal types and exact debit/credit rules
- Cash difference surplus posting journal type: CashDifferenceSurplusPosting
  - Debit: cash on hand account
  - Credit: cash difference gain account
- Cash difference shortage posting journal type: CashDifferenceShortagePosting
  - Debit: cash difference loss account
  - Credit: cash on hand account

#### Posting trigger
- Triggered when a cash session is closed and the calculated difference is non-zero.
- Zero or near-zero variance should not create a journal.

#### Transaction/atomicity boundary
- The journal must be created inside the same transaction as the session close update.

#### Idempotency strategy
- Use the cash-session id as the reference id and prevent duplicate posting on replays.
- Reclosing the same session should not create a second journal; the close operation should be treated as an idempotent state transition.

#### Reversal/cancellation behavior
- If a session is reopened or corrected, create a reversal journal rather than editing the original difference entry.
- Reopening should not create a new revenue or expense posting unless the corrected close also produces a new non-zero variance.

#### Business/outlet/warehouse scope
- Scoped to the outlet and terminal context embedded in the cash session.

#### Permissions
- Cash-session close permission plus accounting posting permission.

#### Audit requirements
- Record opening cash, expected closing cash, actual closing cash, difference amount, notes, and user id.
- Capture whether the session close produced a variance journal or was treated as a zero-variance control event.

#### Database/API/frontend changes
- Backend: posting hook inside the session close workflow.
- Frontend: optional visibility in session details or accounting views.

#### Acceptance criteria
- A positive variance creates a balanced gain journal.
- A negative variance creates a balanced loss journal.
- Zero variance does not create a posting.
- Reclosing the same session does not double-post.

#### Required automated tests
- Surplus posting test
- Shortage posting test
- Zero-variance close does not create a posting
- Duplicate close does not create duplicate posting

#### Estimated complexity
- Medium

---

### P2-06 — COGS and Inventory Valuation

#### Scope
- Make COGS and inventory valuation reportable from posted journals and historical cost snapshots rather than from mutable product cost fields.
- Add the minimum reporting data needed for Phase 2 without introducing a bigger inventory valuation model.

#### Readiness review clarifications
- COGS source of truth must be the posted journal lines that hit the COGS account for sale, sales-return, exchange, and reversal postings. Reports must not derive COGS from current product cost or current selling price.
- Inventory valuation source of truth must be a stock-movement valuation model reconstructed from posted stock movements, receipt cost, and the same historical cost basis used by the journal postings. The report must reconcile to the Inventory control account in the General Ledger.
- The valuation methodology for each variant/location must be a running weighted-average cost pool. Each receipt or valid stock increase adds quantity and value at the movement unit cost; each issue, sale, shortage, transfer-out, or other reduction consumes from that pool at the current weighted-average unit cost, while reverse and correction movements use the original movement cost basis where that is required for reproducibility.
- As-of-date valuation must be reconstructed in chronological movement order using the movement timestamp and, when required, the business date as the fallback order. The report must support historical as-of reporting by replaying movements up to the requested date and not by reading a single current snapshot.
- COGS and valuation reports must support date-range, business, outlet, warehouse, product, variant, and location filtering. Rounding differences must stay in the configured rounding-difference account and must not be mixed into valuation or COGS balances.
- Legacy transactions created before accounting integration or before historical sale-line cost existed are not recoverable to exact historical cost without a one-time backfill or an explicit estimation policy. Those rows must be marked as legacy/estimated and excluded from precise historical reporting unless such a policy is approved.

#### Existing functionality to reuse
- Existing GRN unit-cost data in [src/RetailPOS.API/Services/GrnService.cs](src/RetailPOS.API/Services/GrnService.cs)
- Existing inventory and stock-ledger data
- Existing accounting report service patterns in [src/RetailPOS.API/Services/AccountingReportService.cs](src/RetailPOS.API/Services/AccountingReportService.cs)

#### Dependencies
- P2-01 and P2-02 accounting postings must already be stable so the reports can be built from journal lines and cost metadata.

#### Required account mappings
- Inventory account
- COGS account
- Inventory gain and inventory shortage accounts

#### Journal types and exact debit/credit rules
- This milestone is primarily a reporting milestone rather than a new operational posting family.
- The posting rules already established in P2-01 and P2-02 are reused as the source data for COGS and inventory valuation.
- Any revaluation or correction event should create a dedicated adjustment journal with the same double-entry structure.

#### Posting trigger
- No new trigger by itself; the milestone uses the sale and purchase postings as the source of truth.

#### Transaction/atomicity boundary
- Report generation must not create journals; it must only read from the posted journal and cost-snapshot data.

#### Idempotency strategy
- Not applicable to reporting output.

#### Reversal/cancellation behavior
- Cost and valuation reports must reflect reversal journals correctly.

#### Business/outlet/warehouse scope
- Reports should be filterable by business, outlet, and warehouse.

#### Permissions
- Accounting reporting permission plus existing inventory-report permissions.

#### Audit requirements
- Report generation should be traceable to the underlying journal references.

#### Database/API/frontend changes
- Backend: implemented in `AccountingReportService`. COGS is read solely from posted lines hitting the configured COGS account. Inventory valuation replays valuation-aware stock-ledger movements into variant/location weighted-average pools, excluding and exposing movements without a reliable cost basis.
- API: `GET /api/accounting/reports/cogs` and `GET /api/accounting/reports/inventory-valuation` accept date, outlet, warehouse, location, product, and variant filters. Tenant business scope is enforced by the existing tenant access service.
- Frontend: Cost of Goods Sold and Inventory Valuation pages provide filters, source drill-down rows, totals, reconciliation differences, and loading, empty, and error states.

#### Acceptance criteria
- COGS and inventory valuation are based on historical cost snapshots from posted journals.
- Refunds correctly reverse the original COGS effect.
- The report is consistent with posted journals and stock movements.

#### Required automated tests
- Sale return and reversal reduce COGS using posted journal credits
- GRN receipt cost is used for running weighted-average valuation
- Historical as-of replay, transfer value preservation, location isolation, reconciliation differences, unvalued movement visibility, and report permissions

#### Estimated complexity
- High

---

### P2-07 — Cash Book Report

#### Scope
- Build a cash-book report from the journal engine and account mappings.
- Support filtering by business, outlet, and date range.

#### Readiness review clarifications
- The sole financial source of truth is posted `JournalEntryLine` rows for the selected configured liquidity account. The report must not read or merge POS payments, expenses, supplier payments, cash sessions, or other operational tables as a second transaction source.
- A selected liquidity account is one of the resolved Cash, Petty Cash, or Default Bank account mappings. The report must return separate account books and running balances when more than one configured liquidity account is selected; it must not mix cash and bank into one running balance. Outlet-level cash, petty-cash, and bank overrides are included through the existing configuration resolver.
- Opening balance is the selected account's posted debit-minus-credit balance before `dateFrom`, after tenant and location scope are applied. Period movements are posted rows whose `PostingDate` is within the inclusive date range. For asset liquidity accounts, debit is an inflow and credit is an outflow. Running balance is opening balance plus cumulative inflows minus cumulative outflows, ordered by posting date, journal number, and journal ID.
- Closing balance is opening balance plus total inflows minus total outflows. It must equal the selected account's General Ledger balance for the same tenant, scope, and `dateTo`; any difference must be returned and displayed as a reconciliation exception, never silently adjusted.
- Include only journals with `Status = Posted`. When an original journal is reversed, its original posted effect remains in its original period and the linked posted reversal appears in its own posting period with its opposite cash/bank line. Draft, cancelled, rejected, and unposted source records are excluded.
- Report rows must carry journal number, journal type, reference type/id/number, posting date, business date where present, narration from the line description with header-description fallback, and available outlet, warehouse, customer, supplier, payment-method, and cost-center dimensions. Counter-account information is deterministic only as the other line or lines in the same journal; display the counterpart account names and net amount rather than inventing a payee or payer.
- Opening-balance journals, POS sales/refunds, expense postings, other-income postings, supplier payments/advances, cash-session surplus/shortage postings, and manual journals require no special operational-table joins: they participate whenever a posted line affects the selected liquidity account. This prevents duplication and preserves journal reversal behavior.
- Tenant business scope is derived from `ITenantAccessService`, not an unrestricted client-supplied business ID. Outlet and warehouse/location filters must be authorized through the existing location-access rules and must constrain both opening and in-period queries identically.

#### Existing functionality to reuse
- Existing accounting report service and reporting controllers
- Journal search and journal-line data already available through the journal engine

#### Dependencies
- P2-01 through P2-05 postings must already exist so the cash-book has real source data.

#### Required account mappings
- Cash on hand account
- Petty cash account when configured
- Default bank account
- Cash difference gain/loss accounts if the report should display those movements

#### Journal types and exact debit/credit rules
- No new posting family is required for this milestone.
- Cash book output is derived from journal lines posted to cash and bank accounts.

#### Posting trigger
- Reporting only; no new posting trigger.

#### Transaction/atomicity boundary
- Read-only reporting path.

#### Idempotency strategy
- Not applicable.

#### Reversal/cancellation behavior
- Report results must include reversal journals and keep the balance consistent with the posted journal timeline.

#### Business/outlet/warehouse scope
- Filter by tenant business, outlet, warehouse/location, account slot/account ID, and posting-date range.
- Business is the authenticated tenant context. A caller cannot use the report to select another business.

#### Permissions
- Accounting reporting permission for cash book.

#### Audit requirements
- Report generation does not need a separate audit trail beyond the journal lineage.

#### Database/API/frontend changes
- Backend: implemented `GET /api/accounting/reports/cash-book` with read-only DTOs accepting date range, configured account slots, location, journal type, reference type, and reference filters. It returns per-account opening, inflow, outflow, running, closing, GL balance, reconciliation difference, and journal-line drill-down rows.
- Frontend: implemented the Cash Book report page with Cash/Petty Cash/Bank selection, date and location filters, per-account totals, running-balance rows, source-document drill-down, and clear empty/error/reconciliation-exception states.

#### Acceptance criteria
- The report's opening balance plus inflows minus outflows equals its calculated closing balance for every selected account.
- The closing balance reconciles to the selected Cash, Petty Cash, or Bank GL account for the identical tenant, account, scope, and as-of date.
- Reversal journals, opening-balance journals, POS sales/refunds, expenses, other income, supplier payments/advances, cash-session differences, and manual journals appear only through their posted journal lines and in the correct posting period.
- Results can be filtered by authorized outlet, warehouse/location, account, and date range without cross-tenant data leakage.

#### Required automated tests
- Opening balance before range, inflow/outflow totals, running balance, closing balance, and GL reconciliation for cash and bank accounts
- Multiple selected configured liquidity accounts remain independently balanced
- Reversal and opening-balance journals appear in the correct posting period
- POS sale/refund, expense, other income, supplier payment/advance, cash-session difference, and manual-journal cash lines are included exactly once
- Outlet, warehouse/location, date-range, tenant-isolation, source/document, and permission filters work correctly

#### Estimated complexity
- Medium

---

### P2-08 — Phase 2 Reconciliation, Legacy Coexistence and Final Sign-Off

#### Scope
- Reconcile the new journal-backed accounting flow with the existing legacy finance endpoints and verify that the business can safely move forward.
- Define the transition boundaries for legacy coexistence and sign-off.

#### Existing functionality to reuse
- Existing legacy routes and accounting UI entry points
- Existing permissions, audit service, and regression test suite

#### Dependencies
- All prior Phase 2 milestones must be accepted.

#### Required account mappings
- All mapping slots used by the implemented milestones must be available and validated.

#### Journal types and exact debit/credit rules
- No new posting family in this milestone.
- The milestone is focused on verification and transition control.

#### Posting trigger
- No new posting trigger.

#### Transaction/atomicity boundary
- Regression and sign-off only.

#### Idempotency strategy
- Verify that all operational flows under review are safe from duplicate posting.

#### Reversal/cancellation behavior
- Confirm that reversal and cancellation behavior is consistent across milestones.

#### Business/outlet/warehouse scope
- Validate cross-scope behavior for business, outlet, warehouse, terminal, and cash-session context.

#### Permissions
- Confirm that backend authorization, seed roles, and frontend route guards align around the new accounting vocabulary.

#### Audit requirements
- Validate that the audit trail is complete for each posted business event.

#### Database/API/frontend changes
- Backend: final reconciliation review completed. P2-01 through P2-07 use the Journal Engine as the accounting source of truth; P2-06 and P2-07 independently reconcile report balances to matching GL-account journal-line totals.
- Frontend: final production build and configured frontend test suite completed successfully. P2-07 adds a reconciliation-exception state in the Cash Book UI.

#### Acceptance criteria
- All Phase 2 milestones pass regression and smoke tests.
- Legacy coexistence does not create diverging financial records.
- The implementation is ready for review and release.

#### Required automated tests
- Full backend accounting regression suite: passed
- Permission regression suite: passed within the accounting regression suite
- End-to-end smoke tests for sale, purchase, adjustment, cash-session, COGS valuation, and Cash Book posting: passed

#### Final reconciliation review outcome
- **Phase 2 Status: COMPLETED AND ACCEPTED.** P2-01 through P2-08 are completed and accepted.
- P2-01 sale/refund/exchange, P2-02 GRN, P2-03 supplier payment/advance, P2-04 stock adjustment, P2-05 cash variance, P2-06 COGS/inventory valuation, and P2-07 Cash Book are accepted as one journal-backed accounting system.
- P2-08 is accepted for Phase 2 final sign-off. No Phase 3 work is included in this plan update.

#### Post-Phase-2 Technical Debt / Future Enhancements
- Add retry-specific duplicate-submission regression tests for GRN completion and cash-session close.
- Add uniform fault-injection atomicity tests across all financially relevant operational posting services.
- Remove the dormant legacy `Account`/`Transaction`/`Bill` model or add an explicit runtime guard before any legacy posting path can be re-exposed.
- Centralize accounting rounding thresholds and policy instead of retaining service-local constants.
- Define and implement a sensitive journal-line masking policy.
- Automate inter-outlet liquidity-account mappings where business workflows require them.
- Add AP/AR ageing reconciliation.

#### Preserved Legacy-Data Limitation
- Historical movements without a reliable cost basis remain unvalued and must never be silently estimated using current product cost. They remain explicitly identifiable and are excluded from precise historical valuation; reports must surface the resulting reconciliation difference until an approved, auditable backfill or estimation policy exists.

#### Estimated complexity
- Medium

## 7. Recommended first milestone

P2-01 should be the first implementation slice because it exercises the new posting service in the highest-volume workflow and provides the best signal on whether the accounting pattern is correct before the purchase, inventory, and cash-session milestones are introduced.

## 8. Major accounting risks to monitor during Phase 2

- Historical cost drift: if sale or GRN postings use current product cost instead of historical cost, COGS and valuation will be wrong.
- Partial return valuation: partial refunds must reference the original sale cost basis and not recompute from the current inventory view.
- Reconstructability risk: the current StockLedger preserves quantity, movement type, source reference, and timestamp, but it does not yet preserve unit cost, movement value, or a durable cost-basis field needed to reconstruct historical valuation without re-deriving from other systems.
- Duplicate posting: retries and duplicate submits can create duplicate journals unless the idempotency strategy is enforced at the database layer.
- Closed-period risk: posting into a closed or locked period must be blocked consistently across API and service layers.
- Legacy coexistence risk: old finance routes and new journal-backed flows must not both write to the same financial state without a clear guard.
- Permission drift: backend policy, seed role data, and frontend route access must stay aligned.

## 9. Implementation guardrails

- Do not create a new accounting engine in parallel with Phase 1.
- Do not add any journal creation logic directly inside controllers.
- Do not mutate posted journals; use reversal journals for corrections.
- Do not use account balances as a source of truth for operational calculations.
- Do not allow posting without a valid financial year and accounting period resolution.
