# Outlet Sales Target and Weekly Off Days: Gap Analysis

**Audit date:** 2026-11-06. **Audience:** engineers and the business owner reviewing this feature.

Status values: Implemented, Partial, Missing, Conflicting, Verification Required. "Before" is the state found by the read-only audit.

## 1. Summary

- Sales targets existed only in specifications. No entity, table, migration, endpoint or UI existed. The dashboard and the Cash Sessions landing page deliberately showed N/A or "Not configured".
- No outlet-level weekly off day existed. HR has its own weekly-off policy, which is not equivalent (see §3).
- One blocking conflict was found (bi-weekly). The owner resolved it on 2026-11-06: **keep D05 half-months**.

## 2. Findings

| Requirement | Before | Evidence | Reuse strategy |
|---|---|---|---|
| Target entity and schema | Missing | `IRANIMART_POS_MASTER_LLD_V1_2.md` §3.1 names `OutletSalesTargetVersion`; nothing in `src/` | Build the LLD aggregate with normalized day rows |
| Weekday targets (FR-TARGET-01) | Missing | `POS_MASTER_BLUEPRINT_GAP_ANALYSIS.md` §B.2 | New, in the Outlet feature |
| Calendar calculations (FR-TARGET-02) | Missing | as above | Pure calculator in Application |
| Overrides and history (FR-TARGET-03) | Missing | as above | New tables; `IAuditService` for audit |
| Weekly off days | Missing | Search found only `HrWeeklyOff*` | New, stored on the target version |
| Dashboard target metrics | Partial | `SalesFinanceQueries.cs` passes `null` targets; `02_OWNER_DASHBOARDS_LLD.md` §6 defines the formulas | Fill the existing fields; add coverage metadata additively |
| Cash Sessions target cards (FR-TARGET-04) | Partial | `cash-session-list.component.ts` has placeholder cards | Replace placeholders; same calculator |
| Outlet form | Implemented, sections only | `outlet-form.component.html/.ts` | Keep the reactive form; wrap sections in tabs |
| Terminals, invoice prefix lock, print settings | Implemented | `OutletPosSetupApplier.cs` | Unchanged |
| Time zone | Partial | `timeZoneId` is in the form and payload but has no input | Preserved as is; no input added |
| Tabs | Implemented elsewhere | `pages/settings/settings-page.component.html` uses underlined tabs with legacy Tailwind greys | Same layout with `--rp-*` tokens, as the design system requires |
| Multi-select | Missing | UI kit has none | Small feature-owned control |
| Unsaved-changes guard | Implemented | `guards/unsaved-changes.guard.ts` | Reuse on outlet routes |
| Business week convention | Missing | No week logic in `src/`; dashboard buckets are rolling 7-day ranges | Saturday to Friday, as the brief and FR-TARGET-01 order imply |

## 3. Conflicts and decisions

| ID | Status | Topic | Sources | Decision |
|---|---|---|---|---|
| C-01 | Conflicting, resolved by owner | Bi-weekly period | Brief §3.5 (14 days) vs D05, FR-TARGET-02, SRS §16.5 ("No 14-day rolling biweekly rule") | Keep D05: First Half 1–15 and Second Half 16–end. The 14-day calculation is not built. |
| C-02 | Conflicting, resolved by brief | Unset open weekday | FR-TARGET-01 (unset = Not configured) vs brief §3.3 (valid target required when enabled) | Every open weekday needs an amount when targets are enabled. Zero is allowed. "Not configured" remains for disabled or missing versions. |
| C-03 | Resolved by brief | Permission | Gap analysis §H-18 proposed `outlets.targets.view/edit`, never approved; brief §6.5 and Phase 4 keep existing rules | No new keys. Read uses `outlets.view`, write uses `outlets.create` / `outlets.edit`. |
| C-04 | Resolved by brief | Write API | LLD names `PUT /outlets/{id}/sales-targets`; brief asks for an atomic create and no parallel save | Writes go through the outlet create/update payload, as terminals do. `GET /outlets/{id}/sales-targets` reads. |
| C-05 | Not equivalent | HR weekly off | `HrWeeklyOffPolicyVersion` drives attendance and payroll | Not reused. Changing a store's trading days must not alter payroll, and the reverse. The two settings can differ. |
| C-06 | Verification Required | Time zone source | Outlets may override the zone (D9); LLD §18.4 and the dashboard use the Company zone | Targets use the Company zone, matching LLD §18.4. |

## 4. Implementation plan

1. Additive migration: three new tables, no change to existing rows. Outlets with no version are "Not configured" with no off days.
2. Application: pure calculator and rules, read port, query.
3. Infrastructure: applier joined to the outlet save, so outlet and target are one atomic `SaveChanges`.
4. Angular: three tabs, off-day multi-select, Sales Target tab.
5. Dashboard, then Cash Sessions, using the same calculator and the dashboard's net sales definition.
6. Tests, builds, documentation.

## 5. Calculation rules

See the "Outlet sales targets" section of `IRANIMART_POS_MASTER_LLD_V1_2.md` (§31) for the final rules.

## 6. Status after implementation

| Item | Status |
|---|---|
| Three-tab Outlet Create/Edit, Weekly Off Days, Sales Target tab | Implemented |
| Weekly, First Half, Second Half, Monthly, with overrides and Reset to Auto | Implemented |
| Effective-dated history of off days, targets and enabled state | Implemented |
| Sales & Finance Dashboard targets, off-day and partial coverage | Implemented |
| Cash Sessions target cards (FR-TARGET-04) | Implemented, without a freshness projection |
| 14-day bi-weekly period | Not built, by owner decision C-01 |
| Migration applied to Development or Production | **Not applied.** Needs explicit approval |
| Check in a browser at 390, 768 and 1440 px | Verification Required |
