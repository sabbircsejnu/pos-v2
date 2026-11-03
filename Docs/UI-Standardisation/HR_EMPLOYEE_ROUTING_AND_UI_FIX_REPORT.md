# HR Employee Routing and UI Fix Report

## Routes added

The HR employee routes now provide separate list, create, view and edit pages:

| Route | Purpose | Required permission |
|---|---|---|
| `/hr/employees` | Employee list | `hr.employee.view` |
| `/hr/employee/new` | Create employee | `hr.employee.create` |
| `/hr/employee/:employeeId/view` | Employee profile | `hr.employee.view` |
| `/hr/employee/:employeeId/edit` | Edit employee | `hr.employee.view` and `hr.employee.edit` |

Each route retains the existing HR capability and authenticated Business-context guards. No backend endpoint or API payload contract was changed.

## Components refactored

- `HrEmployeesPageComponent` now selects a dedicated page mode from the active route. The employee table is rendered only on `/hr/employees`; profile and form content are not embedded in the list.
- View and Edit load the employee by the route's positive, safe-integer ID. Invalid IDs show a not-found state without requesting employee data. Loading, not-found, retryable error and authorization-error states are explicit.
- The table's View and Edit actions navigate to their employee URLs. New Employee navigates to the Create URL. The list's search and server page are carried in query parameters and restored when returning.
- The profile includes Back to Employees and permission-gated Edit Employee actions. Cancel from Edit returns to that employee's View page; Cancel from Create returns to the list.
- Updating preserves confidential values not editable under the caller's sensitive-data permissions, instead of replacing them with values from hidden form fields.

## Existing functionality preserved

The profile page retains the existing authorized employee documents, personal and identity information, Login User linking, lifecycle actions and history, position/manager history and assignment, operational location assignments, and location-transfer actions. Existing permission checks remain feature-owned. Current position and department are displayed from the names supplied by the position-history API rather than rendering the profile's internal IDs.

Create and Update continue to use the existing service methods, payload shapes, and validation rules. Advancing the stepper does not save. Saving is guarded against duplicate submissions.

## Test results

- Focused employee feature tests: **7 files passed, 85 tests passed**.
- Full Angular suite: **80 files passed, 1,064 tests passed**.
- Production build: **passed**.
- `git diff --check`: passed; Git emitted only line-ending conversion warnings for existing working-copy files.

Coverage includes route configuration and permission metadata, direct View/Edit URL loading, route-ID parsing, malformed IDs, 403 handling without exposing profile data or navigating to logout, list search/page restoration, navigation and cancel flows, confidential field visibility, step validation and persistence, and duplicate-submit protection.

## Browser verification

Authenticated browser verification was **not performed**. No browser pages were shared, and opening `http://localhost:4200/hr/employees` returned `ERR_CONNECTION_REFUSED`. No authorized development login was available in the shared browser context. The automated route tests exercise direct URL initialization, but do not substitute for visual, authenticated browser testing.

## Remaining risks

- Real Business-scoped API responses (including cross-Business 403/404 behavior) still need verification in the authorized Development environment.
- Desktop and mobile rendering at 390, 768 and 1440 px, plus a real browser refresh and browser Back/Forward sequence, remain to be checked in an authenticated browser.
- No other HR or Payroll modules were migrated.
