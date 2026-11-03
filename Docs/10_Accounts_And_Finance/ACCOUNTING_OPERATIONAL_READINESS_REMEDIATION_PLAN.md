# Accounting Operational Readiness Remediation Plan

Updated: 2026-09-06
Status: Slice 1 and Slice 2 implemented. Slice 3 not started.

## Frozen Remediation Boundary

This plan follows `ACCOUNTING_OPERATIONAL_CONTROLS_PERMISSIONS_AND_AUDIT_GAP_ASSESSMENT.md` and is limited to operational readiness remediation. Phase 1-3 Accounting remains signed off, P3-08 remains final accepted, P3-03/P3-04 remain Deferred / Not Required, and Phase 4 has not started.

The frozen remediation slices are:

1. **Slice 1 - Accounting Manager Role & Governance Audit Controls**
2. **Slice 2 - Core Financial Transaction Audit Coverage**
3. **Slice 3 - Banking/Reconciliation Audit Coverage & Final Operational Acceptance**

No final operational-readiness acceptance checklist is created before Slice 3.

## Slice 1 - Accounting Manager Role & Governance Audit Controls

Status: Implemented - Ready for Bounded Review

### Accounting Manager Permission Composition

Role name: `Accounting Manager`

Granted permissions:

- `suppliers.view`
- `accounting.dashboard.view`
- `accounting.chart_accounts.view`
- `accounting.chart_accounts.create`
- `accounting.chart_accounts.edit`
- `accounting.configuration.view`
- `accounting.journals.view`
- `accounting.manual_journals.view`
- `accounting.manual_journals.create`
- `accounting.manual_journals.update`
- `accounting.manual_journals.submit`
- `accounting.manual_journals.cancel`
- `accounting.expenses.view`
- `accounting.expenses.create`
- `accounting.expenses.edit`
- `accounting.expenses.submit`
- `accounting.expenses.cancel`
- `accounting.supplier_payments.view`
- `accounting.supplier_payments.create`
- `accounting.supplier_payments.submit`
- `accounting.supplier_payments.cancel`
- `accounting.supplier_advances.view`
- `accounting.supplier_advances.create`
- `accounting.supplier_advances.submit`
- `accounting.supplier_advances.cancel`
- `accounting.other_income.view`
- `accounting.other_income.create`
- `accounting.other_income.edit`
- `accounting.other_income.submit`
- `accounting.other_income.cancel`
- `accounting.settlements.view`
- `accounting.settlements.create`
- `accounting.liquidity_transfers.view`
- `accounting.liquidity_transfers.create`
- `accounting.statement_import.view`
- `accounting.statement_import.create`
- `accounting.reconciliation.view`
- `accounting.reconciliation.create`
- `accounting.reconciliation.match`
- `accounting.reconciliation.unmatch`
- `accounting.adjustments.propose`
- `accounting.exceptions.manage`
- `accounting.financial_years.view`
- `accounting.periods.view`
- `accounting.close.prepare`
- `accounting.close.audit.view`
- `accounting.reports.general_ledger`
- `accounting.reports.trial_balance`
- `accounting.reports.profit_loss`
- `accounting.reports.balance_sheet`
- `accounting.reports.cash_book`
- `accounting.reports.cash_flow`
- `accounting.reports.profitability`
- `accounting.reports.cogs`
- `accounting.reports.inventory_valuation`
- `accounting.reports.ap_subledger`
- `accounting.reports.ap_aging`
- `accounting.reports.supplier_statement`
- `accounting.reports.ap_reconciliation`
- `accounting.reports.vat`
- `reports.export`

### Explicit Exclusions and Segregation Decisions

The role intentionally excludes:

- `*`
- `users.*`
- `roles.*`
- `settings.edit`
- `businesses.*`
- `accounting.configuration.edit`
- `accounting.journals.create/update/cancel/post/reverse`
- `accounting.manual_journals.approve/reject/post/reverse`
- `accounting.expenses.approve/reject`
- `accounting.supplier_payments.approve/reject/reverse`
- `accounting.supplier_advances.approve/reject/reverse`
- `accounting.other_income.approve/reject`
- `accounting.settlements.post/reverse`
- `accounting.liquidity_transfers.post/reverse`
- `accounting.reconciliation.close/reopen`
- `accounting.adjustments.approve/reverse`
- `accounting.periods.close/reopen`
- `accounting.close.approve/execute/override_exception`
- `accounting.financial_years.manage/reopen`
- `accounting.opening_balances.post/adjust`

Segregation treatment:

- Accounting Manager is a maker/reviewer role for routine finance operations.
- Approval, posting, reversal, close execution, reopen and override duties remain with existing approver/admin/owner roles or a future separately approved approver role.
- Legacy finance write permissions remain harmless because P3-08 backend write guards reject legacy Account/Transaction/Bill writes regardless of aliases.

### Seed Behavior

Implemented through existing role architecture:

- `AccountingManagerPermissions` centralizes the runtime role name and permission list.
- `DbSeeder` now ensures `Accounting Manager` idempotently on already-seeded databases.
- `GetDefaultRoles()` includes `Accounting Manager` for fresh seeds.
- `BusinessOnboardingService` ensures the role exists but does not create or assign an Accounting Manager user.
- `database/seed-data.sql`, `database/seed-v2-roles.sql`, and `database/add-accounting-v2-permissions.sql` include idempotent role seeding.

Preserved behavior:

- `AccountsAdmin` is not renamed or narrowed.
- Existing roles/users are not automatically moved.
- No permanent Accounting Manager user is created.
- No migration was created.

### Permission Refresh / Revocation Behavior

Retained authentication behavior:

- Login and token refresh compute effective permissions from current role/user permission data.
- `/auth/me` recomputes permissions from database state.
- Already issued access tokens retain embedded permission claims until refresh, re-login or access-token expiry.
- Current default access-token lifetime remains `JwtSettings.ExpirationMinutes = 60` unless deployment configuration overrides it.

Operational instruction:

- For normal permission changes, instruct affected users to refresh/re-login for updated UI and API claims.
- For urgent revocation, deactivate the user and revoke/rotate refresh tokens through existing logout/session procedures where available; immediate global access-token invalidation remains a future optional hardening item and was not implemented in Slice 1.

### Audited Governance Actions Implemented

- Role creation.
- Role update.
- Permission added/removed from a role, captured as before/after `Permissions` field values.
- Role deletion where currently supported.
- User creation with role/business/outlet/permission/is-active fields.
- User role assignment/removal via update with before/after `RoleId`.
- Direct user permission override changes via update with before/after `PermissionOverrides`.
- User activation/deactivation and soft delete with before/after `IsActive`.

Sensitive data treatment:

- Passwords, password hashes, tokens and refresh tokens are not included in audit field changes.

### Audited Configuration and Master-Data Actions Implemented

- Business accounting configuration explicit update/create through `UpdateBusinessConfigurationAsync`.
- Outlet accounting override create/update through `UpsertOutletConfigurationAsync`.
- Chart of Account create.
- Chart of Account update, including name, parent, control/manual-posting flags, active flag and display order.
- Chart of Account deactivation.
- Bank Account create through `BankAccountService.CreateAsync`.

### Bank Account Mutation Patching Review

Status: Completed - no additional production mutation surface found.

| Operation | Classification | Evidence | Audit treatment |
| --- | --- | --- | --- |
| Create | Audited and Verified | `BankAccountService.CreateAsync(BankAccountDto, actorUserId)` is the only Bank Account mutation method found; `BankAccountDto` includes `DisplayName`, `AccountReference`, `ChartAccountId`, `OutletId`, `IsActive`, `ReconciliationStartDate`, and `OpeningStatementBalance`. | Audit records actor, timestamp, `BankAccount` entity ID, Business scope, `DisplayName`, `ChartAccountId`, `OutletId`, `IsActive`, `ReconciliationStartDate`, and `OpeningStatementBalance`. `AccountReference` is intentionally excluded from field-change audit evidence. Failed duplicate create leaves no second success audit event. |
| Update | Not Applicable - no supported mutation surface | No `UpdateAsync` method exists in `BankAccountService`; no `PUT/PATCH /api/accounting/bank-accounts` controller route exists; Angular only reads bank accounts through `AccountingService.getBankAccounts()`. | No audit required until a supported update surface exists. |
| Activation/deactivation or status change | Not Applicable - no supported mutation surface | `BankAccount.IsActive` exists on the entity/DTO and is set during create, but there is no service/controller/UI route to mutate it after creation. | No audit required until a supported status mutation surface exists. |
| Linked Chart Account change | Not Applicable - no supported mutation surface | `ChartAccountId` is supplied only at Bank Account create; no Bank Account update API exists to change the link. | Linked account ID is audited on create; no post-create link-change audit is applicable. |
| Business/Outlet scope change | Not Applicable - no supported mutation surface | `BusinessId` is assigned from `ITenantAccessService.RequireBusinessId()` during create; optional `OutletId` is validated during create. No post-create Business/Outlet move/update method exists. | Business scope and optional Outlet ID are audited on create; no post-create scope-change audit is applicable. |
| Delete | Not Applicable - no supported mutation surface | No delete method or endpoint exists for `BankAccount`. | No audit required until a supported delete surface exists. |

Deferred from Slice 1 by boundary:

- Supplier Payment, Supplier Advance, GRN, settlement, liquidity transfer posting, bank statement import, matching/unmatching, reconciliation close/reopen, exception classification/resolution and bank adjustment proposal/approval/reversal audit coverage. These are assigned to Slice 2 or Slice 3.

### Tests and Results

Focused validations:

- Accounting Manager role/seed/user/permission refresh/governance/master-data audit tests: 7/7 passed.
- Bank Account mutation patching review focused backend bundle: 35/35 passed.
- Backend operational-control, audit, permission, tenant-isolation and legacy-guard regression bundle: 70/70 passed.
- Focused Angular Accounting Manager menu test: 14/14 passed.

Full validations:

- Full backend regression suite: 491/491 passed.
- Backend Release build: passed.
- Full Angular test suite: 58/58 passed.
- Angular production build: passed.
- Final `git diff --check`: passed with CRLF normalization warnings only.

### Retained Limitations

- Already issued access tokens remain claim-static until refresh/re-login/expiry. This is documented and retained; no token blacklist/session redesign was implemented.
- Accounting Manager is not an approver role. A future `Accounting Approver` role may be useful, but it is not introduced in Slice 1.
- Bank Account update/status/link/scope/delete audit is not applicable to current production behavior: only create is supported. Corrections currently require creating a new Bank Account record and using the new record in downstream reconciliation/settlement workflows, or a future approved Bank Account maintenance feature.

### Migration

No migration was created.

## Slice 2 - Core Financial Transaction Audit Coverage

Status: Implemented - Ready for Bounded Review

### Slice 2 Patching Review Coverage Matrix

| Workflow | Supported action | Classification | Actor | Status / before-after | Journal linkage | Test evidence |
| --- | --- | --- | --- | --- | --- | --- |
| Supplier Payment | Create, submit, approve/post | Audited and Verified | Controller/service passes the initiating user; audit metadata stores `actorUserId`; audit context supplies Business and timestamp. | Draft -> Submitted -> Posted; payment amount and payable allocations are recorded. | Payment source ID/reference links to its posted Journal Entry. | `SupplierPaymentWorkflowServiceTests.ApproveAsync_PostsPaymentJournal_AndCreatesAdvanceForOverpayment`; `P302SupplierPaymentAllocationAtomicityTests.ApproveAsync_RollsBackJournalAndAllocations_WhenAllocationActivationPersistenceFails_ThenRetriesSafely`. |
| Supplier Payment | Reverse | Audited and Verified | Initiating user is passed to reversal audit and reversal Journal Entry. | Posted -> Reversed; required reason and allocation restoration are recorded. | Original and reversal Journal Entry IDs remain linked to the payment source; history is retained. | `SupplierPaymentWorkflowServiceTests.ReverseAsync_ReversesPostedPaymentAndEffectiveAllocationWithoutDeletingHistory`; `P302SupplierPaymentReversalAtomicityTests.ReverseAsync_ReversalJournalBoundaryFailure_RollsBackEverything_ThenRetriesWithoutDuplicate`. |
| Supplier Advance | Create, submit, approve/post | Audited and Verified | Initiating user; Business, source and timestamp retained in audit context/metadata. | Draft -> Submitted -> Posted; amount and advance status are captured. | Advance source ID/reference links to the posted Journal Entry. | `SupplierPaymentWorkflowServiceTests.CreateAdvanceAsync_AndApproveAdvance_PostsAdvanceJournal`. |
| Supplier Advance | Apply utilization / reverse utilization | Audited and Verified | Initiating user is stored on each utilization action. | Utilization status and amount/payable allocation changes are recorded; reversal reason is required. | Utilization source, payable and advance references plus related Journal Entry/reversal IDs are retained. | `SupplierPaymentWorkflowServiceTests.ApplyAdvanceAsync_AndReverseUtilization_RecordAuditWithJournalLinkage`; `P302SupplierAdvanceUtilisationAtomicityTests.ApplyAdvanceAsync_PartialUtilisation_RollsBackWhenPersistenceFails_ThenRetriesSafely`; `P302SupplierAdvanceUtilisationAtomicityTests.ReverseAdvanceUtilizationAsync_DuplicateReversal_WhenReversalRepeated_ThenIdempotent`. |
| GRN | Create and complete receipt | Audited and Verified | Authenticated creator/completer is passed through controller and service; completion actor now also owns the Journal Entry and stock-ledger mutation. | Full -> Completed; PO status, received quantity, receipt amount and VAT are recorded. | GRN ID/reference -> purchase receipt Journal Entry; audit captures GRN, PO, supplier, warehouse and journal IDs. | `PurchaseAndGrnAccountingTests.GrnCompletion_PostsPurchaseReceiptJournal_UsingGrnUnitCost`; `P308RetryAtomicityTests.GrnCompletionRetry_DoesNotDuplicateInventoryOrAuthoritativeJournal`. |
| GRN | Duplicate completion / retry | Audited and Verified | Retry does not replace or invent an actor; a completed GRN is rejected before a second success audit. | Inventory, GRN and PO completion state are not applied twice. | Existing posted Journal Entry is the sole authoritative receipt; rollback/retry is atomic. | `PurchaseAndGrnAccountingTests.GrnCompletion_PostsPurchaseReceiptJournal_UsingGrnUnitCost`; `P302RelationalFaultInjectionTests.CompleteAsync_RollsBackJournalAndGrn_WhenPayableProjectionPersistenceFails_ThenRetriesSafely`. |
| Sale | Complete/post | Audited and Verified | Cashier initiating the sale is the actor; Business and Outlet are retained. | New -> Completed; amount, payment, items, exchange source (when applicable) and journal ID captured. | Sale ID/reference -> posted sale Journal Entry. | `SaleServiceAccountingJournalTests.CreateAsync_PostsSaleJournal_WhenAccountingServicesAreAvailable`; `SaleServiceVatAndExchangeAccountingTests.CreateAsync_PersistsExchangeLinkage_ForReplacementSale`. |
| Sale | Void / refund | Audited and Verified | Controller requires a positive authenticated user ID; the same action actor is used for the sale audit, stock ledger and reversal Journal Entry. No original-cashier fallback is allowed. | Completed -> Voided/Refunded; reason and material value/status change recorded. | Sale source -> reversal Journal Entry; refund and void audit retain journal reference. | `SalesControllerActorTests.VoidAndRefund_RejectRequestsWithoutPositiveAuthenticatedUserId`; `SaleServiceVoidJournalTests.VoidAsync_WritesSaleVoidJournal_WithActorAndReason`; `SaleServiceVatAndExchangeAccountingTests.RefundAsync_PostsVatReversal_ForPartialRefundOnly` (actor 99 differs from Sale cashier); `SaleServiceHistoricalCostSnapshotTests`. |
| Cash Session | Open and close | Audited and Verified | Initiating user is retained in session and audit metadata; Business/Outlet/Terminal and timestamp are included. | New -> Open -> Closed; expected/actual cash, variance and closing note are captured. | Cash Session ID/reference links to a variance Journal Entry when threshold is exceeded; no journal is created for immaterial variance. | `CashSessionServiceTests.OpenAsync_CreatesOpenCashSession_ForActiveTerminal`; `CashSessionServiceTests.CloseAsync_PostsVarianceJournal_WhenDifferenceIsNonZero`. |
| Cash Session | Duplicate close / retry | Audited and Verified | The original close actor remains authoritative; retry creates no second successful close audit. | Already Closed is returned without applying another close. | One variance Journal Entry at most for the Cash Session source. | `CashSessionServiceTests.CloseAsync_WhenAlreadyClosed_DoesNotRecordSecondSuccessfulCloseAudit`; `CashSessionServiceTests.CloseAsync_IsIdempotent_WhenSessionIsAlreadyClosed`; `P308RetryAtomicityTests.CashSessionCloseRetry_ReturnsClosedSessionAndDoesNotDuplicateVarianceJournal`. |
| Stock Adjustment | Create, update, submit, approve, reject, cancel | Audited and Verified | Initiating user and Business/Outlet or warehouse location are retained. | Status transitions and line-level before/after quantity/value summaries are recorded; rejection reason is retained. | No journal before approval; approval links the resulting gain/shortage Journal Entry. | `StockAdjustmentWorkflowHttpTests.Submit_TransitionsDraftToPendingApproval`; `StockAdjustmentWorkflowHttpTests.Approve_ForPositiveQuantityChange_PostsInventoryGainJournal`; `StockAdjustmentWorkflowHttpTests.Reject_DoesNotUpdateInventory`; `StockAdjustmentWorkflowHttpTests.Cancel_DoesNotUpdateInventory`. |
| Stock Adjustment | Reverse approved adjustment | Audited and Verified | Reversing user and required reason retained. | Approved -> Reversed; inventory quantity is restored and reversal reason recorded. | Original and reversal Journal Entry IDs are included in audit metadata/fields. | `StockAdjustmentWorkflowHttpTests.Reverse_ApprovedGainAdjustment_CreatesReversalJournalAndRestoresInventory`; `StockAdjustmentWorkflowHttpTests.Reverse_ApprovedShortageAdjustment_CreatesReversalJournalAndRestoresInventory`. |
| Manual Journal | Create draft | Audited and Verified | Controller passes user ID; audit context stores request actor; metadata stores `createdBy`. | `Status: null -> Draft`; source reference and line totals captured. | Draft journal ID is the primary entity and `journalId`. | `JournalEntryServiceTests.ManualJournalLifecycle_AuditPayloadsIncludeStatusReferencesActorsAndJournalLinks`; `ManualJournalAuditTests`. |
| Manual Journal | Update draft/rejected | Audited and Verified | Controller passes user ID; metadata stores `updatedBy`. | Header/reference/date/outlet/warehouse, line count, debit total and credit total before/after captured. | Same journal ID retained. | `JournalEntryServiceTests.ManualJournalLifecycle_AuditPayloadsIncludeStatusReferencesActorsAndJournalLinks`. |
| Manual Journal | Submit | Audited and Verified | Controller passes user ID; metadata stores `submittedBy`. | `Draft/Rejected -> Submitted`; submitted timestamp captured. | Same journal ID retained. | `JournalEntryServiceTests.ManualJournalLifecycle_AuditPayloadsIncludeStatusReferencesActorsAndJournalLinks`; `ManualJournalAuditTests`. |
| Manual Journal | Approve | Audited and Verified | Controller passes user ID; metadata stores `approvedBy`. | `Submitted -> Approved`; approved timestamp captured. | Same journal ID retained. | `JournalEntryServiceTests.ManualJournalLifecycle_AuditPayloadsIncludeStatusReferencesActorsAndJournalLinks`; `ManualJournalAuditTests`. |
| Manual Journal | Reject | Audited and Verified | Controller passes user ID; metadata stores `rejectedBy`. | `Submitted -> Rejected`; rejection reason and rejection timestamp captured. | Same journal ID retained; no posting journal created. | `JournalEntryServiceTests.ManualJournalLifecycle_AuditPayloadsIncludeStatusReferencesActorsAndJournalLinks` (asserts actor, Business, reference, status and reason); `AccountingJournalsController` reject route. |
| Manual Journal | Post | Audited and Verified | Controller/system caller passes user ID; metadata stores `postedBy`. | `Approved -> Posted` in approval-required mode or `Draft -> Posted` in direct-post mode; permanent journal number and posted timestamp captured. | Posted journal ID is the primary entity and `journalId`. Retry on already posted/reversed returns existing journal without duplicate success audit. | `JournalEntryServiceTests.ManualJournalLifecycle_AuditPayloadsIncludeStatusReferencesActorsAndJournalLinks`; `JournalEntryServiceTests.CreateOrGetSystemDraftAsync_IsIdempotentByTypeAndReference`; `P308RetryAtomicityTests`. |
| Manual Journal | Reverse | Audited and Verified | Controller/system caller passes user ID; metadata stores `reversedBy`. | Original posted journal becomes reversed; reason captured. | Audit records original journal ID and reversal journal ID. Existing reversal path returns without duplicate success audit. | `JournalEntryServiceTests.ManualJournalLifecycle_AuditPayloadsIncludeStatusReferencesActorsAndJournalLinks`; `JournalEntryServiceTests.ReverseAsync_CreatesReversal_AndMarksOriginal`. |
| Manual Journal | Cancel | Audited and Verified | Controller passes user ID; metadata stores `cancelledBy`. | Pre-post status -> Cancelled; cancellation reason captured. | Same journal ID retained; no posting journal created. | `JournalEntryServiceTests.ManualJournalLifecycle_AuditPayloadsIncludeStatusReferencesActorsAndJournalLinks`; `JournalEntryServiceTests.CancelDraftAsync_CancelsDraft_AndRejectsPosting`. |
| Manual Journal | Delete | Not Applicable - no supported action | No controller/service delete endpoint for journal entries. | Not applicable. | Not applicable. | Controller route inventory. |
| Manual Journal | Reopen / return to draft | Not Applicable - no supported action | No controller/service reopen endpoint exists for journal entries. | Rejected journals are editable/submittable, but not through a distinct reopen action. | Not applicable. | Controller/service route inventory. |
| Opening Balance | Create/preparation | Accepted System-Generated Behavior | User calls opening-balance post; service creates system draft with the same actor. | System draft `null -> Draft` is audited by `JournalEntryService`. | Draft opening-balance journal ID is retained. | `OpeningBalanceAuditTests`; `OpeningBalanceServiceTests`. |
| Opening Balance | Post | Audited and Verified | Controller passes user ID; audit context stores request actor; journal metadata stores `postedBy`. | Draft -> Posted; Business, source reference `OpeningBalance`, status and permanent journal number captured. | Posted opening-balance journal ID is the source record and resulting journal. | `OpeningBalanceAuditTests`; `OpeningBalanceWorkflowHttpTests`. |
| Opening Balance | Update | Not Applicable - no supported action | Posted opening balances are immutable; corrections use adjustments. | Historical posted opening balances remain unchanged. | Not applicable. | `OpeningBalanceService.PostOpeningBalanceAsync`; `JournalEntryService.ReverseAsync` guard. |
| Opening Balance | Validate/submit | Not Applicable - no supported action | There is no separate submit endpoint; validation is part of post/adjustment. | Not applicable. | Not applicable. | `AccountingOpeningBalancesController` route inventory. |
| Opening Balance | Reverse | Not Applicable - no supported action | `JournalEntryService.ReverseAsync` rejects `OpeningBalance` and `OpeningBalanceAdjustment` journals. | Historical posted opening balances remain unchanged. | Not applicable. | `JournalEntryService.ReverseAsync` guard. |
| Opening Balance | Correction/re-post | Audited and Verified | Controller passes user ID to `PostAdjustmentAsync`. | New adjustment journal is created/posted; reason captured in description and audit metadata. | Original opening-balance journal ID remains; adjustment journal ID is posted separately. | `OpeningBalanceAuditTests`; `OpeningBalanceWorkflowHttpTests`. |
| Expense | Create | Audited and Verified | Controller passes user ID; audit context stores request actor. | `null -> Draft`; amount, VAT/tax, category/account/outlet/reference captured. | No journal until approve. | `ExpenseWorkflowServiceTests`; `ExpenseWorkflowHttpTests`. |
| Expense | Update / material VAT-account-amount change | Audited and Verified | Controller passes user ID; audit context stores request actor. | Amount, VAT rate/amount, taxable amount, tax code, tax-inclusive flag, category, payment account, outlet and reference before/after captured. | No journal until approve. | `ExpenseWorkflowServiceTests.UpdateAsync_AuditsMaterialAmountVatAndAccountChanges`; `ExpenseWorkflowHttpTests`. |
| Expense | Submit | Audited and Verified | Controller passes user ID. | Draft/Rejected -> Submitted. | No journal until approve. | `ExpenseWorkflowServiceTests`; `ExpenseWorkflowHttpTests`. |
| Expense | Approve/post | Audited and Verified | Controller passes user ID. | Submitted -> Approved; expense account and `JournalEntryId` captured. | Posted system journal reference type `Expense`, source ID = expense ID. | `ExpenseWorkflowServiceTests.ApproveAsync_PostsJournal_FromCategoryMapping`; `ExpenseWorkflowHttpTests`. |
| Expense | Reject | Audited and Verified | Controller passes user ID. | Submitted -> Rejected; rejection reason captured. | No journal created. | `ExpenseWorkflowServiceTests.RejectAndCancel_DoNotCreateJournals`. |
| Expense | Cancel | Audited and Verified | Controller passes user ID. | Old status -> Cancelled; cancellation reason captured. | No journal created. | `ExpenseWorkflowServiceTests.RejectAndCancel_DoNotCreateJournals`. |
| Expense | Delete | Audited and Verified | Legacy delete controller now passes current user ID; audit includes `DeletedBy`. | Draft expense deleted. | No journal created. | `ExpenseWorkflowServiceTests`; controller route inventory. |
| Expense | Reverse | Not Applicable - no supported action | No expense reversal endpoint/service method exists. | Corrections require approved future workflow. | Not applicable. | `ExpenseService` and controller route inventory. |
| Other Income | Create | Audited and Verified | Controller passes user ID; audit context stores request actor. | `null -> Draft`; amount, VAT/tax, income/payment account, outlet/reference captured. | No journal until approve. | `OtherIncomeWorkflowServiceTests`; `OtherIncomeWorkflowHttpTests`. |
| Other Income | Update / material VAT-account-amount change | Audited and Verified | Controller passes user ID. | Amount, VAT rate/amount, taxable amount, tax code, tax-inclusive flag, income category/account, payment account, outlet and reference before/after captured. | No journal until approve. | `OtherIncomeWorkflowServiceTests.UpdateAsync_AuditsMaterialAmountVatAndAccountChanges`; `OtherIncomeWorkflowHttpTests`. |
| Other Income | Submit | Audited and Verified | Controller passes user ID. | Draft/Rejected -> Submitted. | No journal until approve. | `OtherIncomeWorkflowServiceTests`; `OtherIncomeWorkflowHttpTests`. |
| Other Income | Approve/post | Audited and Verified | Controller passes user ID. | Submitted -> Approved; income account and `JournalEntryId` captured. | Posted system journal reference type `OtherIncome`, source ID = income ID. | `OtherIncomeWorkflowServiceTests.ApproveAsync_PostsJournal_UsingDefaultConfigurationAccount`; `OtherIncomeWorkflowHttpTests`. |
| Other Income | Reject | Audited and Verified | Controller passes user ID. | Submitted -> Rejected; rejection reason captured. | No journal created. | `OtherIncomeWorkflowServiceTests.RejectAndCancel_DoNotCreateJournals`. |
| Other Income | Cancel | Audited and Verified | Controller passes user ID. | Old status -> Cancelled; cancellation reason captured. | No journal created. | `OtherIncomeWorkflowServiceTests.RejectAndCancel_DoNotCreateJournals`. |
| Other Income | Delete/reverse | Not Applicable - no supported action | No delete or reverse endpoint/service method exists. | Not applicable. | Not applicable. | Controller/service route inventory. |
| Return | Not a separate workflow | Accepted System-Generated Behavior | Return inventory movement is generated through `SaleService.RefundAsync` or `VoidAsync`. | Sale status moves to Refunded or Voided; reason and restored quantity captured. | Refund/void uses `SaleRefund` reversal posting when accounting services are configured. | `SaleServiceHistoricalCostSnapshotTests`; `SaleServiceVatAndExchangeAccountingTests`; `SaleServiceVoidJournalTests`. |
| Exchange | Replacement sale with original-sale linkage | Audited and Verified | Cashier/user creates replacement sale. | Sale completion audit includes `OriginalSaleId`; original sale stores `ReplacementSaleId`. | Replacement sale journal ID is captured; exchange return side maps through refund when used. | `SaleServiceVatAndExchangeAccountingTests.CreateAsync_PersistsExchangeLinkage_ForReplacementSale`. |
| Stock Count | Create/upload/submit/approve/reject/reopen | Partially Audited (non-financial count lifecycle) | User IDs and lifecycle timestamps are persisted on the Stock Count; the phase-1 count lifecycle does not write separate financial audit events. | Count status and physical/system quantity variance are recorded; count approval alone does not mutate inventory. | No Journal Entry or financial posting at this stage. | `StockCountPhase1HttpTests.SubmitRejectReopen_WithPermission_TransitionsStatus`; `StockCountPhase1HttpTests.Approve_AndGenerateAdjustmentDraft_TransitionsAndCreatesDraftLines`. |
| Stock Count | Generate financial correction | Audited and Verified via Stock Adjustment | Initiating user is stored on generated Stock Adjustment; source count ID is retained. | Count moves to `AdjustmentGenerated`; draft adjustment contains summarized variance quantities. No inventory value changes until approval. | Generated Stock Adjustment approval posts and audits its Journal Entry; reversal is through the same Stock Adjustment. | `StockCountPhase1HttpTests.Approve_AndGenerateAdjustmentDraft_TransitionsAndCreatesDraftLines`; `StockCountPhase1HttpTests.GeneratedAdjustment_Approval_MarksStockCountCompleted`; `StockAdjustmentWorkflowHttpTests.Approve_ForPositiveQuantityChange_PostsInventoryGainJournal`. |
| Stock Count | Direct financial posting / reversal | Not Applicable - no supported action | No direct Stock Count posting/reversal endpoint exists. | Financial correction is delegated to Stock Adjustment. | Not applicable except through Stock Adjustment. | `StockCountService.GenerateAdjustmentDraftAsync`; `StockCountsController.GenerateAdjustmentDraft`. |
| User/system actor treatment | Direct user actions | Audited and Verified | Normal HTTP requests populate `RealUserId`; `ActingUserId` remains null unless role-switch/acting headers are present. Services also persist action actor columns or metadata. | Action timestamps come from entity lifecycle columns and audit `CreatedAt`. | Source transaction references remain in journal/audit metadata. | `ExpenseWorkflowHttpTests`; `OtherIncomeWorkflowHttpTests`; `AccountingOperationalReadinessSlice1Tests`. |
| User/system actor treatment | Background/system-generated postings | Accepted System-Generated Behavior | Slice 2 has no independent background posting worker. System-generated journals are created by user-initiated workflows with that initiating user ID; no fake or arbitrary user is assigned. `AuditService` uses `SystemJob` only when there is no HTTP audit context. | `AuditService` supplies event `CreatedAt`; all posting methods receive an actor ID. | Source reference and idempotency discriminator tie each system journal to its source action. | `JournalEntryServiceTests.CreateOrGetSystemDraftAsync_IsIdempotentByTypeAndReference`; `AuditServiceTests`; `Program.cs` hosted-service registration (only subscription reminder job). |
| Failure/rollback behavior | Posting/audit failures, duplicates and unauthorized actions | Audited and Verified | Failed transactional paths roll back source status and journals; forbidden HTTP requests are blocked before successful financial audit events. | No misleading successful audit is retained for representative failed/rolled-back paths. | Duplicate/idempotent paths return existing records or reject before second success evidence. | `OtherIncomeAtomicityTests`, `P305ExpenseVatAtomicityTests`, `P302SupplierPayment*AtomicityTests`, `P308RetryAtomicityTests`, duplicate GRN/Cash tests, Expense/Other Income forbidden tests. |

### Unsupported / Not Applicable Mutation Surfaces

- Supplier Payment update/reject endpoints are not currently supported by `AccountingSupplierPaymentsController`/`ISupplierPaymentService`; no audit is applicable until a supported mutation surface exists.
- Supplier Advance update/reject endpoints are not currently supported; no audit is applicable until a supported mutation surface exists.
- Direct Stock Count financial posting is not a separate supported accounting workflow in this slice; stock-count corrections post financially through Stock Adjustment.

### Explicitly Deferred to Slice 3

- Bank settlement post/reverse audit.
- Liquidity transfer post/reverse audit.
- Bank statement import audit.
- Bank reconciliation match/unmatch audit.
- Reconciliation close/reopen audit.
- Exception classification/resolution audit.
- Bank adjustment proposal/approval/reversal audit.

### Slice 2 Tests and Results

Focused validation:

- Sale void/refund/exchange and actor-propagation focused tests: 32/32 passed.
- Combined Slice 2 focused regression bundle covering Manual Journal, Opening Balance, Expense, Other Income, Return/Refund/Exchange, Stock Count/Adjustment, supplier workflows, GRN, Cash Session, actor, permission, retry and rollback evidence: 160/160 passed.
- Full backend regression suite: 498/498 passed.
- Backend Release solution build: passed with 0 errors and 4 existing warnings.
- Angular code changed: no. Angular tests/build were not required for this patching review.
- `git diff --check`: passed.

Known warnings are unchanged: `SQLitePCLRaw.lib.e_sqlite3` NU1903 advisory and existing P306 xUnit analyzer warnings.

### Migration

No migration was created.

Do not include banking/reconciliation workflows in Slice 2 unless a genuine dependency is found.

## Slice 3 - Banking/Reconciliation Audit Coverage & Final Operational Acceptance

Status: Not started.

Bounded future scope:

- Bank statement import audit.
- Match/unmatch audit.
- Reconciliation close/reopen audit.
- Exception classification/resolution audit.
- Bank adjustment proposal/approval/reversal audit.
- Settlement post/reverse audit.
- Liquidity transfer post/reverse audit.
- Final operational-readiness acceptance checklist after Slice 3 validation.

Do not begin Phase 4 until the remediation slices are complete and accepted.

## Known Non-Blocking Advisories

- Existing `SQLitePCLRaw.lib.e_sqlite3` NU1903 advisory remains in backend test/build output.
- Existing xUnit analyzer warnings remain in `P306AdjustmentRelationalAtomicityTests`.
- Existing jsdom download-navigation advisory remains from the accepted close-workbench Angular spec during full Angular tests.

## Final Status

**Accounting Operational Readiness Remediation Slice 2 Complete – Ready for Final Slice 2 Acceptance**. Slice 3 not started; Phase 4 not started; final operational-readiness acceptance checklist not created.
