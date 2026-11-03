# P3-06 Bank Settlement and Reconciliation Acceptance Checklist

Status: Accepted
Date: 2026-09-06

## Backend Evidence

- [x] Business-owned BankAccount foundation with active/account/outlet validation.
- [x] Card and Mobile/MFS clearing cutover with SalePayment historical account snapshots.
- [x] SettlementBatch with clearing-to-bank Journal Engine posting, fee support, idempotency, reversal, and relational rollback evidence.
- [x] LiquidityTransfer for Cash-to-Bank, Bank-to-Cash, and Bank-to-Bank with linked reversal and atomicity evidence.
- [x] CSV-first BankStatementImport with SHA-256 content checksum and Business/BankAccount uniqueness.
- [x] Immutable BankStatementLine evidence with raw source row preservation and no fabricated balances/references.
- [x] BankReconciliationSession with active overlap prevention, reconciliation start-date enforcement, close/reopen history, and readiness validation.
- [x] Manual BankReconciliationMatch groups supporting one-to-one, one-to-many, and many-to-one exact matching with 0.00 tolerance.
- [x] Audited unmatch preserving historical match/link records.
- [x] Bank Fee and Bank Interest proposal/approval/posting through Journal Engine.
- [x] Statement-linked chargeback and failed-settlement exception classification without automatic accounting.
- [x] Reconciliation close/reopen server-side workflow with closed-session immutability.
- [x] Relational atomicity, rollback, retry, period-governance, idempotency, and tenant-isolation evidence.

## Angular Workflow

- [x] Accounting Bank Reconciliation route and finance navigation entry.
- [x] BankAccount selector and backend session list/status display.
- [x] Backend-calculated opening/closing, movement, difference, unmatched counts/amounts, and unresolved exception count display.
- [x] CSV import interaction with backend status/error handling.
- [x] Read-only statement evidence panel.
- [x] Backend-provided GL candidate panel.
- [x] Exact-total manual match selection for one-to-one, one-to-many, and many-to-one requests.
- [x] Permission-gated match/close/reopen controls and closed-session read-only behavior.
- [x] Backend refresh after matching/import actions.
- [x] No Angular-side financial authority, account calculation, journal creation, or statement mutation.
- [x] Adjustment proposal/approval/reversal panels and exception classify/resolve panels.
- [x] Active-match list and unmatch interaction.
- [x] Settlement-history investigation panel.

## Source of Truth

- JournalEntry and JournalEntryLine remain the financial source of truth.
- BankStatementLine remains immutable external evidence.
- Reconciliation, adjustment, settlement, transfer, and exception records store operational state, linkage, audit, and classification only.
- Adjustment JournalEntryLines are not added a second time to reconciliation totals.

## Validation Evidence

- Focused Angular workbench tests: 4/4 passed.
- Full Angular suite: 42/42 passed.
- Angular production build: passed.
- Full backend suite: 454/454 passed.
- Backend Release build: passed.
- P3-06 focused backend evidence: green across foundation, settlement/transfer, statement import, reconciliation/matching, close, adjustment, exception, atomicity, authorization, and period governance suites.

## Known Limitations

- CSV-first import only; no Excel, PDF, Open Banking, direct bank API, or provider-specific integrations.
- Matching is manual-first; no silent automatic matching.
- No payment-provider API integration.
- No automatic chargeback accounting.
- No automatic failed-settlement accounting.
- Bank Fee and Bank Interest require explicit proposal and approval; statement narrative never auto-posts.
- Adjustment, exception, unmatch, and settlement-history actions remain explicitly permission-gated and refresh from authoritative backend state after each action.
- No fabricated historical statement matches or balances.
