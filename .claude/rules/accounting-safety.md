---
paths:
  - "src/**/Accounting*/**"
  - "src/**/Accounts/**"
  - "src/**/ChartAccounts/**"
  - "src/**/Bank*/**"
  - "src/**/FinancialPeriods/**"
  - "src/**/FinancialYearCloseRuns/**"
  - "src/**/OpeningBalances/**"
  - "src/**/Expenses/**"
  - "src/**/OtherIncome/**"
  - "src/**/Supplier*/**"
  - "src/**/Bills/**"
  - "src/**/Transactions/**"
  - "src/**/VatReports/**"
  - "src/**/ApReports/**"
  - "retailpos-frontend/src/app/pages/accounting/**"
---

# Accounting safety

- Specs and acceptance evidence: `Docs/10_Accounts_And_Finance/` (`BASIC_ACCOUNTING_MODULE.md`, phase plans, `P2-*`/`P3-*`/`RM-*` acceptance checklists, `JOURNAL_ENGINE_ACCEPTANCE_CHECKLIST.md`). Open only the checklist for the area you touch.
- Preserve accepted calculations, posting rules, journal numbering/sequence locking, period and year-end close gates, reversal links, reconciliation state and audit evidence exactly. Do not change them without an approved decision.
- Operational status + journal + audit writes are atomic in the handler-owned transaction; the Journal Engine joins the outer transaction on the same DbContext. No blanket transaction behaviors, no automatic retry of non-idempotent posting.
- Closed periods/years reject postings; reopen flows require permission and audit.
- Characterize success **and** rollback with PostgreSQL integration tests before changing a posting workflow.
