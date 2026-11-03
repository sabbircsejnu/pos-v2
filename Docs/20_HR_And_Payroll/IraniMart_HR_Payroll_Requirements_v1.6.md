# IraniMart HR & Payroll Module — Requirements & Architecture

**Version:** 1.6  
**Date:** 2026-10-23  
**Status:** Development Baseline — updated Factory ownership/projection and simplified Position semantics  
**Target Architecture:** Clean Architecture, modular monolith ready for independent microservice extraction

---

## Revision Note — v1.6

This revision records the approved IraniMart-specific operational-location decisions made during implementation:

- `OrganisationUnit` remains the backend/domain concept, while the user-facing meaning is **Department / Team**.
- Outlet and Warehouse remain authoritative outside HR and appear in HR through the Operational Location projection.
- Factory is required only for HR workforce-location use. A lightweight Business-scoped **Factory master** is therefore owned by the HR bounded context, exposed under Administration, and synchronizes into the same HR Operational Location projection used by employee assignments.
- Factory is a real `Factory` source type and must never be aliased to Warehouse.
- Position is a reusable job role under a Department / Team. Normal Position create/edit no longer asks for Operational Location or Position effective dates; employee position/location assignments own effective-dated workplace history.
- These changes do not introduce a manufacturing/production module.

---

## 1. Purpose

Build a complete HR platform with an optional Payroll capability for IraniMart/POS ERP. HR must work fully without Payroll. Payroll can be enabled for customers who require it, without changing or duplicating the core HR domain.

The complete HR & Payroll bounded context must remain sufficiently isolated from the rest of the ERP so it can later be deployed as a separate microservice with minimal domain/application changes.

---

## 2. Non-Negotiable Architecture Principles

### 2.1 Clean Architecture
Maintain the same Clean Architecture approach used by the existing modules.

Recommended dependency direction:

```text
Presentation/API
      ↓
Application
      ↓
Domain
      ↑
Infrastructure
```

- **Domain** contains entities, value objects, domain rules/events and domain services. It must not depend on EF Core, controllers, external devices, email, files, accounting, or other infrastructure.
- **Application** contains use cases, CQRS commands/queries, handlers, DTOs, validators, authorization requirements and ports/interfaces.
- **Infrastructure** implements persistence, attendance-device adapters, email, file storage, exports, integrations, background jobs and external services.
- **Presentation/API** exposes HTTP endpoints and handles transport concerns only.
- Business rules must not live in controllers, Angular components, repositories or integration adapters.
- Use existing project conventions for MediatR/CQRS, validation, authorization, result/error handling, persistence and testing rather than introducing parallel patterns.

### 2.2 Modular Independence
Treat HR & Payroll as an independent bounded context/module.

Do not create direct dependencies from HR domain code to Sales, Inventory, POS, Accounting or other modules. Cross-module communication must occur through explicit contracts, application interfaces, domain/integration events, or an outbox/inbox mechanism where appropriate.

Avoid cross-module database joins and direct writes to another module's tables.

### 2.3 Microservice Readiness
Design so that the entire HR & Payroll module can later move to a separate service/database.

Requirements:

- Module-owned tables/schema and migrations.
- Menu/action/permission/reference-data changes must use the existing migration/seed mechanism and remain duplicate-safe.
- No foreign-key dependency to another module's internal tables where an external identifier/reference can be used instead.
- Explicit integration contracts.
- Idempotent event consumers.
- Outbox/inbox support for reliable asynchronous integration where required.
- No assumption that another module shares the same DbContext or transaction.
- API/event contracts must be versionable.
- HR may reference an Identity/Login User by stable external identifier, but must not own authentication credentials or depend on Identity persistence internals.

**Microservice-ready does not mean microservices must be introduced now.** Prefer a well-isolated modular monolith initially unless deployment/scaling requirements justify extraction.


### 2.4 Reusable Approval Workflow Framework

Approval-driven HR/Payroll processes must use a reusable approval framework or the ERP's existing central approval mechanism rather than implementing unrelated approval logic separately for salary, leave, overtime, transfers and payroll.

The framework must support, where required:

```text
Approval Policy
    ↓
Approval Instance
    ↓
Step 1 → Step 2 → ... → Final Decision
```

A policy/step may define:

- Process type, e.g. Leave, Overtime, Employee Transfer, Salary Setup, Salary Revision, Payroll Run, Exit
- Sequence/order
- Approver type: specific User, Role, Reporting Manager, Location Manager or other supported resolver
- Single-level or multi-level approval
- Required number of approvals where applicable
- Approve / Reject / Cancel transitions
- Self-approval policy
- Delegation/substitution where supported
- Effective From/To or version where approval policy changes over time
- Business/location/location-type scope
- Escalation/reminder metadata where required

Baseline rules:

- Salary setup/revision uses maker-checker separation as defined later.
- Leave and Overtime support multi-level approval.
- Payroll Run uses the configured single-level approval baseline unless changed by an approved policy.
- Employee transfer and exit approval may use the reusable framework where configured.
- An approval decision must be idempotent; duplicate requests must not approve/reject twice.
- Approval history is immutable and must show every step, actor, decision, timestamp and comment/reason.
- Backend rules are authoritative; UI state alone must never enforce approval order.
- If the ERP already contains a reusable approval engine, extend/reuse it rather than building a parallel HR-only engine.
- Approval policy configuration must remain outside core domain entities through explicit application/domain abstractions so the HR & Payroll bounded context remains microservice-ready.


---

## 3. Product Packaging / Feature Independence

### 3.1 HR Core — Must Work Without Payroll
A customer must be able to enable only HR and use:

- Organisation hierarchy
- Position management
- Employee records
- Employment lifecycle/history
- Employee documents
- Location assignments/transfers
- Shift and roster
- Holiday/weekly-off configuration
- Attendance
- Attendance device integration
- Leave
- Overtime tracking/approval
- Exit and clearance
- Core HR operational reports
- HR reports/dashboard
- Permissions
- Audit logs

No Payroll configuration, menu, API permission or database workflow should be required for HR-only operation.

### 3.2 Payroll Add-on
Payroll is an optional capability/feature set on top of HR Core and includes:

- Salary components
- Employee salary structures
- Salary revisions
- Allowances/deductions
- Advances/loans
- Payroll period control / processing
- Payroll approval/reopen
- Salary payment / reconciliation
- Payslips
- Payroll exports/bank files
- Final financial settlement
- Accounting integration
- Payroll reports/dashboard

### 3.3 Feature Enablement
Support tenant/business-level capability configuration, e.g.:

```text
HR.Enabled = true
Payroll.Enabled = false | true
Attendance.Enabled = true | false
DeviceIntegration.Enabled = true | false
```

When Payroll is disabled:

- Payroll menus/routes must not be shown.
- Payroll endpoints must reject unauthorized/disabled capability access.
- HR workflows must remain functional.
- HR entities must not require salary/payroll data.
- Attendance/leave/overtime may operate independently as HR features.

Feature flags are not a replacement for authorization; both capability and permission checks are required.

---

## 4. Organisation Structure, Location & Position Management

IraniMart separates **organisation structure** from **operational workplace**.

The user-facing organisation hierarchy is:

```text
Company / Business
└── Department
    └── Team / Section
        └── Position
            └── Employee
```

The operational-location dimension is separate:

```text
Operational Location
├── Outlet
├── Warehouse
└── Factory
```

An Employee may hold a Position in one Department / Team while working at an Outlet, Warehouse or Factory. Workplace must not be encoded into the Position name.

### 4.1 Organisation / Departments & Teams

`OrganisationUnit` remains the backend/domain concept. In the IraniMart UI it represents **Departments & Teams**.

Examples:

- Sales
- Production
- Accounts & Finance
- HR & Administration
- Warehouse / Inventory
- Procurement
- Ecommerce
- Sales → Retail Sales
- Sales → Online Sales
- Production → Quality Control

Organisation units must be Business-scoped, hierarchical and cycle-safe. They are HR-owned master data and are not operational-location records.

### 4.2 Operational Location Ownership and Projection

HR uses one read model/projection for employee workplace selection and historical location context, but the authoritative source depends on location type:

- **Outlet:** existing Outlet master remains authoritative.
- **Warehouse:** existing Warehouse master remains authoritative.
- **Factory:** lightweight HR-owned Factory master is authoritative because Factory is required only for HR workforce-location use.
- **Head Office / other future types:** require an explicit approved source before specific locations are created; Business-wide scope must not be represented by a fake location row.

HR must not create duplicate Outlet or Warehouse masters.

The unified HR Operational Location projection must expose source identity and type so employee workflows can select locations without coupling the Employee aggregate to Outlet/Warehouse/Factory tables.

Required source types for the current IraniMart scope:

```text
Outlet
Warehouse
Factory
```

A projected operational location should support, where applicable:

- Stable source ID/code
- Name
- Source/location type
- Business reference
- Address/contact details
- Active/inactive state
- Source version/change identity
- Historical/tombstone behavior required to preserve old assignments

### 4.3 Lightweight Factory Master

Factory is needed only as an HR operational location. This requirement does **not** create a Production/Manufacturing module.

Expose a simple Factory master under the existing Administration area, alongside Outlets and Warehouses.

Minimum user-facing fields:

- Code
- Name
- Address
- Status

Backend-owned fields include Business identity and normal audit/concurrency metadata.

Required operations:

- List
- Create
- Update
- Activate / Deactivate where the existing master-data pattern supports it

Normal users must not physically delete Factory history that is referenced by employee, attendance or payroll records.

Factory create/update/activation/deactivation must synchronize the HR Operational Location projection in the same logical operation. The resulting projection uses:

```text
SourceEntityType = Factory
SourceEntityId   = FactoryId
```

The synchronization must be Business-scoped and idempotent. Because the Factory master and HR projection are within the same HR bounded context and database initially, a local transactional projection update is preferred; RabbitMQ is not required merely to move Factory data inside the same module. Future external consumers may use the shared outbox without changing Factory ownership.

### 4.4 Employee Location Assignment

Employees are not different entity types based on workplace.

Examples:

```text
Employee A
Department / Team: Production
Position: Production Supervisor
Primary Location: Factory - Gazipur

Employee B
Department / Team: Sales
Position: Sales Executive
Primary Location: Outlet - Bashundhara

Employee C
Department / Team: Warehouse / Inventory
Position: Storekeeper
Primary Location: Warehouse - Central
```

The same Employee aggregate supports Primary and Additional assignments across Outlet, Factory and Warehouse.

Location-specific attendance/roster/weekly-off/overtime rules are configuration/policy concerns, not separate Employee models.

### 4.5 Position Management

A Position is a reusable job role within a Department / Team.

Normal Position create/edit must contain:

- Position code
- Position name
- Department / Team
- Reports-to Position
- Status

Examples:

```text
Department / Team: Sales
Position: Sales Executive

Department / Team: Production
Position: Production Supervisor
```

Do **not** require Operational Location on the normal Position master. The same `Sales Executive` Position may be assigned to employees at different Outlets.

Do **not** require Position `Effective From` / `Effective To` in the normal create/edit UX. Effective-dated history belongs to employee position assignments, employee location assignments, transfers and lifecycle events. Existing legacy Position date/location fields may remain for compatibility but must not drive new Position semantics or employee location authorization.

Position status may retain the existing Vacant/Filled/Temporarily Filled/Inactive values, but occupancy automation is a separate decision. Positions with historical assignments are deactivated rather than physically deleted.

---

## 5. Employee Management

Support configurable employment types including permanent, probation, contractual, part-time, temporary, intern and others.

Employee profile should support:

### Employment
- Employee ID/code
- Name
- Joining date
- Employment type/status
- Position/department
- Reporting manager
- Primary and additional locations, regardless of location type (Outlet/Factory/Warehouse/Head Office/etc.)
- Probation/contract dates
- Confirmation date
- Notice period
- Last working date

### Personal
- Date of birth
- Gender where required
- NID/national identity information
- Passport where applicable
- Phone/email
- Present/permanent address
- Emergency contact

### Payment information (Payroll-enabled tenants)
- Preferred payment method
- Bank details
- Mobile banking provider/account

Sensitive fields require field-level permission.


### Employee Lifecycle & Effective-Dated History

Employee lifecycle changes must be historical and effective-dated rather than destructively overwriting employment state.

Supported lifecycle events should include, where applicable:

```text
Joined
→ Probation
→ Confirmed
→ Promotion / Position Change
→ Department / Team Change
→ Reporting Manager Change
→ Employment Type Change
→ Temporary Inactive / Suspension
→ Reinstated
→ Exit
→ Rehire
```

Requirements:

- Preserve previous position, department, manager, employment type and status history.
- Store Effective From/To dates or equivalent immutable lifecycle events.
- Promotion or position change must not erase the previous assignment.
- Rehire must create a new employment period/history while preserving the previous exit.
- Temporary inactive/suspension must not be treated as physical deletion.
- Lifecycle changes that require approval must use the reusable approval framework.
- Historical reporting must resolve the employee state applicable to the requested date/period.
- Payroll and attendance history must not be recalculated merely because a later lifecycle change occurred.
- Lifecycle changes must be audited with actor, timestamp, reason, previous/new state and effective date.


---

## 6. Employee ↔ Login User / Identity Mapping

Employee and authenticated application user are separate concepts and must not be treated as the same entity.

### 6.1 Optional Relationship

Support an optional one-to-one logical relationship:

```text
Identity / Login User
        │
        │ optional 1 : 1
        ↓
Employee
```

Rules:

- An Employee may exist without any application login.
- A Login User may exist without any Employee record.
- An Employee may be linked to at most one active Login User unless a future approved requirement explicitly changes this.
- A Login User may be linked to at most one active Employee.
- Linking and unlinking must be explicit, permission-controlled and audited.
- Deactivating an Employee must not automatically delete the Identity/Login User.
- Disabling a Login User must not delete or corrupt Employee history.
- Identity lifecycle and employment lifecycle remain independently governed.

Typical examples:

- Factory worker: Employee exists; no application login required.
- Showroom staff: Employee exists; login optional depending on assigned system responsibilities.
- HR Manager: Employee may be linked to a Login User.
- Super Admin / support / technical account: Login User may exist without an Employee record.

### 6.2 Architecture Boundary

Do not create a hard database foreign-key dependency from the HR domain to the Identity module's internal user tables.

The HR module should keep only an external/stable Identity User reference or an HR-owned mapping record, for example:

```text
EmployeeUserLink
----------------
Id
EmployeeId
IdentityUserId
LinkedAt
LinkedByUserId
UnlinkedAt
UnlinkedByUserId
UnlinkReason
IsActive
```

Exact persistence shape may follow existing solution conventions, but the boundary requirements are mandatory:

- Identity remains the owner of authentication credentials, login state, roles and permissions.
- HR remains the owner of employee/employment data.
- Cross-module linkage uses stable identifiers/contracts rather than direct ownership of Identity entities.
- No cascade delete from Identity into Employee or from Employee into Identity.
- If HR & Payroll is extracted into a microservice later, the relationship must remain valid through external Identity references/contracts without domain-layer rewrite.

### 6.3 Authentication, Authorization and Employee Context

Authentication/authorization and employee context must remain separate concerns.

```text
Authenticated Login User
        │
        ├── Roles / Permissions / Claims      ← Identity/Authorization authority
        │
        └── Optional Employee Link
                  ↓
              Employee
                  ├── Position
                  ├── Department
                  ├── Primary/Additional Location
                  └── Reporting hierarchy
```

A Position, Department or Employee record must **not automatically grant application permissions**.

For example, an Employee with the job title `Outlet Manager` does not automatically receive HR or Payroll permissions. Access remains controlled by the existing Identity/Role/Permission system.

The Employee link supplies HR/business context that may be used together with authorization, such as:

- Current employee identity
- Employee's applicable location(s)
- Reporting manager/subordinate relationships
- Position/department
- Leave/attendance/roster context
- Notification routing
- Future employee self-service context

### 6.4 Location-Scoped Authorization

Where a Login User is linked to an Employee, the Employee relationship may help determine contextual scope, but backend authorization remains authoritative.

Example:

```text
Login User
    ↓
Permission: HR.Employee.View
    +
Allowed Location Scope: Outlet - Uttara
    ↓
Requested Employee belongs to permitted location?
    ↓
Allow / Deny
```

Do not assume the Employee's own primary location automatically defines all of the Login User's access. Explicit configured role/location scope must take precedence.

### 6.5 Link / Unlink Workflow

Support controlled operations such as:

```text
Create Employee
    ↓
Optional: Link Existing Login User
    or
Optional: Create/Provision Login User through Identity workflow
    ↓
Active Employee ↔ User Link
```

And:

```text
Active Link
    ↓
Authorized Unlink / Employment Exit / Account Change
    ↓
Link Closed with history retained
```

Requirements:

- Prevent duplicate active links.
- Validate both Employee and Identity User references.
- Linking must not silently overwrite another active mapping.
- Unlinking requires an explicit permission.
- Reason/comment should be required for unlink/relink where appropriate.
- Historical mappings must be retained.
- Re-linking must create/update historical linkage records rather than destroying prior history.

### 6.6 Audit Requirements

Employee/User linkage changes are security-sensitive and must be fully audited.

Capture at minimum:

- Employee ID
- Identity User ID
- Action: Linked / Unlinked / Relinked
- Actor User ID
- Timestamp
- Business/tenant/location context where applicable
- Previous link
- New link
- Reason/comment
- Correlation/request ID

Do not log authentication secrets, password data, access tokens or other credential material.

### 6.7 Current and Future Use

Initial HR operation must not require every Employee to have a Login User.

The design must remain ready for future Employee Self-Service without redesigning the Employee aggregate. Future capabilities may include:

- View own profile
- View own attendance
- View own roster
- Request leave
- View leave balance
- View overtime
- View/download own payslip
- Receive HR notifications
- Manager/subordinate self-service workflows

These self-service capabilities remain out of scope unless separately approved; only the Identity ↔ Employee linkage foundation is in scope now.


---

## 7. Employee Location Assignment & Transfer

Employees may belong to multiple locations and may move between different location types over time.

Examples include:

- Outlet → Outlet transfer
- Outlet → Factory transfer
- Factory → Warehouse transfer
- Head Office → Outlet temporary assignment
- One primary location with one or more additional locations

Maintain:

- Employee
- Location
- Location type snapshot/reference
- Primary/additional assignment
- Effective From/To
- Assignment type
- Temporary/permanent indicator where required
- Approval
- Reason/comment
- Complete history

Transfer/location change workflow:

```text
Draft/Requested → Approved → Effective
```

Business rules must prevent invalid overlapping primary assignments unless explicitly supported by policy.

Never overwrite historical assignments. Historical attendance, roster, payroll and reporting must resolve the location applicable for the relevant effective date rather than only the employee's current location.

---

## 8. Employee Document Management

Full document management supporting NID, passport, contracts, appointment/confirmation letters, certificates, training, salary revision letters, disciplinary records, resignation/clearance documents and configurable types.

Store metadata including:

- Type/category
- Document number
- Issue/expiry date
- File reference
- Notes/status
- Uploaded by/at
- Replacement/version history

Support expiry reminders and permission-controlled access. Store files through an abstraction so local/S3/object storage can be changed without domain changes.

---

## 9. Shift, Roster, Holiday & Weekly Off

### Shift
Support fixed, multiple, rotating and employee-specific shifts.

Configure start/end time, break, minimum working hours, grace rules, overtime eligibility and cross-midnight shifts.

### Roster
Support daily/weekly/monthly roster, bulk assignment, copy previous roster, location roster and historical changes.

Roster and shift policy must support both location-level and location-type defaults. A Factory may use rotating/night shifts while an Outlet may use showroom opening/closing shifts. Employee-specific overrides remain possible where authorized.

### Holiday
Support:

- Company-wide calendar
- Location-type calendar/default where needed
- Location-specific calendar
- Employee override

### Weekly Off
Support:

- Location-type default
- Location-wise
- Employee-wise
- Roster-based

Recommended conflict precedence:

```text
Employee Override → Roster → Location Policy → Location-Type Policy → Company Policy
```

Make precedence explicit/configurable where appropriate.

---

## 10. Attendance Device Integration

This is a first-class integration requirement, not merely a manual import feature.

### 10.1 Supported Sources
Attendance can originate from:

- Built-in/manual entry
- Face-recognition device
- Fingerprint/card device
- Biometric attendance terminal
- External system/API
- File/import mechanism

### 10.2 Push-Based Device Support
Prefer devices capable of real-time PUSH/ADMS or equivalent communication.

Target flow:

```text
Employee Punch
    ↓
Attendance Device
    ↓ Wi-Fi/LAN/Internet
Device Integration Endpoint / Adapter
    ↓
Raw Punch Store
    ↓
Attendance Processor
    ↓
Daily Attendance
    ↓
Late / Absent / Working Hours / Overtime
```

A device may use a vendor protocol rather than arbitrary JSON REST. Implement vendor-specific adapters behind an application port. ZKTeco ADMS/TA PUSH is an example; the domain must not depend on ZKTeco.

### 10.3 Device Master
Maintain:

- Device ID/internal ID
- Serial number
- Vendor/model
- Location
- Location type (derived from Location, not separately owned by the device)
- Connection/protocol type
- Server/device configuration metadata
- Last communication time
- Status
- Firmware information where available
- Active/inactive

Secrets must be stored securely and not exposed through normal APIs/logs.

### 10.4 Employee ↔ Device User Mapping
Maintain mapping between employee and vendor/device user identifier, including history where required.

### 10.5 Raw Punch Data
Never write a device punch directly as final attendance.

Persist immutable raw punch information such as:

- Device serial
- Device user ID
- Punch timestamp
- Verification method (face/fingerprint/card/etc.)
- Direction/status if supplied
- Received timestamp
- Source
- Raw payload/reference where appropriate
- Processing status/error
- Unique/idempotency fingerprint

### 10.6 Reliability
Support:

- Duplicate detection/idempotency
- Offline device storage and delayed sync
- Re-sync/reprocessing
- Out-of-order events
- Device clock/timezone discrepancy
- Failed employee mapping
- Multiple punches per day
- Cross-midnight shifts
- Device health/last-seen monitoring
- Integration failure logging

Raw punches should remain immutable. Corrections should create separate attendance adjustments/audit records.

### 10.7 Wi-Fi Device Target
Current hardware direction for small showrooms is a Wi-Fi-enabled face terminal supporting customer-hosted push/ADMS, with face recognition as primary and fingerprint/card fallback. Device choice must remain replaceable through adapters.

---

## 11. Attendance Processing

Daily attendance may contain:

- Employee/date
- Applicable location/location type for that work date
- Scheduled shift/roster
- Clock in/out
- Working hours
- Break
- Late duration
- Early leave
- Overtime hours
- Status
- Source
- Processing/version metadata

Statuses may include Present, Absent, Late, Half Day, Leave, Holiday, Weekly Off and Missing Punch.

Late and absence rules support company policy plus employee-specific configuration/override. Attendance correction requires original value, corrected value, reason, actor, approver where required, and immutable audit history.

---

## 12. Leave Management

Configurable leave types such as annual, casual, sick, unpaid, maternity, paternity and special leave.

Each type may define:

- Paid/unpaid
- Entitlement
- Joining-date-based accrual
- Carry forward
- Maximum carry forward
- Payroll impact
- Documentation requirements
- Approval workflow

Entitlement model: **Policy + Employee Override**.

Track opening, accrued, used, adjusted, carried forward and remaining balance.

Leave approval is multi-level and configurable. Payroll only consumes final approved leave.


### Leave Year / Balance Closing

Leave policies must support an explicit leave-year lifecycle.

A leave year should support:

- Start/end date
- Opening balance
- Accrued balance
- Approved usage
- Manual adjustment
- Carry-forward
- Carry-forward cap
- Expiry/lapse
- Encashment reference where separately supported
- Closing balance
- Closed/reopened status
- Policy/version used for the year

Recommended lifecycle:

```text
Open → Closing Review → Closed
```

Rules:

- Closing must calculate carry-forward/expiry using the policy version applicable to that leave year.
- Closed-year balances must not be silently changed.
- Reopening a closed leave year requires explicit permission, reason and audit.
- Negative leave balance may be allowed or blocked by configurable leave policy.
- Opening/imported balances must be identifiable separately from normal accrual.
- Closing must be idempotent; retry must not duplicate carry-forward or expiry transactions.
- Historical leave reports must remain reproducible after later policy changes.


---

## 13. Overtime

Overtime is calculated from attendance hours but payable overtime requires multi-level approval.

Support:

- Scheduled vs actual hours
- Break rules
- Holiday/weekly-off rules
- Eligibility
- Normal/weekly-off/public-holiday/special-day rates
- Fixed hourly or multiplier-based rate
- Policy rate + employee override

Suggested lifecycle:

```text
Calculated → Pending Approval → Approved/Rejected → Payroll Eligible
```

HR-only customers can use overtime tracking/approval without Payroll; approved overtime simply remains an HR record/reporting input.

---

## 14. Salary Structure — Optional Payroll Capability

Salary components must be configurable rather than hard-coded.

Examples:

### Earnings
- Basic
- House rent
- Medical
- Transport
- Food
- Attendance allowance
- Other allowance

### Variable Earnings
- Approved overtime
- Sales/performance incentive
- Festival/special bonus
- Manual earning

### Deductions
- Late
- Absence
- Unpaid leave
- Advance/loan recovery
- Tax/statutory
- Manual/other deduction

Components may support fixed amount, percentage/formula, recurring/one-time, taxable/non-taxable and active/inactive configuration.

Employee allowances are employee-wise configurable.

---

## 15. Salary Setup, Revision, Approval & History

Salary is a financially sensitive master record and must not become active merely because a user can edit salary data.

Both **initial salary setup** and **subsequent salary revisions** require controlled approval before they become effective.

### 15.1 Initial Salary Setup Approval

When Payroll is enabled and a new Employee receives an initial salary structure, the salary must follow an approval lifecycle.

Recommended lifecycle:

```text
Draft
  ↓
Pending Approval
  ↓
Approved / Rejected / Cancelled
  ↓
Active
```

Rules:

- Initial salary setup must not become Active while in Draft or Pending Approval.
- Rejected setup remains historical and must not silently become active.
- Cancelled setup remains historical where required by audit policy.
- Approval must record the approver and approval timestamp.
- The effective date must be captured.
- Only one effective salary structure may apply to the same Employee for the same effective period unless the domain explicitly supports non-overlapping component schedules.
- Payroll calculation must consume only Approved/Active salary data applicable to the payroll period.

Initial salary setup should retain at minimum:

- Employee
- Salary structure/components
- Gross/fixed salary basis where applicable
- Allowances/deductions configured at setup
- Effective date
- Reason/comment
- Requested/created by
- Requested/created at
- Submitted by/at where separate
- Approved/rejected/cancelled by
- Approved/rejected/cancelled at
- Approval/rejection/cancellation reason
- Status
- Version/concurrency metadata where appropriate

### 15.2 Salary Revision Approval

Subsequent salary changes must never overwrite the current salary master directly.

Recommended lifecycle:

```text
Current Approved Salary
        ↓
Create Salary Revision
        ↓
Draft
        ↓
Pending Approval
        ↓
Approved / Rejected / Cancelled
        ↓
Effective on Approved Effective Date
```

A Salary Revision must retain:

- Employee
- Previous salary/component snapshot
- Proposed new salary/component snapshot
- Effective date
- Reason
- Requested by/at
- Submitted by/at where applicable
- Approved/rejected/cancelled by/at
- Approval/rejection/cancellation reason
- Status

Historical salary must never be overwritten.

When an Approved revision becomes effective, the previous salary record must remain historically reproducible with its prior effective period.

### 15.3 Maker-Checker / Self-Approval Rule

Salary setup and salary revision must support maker-checker separation.

The user who creates or submits an initial salary setup or salary revision must **not approve the same request** when maker-checker enforcement is enabled.

Recommended rule for IraniMart baseline:

```text
Maker ≠ Approver
```

This must be enforced server-side, not only by hiding an Approve button.

Required permission separation:

```text
Payroll.Salary.View
Payroll.Salary.Manage
Payroll.SalarySetup.Create
Payroll.SalarySetup.Submit
Payroll.SalarySetup.Approve
Payroll.SalaryRevision.Create
Payroll.SalaryRevision.Submit
Payroll.SalaryRevision.Approve
```

If the existing permission model uses fewer/more granular action codes, use equivalent stable permissions while preserving separation of duties.

### 15.4 Reject / Cancel Behaviour

Approval workflows must support explicit rejection and cancellation.

Rules:

- Rejection requires an authorized approver.
- Rejection reason is mandatory.
- Cancellation is allowed only from configured pre-effective states.
- Cancellation reason should be mandatory.
- A Rejected or Cancelled salary request must never affect Payroll.
- Rejected/Cancelled records remain available for audit/history.
- A corrected request should normally create a new revision/version rather than silently modifying an already rejected/approved historical record.

Suggested state model:

```text
Draft
  ├── Cancelled
  ↓
Pending Approval
  ├── Rejected
  ├── Cancelled
  ↓
Approved
  ↓
Active / Scheduled for Effective Date
```

Once effective/active, correction should be performed through a new approved salary revision rather than direct mutation.

### 15.5 Effective-Date Rules

Salary setup/revision is effective-dated.

Requirements:

- Future-dated Approved salary may remain Scheduled until its effective date.
- Payroll for a historical period must use the salary version effective for that period.
- Later salary changes must not change historical payroll.
- Overlapping effective salary periods must be prevented or explicitly resolved.
- Mid-month salary/position/location changes are handled through authorized manual payroll adjustment rather than forced automatic proration, as defined elsewhere in this requirement.
- Partial-month policy remains company-configurable: calendar days, fixed 30-day basis or working days.

### 15.6 Payroll Consumption

Payroll must consume salary information only when:

```text
Salary Setup / Revision = Approved
AND
Effective Date applies to Payroll Period
AND
Record is Active/Scheduled as applicable
```

Draft, Pending Approval, Rejected and Cancelled salary records must not affect payroll calculation.

If a pending salary revision overlaps a payroll period, payroll validation should surface it as a warning or blocking validation according to configured policy.

### 15.7 Audit — Mandatory

Initial salary setup and every salary revision must be fully auditable.

Capture at minimum:

- Employee ID
- Previous salary/components
- Proposed/new salary/components
- Changed fields
- Effective date
- Status transition
- Requested/created by
- Submitted by
- Approved/rejected/cancelled by
- All relevant timestamps
- Reason/comment
- Rejection/cancellation reason
- Business/tenant/location context where applicable
- Correlation/request ID

Audit records must use the existing central audit mechanism where available and must remain immutable to normal users.

Sensitive salary values must follow existing payroll/HR field-level security and audit-masking policy.

### 15.8 Concurrency / Duplicate Protection

Protect against:

- Double approval
- Approval after rejection/cancellation
- Multiple active initial salary setups for the same effective period
- Overlapping approved salary revisions
- Concurrent edits to the same Draft revision
- Repeated submit/approve requests

Use backend validation, database constraints/versioning and idempotency/concurrency controls where appropriate.

---

## 16. Salary Advance & Employee Loan

Support salary advance and loans with:

- Amount/date
- Recovery start
- Outstanding balance
- Fixed monthly installment
- Manual monthly deduction
- Configurable schedule
- Partial repayment
- Early settlement
- Status/history

Payroll must never recover more than the outstanding balance.

---

## 17. Monthly Payroll Engine

Payroll is processed per payroll period and considers only eligible/approved inputs.

Conceptual calculation:

```text
Fixed Salary
+ Allowances
+ Approved Overtime
+ Incentives / Bonuses
+ Other Earnings
- Late / Absence / Unpaid Leave
- Loan / Advance Recovery
- Tax / Statutory Deduction
- Other Deductions
= Net Payable
```

### Payroll Snapshot — Mandatory
At payroll calculation/finalization, snapshot all inputs required to reproduce the calculation:

- Salary components/rates
- Attendance summary
- Approved leave
- Approved overtime
- Allowances
- Deductions
- Loan installment
- Tax/statutory data
- Manual adjustments
- Relevant policy/version

Later changes to HR master data must not alter historical payroll.


### Payroll Currency, Calculation Order & Rounding

Payroll arithmetic must be deterministic across calculation, payslip, export and Accounting posting.

Configure or explicitly define:

- Payroll currency
- Currency decimal precision
- Rounding mode
- Whether rounding occurs per component, per intermediate formula or only at defined totals
- Calculation/component order
- Formula dependency order
- Treatment of fractional attendance/overtime units
- Final Gross, Deduction and Net Pay rounding

Rules:

- The same authoritative calculated amounts must be used by Payroll, Payslip, Bank Export and Accounting integration.
- Frontend display rounding must not independently change persisted/accounting values.
- Historical Payroll Snapshot must retain the rounding/calculation policy/version used.
- Formula evaluation must reject circular dependencies.
- Calculation and rounding behavior requires focused unit tests for boundary values.


---

## 18. Payroll Workflow

### Payroll Period Lifecycle / Lock / Close

Payroll Period and Payroll Run are related but distinct concepts.

A Payroll Period represents the controlled processing window, e.g. October 2026. A Payroll Run contains calculated employee payroll records for that period.

Recommended Payroll Period lifecycle:

```text
Open
  ↓
Processing
  ↓
Locked
  ↓
Approved
  ↓
Posted        (when Accounting integration is enabled)
  ↓
Closed
```

If Accounting integration is disabled, `Posted` may be skipped according to configured workflow and the period may proceed from Approved/payment completion to Closed.

Requirements:

- **Open:** permitted HR/payroll inputs may still change according to normal rules.
- **Processing:** payroll calculation/recalculation is in progress.
- **Locked:** normal salary/attendance/leave/overtime/payroll inputs affecting the period can no longer be silently changed for that payroll process.
- **Approved:** payroll approval completed.
- **Posted:** Accounting posting completed where enabled.
- **Closed:** period is finalized and read-only to normal operations.
- Lock, unlock/reopen and close actions require explicit permissions.
- Reopen/unlock requires mandatory reason and immutable audit.
- A Closed period must not be mutated by normal edit APIs.
- Corrections after close must use a controlled reopen, adjustment or off-cycle payroll process according to policy.
- Prevent duplicate active payroll periods/runs for the same business/location scope and period where not explicitly allowed.
- Payroll Period status transitions must be concurrency-safe and idempotent.
- Historical reports and snapshots must remain unchanged unless a controlled correction workflow explicitly creates a new version/adjustment.
- Accounting posting and payment completion requirements for Close must be configurable and validated.

### Payroll Run Status

Recommended Payroll Run status model:

```text
Draft → Pending Approval → Approved → Payment In Progress → Paid/Completed
```

Payroll approval is single-level.

Separate permissions for prepare, edit draft, submit, approve, pay and reopen. Support maker-checker separation where configured.

Before submission validate missing salary, attendance/shift issues, pending leave/overtime, invalid payment details, negative net pay, duplicate payroll, invalid loan recovery and required configuration.

Distinguish blocking errors from warnings.

---

## 19. Payroll Reopen / Correction

Authorized users may reopen a paid/completed payroll.

Require:

- Explicit permission
- Mandatory reason
- Actor/time
- Before/after audit trail
- Re-approval before finalization

A payroll correction must not imply that an already executed external bank/cash/mobile payment has been reversed. Surface this clearly.

---

## 20. Salary Payment

Support:

- Cash
- Bank transfer
- Mobile banking
- Mixed payment
- Authorized partial payment

Track employee/payroll, payable, paid/outstanding amount, method, reference, date, actor and notes.

For mixed payments, allocations must reconcile to the settled amount.


### Payment Hold / Release

Support holding an individual employee payment without invalidating the approved payroll for other employees.

Track:

- Hold status
- Hold reason
- Held by/at
- Released by/at
- Release reason/comment

A Held payment remains outstanding and must not be marked Paid.

### Payment Batch / Failure / Return

Support payment batches for bank/mobile/export-driven processing.

Suggested payment states:

```text
Pending → Ready → Processing → Paid
             ├── Held
             ├── Failed
             └── Returned
```

Requirements:

- Failed/returned payment must retain failure/return reason and external reference where available.
- A failed/returned employee payment remains outstanding until successfully reissued, settled or otherwise authorized.
- Retrying payment must not duplicate an already-settled payment.
- Bank-file generation must retain a batch/reference to the employee payment records included.
- Regenerating a bank file must be controlled and auditable.

### Payment Reconciliation

Support reconciliation of Payroll payment records against actual Cash/Bank/Mobile settlement evidence where applicable.

Reconciliation should track:

- Payroll payment/batch
- Expected amount
- Settled amount
- Settlement date
- External/bank/mobile reference
- Difference
- Reconciliation status
- Reconciled by/at
- Notes/reason

Suggested reconciliation status:

```text
Unreconciled → Partially Reconciled → Reconciled
                           └── Exception
```

Rules:

- Reconciliation does not rewrite the original Payroll Snapshot.
- Differences/exceptions require explicit handling and audit.
- Accounting payment posting must use controlled, idempotent semantics and must not silently duplicate settlement journals.
- Payment hold, release, failure, return, retry and reconciliation are security-sensitive auditable actions.


---

## 21. Payslip & Payroll Export

Payslip supports PDF download, print and email. Initial in-system access is HR/Admin only.

Payslip includes employee, code, position/location, period, earnings, overtime, allowances, deductions, gross/net and payment information.

Payroll exports:

- Excel
- CSV
- PDF summary
- Bank payment file

Bank formats should be adapter/template based. Sensitive exports must be audited.

---

## 22. Accounting Integration

Payroll should be capable of posting to the existing Accounting module while preserving module independence.

**Do not write directly to Accounting tables from Payroll.** Use an Accounting application contract or integration event.

Recommended accounting lifecycle:

### Payroll Posting
When payroll reaches the configured posting state (recommended explicit **Posted** action after approval), create accounting instructions such as:

```text
Salary Expense                Dr
Overtime Expense              Dr
Bonus / Incentive Expense     Dr
Allowance Expense             Dr
    Employee Salary Payable       Cr
    Tax / Statutory Payable       Cr
    Loan / Advance Receivable     Cr (where applicable)
```

### Salary Payment
When payment is recorded/posted:

```text
Employee Salary Payable       Dr
    Cash / Bank / Mobile Account  Cr
```

Exact chart-of-account mapping must be configurable and use Accounting-owned account identifiers/contracts.

### Reliability / Idempotency
- A payroll posting must have a stable external reference/idempotency key.
- Retry must not create duplicate journals.
- Store Accounting journal/reference returned by Accounting.
- Reopen/correction after posting must use controlled adjustment/reversal semantics; never silently mutate an already-posted journal.
- Prefer outbox/inbox integration if posting crosses a service boundary.

### Cost Allocation
Payroll expense must support generic location/location-type/cost-centre allocation rather than being hard-coded to Outlet only.

Examples:

- Outlet payroll cost → specific outlet/cost centre
- Factory payroll cost → factory/cost centre
- Warehouse payroll cost → warehouse/cost centre
- Head Office payroll cost → head-office/admin cost centre

Multi-location employee allocation should support configurable allocation or authorized manual allocation. Historical payroll must retain the allocation used for that payroll period even if the employee later transfers to another location.

If Accounting is not enabled, Payroll must still operate and allow manual exports.

---

## 23. Employee Exit & Clearance

Support resignation, termination, contract completion, retirement and configurable exit types.

Lifecycle:

```text
Exit Initiated → Notice/Approval → Clearance → Final Settlement → Completed
```

Standard configurable clearance checklist may include assets, ID, cash/finance, inventory responsibility, loans, documents, manager, HR and Accounts clearance.

Final settlement is configurable and may include outstanding salary, overtime, allowance, bonus, leave encashment, loan/advance, unpaid leave, asset recovery and manual adjustments.

Payroll-disabled customers can complete non-financial HR exit/clearance without Payroll.

---

## 24. Authorization & Data Security

Authorization model: **Role + Location Scope + Field-Level Permission**.

Examples:

### HR
View/create/edit employee, transfer, sensitive data, bank details, documents, roster, attendance correction, leave administration, overtime administration, exit.

### Payroll
View salary, edit salary, approve revision, prepare/submit/approve/reopen payroll, process payment, export, payslip, loan administration.

### Location Scope
A manager for Location A must not automatically access employees/payroll for Location B. Scope may be granted to a specific location, multiple locations, or a location type (for example all Outlets) where the authorization model permits it.

Factory HR users may be restricted to Factory employees; Outlet managers may be restricted to their own Outlet(s). Backend/API must enforce scope; UI filtering is insufficient.

### Field-Level
Separately protect salary/history, bank account, identity data, documents, loans, tax, payroll and final settlement.

### Security
- Never log secrets, passwords, tokens or full sensitive payloads unnecessarily.
- Apply least privilege.
- Protect downloadable documents and payroll exports.
- Audit access/export of highly sensitive payroll data where appropriate.

---

## 25. Menu, Submenu, Action/Permission & Migration Requirements

Menu/navigation, action permissions and their seed/migration data are part of the deliverable, not optional UI follow-up work.

The implementation must reuse the ERP's existing Menu, Action/Permission, Role, User-Role and authorization infrastructure. Do **not** introduce a separate HR-specific permission framework if the platform already has a central one.

### 25.1 Menu / Navigation Structure

The exact route/component names should follow the existing Angular/application conventions, but the functional menu structure should include at least the following.

```text
HR
├── Dashboard
├── Employees
│   ├── Employee List
│   ├── Add / Create Employee
│   ├── Employee Details
│   ├── Employee Lifecycle / History
│   ├── Employee Documents
│   ├── Employee ↔ Login User Link
│   └── Transfers / Location Assignments
├── Employee Management
│   ├── Employees
│   ├── Departments & Teams
│   ├── Positions
│   └── Operational Locations (read-only projection)
├── Attendance
│   ├── Daily Attendance
│   ├── Raw Punches
│   ├── Attendance Corrections
│   ├── Attendance Devices
│   ├── Employee ↔ Device User Mapping
│   ├── Shifts
│   ├── Rosters
│   ├── Holidays
│   └── Weekly Off
├── Leave
│   ├── Leave Requests
│   ├── Leave Approvals
│   ├── Leave Policies / Types
│   ├── Leave Balances / Adjustments
│   └── Leave Year Closing
├── Overtime
│   ├── Overtime Records
│   ├── Overtime Approvals
│   └── Overtime Policy / Rates
├── Exit & Clearance
│   ├── Exit Cases
│   └── Clearance
├── HR Reports
├── Data Import / Opening Data
└── HR Settings
```

Payroll must remain an optional capability. When enabled, expose:

```text
Payroll
├── Dashboard
├── Salary Setup
│   ├── Salary Components
│   ├── Employee Salary Structure
│   ├── Salary Setup Approval
│   ├── Allowances / Deductions
│   └── Salary Revisions / Approval
├── Advances / Loans
├── Payroll Periods / Close
├── Payroll Runs
├── Payroll Approval
├── Salary Payments
├── Payment Batches / Reconciliation
├── Payslips
├── Payroll Exports / Bank Files
├── Accounting Posting
├── Payroll Reports
└── Payroll Settings
```

Factory master navigation belongs to the existing Administration area, not to the HR Operational Locations read-only page:

```text
Administration
├── Outlets
├── Warehouses
└── Factories
```

Factories is a lightweight HR-supporting master only; it must not expose manufacturing/production functionality.

When `Payroll.Enabled = false`:

- Payroll parent menu must not be shown.
- Payroll child menus/routes must not be shown.
- Payroll APIs must still enforce capability checks server-side.
- HR-only users must not require Payroll permissions or Payroll seed data to use HR.
- Hidden navigation alone is not security.

When `Attendance.Enabled = false` or `DeviceIntegration.Enabled = false`, corresponding menus/routes must follow capability configuration while backend authorization remains enforced.

### 25.2 Menu Behaviour

Menus must be controlled by both:

1. **Feature/capability availability**, and
2. **User authorization/permission**

Recommended decision:

```text
Feature Enabled?
    +
User Has Required Menu/Action Permission?
    +
User Has Required Location/Field Scope?
        ↓
Show/Allow
```

A menu should not appear merely because the feature exists. Conversely, hiding a menu must never replace API authorization.

Menu ordering, parent/child relationships, icon metadata, routes, component keys and other navigation metadata should follow the existing system schema and conventions.

### 25.3 Required HR Action / Permission Catalogue

Final permission names may be adapted to the existing naming convention, but stable permission codes must exist for equivalent capabilities.

Suggested baseline:

```text
HR.Dashboard.View

HR.Employee.View
HR.Employee.Create
HR.Employee.Edit
HR.Employee.Deactivate
HR.Employee.ViewSensitive
HR.Employee.LinkUser
HR.Employee.UnlinkUser
HR.EmployeeLifecycle.View
HR.EmployeeLifecycle.Manage
HR.EmployeeLifecycle.Rehire

HR.EmployeeDocument.View
HR.EmployeeDocument.Upload
HR.EmployeeDocument.Replace
HR.EmployeeDocument.Delete

HR.Organisation.View
HR.Organisation.Manage
HR.Position.View
HR.Position.Manage
HR.Location.View
HR.Location.Manage
HR.Factory.View
HR.Factory.Manage

HR.EmployeeTransfer.View
HR.EmployeeTransfer.Create
HR.EmployeeTransfer.Approve

HR.Shift.View
HR.Shift.Manage
HR.Roster.View
HR.Roster.Manage
HR.Holiday.View
HR.Holiday.Manage
HR.WeeklyOff.View
HR.WeeklyOff.Manage

HR.Attendance.View
HR.Attendance.ManualEntry
HR.Attendance.Correct
HR.Attendance.ApproveCorrection
HR.Attendance.RawPunch.View
HR.Attendance.Reprocess

HR.Device.View
HR.Device.Manage
HR.DeviceUserMapping.View
HR.DeviceUserMapping.Manage

HR.Leave.View
HR.Leave.Request
HR.Leave.Approve
HR.Leave.Admin
HR.LeaveBalance.View
HR.LeaveBalance.Adjust
HR.LeaveYear.Close
HR.LeaveYear.Reopen

HR.Overtime.View
HR.Overtime.Approve
HR.Overtime.Override
HR.OvertimePolicy.Manage

HR.Exit.View
HR.Exit.Initiate
HR.Exit.Approve
HR.Exit.Clearance

HR.Import.View
HR.Import.Execute
HR.ApprovalPolicy.Manage
HR.Report.View
HR.Settings.Manage
```

### 25.4 Required Payroll Action / Permission Catalogue

Suggested baseline:

```text
Payroll.Dashboard.View

Payroll.Salary.View
Payroll.Salary.Manage
Payroll.SalarySetup.Create
Payroll.SalarySetup.Submit
Payroll.SalarySetup.Approve
Payroll.SalaryRevision.Create
Payroll.SalaryRevision.Submit
Payroll.SalaryRevision.Approve

Payroll.AllowanceDeduction.View
Payroll.AllowanceDeduction.Manage

Payroll.Loan.View
Payroll.Loan.Manage

Payroll.Run.View
Payroll.Run.Create
Payroll.Run.Calculate
Payroll.Run.EditDraft
Payroll.Run.Submit
Payroll.Run.Approve
Payroll.Run.Reopen

Payroll.Period.View
Payroll.Period.Lock
Payroll.Period.Reopen
Payroll.Period.Close

Payroll.Payment.View
Payroll.Payment.Process
Payroll.Payment.Hold
Payroll.Payment.Release
Payroll.Payment.Retry
Payroll.Payment.Reconcile

Payroll.Payslip.View
Payroll.Payslip.Email

Payroll.Export
Payroll.BankFile.Generate

Payroll.Accounting.Post
Payroll.Accounting.ViewPosting

Payroll.Import.View
Payroll.Import.Execute
Payroll.ApprovalPolicy.Manage
Payroll.Report.View
Payroll.Settings.Manage
```

Where the existing authorization system distinguishes menu permission, API action permission and field/data permission, preserve those distinctions rather than collapsing them into one flag.

### 25.5 Permission Scope

Permission evaluation must continue to work with:

- Role
- User-specific grants/overrides if already supported
- Location scope
- Multiple-location scope
- Location-type scope where supported
- Field-level sensitivity
- Feature/capability flags

Examples:

```text
HR.Employee.View
+ AllowedLocation = Outlet - Uttara
→ may view permitted Uttara employees only
```

```text
Payroll.Salary.View
+ AllowedLocationType = Factory
→ may view salary for Factory-scoped employees only
```

A user may have `HR.Employee.View` without `HR.Employee.Edit`.

A user may have `Payroll.Run.View` without `Payroll.Run.Approve`.

### 25.6 Default Role / Role Mapping

The implementation should define and seed role mappings only where the existing ERP's role model expects seeded defaults.

Potential baseline roles may include:

```text
HR Manager
HR Officer
Attendance Manager
Payroll Manager
Payroll Approver
Location / Outlet Manager
Factory HR / Attendance User
```

However:

- Do not create duplicate roles if equivalent roles already exist.
- Existing central roles should be extended through permission mapping where appropriate.
- Super Admin behavior must follow the current Identity authorization model.
- Default role permission assignment must be explicit and reviewable.
- Payroll permissions must not be automatically granted to HR-only roles.
- Location Manager permissions must remain location-scoped.

If the platform uses a role such as `Admin` or another pre-existing business administrator role, reuse/extend it only according to current project conventions and approved access rules.

### 25.7 Migration / Seed Requirements

All required database changes and reference-data seeds must be delivered through the project's normal migration/seed mechanism.

The implementation must add, where applicable:

- HR parent menu
- HR submenus
- Payroll parent menu
- Payroll submenus
- Stable permission/action records
- Menu ↔ permission/action mappings
- Route/component metadata if persisted by the existing platform
- Feature/capability mappings if persisted
- Approved default role ↔ permission mappings
- Required HR/Payroll lookup/reference data
- New HR/Payroll domain tables/schema
- Constraints/indexes required by the module

Migration/seed implementation requirements:

- Follow the existing migration and seed architecture.
- Do not manually patch production databases.
- Use stable codes/keys for menu and permission records.
- Avoid environment-specific hard-coded numeric IDs where existing conventions provide stable keys.
- Be idempotent or duplicate-safe according to the project's established seeding pattern.
- Re-running deployment/seed must not create duplicate menus, permissions or role mappings.
- Do not rename/reuse an existing unrelated permission code.
- Do not delete existing menu/permission records belonging to other modules.
- Preserve historical/reference integrity when a menu or permission is deactivated.
- Any destructive migration requires explicit review.
- Migration ordering must respect existing Identity/Menu/Permission dependencies.
- HR-only deployment must not require Payroll data initialization beyond what is technically required for an optional disabled capability.
- Roll-forward deployment must be supported; rollback strategy should follow existing project practice.

### 25.8 Migration Validation

Every implementation phase/slice that changes persistence or seeded authorization/navigation data must verify:

```text
EF / migration model status
Database migration applies successfully
No unexpected model drift
Required HR menus exist exactly once
Required Payroll menus exist exactly once when seeded
Required action/permission codes exist exactly once
Menu ↔ permission mappings are correct
Default role mappings are correct
Disabled capabilities hide their menus
Unauthorized users cannot call protected APIs
Authorized users can access the corresponding routes/actions
Location scope is still enforced
Approval policy/reference seed is duplicate-safe where seeded
Payroll period/payment reconciliation schema changes apply successfully
Import/opening-data constraints and indexes are present where introduced
```

Where the current project has automated migration, permission or OpenAPI tests, extend them rather than relying only on manual verification.

### 25.9 Menu / Permission Auditability

Administrative changes to role/permission mappings should continue to use the existing central Identity/Audit mechanism.

At minimum, where supported by the current platform, retain:

- Who changed role/permission access
- What permission/menu access changed
- Target role/user
- Before/after state
- Timestamp
- Business/location context where applicable
- Reason/comment where required

Do not create a parallel HR-only authorization audit store if central Identity/Audit already supports this.

### 25.10 Definition of Done for UI Authorization

A backend feature is **not considered complete** merely because its API and page exist.

For a user-facing HR/Payroll slice, Definition of Done includes, where applicable:

```text
Domain/Application implementation
API endpoint
Frontend route/page
Parent menu/submenu
Action/permission seed
Menu ↔ permission mapping
Feature/capability guard
Backend authorization
Location/field-level authorization
Role mapping where approved
Audit coverage
Approval workflow integration where applicable
Effective-dated/history handling where applicable
Migration/seed
Automated tests
Release build
Migration/model validation
```

Missing menu/action/permission migration is therefore an incomplete feature, not a post-release configuration task.


---

## 26. Audit Log — Mandatory

Audit logging is a core requirement across HR & Payroll.

For important actions capture:

- Actor user ID/name reference
- Timestamp
- Business/tenant/location context
- Action/operation
- Entity type and ID
- Human-readable action summary
- Previous values
- New values
- Changed fields
- Reason/comment where required
- Correlation/request ID
- Source/channel where useful

Critical areas include employee changes and lifecycle transitions, location transfer, documents, salary changes, attendance corrections, leave adjustments/approval/year closing, overtime override/approval, loan changes, approval-policy decisions, payroll period lock/reopen/close, payroll adjustments, approvals, reopening, imports/opening data, accounting posting, payment hold/release/failure/return/reconciliation, exit and final settlement.

Audit records must be immutable to normal application users.

Do not audit sensitive values blindly. Mask/exclude passwords, authentication secrets and other protected fields. Sensitive HR values should follow the existing audit/security policy.

Where the ERP already has a central Audit Log mechanism, integrate through an abstraction/event rather than implementing an incompatible second audit system.

---

## 27. Notifications & Reminders

Support reminders/notifications for document/contract expiry, probation completion, pending leave/overtime, missing attendance, payroll approval, loan recovery, exit clearance and other configurable HR events.

Notification delivery must be infrastructure-driven and replaceable.

---

## 28. Dashboard & Reporting

### HR Dashboard
- Headcount
- Active/new/probation/leaving employees
- Headcount by location, location type and department (including Outlet vs Factory breakdown)
- Vacant positions
- Present/absent/late/on leave
- Missing attendance
- Overtime
- Expiring documents/contracts
- Pending approvals/clearance

### Payroll Dashboard (only when Payroll enabled)
- Gross payroll
- Net payroll
- Allowances/overtime/deductions
- Payroll cost by location, location type and department
- Outstanding payments
- Payroll status

### Reports
Employee master/status/location/location-type/department/position/joining/transfer/lifecycle history/salary history/document expiry; Outlet-vs-Factory headcount and movement; attendance/late/absence/missing punch/hours/adjustments by location/location type; leave balance/history/unpaid leave/leave-year close; overtime hours/cost; payroll period status/register/detail/earnings/deductions/tax/net/payment/outstanding/payment exception/reconciliation/cost by location/location type; loans/advances; exit/turnover/clearance/final settlement.

Reports must respect capability, permission, location and field-level security.

---

## 29. Historical Integrity & Concurrency

Historical HR/payroll transactions must remain reproducible.

Prevent accidental duplicates/concurrent corruption including:

- Duplicate payroll for employee/period
- Duplicate device punch processing
- Duplicate attendance import
- Duplicate loan recovery
- Duplicate approval execution
- Duplicate accounting posting
- Duplicate payment processing

Use database constraints, optimistic concurrency/versioning and idempotency where appropriate—not only UI checks.


### Effective-Dated Policy Versioning

Rules that can affect historical HR/Payroll outcomes must be effective-dated or versioned where appropriate.

This includes, at minimum:

- Attendance late/absence rules
- Shift/roster policies where policy-driven
- Holiday/weekly-off policy
- Leave entitlement/accrual/carry-forward rules
- Overtime eligibility/rate rules
- Payroll partial-month policy
- Payroll calculation/rounding policy
- Approval policies where changes must not reinterpret historical decisions

Requirements:

- Do not mutate an already-used historical policy version in a way that changes historical results.
- New policy changes should create a new version/effective period.
- Transactions/snapshots should retain the policy/version needed to reproduce the historical decision/calculation.
- Prevent ambiguous overlapping active policy versions for the same scope unless precedence is explicitly defined.
- Policy activation/deactivation must be audited.


---

## 30. Data Ownership / Persistence

HR & Payroll owns its domain data. Prefer a dedicated schema/DbContext boundary within the current deployment so extraction to a separate database is feasible later.

Examples of logical aggregates/data areas:

- Organisation / Location / Location Type / Position
- Approval Policy / Approval Instance / Approval Step History
- Employee Lifecycle / Employment Period History
- Employee / Employment
- Employee / Identity User Link
- Employee Location Assignment
- Employee Document
- Shift / Roster
- Holiday / Weekly Off
- Attendance Device / Device User Mapping / Raw Punch
- Attendance / Attendance Adjustment
- Leave Policy / Entitlement / Request / Leave Year Close
- Overtime
- Salary Structure / Revision
- Loan / Advance
- Payroll Period / Payroll Run / Employee Payroll / Payroll Snapshot / Adjustment
- Payroll Payment / Payment Batch / Payment Reconciliation
- Import Batch / Import Row Result / Opening Data Import
- Effective-Dated Policy Version
- Exit / Clearance / Final Settlement
- Integration state/outbox

Do not expose EF entities as public API contracts.


### Bulk Import / Opening Data Migration

The HR/Payroll product must support controlled onboarding of existing business data without bypassing domain validation.

Supported import areas should include, as applicable:

- Employees
- Location/position/department assignments
- Employee ↔ Login User reference mapping where safely resolvable
- Initial salary setup
- Leave opening balances
- Existing loan/advance opening balances
- Historical/current assignment data required for go-live

Import workflow should support:

```text
Upload / Prepare
    ↓
Dry-Run Validation
    ↓
Review Errors / Warnings
    ↓
Authorized Execute
    ↓
Import Batch Result
```

Requirements:

- Use versioned templates/contracts.
- Validate every row and provide row-level error/warning output.
- Do not directly bypass Application/Domain rules through raw table inserts.
- Import must respect permissions and location/business scope.
- Import batches require stable IDs and audit.
- Re-running the same import must be duplicate-safe/idempotent where a stable external/import key is supplied.
- Support partial success only when explicitly designed; otherwise fail atomically for the relevant import scope.
- Opening balances must be distinguishable from organically generated transactions.
- Sensitive salary/bank/identity data in import files must follow normal security controls.
- Import files/results must have an explicit retention/access policy.
- Production import should support a dry-run before commit.


---

## 31. CQRS / Application Use Cases

Follow existing CQRS/MediatR conventions. Commands mutate state; queries return read models optimized for UI/reporting.

Examples:

```text
CreateEmployeeCommand
TransitionEmployeeLifecycleCommand
RehireEmployeeCommand
LinkEmployeeUserCommand
UnlinkEmployeeUserCommand
GetEmployeeForCurrentUserQuery
AssignEmployeeLocationCommand
ApproveEmployeeTransferCommand
CreateRosterCommand
ImportRawPunchCommand / ReceiveDevicePunchCommand
ReprocessAttendanceCommand
CorrectAttendanceCommand
RequestLeaveCommand
ApproveLeaveCommand
CloseLeaveYearCommand
ReopenLeaveYearCommand
ApproveOvertimeCommand
CreateInitialSalarySetupCommand
SubmitInitialSalarySetupCommand
ApproveInitialSalarySetupCommand
RejectInitialSalarySetupCommand
CancelInitialSalarySetupCommand
ReviseSalaryCommand
SubmitSalaryRevisionCommand
ApproveSalaryRevisionCommand
RejectSalaryRevisionCommand
CancelSalaryRevisionCommand
CreatePayrollPeriodCommand
LockPayrollPeriodCommand
ReopenPayrollPeriodCommand
ClosePayrollPeriodCommand
CreatePayrollRunCommand
CalculatePayrollCommand
SubmitPayrollCommand
ApprovePayrollCommand
PostPayrollToAccountingCommand
RecordPayrollPaymentCommand
HoldPayrollPaymentCommand
ReleasePayrollPaymentCommand
RetryPayrollPaymentCommand
ReconcilePayrollPaymentBatchCommand
ReopenPayrollCommand
ValidateHrOpeningImportCommand
ExecuteHrOpeningImportCommand
InitiateEmployeeExitCommand
CompleteClearanceItemCommand
CompleteFinalSettlementCommand
```

Handlers should orchestrate domain behavior and ports, not contain infrastructure-specific code.

---

## 32. Integration Contracts

Define ports/contracts for replaceable dependencies, e.g.:

```text
IIdentityUserDirectory / IIdentityUserReferenceResolver
IApprovalActorResolver / IApprovalPolicyProvider
IAttendanceDeviceAdapter / IAttendancePunchReceiver
IDocumentStorage
IEmailSender
IPayrollExportGenerator
IBankPaymentFileGenerator
IImportFileParser / IImportStorage
IAccountingIntegration
IAuditWriter (or existing central audit contract)
INotificationPublisher
IClock
ICurrentUser / IRequestContext
```

Names should align with existing solution conventions.

---

## 33. Testing Requirements

Testing is mandatory for each slice.

### Domain Unit Tests
Cover payroll calculation/rounding, leave accrual/year-close, overtime, attendance rules, loan recovery, employee lifecycle, approval sequencing, payroll-period transitions, payment states, policy versioning, precedence and edge cases.

### Application Tests
Cover commands/queries, validation, authorization, idempotency and feature/capability behavior.

### Integration Tests
Cover EF persistence, APIs, location/field permissions, device punch ingestion, outbox/inbox, accounting integration and concurrency.

### Regression Tests
Existing modules and HR-only mode must remain functional when Payroll is enabled/disabled.

### Key Scenarios
- Reusable approval workflow enforces configured step order and stores immutable decision history.
- Self-approval restrictions are enforced server-side where configured.
- Employee promotion/manager/department/status changes preserve effective-dated history.
- Rehire creates a new employment period without erasing the prior exit.
- Leave year close creates carry-forward/expiry exactly once and closed years are read-only.
- Reopening a closed leave year requires permission, reason and audit.
- Payroll Period cannot be closed while configured blocking conditions remain.
- Locked/Closed Payroll Period rejects normal mutation attempts.
- Reopening/unlocking a Payroll Period requires permission, reason and audit.
- Closed-period correction uses controlled reopen/adjustment/off-cycle behavior rather than silent mutation.
- Payroll rounding produces the same authoritative amounts for payroll, payslip, export and accounting.
- Historical policy/version changes do not alter historical attendance/leave/overtime/payroll outcomes.
- Held salary payment remains outstanding and is excluded from settled totals.
- Failed/returned payment can be safely retried without duplicate settlement.
- Payment reconciliation identifies partial/difference/exception states without changing the payroll snapshot.
- Bulk import dry-run returns row-level validation without persisting data.
- Re-running an import with stable import keys does not duplicate employees/salary/opening balances.
- Initial salary setup cannot become active before approval.
- Draft/Pending/Rejected/Cancelled salary records do not affect payroll.
- Salary revision preserves previous salary history.
- Maker cannot approve the same salary setup/revision when maker-checker is enabled.
- Rejection requires an approver and reason.
- Cancellation does not affect payroll and remains auditable.
- Future-dated approved salary becomes applicable only from its effective date.
- Overlapping approved salary periods are prevented or deterministically resolved.
- Duplicate/repeated salary approval requests do not cause double activation.
- Required HR parent/submenus are seeded exactly once.
- Payroll menus are hidden when Payroll is disabled.
- Payroll APIs reject access when Payroll capability is disabled even if a stale route is called directly.
- Required permission/action codes are seeded exactly once.
- Menu ↔ permission mappings match the intended actions.
- Default role ↔ permission mappings match approved access.
- User with view-only permission cannot execute edit/approve actions.
- Location-scoped user sees only permitted location data.
- Employee position/title does not grant permissions automatically.
- Seed/migration rerun does not create duplicate menus, permissions or role mappings.
- Migration/model validation reports no unexpected model changes after the intended migration.
- HR works with Payroll disabled.
- Employee can exist without a Login User.
- Login User can exist without an Employee.
- One Employee cannot have multiple active Login User links.
- One Login User cannot have multiple active Employee links.
- Link/unlink/relink operations are permission-controlled and audited.
- Disabling a Login User does not remove Employee history.
- Deactivating/exiting an Employee does not automatically delete the Identity User.
- Employee position/title does not implicitly grant permissions.
- Payroll cannot be accessed when disabled.
- Device duplicate punch does not duplicate attendance.
- Offline/delayed punch can be reprocessed.
- Historical payroll is unchanged by later salary/policy changes.
- Repeated Accounting posting does not duplicate journal.
- Location-scoped user cannot access another location.
- Outlet-scoped user cannot access Factory employees unless explicitly permitted.
- Factory-scoped user cannot access unrelated Outlet employees unless explicitly permitted.
- Employee transfer between Outlet/Factory preserves effective-dated history.
- Historical attendance/payroll continues to report the correct historical location after transfer.
- Field-level permission hides salary/bank data.
- Reopened payroll retains before/after audit history.

Every implementation slice should pass Release build, relevant backend tests, frontend tests/build, migration/model validation and `git diff --check` before acceptance.

---

## 34. Recommended Delivery Phases

### Phase 1 — HR Foundation
Departments & Teams (`OrganisationUnit`), reusable Positions, HR Operational Location projection, Outlet/Warehouse source integration, lightweight HR-owned Factory master with projection synchronization, employee master, employment types, effective-dated employee lifecycle/history, optional Employee ↔ Login User mapping, multiple locations/transfers, documents, controlled employee/opening-data import foundation, HR menu/submenu/action-permission seeds, approved role mappings, migrations, permissions and audit foundation. Head Office remains Business scope unless/until a real authoritative location source is approved.

### Phase 2 — Shift, Roster & Attendance
Shift/roster, holidays/off-days, versioned attendance-related policies, manual attendance, device master, employee-device mapping, PUSH/ADMS integration framework, raw punch ingestion, attendance processing/corrections and core attendance reports.

### Phase 3 — Leave, Overtime & Complete HR Operations
Leave policies/accrual/employee overrides, leave-year close/reopen, multi-level leave approval, overtime calculation/rates and multi-level overtime approval, non-financial employee exit/clearance, reusable approval framework coverage, and core HR operational reports required for HR-only customers.

**At this point HR-only customers have a complete usable product, including non-financial exit/clearance and core operational reporting.**

### Phase 4 — Payroll Foundation
Salary components/structures, initial salary setup approval, salary revision approval/history, maker-checker and self-approval prevention, reject/cancel workflow, effective-dated payroll policies, allowances/deductions, partial-month policy, manual adjustments, loans/advances, salary/opening payroll data import, Payroll menu/submenu/action-permission seeds and capability-controlled navigation.

### Phase 5 — Payroll Processing & Period Control
Payroll Period lifecycle/lock/reopen/close, payroll engine/snapshot, deterministic calculation order and rounding, validations, single-level Payroll Run approval, controlled reopen/correction/off-cycle handling and payroll audit.

### Phase 6 — Payment, Payslip & Accounting
Cash/bank/mobile/mixed/partial payment, payment hold/release, payment batches, failure/return/retry, reconciliation, payslip/email, exports/bank files, Accounting posting/payment integration and idempotency.

### Phase 7 — Payroll-Backed Final Settlement
Payroll-backed employee final settlement, financial clearance completion, outstanding salary/overtime/allowance/bonus/leave encashment/loan/asset recovery adjustments and Accounting integration where enabled. Non-financial exit/clearance already exists from Phase 3.

### Phase 8 — Advanced Reporting & Dashboard
Full HR and Payroll dashboards, advanced analytics, management reporting, payment/reconciliation analytics and extended report library beyond the core operational reports delivered earlier.

---

## 35. Acceptance Criteria Summary

The solution is acceptable when:

1. Approval-driven processes use the reusable/existing approval framework with immutable step history and server-side sequencing.
2. Employee lifecycle changes are effective-dated and preserve prior employment/position/department/manager/status history.
3. Rehire preserves the prior employment/exit history and creates a new employment period.
4. Leave-year close/reopen, carry-forward and expiry are controlled, idempotent and auditable.
5. Payroll Period supports Open/Processing/Locked/Approved/Posted/Closed control with permissioned reopen/unlock/close.
6. Closed/locked periods cannot be silently mutated; corrections use controlled reopen, adjustment or off-cycle semantics.
7. Payroll calculation order, currency precision and rounding are deterministic across Payroll, Payslip, Export and Accounting.
8. Effective-dated policy versions preserve historical outcomes.
9. Salary payment supports hold/release, batch processing, failure/return/retry and reconciliation without duplicate settlement.
10. Bulk/opening-data import supports dry-run, row-level validation, permission checks, audit and duplicate-safe execution.
11. Phase 3 delivers a complete HR-only product including non-financial exit/clearance and core operational reporting.

12. Initial salary setup requires approval before becoming active.
13. Salary revisions require approval and preserve previous salary history.
14. Draft, Pending, Rejected and Cancelled salary records never affect payroll.
15. Maker-checker separation prevents a maker from approving the same salary request when enabled.
16. Reject/cancel actions are permission-controlled, reasoned and audited.
17. Salary effective dates are respected and overlapping active salary periods are prevented or deterministically resolved.

18. Required HR menus and submenus are delivered through the existing menu architecture.
19. Payroll menus are capability-controlled and are not exposed when Payroll is disabled.
20. Required HR and Payroll action/permission codes are seeded through the normal migration/seed mechanism.
21. Menu ↔ permission mappings and approved role ↔ permission mappings are complete and duplicate-safe.
22. Menu visibility never replaces server-side authorization.
23. Migration/seed reruns do not duplicate menu, action, permission or role-mapping records.
24. User-facing slices are not accepted if required menu/action/permission migration is missing.

25. Employee and Login User remain separate concepts with an optional one-to-one link.
26. Employee records can exist without Login Users and Login Users can exist without Employee records.
27. Employee/User linking does not create a hard HR-domain dependency on Identity persistence.
28. Link/unlink/relink operations are permission-controlled, historical and audited.
29. Employee position/department/location does not implicitly grant application permissions.

30. HR operates completely without Payroll enabled.
31. Payroll can be enabled without changing HR data model/workflows unnecessarily.
32. HR & Payroll remains an isolated Clean Architecture bounded context.
33. The module can later be extracted to a microservice without rewriting its domain/application layers.
34. Departments & Teams, reusable Position management and one HR Operational Location projection are supported.
35. Outlet and Warehouse remain source-owned; the lightweight HR-owned Factory master synchronizes into the same Operational Location projection; employees use the same assignment model across Outlet/Factory/Warehouse without separate employee entity types. Head Office is not fabricated as a location when Business scope is sufficient.
36. Employees support multiple effective-dated locations and transfer approval/history, including transfer between different location types.
37. Full employee document management and expiry tracking exist.
38. Rotating roster, holiday and weekly-off rules work deterministically.
39. Attendance accepts manual and device/external sources.
40. PUSH/ADMS-style devices can deliver raw punches through replaceable adapters.
41. Raw punches are immutable, idempotent and separately processed into attendance.
42. Late/absence rules support employee overrides.
43. Leave supports joining-date accrual, policy override and multi-level approval.
44. Overtime derives from attendance and requires multi-level approval before Payroll eligibility.
45. Salary components/allowances are configurable.
46. Salary revisions are effective-dated, approved and historical.
47. Payroll snapshots all calculation inputs.
48. Payroll supports attendance, leave, overtime, incentives, deductions, tax, loans and manual adjustments.
49. Payroll uses single-level approval and controlled reopen.
50. Payment supports cash, bank, mobile and mixed allocations.
51. Payslip and Excel/CSV/PDF/bank exports exist.
52. Payroll can post to Accounting through an explicit integration contract/event, not table writes.
53. Accounting posting/payment is idempotent and traceable.
54. Accounting-disabled deployments can still run Payroll using exports.
55. Role + location + field-level security is enforced server-side.
56. Critical HR/payroll operations have immutable, meaningful audit history.
57. Historical payroll remains reproducible.
58. Duplicate/concurrent operations are protected at backend/database level.
59. HR and Payroll menus/APIs/reports respect feature enablement.
60. Tests cover HR-only, Payroll-enabled, device integration, accounting integration, permissions, audit and historical integrity.

---
## 36. Location Architecture Constraints

The following rules are mandatory:

1. Do **not** introduce `OutletEmployee`, `FactoryEmployee`, `WarehouseEmployee` or similar parallel employee aggregates.
2. Do **not** hard-code attendance/payroll logic to Outlet only.
3. HR uses a unified `OperationalLocationProjection` as the workplace reference for employee assignments, attendance, devices, payroll snapshots and reports.
4. Outlet and Warehouse remain authoritative in their existing modules; HR does not create competing Outlet/Warehouse masters.
5. Factory is an approved exception because it is required only for HR workforce-location use: the HR bounded context owns a lightweight Factory master and synchronizes it into `OperationalLocationProjection`.
6. Factory must remain a distinct source type and must never be represented as Warehouse.
7. Apply location rules through source type, projected location, policy/configuration and effective-dated employee assignment.
8. Devices belong to an Operational Location projection; device events inherit operational context from that location.
9. Authorization must support location-scoped access without assuming all locations are Outlets.
10. Reports and dashboards must be filterable by projected location and source/location type where supported.
11. Payroll/accounting allocation must support Outlet, Factory and Warehouse; Head Office uses Business scope unless a real location source is later approved.
12. Historical records must retain or snapshot the effective location context applicable at the time of the transaction.
13. New operational-location types require an explicit authoritative source/ownership decision, but must not require redesigning the Employee aggregate.

---

## 37. Explicit Out-of-Scope / Future Extensions

Unless separately approved:

- Employee self-service portal/mobile app (Identity ↔ Employee linkage foundation is in scope; self-service functionality itself is future scope)
- Recruitment/ATS
- Performance appraisal
- Training/LMS
- Expense reimbursement
- Direct bank API payment execution
- Direct mobile-banking API execution
- Advanced workforce optimization
- Government tax filing automation

The architecture should permit these later without coupling them into the initial domain.

---

## 38. Overall Operational Flow

### HR Core

```text
Departments & Teams / Position / Factory Master
        ↓
Operational Location Projection (Outlet / Warehouse / Factory)
        ↓
Employee / Lifecycle History / Optional Login User Link / Location Assignment / Documents
        ↓
Shift / Roster
        ↓
Device Punch / Manual Attendance
        ↓
Raw Punch Processing
        ↓
Attendance
        ↓
Leave / Overtime / Leave-Year Close
        ↓
Non-Financial Exit / Clearance / Core HR Reports
```

### Optional Payroll

```text
Approved HR Inputs
        +
Salary Structure / Loan / Adjustments
        ↓
Payroll Period Open / Processing
        ↓
Payroll Calculation + Snapshot + Rounding Policy
        ↓
Validation
        ↓
Period Lock + Single-Level Payroll Approval
        ↓
Explicit Accounting Posting (when enabled)
        ↓
Payment / Hold / Batch / Retry
        ↓
Payment Reconciliation
        ↓
Accounting Payment Posting (when enabled)
        ↓
Period Close
        ↓
Payslip / Export / Completion
```

The design goal is a **complete HR product on its own**, an **optional Payroll product layered on HR**, and an **HR & Payroll bounded context that remains clean enough to extract into an independent service later**.