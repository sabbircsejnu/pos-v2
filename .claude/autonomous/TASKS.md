# Owner Dashboards — implementation tasks

Spec: `Docs/Owner_Dashboards/`. Mapping and decisions: `Docs/Owner_Dashboards/IMPLEMENTATION_GAP_ANALYSIS.md`.

| ID | Task | Depends on | Verify with | Status |
|---|---|---|---|---|
| OD-00 | Read-only audit and gap analysis | none | Document exists | done |
| OD-01 | Application contracts, policy, pure calculators, tests | OD-00 | UnitTests `~OwnerDashboard` | done |
| OD-02 | Scope provider and shared filter validation | OD-01 | UnitTests, integration auth tests | done |
| OD-03 | Sales & Finance store, handlers, controller | OD-02 | PostgreSQL integration tests | done |
| OD-04 | Inventory snapshot store, handlers, controller | OD-02 | PostgreSQL integration tests | done |
| OD-05 | Transfer recommendation engine and details endpoint | OD-01, OD-04 | UnitTests, integration tests | done |
| OD-06 | Angular models, API service, filter store, shared components | OD-03 | Vitest | done |
| OD-07 | Sales & Finance page, route and menu | OD-06 | Vitest | done |
| OD-08 | Inventory Intelligence page and details dialog | OD-05, OD-06 | Vitest | done |
| OD-09 | Transfer-create prefill | OD-08 | Vitest | done |
| OD-10 | Security and code review, full builds and suites | all | verify-build | done |

# UOM v1 (D01–D12) — implementation tasks

Spec: `Docs/Unit_Of_Measurement/`. Audit and decisions: `Docs/Unit_Of_Measurement/IMPLEMENTATION_IMPACT_ANALYSIS.md`.

| ID | Task | Verify with | Status |
|---|---|---|---|
| UOM-00 | Audit and impact analysis | Document exists | done |
| UOM-01 | Widen quantities to decimal, migration | Full backend suites | done |
| UOM-02 | Unit master, versioned config, conversion engine, API | `UomPostgresHttpTests` | done |
| UOM-03 | PO, GRN, damaged and sealed/loose receiving | `UomPostgresHttpTests` | done |
| UOM-04 | POS sale, held sale, void, refund, packaging | `UomPostgresHttpTests` | done |
| UOM-05 | Pack-state, open, transfer, adjustment, count | `UomPostgresHttpTests` | done |
| UOM-06 | Repacking with Pattern P1 | `UomPostgresHttpTests` | done |
| UOM-07 | Valuation, dashboards, reports | Integration tests | done |
| UOM-08 | Angular UI | Vitest, build | done |
| UOM-09 | Full regression, report | verify-build | done |
| UOM-10 | I02 multiple partial refunds, I04 unified refund UI | Refund PostgreSQL tests, Vitest | done |
| UOM-11 | I03 promotion eligibility per selling option | Promotion PostgreSQL tests, Vitest | done |
| UOM-12 | I01 permission matrix, I05 P1 proofs | Permission and accounting tests | done |
| UOM-13 | Gaps: whole-pack transfer receipt, open prepared packs, rejected-only GRN, slow movers, labels, Flutter guard | Gap tests, unit, flutter | done |
| UOM-14 | Migration prepared, backed up, rehearsed | Rehearsal totals identical | done |
| UOM-15 | Final verification report | Report exists | done |
| UOM-16 | Apply migration to Development | History shows both migrations; totals match | blocked (U-06) |
| UOM-17 | Browser walk-through and responsive checks | Real browser | blocked (no browser tool) |
| UOM-18 | I07 gap closure: cost redaction, refund VAT, exact refund cost, posting guard, transfer opportunities | Final gap tests | done |
| UOM-19 | Fresh backup and full three-migration rehearsal | Totals identical | done |

# Product Exchange (EX-01–EX-31, D-01–D-19, C-01–C-10, OC-01–OC-07, DN-01)

Spec: `Docs/08_Sales_And_POS/Exchange/` (SRS v2.1, LLD v2.2, Gap v2.2, Plan v2.2, Stage 0 audit). Test DB: isolated A2 Postgres 55432 via `RETAILPOS_TEST_POSTGRES` (disposable template clones only).

| ID | Task | Verify with | Status |
|---|---|---|---|
| EXC-00 | Stage 0 audit | Stage 0 report | done |
| EXC-01 | DN-01 decision recorded; policy contract | Docs, unit | done |
| EXC-02 | Permissions (5), role-form group, template definitions (no seeding of existing roles) | Unit, Vitest | done |
| EXC-03 | D-14 void guard (`SALE_HAS_RETURNS`) | Void PG tests | done |
| EXC-04 | D-01 legacy link rejection | Sale contract tests | done |
| EXC-05 | Domain entities, EF config, migration `PosV2ExchangeFoundation` (incl. cash CHECKs) | MigrationChainValidationTests | done |
| EXC-06 | Pure calculators: return credit, high value, eligibility dates, chain, phone classification, settlement plan, journal composition | Unit | done |
| EXC-07 | Exchange policy API (versions, disabled until saved) | Unit, PG HTTP | done |
| EXC-08 | Source-invoice lookup + preview | Unit, PG HTTP | done |
| EXC-09 | Supervisor approval endpoint | Unit, PG HTTP | done |
| EXC-10 | Confirm (atomic), cash, stock, journal, outbox, receipt snapshot | PG T-01..T-05 | done |
| EXC-11 | Reversal | PG | done |
| EXC-12 | History, detail, receipt document/PDF | PG HTTP | done |
| EXC-13 | Reporting: owner dashboard, mobile report, metrics | PG | done |
| EXC-14 | Angular: service, workflow, approval dialog, receipt, history, settings, entry points | Vitest, build | done |
| EXC-15 | Security/code review, full regression, docs | verify-build | done |
| EXC-16 | Hardening L4 (dashboard COGS vs retained packaging) and L6 (terminal at Confirm/Reverse) | PG repro + tests | done |
| EXC-17 | Hardening L1b (per-login/per-cashier lockout) and L3 (by-sale non-leaking 404) | PG + unit | done |
| EXC-18 | I-12 resolution, VI-20/VI-03 analysis, UAT checklist, docs | Plan §12.3–12.5 | done |
| EXC-19 | Final regression: unit, frontend, builds, full PG integration (no LiveInfrastructure) | verify-build | done |
| EXC-20 | Browser UAT environment: persistent synthetic DB, provisioning tool, UAT API/web, Playwright + Edge | Provision twice (idempotent) | done |
| EXC-21 | Browser E2E specs for the 40-case checklist | Playwright | done |
| EXC-22 | Fix browser defects DEF-01..04 with regression tests | Vitest, xUnit, Playwright | done |
| EXC-23 | Full browser run + DB reconciliation + docs | Playwright report, reconcile | done |
| EXC-24 | UX: invoice lookup layout, replacement product search (product code/SKU/name/barcode), wizard polish | Vitest, Playwright 05 | done |
