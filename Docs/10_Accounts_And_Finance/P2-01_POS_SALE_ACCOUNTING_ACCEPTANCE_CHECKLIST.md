# P2-01 Acceptance Checklist — POS Sale, Return, Refund and Exchange Accounting

Updated: 2026-08-09
Scope: P2-01 core POS sale accounting slice
Source of truth: [Docs/10_Accounts_And_Finance/PHASE_2_ACCOUNTING_DEVELOPMENT_PLAN.md](Docs/10_Accounts_And_Finance/PHASE_2_ACCOUNTING_DEVELOPMENT_PLAN.md)

Status legend:
- Passed
- Partially Passed
- Not Implemented

---

## 1. Approval and scope gate

- P2-01 is the first operational accounting slice in the Phase 2 rollout plan.
- P2-02 and later milestones remain out of scope for this acceptance review.

---

## 2. Requirement status matrix

| Requirement | Status | Evidence | Notes |
|---|---|---|---|
| Completed sale creates a balanced posting journal | Passed | [src/RetailPOS.API/Services/SaleService.cs](src/RetailPOS.API/Services/SaleService.cs) and [tests/RetailPOS.Tests/Sales/SaleServiceAccountingJournalTests.cs](tests/RetailPOS.Tests/Sales/SaleServiceAccountingJournalTests.cs) | The sale flow builds payment, discount, revenue, COGS, inventory, and rounding lines through the journal engine. |
| Refund and void flows create reversal-style journals | Passed | [src/RetailPOS.API/Services/SaleService.cs](src/RetailPOS.API/Services/SaleService.cs) and [tests/RetailPOS.Tests/Sales/SaleServiceVoidJournalTests.cs](tests/RetailPOS.Tests/Sales/SaleServiceVoidJournalTests.cs) | Refund and void handling route through the reversal posting path rather than mutating the original journal. |
| Historical unit-cost snapshot is captured and reused for reversal logic | Passed | [src/RetailPOS.Core/Entities/SaleItem.cs](src/RetailPOS.Core/Entities/SaleItem.cs) and [tests/RetailPOS.Tests/Sales/SaleServiceHistoricalCostSnapshotTests.cs](tests/RetailPOS.Tests/Sales/SaleServiceHistoricalCostSnapshotTests.cs) | Sale lines persist a historical unit-cost snapshot and the refund/void path reuses it for COGS reversal. |
| Posting remains inside the operational transaction boundary | Partially Passed | [src/RetailPOS.API/Services/SaleService.cs](src/RetailPOS.API/Services/SaleService.cs) | The sale service performs the accounting hook inside the same service transaction flow, but no dedicated rollback regression test was added in this slice. |
| Duplicate submissions are safely deduplicated | Partially Passed | [src/RetailPOS.API/Services/JournalEntryService.cs](src/RetailPOS.API/Services/JournalEntryService.cs) and [src/RetailPOS.API/Services/SaleService.cs](src/RetailPOS.API/Services/SaleService.cs) | The journal engine supports idempotent system-draft creation through a deterministic discriminator; a dedicated duplicate-sale regression test is still missing. |
| VAT posting to a VAT Payable account is implemented | Passed | [src/RetailPOS.API/Services/SaleService.cs](src/RetailPOS.API/Services/SaleService.cs), [src/RetailPOS.API/Services/AccountingConfigurationService.cs](src/RetailPOS.API/Services/AccountingConfigurationService.cs), and [tests/RetailPOS.Tests/Sales/SaleServiceVatAndExchangeAccountingTests.cs](tests/RetailPOS.Tests/Sales/SaleServiceVatAndExchangeAccountingTests.cs) | Sale and refund posting now add VAT Payable lines using the configured VAT Payable account when tax is applicable. |
| Exchange linkage and exchange-specific accounting treatment are implemented | Passed | [src/RetailPOS.API/Services/SaleService.cs](src/RetailPOS.API/Services/SaleService.cs), [src/RetailPOS.Core/Entities/Sale.cs](src/RetailPOS.Core/Entities/Sale.cs), and [tests/RetailPOS.Tests/Sales/SaleServiceVatAndExchangeAccountingTests.cs](tests/RetailPOS.Tests/Sales/SaleServiceVatAndExchangeAccountingTests.cs) | Replacement sales persist an original-sale linkage and the original sale gets a replacement-sale linkage for exchange traceability. |

---

## 3. Automated evidence

### Focused accounting/sale regression subset

Command:
- `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj --filter "FullyQualifiedName~SaleServiceVatAndExchangeAccountingTests"`

Observed result:
- 3 tests passed
- 0 failed
- 0 skipped

### Full backend regression suite

Command:
- `dotnet test RetailPOS.slnx`

Observed result:
- 308 tests passed
- 0 failed
- 0 skipped

---

## 4. Final decision

Decision: Accepted

Rationale:
- The core POS sale/refund/void accounting path is implemented, regression-tested, and verified by the full backend suite.
- The VAT Payable posting and exchange-linkage behaviors required by the plan are now implemented and covered by dedicated regression tests.

Follow-up items:
1. Keep P2-02 out of scope until this slice is formally approved and working in the target environment.
2. Continue to monitor the accounting configuration defaults for VAT Payable mappings in downstream onboarding and seed flows.
