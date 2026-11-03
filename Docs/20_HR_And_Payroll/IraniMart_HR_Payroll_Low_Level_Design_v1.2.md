# IraniMart HR & Payroll — Low-Level Design (LLD)

**Version:** 1.2

**Date:** 2026-10-07

**Source Requirement:** [Requirements v1.5](./IraniMart_HR_Payroll_Requirements_v1.5.md)

**Status:** Architecture-fit Development Design Baseline — functional scope unchanged; Phase 0 prerequisites explicitly gated

**Target:** Clean Architecture modular monolith, independently extractable HR & Payroll bounded context

**Primary Consumers:** Developers, GitHub Copilot/Codex, reviewers, QA

**Supersedes:** LLD header v1.1, dated 2026-10-03. The available repository baseline is named `IraniMart_HR_Payroll_Low_Level_Design_v1.0.md`; its header, not its filename, identifies v1.1. This revision does not alter that file or Requirements v1.5.

## Revision History

| Version | Date | Change |
|---|---|---|
| 1.1 | 2026-10-03 | Modular-monolith baseline, source-owned operational locations and generic extended/cross-midnight shift rules |
| 1.2 | 2026-10-07 | Maps persistence, permissions/navigation, capabilities, Audit v2, new durable messaging, HR Approval Kernel, private files and Accounting retry/correction contracts to verified current code; adds Phase 0, explicit close gates, open decisions and adaptation register |

Requirements v1.5 remains the functional source of truth. Repository constraints determine implementation mechanisms, not removal of approved functions. New contracts/tables/jobs described below are proposed implementation work unless explicitly identified as existing. See Sections 38–41 for decisions, adaptations, conflicts and current-source evidence.

Revision inputs: [Requirements v1.5](./IraniMart_HR_Payroll_Requirements_v1.5.md), [header-v1.1 LLD baseline](./IraniMart_HR_Payroll_Low_Level_Design_v1.0.md), and the previously completed **IraniMart HR & Payroll Architecture Fit and Implementation Plan (2026-10-07)**. The assessment was retained outside the repository in session artifacts; it is not a production-code dependency.

---

## 1. Purpose

This document translates the approved HR & Payroll requirements into a development-level design covering:

- module boundaries and dependency rules;
- database schemas, tables, keys, constraints and indexes;
- aggregate boundaries and domain invariants;
- CQRS commands/queries and handler responsibilities;
- REST API contracts and action endpoints;
- reusable approval workflow;
- role, permission, menu and location-scope enforcement;
- audit design;
- attendance-device integration;
- leave/overtime/payroll algorithms and state machines;
- payment, reconciliation and Accounting integration;
- background jobs;
- Angular/front-end structure;
- migration and seed strategy;
- automated tests;
- phased implementation slices.

This LLD is intentionally concrete enough to guide implementation.

The current IraniMart backend is a **single deployable monolith already organized using Clean Architecture projects**:

```text
pos-v2/
├── src/
│   ├── RetailPOS.API
│   ├── RetailPOS.Application
│   ├── RetailPOS.Domain
│   └── RetailPOS.Infrastructure
├── tests/
│   ├── RetailPOS.IntegrationTests
│   └── RetailPOS.UnitTests
└── retailpos-frontend/
```

Sales, Purchase, Inventory and Accounting are already implemented inside this solution.

The HR & Payroll implementation must therefore extend the existing solution as a **well-isolated modular bounded context inside the current monolith**. Do not introduce new microservices, a second solution layout, or broad rewrites of existing Sales/Purchase/Inventory/Accounting as a prerequisite.

Where the existing IraniMart codebase already has a shared abstraction, naming convention, ID type, audit mechanism, authorization framework, navigation, error handling, MediatR convention or migration pattern, reuse it. Do not create a parallel framework merely to match an example name. The current Domain assembly uses the `RetailPOS.Core` namespace; there is no common base entity to assume or retrofit across existing modules.

### 1.1 Verified Current Implementation Baseline

| Concern | Existing implementation | v1.2 direction |
|---|---|---|
| Backend | .NET 10; four existing Clean Architecture projects; Sales/Purchase/Inventory/Accounting already implemented | Logical HR/Payroll modules in those projects; no new service or broad rewrite |
| Persistence | `RetailPOSDbContext`, EF/Npgsql 9, snake_case; baseline `20261004095137_InitialBaseline` | Same context/migration assembly; additive forward migrations; `hr_*`/`payroll_*` tables |
| CQRS | Application assembly MediatR registration, FluentValidation, `ICommand`/`IQuery`, narrow stores; general validation behavior only | Explicit use-case scope/capability/transaction/audit/idempotency orchestration |
| Authorization/navigation | Code `PermissionCatalog`, API policies, JSON role grants, additive user overrides, static Angular `NAV_SECTIONS`, role-editor categories | Extend these surfaces; no relational Menu/Action/Permission framework |
| Entitlements | `BusinessFeatureSetting`; missing setting/scope can evaluate enabled | Business-scoped opt-in rules for new HR capabilities; Payroll default OFF |
| Audit | Legacy `AuditLog`, current central `AuditEvent`/entities/field changes | Audit v2 only for new HR/Payroll integration |
| Delivery | Non-durable in-process publisher; no executable RabbitMQ/outbox/inbox | New minimal shared durable delivery foundation in Phase 0 |
| Locations | Outlet and Warehouse; no Factory source | HR local projection; Factory ownership decision before Factory work |
| Approvals | Module-specific approvals; no generic engine | New small HR/Payroll Approval Kernel; existing workflows unchanged |
| Files | Public product `/uploads` | Private HR documents, payslips, imports and exports |
| Accounting | Existing Application system-journal contracts, open-period validation, linked reversal | Payroll wrapper adds frozen request/hash and repeat-success correction resolution |
| Frontend | Angular 21 standalone components/routes, services/models/pages | Existing shell/navigation/guards; new capability-aware behavior |

---

## 2. Design Authority and Non-Negotiable Rules

1. Maintain existing Clean Architecture dependency direction.
2. HR and Payroll are one bounded context for now, but Payroll remains an optional capability.
3. HR must operate with `Payroll.Enabled = false`.
4. No HR Domain dependency on Identity, Accounting, file storage, email, biometric vendors, EF Core or Angular.
5. Do not use another module's tables as HR domain tables.
6. No direct writes from Payroll to Accounting tables.
7. Do not add database foreign keys from HR to Identity or Accounting internal tables.
8. Use explicit ports/contracts for cross-module dependencies.
9. Preserve effective-dated and historical records; avoid destructive overwrite of business history.
10. Use current central Audit v2 through an Application abstraction; never create a second HR audit system.
11. Extend the existing code permission catalogue, API policies, JSON role grants, static navigation and role-editor categories; do not create relational Menu/Action/Permission tables.
12. Backend permission/capability/location-scope checks are authoritative.
13. All command handlers must be idempotent/concurrency-safe where duplicate submission is realistic.
14. Raw biometric/device punches are immutable evidence and are never final attendance directly.
15. Historical payroll must be reproducible from snapshots and versioned/effective-dated inputs.
16. Closed payroll periods are read-only except through controlled reopen/adjustment/off-cycle workflows.
17. Every user-facing slice includes route/page/static navigation, code permission/API policy, role category/grant upgrade, capability/scope, Audit v2, migration/model validation and tests where applicable.
18. Keep the current monolithic deployment; HR/Payroll is added as logical modules inside the existing `RetailPOS.*` projects.
19. Existing Sales, Purchase, Inventory and Accounting modules are not to be rewritten before HR/Payroll begins.
20. Outlet/Warehouse remain source-owned; HR must not create a competing authoritative master. Factory does not exist today: decide its source ownership before implementing Factory projection/policies/events.
21. HR may maintain local integration projections/read models of source-owned operational data for isolation and performance.
22. Implement one new minimal reusable transactional outbox, publisher and inbox foundation for reliable cross-boundary delivery. Existing non-durable publishing is not that foundation.
23. Same-process/same-database availability must not be used to justify Domain-layer coupling or direct writes across bounded contexts.
24. Initially use `RetailPOSDbContext`; no `HrPayrollDbContext`, baseline regeneration, replacement or destructive rollback.
25. Required Business-scoped capabilities fail closed when scope/configuration is missing; preserve unrelated feature defaults.
26. Confidential documents/payslips/exports are private and authorize each read/stream; no public product-upload reuse.


---

## 3. Logical Deployment Model

```text
┌────────────────────────────────────────────────────────────┐
│                        Angular UI                          │
│ HR routes                              Payroll routes      │
└──────────────────┬──────────────────────────┬──────────────┘
                   │                          │
                   ▼                          ▼
┌────────────────────────────────────────────────────────────┐
│                   Presentation / API                       │
│ /api/hr/v1/*                         /api/payroll/v1/*      │
│ capability + auth + request/response transport concerns    │
└────────────────────────────┬───────────────────────────────┘
                             ▼
┌────────────────────────────────────────────────────────────┐
│                       Application                          │
│ Commands | Queries | Validators | Auth Requirements        │
│ Ports | DTOs | Approval orchestration | Integration events │
└────────────────────────────┬───────────────────────────────┘
                             ▼
┌────────────────────────────────────────────────────────────┐
│                         Domain                             │
│ Aggregates | Value Objects | Rules | State transitions     │
│ Domain events | Calculation services                       │
└────────────────────────────▲───────────────────────────────┘
                             │ implemented by
┌────────────────────────────┴───────────────────────────────┐
│                     Infrastructure                         │
│ EF persistence | Files | Email | Device adapters           │
│ Accounting adapter | Outbox/Inbox | Jobs | Exports         │
└────────────────────────────────────────────────────────────┘
```

### 3.1 Current Project / Folder Boundary

Use the current solution layout instead of introducing a new `Modules/HrPayroll/*` solution structure.

Recommended logical placement:

```text
src/
├── RetailPOS.API/
│   └── Controllers/
│       ├── HR/
│       └── Payroll/
│
├── RetailPOS.Application/
│   ├── HR/
│   │   ├── Common/
│   │   ├── Organisation/
│   │   ├── Employees/
│   │   ├── Approvals/
│   │   ├── Attendance/
│   │   ├── Leave/
│   │   ├── Overtime/
│   │   ├── Exit/
│   │   ├── Reports/
│   │   └── Imports/
│   └── Payroll/
│       ├── Common/
│       ├── Salary/
│       ├── Loans/
│       ├── Periods/
│       ├── Runs/
│       ├── Payments/
│       ├── Reconciliation/
│       ├── Payslips/
│       ├── FinalSettlement/
│       └── Reports/
│
├── RetailPOS.Domain/
│   ├── HR/
│   │   ├── Organisation/
│   │   ├── Employees/
│   │   ├── Approvals/
│   │   ├── Attendance/
│   │   ├── Leave/
│   │   ├── Overtime/
│   │   └── Exit/
│   └── Payroll/
│       ├── Salary/
│       ├── Loans/
│       ├── Periods/
│       ├── Runs/
│       ├── Payments/
│       └── FinalSettlement/
│
└── RetailPOS.Infrastructure/
    ├── HR/
    │   ├── Persistence/
    │   ├── Projections/
    │   ├── IdentityIntegration/
    │   ├── AttendanceDevices/
    │   ├── Documents/
    │   ├── Imports/
    │   └── Messaging/
    └── Payroll/
        ├── Persistence/
        ├── AccountingIntegration/
        ├── Exports/
        └── Messaging/

tests/
├── RetailPOS.UnitTests/
│   ├── HR/
│   └── Payroll/
└── RetailPOS.IntegrationTests/
    ├── HR/
    └── Payroll/
```

The exact folder names should follow the current codebase's feature conventions, but this logical separation must remain.

### 3.2 Deployment and Module Interaction

Current deployment remains one application/process.

```text
Angular UI
   ↓
RetailPOS.API
   ↓
RetailPOS.Application
   ↓
RetailPOS.Domain
   ↑
RetailPOS.Infrastructure
   ↓
Single deployed IraniMart ERP
```

Logical module interaction:

```text
Sales / POS ─────┐
Inventory ───────┼── explicit contracts/events/projections ──> HR Core
Purchase ────────┘
                                                         ↓
                                                      Payroll
                                                         ↓
                                         Accounting contract/event
```

The goal is a **modular monolith now, extractable later**.

---

## 4. Database Conventions

### 4.1 Existing Context, Migration Assembly and Table Ownership

Use the existing production `RetailPOSDbContext` and its migration assembly initially. A second `HrPayrollDbContext` is explicitly out of scope. Keep existing tables, default schema and migration history unchanged.

The physical table convention for this revision is:

```text
hr_*       → HR core, Approval Kernel, attendance, leave, overtime, exit
payroll_*  → salary, runs, payments, reconciliation, final settlement
integration_* → new shared outbox/inbox foundation (Section 9)
```

All module tables/columns use existing snake_case naming. Configure module mappings explicitly through the current context; do not assume configuration assembly scanning exists. Keep configurations/stores logically grouped by feature without moving existing entities.

Add forward-only HR/Payroll migrations **after** `20261004095137_InitialBaseline`, updating the same model snapshot. Do not regenerate, squash, replace or rerun the baseline as an upgrade strategy. Separate schemas/context may be evaluated only in a future approved extraction ADR, not introduced during initial HR work.

Migration tests currently assert a single baseline. During implementation, extend them to verify the preserved baseline plus new forward migrations, clean installation and upgrade from an existing baseline database. Startup suppresses pending-model warnings; explicit model/snapshot validation is required and no HR startup ad-hoc DDL is permitted.

### 4.2 Key and Column Conventions

- Use `bigint/long` for current internal IDs and external User/Business/Accounting references. GUIDs remain appropriate for message/correlation IDs without changing entity primary keys.
- Cross-module references such as `IdentityUserId`, `AccountingJournalId`, `BusinessId` are references only; do not create FK constraints to another bounded context's internal tables.
- Use UTC instants explicitly for new records; retain local date/time plus timezone for workforce semantics. Do not change legacy Npgsql timestamp behavior globally.
- Configure new money amounts explicitly as `numeric(18,2)` where matching current currency/accounting precision; intermediate rates/quantities as `numeric(18,6)` unless an approved business rule requires different precision. Round at the frozen policy-defined boundary, not repeatedly per intermediate step.
- Store operational local date/time separately where business semantics require local time.
- Monetary values: `decimal(18,2)` unless existing project monetary precision is higher.
- Hours/durations: store integer minutes wherever possible to avoid floating-point drift.
- Use PostgreSQL-compatible optimistic concurrency (`xmin`, as used by Inventory, or an explicit numeric version) for mutable workflow aggregates; `RowVersion` below is logical, not SQL Server rowversion.
- Use soft deactivation/status for historical master data instead of physical delete.
- There is no existing common base entity. Use explicit HR-local metadata consistent with existing `CreatedAt`/`UpdatedAt` and actor fields; do not introduce/retrofit a platform-wide base class or `RStatus`.
- Sensitive values must not be copied into generic logs.
- Every HR/Payroll tenant record requires `BusinessId`, either explicitly or through a mandatory module-owned parent; every read/write/export/worker must prove its Business fence. There is no current global tenant query filter.
- Internal module FKs and tenant-safe parent validation are required. Cross-module stable references have no navigation/FK to Identity, operational masters or Accounting internals.

### 4.3 Logical Common Columns

These are logical HR-local metadata; no existing base class or shared unit of work is assumed. Map UTC names to existing snake_case physical conventions. `BusinessId` is required for module-owned tenant records, not a nullable platform/default Business.

```text
Id
BusinessId / TenantId
IsActive or RStatus
CreatedAtUtc
CreatedByUserId
ModifiedAtUtc
ModifiedByUserId
Version / RowVersion
```

---

# 5. Database Design

## 5.1 Organisation and Operational Location Projection

### 5.1.1 Ownership Decision

IraniMart currently contains source-owned `Outlet` and `Warehouse` entities. **Factory does not exist in the inspected Domain/DbContext.** Factory source ownership and identity/lifecycle contract are a Phase 0 architecture decision, not an assumption that a Factory table or event stream exists.

Outlet/Warehouse IDs are `long`; legacy Business ownership can be nullable. Bootstrap accepts only records with a validated owning Business; unowned/ambiguous source rows are reported for remediation, never assigned to an arbitrary Business. Current Outlet has a hard-delete path and no active/version stream. Source version/deletion contracts below are new work.

Therefore:

```text
❌ Do not create a second authoritative `hr_locations` master.
✅ Existing operational module remains source of truth.
✅ HR keeps a local projection/read model for the fields it needs.
```

Target flow:

```text
Existing Outlet / Warehouse source
                ↓ same local source transaction
        NEW shared transactional Outbox
                ↓
        NEW publisher / RabbitMQ transport
                ↓
        NEW inbox + HR idempotent consumer
                ↓
      OperationalLocationProjection
```

Factory/HeadOffice/other source adapters are added only after their ownership ADR is approved. Warehouse is not a Factory alias. Retain the approved generic location/location-type functionality through HR policy/type configuration over source-owned projections; HR does not edit another module's operational master.

### `hr_operational_location_projections`

Purpose: local HR-owned integration projection of source operational locations.

| Column | Type | Rules |
|---|---|---|
| Id | bigint | HR-local PK |
| BusinessId | bigint/reference | copied stable business reference |
| SourceSystem | string(50) | e.g. `RetailPOS` |
| SourceEntityType | string(50) | initially `Outlet` or `Warehouse`; future `Factory`/others require approved source contract |
| SourceEntityId | string(80) | stable canonical source identifier; current long IDs serialized invariantly |
| Code | string(100)? | optional; only if source exposes stable code |
| Name | string(150) | copied display value |
| Address | string(500)? | copied where useful |
| ContactNumber | string(100)? | copied where useful |
| ManagerExternalUserId | string/bigint? | source reference only |
| ParentSourceEntityId | string/bigint? | optional |
| IsSourceActive | bool? | only if source exposes lifecycle state |
| SourceUpdatedAtUtc | datetime? | source timestamp if available |
| SourceVersion | long/string | required agreed monotonic version/change sequence; source adapter prerequisite |
| LastSyncedAtUtc | datetime | required |
| IsSourceDeleted | bool | tombstone; preserve historical references |
| SyncStatus | string | Current/Stale/Conflict/SourceUnavailable |

Constraints/indexes:
- unique `(BusinessId, SourceSystem, SourceEntityType, SourceEntityId)`;
- index `(BusinessId, SourceEntityType)`;
- index `(BusinessId, Name)`;
- do not expose normal HR create/edit/delete operations for source-owned operational location data.

### Integration Events

Extend the authoritative source write paths narrowly to append versioned integration events to the **new** shared outbox in the source transaction. No broker or location lifecycle events exist to assume today. Proposed initial event families:

```text
OutletCreated
OutletUpdated
OutletDeleted       // tombstone for current hard-delete behavior
OutletDeactivated  // only if an approved source lifecycle adds deactivation

WarehouseCreated
WarehouseUpdated
WarehouseDeleted
WarehouseDeactivated // only if source lifecycle supports it
```

Define one versioned envelope in Section 9, with Business, source identity/version, correlation and tombstone semantics. Do not generate `FactoryCreated`/`FactoryUpdated`/`FactoryDeactivated` contracts or publishers until Factory ownership/source is decided. Examples elsewhere using Factory policies remain required future functionality, explicitly gated by that decision.

Consumers must be idempotent. If a monotonic source version exists, stale/out-of-order events must not overwrite newer projection state.

### Initial Sync / Repair

Events cover only source changes after integration is active. HR therefore requires:

1. a source-owned Business-scoped read/export Application port for initial paginated bootstrap;
2. an agreed snapshot high-water mark and monotonic source version/change sequence, or a bounded source-write pause during bootstrap if the source cannot provide one;
3. replay/consume subsequent events without letting a stale bootstrap row overwrite a newer event;
4. continuous outbox/broker/inbox synchronization once the Phase 0 foundation is enabled.

A periodic/on-demand reconciliation compares source IDs/versions and repairs missing/stale projections through the same Application use case. Confirm deletion from a complete successful source snapshot or explicit tombstone, not from a failed/partial page. A source outage is an observable stale state, not mass deactivation. While the foundation is being commissioned, bootstrap/reconciliation may provide an explicitly identified transitional read projection; event synchronization is not considered delivered until recovery/duplicate tests pass.

Source rename, deactivation or deletion never removes a projection referenced by historical assignments/attendance/payroll. Display historical snapshots and prohibit new assignments to confirmed inactive/deleted sources according to policy.

### `hr_org_units`

HR-owned organisation hierarchy for Division/Department/Team/Section.

| Column | Type | Rules |
|---|---|---|
| Id | bigint | PK |
| BusinessId | bigint/reference | required |
| Code | string(50) | required |
| Name | string(150) | required |
| UnitType | string/enum | Division/Department/Team/Section/etc. |
| ParentOrgUnitId | bigint? | self FK |
| OperationalLocationProjectionId | bigint? | optional local HR projection scope |
| EffectiveFrom | date | required |
| EffectiveTo | date? | optional |
| IsActive | bool/status | required |

Unique `(BusinessId, Code)`.
Prevent hierarchy cycles.

The Departments & Teams page uses the existing OrganisationUnit create/update
APIs (`POST /api/hr/v1/org-units`, `PUT /api/hr/v1/org-units/{id}`).
Viewing requires `hr.organisation.view`; both Add and Edit require the existing
`hr.organisation.manage` permission and HR capability. The standard dialog offers
Code, Name, Type (Department/Team), Parent and Active/Inactive status. Parent
references remain Business-scoped, active and acyclic; existing domain types and
location scope are not redefined.

Normal creation omits effective dates: the server sets `EffectiveFrom` to the UTC
creation date, consistent with existing HR date handling. Normal editing omits
`EffectiveFrom`, retaining its stored historical value, and preserves the existing
`EffectiveTo` and operational location scope. Explicit legacy API dates remain
supported and validated. No schema change is needed. Activation/deactivation uses
`IsActive` through update; there is no separate status-action or delete endpoint.

### `hr_positions`

| Column | Type | Rules |
|---|---|---|
| Id | bigint | PK |
| BusinessId | bigint/reference | required |
| Code | string(50) | required |
| Name | string(150) | required |
| OrgUnitId | bigint | FK → hr_org_units |
| OperationalLocationProjectionId | bigint? | legacy read-only value; not populated by Position writes |
| ReportsToPositionId | bigint? | self FK |
| Status | enum | Vacant/Filled/TemporarilyFilled/Inactive |
| EffectiveFrom | date? | legacy read-only value; new Positions are undated |
| EffectiveTo | date? | legacy value / deactivation evidence |

Unique `(BusinessId, Code)`.
Prevent `ReportsToPositionId == Id` and reporting cycles.

**IraniMart Position simplification (2026-10-22):** A Position is a reusable job
role within a Department / Team (`OrgUnitId` retains its domain/API name).
The Position form contains only Code, Position Name, Department / Team, Reports
To and Status; the list omits location/effective columns. Employee position
assignments own Primary/Temporary/Acting and effective intervals; separate
employee location assignments and approved transfers own operational locations
and their effective history.

Position location and effective dates are retained in read responses and storage
for legacy compatibility, but normal creates leave them null and normal updates
do not overwrite them. Legacy request properties remain accepted but are ignored
by create/update. Position dates no longer gate employee assignments or opening
imports. Those workflows still validate employee employment/assignment dates,
Department/Team membership and validity, active Position status, and employee
location/transfer evidence.

Employee assignment/import authorization no longer derives from the selected
Position's location. Existing effective-primary employee scope checks, import
target-location checks, permissions, HR capability and Business boundaries remain.
Position master reads/writes retain Department/Team location visibility but ignore
legacy Position scope, including for Reports To references.

Authorization dependency inventory for this transition:

| Previous dependency | Replacement / retained evidence |
|---|---|
| Position list filtered by both Position and Department/Team location | Department/Team visibility only |
| Position create checked the submitted Position location | Business/permission/HR capability plus Department/Team visibility |
| Position update checked submitted and existing Position scope | Existing and requested Department/Team visibility |
| Position deactivate and Reports To checked Position location, falling back to its Department/Team | Department/Team visibility only |
| Employee position-assignment handler resolved the selected Position's location for authorization | Existing effective-primary employee location access plus unchanged assignment permission |
| Opening-import row authorization resolved Position location | Existing row target-location and employee-scope checks plus unchanged assignment permission |
| Position start/end dates gated employee position assignments and opening imports | Employee employment/assignment dates and Department/Team validity; Position must still be active |

Previously null Position location already meant no direct Position restriction,
with Department/Team scope used as a fallback. Employee transfer and lifecycle
handlers have no direct Position-date gate: they already use employee history,
location scope and effective-date evidence. Their behavior is unchanged. Separate
position and location histories remain separate; no new combined assignment model
or Factory projection type is introduced by this cleanup.

The single `SimplifyReusablePositionMaster` migration makes only
`hr_positions.effective_from` nullable, without backfilling dates, dropping columns
or rewriting historical values. Applying it requires deployment approval; it must
precede deploying writes that omit dates. Rollback to NOT NULL fails if undated
Positions exist rather than fabricating dates.

Vacant/Filled/TemporarilyFilled remain existing manually maintained statuses.
They are not computed occupancy, and can diverge from assignments; changing that
model is a separate design decision.

### Historical Snapshot Rule

The projection is current reference/read data. Historical attendance/payroll must preserve the operational context that applied at the time.

Example:

```text
SourceEntityType = Outlet
SourceEntityId = 10
LocationNameSnapshot = "Uttara Outlet"
LocationCodeSnapshot = "..."
```

A later source rename/deactivation must not rewrite historical payroll/attendance meaning.

---

## 5.2 Employee Foundation

### `hr_employment_types`

Seed/configurable types: Permanent, Probation, Contractual, PartTime, Temporary, Intern, Other.

Unique `(BusinessId, Code)`.

### `hr_employees`

This is the employee identity/master aggregate root. Do not make separate OutletEmployee/FactoryEmployee tables.

| Column | Type | Rules |
|---|---|---|
| Id | bigint | PK |
| BusinessId | bigint/reference | required |
| EmployeeCode | string(50) | required |
| FirstName | string(100) | required |
| MiddleName | string(100)? | optional |
| LastName | string(100)? | optional |
| DisplayName | string(200) | required |
| DateOfBirth | date? | permission-protected |
| GenderCode | string(30)? | optional |
| NationalId | string(100)? | encrypted/masked if existing security supports |
| PassportNo | string(100)? | protected |
| PersonalEmail | string(200)? | optional |
| WorkEmail | string(200)? | optional |
| Mobile | string(50)? | optional |
| PresentAddress | string/text? | protected |
| PermanentAddress | string/text? | protected |
| EmergencyContactName | string(150)? | optional |
| EmergencyContactPhone | string(50)? | optional |
| CurrentEmploymentStatus | enum | Active/Probation/Inactive/Suspended/Exited/etc. |
| CurrentEmploymentTypeId | bigint? | denormalized current pointer |
| CurrentPositionId | bigint? | denormalized current pointer |
| CurrentOrgUnitId | bigint? | denormalized current pointer |
| CurrentPrimaryLocationProjectionId | bigint? | denormalized current pointer |
| JoiningDate | date | required |
| ConfirmationDate | date? | optional |
| LastWorkingDate | date? | optional |

Constraints/indexes:
- unique `(BusinessId, EmployeeCode)`;
- optional unique work email per business if business rule allows;
- index current location/status;
- current pointers are convenience fields; historical truth remains assignment/employment tables.

### `hr_employment_periods`

Tracks join/rehire/exit periods.

| Column | Type |
|---|---|
| Id | bigint PK |
| EmployeeId | bigint FK |
| EmploymentTypeId | bigint FK |
| StartDate | date |
| EndDate | date? |
| StartReason | string(250)? |
| EndReason | string(500)? |
| Status | Active/Closed |
| SourceEventType | enum/string |

Constraint: one active period per employee.

### `hr_employee_lifecycle_events`

Immutable lifecycle history.

| Column | Type |
|---|---|
| Id | bigint PK |
| EmployeeId | bigint FK |
| EmploymentPeriodId | bigint FK |
| EventType | Joined/Probation/Confirmed/Promotion/PositionChanged/OrgUnitChanged/ManagerChanged/EmploymentTypeChanged/Suspended/Reinstated/Exited/Rehired |
| EffectiveDate | date |
| PreviousStateJson | json/text |
| NewStateJson | json/text |
| Reason | string(1000)? |
| ApprovalInstanceId | bigint? |
| ActorUserId | external user ref |
| OccurredAtUtc | datetime |

Index `(EmployeeId, EffectiveDate, Id)`.

### `hr_employee_position_assignments`

| Column | Type |
|---|---|
| Id | bigint PK |
| EmployeeId | bigint FK |
| PositionId | bigint FK |
| OrgUnitId | bigint FK |
| ReportsToEmployeeId | bigint? FK to employee |
| AssignmentType | Primary/Temporary/Acting |
| EffectiveFrom | date |
| EffectiveTo | date? |
| Reason | string(500)? |
| ApprovalInstanceId | bigint? |
| IsCurrent | bool |

No overlapping active primary position assignment unless explicitly approved by policy.

### `hr_employee_location_assignments`

| Column | Type |
|---|---|
| Id | bigint PK |
| EmployeeId | bigint FK |
| OperationalLocationProjectionId | bigint FK |
| AssignmentType | Primary/Additional |
| IsTemporary | bool |
| EffectiveFrom | date |
| EffectiveTo | date? |
| Status | Draft/PendingApproval/Approved/Effective/Closed/Rejected/Cancelled |
| Reason | string(500)? |
| ApprovalInstanceId | bigint? |
| RequestedByUserId | external ref |
| ApprovedByUserId | external ref? |

Constraints:
- prevent overlapping primary assignments for same employee unless policy says otherwise;
- index `(EmployeeId, EffectiveFrom, EffectiveTo)`;
- index `(OperationalLocationProjectionId, Status, EffectiveFrom)`.

### `hr_employee_user_links`

No FK to Identity user table.

| Column | Type |
|---|---|
| Id | bigint PK |
| EmployeeId | bigint FK |
| IdentityUserId | string/long external ref |
| LinkedAtUtc | datetime |
| LinkedByUserId | external ref |
| UnlinkedAtUtc | datetime? |
| UnlinkedByUserId | external ref? |
| UnlinkReason | string(500)? |
| IsActive | bool |

Constraints:
- unique active link by EmployeeId;
- unique active link by IdentityUserId;
- retain inactive historical rows.

---

## 5.3 Documents

### `hr_document_types`

Configurable document category master.

### `hr_employee_documents`

| Column | Type |
|---|---|
| Id | bigint PK |
| EmployeeId | bigint FK |
| DocumentTypeId | bigint FK |
| DocumentNumber | string(100)? |
| IssueDate | date? |
| ExpiryDate | date? |
| CurrentVersionId | bigint? |
| Status | Active/Expired/Replaced/Archived |
| Notes | string(1000)? |

### `hr_employee_document_versions`

| Column | Type |
|---|---|
| Id | bigint PK |
| EmployeeDocumentId | bigint FK |
| VersionNo | int |
| StorageKey | string(500) |
| OriginalFileName | string(255) |
| ContentType | string(100) |
| SizeBytes | bigint |
| UploadedByUserId | external ref |
| UploadedAtUtc | datetime |
| ReplacesVersionId | bigint? |
| Hash | string(64), required SHA-256 content hash |
| BusinessId | bigint, required tenant reference |
| Classification | EmployeeDocument; shared adapter also supports Payslip/PayrollExport/ImportEvidence |
| ScanStatus | Pending/Clean/Rejected/Error; download only Clean |
| EncryptionKeyReference | string? key identifier only, never key material |
| RetainUntilUtc | datetime? retention policy/hold aware |
| RevokedAtUtc | datetime? stops access without deleting history |

Unique `(EmployeeDocumentId, VersionNo)`.

Files are stored through an Application-owned `IDocumentStorage` port; DB stores metadata/reference only. This is a **new private adapter**, not reuse of public product upload behavior. Sections 13.5 and 17 govern reads; the private storage contract is detailed in Section 5.3.1.

### 5.3.1 Private Storage and Delivery Contract

Store blobs outside `wwwroot`/`/uploads`, or in a private object-storage container with public access disabled. Existing product images are public static files with public caching; their URLs/storage policy are prohibited for HR documents, payslips, salary imports and exports.

Proposed narrow Application port operations: `StagePrivateAsync`, `ValidateContentAsync`, `CommitVersionAsync`, `OpenAuthorizedReadAsync`, `RevokeAsync` and retention cleanup. Infrastructure implements storage/scan/encryption. The API accepts streams, never a caller-selected filesystem path or raw storage key.

Workflow:

1. Require verified Business, HR/Payroll capability, document/upload permission, employee scope and field classification permission.
2. Validate content bytes/MIME/extension and configured size, sanitize filename, generate an opaque storage key, compute hash and quarantine for scan. File availability must not depend on extension alone.
3. Store immutable version metadata and Audit v2 in the local database transaction. Blob staging is not claimed to be database-atomic: failed commit leaves a staged orphan for owned cleanup; failed scan marks explicit rejection/error.
4. Publish a version only after successful scan/metadata commit; replacement points at a new immutable version. Maintain issue/expiry dates, reminders and retention/legal-hold policy; expiry restricts use according to document policy without destroying evidence.
5. Download/print/email/export handlers revalidate capability, Business, employee/location scope, document classification and action permission on every request. Initial payslip access is HR/Admin only as required by v1.5; optional employee self-service is not enabled by employee linkage.
6. Stream via authenticated API with `Cache-Control: private, no-store`, safe `Content-Disposition`, correct content type and no public storage key response. Log/persist an Audit v2 access attempt before releasing confidential content and record outcome without file bytes.
7. Prefer authenticated streaming. If a signed URL is approved, it is short-lived, version/Business-bound and issued only after authorization/audit; use a validated download gateway when revocation/per-request authorization is required. Never persist a permanent public bearer URL; exclude URL tokens from logs.
8. Email uses an authorized recorded recipient and a protected download link by default. Attachment delivery requires an explicit approved confidential-email policy; audit send/result and do not add a public link.
9. Expiry/revocation/replacement invalidate new access grants; cleanup is version/retention/hold aware, audited and limited to owned keys. Background readers use explicit Business/service identity.

Payslip/export metadata reuses this private adapter and classification model, with frozen run/result/version references; physical HR document tables are not a competing payroll result store.

---

## 5.4 Reusable HR/Payroll Approval Kernel

Existing approvals are module-specific; no generic policy/instance/step engine was found. Implement a small Kernel within existing HR Domain/Application/Infrastructure folders. Do not refactor PO, stock-count, expense or other unrelated approval workflows as an HR prerequisite.

The Kernel supports Leave, Overtime, EmployeeTransfer, SalarySetup, SalaryRevision, Exit and PayrollRun. Payroll approval remains the required **single-level baseline**; leave/overtime support configured multi-level approval. Transfer/exit use the Kernel when configured. Salary maker-checker policy is enforced against the real User ID, not role name or acting-role identity.

Application-owned approver resolver ports obtain permitted User/Role, reporting-manager and location-manager candidates. Identity remains credential/permission authority. Snapshot policy version, scope, resolved step/quorum and delegation/substitution at request time so later edits cannot rewrite a pending/history decision.

### `hr_approval_policies`

| Column | Type |
|---|---|
| Id | bigint PK |
| BusinessId | external/tenant ref |
| Code | string(100) |
| ProcessType | enum/string |
| Name | string(150) |
| VersionNo | int |
| ScopeType | Business/LocationType/Location |
| ScopeId | string/long? |
| AllowSelfApproval | bool |
| EffectiveFrom | date |
| EffectiveTo | date? |
| IsActive | bool |

Unique `(BusinessId, Code, VersionNo)`.

### `hr_approval_policy_steps`

| Column | Type |
|---|---|
| Id | bigint PK |
| ApprovalPolicyId | bigint FK |
| Sequence | int |
| ApproverType | User/Role/ReportingManager/LocationManager |
| ApproverReference | string? |
| RequiredApprovals | int |
| CanReject | bool |
| CanCancel | bool |

Unique `(ApprovalPolicyId, Sequence)`.

### `hr_approval_instances`

| Column | Type |
|---|---|
| Id | bigint PK |
| PolicyId | bigint FK |
| ProcessType | string |
| TargetType | string |
| TargetId | bigint/string |
| RequestedByUserId | external ref |
| RequestedAtUtc | datetime |
| Status | Pending/Approved/Rejected/Cancelled |
| CurrentStepSequence | int? |
| CorrelationId | string/guid |
| BusinessId | bigint required |
| TargetVersion | long/int, binds approval to submitted content version/hash |
| RequestHash | string(64), frozen approved-target hash |
| Version | numeric/xmin workflow concurrency |

Unique idempotency constraint on process-specific stable target + active instance where appropriate.

### `hr_approval_instance_steps`

Frozen resolved step configuration with mutable current status projection. **Decision history is separate and append-only**; a Pending step status cannot be called immutable while changing it to Approved.

| Column | Type |
|---|---|
| Id | bigint PK |
| ApprovalInstanceId | bigint FK |
| Sequence | int |
| ResolvedApproverType | string |
| ResolvedApproverReference | string |
| ResolvedApproverCandidatesJson | JSON of frozen eligible User IDs and approved delegation/substitution references |
| Decision | Pending/Approved/Rejected/Skipped/Cancelled |
| DecisionByUserId | external ref? last effective decision projection, not complete quorum history |
| DecisionAtUtc | datetime? last effective decision projection |
| Comment | string(1000)? |
| IdempotencyKey | string(150)? |
| RequiredApprovalsSnapshot | int |
| PolicyStepVersionSnapshot | string/int |

Indexes:
- unique `(ApprovalInstanceId, Sequence)` for step projection.

### `hr_approval_decisions`

Append-only: BusinessId, InstanceId, StepId, DecisionKey, ActorRealUserId, ActingRoleId?, DelegatedForUserId?, Decision, Reason, RequestHash, OccurredAtUtc, CorrelationId.

- unique `(BusinessId, DecisionKey)`; same key/same payload returns the recorded outcome, same key/different payload returns conflict;
- unique applicable `(StepId, ActorRealUserId)` prevents one actor satisfying quorum twice;
- lock/concurrency-check instance and current step; verify capability/process permission, tenant/location scope, approver eligibility, sequence, quorum, self-approval and submitted target hash;
- commit decision, step/instance projection, target business transition, required Audit v2 and any outbox effect in one local transaction;
- reject/cancel reason is mandatory where the process requires it; retain history, do not delete decisions;
- policy changes create versions; cancellation/rejection is terminal and cannot be approved on retry;
- generic approval routes do not grant generic authority: process-specific permission/capability checks apply, including Payroll.Enabled for salary/run decisions.

Kernel foundation must be usable before Phase 1 transfers that require approval; Phase 3 completes leave/overtime resolvers and workflow use cases rather than inventing a second engine.

---

## 5.5 Shift, Roster, Holiday and Weekly Off

### `hr_shifts`

One generic Shift model must support normal, extended and cross-midnight work.

| Column | Type |
|---|---|
| Id | bigint PK |
| BusinessId | ref |
| Code | string(50) |
| Name | string(100) |
| StartTimeLocal | time |
| EndTimeLocal | time |
| CrossesMidnight | bool |
| MinimumWorkMinutes | int |
| MaximumAllowedWorkMinutes | int? |
| AllowedEarlyPunchMinutes | int |
| AllowedLatePunchMinutes | int |
| MinimumRestBeforeNextShiftMinutes | int? |
| GraceInMinutes | int |
| GraceOutMinutes | int |
| OvertimeEligible | bool |
| IsActive | bool |

Do not create Eid-specific shift entities. The same model covers normal showroom/factory shifts, extended factory production, seasonal/Eid work, urgent orders, stock counting, maintenance, late dispatch and other authorized long shifts.

### `hr_shift_breaks`

ShiftId, Sequence, Start/End or DurationMinutes, IsPaid.

### `hr_roster_assignments`

| Column | Type |
|---|---|
| Id | bigint PK |
| EmployeeId | bigint FK |
| WorkDate | date |
| ShiftId | bigint FK |
| OperationalLocationProjectionId | bigint FK |
| Source | Manual/Bulk/Template |
| Status | Draft/Published/Cancelled |
| Version | concurrency token |

Unique active roster `(EmployeeId, WorkDate)` unless split-shift is explicitly introduced later.

For cross-midnight work, canonical `WorkDate` is the **shift start date**.

```text
WorkDate = 15-Apr
Clock In = 15-Apr 10:00
Clock Out = 16-Apr 02:00
```

Expected result: one attendance record for `15-Apr`.

### Holiday Calendar

Holiday and Weekly Off are different concepts.

### `hr_holiday_calendars`

| Column | Type |
|---|---|
| Id | bigint PK |
| BusinessId | ref |
| Name | string(150) |
| ScopeType | Company/LocationType/Location |
| ScopeReference | string/bigint? |
| EffectiveFrom | date |
| EffectiveTo | date? |
| IsActive | bool |

Examples: company/global public holiday, all Factory holiday, all Outlet holiday, or one specific Outlet holiday.
For `Location` scope, reference the HR `OperationalLocationProjection`.

### `hr_holidays`

CalendarId, HolidayDate, Name, HolidayType, IsPaid, OvertimeRuleCode?, IsActive.

### Weekly Off

Weekly Off is recurring policy, not a holiday row.

### `hr_weekly_off_policy_versions`

| Column | Type |
|---|---|
| Id | bigint PK |
| BusinessId | ref |
| ScopeType | Company/LocationType/Location |
| ScopeReference | string/bigint? |
| VersionNo | int |
| EffectiveFrom | date |
| EffectiveTo | date? |
| IsActive | bool |

### `hr_weekly_off_policy_days`

PolicyVersionId, DayOfWeek, OptionalWeekPattern.

```text
Bashundhara Outlet → Tuesday
Jamuna Outlet      → Monday
Gazipur Factory    → Friday
```

Versioning is mandatory so later weekly-off changes do not rewrite historical attendance.

### `hr_employee_weekly_off_overrides`

EmployeeId, Date/DateRange, IsWeeklyOff, Reason, Effective dates.

Policy precedence:

```text
Employee Override
→ Explicit Published Roster
→ Specific Location Policy
→ Location-Type Policy
→ Company Policy
```

Day classification precedence:

```text
Employee Override
→ Explicit Roster
→ Holiday
→ Weekly Off
→ Normal Working Day
```

If Holiday and Weekly Off overlap, preserve enough information for deterministic attendance and overtime-rate resolution; do not double-count benefits.

---

## 5.6 Attendance Device Integration

### `hr_attendance_devices`

| Column | Type |
|---|---|
| Id | bigint PK |
| BusinessId | ref |
| DeviceCode | string(50) |
| SerialNumber | string(150) |
| VendorCode | string(50) |
| Model | string(100) |
| OperationalLocationProjectionId | bigint FK |
| ProtocolType | ADMS/Push/API/File/etc. |
| ConnectionMetadataJson | json/text, no secrets in plaintext if avoidable |
| FirmwareVersion | string(100)? |
| LastSeenAtUtc | datetime? |
| DeviceTimeZoneId | string(100)? |
| Status | Active/Offline/Disabled/Error |

Unique `(VendorCode, SerialNumber)` per deployment.

### `hr_device_user_mappings`

| Column | Type |
|---|---|
| Id | bigint PK |
| DeviceId | bigint FK |
| EmployeeId | bigint FK |
| DeviceUserId | string(100) |
| EffectiveFrom | datetime |
| EffectiveTo | datetime? |
| IsActive | bool |

Unique active `(DeviceId, DeviceUserId)`.
Index `(EmployeeId, IsActive)`.

### `hr_raw_attendance_punches`

Immutable device evidence.

| Column | Type |
|---|---|
| Id | bigint PK |
| BusinessId | ref |
| DeviceId | bigint FK |
| DeviceSerialSnapshot | string(150) |
| DeviceUserId | string(100) |
| EmployeeId | bigint? |
| PunchTimeLocal | datetime |
| PunchTimeUtc | datetime? |
| VerificationMethod | Face/Fingerprint/Card/Password/Unknown |
| DirectionCode | string? |
| ReceivedAtUtc | datetime |
| RawPayload | json/text? |
| IdempotencyFingerprint | string(128) |
| ProcessingStatus | Pending/Processed/Unmapped/Failed/IgnoredDuplicate |
| ProcessingError | string(2000)? |
| ProcessingVersion | int |

Unique `IdempotencyFingerprint`.
Indexes `(EmployeeId, PunchTimeLocal)`, `(ProcessingStatus, ReceivedAtUtc)`.

**Never update punch evidence except technical processing metadata. Never delete to "correct" attendance.**

---

## 5.7 Attendance Processing

### `hr_attendance_policy_versions`

Scope, version, effective dates and values such as late threshold, absent rule, minimum hours, punch pairing rule, early leave, rounding, employee override support.

### `hr_daily_attendance`

| Column | Type |
|---|---|
| Id | bigint PK |
| EmployeeId | bigint FK |
| WorkDate | date |
| OperationalLocationProjectionId | bigint FK |
| SourceLocationTypeSnapshot | string/ref |
| ShiftId | bigint? |
| RosterAssignmentId | bigint? |
| FirstInAt | datetime? |
| LastOutAt | datetime? |
| WorkedMinutes | int |
| BreakMinutes | int |
| LateMinutes | int |
| EarlyLeaveMinutes | int |
| OvertimeMinutesCalculated | int |
| Status | Present/Absent/Late/HalfDay/Leave/Holiday/WeeklyOff/MissingPunch |
| AttendancePolicyVersionId | bigint? |
| ProcessingVersion | int |
| LastProcessedAtUtc | datetime |
| IsManuallyAdjusted | bool |
| RowVersion | concurrency |

Unique `(EmployeeId, WorkDate)`.

### `hr_attendance_adjustments`

Immutable correction request/history.

| Column | Type |
|---|---|
| Id | bigint PK |
| DailyAttendanceId | bigint FK |
| AdjustmentType | ClockIn/ClockOut/Status/WorkedMinutes/etc. |
| PreviousValueJson | json/text |
| ProposedValueJson | json/text |
| Reason | string(1000) |
| RequestedByUserId | ref |
| RequestedAtUtc | datetime |
| ApprovalInstanceId | bigint? |
| Status | Draft/Pending/Approved/Rejected/Cancelled |
| AppliedAtUtc | datetime? |

Approved adjustment changes derived attendance state but retains original punch evidence and adjustment history.

---

## 5.8 Leave

### `hr_leave_types`
Code, Name, Paid/Unpaid, document requirement, active.

### `hr_leave_policy_versions`

| Column | Type |
|---|---|
| Id | bigint PK |
| BusinessId | ref |
| LeaveTypeId | bigint FK |
| ScopeType/ScopeId | policy scope |
| VersionNo | int |
| AccrualMethod | JoiningDate/Calendar/etc. |
| AnnualEntitlement | decimal |
| CarryForwardAllowed | bool |
| CarryForwardCap | decimal? |
| NegativeBalanceAllowed | bool |
| PayrollImpactCode | string |
| EffectiveFrom/To | date |
| IsActive | bool |

### `hr_leave_entitlements`

EmployeeId, LeaveTypeId, LeaveYearStart/End, Opening, Accrued, Used, Adjusted, CarriedForward, Expired, Remaining, PolicyVersionId.

Do not treat `Remaining` as the only truth; maintain ledger.

### `hr_leave_ledger`

Immutable balance ledger:
EmployeeId, LeaveTypeId, LeaveYearKey, TransactionType, Quantity, SourceType, SourceId, EffectiveDate, CreatedAt.

Unique source transaction idempotency where applicable.

### `hr_leave_requests`

EmployeeId, LeaveTypeId, FromDate, ToDate, RequestedUnits, Reason, Status, ApprovalInstanceId, RequestedBy/At.

### `hr_leave_request_days`

RequestId, LeaveDate, Units (1/0.5/etc.), IsPaidSnapshot, LocationIdSnapshot, Status.

### `hr_leave_year_closures`

LeaveYearStart/End, Scope, Status Open/ClosingReview/Closed, ClosedBy/At, ReopenedBy/At, Reason, ClosingPolicyVersion, CorrelationId.

Unique `(BusinessId, Scope, LeaveYearStart, LeaveYearEnd)`.

Closing writes carry-forward/expiry ledger entries idempotently.

---

## 5.9 Overtime

### `hr_overtime_policy_versions`

Scope, effective dates, eligibility, minimum minutes, rate type, multiplier/fixed rate, normal/weekly-off/public-holiday/special-day rates, optional slab rules, rounding.

### `hr_overtime_entries`

| Column | Type |
|---|---|
| Id | bigint PK |
| EmployeeId | bigint FK |
| WorkDate | date |
| DailyAttendanceId | bigint FK |
| CalculatedMinutes | int |
| ApprovedMinutes | int? |
| RateType/RateSnapshot | fields/json |
| CalculatedAmount | decimal? |
| Status | Calculated/PendingApproval/Approved/Rejected/PayrollEligible |
| PolicyVersionId | bigint |
| ApprovalInstanceId | bigint? |

Unique appropriate source `(EmployeeId, WorkDate, DailyAttendanceId)`.

HR-only tenants can stop at Approved/PayrollEligible without Payroll consumption.

---

# 6. Payroll Database Design

## 6.1 Salary Components and Structures

### `payroll_salary_components`

| Column | Type |
|---|---|
| Id | bigint PK |
| BusinessId | ref |
| Code | string(50) |
| Name | string(100) |
| ComponentType | Earning/Deduction |
| ValueType | Fixed/Percentage/Formula/Calculated |
| TaxTreatmentCode | string? |
| IsRecurring | bool |
| CalculationOrder | int |
| FormulaExpression | string? |
| IsActive | bool |

Unique `(BusinessId, Code)`.
Formula references must be validated and circular dependencies rejected.

### `payroll_salary_structures`
Reusable template header, optional.

### `payroll_salary_structure_lines`
StructureId, SalaryComponentId, DefaultValue/FormulaOverride, CalculationOrder.

### `payroll_employee_salary_versions`

Represents initial salary setup and subsequent revisions.

| Column | Type |
|---|---|
| Id | bigint PK |
| EmployeeId | bigint external within bounded context |
| VersionNo | int |
| EffectiveFrom | date |
| EffectiveTo | date? |
| Status | Draft/PendingApproval/Approved/Scheduled/Active/Rejected/Cancelled/Superseded |
| GrossReferenceAmount | decimal? |
| Reason | string(1000)? |
| ApprovalInstanceId | bigint? |
| CreatedBy/At | metadata |
| SubmittedBy/At | metadata |
| ApprovedBy/At | metadata |
| RejectedCancelledBy/At | metadata |
| DecisionReason | string(1000)? |
| RowVersion | concurrency |

Constraints:
- unique `(EmployeeId, VersionNo)`;
- prevent overlapping Approved/Scheduled/Active effective periods;
- maker cannot approve same request when configured;
- non-approved versions never feed payroll.

### `payroll_employee_salary_lines`

SalaryVersionId, SalaryComponentId, ValueTypeSnapshot, Amount/Rate, FormulaSnapshot, CalculationOrder, IsActive.

Unique `(SalaryVersionId, SalaryComponentId)` unless repeated components are explicitly allowed.

---

## 6.2 Loans and Advances

### `payroll_employee_loans`

EmployeeId, LoanType, PrincipalAmount, ApprovedAmount, DisbursedDate, RecoveryStartDate, RecoveryMode, FixedInstallment, OutstandingAmount, Status, ApprovalInstanceId.

### `payroll_loan_schedules`

LoanId, InstallmentNo, DuePayrollPeriod, PlannedAmount, ActualRecoveredAmount, Status.

Constraints:
- never recover > outstanding;
- unique `(LoanId, InstallmentNo)`.

#### Phase 4F Repository Implementation Contract

The repository implements loan and advance activation through the existing Approval Kernel. A request remains non-effective while Draft or PendingApproval; maker-checker approval is mandatory, and the terminal approval decision atomically records approval and disbursement. Rejection and cancellation leave the outstanding amount at zero and create no disbursement.

Recovery schedules are immutable numbered versions with immutable installment lines. Repayments and schedule allocations are append-only evidence; a schedule change appends a successor version rather than rewriting prior installments. Repayment is capped at the outstanding balance, and zero balance marks the loan Settled. FixedMonthly, Manual, and CustomSchedule modes are supported.

Payroll Periods are a later phase, so the current schedule uses `DateOnly` due dates instead of the proposed `DuePayrollPeriod` foreign key. The persistence model uses `payroll_loan_schedule_versions`, `payroll_loan_schedule_lines`, `payroll_loan_repayments`, and `payroll_loan_repayment_allocations` to preserve schedule and recovery history. No Payroll Period or salary-deduction integration is implied by this slice; such integration must be added in its approved phase.

---

## 6.3 Payroll Period and Run

### `payroll_periods`

| Column | Type |
|---|---|
| Id | bigint PK |
| BusinessId | ref |
| PeriodCode | string(30) |
| PeriodStart | date |
| PeriodEnd | date |
| PayDate | date? |
| Status | Open/Processing/Locked/Approved/Posted/Closed |
| LockedBy/At | metadata |
| ApprovedBy/At | metadata |
| PostedBy/At | metadata |
| ClosedBy/At | metadata |
| ReopenedBy/At | metadata |
| ReopenReason | string(1000)? |
| RowVersion | concurrency |

Unique `(BusinessId, PeriodStart, PeriodEnd)`.

### `payroll_runs`

| Column | Type |
|---|---|
| Id | bigint PK |
| PayrollPeriodId | bigint FK |
| RunNo | int |
| RunType | Regular/OffCycle/Correction |
| ScopeType | Business/Location/LocationType/etc. |
| ScopeId | string/long? |
| Status | Draft/PendingApproval/Approved/PaymentInProgress/Paid/Completed/Cancelled |
| ApprovalInstanceId | bigint? |
| CalculationPolicyVersion | string/ref |
| CurrencyCode | string(3) |
| DecimalPrecision | int |
| RoundingMode | string |
| CalculatedAtUtc | datetime? |
| SubmittedAtUtc | datetime? |
| ApprovedAtUtc | datetime? |
| RowVersion | concurrency |

Unique `(PayrollPeriodId, RunNo)`.
Block duplicate regular run for same scope unless explicitly allowed.

### `payroll_employee_results`

One row per employee per run.

| Column | Type |
|---|---|
| Id | bigint PK |
| PayrollRunId | bigint FK |
| EmployeeId | bigint |
| EmployeeCodeSnapshot | string |
| EmployeeNameSnapshot | string |
| LocationIdSnapshot | bigint? |
| LocationTypeCodeSnapshot | string? |
| PositionNameSnapshot | string? |
| SalaryVersionId | bigint |
| GrossEarnings | decimal |
| TotalDeductions | decimal |
| NetPay | decimal |
| PaidAmount | decimal |
| OutstandingAmount | decimal |
| Status | Calculated/Held/Payable/PartiallyPaid/Paid/Exception |
| SnapshotId | bigint |
| RowVersion | concurrency |

Unique `(PayrollRunId, EmployeeId)`.

### `payroll_employee_result_lines`

Normalized earning/deduction output.

| Column | Type |
|---|---|
| Id | bigint PK |
| PayrollEmployeeResultId | bigint FK |
| SalaryComponentId | bigint? |
| ComponentCodeSnapshot | string |
| ComponentNameSnapshot | string |
| LineType | Earning/Deduction |
| Quantity | decimal? |
| Rate | decimal? |
| Amount | decimal |
| SourceType | Salary/Attendance/OT/Leave/Loan/Manual/Tax/etc. |
| SourceId | string/bigint? |
| CalculationOrder | int |

### `payroll_input_snapshots`

Immutable calculation input snapshot.

| Column | Type |
|---|---|
| Id | bigint PK |
| PayrollEmployeeResultId | bigint FK |
| SnapshotVersion | int |
| SnapshotJson | json/text |
| Hash | string(128) |
| CreatedAtUtc | datetime |

Snapshot must include salary lines, attendance summary, leave, OT, loans, tax/statutory, adjustments, location allocation and policy/rounding versions.

### `payroll_adjustments`

RunId/EmployeeResultId, Type Earning/Deduction, Component/reference, Amount, Reason, CreatedBy, Approval if configured, Status.

---

## 6.4 Payment, Batch and Reconciliation

### `payroll_payment_batches`

| Column | Type |
|---|---|
| Id | bigint PK |
| PayrollRunId | bigint FK |
| BatchReference | string(100) |
| Method | Bank/Mobile/Cash/Mixed |
| ExternalFileReference | string? |
| ExpectedAmount | decimal |
| Status | Draft/Ready/Processing/Completed/PartiallyFailed/Failed |
| GeneratedBy/At | metadata |
| SubmittedAtUtc | datetime? |
| SubmittedByUserId | user reference? |
| SubmissionExternalReference | string(150)? |
| SubmissionReason | string(1000)? |
| SubmissionIdempotencyKey | string(150)? |
| SubmissionPayloadHash | string(64)? |

Unique `BatchReference`, creation idempotency key and (when present) submission idempotency key, each scoped to Business. A batch is created only from wholly outstanding payments in an approved Payroll run. Submission requires the expected batch version, an idempotency key, an operator-supplied external submission reference and reason. Bank/mobile allocations additionally require a generated bank-file version before submission. Submission records an operator-attested handoff; it is not settlement evidence and does not replace reconciliation.

The submission transaction changes the batch and its Ready payments to Processing atomically. Same-key/same-payload retries return the persisted submission; changed payload, stale versions, and attempts to submit an ineligible batch conflict. Payment settlement/failure/return transitions recompute the parent batch status in the same audited transaction. Bank-file versions are generated only from frozen payment destination/allocation snapshots for an approved Payroll run; generation requires the dedicated bank-file permission and sensitive payment-data permission, is audited, versioned and idempotent, and does not create a separate Approval Kernel instance.

### `payroll_payments`

| Column | Type |
|---|---|
| Id | bigint PK |
| PayrollEmployeeResultId | bigint FK |
| PaymentBatchId | bigint? |
| Amount | decimal |
| Method | Cash/Bank/Mobile/etc. |
| Status | Pending/Ready/Processing/Held/Paid/Failed/Returned/Cancelled |
| ExternalReference | string(150)? |
| FailureReturnReason | string(1000)? |
| PaidAt | datetime? |
| HeldBy/At | metadata? |
| HoldReason | string(1000)? |
| ReleasedBy/At | metadata? |
| IdempotencyKey | string(150) |
| OriginalPaymentId | bigint? prior failed/returned attempt reference |
| AttemptNo | int |
| ResolutionStatus | None/PendingInvestigation/ResolvedNonSettlement/ResolvedReturn/ResolvedReissue |
| ResolutionEvidenceReference | string? private evidence/provider reconciliation reference |
| RowVersion | concurrency |

Unique `(BusinessId, IdempotencyKey)`. Reissue inserts a linked new attempt rather than overwriting the original status/evidence; resolution records identify why a previous Failed/Returned attempt is no longer an unresolved close blocker.

### `payroll_payment_allocations`

For mixed/partial payment:
PaymentId, AllocationMethod, AccountReference, Amount, ExternalReference.

### `payroll_payment_reconciliations`

Reconciliation evidence is an append-only allocation-level snapshot, not a mutable flag
on a payment or batch. Each correction creates the next version for that payment
allocation; reads and close assessment resolve the highest version. It never changes
payment amounts, settlement evidence, allocation state, or the Payroll result snapshot.

| Column | Type |
|---|---|
| Id | bigint PK |
| BusinessId | bigint FK |
| PayrollPaymentId | bigint FK |
| PayrollPaymentAllocationId | bigint FK |
| PaymentBatchId | bigint? FK |
| Version | bigint, allocation-scoped sequence |
| PaymentVersion | bigint, payment version observed |
| ExpectedAmount | decimal, frozen allocation amount |
| SettledAmount | decimal, independently reconciled amount |
| Difference | decimal, ExpectedAmount - SettledAmount |
| Status | Unreconciled/PartiallyReconciled/Reconciled/Exception |
| SettlementDate | date? |
| EvidenceSource | Cash/Bank/Mobile |
| ExternalReference | string(150)? |
| Notes | string(1000)? |
| IdempotencyKey | string(150) |
| PayloadHash | string(64) |
| ReconciledByUserId | user ref |
| ReconciledAtUtc | datetime |

Unique `(BusinessId, PaymentId, AllocationId, Version)`, idempotency identity per
payment allocation, and non-null external reference per payment allocation. The
expected amount and payment version preserve the exact state examined. A reported
settled amount cannot exceed verified payment settlement evidence or the frozen
allocation amount; a difference or unresolved payment exception requires a reason.
Bank/mobile evidence requires sensitive payment-data permission. The reconciliation
read is access-audited; mutation and central Audit v2 evidence commit in the same
transaction as the immutable snapshot.

---

## 6.5 Accounting Integration State

### `payroll_accounting_postings`

| Column | Type |
|---|---|
| Id | bigint PK |
| PayrollPeriodId | bigint? |
| PayrollRunId | bigint? |
| PaymentBatchId | bigint? |
| PostingType | PayrollExpense/SalaryPayment/Correction/FinalSettlement |
| IdempotencyKey | string(150) |
| BusinessId | bigint required |
| PostingReference | string(150), stable Accounting source identity |
| RunRevision | int?, frozen source revision |
| RequestPayloadJson | text, immutable canonical instruction, private/restricted |
| RequestPayloadHash | string(64), required SHA-256 |
| OriginalPostingId | bigint?, local link for correction |
| OriginalJournalReference | string/long?, Accounting-owned original reference |
| CorrectionReference | string(150)?, stable linked correction/reversal identity |
| AccountingReference | string/long? |
| Status | Pending/Processing/Posted/Failed/Conflict/Reversed |
| ErrorMessage | string(2000)? |
| PostedAtUtc | datetime? |
| AttemptCount / NextAttemptAtUtc / Version | retry/concurrency metadata |

Unique `(BusinessId, PostingReference)` and `(BusinessId, IdempotencyKey)`. Hash/payload become immutable before delivery. A conflicting hash is a terminal operator-visible conflict, not a successful return of an unrelated existing journal. Section 23 defines reconciliation after an uncertain response.

Never update Accounting tables directly.

---

## 6.6 Private Payslip / Export Artifacts

Proposed `payroll_artifact_versions`: Id, BusinessId, RunId/RunRevision, EmployeeResultId? (required for employee payslip), ArtifactType, VersionNo, SnapshotId/Hash, PaymentInfoSnapshotHash?, TemplateVersion, Format, StorageKey, ContentHash, ContentType, ByteLength, Classification, ScanStatus, CreatedBy/AtUtc, ExpiresAtUtc?, RetainUntilUtc?, RevokedAtUtc?, ReplacesVersionId?.

- unique `(BusinessId, RunId, RunRevision, EmployeeResultId, ArtifactType, VersionNo)` with explicit partial-index rules for run-wide versus employee artifacts;
- produce only from approved immutable run results; changes in payment-information presentation create a new artifact version, not overwrite an old approved payslip;
- use Section 5.3.1 private adapter, re-check employee/result tenant scope and HR/Admin access on download/print/email; audit creation/access/export;
- download returns a permission-controlled stream, not StorageKey; template/data hashes make regeneration traceable;
- bank-file generation is approved/versioned and idempotent; generation is not evidence of successful bank payment;
- exports obey field permissions, formula-safe CSV/Excel output and audited row count/hash. Retain confidential payloads and access grants only under approved policy.

### Phase 6E implementation boundary

- The current API supports Payslip generation/reuse, metadata read, private download,
  print streaming and idempotent email-request recording for an approved protected
  Payroll run and its persisted employee result. It renders persisted Payroll totals
  and result-line snapshots; it does not calculate Payroll or expose storage keys.
- Artifact metadata and delivery evidence use the single `RetailPOSDbContext`.
  Artifact identity includes the frozen Payroll result hash, payment-presentation
  hash and template version. Changed presentation inputs create a successor artifact;
  existing artifact bytes and metadata are not overwritten.
- Generation, download, print and delivery outcome are covered by required Audit v2
  transactions. HTTP responses are private/no-store where they contain artifact
  bytes. Download, print, email, Payslip view, salary view and payment view remain
  separately permission-checked, in addition to Business and employee operational
  location scope.
- New Payroll calculation snapshots freeze effective position code/name and the
  location projection name/source reference/type used for Payslip presentation.
  Payslip rendering reads only this integrity-checked snapshot; legacy snapshots
  without presentation labels retain the stable location-ID fallback and are never
  backfilled from mutable current HR/location masters.
- Private storage, encryption/key management, clean-content scanning and production
  email transport use fail-closed adapters. Until ADR-03 configuration and the
  approved protected email-delivery policy are supplied, production artifact/delivery
  acceptance is blocked; no public storage URL or unprotected attachment fallback is
  permitted. Position and friendly historical location-name presentation are not
  claimed unless frozen source evidence is available.

---

# 7. Exit, Clearance and Final Settlement

### `hr_exit_cases`

EmployeeId, ExitType, InitiatedDate, ProposedLastWorkingDate, ActualLastWorkingDate, Reason, Status, ApprovalInstanceId, InitiatedBy/At.

Status:
`Draft → PendingApproval → Approved → ClearanceInProgress → HRCompleted → Completed/Cancelled`.

### `hr_clearance_templates`
Business/location/location-type scoped template.

### `hr_clearance_template_items`
TemplateId, Sequence, Code, Name, ResponsibleRole/Reference, IsMandatory.

### `hr_clearance_items`
ExitCaseId, TemplateItemId, Status Pending/Cleared/Exception/NotApplicable, ClearedBy/At, Comment, EvidenceReference.

### `payroll_final_settlements`

Optional; only when Payroll enabled.

EmployeeId, ExitCaseId, Status Draft/PendingApproval/Approved/Posted/Paid/Completed, GrossDue, TotalDeductions, NetSettlement, SnapshotJson, AccountingReference.

### `payroll_final_settlement_lines`
Component code/name, type, amount, source.

---

# 8. Import / Opening Data

### `hr_import_batches`

| Column | Type |
|---|---|
| Id | bigint PK |
| BusinessId | ref |
| ImportType | Employee/Assignment/Salary/LeaveOpening/LoanOpening/etc. |
| TemplateVersion | string |
| FileStorageKey | string |
| IdempotencyKey | string? |
| Status | Uploaded/Validated/ValidationFailed/Ready/Processing/Completed/PartiallyCompleted/Failed |
| IsDryRun | bool |
| TotalRows | int |
| ValidRows | int |
| InvalidRows | int |
| CreatedBy/At | metadata |
| ExecutedBy/At | metadata? |

### `hr_import_rows`

ImportBatchId, RowNo, ExternalKey, RawJson, ValidationStatus, ErrorJson, CreatedEntityType, CreatedEntityId.

Do not import by direct raw SQL inserts. Execute application use cases or dedicated domain-safe batch services.

## Phase 1I implementation contract

- Only `Employee` and `Assignment` opening imports are implemented. Position and
  org unit IDs resolve existing masters; this slice does not create those masters
  or import salary, leave, loans, payroll or attendance.
- Format: bounded, strict UTF-8 CSV, template version `1`, exact ordered headers,
  ISO `yyyy-MM-dd` dates and positive Business-owned reference IDs. CSV quoting,
  escaped quotes and embedded line breaks are supported. No XLSX dependency or
  generic import framework is introduced.
- Employee header:
  `ExternalKey,EmployeeCode,FirstName,DisplayName,EmploymentTypeId,EmploymentStatus,JoiningDate,PositionId,OrgUnitId,LocationProjectionId,AssignmentType,NationalId,Mobile`.
  Employment status is `Active` or `Probation`. Position/org are supplied together;
  optional location assignments are `Primary` or `Additional`.
- Assignment header:
  `ExternalKey,EmployeeCode,LocationProjectionId,AssignmentType,EffectiveFrom`.
  One opening assignment per employee per batch is supported. Primary conflicts
  require the existing transfer workflow, not an import overwrite.
- `ExternalKey` is a non-confidential caller reference, unique within the file.
  Business + stable idempotency key identifies a batch; content SHA-256, import
  type and template must match on replay. Completed retries reuse recorded IDs.
- Upload -> mandatory dry-run -> `Ready` -> atomic execution -> `Completed`.
  Invalid rows produce `ValidationFailed`. Execution conflicts roll back **all**
  rows and append `Failed` evidence; retry requires another successful dry-run.
  If mandatory audit cannot persist, neither business mutations nor their outcome
  are committed. PostgreSQL advisory locks serialize Business imports; normal
  employee/assignment writes still use existing unique/concurrency/domain rules.
- Dry-run persists only batch/row evidence. Execution revalidates references and
  current permissions, then reuses employee creation (including initial employment
  period/lifecycle), position-history and location-assignment stores.
- Rows are append-only revisions, unique by Business/batch/revision/row number.
  `RawJson` is `{}`; confidential parsed values are not copied into row evidence.
  Invalid overlong external references are bounded to the schema length and retain
  their validation error. Previous revisions remain preserved.
- HTTP routes under `/api/hr/v1/opening-imports`: `GET template`, multipart `POST`,
  `GET {id}`, `POST {id}/dry-run`, `POST {id}/execute`. Summaries contain row errors
  and created IDs, never private storage/encryption references. Confidential
  responses use `private, no-store` and `nosniff`.
- Authorization rechecks HR.Enabled, authenticated Business/user, import action,
  employee creation, sensitive/identity fields, position and operational-location
  actions. Assignment import also requires current employee location access.
  Scoped callers cannot create unassigned/Additional-only employees; Additional
  assignments never grant access. Scoped batch readers must own the batch and
  still have current employee/source location access; if retained source is
  unavailable or expired, that scoped read fails closed.
- Private source uses the Phase 0F interfaces: actual CSV parsing/inspection, clean
  scan, SHA-256, encryption and provider-owned staging. Failed metadata/audit commit
  attempts owned-stage deletion; unsuccessful cleanup is surfaced, not hidden.
  Blob/database writes are **not** atomic. Operators must reconcile owned orphans
  against both document versions and import batch references before deletion.
- Production file acceptance remains ADR-03 blocked: provider/root/ACL, encryption
  keys, scanner, file/ciphertext limits and import row/retention policy must be
  explicitly approved/composed. No default provider or scan/encryption bypass is
  registered. Deterministic encryption/scanner/blob adapters exist only in tests.

---

# 9. New Shared Durable Outbox / Inbox / RabbitMQ Foundation

**Current state:** `ISaleEventPublisher`/logging publisher is non-durable/in-process; there is no executable RabbitMQ client, transactional outbox, dispatcher or inbox consumer. Everything in this section is **new shared Phase 0 infrastructure**, not an existing dependency. Implement in the current projects and deployment; do not introduce a microservice, general event-sourcing framework or broad conversion of existing Sales workflows.

Application owns narrow envelope/append/consume contracts; Infrastructure owns EF persistence, leases, transport and workers; API composes configuration and hosted services. Existing generic job scheduling is not assumed.

## 9.1 Minimal Shared Persistence and Envelope

Proposed `integration_outbox_messages`: Id/MessageId (UUID unique), BusinessId required, Producer, MessageType, SchemaVersion, AggregateType/Id, SourceEntityType/Id?, SourceVersion?, CorrelationId, CausationId?, OccurredAtUtc, PayloadJson, PayloadHash, Status (Pending/Leased/Published/DeadLetter), AttemptCount, NextAttemptAtUtc, LeaseOwner?, LeaseExpiresAtUtc?, PublishedAtUtc?, LastErrorCode?, Version.

Proposed `integration_inbox_messages`: BusinessId, ConsumerName, MessageId, MessageType, SchemaVersion, PayloadHash, ProcessedAtUtc, EffectReference?, SafeResultJson?.

- unique `(BusinessId, ConsumerName, MessageId)`; indexed pending/next-attempt outbox and source/business checkpoints;
- use existing context, snake_case, additive migration; no HR-private duplicate delivery tables;
- payload Business must match trusted envelope/source; no arbitrary caller-selected tenant;
- message schema versions are explicit and validated; unsupported types/versions are poison failures;
- payload includes only required integration data; no document bytes, credentials or unnecessary salary/bank fields.

## 9.2 Source Transaction and Publisher

Append the outbox record in the **same local database transaction** as the source mutation and required Audit v2. Never publish RabbitMQ inside a business transaction as a substitute for outbox persistence. Atomicity must be tested by induced commit/outbox/audit failure.

Worker claims bounded batches with transactional row locking/lease and reclaim after expiry; parallel dispatchers must not double-claim live work. Publish to configured durable exchange/queues with persistent messages, publisher confirms and mandatory/unroutable detection. Mark Published only after positive broker confirmation and routing success. A crash after publish/before mark causes redelivery; consumer idempotency is mandatory.

## 9.3 Inbox and Consumer Transaction

Authenticate/validate source and resolve Business explicitly, initialize actor/audit context, validate payload/hash/version, then acquire/insert inbox identity under its unique constraint. Apply projection/business effect plus inbox success plus required audit/outbox response in one local transaction. Ack only after commit.

Duplicate MessageId/same hash resolves the prior committed effect; duplicate identity/different hash is conflict/quarantined, never ignored. Inbox insertion without effect commit must roll back. Projection consumers reject/no-op stale source versions without losing newer state and persist deletion tombstones.

For async Payroll → Accounting, the Accounting-owned Application use case retains Section 23 stable posting/hash checks in addition to delivery inbox identity. Re-enveloping a retry with a new MessageId must not duplicate a journal.

Delivery guarantee is **at-least-once transport with idempotent/effectively-once business effects**, not exactly-once broker delivery.

## 9.4 Retry, Failure and Operations

Bounded exponential backoff with jitter; lease timeout and attempt ceilings configured. Transient network/broker failures retry; validation/hash/unsupported-schema poison messages go to durable dead-letter/quarantine with safe diagnostic metadata. Consumer exceptions never ack success.

Provide backlog age/depth, attempt counts, dead-letter counts, broker health, alerts and audited authorized replay using the original MessageId/hash. Replay cannot silently change payload/Business; corrected content requires a new causally linked event. Retention cannot delete pending messages or inbox identities still inside the redelivery horizon; protect payload storage and external broker credentials.

Phase 0 recovery tests: DB rollback, broker down/unroutable/negative confirm, crash before/after confirmation, duplicate concurrent consume, inbox rollback, out-of-order source updates, poison/replay, tenant mismatch and lease takeover. Outlet/Warehouse bootstrap + event sync + reconciliation all remain required; Factory events remain gated.

---

# 10. Aggregate Boundaries and Domain Invariants

## 10.1 Employee Aggregate

Root: `Employee`.

Owns or coordinates:
- employment state/current pointers;
- lifecycle transitions;
- user link association rules through application service;
- assignment requests through dedicated aggregates.

Key invariants:
- employee code unique per business;
- exited employee cannot receive new active operational assignments except via rehire;
- rehire creates a new employment period;
- no destructive lifecycle-history rewrite.

## 10.2 EmployeeLocationAssignment Aggregate

- only approved assignment becomes effective;
- one active primary assignment per employee at a date;
- transfer must close previous primary assignment when new one becomes effective;
- additional assignments do not automatically grant system permissions.

## 10.3 Approval Aggregate

- decisions follow sequence;
- rejected/cancelled instance cannot later be approved;
- duplicate decision is idempotent;
- self-approval follows policy;
- full step history immutable.

## 10.4 Attendance Aggregate

`RawAttendancePunch` is evidence, `DailyAttendance` is derived state.

- duplicate raw punch fingerprint rejected/marked duplicate;
- correction never deletes raw evidence;
- daily record derived from effective roster/shift/policy/location for that date;
- cross-midnight/extended shift remains one attendance record attributed to the shift start date;
- punch pairing must respect configured early/late windows and maximum shift duration;
- reprocessing increments processing version;
- approved manual correction wins according to explicit precedence and remains auditable.

## 10.5 Leave Aggregate

- approved request consumes ledger balance;
- rejected/cancelled request does not consume;
- negative balance only if policy allows;
- year close writes carry-forward/expiry once;
- closed leave year cannot mutate without reopen.

## 10.6 Overtime Aggregate

- calculation derives from attendance;
- payroll uses only Approved/PayrollEligible overtime;
- approval minutes cannot exceed allowed/calculated amount without explicit override permission and audit;
- rate/policy snapshot is retained.

## 10.7 Salary Version Aggregate

- only approved/scheduled/active version may affect payroll;
- effective periods cannot overlap ambiguously;
- maker cannot approve own request when policy requires separation;
- rejected/cancelled records remain historical;
- approved salary is corrected through new version, not direct overwrite.

## 10.8 Payroll Period Aggregate

Allowed state transitions:

```text
Open → Processing
Processing → Open | Locked
Locked → Processing (authorized reopen) | Approved
Approved → Locked (authorized correction) | Posted | Closed (Accounting disabled AND all close gates pass)
Posted → Closed (all close gates pass)
Closed → controlled Reopened correction workflow → new version/run; never ordinary edit
```

Every transition is validated and audited through Audit v2. `Posted` indicates required Accounting state, not payment settlement or permission to bypass close checks.

### 10.8.1 Close Gate Matrix

Close evaluates a frozen policy and current period/run/payment/posting state:

| Gate | Required baseline behavior |
|---|---|
| Approved results | Required runs approved, calculation snapshots frozen; no pending recalculation/approval/correction |
| Processing payment | Any Processing/uncertain external settlement attempt blocks; exception cannot pretend settlement is known |
| Failed/Returned | Unresolved Failed/Returned attempts block; resolve/reissue with ledger evidence first |
| Held | Blocks by default; only approved versioned close-with-held exception policy can permit outstanding hold |
| Unpaid/partial | Outstanding liability never silently disappears; default blocks unless explicit approved outstanding-exception policy permits |
| Reconciliation | Every paid payable result must have payment attempts covering its payable amount and each allocation fully reconciled to verified settlement evidence; missing/Unreconciled/PartiallyReconciled/Exception evidence blocks |
| Accounting enabled | All required expense, settled-payment and correction postings must be successfully Posted/resolved; Pending/Processing/Failed/Conflict blocks |
| Accounting disabled | Skip Accounting posting gates only; all payment/approval/reconciliation/audit gates remain |
| Finalization | Required payslip/result artifacts and policy-specific closing validations complete |

Persist module-owned close assessment/history: BusinessId, PeriodId, Version, PolicyVersion, RunRevisionRefs, Payment/PostStateVersions, GateResults, Exceptions, Actor, Reason, ApprovalReference?, AssessedAtUtc, ClosedAtUtc. This is workflow evidence, not a new audit system.

Any allowed outstanding exception requires `payroll.period.close_with_exception` in addition to close permission, an approved policy explicitly naming that gate, non-empty reason, required approval and central Audit v2. No implicit force-close or wildcard capability bypass. Uncertainty/failed Accounting when enabled cannot be converted into a silent exception.

Assess and commit under period lock/optimistic version plus affected aggregate concurrency checks; payment/reconcile/post/reopen mutations coordinate on the same period boundary to prevent starting a payment after checks but before close. Reject stale assessments with conflict. Audit failure must prevent close. Closed data is immutable through ordinary endpoints/jobs; only authorized reopen/new revision/correction/off-cycle preserves prior history and financial references.

## 10.9 Payroll Run Aggregate

- employee appears once per run;
- only approved/effective inputs;
- snapshot created with calculation;
- recalculation before approval replaces draft result under concurrency control;
- approved result cannot be silently recalculated;
- correction requires reopen/new run/version.

## 10.10 Payment Aggregate

- total successful/settled allocation cannot exceed employee payable;
- Held is outstanding;
- Paid cannot be retried;
- Failed/Returned may be reissued as a **new attempt** with a new idempotency key/reference only after confirmed non-settlement/returned funds and required ledger corrections;
- reconciliation never mutates payroll snapshot.

---

# 11. State Machines

## 11.1 Salary Setup / Revision

```text
Draft
 ├─> Cancelled
 └─> PendingApproval
       ├─> Rejected
       ├─> Cancelled
       └─> Approved
             ├─> Scheduled   (future effective date)
             └─> Active      (effective now)
Active/Scheduled
 └─> Superseded by a later approved version
```

## 11.2 Leave Request

```text
Draft → PendingApproval → Approved
                    ├──> Rejected
                    └──> Cancelled
Approved → Cancelled only through controlled reversal rules if business allows
```

## 11.3 Overtime

```text
Calculated → PendingApproval → Approved → PayrollEligible
                         └──> Rejected
```

## 11.4 Payroll Period

```text
Open → Processing → Locked → Approved → Posted → Closed
                                └──────────────→ Closed
```

Both arrows to Closed require every Section 10.8.1 gate. The direct Approved → Closed path applies only when Accounting is disabled. Reopen preserves the closed snapshot/history and initiates a controlled new correction revision.

## 11.5 Payroll Run

```text
Draft → PendingApproval → Approved → PaymentInProgress → Paid/Completed
  └──────────────> Cancelled (only before protected states)
```

## 11.6 Payment

```text
Pending → Ready → Processing → Paid
           └──> Held → Ready
                  Processing → Failed (confirmed failure, not uncertain)
                  Paid → Returned (confirmed return evidence)
Failed/Returned → new Ready attempt after authorized resolution/reissue
```

An uncertain timeout remains Processing/RequiresInvestigation until provider/bank evidence resolves it. Do not retry a possibly settled payment or overwrite a paid/returned attempt.

---

# 12. Application Layer / CQRS Design

## 12.1 Command Rules

Every command should have:
- immutable request DTO/record;
- validator;
- capability requirement where relevant;
- permission requirement;
- location/data-scope authorization;
- handler;
- transaction boundary owned by application/infrastructure;
- domain invariant calls;
- audit emission;
- idempotency/concurrency guard where relevant.

### 12.1.1 Existing Wiring and Explicit Concern Ownership

Use existing `ICommand`, `ICommand<T>` and `IQuery<T>` markers, MediatR handlers and FluentValidation scanning. Do not add an alternative mediator/generic repository/result framework. Existing global registration supplies **ValidationBehavior only**; authorization, transaction, audit and idempotency do not automatically run in a pipeline.

| Concern | Explicit implementation location |
|---|---|
| Authentication/action permission | Existing API policy from `PermissionCatalog`; handler Application authorization port for non-HTTP entry points |
| Business | Verified API Business middleware/current scope + `RequireBusinessId`; handler/store predicates and explicit worker Business |
| Capability | New module-aware resolver over existing entitlement storage, checked in handler/worker as well as endpoint |
| Employee/location/field scope | Narrow Application scope ports and filtered read/write stores, checked before mutation/data materialization |
| Validation | FluentValidation + Domain invariants; API maps validation consistently to 400 |
| Transaction | Proposed narrow HR transaction executor backed by the existing context; handler defines unit, Infrastructure executes it |
| Idempotency | Operation-specific unique Business/key + frozen hash and persisted outcome; same key/different request conflicts |
| Audit | Explicit Audit v2 write/flush before critical commit; named scope/interceptor not sufficient by itself |
| Outbox | Append in the transaction via shared Application port; dispatcher runs after commit |
| Concurrency | Existing-compatible PostgreSQL token/locks and tenant-scoped DB constraints |

Command sequence: resolve actor/Business → permission/capability/scope → validate → begin local transaction → resolve idempotency under unique key/lock → load/concurrency-check target → Domain operation → write narrow stores/approval/outbox/audit → flush required audit → commit → return recorded outcome. Never claim transaction protection merely because `SaveChanges` is called.

Queries/exports perform the same tenant/capability/action/field checks and confidential access audit where applicable. Controllers depend on Application contracts only; EF/persistence transaction types remain in Infrastructure, preserving existing architecture tests.

## 12.2 Phase 1 Commands

```text
SyncOperationalLocationProjectionCommand
ReconcileOperationalLocationProjectionsCommand

CreateOrgUnitCommand
UpdateOrgUnitCommand
CreatePositionCommand
UpdatePositionCommand
DeactivatePositionCommand

CreateEmployeeCommand
UpdateEmployeePersonalDetailsCommand
TransitionEmployeeLifecycleCommand
RehireEmployeeCommand

AssignEmployeePositionCommand
AssignEmployeeLocationCommand
RequestEmployeeTransferCommand
ApproveEmployeeTransferCommand
CancelEmployeeTransferCommand

LinkEmployeeUserCommand
UnlinkEmployeeUserCommand

CreateEmployeeDocumentCommand
UploadEmployeeDocumentVersionCommand
ReplaceEmployeeDocumentCommand
ArchiveEmployeeDocumentCommand

ValidateHrOpeningImportCommand
ExecuteHrOpeningImportCommand
```

## 12.3 Phase 2 Commands

```text
CreateShiftCommand
UpdateShiftCommand
PublishRosterCommand
BulkAssignRosterCommand
CreateHolidayCalendarCommand
CreateHolidayCommand
CreateWeeklyOffPolicyCommand

RegisterAttendanceDeviceCommand
UpdateAttendanceDeviceCommand
MapDeviceUserCommand
UnmapDeviceUserCommand
ReceiveDevicePunchCommand
ImportRawPunchCommand
ReprocessAttendanceCommand
CorrectAttendanceCommand
ApproveAttendanceCorrectionCommand
```

## 12.4 Phase 3 Commands

```text
CreateLeaveTypeCommand
CreateLeavePolicyVersionCommand
RequestLeaveCommand
ApproveLeaveStepCommand
RejectLeaveCommand
CancelLeaveCommand
AdjustLeaveBalanceCommand
CloseLeaveYearCommand
ReopenLeaveYearCommand

CreateOvertimePolicyVersionCommand
CalculateOvertimeCommand
SubmitOvertimeCommand
ApproveOvertimeStepCommand
RejectOvertimeCommand

InitiateEmployeeExitCommand
ApproveEmployeeExitCommand
CompleteClearanceItemCommand
CompleteNonFinancialExitCommand
```

## 12.5 Phase 4 Commands

```text
CreateSalaryComponentCommand
CreateSalaryStructureCommand
CreateInitialSalarySetupCommand
SubmitInitialSalarySetupCommand
ApproveInitialSalarySetupCommand
RejectInitialSalarySetupCommand
CancelInitialSalarySetupCommand

CreateSalaryRevisionCommand
SubmitSalaryRevisionCommand
ApproveSalaryRevisionCommand
RejectSalaryRevisionCommand
CancelSalaryRevisionCommand

CreateEmployeeLoanCommand
UpdateLoanRecoveryScheduleCommand
RecordManualLoanRepaymentCommand
```

## 12.6 Phase 5 Commands

```text
CreatePayrollPeriodCommand
StartPayrollProcessingCommand
CreatePayrollRunCommand
CalculatePayrollCommand
RecalculateDraftPayrollCommand
AddPayrollAdjustmentCommand
SubmitPayrollCommand
LockPayrollPeriodCommand
ApprovePayrollRunCommand
PostPayrollToAccountingCommand
ReopenPayrollCommand
ReopenPayrollPeriodCommand
ClosePayrollPeriodCommand
```

## 12.7 Phase 6 Commands

```text
CreatePaymentBatchCommand
GenerateBankPaymentFileCommand
RecordPayrollPaymentCommand
HoldPayrollPaymentCommand
ReleasePayrollPaymentCommand
MarkPayrollPaymentFailedCommand
MarkPayrollPaymentReturnedCommand
RetryPayrollPaymentCommand
ReconcilePayrollPaymentCommand
ReconcilePayrollPaymentBatchCommand
EmailPayslipCommand
```

## 12.8 Phase 7 Commands

```text
CreateFinalSettlementCommand
CalculateFinalSettlementCommand
SubmitFinalSettlementCommand
ApproveFinalSettlementCommand
PostFinalSettlementToAccountingCommand
RecordFinalSettlementPaymentCommand
CompleteFinalSettlementCommand
```

## 12.9 Queries

Queries must use read models/projections and `AsNoTracking`/provider equivalent where appropriate.

Representative queries:

```text
GetEmployeeByIdQuery
SearchEmployeesQuery
GetEmployeeLifecycleHistoryQuery
GetEmployeeAssignmentsQuery
GetEmployeeDocumentsQuery
GetEmployeeForCurrentUserQuery

GetRosterQuery
GetDailyAttendanceQuery
SearchAttendanceQuery
GetRawPunchesQuery
GetAttendanceExceptionsQuery

GetLeaveBalanceQuery
SearchLeaveRequestsQuery
GetOvertimeQuery

GetEmployeeCurrentSalaryQuery
GetEmployeeSalaryHistoryQuery
GetPayrollPeriodQuery
SearchPayrollRunsQuery
GetPayrollEmployeeResultQuery
GetPayrollRegisterQuery

GetPaymentBatchQuery
GetOutstandingPayrollPaymentsQuery
GetPaymentReconciliationQuery

GetExitCaseQuery
GetFinalSettlementQuery
```

---

# 13. REST API Design

## 13.1 General API Rules

Base routes:

```text
/api/hr/v1/*
/api/payroll/v1/*
```

Use current API versioning convention if different.

Response/error envelope must reuse existing solution conventions.

For commands that may be retried from UI/integration:
- define `Idempotency-Key` header or explicit request ID and persist Business-scoped identity/hash/outcome in the use case; no global idempotency middleware currently exists to assume;
- return conflict/validation response rather than duplicate side effects.

Use pagination for all potentially unbounded lists.

Recommended common query fields:

```text
page
pageSize
search
sortBy
sortDirection
locationId
locationTypeId
status
fromDate
toDate
```

Never trust UI filtering for authorization.

---

## 13.2 Organisation / Operational Location APIs

Operational locations are source-owned and read-only from HR.

| Method | Route | Purpose | Permission |
|---|---|---|---|
| GET | `/api/hr/v1/operational-locations` | search HR projection | `hr.location.view` |
| GET | `/api/hr/v1/operational-locations/{id}` | projection details | `hr.location.view` |
| POST | `/api/hr/v1/operational-locations/reconcile` | privileged repair/reconciliation | `hr.location.manage` or dedicated integration permission |
| GET | `/api/hr/v1/org-units` | hierarchy/search | `hr.organisation.view` |
| POST | `/api/hr/v1/org-units` | create | `hr.organisation.manage` |
| PUT | `/api/hr/v1/org-units/{id}` | update | `hr.organisation.manage` |
| GET | `/api/hr/v1/positions` | search | `hr.position.view` |
| POST | `/api/hr/v1/positions` | create | `hr.position.manage` |
| PUT | `/api/hr/v1/positions/{id}` | update | `hr.position.manage` |
| POST | `/api/hr/v1/positions/{id}/deactivate` | deactivate | `hr.position.manage` |

Do not expose HR create/update/delete endpoints for source-owned Outlet/Factory master data.

---

## 13.3 Employee APIs

| Method | Route | Purpose | Permission |
|---|---|---|---|
| GET | `/api/hr/v1/employees` | paged employee search | `hr.employee.view` |
| GET | `/api/hr/v1/employees/{id}` | profile | `hr.employee.view` |
| POST | `/api/hr/v1/employees` | create | `hr.employee.create` |
| PUT | `/api/hr/v1/employees/{id}` | edit permitted fields | `hr.employee.edit` |
| POST | `/api/hr/v1/employees/{id}/deactivate` | deactivate | `hr.employee.deactivate` |
| GET | `/api/hr/v1/employees/{id}/lifecycle` | history | `hr.employee_lifecycle.view` |
| POST | `/api/hr/v1/employees/{id}/lifecycle` | transition | `hr.employee_lifecycle.manage` |
| POST | `/api/hr/v1/employees/{id}/rehire` | rehire | `hr.employee_lifecycle.rehire` |
| GET | `/api/hr/v1/employees/{id}/locations` | location history | `hr.employee_transfer.view` |
| POST | `/api/hr/v1/employees/{id}/location-assignments` | assign/request transfer | `hr.employee_transfer.create` |
| POST | `/api/hr/v1/employee-location-assignments/{id}/approve` | approve | `hr.employee_transfer.approve` |
| POST | `/api/hr/v1/employee-location-assignments/{id}/cancel` | cancel | appropriate permission |
| GET | `/api/hr/v1/employees/{id}/positions` | assignment history | `hr.employee.view` |

Sensitive fields should be omitted/null/masked unless field-level permission is satisfied.

---

## 13.4 Employee ↔ Login User APIs

| Method | Route | Permission |
|---|---|---|
| GET | `/api/hr/v1/employees/{id}/user-link` | `hr.employee.view` |
| POST | `/api/hr/v1/employees/{id}/user-link` | `hr.employee.link_user` |
| DELETE | `/api/hr/v1/employees/{id}/user-link` | `hr.employee.unlink_user` |
| GET | `/api/hr/v1/me/employee` | authenticated user context |

Example link request:

```json
{
  "identityUserId": "348",
  "reason": "Employee requires HR manager access context"
}
```

Application handler must validate user existence through `IIdentityUserDirectory`; do not query Identity tables from Domain.

---

## 13.5 Document APIs

```text
GET    /api/hr/v1/employees/{employeeId}/documents
POST   /api/hr/v1/employees/{employeeId}/documents
POST   /api/hr/v1/employee-documents/{id}/versions
GET    /api/hr/v1/employee-documents/{id}/download
POST   /api/hr/v1/employee-documents/{id}/archive
```

Download uses authorization and secure storage abstraction, not public storage key exposure.

---

## 13.6 Approval APIs

If a central approval API already exists, reuse it.

Logical routes:

```text
GET  /api/hr/v1/approvals/inbox
GET  /api/hr/v1/approvals/{instanceId}
POST /api/hr/v1/approvals/{instanceId}/approve
POST /api/hr/v1/approvals/{instanceId}/reject
POST /api/hr/v1/approvals/{instanceId}/cancel
GET  /api/hr/v1/approval-policies
POST /api/hr/v1/approval-policies
PUT  /api/hr/v1/approval-policies/{id}
```

Generic approval endpoint still requires process-specific authorization validation.

---

## 13.7 Shift / Roster / Holiday APIs

```text
GET/POST/PUT   /api/hr/v1/shifts
GET            /api/hr/v1/rosters
POST           /api/hr/v1/rosters
POST           /api/hr/v1/rosters/bulk
POST           /api/hr/v1/rosters/{id}/publish

GET/POST/PUT   /api/hr/v1/holiday-calendars
POST           /api/hr/v1/holiday-calendars/{id}/holidays

GET/POST/PUT   /api/hr/v1/weekly-off-policies
POST           /api/hr/v1/employees/{id}/weekly-off-overrides
```

---

## 13.8 Attendance Device APIs

Administrative APIs:

```text
GET    /api/hr/v1/attendance/devices
POST   /api/hr/v1/attendance/devices
PUT    /api/hr/v1/attendance/devices/{id}
POST   /api/hr/v1/attendance/devices/{id}/disable
GET    /api/hr/v1/attendance/devices/{id}/mappings
POST   /api/hr/v1/attendance/devices/{id}/mappings
DELETE /api/hr/v1/attendance/device-user-mappings/{id}
```

Internal normalized punch ingress:

```text
POST /api/hr/v1/integrations/attendance/punches
```

This endpoint is for trusted adapter/internal integration and must be authenticated appropriately.

**Vendor push endpoint:** the ZKTeco/other vendor adapter may require vendor-defined paths/protocol semantics. Implement those in Infrastructure/Integration endpoints according to the exact device protocol/firmware documentation. Do not pretend a vendor ADMS device sends the normalized REST JSON below.

Normalized internal DTO:

```json
{
  "vendor": "ZKTeco",
  "deviceSerial": "IM-UTTARA-01",
  "deviceUserId": "23",
  "punchTimeLocal": "2026-10-03T09:59:12",
  "verificationMethod": "Face",
  "directionCode": "IN",
  "rawReference": "optional"
}
```

---

## 13.9 Attendance APIs

```text
GET  /api/hr/v1/attendance/daily
GET  /api/hr/v1/attendance/daily/{id}
GET  /api/hr/v1/attendance/raw-punches
POST /api/hr/v1/attendance/reprocess
POST /api/hr/v1/attendance/{id}/corrections
POST /api/hr/v1/attendance/corrections/{id}/submit
POST /api/hr/v1/attendance/corrections/{id}/approve
POST /api/hr/v1/attendance/corrections/{id}/reject
```

Raw punch endpoint requires elevated permission.

---

## 13.10 Leave APIs

```text
GET/POST/PUT /api/hr/v1/leave/types
GET/POST     /api/hr/v1/leave/policies
GET          /api/hr/v1/leave/balances
GET          /api/hr/v1/employees/{id}/leave-balances
POST         /api/hr/v1/leave/requests
GET          /api/hr/v1/leave/requests
GET          /api/hr/v1/leave/requests/{id}
POST         /api/hr/v1/leave/requests/{id}/submit
POST         /api/hr/v1/leave/requests/{id}/cancel
POST         /api/hr/v1/leave/balances/{id}/adjust
POST         /api/hr/v1/leave-years/{yearKey}/close
POST         /api/hr/v1/leave-years/{yearKey}/reopen
```

Approval itself may use central approval routes or process-specific façade endpoint.

---

## 13.11 Overtime APIs

```text
GET/POST /api/hr/v1/overtime/policies
GET      /api/hr/v1/overtime
POST     /api/hr/v1/overtime/calculate
POST     /api/hr/v1/overtime/{id}/submit
POST     /api/hr/v1/overtime/{id}/approve
POST     /api/hr/v1/overtime/{id}/reject
```

---

## 13.12 Exit / Clearance APIs

```text
POST /api/hr/v1/exits
GET  /api/hr/v1/exits
GET  /api/hr/v1/exits/{id}
POST /api/hr/v1/exits/{id}/submit
POST /api/hr/v1/exits/{id}/approve
GET  /api/hr/v1/exits/{id}/clearance
POST /api/hr/v1/exits/{id}/clearance/{itemId}/complete
POST /api/hr/v1/exits/{id}/complete-hr
```

---

## 13.13 Salary APIs

```text
GET    /api/payroll/v1/salary-components
POST   /api/payroll/v1/salary-components
PUT    /api/payroll/v1/salary-components/{id}

GET    /api/payroll/v1/employees/{employeeId}/salary
GET    /api/payroll/v1/employees/{employeeId}/salary-history

POST   /api/payroll/v1/employees/{employeeId}/salary-setups
POST   /api/payroll/v1/salary-setups/{id}/submit
POST   /api/payroll/v1/salary-setups/{id}/approve
POST   /api/payroll/v1/salary-setups/{id}/reject
POST   /api/payroll/v1/salary-setups/{id}/cancel

POST   /api/payroll/v1/employees/{employeeId}/salary-revisions
POST   /api/payroll/v1/salary-revisions/{id}/submit
POST   /api/payroll/v1/salary-revisions/{id}/approve
POST   /api/payroll/v1/salary-revisions/{id}/reject
POST   /api/payroll/v1/salary-revisions/{id}/cancel
```

All routes require `Payroll.Enabled`.

---

## 13.14 Loan / Advance APIs

```text
GET  /api/payroll/v1/loans
POST /api/payroll/v1/loans
GET  /api/payroll/v1/loans/{id}
POST /api/payroll/v1/loans/{id}/repayments
PUT  /api/payroll/v1/loans/{id}/schedule
```

---

## 13.15 Payroll Period / Run APIs

```text
GET  /api/payroll/v1/periods
POST /api/payroll/v1/periods
GET  /api/payroll/v1/periods/{id}
POST /api/payroll/v1/periods/{id}/start-processing
POST /api/payroll/v1/periods/{id}/lock
POST /api/payroll/v1/periods/{id}/reopen
POST /api/payroll/v1/periods/{id}/close

POST /api/payroll/v1/periods/{periodId}/runs
GET  /api/payroll/v1/runs
GET  /api/payroll/v1/runs/{id}
POST /api/payroll/v1/runs/{id}/calculate
POST /api/payroll/v1/runs/{id}/recalculate
POST /api/payroll/v1/runs/{id}/adjustments
POST /api/payroll/v1/runs/{id}/submit
POST /api/payroll/v1/runs/{id}/approve
POST /api/payroll/v1/runs/{id}/reopen
GET  /api/payroll/v1/runs/{id}/employees
GET  /api/payroll/v1/runs/{id}/employees/{employeeId}
```

Close request example:

```json
{
  "reason": "October 2026 payroll finalized",
  "expectedVersion": 7,
  "policyVersion": 2,
  "exceptions": []
}
```

The close command recomputes Section 10.8.1 gates inside the concurrency boundary; it never trusts client gate results. Return 409 with safe structured blocking gate codes/reference IDs when blockers/stale version exist; 403 when exception permission/scope is absent. Exception entries carry gate/target/reason/approved policy and approval reference; no `force: true` flag. A repeated successful identical close request returns the persisted close result; different content under the same key conflicts.

Reopen requires its distinct permission, reason and expected closed version. Return the new correction revision and linked prior snapshot/posting references; do not edit the old closed snapshot or silently release held/returned money.

---

## 13.16 Payment / Reconciliation APIs

```text
POST /api/payroll/v1/runs/{runId}/payment-batches
GET  /api/payroll/v1/payment-batches
GET  /api/payroll/v1/payment-batches/{id}
POST /api/payroll/v1/payment-batches/{id}/generate-bank-file
POST /api/payroll/v1/payment-batches/{id}/submit

POST /api/payroll/v1/payments
POST /api/payroll/v1/payments/{id}/hold
POST /api/payroll/v1/payments/{id}/release
POST /api/payroll/v1/payments/{id}/mark-failed
POST /api/payroll/v1/payments/{id}/mark-returned
POST /api/payroll/v1/payments/{id}/retry

POST /api/payroll/v1/payments/{id}/reconcile
POST /api/payroll/v1/payment-batches/{id}/reconcile
GET  /api/payroll/v1/payment-reconciliation
```

---

## 13.17 Payslip / Export APIs

```text
GET  /api/payroll/v1/runs/{runId}/payslips/{employeeId}
POST /api/payroll/v1/runs/{runId}/payslips/{employeeId}/email
GET  /api/payroll/v1/runs/{runId}/exports/excel
GET  /api/payroll/v1/runs/{runId}/exports/csv
GET  /api/payroll/v1/runs/{runId}/exports/pdf-summary
POST /api/payroll/v1/runs/{runId}/exports/bank-file
```

Downloads require explicit payslip view/download permission, verified Business/employee scope, Payroll capability and private access Audit v2. Print/email require their separate actions. HR/Admin-only initial access is enforced by approved role grants, not a hard-coded role-name check. Section 6.6 artifacts never expose a public storage URL; export formats must not bypass sensitive field checks.

---

## 13.18 Accounting APIs

Payroll-facing façade:

```text
POST /api/payroll/v1/runs/{runId}/post-accounting
GET  /api/payroll/v1/runs/{runId}/accounting-posting
POST /api/payroll/v1/payment-batches/{batchId}/post-accounting
```

These call `IAccountingIntegration`; controller/handler never writes Accounting tables.

---

## 13.19 Import APIs

```text
POST /api/hr/v1/imports
GET  /api/hr/v1/imports/{id}
GET  /api/hr/v1/imports/{id}/rows
POST /api/hr/v1/imports/{id}/validate
POST /api/hr/v1/imports/{id}/execute
GET  /api/hr/v1/imports/{id}/error-report
```

Payroll-sensitive imports still use payroll permissions/capability even if routed through shared import controller.

---

# 14. Representative DTO Contracts

## 14.1 Create Employee

```json
{
  "employeeCode": "EMP-00127",
  "firstName": "Rahim",
  "lastName": "Ahmed",
  "joiningDate": "2026-10-01",
  "employmentTypeId": 1,
  "positionId": 12,
  "primaryOperationalLocationId": 4,
  "reportingManagerEmployeeId": 18,
  "workEmail": "rahim@example.com",
  "mobile": "01XXXXXXXXX"
}
```

Handler should create employee + initial employment period + initial position/location assignments in one application transaction where consistent with approval rules.

## 14.2 Salary Revision

```json
{
  "effectiveFrom": "2027-01-01",
  "reason": "Annual salary review",
  "components": [
    { "componentId": 1, "amount": 30000.00 },
    { "componentId": 2, "amount": 15000.00 }
  ]
}
```

## 14.3 Payroll Calculation Request

```json
{
  "expectedPeriodVersion": 4,
  "employeeIds": null,
  "recalculate": false
}
```

`employeeIds = null` means all eligible employees within authorized run scope.

## 14.4 Attendance Correction

```json
{
  "adjustmentType": "ClockOut",
  "proposedValue": "2026-10-03T20:05:00",
  "reason": "Device was offline during employee exit punch"
}
```

---

# 15. Authorization Design

Authorization is evaluated in this order:

```text
Authenticated?
   ↓
Validated Business scope?
   ↓
Capability Enabled?
   ↓
Action Permission?
   ↓
Location / LocationType Scope?
   ↓
Field-Level Permission?
   ↓
Domain State Allows Action?
   ↓
Execute
```

## 15.1 Feature Guards

```text
HR.Enabled
Attendance.Enabled
DeviceIntegration.Enabled
Payroll.Enabled
```

Feature guard must exist in API/application layer; Angular hiding is secondary.

### 15.1.1 Business-Scoped Safe Defaults and Dependencies

Existing `FeatureEntitlementReadStore.IsEnabledAsync` returns true when a setting is missing; `IsFeatureEnabledQueryHandler` returns true when Business scope is absent. **Do not use those fallback results as proof that a new HR capability is enabled.** Do not change the legacy default behavior for Sales/Inventory/Accounting or unrelated features.

Use existing `BusinessFeatureSetting` persistence with a proposed narrow module capability resolver that distinguishes explicit settings from absence. Add a presence-aware read contract/adapter if needed; the current bool-only contract cannot prove explicit opt-in.

| Capability | Missing Business | Missing explicit setting | Dependency/enforcement |
|---|---|---|---|
| HR.Enabled | reject/deny | OFF until provisioned | HR entry points/jobs/private employee reads require explicit true |
| Payroll.Enabled | reject/deny | **OFF** | explicit Business opt-in AND HR enabled; wildcard/admin never substitutes |
| Attendance.Enabled | reject/deny | OFF until selected at HR onboarding | explicit true AND HR enabled |
| DeviceIntegration.Enabled | reject/deny | OFF | explicit true AND Attendance + HR enabled; manual attendance remains possible without device |

HR onboarding explicitly provisions HR/selected attendance flags; Payroll and device integration remain false unless authorized provisioned. Seeded false rows alone are insufficient: deletion/missing-row must still deny. Capability changes require approved feature-administration permission and Audit v2; employees/roles cannot enable their own capability.

Resolve Business from validated membership/current context, not merely `X-Business-Id`. Jobs/inbox consumers explicitly establish trusted Business and service/actor context; absent Business cannot mean all businesses. Apply checks to generic approvals, salary imports, private downloads, exports, scheduled activation, payroll retry and device ingestion, not just module controllers.

Disabling a capability retains data/history, stops new processing/access under that capability and surfaces an explicit disabled state. Queue work is paused/deferred with observable status, not consumed as success; privileged retention/remediation needs an explicit separately authorized operation. Business switch/entitlement change invalidates client/server caches. HR-only exit/clearance and leave/OT remain available with Payroll off; financial settlement is separately gated.

## 15.2 Location Scope

Application authorization service should expose equivalent methods:

```csharp
CanAccessOperationalLocation(locationProjectionId)
CanAccessAnyOperationalLocation(locationProjectionIds)
CanAccessSourceLocationType(sourceEntityType)
CanAccessEmployee(employeeId, effectiveDate?)
```

Do not derive all authorization automatically from Employee's own location link.

## 15.3 Field Security

Sensitive fields:
- NID/passport;
- personal address;
- bank/mobile payment account;
- salary;
- loan;
- tax;
- final settlement.

Response projection should omit or mask fields when caller lacks permission.

Protect queries, detail, search/sort, reports/export, import errors, private-file reads and Audit v2 views consistently. Add proposed field permissions `hr.employee.view_identity`, `payroll.payment.view_sensitive`, and explicit document/download/export permissions below. General employee view or `audit.view` alone must not reveal salary, identity numbers or payment account data. `hr.employee.view_sensitive` is broad restricted HR personal-data access, not an automatic salary grant.

Authorization uses User/Business membership and assigned permission/scope grants. Employee-user linkage and employee location assignment do not grant access. Reuse existing Outlet access port where it fits; generic projection/Factory scopes require a new explicit adapter after ownership decision. Never put a Factory ID into an OutletId field.

---

# 16. Menu and Permission Design

Menus/actions below are **functional UI concepts**, not database tables. The implementation is the existing code permission catalogue, API policy registration, JSON role grants, static Angular navigation, permission directives/guards and role-editor categories.

## 16.1 HR Menu

```text
HR
├── Dashboard
├── Employees
│   ├── Employee List
│   ├── Add Employee
│   ├── Employee Details
│   ├── Employee Lifecycle / History
│   ├── Employee Documents
│   ├── Employee ↔ Login User Link
│   └── Transfers / Location Assignments
├── Organisation
│   ├── Org Units
│   ├── Positions
│   ├── Operational Locations (read-only)
│   └── Location Policies / Scope
├── Attendance
│   ├── Daily Attendance
│   ├── Raw Punches
│   ├── Corrections
│   ├── Devices
│   ├── Device User Mapping
│   ├── Shifts
│   ├── Rosters
│   ├── Holidays
│   └── Weekly Off
├── Leave
│   ├── Requests
│   ├── Approvals
│   ├── Policies / Types
│   ├── Balances / Adjustments
│   └── Leave Year Closing
├── Overtime
├── Exit & Clearance
├── HR Reports
├── Data Import / Opening Data
└── HR Settings
```

## 16.2 Payroll Menu

Only when `Payroll.Enabled`.

```text
Payroll
├── Dashboard
├── Salary Setup
├── Salary Setup Approval
├── Salary Revisions / Approval
├── Allowances / Deductions
├── Advances / Loans
├── Payroll Periods / Close
├── Payroll Runs
├── Payroll Approval
├── Salary Payments
├── Payment Batches / Reconciliation
├── Payslips
├── Exports / Bank Files
├── Accounting Posting
├── Payroll Reports
└── Payroll Settings
```

`hr.location.manage` does **not** mean HR can edit source-owned Outlet/Factory masters. It covers HR-side policy/scope/reconciliation administration unless the existing authorization model deliberately maps it differently.

## 16.3 Stable Permission Baseline

New permission codes use lowercase dotted catalogue names with snake_case segments, consistent with existing-compatible naming; no new PascalCase naming scheme. Existing legacy code variants remain unchanged. The listed grants must be reviewed against Requirements v1.5 action separation before registration; UI visibility is not authorization.

```text
hr.dashboard.view
hr.employee.view
hr.employee.create
hr.employee.edit
hr.employee.deactivate
hr.employee.view_sensitive
hr.employee.view_identity
hr.employee.link_user
hr.employee.unlink_user
hr.employee_lifecycle.view
hr.employee_lifecycle.manage
hr.employee_lifecycle.rehire

hr.employee_document.view
hr.employee_document.download
hr.employee_document.upload
hr.employee_document.replace
hr.employee_document.delete

hr.organisation.view
hr.organisation.manage
hr.position.view
hr.position.manage
hr.location.view
hr.location.manage
hr.employee_transfer.view
hr.employee_transfer.create
hr.employee_transfer.approve

hr.shift.view
hr.shift.manage
hr.roster.view
hr.roster.manage
hr.holiday.view
hr.holiday.manage
hr.weekly_off.view
hr.weekly_off.manage

hr.attendance.view
hr.attendance.manual_entry
hr.attendance.correct
hr.attendance.approve_correction
hr.attendance.raw_punch.view
hr.attendance.reprocess
hr.device.view
hr.device.manage
hr.device_user_mapping.view
hr.device_user_mapping.manage

hr.leave.view
hr.leave.request
hr.leave.approve
hr.leave.admin
hr.leave_balance.view
hr.leave_balance.adjust
hr.leave_year.close
hr.leave_year.reopen

hr.overtime.view
hr.overtime.approve
hr.overtime.override
hr.overtime_policy.manage

hr.exit.view
hr.exit.initiate
hr.exit.approve
hr.exit.clearance
hr.import.view
hr.import.execute
hr.approval_policy.manage
hr.report.view
hr.settings.manage

payroll.dashboard.view
payroll.salary.view
payroll.salary.manage
payroll.salary_setup.create
payroll.salary_setup.submit
payroll.salary_setup.approve
payroll.salary_revision.create
payroll.salary_revision.submit
payroll.salary_revision.approve
payroll.allowance_deduction.view
payroll.allowance_deduction.manage
payroll.loan.view
payroll.loan.manage

payroll.period.view
payroll.period.lock
payroll.period.reopen
payroll.period.close
payroll.period.close_with_exception

payroll.run.view
payroll.run.create
payroll.run.calculate
payroll.run.edit_draft
payroll.run.submit
payroll.run.approve
payroll.run.reopen

payroll.payment.view
payroll.payment.view_sensitive
payroll.payment.process
payroll.payment.hold
payroll.payment.release
payroll.payment.retry
payroll.payment.reconcile

payroll.payslip.view
payroll.payslip.download
payroll.payslip.print
payroll.payslip.email
payroll.export
payroll.bank_file.generate
payroll.accounting.post
payroll.accounting.view_posting
payroll.import.view
payroll.import.execute
payroll.approval_policy.manage
payroll.report.view
payroll.settings.manage
```

## 16.4 Catalogue, Policies, Navigation and Role Upgrade Wiring

For every new route/action:

1. Extend Application `PermissionCatalog` with stable code/description/category; API registers policies from the catalogue and controllers apply the exact policy. Generic approval endpoints also enforce process-specific permission.
2. Extend role-form hard-coded categories as well as catalogue data; otherwise HR/Payroll permissions may exist but be unusable in the role editor.
3. Extend static `NAV_SECTIONS` and route/action permission configuration; `MenuService`, auth/permission guards and `appHasPermission` remain the shell integration. Add capability-aware filtering/guards (not currently assumed available).
4. Keep menu view/action grants separate. Payroll root/children require explicit Payroll capability and authorized action; route access must still fail if users paste a hidden URL.
5. Upgrade existing JSON role grants with a versioned, approved, **set-union** migration/application upgrade task keyed by stable role identity and grant-upgrade version. Deduplicate codes, preserve customized grants, use optimistic transaction safety, report malformed JSON/unknown roles for correction, and audit each actual grant change.
6. Roles are currently shared and have no BusinessId. Do not grant every shared role Payroll rights for a single Business. Define approved built-in role upgrade targets or new explicitly assigned HR/Payroll roles; Business capability/scope still applies. Existing additive user overrides cannot express deny; do not pretend employee/user override is a revocation mechanism.
7. Fresh-role defaults and existing-role upgrades are separate. `DbSeeder` early-returns when roles exist; changing fresh defaults alone is insufficient. Do not run overwrite-style seed scripts against customized production grants or rebuild Roles.
8. No Menu/Action/Permission/RolePermission tables or relational seed migrations are introduced. Do not use hard-coded role names as authorization; role naming/display-label differences are not reliable contracts.

---

# 17. Audit Design

## 17.1 Audit Envelope

Use existing central **Audit v2** (`AuditEvent`, `AuditEventEntity`, `AuditEventFieldChange`), scoped audit context and Application audit abstractions. Legacy `AuditLog` is not the new integration target. Do not add HR-specific audit tables/system.

Every critical operation must send equivalent information through an Application contract:

```text
ActorUserId
ActorEmployeeId?         // if linked and available
BusinessId
LocationId?
CorrelationId
ActionCode
EntityType
EntityId
Summary
Before
After
ChangedFields
Reason
OccurredAtUtc
SourceChannel
```

## 17.2 Explicit Audit Points

Mandatory:
- employee create/edit/deactivate;
- lifecycle transition/rehire;
- user link/unlink;
- location transfer;
- document upload/replace/archive;
- approval decision;
- raw punch reprocessing action;
- attendance correction;
- leave balance adjustment/year close/reopen;
- overtime override/approval;
- salary setup/revision decisions;
- loan changes;
- payroll calculate/recalculate/adjust/submit/approve/reopen;
- payroll period lock/reopen/close;
- payment hold/release/fail/return/retry/reconcile;
- exports/downloads containing sensitive payroll data;
- Accounting posting/reversal/correction;
- import validate/execute;
- exit/clearance/final settlement.

Passwords/tokens/secrets/raw credential payloads must never be included.

## 17.3 Transactional/Durable Critical Audit

The current interceptor recognizes `RetailPOSDbContext` and initialized audit context. It records after-save and catches/logs audit failures, so automatic interception alone does **not** guarantee a critical HR state change and its audit are atomic.

For lifecycle/user-link/transfer/approval/salary/leave-ledger/payroll lock/approval/close/reopen/payment/correction/grant changes, explicitly commit business state and Audit v2 in one local transaction and flush the named audit scope **before commit**. The existing scoped audit service can group changes; orchestration must prove failure rolls back the business transaction and avoids duplicate automatic/explicit events.

If a reviewed central-audit durable-delivery path is chosen instead, append its durable record in the same transaction and deliver into Audit v2 with idempotency; no best-effort fire-and-forget and no second HR audit destination. Phase 0 chooses/tests one atomic contract. Existing `IAuditEventCommandStore` request lacks full before/after field inputs: extend an Application-owned contract narrowly if needed rather than claim the example envelope already exists.

Initialize correlation, real/acting User and Role, Business, source and integration identity for jobs/consumers; generic system-job null Business is insufficient. Record generic operational projection ID/type in safe metadata; Outlet context is only for actual Outlet IDs.

## 17.4 Confidential Read, Access and Export Audit

Audit authorized and denied sensitive access as appropriate for salary, bank/mobile payment data, identity information, payslips, payroll exports and employee documents. Include detail/search/report reads, download/stream, print, email, bank-file generation and export. Persist the access attempt durably before releasing confidential content; if required access audit cannot be persisted, fail closed and expose an operational error.

Capture actor/acting role, verified Business/location scope, action/classification, entity/version/artifact references, approved purpose/reason where required, selected filter summary, record count, output hash and delivery/outcome/correlation. Do not put full salary statements, identity numbers, account numbers, file bytes or signed tokens into generic logs/audit summaries.

Current credential-oriented redaction is insufficient for HR. Define write redaction plus field-level ACLs/encryption; retain required private domain salary/document history with safe audit reference rather than erase approved business evidence. Restricted central audit detail may expose protected changed fields only to appropriately scoped sensitive readers.

Extend central Audit v2 list/detail/search/export authorization/projection for confidential HR events. Existing controller-wide `audit.view` and caller-optional Business filters are not enough. Enforce verified Business/data scope and classification permission before materialization; ID lookup must not bypass it. This is a necessary narrow central integration change, not a replacement audit subsystem.

Required tests cover audit writer failure rollback, job identity, duplicate suppression, sensitive response/export masking and cross-Business audit detail denial.

## 17.5 Phase 0E Implementation Contract

ADR-10's persistence choice is approved as **same-database transactional Audit v2 flush**. There is no new HR audit table, audit broker or second audit destination.

- Application `IRequiredAuditTransaction.ExecuteAsync` owns a relational transaction and a required named audit scope over the existing `RetailPOSDbContext`. Invoke it before staging changes, with a verified positive Business matching the initialized audit context and a non-empty correlation. HTTP actors require their real User identity; jobs initialize `AuditContext.ForBusinessSystemJob` with a stable job identifier and Business. Generic unscoped system jobs cannot use this boundary.
- The callback uses existing narrow stores. Pending writes are saved, explicit Application before/after evidence is accumulated with interceptor evidence, and central `AuditService.FlushScopeAsync` persists one grouped event before commit. Required-scope capture/serialization/persistence failures propagate and roll back business state. Cleanup detaches newly tracked rolled-back entities and reloads pre-existing tracked entities; cleanup failure is explicit. Nested transactions/scopes, early flush and changes to the scope are rejected.
- Explicit `IAuditEventCommandStore` evidence inside a required scope does not create a second event. This is grouping within one operation, **not** a substitute for command-owned request idempotency: future business handlers must resolve repeat-success requests inside this boundary before repeating effects.
- Central list/search/detail/export use the Application `IAuditReadAccessScope` port. HR/Payroll module names and confidential primary/affected entity types identify restricted events, including legacy `system` events and mixed events. Query fencing happens before materialization; confidential rows require the verified matching Business, effective location access and explicit capability/permission grants. An unscoped administrator or wildcard cannot bypass Business or capability. Unauthorized detail returns the same not-found result as an absent ID.
- The foundation intentionally uses a conservative event-level ACL: HR requires `audit.view`, `HR.Enabled`, identity/sensitive employee/document-view grants; Payroll requires `audit.view`, `Payroll.Enabled`, salary/payment-sensitive/payslip/export grants. Mixed events satisfy both groups. Non-owner/non-administrator readers are restricted to their effective Outlet; unlocated or future generic-location evidence is denied until its scoped adapter exists. ADR-08's later process/location policies may narrow these intersections without granting access to unclassified data. This is a documented fail-closed foundation adaptation, not a replacement for future business endpoint field/action permissions.
- Authorized confidential list/detail/export access and scoped denied ID attempts create durable central access evidence before return, including action, actor/Business/correlation, safe event reference, record count and projected-output hash. If this audit fails, content is not released. Unscoped readers receive no confidential rows; they cannot manufacture a Business-owned access record. Confidential exports do not also create the legacy best-effort export event.
- Confidential write/read projections redact non-allowlisted field values and all display snapshots, arbitrary metadata, summaries, HTTP paths and error text. Safe IDs/status/version/timestamps/count/hash remain; private salary/document history belongs in its protected domain storage, not generic audit payloads. Legacy already-stored raw confidential data is masked on read, without rewriting historical database rows. Identified system-job paths remain available for operational identity. Other modules retain existing behavior.
- For HR/Payroll and central audit routes, audit Business comes from the server-validated selection; real/acting identity comes from authenticated role-switch claims rather than caller-supplied acting headers. Legacy header behavior outside these routes is not changed by Phase 0E.

No EF model change or migration is required for this contract. New HR business handlers, approval workflows, private storage and later Phase 0 foundations remain outside this slice.

---

# 18. Attendance Processing Algorithm

For each employee/work date:

```text
1. Resolve effective employment period.
2. Resolve effective operational-location assignment for the work date.
3. Resolve published roster.
4. Resolve shift.
5. Set canonical WorkDate = shift start date.
6. Resolve holiday / weekly-off / roster precedence.
7. Resolve applicable attendance policy version.
8. Build punch-search window from shift start/end plus allowed early/late buffers.
9. For CrossesMidnight = true, search across the next calendar date.
10. Enforce MaximumAllowedWorkMinutes / maximum punch-pairing window.
11. Load mapped raw punches.
12. Deduplicate by immutable fingerprint.
13. Order by actual punch time.
14. Interpret first-in/last-out or policy-specific pairing.
15. Calculate worked minutes and paid/unpaid breaks.
16. Calculate late / early leave / missing punch.
17. Calculate OT-eligible minutes, but do not make OT payroll-payable yet.
18. Apply approved attendance adjustments.
19. Upsert one DailyAttendance row for the canonical WorkDate.
20. Create/update Overtime calculated record if eligible.
21. Validate minimum rest before next shift where configured.
22. Emit audit only for manual/administrative changes; automatic processing logs telemetry.
```

### Extended / Cross-Midnight Example

```text
Roster WorkDate: 15-Apr
Shift Start:     15-Apr 10:00
Shift End:       16-Apr 02:00
```

Expected:

```text
DailyAttendance.WorkDate = 15-Apr
FirstInAt                = 15-Apr 10:00
LastOutAt                = 16-Apr 02:00
Attendance rows          = 1
```

This is a general workforce rule, not an Eid-only exception.

Overtime is based on net worked time after break rules and scheduled payable time. Holiday/weekly-off/special-day rate resolution belongs in overtime policy.

Unmapped device punches remain raw with `Unmapped` status and are reprocessable after mapping.

---

# 19. Leave Calculation

Use ledger-based balance.

```text
Opening
+ Accrual
+ Manual Positive Adjustment
+ Carry Forward
- Approved Leave Usage
- Manual Negative Adjustment
- Expiry/Lapse
= Remaining
```

Do not directly set `Remaining` as an arbitrary master value except controlled opening/adjustment workflows.

On leave approval:
1. validate effective policy/version;
2. validate overlap;
3. validate balance/negative-balance rule;
4. create immutable ledger usage transaction;
5. update entitlement projection;
6. publish leave status for attendance/payroll consumption.

On cancellation/reversal, create reversing ledger transaction; do not delete original usage.

---

# 20. Overtime Calculation

```text
Gross eligible worked minutes
- scheduled required minutes
- excluded break minutes
- policy threshold
= calculated overtime minutes
```

Clamp eligible overtime at zero before approved bands/rounding. When Section 18 already supplies **net** eligible worked minutes after excluded breaks, do not subtract those breaks again; paid/unpaid break and scheduled payable-time treatment must come from the same frozen policy.

Then apply:
- normal day / weekly-off / public-holiday / special-day rate;
- optional policy-defined overtime slabs;
- employee override if authorized;
- policy rounding;
- approval.

Do not hard-code Eid-specific overtime logic. Eid and other special periods are expressed through holiday/special-day policy and roster/shift configuration.

Payroll uses `ApprovedMinutes` and approved rate snapshot only.

---

# 21. Payroll Calculation Engine

## 21.1 Precondition

Reject/block calculation when:
- Payroll disabled;
- period not Open/Processing;
- duplicate protected run;
- employee has no approved applicable salary;
- employee outside authorized run scope;
- required attendance data incomplete according to policy;
- blocking leave/OT approvals pending;
- invalid loan state;
- overlapping salary versions;
- invalid formula/circular dependency.

Warnings may be allowed for non-blocking issues.

## 21.2 Employee Calculation Sequence

For each eligible employee:

```text
1. Resolve employment state for period.
2. Resolve location/cost allocation for period.
3. Resolve approved salary version effective for dates.
4. Resolve fixed salary components.
5. Apply partial-month policy.
6. Load attendance summary.
7. Load approved paid/unpaid leave.
8. Load approved payroll-eligible overtime.
9. Load incentives/bonuses/manual earnings.
10. Calculate late/absence/unpaid-leave deductions.
11. Calculate loan/advance recovery without exceeding outstanding.
12. Calculate tax/statutory/manual deductions.
13. Apply deterministic component calculation order/formulas.
14. Apply configured rounding rules.
15. Calculate Gross Earnings.
16. Calculate Total Deductions.
17. Calculate Net Pay.
18. Validate non-negative/exception rules.
19. Persist normalized result lines.
20. Persist immutable input snapshot/hash.
```

Conceptual equation:

```text
Fixed Salary
+ Allowances
+ Approved Overtime
+ Incentives / Bonuses
+ Other Earnings
- Late / Absence / Unpaid Leave
- Loan / Advance Recovery
- Tax / Statutory
- Other Deductions
= Net Pay
```

## 21.3 Recalculation

Allowed only in Draft/unprotected state.
Recalculation:
- creates a new snapshot version or replaces draft snapshot according to existing project convention;
- never changes an Approved/Posted historical result silently.

---

# 22. Payroll Snapshot Contract

Snapshot content must be sufficient to reproduce the result without querying current master values.

Suggested logical JSON:

```json
{
  "employee": {
    "employeeId": 127,
    "employeeCode": "EMP-00127",
    "locationId": 4,
    "positionId": 12
  },
  "salary": {
    "salaryVersionId": 55,
    "components": []
  },
  "attendance": {
    "workingDays": 26,
    "presentDays": 24,
    "absentDays": 1,
    "unpaidLeaveDays": 1,
    "lateMinutes": 40
  },
  "overtime": [],
  "leave": [],
  "loans": [],
  "manualAdjustments": [],
  "policies": {
    "partialMonthPolicyVersion": "PM-3",
    "roundingPolicyVersion": "ROUND-1"
  },
  "calculation": {
    "currency": "BDT",
    "precision": 2,
    "roundingMode": "configured",
    "gross": 50000.00,
    "deductions": 2500.00,
    "net": 47500.00
  }
}
```

Actual shape should be versioned.

---

# 23. Accounting Integration Contract

### 23.0 Existing Contracts and Required Narrow Adaptation

Reuse Accounting Application `IManualJournalDraftStore.CreateOrGetSystemDraftAsync`, `PostAsync`, `ReverseAsync` and Accounting-owned posting account resolution. Infrastructure adapters translate Payroll instructions to those contracts; Payroll Domain/Application never references Accounting EF entities or writes journal tables.

Current system identity is `(BusinessId, JournalType, ReferenceType, ReferenceId)`. Current create-or-get can return an existing draft **without payload comparison**. Current reversal requires Posted and does not generally return the prior reversal when retried after success. These are explicit gaps to resolve with narrow Accounting-owned Application lookup/contract enhancements in Phase 0, not assumed guarantees.

### 23.0.1 Frozen Posting Instruction and Hash Contract

Before delivery persist a canonical versioned instruction in `payroll_accounting_postings`:

```text
schemaVersion, BusinessId, operation, source namespace/type/id,
run/result revision, PostingReference, original/correction references,
currency, posting date/business date, frozen resolved account IDs,
OutletId?/WarehouseId?/CostCenterId?, ordered lines with stable line IDs,
debit/credit amounts, approved rounding policy/version
```

Example stable identities: `payroll:run:123:revision:2:expense`, `payroll:payment:456:settlement:1`, `payroll:correction:789:reversal`. Month/user-provided display reference alone is not unique. Map this identity deterministically to Accounting journal type/reference type/reference ID/discriminator; freeze that mapping and enforce owning namespace.

Canonical serialization: explicitly ordered properties, UTF-8, invariant dates/currency, explicit null rules, decimal strings at defined accounting precision, line ordering by immutable line ID; hash SHA-256 over those bytes, independent of locale/dictionary order. Same reference and same canonical hash resumes/returns the same journal/outcome; same reference and different content returns **409 Conflict**, never unrelated journal success. Do not mutate instruction/accounts on retry.

Accounting's durable contract must atomically bind owned source identity and hash/payload to its journal or reconstruct/compare the actual frozen journal instruction through Accounting-owned lookup. A Payroll-only hash written after an external success is insufficient: a crash before local reference persistence must still detect conflicting reused reference. Validate the draft returned by current create-or-get before posting.

Ledger dates/open period/non-closed year and balanced same-Business active account validation stay Accounting-owned. Workers establish explicit Business context; ambient unvalidated/default scope is prohibited.

## 23.1 Payroll Expense Posting

Application builds an Accounting instruction, not an Accounting entity.

Example logical request:

```json
{
  "idempotencyKey": "PAYROLL:RUN:2026-10:1",
  "postingDate": "2026-10-31",
  "reference": "Payroll Oct 2026",
  "sourceType": "PayrollRun",
  "sourceId": "123",
  "lines": [
    { "accountCode": "SALARY_EXPENSE", "debit": 450000.00, "credit": 0 },
    { "accountCode": "OVERTIME_EXPENSE", "debit": 20000.00, "credit": 0 },
    { "accountCode": "SALARY_PAYABLE", "debit": 0, "credit": 450000.00 },
    { "accountCode": "TAX_PAYABLE", "debit": 0, "credit": 20000.00 }
  ]
}
```

This JSON is a readable logical example, not the full frozen wire contract. Freeze resolved account IDs and Section 23.0.1 fields before delivery. Exact COA identifiers come from Accounting-owned mapping/configuration.

Add Accounting-owned configuration for salary/OT/allowance expense, salary/statutory payable, employee loan/advance and cash/bank/mobile settlement slots. Existing configuration does not contain all Payroll slots. Existing dimensions support Outlet/Warehouse plus string CostCenterId; Factory/head-office mapping is an explicit decision, not an invented Accounting Factory FK.

Response:

```json
{
  "accountingReference": "JRN-000123",
  "posted": true
}
```

## 23.2 Payment Posting

```text
Employee Salary Payable   Dr
    Cash/Bank/Mobile          Cr
```

Idempotency key derives from the confirmed payment settlement/allocation identity, not request time. Do not expense salary twice: accrual and settlement are distinct balanced instructions. Partial/mixed payments produce allocations with stable accounting identities. Failed/unconfirmed attempts must not post a cash settlement as Paid.

## 23.3 Failure Handling

- persist immutable instruction/hash and explicit Pending/Processing/Posted/Failed/Conflict state;
- on timeout/uncertain response, use Accounting-owned lookup by exact Business/source identity, validate hash/content and resolve the existing draft/posted journal before retry;
- retry transient failures with the original identity/content; expose validation/hash/open-period conflicts for operator action, never auto-change date/accounts;
- enabled Accounting outage keeps required postings pending/failed and blocks close; disabled Accounting skips its posting gates, not other close gates;
- synchronous in-process Application contract is the lowest-risk initial adapter. If asynchronous delivery is chosen, use Section 9 outbox/inbox and response/reconciliation state; never the legacy publisher;
- return/resume repeat success without duplicate journals or side effects.

## 23.4 Reversal, Correction and Reopen Contract

Each correction has stable Business-scoped CorrectionReference and links OriginalPostingId/OriginalJournalReference, affected run revision and reason/approval. Accounting owns reversal/adjustment creation and returns its linked journal identity.

Repeat-success rule: if the original was already reversed by **this correction**, the same request/hash resolves the existing linked reversal and returns success. If it was reversed by a different correction, or content differs, return conflict. Lookup of prior success precedes invoking the legacy Posted-only reversal; concurrent attempts must resolve one winner to the same outcome, including response-loss retries.

Payroll reopen does not undo settled bank/mobile/cash payments, delete approved snapshots, unpost journals or mutate an original Paid attempt. Reopen creates a new revision/run and reapproval. Approved Accounting correction policy selects differential adjustment or linked reversal-and-replacement of expense accrual; settlement correction occurs only for verified returned/reallocated money. Replacement has its own stable identity and links original/reversal/correction. Do not blanket reverse all payments when a run is reopened.

Retain a chain of original journal → reversal/adjustment → replacement and original snapshot → correction snapshot. Reversed originals are not silently reposted. Required correction postings must complete before re-close. Final settlement follows the same frozen request/hash/linked correction contract.

Contract acceptance tests: repeated successful posting; same key/different lines/accounts/amount/date/Business conflict; crash after create/post before Payroll save; repeated successful reversal; concurrent correction; reversal by another correction; reopened salary with previously settled payments; disabled/enabled Accounting close behavior.

---

# 24. Attendance Device Adapter Contract

Application-level normalized port:

```csharp
public interface IAttendancePunchReceiver
{
    Task<ReceivePunchResult> ReceiveAsync(
        NormalizedAttendancePunch punch,
        CancellationToken cancellationToken);
}
```

Vendor adapter responsibilities:
- parse vendor protocol;
- authenticate/validate device;
- resolve device by serial;
- normalize timestamp/method/direction;
- preserve raw payload reference;
- call application port;
- return vendor-required protocol response.

Application/domain must not contain `ZKTeco` conditionals.

---

# 25. Background Jobs

No general reusable scheduler was found; the current application uses hosted-service patterns (for example subscription reminders). Add small scoped BackgroundService/worker adapters in the existing deployment, reusing DI/lifetime conventions. Do not assume a durable scheduler or add a broad scheduler platform as a prerequisite.

Every job has explicit Business iteration/validated scope, capability check, idempotent checkpoint/ledger key, cancellation, lease/concurrency, safe failure/retry and telemetry. Critical state changes use the same Application transaction/Audit v2 rules as HTTP commands. Cross-process messages use the new shared outbox/inbox; volatile timers are not delivery guarantees.

Recommended jobs:

| Job | Frequency/Trigger | Purpose |
|---|---|---|
| Operational location reconciliation | periodic/on-demand | detect/repair stale source projections |
| Attendance raw punch processor | near-real-time/queue | process Pending punches |
| Attendance reprocess retry | periodic | retry Failed/Unmapped after fix |
| Device health monitor | periodic | mark stale/offline, last seen |
| Document expiry reminder | daily | upcoming expiry notifications |
| Probation/contract reminder | daily | HR reminders |
| Leave accrual | scheduled according to policy | ledger accrual |
| Scheduled salary activation | daily or on-demand | Approved future salary → Active |
| Approval reminder/escalation | periodic | pending approval notification |
| Outbox dispatcher | frequent | publish integration events |
| Payroll notification | event-driven | pending approval/payment warnings |

Leave-year close and payroll close should remain explicit controlled commands, not blindly scheduled without business approval.

---

# 26. Angular / Front-End LLD

## 26.1 Standalone Feature Placement

```text
src/app/pages/hr/
  dashboard/
  organisation/
  operational-locations/
  positions/
  employees/
  employee-lifecycle/
  employee-documents/
  employee-user-link/
  shifts/
  rosters/
  holidays/
  attendance/
  devices/
  leave/
  overtime/
  exits/
  imports/
  reports/
  settings/

src/app/pages/payroll/
  dashboard/
  salary-components/
  salary-setup/
  salary-revisions/
  loans/
  periods/
  runs/
  approvals/
  payments/
  reconciliation/
  payslips/
  exports/
  accounting/
  final-settlement/
  reports/
  settings/
```

The current frontend uses standalone components and lazy routes; do not create a new NgModule feature shell. Follow existing `pages`, `services`, `models`, `guards`, `directives` and component conventions; organize HR/Payroll pages logically as above. Register routes in existing `app.routes.ts`, typed API models/services in existing folders, navigation in static `NAV_SECTIONS` and permission categories in the role form.

## 26.2 Route Guards

Every route:
1. feature/capability guard;
2. permission guard;
3. data scope remains backend-enforced.

Existing auth/permission guards and directive are reusable; module capability state/guard/filtering is new work. Resolve active validated Business capabilities on login/Business switch, default-deny unresolved loading/errors for HR/Payroll, invalidate stale cache and prevent URL access while disabled. Existing unrelated navigation/defaults remain unchanged.

Example:

```text
/payroll/runs
requires:
Payroll.Enabled
payroll.run.view
```

## 26.3 UI State Rules

- actions shown only when both permission and current domain state permit;
- API failure remains authoritative;
- sensitive salary/bank fields are not merely CSS-hidden—API projection must protect them;
- all lists paginated server-side;
- approval/reject/cancel prompts require reason when domain requires it;
- concurrency conflict should show refresh/retry message rather than overwrite.

---

# 27. Reports / Read Models

Use query-specific projections rather than loading aggregates.

Core HR read models:
- employee directory;
- headcount by location/location type/org unit;
- employee lifecycle;
- transfer history;
- expiring documents;
- daily attendance;
- missing punch;
- lateness/absence;
- leave balance/history;
- OT hours;
- exit/clearance.

Payroll read models:
- payroll register;
- payroll employee detail;
- component totals;
- deduction/earning analysis;
- payroll cost by location/location type;
- outstanding payment;
- payment exception;
- reconciliation;
- salary history;
- loan balances;
- final settlement.

All reports enforce capability, permission, location and field-level security.

---

# 28. Import Design

## 28.1 Template Versioning

Each import type has a versioned template, e.g.:

```text
EmployeeImport.v1
EmployeeAssignmentImport.v1
SalaryOpeningImport.v1
LeaveOpeningBalanceImport.v1
LoanOpeningImport.v1
```

## 28.2 Two-Step Execution

```text
Validate (dry-run)
   ↓
row errors/warnings
   ↓
Execute using validated batch/version
```

Execution should reject if source file/hash changed after validation unless revalidated.

## 28.3 Row Error Shape

```json
{
  "row": 17,
  "externalKey": "EMP-00127",
  "errors": [
    { "field": "PrimaryOperationalLocationReference", "code": "NOT_FOUND", "message": "Location code not found." }
  ]
}
```

---

# 29. Concurrency and Idempotency

Use both application and DB enforcement.

Critical protections:

```text
Operational location projection unique by Business/source system/type/id
EmployeeCode unique
One active EmployeeUserLink per employee/user
One active primary location assignment at a date
Raw punch fingerprint unique
Daily attendance unique by Employee + WorkDate
Leave ledger source idempotency
Salary version overlap prevention
One payroll employee result per Run + Employee
Payroll posting idempotency key unique
Payment idempotency key unique
Import stable external key/batch protection
Approval decision idempotency
Payroll period transition concurrency token
```

Expected stale row version returns HTTP 409 / existing conflict result model.

---

# 30. Error Handling

Reuse existing endpoint response conventions and error middleware without a new Result framework. Responses currently mix `ApiResponse<T>`, `{data,message}` and direct DTOs; select the compatible pattern for each new controller and describe it in OpenAPI.

Global middleware does not universally translate FluentValidation to 400. New controllers/endpoint adapter must explicitly map validation, permission/capability, not-found and conflict errors as current selected controllers do; do not assume an unregistered behavior handles them. Do not change unrelated old endpoints.

Suggested semantic mapping:

| Condition | HTTP |
|---|---:|
| validation error | 400 |
| unauthenticated | 401 |
| permission/capability/location denied | 403 |
| not found | 404 |
| duplicate/concurrency/state conflict | 409 |
| domain rule violation unsuitable for 409 under current convention | existing project rule |
| unexpected infrastructure error | 500 |

Domain/application exceptions should carry safe structured details for logs; never leak payroll/bank secrets to client responses.

---

# 31. Observability

Add structured logging/metrics for:

- device punches received/duplicate/unmapped/failed;
- attendance processing latency;
- pending approval counts;
- payroll calculation duration;
- payroll calculation failures;
- payroll close failures;
- accounting posting retries/failures;
- payment reconciliation exceptions;
- import row failures;
- outbox backlog.

Include CorrelationId, BusinessId and safe aggregate identifiers. Avoid sensitive salary/bank values in general logs.

---

# 32. Migration and Seed Plan

## 32.1 Migration Rules

Each slice that changes persistence:
1. update entities/configurations;
2. generate one intentional migration;
3. inspect migration;
4. verify no unrelated schema drift;
5. apply to clean/test DB;
6. run integration tests;
7. confirm model snapshot is current.

Never create placeholder/empty migrations just to mark a slice.

Use the existing context and snapshot; migration IDs extend the preserved baseline. Validate both clean install and upgrade from a populated baseline test database, all Business fences/unique constraints and concurrency mappings. Do not perform destructive regeneration, parallel HrPayrollDbContext migrations or HR-specific startup DDL. Audit/delivery/config additions are narrowly owned shared migrations where needed.

## 32.2 Seed Rules

Provision only approved business configuration (employment types, capability rows, HR policies/approval templates and Accounting-owned mappings). Code permissions, API policies, role-editor categories and static navigation are code changes, **not relational permission/menu seeds**.

Existing JSON role upgrades follow Section 16.4: approved built-in targets, versioned set-union, duplicate-safe, customized grants preserved, shared-role scope reviewed and changes audited. Fresh install defaults do not replace existing-role upgrades. Add no role grants silently to arbitrary customized roles.

## 32.3 No Parallel Identity/Menu System

Current Identity User/Role and JSON grants remain authoritative. There are no relational Menu/Action/Permission/RolePermission tables to seed. Extend `PermissionCatalog`, catalogue-derived API policies, static navigation/categories and controlled grants; integrate Audit v2 rather than legacy AuditLog. Preserve existing permission overrides/role switching, but maker-checker uses real User identity.

---

# 33. Testing Strategy

## 33.1 Domain Unit Tests

Must include:
- assignment overlap;
- employee lifecycle transitions;
- rehire;
- approval sequence/self-approval;
- attendance late/absent calculations;
- cross-midnight shift;
- leave accrual and closing;
- leave negative balance;
- overtime rate/rounding;
- salary effective-date overlap;
- maker-checker;
- loan max recovery;
- payroll formula order;
- rounding edge cases;
- payroll period transitions;
- payment state transitions.

## 33.2 Application Tests

For each command:
- validation;
- capability disabled;
- permission denied;
- location denied;
- happy path;
- invalid state;
- duplicate/idempotent retry;
- audit emitted;
- required Audit v2 failure rolls back critical state or leaves committed durable central delivery;
- confidential access audit persisted before data release;
- expected integration call.

## 33.3 Integration Tests

- EF mapping/constraints;
- migrations;
- unique indexes;
- API routes/OpenAPI;
- catalogue/API policies/static navigation/role-editor categories and duplicate-safe customized JSON grant upgrade;
- HR-only mode;
- payroll-enabled mode;
- raw punch ingestion;
- outbox/inbox;
- Accounting fake adapter;
- concurrency conflicts.

### 33.3.1 Architecture-Fit Regression Matrix

Use existing unit/integration/Angular fixtures; PostgreSQL tests verify provider-specific locks/indexes/xmin rather than assuming an in-memory substitute proves them.

| Area | Minimum verification |
|---|---|
| Persistence | Preserved baseline + additive migrations; clean install/populated-baseline upgrade/model snapshot; no unrelated schema drift |
| Capability | All four flags missing/false/true/dependency combinations, missing/foreign Business, wildcard/role switch, workers, cache switch; unrelated legacy defaults unchanged |
| Scope/permissions | API/handler/query/export and private/audit ID reads cross-Business denial; location/field separation; route paste and hidden action enforcement |
| Grants/navigation | Fresh defaults/existing customized shared Roles, repeated set-union upgrade, malformed grants explicit error, editor categories/nav visibility; no table seeding |
| Approval | Every supported process; multi-approver decision history, target hash, same real maker under switched roles, concurrent duplicate/conflicting decision |
| Audit/storage | Critical rollback/durable delivery, private staged orphan/scan/revocation/expiry, salary/bank/identity/payslip/doc/export masking and audit |
| Locations/delivery | Bootstrap race/high-water mark, out-of-order/duplicate/deletion tombstone, complete reconciliation, tenant mismatch, broker/confirm/crash/inbox/replay recovery |
| Attendance | Individual weekly-offs; holiday scope precedence; 10AM→2AM single row; break/max-duration/punch window/rest and generic special-day rates |
| Accounting | Frozen hash equality/conflict; uncertain response lookup; repeated reversal/correction; concurrent correction; reopened run with settled payments |
| Close | Processing, unresolved Failed/Returned, default Held, outstanding, required partial reconciliation, enabled pending posting all block; disabled Accounting skips only its gates; explicit exception permission/reason/approval/audit and close/payment race |

Frontend tests cover standalone lazy routes, Business/capability invalidation, role category/action gates, safe fields and confidential download errors. No test may use successful-shaped fallbacks for audit/delivery/accounting failures.

## 33.4 End-to-End Acceptance Chains

HR-only:

```text
Sync/verify Operational Location Projection
→ Create Position
→ Create Employee
→ Assign Location
→ Create Shift/Roster
→ Receive Punches
→ Process Attendance
→ Request/Approve Leave
→ Approve OT
→ Exit/Clearance
```

Payroll:

```text
Approve Salary
→ Open Payroll Period
→ Calculate Run
→ Lock Period
→ Approve Run
→ Post Accounting
→ Create/Process Payment
→ Reconcile
→ Close Period
```

---

# 34. Phase-by-Phase Implementation Slices

## Phase 0 — Architecture/Foundation Gate

This gate precedes HR business implementation. Work stays inside the four current projects; all infrastructure below is additive, scoped and reusable.

| Slice | Deliverable and exit evidence |
|---|---|
| 0A — Persistence/architecture | Existing context/baseline preserved; snake_case/prefix mappings and forward migration/model test strategy confirmed; Domain namespace/architecture tests retained |
| 0B — Source ownership | Factory ownership/source ADR approved or formally blocked with owner/date; no Factory events/policies until resolved; Outlet/Warehouse bootstrap/version/tombstone contracts agreed |
| 0C — Capabilities | Business-scoped presence-aware resolver, new flags default OFF, Payroll explicit opt-in, missing scope/row denial tests; unrelated legacy defaults unchanged |
| 0D — Permissions/navigation | Code catalogue/API policy/static navigation/category wiring and controlled JSON role upgrade strategy approved; customized/shared role protection tests |
| 0E — Audit v2 | Critical local transactional/durable audit contract verified; job context, HR confidential redaction/central read/export scope and access-audit design validated |
| 0F — Private storage | Provider/retention/scanning/key/access ADR; private adapter and version/quarantine/download authorization tests; no public product behavior |
| 0G — Reliable delivery | New shared outbox/inbox/RabbitMQ persistence, dispatcher/consumer and recovery/retry/dead-letter tests; deployable credentials/queues/monitoring plan |
| 0H — Accounting contract | Frozen identity/payload/hash equality and repeat-success reversal/lookup contract; account/dimension/correction mapping decisions and adapter contract tests |
| 0I — Gate acceptance | Dependency/architecture review, security/scope review, migration/model validation and required builds/tests; documented unresolved prerequisite ownership |

Do not interpret a completed inspection as implemented foundation. Mark each item Designed/Implemented/Verified/Blocked with evidence. Factory functionality cannot ship while source ownership is Blocked. A staged Outlet-only delivery may proceed only through an explicitly approved dependency waiver; it is not acceptance of the complete Factory requirement. Do not silently defer durable delivery or Accounting hash/reversal contract and claim Phase 0 complete.

### Phase 0 implementation checkpoints (2026-10-07)

These checkpoints record implementation evidence, not waivers of the exit criteria. The continuation request calls the reusable-foundation review **0H**, Accounting **0I**, and acceptance **0J**; these correspond to the Approval Kernel placement review and the original table's **0H/0I** respectively.

**0F - Implemented/Verified safe foundation; production acceptance Blocked (ADR-03).**

- `Application/ConfidentialFiles` provides versioned metadata/access/byte-validation/scanning/encryption ports and upload/read orchestration. Metadata implementations must use the existing-context required Audit v2 boundary.
- `Infrastructure/ConfidentialFiles/PrivateFileSystemBlobStore` is an unregistered ciphertext-only adapter with explicit private-root/public-root exclusion, Business-bound opaque keys, immutable creation and reparse-point checks. It is not a production provider decision.
- Pending/rejected/error scans cannot publish; expired/revoked versions cannot open. Read integrity is checked and durable access audit must commit before any content stream is returned. Approved plaintext/ciphertext bounds are mandatory; owned plaintext is cleared after audit failure and when the returned stream is disposed.
- Initial focused evidence: 6 unit and 36 integration/architecture tests passed, Release compilation and diff checks passed; acceptance hardening adds disposal coverage. No metadata schema, endpoint, public URL, permissive scanner/key implementation or DI activation exists.
- Provider, key management, scanner, OS ACLs, metadata concurrency, quarantine retention, orphan cleanup, legal hold, expiry and email policy remain ADR-03 prerequisites. Production storage is **not** accepted or activated.

**0G - Implemented/Verified reusable delivery components; production/live-broker acceptance Blocked (ADR-04).**

- Forward migration `20261007214321_Phase0DurableDelivery` adds shared snake_case outbox/inbox tables to `RetailPOSDbContext`; the baseline is unchanged.
- Enqueue requires the source's matching Business required Audit v2 transaction. Frozen identity/payload/envelope hashes include source version, correlation and causation; replay does not rewrite identity/content. Inbox effects, unique Business/consumer/message identity and Audit v2 commit together under a PostgreSQL advisory identity lock.
- Dispatch uses short `FOR UPDATE SKIP LOCKED` lease transactions, bounded attempts/jittered backoff, expired-lease recovery, conditional owner completion and persistent dead-letter state. Payload and envelope mutation is denied through the context. Raw integration payload/effect evidence is centrally redacted.
- `RabbitMqConfirmedPublisher` uses TLS, persistent messages, mandatory routing and tracked publisher confirms. The gated outbox worker is registered only with explicit valid `DurableDelivery` settings; `DurableDelivery:Retry` must cover the bounded batch confirm duration. Credentials belong in the deployment secret provider/environment, never source control.
- `RabbitMqInboxDelivery` is a consumer callback adapter, not an activated subscriber. It ACKs after the database boundary and requests poison/conflict dead-letter or transient retry. Activation **requires** the approved durable quorum queue/delivery-limit/dead-letter topology, subscriber lifecycle and trusted producer/type/version/Business resolver; no queue is silently declared and no consumer or location projection is registered.
- Operator replay requires an explicit authorization port and reason-bearing required audit scope. The composed authorization adapter currently denies replay pending ADR-04; no unsecured operator endpoint exists.
- Focused evidence: 5 unit and 25 integration/architecture/migration-chain tests passed, including real PostgreSQL concurrent inbox duplication, source rollback, leased recovery, hash conflict, retry ceilings and preserved replay identity. RabbitMQ contract tests use mocked channels/publishers: broker-down/unroutable/nack cases prove state handling, **not live broker behavior**.
- Live broker confirmations/unroutable delivery, consumer crash/ACK recovery, queue poison retention/replay, health/metrics/alerts and retention ownership remain acceptance gates. Factory/version/tombstone projection handlers remain blocked by ADR-01/02 and belong to the later agreed source integration slice. Delivery is at-least-once; no exactly-once broker claim is made. Existing volatile Sales publishing is unchanged.

**Reusable Approval Kernel review - Blocked/deferred to the existing Phase 1A placement.**

- This document explicitly places versioned policy/instance/step/decision persistence in Phase 1A. Current approvals remain module-specific; no compatible generic engine is available to adopt.
- ADR-08 approver scopes, quorum, self-approval, delegation and resolver contracts are unresolved. A speculative engine would start Phase 1 and invent those contracts. No Leave/Salary/OT feature or unrelated approval refactor is included in Phase 0.

**Accounting contract (continuation 0I / table 0H) - Implemented/Verified reusable contract; Payroll mapping acceptance Blocked (ADR-05/06).**

- Forward migration `20261007215441_Phase0FrozenAccountingContract` adds Accounting-owned immutable frozen operation evidence. `IFrozenSystemJournalStore` freezes canonical payload/hash before execution, serializes a Business/reference identity and commits the existing journal/number allocation, linked operation and central Audit v2 together.
- Same identity and canonical hash/content resolves the committed journal. Changed content/kind/original linkage conflicts. A successful reversal retry resolves its linked correction before the legacy Posted-only reversal check; a different correction identity cannot reverse an already corrected original again.
- Explicit correction dates prevent retry-time defaults from changing the request. Canonical decimal serialization ignores representation-only trailing zeros; line order, dates, dimensions, values and descriptions remain part of frozen content.
- The existing Accounting store gains opt-in `CreateNewSystemDraftAsync`; legacy `CreateOrGetSystemDraftAsync` behavior remains intact. A frozen caller cannot implicitly adopt an unrelated/unbound legacy journal, including a competing legacy reference creation.
- Focused evidence: 3 unit and 46 integration/Accounting regression/architecture/migration-chain tests passed; real PostgreSQL verifies concurrent same-identity resolution, changed-content conflict, linked repeat reversal, scoped denial and audit-failure rollback of journal, binding and sequence.
- No Payroll posting handler, COA allocation, payment journal flow, payroll reopen calculation or direct HR write to Accounting tables is implemented. ADR-05 selects slots/dimensions and synchronous/asynchronous composition; ADR-06 selects differential versus reversal/replacement and protects settled payments. Those are still production acceptance prerequisites.

**Gate acceptance (continuation 0J / table 0I) - Verification Completed; overall gate PARTIALLY PASSED.**

| Validation | Exact result |
|---|---|
| Final Release solution build | Passed; 0 warnings, 0 errors |
| Full UnitTests | 320 passed, 0 failed, 0 skipped |
| Full IntegrationTests | 1,140 passed, 0 failed, 0 skipped; includes existing Sales/Purchase/Inventory/Accounting regressions |
| Dedicated architecture rerun | 10 passed, 0 failed, 0 skipped; also included in the integration count |
| Full Angular suite | 100 passed in 20 files; 0 failed |
| Angular production build | Passed |
| Migration chain/model | Preserved baseline plus exactly the two intentional forward migrations above; isolated EF CLI reports no pending model changes; migration-chain test passed |
| Repository whitespace | Standard `git diff --check` passed; all 55 new Phase 0 files passed explicit untracked-file checks |
| Existing worktree preservation | Of 1,990 captured preimplementation files, 26 were intentionally changed by Phase 0, 1,964 remained byte-identical, 0 unexpectedly missing |
| Database hygiene | Read-only existing owned/inactive-test-database orphan inventory: 0; fixture-owned databases disposed, no broad cleanup |
| Protected files/index | Requirements v1.5 and baseline migration/designer SHA-256 unchanged; nothing staged or committed |

The extra whole-untracked-worktree whitespace inspection found six pre-existing files with trailing whitespace (including Markdown hard line breaks in the unchanged Requirements). Their captured hashes remain unchanged; these unrelated files were deliberately not reformatted.

Final controls: new capabilities remain Business-scoped/default OFF; no Factory, public HR document endpoint, second context, relational permission tables or Phase 1 feature exists. Audit v2 remains central. Durable database components and RabbitMQ adapter contracts are tested, but live broker/subscriber/operations acceptance is still Blocked; do not promote mock coverage into a production delivery claim.

**Ready for Phase 1: NO.** ADR-01/02 source ownership/lifecycle, ADR-03 production private-storage composition, ADR-04 live durable messaging/operations, ADR-05/06 Payroll Accounting mapping/correction and ADR-08 shared-role/approver decisions remain explicit gates. No dependency waiver is assumed. ADR-07 close/reconciliation exceptions and ADR-09 monetary rules remain required before their later business slices.

**Files added/updated during the continuation from 0E:** 34 files (26 added, 8 existing files updated). The following inventory is relative to this document.

0F additions:

```text
../../src/RetailPOS.Application/ConfidentialFiles/ConfidentialFileContracts.cs
../../src/RetailPOS.Application/ConfidentialFiles/ConfidentialFileService.cs
../../src/RetailPOS.Infrastructure/ConfidentialFiles/PrivateFileSystemBlobStore.cs
../../tests/RetailPOS.UnitTests/HR/ConfidentialFileServiceTests.cs
../../tests/RetailPOS.IntegrationTests/Audit/PrivateFileSystemBlobStoreTests.cs
```

0G additions:

```text
../../src/RetailPOS.Application/Integration/DurableDeliveryContracts.cs
../../src/RetailPOS.Domain/Entities/Integration/IntegrationDelivery.cs
../../src/RetailPOS.Infrastructure/Integration/IntegrationModelConfiguration.cs
../../src/RetailPOS.Infrastructure/Integration/IntegrationOutbox.cs
../../src/RetailPOS.Infrastructure/Integration/OutboxDispatcher.cs
../../src/RetailPOS.Infrastructure/Integration/IntegrationInbox.cs
../../src/RetailPOS.Infrastructure/Integration/RabbitMqDelivery.cs
../../src/RetailPOS.API/Services/IntegrationOutboxHostedService.cs
../../tests/RetailPOS.UnitTests/HR/IntegrationEnvelopeTests.cs
../../tests/RetailPOS.IntegrationTests/Audit/DurableDeliveryFoundationTests.cs
../../tests/RetailPOS.IntegrationTests/Audit/RabbitMqDeliveryContractTests.cs
../../src/RetailPOS.Infrastructure/Data/Migrations/20261007214321_Phase0DurableDelivery.cs
../../src/RetailPOS.Infrastructure/Data/Migrations/20261007214321_Phase0DurableDelivery.Designer.cs
```

Accounting additions:

```text
../../src/RetailPOS.Application/AccountingJournals/Abstractions/IFrozenSystemJournalStore.cs
../../src/RetailPOS.Domain/Entities/Accounting/FrozenSystemJournalOperation.cs
../../src/RetailPOS.Infrastructure/AccountingJournals/FrozenSystemJournalStore.cs
../../src/RetailPOS.Infrastructure/AccountingJournals/FrozenSystemJournalModelConfiguration.cs
../../tests/RetailPOS.UnitTests/HR/FrozenSystemJournalRequestTests.cs
../../tests/RetailPOS.IntegrationTests/Accounting/FrozenSystemJournalContractTests.cs
../../src/RetailPOS.Infrastructure/Data/Migrations/20261007215441_Phase0FrozenAccountingContract.cs
../../src/RetailPOS.Infrastructure/Data/Migrations/20261007215441_Phase0FrozenAccountingContract.Designer.cs
```

Updated shared files:

```text
./IraniMart_HR_Payroll_Low_Level_Design_v1.2.md
../../src/RetailPOS.API/Program.cs
../../src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs
../../src/RetailPOS.Infrastructure/Data/Migrations/RetailPOSDbContextModelSnapshot.cs
../../src/RetailPOS.Infrastructure/RetailPOS.Infrastructure.csproj
../../src/RetailPOS.Infrastructure/Audit/AuditFieldRedaction.cs
../../src/RetailPOS.Application/AccountingJournals/Abstractions/IManualJournalDraftStore.cs
../../src/RetailPOS.Infrastructure/AccountingJournals/ManualJournalDraftStore.cs
```

## Phase 1 — HR Foundation

### 1A — Module Boundary / HR Approval Kernel Base
- establish logical feature folders and narrow stores/authorization/transaction ports on verified Phase 0 facilities;
- implement versioned policy/instance/step/decision Kernel base and transfer approver resolver;
- no unrelated approval refactor; preserve current CQRS/dependency patterns.

### 1B — Operational Location Projection + Organisation
- wire agreed Outlet/Warehouse source change paths/contracts; Factory only after approved ownership prerequisite;
- add HR local operational-location projection;
- add idempotent consumer/inbox behavior;
- add bootstrap/reconciliation path;
- add org units;
- expose read-only location projection API;
- add catalogue policies/static navigation/categories, controlled grants and tests.

### 1C — Position Management
- positions/reporting hierarchy;
- operational-location projection scope;
- APIs/UI;
- code permission/policy/category/grant wiring;
- tests.

### 1D — Employee Master + Employment Period
- employee CRUD;
- employment type;
- initial employment period;
- sensitive field projection;
- tests.

### 1E — Employee Lifecycle / Position / Manager History
- lifecycle transitions;
- position assignment;
- rehire;
- history UI;
- audit.

### 1F — Employee ↔ Login User
- reuse existing Identity/User contract;
- link/unlink;
- duplicate constraints;
- audit;
- tests.

### 1G — Employee Operational Location Assignment / Transfer
- reference local HR projection only;
- primary/additional;
- approval;
- effective-date history;
- location authorization tests.

### 1H — Employee Documents
- document type;
- private staged/scanned storage adapter;
- versioning;
- expiry;
- authorized audited streaming/expiry/retention and permissions.

### 1I — Opening Employee Import
- import batch;
- dry-run;
- employee/assignment import using source operational-location reference;
- audit.

### 1J — Phase 1 Hardening
- static navigation/code catalogue/API policy/category and JSON grant upgrade verification;
- projection sync/reconciliation verification;
- audit review;
- API/OpenAPI;
- regression;
- release build.

#### Phase 1 acceptance checkpoint (2026-10-08)

Phase 1A-1J is implementation-accepted. This checkpoint does not approve production
storage, broker topology, role grants or approver mappings, and does not start Phase 2.

Acceptance defects corrected during hardening:
- Documents and Opening Import use the existing Angular API base (which already
  includes `/api`); neither appends a second `/api`.
- Opening Import has a static, `hr.enabled`/`hr.import.view`-gated navigation entry.
- Position-history GET requires `hr.position.view`; position-assignment POST requires
  its separate manage policy.
- Approval decision/cancel serialization precedes replay lookup. Replays remain
  bound to their instance and re-check current authorization; concurrent same-key
  PostgreSQL decisions create one decision and one audit event.
- Confidential document metadata reads persist central Audit v2 before returning
  data. Required-audit failure releases no metadata. Employee responses are
  `private, no-store` with `nosniff`.
- Document download UI uses `CurrentVersionId`, not the newest attempted version.
  Unapproved/revoked/archived versions are not offered; a rejected replacement
  does not change the published version's download filename.
- Legacy Npgsql Local timestamp reads are normalized before UTC journal-date
  truncation and before exposing retained import-source instants. This preserves
  month-boundary posting and import retention without changing the schema.
- Phase 1 PostgreSQL fixtures no longer ignore `PendingModelChangesWarning`.

Final acceptance evidence:
- Release solution build: zero warnings/errors.
- Full UnitTests: 370/370; full IntegrationTests: 1,246/1,246, zero skipped.
- Architecture: 10/10, included in the integration total.
- HR PostgreSQL provider cases: 42/42, including import (35), employee concurrency
  (2), Login User link (1), transfer/replay/activation (2), and document/model parity (2).
- Angular: 116/116 across 23 files; production build passed.
- Runtime/design-time/snapshot/latest-designer parity, empty/current PostgreSQL
  migration, EF pending-model check and full-chain idempotent SQL generation passed.
- Live API startup used Testing, no seeding/business jobs, and an ephemeral
  process-only JWT key; anonymous HR requests returned 401. Hosted API tests
  generated OpenAPI and verified all Phase 1 policies/routes and 401/403 fences.
- Baseline, prior migrations and snapshot remain byte-identical; no new migration,
  staged changes, unrelated working-tree edits or owned test-database leftovers.

ADR-03 private storage composition, ADR-04 live broker production topology and
ADR-08 production role/approver mappings remain separate deployment/configuration
blockers. Existing fail-closed behavior and deterministic test adapters are retained;
these blockers do not prevent starting Phase 2 implementation after explicit approval.

---

## Phase 2 — Attendance

### 2A Shift / Holiday / Weekly Off
- company/location-type/location-scoped holidays;
- versioned weekly-off policies;
- extended/cross-midnight shift support;
- break, punch buffer, max-duration and minimum-rest rules.

#### Phase 2A repository adaptations

- One `RetailPOSDbContext`; `hr_shifts`, `hr_shift_breaks`, `hr_holiday_calendars`, `hr_holidays`, `hr_weekly_off_policy_versions` and `hr_weekly_off_policy_days`. All configuration writes use the existing required Audit v2 transaction and Business-scoped PostgreSQL transaction advisory lock. Concurrency `Version` is separate from weekly-off `VersionNo`.
- Shift start/end are local `TimeOnly` values; calendar dates are `DateOnly`, not UTC instants. Duration-based ordered breaks implement the LLD's "Start/End or DurationMinutes" alternative. Paid/unpaid evidence is retained; unpaid minutes reduce available minimum net work. `GraceInMinutes` is the late-arrival threshold foundation. No overtime rates or formulas are added.
- The single-start-date/single-midnight model represents a positive duration below 24 hours. Early/late punch windows (including a configured maximum duration) cannot reach the next daily window. Canonical WorkDate remains the start date. `HrShiftRules.EnsureAssignable` rejects inactive definitions for later assignment consumers. These are definitions/contracts only: no attendance row or roster is created.
- User-confirmed history rule: holiday/calendar edits and new weekly-off effective dates are strictly after the current **UTC calendar date**. Historical correction workflows and Business-local cutoff configuration are not introduced in 2A. Effective holiday calendars cannot be rewritten; create a separate future calendar. Calendar scope is immutable even before it becomes effective.
- Weekly-off PUT/`POST /{id}/versions` appends a successor, closes the predecessor at the preceding date, and retains its original days/scope/start/version number. Same-scope effective periods may not overlap. `IsActive=false` denotes a closed configuration, **not** removal from historical resolution: the inclusive effective period still applies. Deactivation supplies a future inclusive end and cannot extend an existing end. Holiday deactivation is restricted to future holidays.
- Calendar `Name` and holiday `Code`/`Name`/classification/paid/notes evidence are exposed. Rates and `OvertimeRuleCode` are deferred to overtime policy. All matching holidays are returned in stable specificity/ID order; a single day classification prioritizes Holiday over Weekly Off while preserving both evidence sets. No benefits are counted here.
- Weekly-off precedence is Specific Location > Location Type > Company even when the more specific policy makes the queried day a normal working day. Reusable explicit Employee Override > Published Roster decisions are contract inputs only; no override or roster tables/endpoints/business logic are introduced.
- `HR.Enabled` and `Attendance.Enabled` are both required, with existing shift/holiday/weekly-off actions. Business/Location-Type writes and shift writes require Business-wide configuration access. Specific-location writes require the existing authorized-source scope and an active/current same-Business HR projection backed by an owned Outlet/Warehouse. Employee Additional assignments are not access grants. Reads filter specific-location rows to authorized sources.
- Factory is a supported future **type scope only**. A specific Factory source projection cannot be invented or configured in 2A.
- The reused scoped projection access/list queries use explicit `Enumerable.Contains`: .NET 10 array/span overload binding otherwise fails EF parameter evaluation. This is a directly coupled compatibility correction, not a change to the authorization rules.
- Angular has only Shift, Holiday and Weekly Off pages, capability-aware navigation/actions, versions/effective periods, validation errors and a date-resolution preview. The existing numeric enum JSON convention is retained for recurring day numbers (Sunday=0); no global serializer change is made. API examples are in `src/RetailPOS.API/Phase2A.http`.

#### Phase 2A acceptance checkpoint (2026-10-08)

- Release solution build: zero warnings/errors. UnitTests: **401/401** (31 new calendar/shift rule cases). Full IntegrationTests: **1281/1281**, zero skipped, including 35 new Phase 2A cases and 10 architecture cases. After the final inactive-shift assignment precondition addition, all 48 relevant Phase 2A/architecture/migration/provider cases were rerun successfully.
- Phase 2A PostgreSQL races: **5/5**, covering stale shift update, stale holiday update, concurrent weekly-off successor, concurrent overlapping first policy and duplicate shift code. Each case migrated an owned empty database through the full chain, reran `MigrateAsync` on the current database, and verified one audited winner. The full integration suite also passed the existing preserved-chain and runtime/design-time/snapshot/latest-designer parity tests.
- Angular: **123/123** across 24 test files, including 7 new calendar UI/request cases; production build passed. EF pending-model-changes check clean; complete idempotent migration SQL generated.
- One forward migration: `20261008194430_Phase2AShiftHolidayWeeklyOff`; all **26 prior migration/designer files byte-identical**, including the baseline. The current model snapshot changed intentionally. No prior migration regeneration, second DbContext, warning suppression, roster/attendance/device/leave/overtime/payroll implementation, staging or commit.
- Existing ADR-03 production private-storage composition, ADR-04 broker topology and ADR-08 approved grants/approver mappings remain separate deployment/configuration blockers; existing fail-closed controls are unchanged. Phase 2B implementation may begin only on an explicit later request.

### 2B Roster
- normal/extended/special roster assignment;

**Repository adaptation / acceptance (Phase 2B):**
- One active employee/WorkDate roster, with Draft/Published/Cancelled states. Bulk draft creation is atomic, capped at 100 distinct employee/date rows. Cancelled rows retain history; future replacement creates a new identity rather than rewriting publication evidence.
- Publication freezes shift/break values, operational-location identity and resolved calendar evidence. Explicit published-day decisions have precedence over holiday/weekly-off classification while retaining both underlying evidence sets. Drafts never feed classification. Employee-specific publication lookup is `/api/hr/v1/rosters/classification`; the existing location-only calendar lookup remains a calendar foundation.
- WorkDate is the local shift start date. Adjacent published roster punch windows and minimum-rest requirements are checked, including unlike shifts. Generic Normal/Extended/Special labels do not add festival/payroll behavior.
- Historical publication cannot be changed or cancelled. Current-date draft publication is permitted; published cancellation is future-only using UTC calendar dates. New assignment/publication requires active employee, effective employment/HR location assignment, active shift and authoritative same-Business source ownership.
- Identity location grants are enforced independently of employee assignments. Published evidence is immutable at SaveChanges; writes reuse RequiredAuditTransaction and the calendar Business advisory lock plus employee row lock. Stale versions/duplicate active dates become conflicts.
- Focused acceptance: **40 unit cases**, **79 integration cases** (including **3 new PostgreSQL roster races**, **10 architecture cases** and migration-chain validation), **5 Angular tests**. Release solution build has zero warnings/errors; pending-model changes clean; `git diff --check` clean. Existing prior migrations are retained; one forward migration `20261008205310_Phase2BRoster` adds the roster table.
- No device, punch ingestion, attendance processing or correction workflow is part of this acceptance.
- canonical WorkDate = shift start date;
- cross-midnight validation.
### 2C Device Master + Device User Mapping

**Repository adaptation / acceptance (Phase 2C):**
- Device metadata uses structured host/port fields rather than unrestricted connection JSON. Hostnames/IPs cannot contain paths, credentials or URI query strings. Credential configuration stores only an opaque external secret name; normal responses expose a boolean, not that name. Edits preserve it unless explicit replacement/clear is requested. Audit v2 redacts secret-reference fields. No external connection, credential resolution or punch ingress is activated in 2C.
- Vendor/serial identity is unique across the deployment; device code is unique per Business. Mapped device serial/vendor, protocol, timezone and location cannot be rewritten. Registration and mappings validate same-Business authoritative HR projection/source ownership; Factory sources remain deferred.
- Mapping intervals are half-open UTC `[EffectiveFromUtc, EffectiveToUtc)`. Device timezone selects the local employment/location date at mapping start. Open intervals have IsActive=true; bounded/closed intervals have IsActive=false but remain eligible historical evidence for their interval. All intervals, including closed ones, participate in overlap checks. This distinguishes history from current configuration state without erasing prior mappings.
- A mapping binds an employed employee to an effective location assignment; it grants no identity/application access. Mapping permissions are independent of device management. HR, Attendance and DeviceIntegration capabilities and identity-source location grants are required.
- Closure requires an optimistic version and an explicit instant now/future, after the mapping start. DELETE in the existing LLD route means audited history-preserving closure and returns the retained mapping (200), not destructive deletion. Mapping core fields/deletion are blocked at SaveChanges.
- One forward migration `20261008210207_Phase2CDevicesAndMappings` creates device/mapping tables with composite Business FKs and unique constraints. Calendar/roster/device source-ownership validation shares a narrow Infrastructure helper; calendar regression checks preserve the accepted 2A behavior.
- Focused acceptance: **53 unit cases** (13 new device cases), **102 integration cases** (23 new device cases: 12 persistence, 7 API, 4 PostgreSQL races), **10 Angular cases** across roster/device pages. Release solution build: zero warnings/errors; Angular production bundle passed; EF pending-model changes clean; empty/current full migration chain and idempotent SQL passed; `git diff --check` clean. All **28 prior migration/designer files** are byte-for-byte preserved.

### 2D1 Vendor-Neutral / Manual Raw Punch Intake

**Repository adaptation / acceptance (Phase 2D1):**
- The LLD raw-punch evidence model and a narrow `ITrustedNormalizedAttendancePunchIngestor` port are implemented without a vendor wire adapter. The controlled API currently accepts audited manual entries only; future trusted adapters submit normalized device events through the port. No protocol endpoint or public device authentication mechanism is implied by the normalized DTO.
- Raw punches retain Business, optional device and serial snapshot, device-user reference, resolved employee when available, wall-clock local time, UTC instant where resolvable, timezone, generic verification/direction codes, received time, stable SHA-256 fingerprint, source type and processing metadata. RawPayload is deliberately not accepted/stored; the safe normalized fields are sufficient. Database uniqueness and a Business transaction advisory lock make manual idempotency deterministic; repeated employee/time/source events return the original immutable evidence even if presentation codes differ.
- Manual punches require HR/Attendance capability, `hr.attendance.manual_entry`, a real actor, active employee/employment, effective Primary location, and identity-granted location scope. Manual rows cannot carry device identity. `hr.attendance.raw_punch.view` fences read APIs by Business/location; raw payload is never returned. Ambiguous/nonexistent manual wall times are rejected; unresolved normalized device identities are retained as Unmapped without inventing an employee.
- SaveChanges blocks deletion and changes to evidence fields; only processing state/error/version metadata is mutable. The normalized device port validates registered active device identity and captures its serial/timezone. No vendor-specific ACK, retry, direction-code or payload assumptions are added.
- Forward migration `20261009065745_Phase2DRawAttendancePunches` adds the raw evidence table, Business-composite FKs, fingerprint uniqueness and query indexes in RetailPOSDbContext.
- Focused acceptance: **3 unit tests**, **7 raw-punch integration/API/persistence/PostgreSQL tests**, plus **11 architecture/migration-chain cases**; Release build passed with zero warnings/errors; EF pending-model changes clean; `git diff --check` clean. Angular was unchanged.

### 2D2 Vendor-Specific Device Adapter — Deferred

Physical-terminal ADMS/PUSH work is deferred until the actual terminal is purchased. It requires the exact terminal model, firmware, vendor protocol documentation, device authentication semantics, and acknowledgement/retry/offline-delivery behavior. The future adapter must translate vendor payloads into the normalized raw-punch port without changing Attendance Processor logic. No vendor adapter has been implemented.

**Final Phase 2B/2C regression checkpoint:**
- Transactional roster/device writes recheck identity-source location scope after acquiring write locks, including employee Primary contexts. Draft reassignment locks both employees in deterministic order; publication/cancellation locks the employee. This prevents a pre-transaction authorization result from authorizing a concurrently relocated device/employee. Additional employee assignments still grant no configuration access.
- Roster WorkDate must permit next-calendar-day resolution; DateOnly.MaxValue is rejected before cross-midnight/adjacent-window arithmetic.
- Final Release build: **zero warnings/errors**. Full UnitTests: **424/424**; full IntegrationTests: **1331/1331**, including **126 PostgreSQL cases** (**54 HR**, **7 new roster/device races**), **10 architecture cases**, full migration-chain/provider/model-parity validation. No skipped backend tests. New slice coverage is **10 roster unit / 26 roster integration** and **13 device unit / 24 device integration** cases.
- Angular: **133/133** across **26 test files**, including **5 roster** and **5 device** cases; production build passed. EF pending-model-changes check remains clean. These results accept 2B/2C only, not punch ingestion, attendance processing, corrections or the complete attendance chain.

Phase 2E proceeds on the vendor-neutral normalized punch contract. The current repository has no standalone attendance-policy version aggregate: Phase 2E uses the immutable published shift snapshot as the applicable per-roster policy source for configured grace, break, minimum/maximum duration and punch-window values. Additional policy values remain deferred until specified; no payroll/overtime approval behavior is inferred. Phase 2F follows after 2E acceptance. Phase 2G remains excluded.

### 2E Attendance Processor

**Repository adaptation / implementation:**
- Daily attendance is derived only from Published roster assignments; Draft/Cancelled rosters do not produce rows. One Business/Employee/WorkDate row is unique, and WorkDate is the published shift-start date. Shift, location and calendar evidence are copied from the immutable publication snapshots rather than re-read from current configuration.
- `POST /api/hr/v1/attendance/process` and `GET /api/hr/v1/attendance/daily` use the existing attendance capability, permissions, identity-location scope, MediatR and central Audit v2 transaction. Processing takes a Business transaction lock; identical evidence is idempotent and a newly arrived in-window punch changes the calculation fingerprint and advances ProcessingVersion.
- Pairing uses normalized `IN`/`OUT` direction codes when present and chronological first/last pairing only when direction is not canonical. A single punch, unmatched direction, trailing unmatched `IN`, or pair beyond the configured maximum is retained as MissingPunch evidence, not treated as completed work. Punches with different timezone IDs in one candidate window fail explicitly rather than comparing incompatible local wall clocks; a single known timezone is snapshotted. Because current published location snapshots have no timezone configuration, shift comparisons use the preserved local wall-clock fields and do not infer a location timezone.
- Only complete pairs accrue work. Configured unpaid break durations are summed and deducted once, capped at elapsed pair time; paid breaks are not deducted. Grace-in/out determine LateMinutes and EarlyLeaveMinutes. A complete pair below the shift's configured MinimumWorkMinutes is HalfDay; no additional half-day threshold is invented. A roster with no punches is Absent, Holiday or WeeklyOff according to its frozen published classification; holiday and weekly-off evidence remains in CalendarEvidenceJson.
- Raw punch evidence is never rewritten by processing; only permitted processing status/error/version metadata changes when a punch is first consumed. OvertimeMinutesCalculated remains zero in this slice: shift eligibility is retained as snapshot data, but no overtime formula/approval workflow is inferred. Delayed punches are consumed by a subsequent process call. There is no roster or attendance rewrite operation here; corrections/reprocessing are Phase 2F.
- The 2E forward migration adds the derived `hr_daily_attendance` table, a unique Business/Employee/WorkDate key, concurrency version, indexes and same-Business references in the existing RetailPOSDbContext. No vendor adapter, Phase 2G report, or Phase 3 overtime logic is part of this slice.
- Phase 2E acceptance: **9/9 attendance calculation unit tests** (40/40 when run with existing calendar-rule tests), **30/30 focused attendance persistence/API/location-scope tests**, **1/1 PostgreSQL concurrent-processing race**, and **11/11 architecture plus migration-chain tests**. Release solution build passed with zero warnings/errors; EF pending-model changes clean; `git diff --check` clean. Migration: `20261009071917_Phase2EAttendanceProcessor`. Angular was unchanged.

### 2F Attendance Correction / Reprocess

#### Repository implementation contract

Manual attendance corrections are represented by immutable, Business-scoped correction requests submitted through the existing Approval Kernel using the `AttendanceCorrection` process type. A request captures the expected attendance processing version, request hash, reason, and serialized before/proposed evidence. Approval applies the proposed values transactionally and records applied-after evidence; rejection and cancellation retain the request evidence without changing the derived attendance row. Raw punch evidence is never modified by this workflow.

The API exposes correction history and submission below a daily attendance resource, plus approve/reject/cancel actions under the correction approval resource. Submission and cancellation require `hr.attendance.correct`; decisions require `hr.attendance.approve_correction`; history requires `hr.attendance.view`. Attendance remains capability-gated and location-authorized. Correction timestamps are local wall-clock values from the frozen attendance context, so the request contract intentionally preserves unspecified local `DateTime` values rather than interpreting them as UTC instants.

Reprocessing continues to consume delayed raw punches and increments `ProcessingVersion`. If a correction is pending and new evidence would change the derived row, reprocessing is rejected until the request is decided or cancelled. After approval, reprocessing may refresh evidence fingerprints, consumed punch IDs and processing metadata but cannot replace manually corrected attendance values. Stable correction/decision keys, optimistic processing-version checks, a Business-scoped PostgreSQL advisory lock, uniqueness constraints and transactional Audit v2 writes provide replay/concurrency/failure safety. These are persistence and workflow adaptations to the existing Approval Kernel and audit transaction infrastructure; no parallel correction-approval engine or raw-evidence rewrite is introduced.

Phase 2F does not add roster editing, overtime approval, reports, or other Phase 2G behavior. Vendor-specific device integration remains deferred under 2D2 and is not required by this correction workflow.

### 2G Core Attendance Reports + hardening

The reporting surface is a read-only paged query over derived DailyAttendance. It supports inclusive WorkDate range, employee, operational-location projection, status, exception-only, and page filters, with late/early-leave/absent/exception counts for the same authorized filter set. Each row returns only the employee code/name and attendance-relevant fields; it does not return identity, contact, document, raw payload, or correction-reason fields.

Report authorization requires `HR.Enabled`, `Attendance.Enabled`, and `hr.attendance.report`. Location access is derived from the caller's identity location grants and applied in the backend; employee additional-location assignments do not expand it. The query is Business-scoped and `AsNoTracking`. The Angular report page uses the same capability and permission gates and treats backend scope as authoritative. CSV/export is not part of this slice.

Rows use frozen shift, location and calendar evidence from attendance processing, link only the raw punches assigned to that one attendance row, and expose effective status, corrected indicator, correction count/latest state, ProcessingVersion and LastProcessedAtUtc. Holiday and weekly-off evidence can both be shown when they overlap; classification and counts remain deterministic and no payroll/overtime benefit is inferred. WorkDate remains the shift-start date, so 10:00 AM → 2:00 AM next day appears once under the first date and the next-day punch does not create a second row.

This implementation adds no reporting persistence or migration and introduces no Phase 3 feature. Vendor-specific 2D2 integration remains deferred until the physical terminal model, firmware, protocol, authentication, ACK/retry and offline behavior are documented.

---

## Phase 3 — Leave/OT/Exit and Complete HR-Only Release

### 3A HR/Payroll Approval Kernel leave/overtime/exit integration

#### 3A implementation checkpoint (2026-10-09)

The existing Approval Kernel is reused; no parallel approval framework or speculative production approver is introduced. Leave, Overtime and Exit use the existing process identifiers, Business-scoped policy lookup, frozen policy/candidates, real-user identity and self-approval checks, sequential/quorum decisions, immutable decision rows, idempotency-key replay/conflict handling, terminal reject/cancel, location access checks and transactional Audit v2.

The API access adapter now permits a concrete authorized operational-location scope for Leave, Overtime and Exit, in addition to the existing EmployeeTransfer and AttendanceCorrection cases. Business scope remains tenant-bound. HR capability and process-specific permissions remain mandatory. Payroll location scope is not enabled by this HR change. Candidate resolution remains fail-closed when production configuration is absent; workflow-specific target snapshot and state transitions are wired by the owning slices (3C, 3E and 3F). No schema migration is needed for this integration checkpoint.

### 3B Leave Types/Policy/Entitlement/Ledger

#### 3B implementation checkpoint (2026-10-09)

Leave types and immutable, effective-dated policy versions now feed employee leave-year entitlements and an append-only ledger. The ledger is the balance source of truth; entitlement rows snapshot the selected policy version and effective primary-location context. Every policy explicitly selects one supported accrual method (annual grant at leave-year start, monthly joining-date anniversary, or annual joining-date anniversary). Anniversary dates clamp to the last valid day of shorter months.

As confirmed for this implementation, policy versions take effect only at a leave-year boundary. Policy effective-from/end dates must align to those boundaries, and the leave-year calendar cannot change between versions at the same scope. Entitlement policy/location context is resolved at the leave-year start and remains its basis for the year, keeping the LLD's single PolicyVersionId per entitlement/leave-year model deterministic even after a mid-year location transfer. Manual opening/adjustment and accrual writes use transactional Audit v2, idempotency keys, immutable ledger evidence, Business/employee/location authorization and PostgreSQL employee-row serialization. The Angular leave page is HR-capability and permission gated. Phase 3C request/approval workflows are not included in this slice.

Validation: focused leave unit tests 7/7; leave persistence, API, and PostgreSQL tests 19/19; full Angular suite 141/141 across 28 files; Angular production build passed; Release solution build passed with zero warnings/errors; full PostgreSQL migration-chain test passed; EF pending-model-changes check clean; idempotent migration SQL generation passed. Migration `20261009112406_Phase3BLeavePolicyAndLedger` adds the leave schema.

### 3C Leave Request + Multi-Level Approval
### 3D Leave-Year Close/Reopen

#### 3D implementation checkpoint (2026-10-09)

Leave-year lifecycle is tracked per employee, leave type, and frozen entitlement year. The authorized flow is Open → ClosingReview → Closed, with an audited, reasoned Reopened event before any further writes. Review is rejected until the UTC calendar date is later than the frozen leave-year end; it accrues policy dues through that inclusive end date, processes already-due carry-forward expiry, and refuses to proceed while a leave request for that employee/type/year remains pending approval. Closing is a separate confirmation against the entitlement concurrency version.

Close calculations preserve a negative balance in its closed source year; negative balances are not carried into a successor year because no debt-transfer rule is defined. For a positive balance, carry-forward is zero when disabled and otherwise limited by the configured cap (or the full positive balance when no cap is configured). The remaining positive amount is expired in the source year. A carried amount is written to the next leave-year ledger with its expiry date calculated as `nextLeaveYearStart.AddMonths(CarryForwardExpiryMonths)`; for a 2026-12-31 year end and six months, that date is 2027-07-01.

Reopening does not rewrite the original close evidence or ledger. Reclose compares the newly reviewed carry-forward and expiry values with the latest close and posts only the necessary ledger deltas. Lifecycle commands and their ledger writes use transactional Audit v2, employee-row serialization, entitlement concurrency versions, and idempotency keys. Carry-forward expiry is recorded once per source ledger credit, and balance projections expose the expiry attached to the carried-forward credit. The leave UI exposes these operations only when the caller has the required balance-view plus close/reopen permissions; backend authorization continues to enforce HR capability, Business, and primary employee-location scope.

The LLD's scope-level `hr_leave_year_closures` aggregate is adapted to the existing employee/type/year entitlement aggregate: lifecycle state and close/reopen evidence live with each entitlement, matching the existing balance and ledger ownership model. This slice does not add a scope-wide batch-close coordinator or global "all employees closed" status; operators perform the permissioned employee/year actions individually. The additive `Phase3DLeaveYearClose` migration extends the existing `RetailPOSDbContext` with immutable lifecycle-event evidence and entitlement close state; it does not create a second context or introduce Phase 3E functionality.

### 3E Overtime Policy/Calculation/Approval
### 3F Non-Financial Exit/Clearance
### 3G Core HR Reports + HR-only acceptance

The Phase 3G core HR report surface is a read-only paged query over the existing employee,
lifecycle-event, document-expiry, and exit-clearance records. It supports inclusive effective-date
ranges, employee and operational-location filters, status filtering, and paging. It returns only
employee code/name and report-relevant operational fields; identity, contact, document-number,
file/storage, confidential attachment, and exit-reason fields are excluded. Mandatory clearance
counts are informational and do not imply financial settlement.

Report access requires `HR.Enabled`, `hr.report.view`, the active Business, and the caller's
identity-derived location grants. Employee visibility follows the employee's primary operational
location; additional assignments do not grant access. Queries are Business-scoped and
`AsNoTracking`. The Angular report route/menu uses the matching capability and permission gates.
This initial core surface does not implement dashboards, CSV/PDF exports, attendance reports
(covered by Phase 2G), leave/overtime/payroll reports, or Factory-source records that do not yet
exist in the operational-location projection.

At completion, `Payroll.Enabled = false` deployment is fully usable.

---

## Phase 4 — Payroll Setup

### 4A Explicit Payroll enablement + code permissions/static navigation/categories/grants
### 4B Salary Component/Structure
### 4C Initial Salary Setup Approval
### 4D Salary Revision Approval/History
### 4E Allowance/Deduction/Manual Adjustment foundation
### 4F Loan/Advance
### 4G Salary/Loan opening import

#### Phase 4G implementation contract

Phase 4G extends the versioned private CSV import pipeline with `Salary` and
`LoanOpening` templates; it does not introduce a second storage/scanning
abstraction. Imports require Payroll capability, `payroll.import.view` or
`payroll.import.execute`, and the corresponding salary/loan view/manage
permission. Employee location access is derived from the primary operational
location effective for the salary or recovery date; Additional assignments do
not establish access.

Execution creates a draft initial-salary setup request or draft loan approval
request through the existing Phase 4C/4F stores. Import execution does not
submit, approve, or activate either request. An approved opening loan retains
the imported historical disbursement date and opening balance as evidence,
does not create a new disbursement, and initializes recovery only from the
imported outstanding balance. A zero outstanding opening balance has no
recovery schedule and becomes settled when approved.

The `payroll/opening-imports` route and `/api/payroll/v1/opening-imports`
endpoint reuse the existing page/controller and enforce the same
Business-scoped, private-storage, scanner, audit-transaction, idempotency, and
atomic-execution rules as Employee/Assignment imports. The forward
`Phase4GPayrollOpeningImports` migration adds opening-balance evidence to the
existing loan table and expands the import-type constraint; it does not alter
the baseline or create another context.

### 4H Foundation hardening

#### Phase 4A implementation contract

Payroll and HR are explicitly provisioned through the existing Super Admin Business feature-settings surface; absent capability rows are displayed as OFF and are not implicitly persisted or enabled. The settings command rejects a resulting `Payroll.Enabled = true` state unless `HR.Enabled` is explicitly true. Existing catalogue permissions remain independent: every `payroll.*` policy requires `Payroll.Enabled`, no HR permission aliases to Payroll, and no shared role is seeded or upgraded with Payroll grants. Angular navigation requires both the verified Payroll capability and the exact Payroll permission, while API authorization remains authoritative. Payroll opt-in changes continue through the central Audit v2 change-tracker path.

#### Phase 4B implementation contract

Phase 4B adds Business-owned salary-component definitions and reusable salary-structure templates to the existing `RetailPOSDbContext`; it does not create employee salary versions, salary approvals, payroll calculations, or employee compensation history. Writes are authorized by the existing `payroll.salary.manage` policy and Payroll capability, reads by `payroll.salary.view` and Payroll capability, and all persistence queries include the validated Business identifier. Required Audit v2 transactions surround writes, and versioned updates reject stale versions. Component and structure codes are immutable after creation; deactivation is soft and versioned, and a component used by an active structure cannot be deactivated.

The formula contract is intentionally restricted to component codes, decimal literals, unary/binary `+` and `-`, `*`, `/`, and parentheses. Functions, external variables, and other token forms are rejected. References must resolve to active components in the same Business; dependency cycles are rejected. Structure formulas and overrides may reference only active components included in that structure. Formula parsing/reference validation does not evaluate payroll amounts.

The additive `Phase4BSalaryComponentStructure` migration creates `payroll_salary_components`, `payroll_salary_structures`, and `payroll_salary_structure_lines`, including Business-scoped unique codes, same-Business composite foreign keys, validation constraints, and concurrency versions. The Angular Salary Setup page is gated by verified Payroll capability and salary-view/manage permissions. Employee-specific salary setup and its approval workflow remain Phase 4C.

---

## Phase 5 — Payroll Processing & Period Control

### 5A Payroll Period
### 5B Payroll Run + eligibility
### 5C Calculation engine + component lines
### 5D Snapshot + deterministic rounding

Payroll currency is resolved from immutable, Business-scoped monetary configuration versions, not installation-wide `SystemSettings`. Each version defines an effective-from date, ISO currency code, decimal precision, and rounding mode. A run requires exactly one such version to apply throughout its whole period and freezes that version reference and values; a configuration boundary inside a period blocks run creation. Monetary configuration is managed through the existing Payroll calculation-policy permissions and required Audit v2 transaction. Historical runs and calculation snapshots retain the frozen monetary version even when a later configuration becomes effective.

### 5E Validation workbench

The validation workbench reads the current run calculation and eligibility evidence and returns
paginated, amount-free findings. Blocking errors are distinct from warnings: missing effective
salary, invalid eligibility evidence, incomplete calculation evidence, duplicate employee
inclusion, negative net pay, required monetary configuration, and invalid calculation inputs block
submission; pending leave/overtime approvals and expected employee exclusions are warnings.
Sensitive findings require both Payroll run-view and salary-view permission and are recorded through
Audit v2. Phase 5E now uses the effective-dated, immutable employee payment-destination master
(Cash, Bank, or Mobile) as authoritative evidence. It checks the destination effective on the
Payroll period end date and emits amount-free `MissingPaymentDestination`,
`InvalidBankPaymentDestination`, `InvalidMobilePaymentDestination`, or
`AmbiguousPaymentDestination` blocking findings per included employee. Managing destinations is
separate from Phase 6 payment execution; no payment record, batch, bank integration, or
reconciliation workflow is introduced here.

### 5F Submit/Approve/Maker-Checker
### 5G Lock/Reopen/Close
### 5H Correction/Off-cycle
### 5I Payroll audit/hardening

Phase 5 implements close assessment/gates, but an enabled financial production release cannot claim close end-to-end acceptance until Phase 6 payment/reconciliation/posting surfaces exercise all gates. There is no temporary close bypass between phases.

---

## Phase 6 — Payments/Payslip/Accounting

### 6A Payment records + mixed/partial payment
### 6B Hold/Release/Fail/Return/Retry
### 6C Payment Batch + Bank File
### 6D Reconciliation
### 6E Private, permission-controlled and audited Payslip PDF/Print/Email
### 6F Excel/CSV/PDF exports

Phase 6F exports only persisted results from a protected Payroll run revision;
they do not recalculate Payroll or create accounting/payment state. The
authenticated endpoints are:

- `GET /api/payroll/v1/runs/{runId}/exports/csv`
- `GET /api/payroll/v1/runs/{runId}/exports/excel`
- `GET /api/payroll/v1/runs/{runId}/exports/pdf-summary`

Every format requires `payroll.export` and `payroll.salary.view`, an enabled HR
and Payroll capability, and access to the run's Business and persisted scope.
Location-scoped result selection uses the frozen Primary `LocationIdSnapshot`;
Additional employee-location assignments do not grant access. Payment totals
and status/reconciliation summaries are included only with
`payroll.payment.view`, and payment destination/account details are never
exported.

CSV protects formula-like text and uses invariant numeric amounts. XLSX stores
untrusted text as text and amounts as numeric cells. The PDF is an aggregate
summary. All formats use persisted component lines, result totals, and frozen
run/employee presentation evidence; mutable Position or Location masters are
not consulted. A required Audit v2 event containing safe run/format/row-count
and output-hash metadata must commit before response bytes are released.
Downloads use attachment disposition and private no-store headers. This slice
adds no export artifact, outbox, or persistence schema.

### 6G Accounting Expense Posting

Payroll owns the expense-posting request, component-to-account mappings, and
posting status. It must submit the canonical journal request through
`IFrozenSystemJournalStore`; Payroll must not create or mutate Accounting
journal entities directly. Each request is scoped to the approved run revision
and uses `payroll-expense:{businessId}:{runId}:r{revision}` as its stable
Accounting reference. Persisted result totals and component lines are the
posting source; the debit and credit totals must balance exactly without a
plug. Posting date is the Payroll period end date, and Accounting remains
responsible for open-period validation without date shifting.

The Payroll mapping table is Business-scoped and maps frozen earning and
deduction component codes plus `NET_PAYABLE` to active Business chart accounts.
Earnings map to expense or cost-of-goods-sold accounts, deduction credits map
only to asset, liability, expense, or cost-of-goods-sold accounts, and net
payable maps to a liability. All selected accounts are revalidated at posting
time. Mapping
administration uses existing Accounting configuration permissions. Payroll
posting and status reads use the Payroll accounting permissions, Payroll
capability checks, run scope, and Required Audit v2.

Accounting's current journal-line contract does not expose an HR Location
dimension. When a Payroll result has a frozen location ID, the journal line
preserves it as the reference-only cost-center value `HR_LOCATION:{id}`.
Payroll must not resolve a mutable Location label or alias the value to an
Outlet, Warehouse, or Factory.

Accounting-enabled period close requires a Posted Accounting journal for every
current Payroll run revision. Accounting may be skipped only when the
Business explicitly disables the feature; an unconfigured feature state blocks
close. Existing payment and reconciliation close gates remain in force.
Close assessment evaluates failed/returned resolution, held payments, and
outstanding liability from the active payment attempts, rather than treating
the presence of a payable Payroll result as an unresolved payment. Period close
and payment, reconciliation, batch, and Accounting posting writes serialize on
the shared Business financial-mutation advisory lock; each writer re-reads its
source state after acquiring the lock. Closed periods reject new payment
changes, while idempotent replays may still return their recorded outcome.

Correction runs fail closed in this slice. Posting a correction as an
additional full expense could double-count a previously posted original; the
approved Accounting reversal/replacement workflow is intentionally deferred to
the later correction/reversal slice. An active correction also blocks posting
its original run, and the period cannot pass its Accounting posting gate until
the correction lifecycle is supported.

### 6H Accounting Payment Posting

Phase 6H posts verified Payroll settlement receipts through the existing
`IFrozenSystemJournalStore`; Payroll does not write Accounting journal entities
directly. The source of truth is a persisted, verified settlement evidence row
and its fully settled allocation, together with the latest successful
reconciliation for that allocation. Payment submission and batch state alone
do not authorize posting.

Posting identity is allocation-level and evidence-specific:
`payroll-settlement:{businessId}:{evidenceId}:{allocationId}`. Replaying the
same identity and canonical payload returns the persisted outcome; a changed
payload for that identity conflicts. Different verified settlement evidence
or a different allocation is a separate legitimate posting. The original
Payroll expense journal remains separate and is not repeated by this phase.
Each settlement debits the Business-configured Cash, Bank, or Mobile/MFS asset
account and credits the active `NET_PAYABLE` liability mapping. Account
ownership/type/activity and Accounting period rules are revalidated when the
frozen journal is submitted.

A payment with remaining balance uses the distinct `PartiallyPaid` state;
only an individually fully settled and reconciled allocation can be posted,
even while its parent payment remains partially paid. The period close gate
requires a Posted SalaryPayment journal for every verified settlement
evidence/allocation identity, in addition to the Phase 6G expense and existing
payment/reconciliation gates. The controlled period lifecycle permits the
transition from `Approved` to `Posted` only after those gates pass. This slice
adds settlement posting identity, reconciliation linkage, and supporting
indexes/constraints through an additive migration; journal persistence remains
owned by Accounting.

### 6I Payload-hash conflicts, uncertain responses, repeat-success reversal/reopen and close hardening

---

## Phase 7 — Final Settlement

### 7A Final Settlement calculation
### 7B Approval
### 7C Accounting posting/payment
### 7D Exit completion and audit

---

## Phase 8 — Advanced Reporting

### 8A HR dashboard
### 8B Payroll dashboard
### 8C Advanced reports
### 8D Performance/query optimization
### 8E Final regression and documentation

---

# 35. Definition of Done for Every Slice

A slice is not complete until all applicable items pass:

```text
[ ] Requirement for the slice implemented only
[ ] All directly coupled surfaces complete; approved requirements not weakened
[ ] Domain rules/invariants covered
[ ] Application command/query + validation
[ ] API route + code permission/catalogue-derived authorization policy
[ ] Verified Business/capability enforcement in API, handler, stores and workers
[ ] Location/field/classification scope where applicable
[ ] Angular standalone route/page/action/static navigation where applicable
[ ] Role-editor category + approved duplicate-safe JSON grant upgrade/defaults
[ ] Audit v2 critical atomicity + confidential read/download/export coverage
[ ] Private storage/authorized delivery where applicable
[ ] Additive existing-context migration reviewed; baseline unchanged
[ ] Migration chain/clean install/populated baseline upgrade + model/snapshot validation
[ ] No unintended EF model drift or unrelated schema changes
[ ] Idempotency/hash-conflict/concurrency + failure/recovery tests where applicable
[ ] Unit tests
[ ] Integration/API tests
[ ] Frontend tests
[ ] Release backend build
[ ] Angular production build
[ ] OpenAPI route verification where applicable
[ ] git diff --check
[ ] No unrelated refactor
[ ] No next-phase work started
```

---

# 36. Copilot / Coding-Agent Implementation Instructions

Use this section as standing instructions when giving this LLD to a coding agent.

1. Read the current solution architecture before creating files.
2. Use existing `RetailPOSDbContext`, long IDs/Domain namespace, narrow stores, MediatR markers/validators, code permissions/policies, static navigation/categories and testing utilities; no assumed base entity or auth/transaction/audit behaviors.
3. Do not introduce a generic repository if the project does not already use one.
4. Integrate central Audit v2; no HR-specific audit system. Critical state changes require atomic/durable audit, not best-effort interceptor alone.
5. Do not create a new Identity user model.
6. Do not directly reference Identity EF entities from HR Domain.
7. Do not directly reference Accounting EF entities from Payroll Domain.
8. Do not couple Domain to ZKTeco or any biometric vendor.
9. Do not implement Payroll features in HR-only slices.
10. Do not add speculative fields/features outside this LLD unless required by existing platform conventions.
11. Before adding a migration, inspect current EF model/migrations and ensure the change is intentional.
12. Preserve unrelated dirty working-tree changes.
13. Run focused tests first, then full relevant regression suites.
14. Do not stage or commit unless explicitly requested.
15. After every slice, report:
   - files changed;
   - migration created;
   - catalogue/policies/static navigation/categories and controlled JSON grant upgrade;
   - APIs added;
   - tests added;
   - build/test result;
   - remaining scope;
   - any deviations from this LLD.
16. Preserve baseline migration, existing feature defaults and unrelated customized role grants. No second HrPayrollDbContext or relational menu/permission tables.
17. Enforce explicit Business Payroll opt-in, private documents and the Accounting frozen-hash/repeat-success correction contract. Do not claim Phase 0 facilities exist until implemented/verified.

---

# 37. Implementation Prompt Template for Each Slice

```text
Implement ONLY <PHASE/SLICE> from
IraniMart_HR_Payroll_Low_Level_Design_v1.2.md.

Before coding:
1. inspect the existing Clean Architecture conventions;
2. inspect code permissions/API policies/JSON grants/static navigation and Audit v2;
3. inspect current EF DbContext/migrations;
4. verify Phase 0 gates and identify narrow reusable services;
5. do not create parallel infrastructure.

Scope:
<copy exact slice scope>

Requirements:
- follow the database/API/domain design in the LLD;
- preserve microservice-ready boundaries;
- use CQRS/MediatR conventions already present;
- enforce capability + permission + location scope server-side;
- add transactional/durable Audit v2 and confidential access coverage;
- add code catalogue/API policies/static navigation/categories and approved JSON grant upgrades;
- preserve baseline; use existing DbContext/additive snake_case migration;
- add focused unit/integration/API/frontend tests;
- do not start the next slice;
- do not stage or commit.

Validation before completion:
- Release backend build;
- relevant backend tests;
- relevant Angular tests;
- Angular production build;
- migration/model status;
- OpenAPI checks for new routes;
- git diff --check.

At the end, report implementation summary and validation results.
```

---

# 38. Architecture Decisions / Open Prerequisites

## 38.1 Confirmed Decisions (Not Open for Initial Implementation)

- One Clean Architecture monolith and existing four projects; no extraction/rewrite.
- Existing `RetailPOSDbContext`, migration assembly and baseline; additive forward snake_case prefix mappings.
- Code catalogue/API policies/JSON grants/static navigation/category model; no relational Menu/Permission framework.
- Business-scoped new module flags default OFF; Payroll explicitly opt-in; unrelated legacy defaults unchanged.
- Audit v2 is the central target; private HR artifacts; no legacy/public-upload reuse.
- Shared durable outbox/inbox/RabbitMQ is new Phase 0 infrastructure.
- Small HR/Payroll Approval Kernel only; no unrelated approval refactor.
- Source-owned Outlet/Warehouse; HR read projection; Warehouse is not Factory.
- Accounting Application contracts only, with frozen instruction/hash and repeat-success linked corrections.
- Section 10.8.1 close gates apply regardless of diagram shorthand.

## 38.2 Open Prerequisite Register

| ID | Decision/owner | Required resolution before |
|---|---|---|
| ADR-01 | Factory operational source/module owner (architecture + operations) | Factory master identity, Business ownership, version/lifecycle/bootstrap/event contracts, location policy/scope and Accounting dimension adapters; no Factory events until approved |
| ADR-02 | HeadOffice/other location authority + source version/deletion contract (architecture) | Source adapters; generic HR type/policy coverage remains required; legacy unowned Outlet/Warehouse remediation plan |
| ADR-03 | Private storage provider/key management/scan/retention/expiry/legal-hold and confidential email policy (operations + security + HR) | Phase 0F acceptance/document and payslip delivery; choose filesystem outside public root or private object store |
| ADR-04 | Broker deployment/topology/TLS/credentials/retry/dead-letter/replay/retention ownership (operations + architecture) | Phase 0G production acceptance; include monitored recovery and no new service assumption |
| ADR-05 | Accounting COA slots/dimensions/sync versus async adapter and durable identity/hash/reversal contract (Accounting owner) | Phase 0H acceptance; Factory/head-office mapping depends on ADR-01/02 |
| ADR-06 | Reopen correction accounting policy: differential versus linked reversal/replacement (finance) | Payroll reopen production enablement; settled payments cannot be blindly reversed |
| ADR-07 | Payment reconciliation requirements and close-with-held/outstanding exceptions (finance + approver) | Payroll settings/payment/close acceptance; Section 10.8.1 strict baseline until approved |
| ADR-08 | HR/salary/leave/OT approver scopes, delegation/quorum/self-approval and shared-role grant upgrade targets (HR + security) | Kernel use cases and permission rollout; no arbitrary default Payroll grants |
| ADR-09 | Salary proration, OT bands, break treatment, rounding, statutory deductions and effective-date rules (HR + finance) | Salary/payroll calculation golden tests; reuse current monetary rounding only where approved |
| ADR-10 | Critical audit local flush transaction versus reviewed durable central delivery; confidential audit ACL contract (audit owner) | Phase 0E acceptance; required audit failure cannot become silent business success |

Record approved ADR reference/version, accountable owner, due date and verification evidence. These are implementation prerequisites, not reduced requirements. No decision above authorizes an unscoped default, public confidential file, dropped Factory requirement or untested financial shortcut.

## 38.3 Requirement / Implementation Conflicts and Resolutions

| Functional/design wording | Current constraint or ambiguity | Resolution without weakening Requirements v1.5 |
|---|---|---|
| Menu/action/role functionality | No relational menu/permission tables | Map every required view/action to catalogue/policy/JSON grants/navigation/category surfaces |
| Factory ownership/policies | No Factory source exists | Preserve Factory functionality; block its integration pending explicit source ADR, never map Warehouse silently |
| Payroll optional/default OFF | Legacy missing row/scope enables features | New module-specific presence-aware Business resolver; leave existing unrelated defaults intact |
| Reliable synchronization/integration | Current publisher is volatile, no durable broker implementation | New tested shared delivery foundation, not an assumed reuse |
| Central audit and confidentiality | Legacy audit naming; current best-effort interceptor/general read rights | Audit v2 atomic/durable contract plus HR read/export ACL/redaction |
| Confidential employee files/payslips | Current uploads are public | New private adapter and authorized audited streaming |
| Financial idempotency | Existing create-or-get ignores differing content; successful reverse retry fails | Accounting-owned frozen-hash conflict check and linked repeat-success correction lookup |
| Approved/Posted → Closed shorthand | Payment/reconciliation/exception gates not fully stated | Strict gate matrix; finance must approve explicit exception policy, never silent close |
| Rounding and overtime examples | Configured business rules must align with current money precision | Approved policy + deterministic golden tests; avoid double break deduction; no guessed formula |

No approved business requirement is removed. Source absence and infrastructure/security gaps are surfaced as prerequisite work. Where a policy is unresolved, keep the strict safe baseline rather than silently weaken controls.

---

# 39. LLD Adaptation Register

| # | v1.1 design/assumption | v1.2 adaptation | Reason/evidence |
|---|---|---|---|
| A01 | Generic module/context choices | Existing four projects/context fixed for initial delivery | Current monolith, dependency tests and sole context |
| A02 | Schemas/base entity examples | Explicit `hr_*`/`payroll_*`, long IDs, explicit metadata, PostgreSQL concurrency | Snake_case mappings; no shared base class; Inventory xmin |
| A03 | Migration strategy open | Preserve baseline plus forward migrations/snapshot/upgrade tests | One baseline; migration test must evolve additively |
| A04 | Possible implicit CQRS behaviors | Explicit concern matrix/transaction flow; validation only globally registered | Application DI and narrow stores |
| A05 | Menu/action seed model | Code catalogue/policies/static navigation/category + JSON set-union grants | No relational permission/menu tables; shared Roles/seed early return |
| A06 | Generic feature guards | Presence-aware Business opt-in defaults/dependencies/caches/jobs | Existing absent setting/scope returns true |
| A07 | Central audit abstractly described | Audit v2 atomic/durable state changes and confidential read/export ACLs | After-save audit catches errors; generic audit view not sufficient |
| A08 | Optional existing messaging reuse | New minimal shared outbox/publisher/inbox/RabbitMQ with failure tests | No executable broker/durable delivery stack |
| A09 | Factory may exist | Explicit Factory source ADR; Outlet/Warehouse authoritative; bootstrap/version/tombstone/reconciliation | No Factory; legacy source lifecycle gaps |
| A10 | Reusable approval might exist | Small HR/Payroll Kernel, immutable decisions/resolved steps; no unrelated refactor | Existing module-specific approvals only |
| A11 | Generic document storage | Private quarantine/version/retention/audited stream contract | Existing product path public before authentication |
| A12 | Posting idempotency/hash optional | Frozen canonical instruction/hash equality, owned identity, conflict and uncertain-response lookup | Current journal create-or-get lacks equality |
| A13 | Generic reversal/correction | Stable linked correction and repeat-success reversal; new run after reopen | Current reversal retry rejects Reversed originals |
| A14 | Close shorthand | Explicit payment/reconciliation/Accounting/exception gates and race protection | Full functional close controls require concrete implementation |
| A15 | Generic jobs/frontend modules | Existing hosted-service style and standalone pages/routes/services/models | No general scheduler; actual Angular structure |
| A16 | Foundation mixed into Phase 1 | Phase 0 foundation gate, then requested Phases 1–8; complete slice DoD | Lowest-risk prerequisite sequencing |
| A17 | Shift rules | Preserved individual weekly-offs, scoped holidays and generic extended/cross-midnight rules | Valid v1.1 business content; Requirements v1.5 |

All adaptations describe implementation mechanisms or strengthen explicit verification; Requirements v1.5 remains unchanged.

---

# 40. Expected Final Architecture Outcome

Current deployment remains one IraniMart application:

```text
pos-v2
│
├── RetailPOS.API
├── RetailPOS.Application
├── RetailPOS.Domain
├── RetailPOS.Infrastructure
├── RetailPOS.UnitTests
└── RetailPOS.IntegrationTests
```

Inside those projects, maintain logical module boundaries:

```text
IraniMart ERP — Modular Monolith
│
├── Existing Sales / POS
│   └── Outlet / other source-owned operational masters
│          ↓ NEW shared transactional Outbox
│       NEW RabbitMQ publisher/transport
│          ↓ NEW inbox/idempotent consumer
├── HR Core
│   ├── Operational Location Projection
│   ├── Organisation / Position
│   ├── Employee / Lifecycle
│   ├── Documents
│   ├── Approval
│   ├── Shift / Roster
│   ├── Attendance / Device Integration
│   ├── Holiday / Weekly Off
│   ├── Leave
│   ├── Overtime
│   └── Exit / Clearance
│
├── Optional Payroll
│   ├── Salary
│   ├── Loan / Advance
│   ├── Payroll Period / Run
│   ├── Snapshot / Calculation
│   ├── Payment / Reconciliation
│   ├── Payslip / Export
│   └── Final Settlement
│
├── Identity / Authorization
│      ↑ explicit reference/application contracts
│
└── Accounting
       ↑ explicit idempotent application contract/events
```

Dependency rules:

```text
HR Domain       ─X→ Sales/POS Domain or Outlet EF entity
Payroll Domain  ─X→ Accounting EF entities
HR Domain       ─X→ Identity EF entities

Source module
   ↓ integration event
HR local projection

Payroll Application
   ↓ accounting abstraction
Infrastructure adapter
   ↓
Existing Accounting application contract
```

The same HR core must work when Payroll is disabled.

The current implementation scope is a **modular monolith**, not microservices. Future extraction must remain possible without rewriting core Domain/Application behavior.

---

# 41. Current-Source Evidence and Revision Verification

This document is a static architecture-fit revision, not evidence that proposed facilities are implemented or runtime tests passed.

| Evidence | Verified fact |
|---|---|
| `src/RetailPOS.Application/DependencyInjection.cs` and `Abstractions/Messaging` | Handler/validator scanning and existing markers; ValidationBehavior is the global behavior |
| `src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs` and `Data/Migrations` | One production context, explicit snake_case, sole preserved baseline |
| `src/RetailPOS.Application/Identity/Permissions/PermissionCatalog.cs`, API `Program.cs`, Domain `Role.cs` | Code catalogue/policies and JSON grants, not relational menu tables |
| `retailpos-frontend/src/app/models/menu.model.ts`, `services/menu.service.ts`, `components/roles/role-form.component.ts`, `app.routes.ts` | Static navigation/permission filtering, hard-coded role categories, standalone routing |
| Application `FeatureEntitlements/Queries/IsFeatureEnabledQuery.cs`, Infrastructure `FeatureEntitlements/FeatureEntitlementReadStore.cs` | Missing Business/setting may return enabled |
| Infrastructure `Audit/AuditChangeTrackerInterceptor.cs`, `Audit/AuditService.cs`, Application `AuditEvents/Abstractions/IAuditEventCommandStore.cs` | Audit v2 current-context capture/flush and best-effort limitation; request fields require adaptation |
| API `Controllers/AuditEventsController.cs`, Infrastructure `AuditEvents/AuditEventReadStore.cs` | Central confidential Business/detail/export restrictions need explicit wiring |
| Infrastructure `Products/ProductImageFileStore.cs`, API `Program.cs` static uploads | Public product behavior is unsuitable for HR confidentiality |
| Domain `Entities/Outlet.cs`, `Entities/Warehouse.cs`, context/source inventory | Existing operational authorities; Factory absent |
| API `Services/ISaleEventPublisher.cs` and logging publisher | Non-durable/in-process current publishing |
| Application `AccountingJournals/Abstractions/IManualJournalDraftStore.cs`, Infrastructure `AccountingJournals/ManualJournalDraftStore.cs` | Existing system-journal draft/post/reverse, missing payload equality and successful reversal replay |
| Application `AccountingConfiguration/Queries/ResolvePostingAccountQuery.cs`, Domain `Money/MonetaryRounding.cs` | Accounting-owned mapping and current money rounding constraints |
| Current test-project architecture/migration/PostgreSQL fixtures | Preserve dependency fences and validate additive chain/provider behavior; use actual existing project placement rather than invent a test framework |

Revision validation: preserve original Requirements and header-v1.1 files; compare content coverage against the source LLD, scan for contradictory existing-infrastructure assumptions, verify tables/permissions/phase gates and document whitespace. Backend/Angular builds, tests and migration execution belong to implementation slice acceptance and are not performed for this documentation-only revision.
