# P2-05 Acceptance Checklist — Cash Session Closing, Shortage and Surplus Accounting

Updated: 2026-08-09
Scope: P2-05 cash-session close accounting slice
Source of truth: [Docs/10_Accounts_And_Finance/PHASE_2_ACCOUNTING_DEVELOPMENT_PLAN.md](Docs/10_Accounts_And_Finance/PHASE_2_ACCOUNTING_DEVELOPMENT_PLAN.md)

Status legend:
- Passed
- Partially Passed
- Failed
- Not Applicable

---

## 1. Approval and scope gate

- P2-05 covers cash-session close accounting for non-zero variance, including surplus and shortage postings.
- Opening float, normal close, and zero-variance close remain control events rather than new revenue or expense postings.
- P2-06 remains out of scope for this acceptance decision.

---

## 2. Requirement status matrix

| Requirement | Status | Evidence | Notes |
|---|---|---|---|
| Positive variance creates a balanced gain journal | Passed | [src/RetailPOS.API/Services/CashSessionService.cs](src/RetailPOS.API/Services/CashSessionService.cs) and [tests/RetailPOS.Tests/Sales/CashSessionServiceTests.cs](tests/RetailPOS.Tests/Sales/CashSessionServiceTests.cs) | The close workflow posts a two-line CashDifferenceSurplusPosting journal when the variance is positive and a closing note is present. |
| Negative variance creates a balanced loss journal | Passed | [src/RetailPOS.API/Services/CashSessionService.cs](src/RetailPOS.API/Services/CashSessionService.cs) and [tests/RetailPOS.Tests/Sales/CashSessionServiceTests.cs](tests/RetailPOS.Tests/Sales/CashSessionServiceTests.cs) | The close workflow posts a two-line CashDifferenceShortagePosting journal when the variance is negative. |
| Zero or near-zero variance does not create a posting | Passed | [src/RetailPOS.API/Services/CashSessionService.cs](src/RetailPOS.API/Services/CashSessionService.cs) and [tests/RetailPOS.Tests/Sales/CashSessionServiceTests.cs](tests/RetailPOS.Tests/Sales/CashSessionServiceTests.cs) | The implementation gates posting behind a non-zero threshold and the regression test verifies no journal draft is created for a near-zero close. |
| The variance posting is created inside the same transaction boundary as the session close update | Passed | [src/RetailPOS.API/Services/CashSessionService.cs](src/RetailPOS.API/Services/CashSessionService.cs) | The service opens a database transaction around the posting and session-state update and rolls back on posting failure. |
| Reclosing the same session does not double-post | Passed | [src/RetailPOS.API/Services/CashSessionService.cs](src/RetailPOS.API/Services/CashSessionService.cs) and [tests/RetailPOS.Tests/Sales/CashSessionServiceTests.cs](tests/RetailPOS.Tests/Sales/CashSessionServiceTests.cs) | Already closed sessions short-circuit the close path and do not create an additional journal. |
| Posting failure rolls back the session close state | Passed | [src/RetailPOS.API/Services/CashSessionService.cs](src/RetailPOS.API/Services/CashSessionService.cs) and [tests/RetailPOS.Tests/Sales/CashSessionServiceTests.cs](tests/RetailPOS.Tests/Sales/CashSessionServiceTests.cs) | The regression test verifies that a failed variance posting leaves the session open with no closed timestamp. |
| Posting is scoped to the outlet and terminal context of the cash session | Passed | [src/RetailPOS.API/Services/CashSessionService.cs](src/RetailPOS.API/Services/CashSessionService.cs) | The journal payload carries the session outlet and terminal identifiers, and the posting uses the outlet-scoped accounting configuration. |
| P2-05 does not duplicate P2-01 sale or refund accounting | Passed | [src/RetailPOS.API/Services/CashSessionService.cs](src/RetailPOS.API/Services/CashSessionService.cs) and [Docs/10_Accounts_And_Finance/PHASE_2_ACCOUNTING_DEVELOPMENT_PLAN.md](Docs/10_Accounts_And_Finance/PHASE_2_ACCOUNTING_DEVELOPMENT_PLAN.md) | The new close-path logic creates only a variance journal and relies on the existing POS sale/refund postings from P2-01. |

---

## 3. Automated evidence

### Cash-session regression suite
- Command: `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj --filter "FullyQualifiedName~CashSessionServiceTests" --logger "console;verbosity=minimal"`
- Observed result: 11 passed, 0 failed, 0 skipped

### Accounting / sales regression suite
- Command: `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj --filter "FullyQualifiedName~Accounting|FullyQualifiedName~SaleService|FullyQualifiedName~CashSessionServiceTests|FullyQualifiedName~PosCashSessionHttpTests" --logger "console;verbosity=minimal"`
- Observed result: 119 passed, 0 failed, 0 skipped

### Full backend suite
- Command: `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj --logger "console;verbosity=minimal"`
- Observed result: 322 passed, 0 failed, 0 skipped

---

## 4. Final decision

Decision: Accepted

Rationale:
- The implementation now posts balanced variance journals for positive and negative cash differences while preserving the approved scope boundary of P2-05.
- Zero-variance and already-closed sessions are handled without creating duplicate postings.
- The cash-session regression suite, accounting/sales regression suite, and the full backend suite all passed without failures.

Follow-up items:
- None required for P2-05 sign-off.
- P2-06 remains deferred and was not started as part of this acceptance decision.
