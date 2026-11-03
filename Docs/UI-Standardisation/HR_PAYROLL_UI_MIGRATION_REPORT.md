# HR & Payroll UI Migration Report

## Status: all implemented HR & Payroll screens migrated; automated gates passed; authenticated browser verification NOT performed

All 21 HR/Payroll page components that exist in the app (`pages/hr/**`, `pages/payroll/**`) now use the approved shared UI kit. Nothing was added: no new screens, features, endpoints or business rules. Services, models, routes, guards, permissions, calculations, approval transitions and API contracts were not touched. Visual verification in an authenticated browser is **pending** (see below).

## Results

| Gate | Result |
|---|---|
| Targeted specs (per group, run by each migration worker) | all passed: 104 + 63 + 31 + 55 + 77 + 63 + 70 tests |
| Full Angular unit suite | **PASS — 70 files / 976 tests** (previous baseline 63 / 608: +7 files, +368 tests). Two of three full runs were fully green; one run had 3 failures in `payroll-run-validation-page.component.spec.ts` (two 5 s timeouts and one dependent call-count assertion) that did not reproduce when the file ran alone (27/27) or in the next two full runs. Treated as load-related flakiness, **not** investigated further — see risks. |
| Production build | **PASS**, no warnings |
| Bundle | Initial total **2.66 MB raw / 416.54 kB transfer** (previous 2.59 MB / 407.27 kB → **+0.07 MB, +9.27 kB transfer**). The HR/Payroll routes are imported eagerly, so the kit pieces they now use ship in the initial bundle. `main` 396.45 → 405.76 kB; global CSS 79.58 → 79.28 kB. |
| `git diff --check` | **PASS** (tracked files). The HR/Payroll pages are entirely untracked, so they are not covered by git; a scan of them found no trailing whitespace. |
| Scope check | Only files under `pages/hr/**` and `pages/payroll/**` were modified in this task (verified by modification time across `retailpos-frontend/src`, `src/`, `retailpos-mobile/`, `Docs/`). The shared kit, `styles.css`, Product List/Form, POS and backend were not touched. No legacy colour utilities, `*ngIf`/`*ngFor` or ad-hoc table markup remain in these components. |

## Screens migrated (21 of 21 existing components)

| Area | Component | Route(s) | Table mode |
|---|---|---|---|
| HR Dashboard | `hr-dashboard-page` | `hr/dashboard` | key figures as lists, no table |
| Organisation | `hr-organisation-page` | `hr/org-units` | client (one unbounded list loaded by the API; no server paging) |
| Operational locations | `hr-operational-locations-page` | `hr/operational-locations` | client |
| Positions | `hr-positions-page` | `hr/positions` | client |
| Employee management, profile, lifecycle, assignments | `hr-employees-page` | `hr/employees` | **server** (existing API pages/searches; fixed page size 25, no sort parameter exists) + client history tables |
| Employee documents | `employee-documents` | embedded in the profile | cards/list |
| Opening employee import | `opening-employee-import` | `hr/opening-imports`, `payroll/opening-imports` | client (rows loaded by the batch) |
| Shifts, holidays, weekly off | `hr-calendars-page` | `hr/shifts`, `hr/holidays`, `hr/weekly-off` | client |
| Rosters | `hr-rosters-page` | `hr/rosters` | client |
| Attendance devices | `hr-devices-page` | `hr/attendance/devices` | client |
| Attendance reports | `hr-attendance-reports-page` | `hr/attendance/reports` | client, search/paging off (server pages 50 rows; existing Previous/Next kept) |
| HR core reports | `hr-core-reports-page` | `hr/reports` | client, same pattern |
| Leave | `hr-leave-page` | `hr/leave` | client (loaded rows) |
| Overtime | `hr-overtime-page` | `hr/overtime` | client (loaded rows) |
| Employee exit and final settlement | `hr-exit-page` | `hr/exits` | client (existing server-side filter form kept in the toolbar) |
| Payroll dashboard | `payroll-dashboard-page` | `payroll/dashboard` | client (small payment-state table) |
| Payroll reports | `payroll-advanced-reports-page` | `payroll/reports` | client, search/paging off (server pages 50 rows) |
| Salary, adjustments, structures | `payroll-salary-setup-page` | `payroll/salary-setup` | client (6 tables) |
| Loans and advances | `payroll-loans-page` | `payroll/loans` | client (schedule/repayment tables) |
| Payroll run validation | `payroll-run-validation-page` | `payroll/validation` | client (findings keep server-side Previous/Next) |
| Payroll period control | `payroll-period-control-page` | `payroll/periods` | client (close-gates table) |
| Payroll approval, payments, payslips | `payroll-payments-page` | `payroll/payments` | client (7 bounded tables) |

**Remaining / not migrated: none of the existing components.** Items in the requested scope list with no implemented screen (for example a dedicated HR/Payroll settings page, a separate approval-workflow screen, a separate payslip screen) were **not created**; approval and payslip actions live inside the screens above and were migrated there.

### Why most tables are client mode
The existing HR/Payroll APIs either return one bounded list per request or page server-side with fixed parameters and no sort/search parameters. Server mode was used only for the employee list, where the API already supports server search and paging. Report screens page on the server at a fixed 50 rows with no search or sort parameter, so their table search/paging is switched off and the existing Previous/Next controls remain. No new requests, endpoints or larger loads were introduced.

## Shared components used
`app-ui-page-header`, `app-ui-card`, `app-ui-data-table` (with custom cells and row actions), `appUiButton`, `appUiControl` + `app-ui-form-field`, `app-ui-badge` (status text always visible), `app-ui-alert` (error/warning/info with live-region roles), `app-ui-state`. Dialog components were not needed: none of these screens had a modal; native confirmations are listed under risks.

## Preserved
- Every permission and capability condition, permission key, confidential-field visibility (salary amounts, bank/payment destination details, documents) and approval/action button rule — checked by new permission-matrix tests.
- Request payloads, validators, ngModel bindings, idempotency keys, file handling, exports/downloads, amounts displayed exactly as the API returns them (no new number formatting), approval transitions and the no-confirmation flows (for example Reject).
- Routes, guards and the capability guard.

## Existing tests
- The original specs pass with their assertions untouched.
- Spec edits were limited to adding `provideRouter([])` (and its import) to the TestBed providers of `hr-rosters-page`, `hr-devices-page`, `payroll-payments-page` and `payroll-run-validation-page` specs, because the page header's breadcrumb link needs a router. No assertion was changed or removed.

## New tests (≈ 368)
Attendance reports 9, HR dashboard 8, Payroll dashboard 9, Core HR reports 10, Payroll reports 12, Organisation 9, Operational locations 6, Positions 16, Calendars 27, Rosters 13, Devices 19, Employees 32, Employee documents 14, Opening import 16, Leave 22, Overtime 18, Loans 17, Exit 35, Period control 21, Salary setup 23, Payments 19, Run validation 16, plus the other additions inside the new files. They assert rendering, label/control ties, field bindings and payloads, table sorting/search/paging where enabled, loading/empty/error states (`role="alert"` / status), permission visibility, confidential-field hiding, named row actions, `aria-sort`, and navigation links.

## Visible differences to review
- Tables page client-side (default 10/25 rows) where the old pages rendered every loaded row (leave, overtime, exit cases, position/location/organisation lists, import rows).
- Combined columns were split (for example "Status / version" → Status and Version); statuses appear as badges with the same text.
- Row action buttons keep their visible text and gain context-bearing accessible names.
- Several plain `<p role="alert">` messages are now kit alerts; some lifecycle forms are cards with small added titles.
- Load errors on the organisation/location/position pages now show a table-level error state (no retry button, as none existed).
- The employee list pager and loading presentation are the table's: existing rows stay visible while reloading.
- Where an actions column has no applicable action for the current user, view-only users may still see an empty "Actions" header on some tables (kit gap: no way to hide the column conditionally); the salary-setup page hides it without manage permission.

## Browser verification
**Not performed** — the development API and dev server are unavailable and no safe account exists; authentication was not bypassed. No screenshots or responsive measurements (390/768/1440 px) were taken for these screens. Layouts rely on the approved kit patterns (auto-fit grids, tables scrolling in their own region) and are unverified on devices or with real data. Visual testing is pending.

## Risks
- **Visual regression is unverified** for all 21 screens; the first authenticated pass should concentrate on the large screens (employees, exit, payments, salary setup, validation).
- **Intermittent test timing:** one full-suite run showed 3 failures in `payroll-run-validation-page.component.spec.ts` under load (not reproducible alone or in two later full runs). If it recurs, check those tests' async waits.
- **Native prompts kept:** `window.confirm` (cancel exit case; new settlement revision) in `hr-exit-page` and `window.prompt` (archive reason) in `employee-documents` remain, because replacing them with dialogs would change method behaviour.
- **Employee list:** the table's page-clamping can trigger one extra reload if the total shrinks below the current page; the list still has no `switchMap`/sequence handling (unchanged from before).
- **Untracked source:** these pages are not in git. A pre-migration backup of both directories was kept outside the repo in the session folder (`files/hr-payroll-backup/`). Commit the migrated pages (and the earlier untracked work) when ready.
- **Eager route imports** add ≈ 9 kB to the initial bundle; lazy-loading these routes would recover it (out of scope).

## Not done by design
No missing HR/Payroll features implemented, no other module migrated, nothing committed.
