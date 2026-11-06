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
