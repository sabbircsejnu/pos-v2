# P2-04 Acceptance Checklist — Stock Adjustment / Stock Count Accounting

Updated: 2026-08-09
Scope: P2-04 stock adjustment / stock count accounting slice
Source of truth: [Docs/10_Accounts_And_Finance/PHASE_2_ACCOUNTING_DEVELOPMENT_PLAN.md](Docs/10_Accounts_And_Finance/PHASE_2_ACCOUNTING_DEVELOPMENT_PLAN.md)

Status legend:
- Passed
- Partially Passed
- Failed
- Not Applicable

---

## 1. Approval and scope gate

- P2-04 covers stock adjustment and stock-count accounting behavior for gains, shortages, linkage, permissions, and audit expectations.
- P2-05 remains out of scope for this acceptance review.

---

## 2. Requirement status matrix

| Requirement | Status | Evidence | Notes |
|---|---|---|---|
| Approved stock gain creates a balanced journal | Passed | [src/RetailPOS.API/Services/StockAdjustmentService.cs](src/RetailPOS.API/Services/StockAdjustmentService.cs) and [tests/RetailPOS.Tests/Inventory/StockAdjustmentWorkflowHttpTests.cs](tests/RetailPOS.Tests/Inventory/StockAdjustmentWorkflowHttpTests.cs) | Approval creates a two-line StockGainPosting journal and the test asserts matching debit and credit totals. |
| Approved stock shortage creates a balanced journal | Passed | [src/RetailPOS.API/Services/StockAdjustmentService.cs](src/RetailPOS.API/Services/StockAdjustmentService.cs) and [tests/RetailPOS.Tests/Inventory/StockAdjustmentWorkflowHttpTests.cs](tests/RetailPOS.Tests/Inventory/StockAdjustmentWorkflowHttpTests.cs) | Approval creates a two-line StockShortagePosting journal and the test asserts matching debit and credit totals. |
| Stock adjustment approval is the accounting posting trigger | Passed | [src/RetailPOS.API/Services/StockAdjustmentService.cs](src/RetailPOS.API/Services/StockAdjustmentService.cs) | Posting occurs from the approval path rather than from stock-count approval directly. |
| Stock count generates a linked stock adjustment draft and preserves the linkage | Passed | [src/RetailPOS.API/Services/StockCountService.cs](src/RetailPOS.API/Services/StockCountService.cs) and [tests/RetailPOS.Tests/Inventory/StockCountPhase1HttpTests.cs](tests/RetailPOS.Tests/Inventory/StockCountPhase1HttpTests.cs) | The stock count service creates a stock adjustment draft with SourceStockCountId and the tests verify the linkage and the later completion transition. |
| Stock count itself does not post directly when a linked stock adjustment exists | Passed | [src/RetailPOS.API/Services/StockAdjustmentService.cs](src/RetailPOS.API/Services/StockAdjustmentService.cs) and [src/RetailPOS.API/Services/StockCountService.cs](src/RetailPOS.API/Services/StockCountService.cs) | The stock-count path creates an adjustment draft; the posting event is triggered when the linked adjustment is approved. |
| Weighted-average cost snapshot is captured and reused for approval-time posting | Passed | [src/RetailPOS.API/Services/StockAdjustmentService.cs](src/RetailPOS.API/Services/StockAdjustmentService.cs) and [tests/RetailPOS.Tests/Inventory/StockAdjustmentWorkflowHttpTests.cs](tests/RetailPOS.Tests/Inventory/StockAdjustmentWorkflowHttpTests.cs) | The service resolves and stores a cost snapshot per adjustment line before posting, and the workflow tests verify the resulting approval-time posting behavior. |
| Multi-line adjustments aggregate per-line values into one journal | Passed | [src/RetailPOS.API/Services/StockAdjustmentService.cs](src/RetailPOS.API/Services/StockAdjustmentService.cs) | The service aggregates line-level values before creating the journal, which is the required behavior for multi-line adjustments. |
| Rejected or cancelled adjustments do not leave orphan postings | Passed | [tests/RetailPOS.Tests/Inventory/StockAdjustmentWorkflowHttpTests.cs](tests/RetailPOS.Tests/Inventory/StockAdjustmentWorkflowHttpTests.cs) | Reject and cancel flows are asserted to leave inventory and journal state unchanged. |
| Duplicate approval does not create duplicate journals | Passed | [src/RetailPOS.API/Services/StockAdjustmentService.cs](src/RetailPOS.API/Services/StockAdjustmentService.cs) and [tests/RetailPOS.Tests/Accounting/JournalEntryServiceTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryServiceTests.cs) | The journal service uses a deterministic system-draft discriminator, and the accounting tests verify idempotent draft creation. |
| Posting is scoped to the outlet or warehouse context of the adjustment | Passed | [src/RetailPOS.API/Services/StockAdjustmentService.cs](src/RetailPOS.API/Services/StockAdjustmentService.cs) | Journal lines carry the correct outlet or warehouse scope based on the adjustment location type. |
| Permissions for create/submit/approve/reject/cancel flows are enforced | Passed | [tests/RetailPOS.Tests/Inventory/StockAdjustmentWorkflowHttpTests.cs](tests/RetailPOS.Tests/Inventory/StockAdjustmentWorkflowHttpTests.cs) and [tests/RetailPOS.Tests/Inventory/StockCountPhase1HttpTests.cs](tests/RetailPOS.Tests/Inventory/StockCountPhase1HttpTests.cs) | The regression suite verifies success paths and expected forbidden responses for unauthorized users. |
| Audit information captures reason, quantity difference, actor, and location context | Passed | [src/RetailPOS.API/Services/StockAdjustmentService.cs](src/RetailPOS.API/Services/StockAdjustmentService.cs) and [tests/RetailPOS.Tests/Inventory/StockAdjustmentWorkflowHttpTests.cs](tests/RetailPOS.Tests/Inventory/StockAdjustmentWorkflowHttpTests.cs) | Lifecycle audit events include reason, quantity changes, actor, and location metadata, and the workflow tests exercise the audited approval/reversal lifecycle end to end. |
| Reversal or correction handling for already approved adjustments creates a linked reversal journal and restores inventory/ledger state | Passed | [src/RetailPOS.API/Services/StockAdjustmentService.cs](src/RetailPOS.API/Services/StockAdjustmentService.cs), [src/RetailPOS.API/Controllers/StockAdjustmentsController.cs](src/RetailPOS.API/Controllers/StockAdjustmentsController.cs), and [tests/RetailPOS.Tests/Inventory/StockAdjustmentWorkflowHttpTests.cs](tests/RetailPOS.Tests/Inventory/StockAdjustmentWorkflowHttpTests.cs) | Approved adjustments can now be reversed through the dedicated endpoint, which restores inventory and stock-ledger state and creates a linked reversal journal without mutating the original posting. |

---

## 3. Automated evidence

### Automated evidence

P2-04 regression suite:
- Command: `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj --filter "FullyQualifiedName~StockAdjustmentWorkflowHttpTests|FullyQualifiedName~StockCountPhase1HttpTests" --logger "console;verbosity=minimal"`
- Observed result: 39 passed, 0 failed, 0 skipped

Accounting regression suite:
- Command: `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj --filter "FullyQualifiedName~Accounting" --logger "console;verbosity=minimal"`
- Observed result: 93 passed, 0 failed, 0 skipped

Full backend suite:
- Command: `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj --logger "console;verbosity=minimal"`
- Observed result: 317 passed, 0 failed, 0 skipped

---

## 4. Final decision

Decision: Accepted

Rationale:
- The core stock-adjustment and stock-count accounting behaviors required for P2-04 are implemented and covered by the full regression suite.
- The gain/shortage journal posting paths, stock-count-to-adjustment linkage, permission enforcement, no-orphan-posting behavior, and reversal/correction handling are all verified end to end.
- The newly added reversal flow introduces no regression in the P2-04, accounting, or full backend regression suites.

Follow-up items:
- None required for P2-04 sign-off.
