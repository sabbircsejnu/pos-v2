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
