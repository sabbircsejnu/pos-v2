# Audit Log System — Implementation Specification

Status: Draft v1
Owner: Backend (RetailPOS.API / RetailPOS.Infrastructure) + Frontend (retailpos-frontend)
Replaces: legacy single-table `audit_logs` ([AuditLog.cs](../src/RetailPOS.Core/Entities/AuditLog.cs)) — kept read-only during transition, then dropped.

---

## 1. Overview

This document specifies a production-grade audit logging subsystem for the multi-outlet Retail POS. Every meaningful user or system action — from a login to a sales-order approval that touches five tables — must be captured as a single, immutable, queryable audit event with full who/what/where/when/before/after context.

The system is built around three normalized tables:
- **`audit_events`** — one row per business action (login, sale created, role updated…).
- **`audit_event_entities`** — one row per database entity touched by that action (a single sale-approval may produce 4–5 entity rows: SalesOrder, StockLedger, Invoice, CustomerBalance, ActivityLog).
- **`audit_event_field_changes`** — one row per field that actually changed on each touched entity, with old/new values stored as JSON.

A `correlation_id` (GUID) groups all rows produced by one logical action, even when execution spans services, jobs, or asynchronous handlers.

## 2. Goals

- Capture **every** state-mutating action in the system: HTTP write requests, background jobs, imports, integrations, login/logout, role switches, permission changes, exports.
- Record **who really acted** (the authenticated user) plus **the role/outlet they were acting as** if they switched.
- Be **complete enough for forensics**: IP, device, browser, OS, request method, request path, status, error.
- Be **immutable**: no UPDATE/DELETE from the application, enforced at DB and API layers. Records can only be INSERTed.
- Be **fast to read** for the Audit Log UI under realistic data volumes (millions of rows over months).
- Be **tamper-evident enough for trust**: append-only, system clock UTC, optional hash chain (out of scope for v1, hooks left in metadata column).
- Be **safe** with sensitive data — passwords, tokens, card numbers, secrets, API keys are never written; they are masked at the source before reaching the audit pipeline.

## 3. Non-Goals (v1)

- Cryptographic hash-chain / blockchain-style proof of integrity (slot reserved in `metadata` for v2).
- Streaming audit events to a SIEM (the table itself is the source of truth; SIEM forwarder is a future component).
- Retention policy / archival to cold storage. v1 keeps everything in `audit_events*`. Retention policy will be added once we have realistic growth numbers.
- Read-only "audit user" view of business records (the Audit Log page lets you see what changed, not "rewind to that point in time").
- GDPR right-to-erasure tooling for audit rows (audit immutability vs. erasure is a policy decision deferred to legal).

## 4. Data Model

### 4.1 `audit_events`

Header row. One per business action.

| column                | type           | notes                                                                                    |
|-----------------------|----------------|------------------------------------------------------------------------------------------|
| `id`                  | bigserial PK   |                                                                                          |
| `correlation_id`      | uuid NOT NULL  | groups multi-step actions; indexed                                                       |
| `business_id`         | bigint NULL    | for future multi-tenant; nullable in v1                                                  |
| `outlet_id`           | bigint NULL    | acting outlet (sale's outlet, transfer source, etc.); indexed                            |
| `real_user_id`        | bigint NULL    | the *authenticated* user; NULL only for system jobs                                      |
| `acting_user_id`      | bigint NULL    | filled when an admin impersonates / acts on behalf of someone                            |
| `real_role_id`        | bigint NULL    | role of the authenticated user                                                           |
| `acting_role_id`      | bigint NULL    | role they were acting as if switched                                                     |
| `action_type`         | varchar(40)    | enum-like: `Create`, `Update`, `Delete`, `Approve`, `Cancel`, `Login`, `Logout`, `RoleSwitch`, `Export`, `Import`, `Payment`, `Adjustment`, `PermissionChange`, `Reverse`, `Hold`, `Resume`, `Print` |
| `action_summary`      | varchar(500)   | human-readable, e.g. `"Approved Sale #SALE-2026-0042 ($1,240.00)"`                       |
| `module`              | varchar(40)    | `Sales`, `Purchase`, `Inventory`, `Accounts`, `Admin`, `Settings`, `Reports`, `Auth`, `System` |
| `source`              | varchar(20)    | `UI`, `API`, `SystemJob`, `Import`, `Integration`                                        |
| `primary_entity_type` | varchar(80)    | e.g. `Sale`, `User`, `Inventory`                                                         |
| `primary_entity_id`   | bigint NULL    | nullable for actions that don't have one (e.g. report export for a date range)           |
| `request_method`      | varchar(10)    | `GET`, `POST`, …; NULL for jobs                                                          |
| `request_path`        | varchar(500)   | sanitized URL path; NULL for jobs                                                        |
| `ip_address`          | inet           | postgres `inet`; NULL for jobs                                                           |
| `user_agent`          | varchar(500)   |                                                                                          |
| `device_name`         | varchar(120)   | parsed from UA                                                                           |
| `browser`             | varchar(60)    | parsed                                                                                   |
| `os`                  | varchar(60)    | parsed                                                                                   |
| `status`              | varchar(10)    | `Success` or `Failed`                                                                    |
| `error_message`       | text NULL      |                                                                                          |
| `metadata`            | jsonb NULL     | open-ended bag (extra HTTP query params, custom keys, future hash chain)                 |
| `created_at`          | timestamptz    | DB-default `now()`                                                                       |

**Indexes:**
- `(created_at DESC)` — primary listing sort
- `(correlation_id)` — group lookup
- `(real_user_id, created_at DESC)` — "what did user X do"
- `(module, action_type, created_at DESC)` — filter combo
- `(primary_entity_type, primary_entity_id)` — "history of this Sale"
- `(outlet_id, created_at DESC)` — outlet-scoped views

### 4.2 `audit_event_entities`

One row per affected DB entity. A single audit event can have N of these.

| column                  | type         | notes                                                  |
|-------------------------|--------------|--------------------------------------------------------|
| `id`                    | bigserial PK |                                                        |
| `audit_event_id`        | bigint FK    | → `audit_events.id` ON DELETE CASCADE *only* via DB-admin / migration; never from app |
| `entity_type`           | varchar(80)  | `Sale`, `StockLedger`, `Invoice`, …                    |
| `entity_id`             | varchar(80)  | string to allow non-integer keys / composite           |
| `operation_type`        | varchar(20)  | `Insert`, `Update`, `Delete`                           |
| `fields_changed_count`  | int          | denormalized for fast list rendering                   |
| `metadata`              | jsonb NULL   | per-entity context (e.g. prior status, snapshot ref)   |
| `created_at`            | timestamptz  |                                                        |

**Indexes:**
- `(audit_event_id)`
- `(entity_type, entity_id)` — "history of inventory row 4321"

### 4.3 `audit_event_field_changes`

| column                    | type          | notes                                          |
|---------------------------|---------------|------------------------------------------------|
| `id`                      | bigserial PK  |                                                |
| `audit_event_entity_id`   | bigint FK     | → `audit_event_entities.id`                    |
| `field_name`              | varchar(120)  | snake_case column name as stored in DB         |
| `old_value`               | jsonb NULL    | NULL for inserts                               |
| `new_value`               | jsonb NULL    | NULL for deletes                               |
| `created_at`              | timestamptz   |                                                |

**Indexes:**
- `(audit_event_entity_id)`

### 4.4 Immutability enforcement

Two layers:

1. **Application layer.** `RetailPOSDbContext.SaveChanges` will reject any `EntityState.Modified` or `EntityState.Deleted` on the three audit tables and throw `InvalidOperationException("Audit records are immutable")`. There is no controller, service, or repository method exposing UPDATE/DELETE for them.
2. **Database layer.** A migration installs a row-level trigger on each of the three tables that blocks `UPDATE` and `DELETE`:
   ```sql
   CREATE OR REPLACE FUNCTION audit_block_modify() RETURNS trigger AS $$
   BEGIN
     RAISE EXCEPTION 'audit table % is append-only', TG_TABLE_NAME;
   END;
   $$ LANGUAGE plpgsql;

   CREATE TRIGGER trg_audit_events_block_modify
     BEFORE UPDATE OR DELETE ON audit_events
     FOR EACH ROW EXECUTE FUNCTION audit_block_modify();
   -- repeat for audit_event_entities, audit_event_field_changes
   ```
   This means even direct SQL through the app's role cannot mutate audit data; only a DBA explicitly disabling the trigger can.

The legacy `audit_logs` table will be marked obsolete in code but kept readable until v1 is deployed and verified, then dropped in a follow-up migration.

## 5. Event Lifecycle

Each request follows this shape:

```
HTTP request
  → AuditContextMiddleware:
       generate correlation_id (or read from X-Correlation-Id header)
       resolve real_user_id, real_role_id, acting_user_id, acting_role_id, outlet_id
       capture ip, user_agent, parsed device/browser/os, method, path
       store on IAuditContext (scoped DI)
  → controller / service runs business logic
       — automatic capture: EF Core SaveChangesInterceptor diffs ChangeTracker entries
       — manual capture: services call IAuditService.RecordAsync(...) for actions that
         don't map cleanly to one DB save (logins, exports, role switches, multi-step
         business flows that need a curated summary)
  → on response:
       AuditFlushFilter writes one audit_event + N entity rows + M field_change rows
       inside the SAME transaction as the business changes for state-mutating endpoints
       (so audit-write failures roll back the business write — atomicity)
       For Login/Logout/RoleSwitch/Export the audit insert is the *only* write and is
       its own transaction.
  → on exception:
       GlobalExceptionHandlerMiddleware records a Failed audit_event with status=Failed,
       error_message populated, in a separate transaction (so audit survives the
       business rollback). No entity/field rows for failed writes.
```

For background jobs the entry point (`Hangfire` job, scheduled task, etc.) constructs an `AuditContext` directly with `source = SystemJob`, `real_user_id = NULL`, and proceeds identically.

## 6. Correlation ID Strategy

- New GUID per HTTP request, generated by `AuditContextMiddleware` if no `X-Correlation-Id` request header is present.
- Echoed back to the client in the `X-Correlation-Id` response header so the frontend can log it alongside errors.
- Background jobs get a fresh GUID per job execution.
- Inside one logical action that fans out to multiple service calls, all of them share the same `IAuditContext.CorrelationId`. The audit pipeline always reads the correlation from the scoped context — never re-generates it.
- Async work (e.g. fire-and-forget event handlers) must explicitly carry the correlation ID into their own scope. Helper: `IAuditContext.RunInChildScope(correlationId, action)`.

## 7. Security and Privacy

- **Sensitive field allowlist/denylist.** A static `AuditFieldRedaction` registry holds:
  - Always-mask field names (case-insensitive substring match): `password`, `passwordhash`, `token`, `refreshtoken`, `secret`, `apikey`, `api_key`, `cardnumber`, `card_number`, `cvv`, `pin`, `otp`, `private_key`.
  - Per-entity overrides for fields that look benign but are sensitive (e.g. `User.PasswordHash`).
- Masking replaces the value with `"***REDACTED***"` in both `old_value` and `new_value`. The field name is kept so reviewers can see *that* something changed without seeing what.
- Request bodies are NEVER logged in full. Only the diff produced by the EF interceptor or the explicit fields passed to `IAuditService.RecordAsync` are written.
- Query strings are sanitized: any param key matching the denylist is replaced with `***` in `request_path`.
- IP for users behind proxies: read `X-Forwarded-For` first hop only, fall back to `RemoteIpAddress`. Never log full XFF chain.
- Authorization: only `Admin` and `SuperAdmin` roles can list audit events; `BusinessOwner` and `OutletManager` can list events scoped to their outlet(s). All other roles get 403.
- The audit-export endpoint produces files with the same redaction rules as the UI.

## 8. Backend Architecture

### 8.1 Projects and folders

- `RetailPOS.Core/Entities/Audit/` — entity classes: `AuditEvent`, `AuditEventEntity`, `AuditEventFieldChange`, plus enums `AuditActionType`, `AuditModule`, `AuditSource`, `AuditStatus`, `AuditOperationType`.
- `RetailPOS.Core/Audit/IAuditContext.cs` — scoped per-request context interface.
- `RetailPOS.Infrastructure/Audit/`
  - `AuditContext.cs` — DI-scoped implementation.
  - `AuditService.cs` (`IAuditService`) — manual recording, batching, flushing.
  - `AuditChangeTrackerInterceptor.cs` — `SaveChangesInterceptor` that reads `ChangeTracker.Entries()`, builds `AuditEventEntity` + `AuditEventFieldChange` rows, hands them to `IAuditService` to flush in the same `SaveChanges`.
  - `AuditFieldRedaction.cs` — masking registry.
  - `UserAgentParser.cs` — small helper using `UAParser` (NuGet) for browser/OS/device extraction.
- `RetailPOS.API/Middleware/AuditContextMiddleware.cs` — per-request context builder.
- `RetailPOS.API/Filters/AuditFlushFilter.cs` — `IAsyncActionFilter` ensuring the event is committed for endpoints that didn't trigger a `SaveChanges` (e.g. failures, queries that still need an audit row like Export).
- `RetailPOS.API/Controllers/AuditEventsController.cs` — list / details / export endpoints.

### 8.2 Recording flows

**Automatic (preferred for CRUD).**
EF Core `SaveChangesInterceptor` walks `ChangeTracker.Entries()`:
- For each non-audit, non-ignored entity in `Added/Modified/Deleted`:
  - Create one `AuditEventEntity` row (operation type derived from `EntityState`).
  - For `Modified`: iterate `entry.Properties.Where(p => p.IsModified)`; skip masked fields' values but record the field name; build `AuditEventFieldChange` rows with `OriginalValue` → `old_value`, `CurrentValue` → `new_value` (JSON-serialized).
  - For `Added`: only `new_value`. For `Deleted`: only `old_value`.
- A single `AuditEvent` header row is created from the current `IAuditContext` (one per `SaveChanges` call). If a controller action triggers multiple `SaveChanges`, they share the correlation but produce multiple events — the audit detail page presents them grouped by correlation.

**Manual (for non-CRUD or curated actions).**
```csharp
await _auditService.RecordAsync(new AuditEventInput {
    ActionType   = AuditActionType.Approve,
    Module       = AuditModule.Sales,
    Summary      = $"Approved Sale #{sale.SaleNumber} ({sale.TotalAmount:C})",
    PrimaryEntity = (nameof(Sale), sale.Id.ToString()),
    Entities      = new[] {
        new AuditEntityInput(nameof(Sale),         sale.Id.ToString(), AuditOperationType.Update,
            fields: new[] { ("status", oldStatus, sale.Status) }),
        new AuditEntityInput(nameof(StockLedger),  ledger.Id.ToString(), AuditOperationType.Insert),
        new AuditEntityInput(nameof(Invoice),      invoice.Id.ToString(), AuditOperationType.Insert),
    },
});
```

The service is responsible for:
- Filling header fields from `IAuditContext`.
- Applying `AuditFieldRedaction`.
- Computing `fields_changed_count` per entity.
- Inserting all rows in one `SaveChanges` (separate `RetailPOSDbContext` *no* — same context, single transaction with the business write).

### 8.3 Failed actions

`GlobalExceptionHandlerMiddleware` already exists. Extend it to: if an `IAuditContext` exists for the request and the request was state-mutating (POST/PUT/PATCH/DELETE), insert one `audit_events` row with `status = Failed`, `error_message` populated, and only `primary_entity_*` if it was already known (e.g. PUT /api/sales/42). No entity/field rows.

This insert uses a **fresh** `DbContext` instance (so it survives the rolled-back business transaction).

## 9. Frontend Architecture

### 9.1 New files

```
retailpos-frontend/src/app/
  models/audit.model.ts
  services/audit.service.ts
  pages/audit/
    audit-list.component.ts
    audit-list.component.html
    audit-list.component.css
    audit-details.component.ts        # drawer / modal
    audit-details.component.html
    audit-details.component.css
    audit-row-expand.component.ts     # inline expand showing affected entities
```

### 9.2 Models (`audit.model.ts`)

Mirror backend DTOs:

```ts
export type AuditActionType = 'Create' | 'Update' | 'Delete' | 'Approve' | 'Cancel'
  | 'Login' | 'Logout' | 'RoleSwitch' | 'Export' | 'Import' | 'Payment'
  | 'Adjustment' | 'PermissionChange' | 'Reverse' | 'Hold' | 'Resume' | 'Print';

export type AuditSource = 'UI' | 'API' | 'SystemJob' | 'Import' | 'Integration';
export type AuditModule = 'Sales' | 'Purchase' | 'Inventory' | 'Accounts'
  | 'Admin' | 'Settings' | 'Reports' | 'Auth' | 'System';

export interface AuditEventListItem {
  id: number;
  createdAt: string;
  userName: string | null;
  actingAsRole: string | null;
  actionType: AuditActionType;
  actionSummary: string;
  module: AuditModule;
  source: AuditSource;
  primaryEntityType: string | null;
  primaryEntityId: string | null;
  affectedEntitiesCount: number;
  status: 'Success' | 'Failed';
  correlationId: string;
}

export interface AuditEventDetails extends AuditEventListItem {
  outletName: string | null;
  realUserName: string | null;
  ipAddress: string | null;
  device: string | null;
  browser: string | null;
  os: string | null;
  requestMethod: string | null;
  requestPath: string | null;
  errorMessage: string | null;
  modulesInvolved: AuditModule[];
  affectedEntities: AuditAffectedEntity[];
}

export interface AuditAffectedEntity {
  id: number;
  entityType: string;
  entityId: string;
  operationType: 'Insert' | 'Update' | 'Delete';
  fieldsChangedCount: number;
  fieldChanges: AuditFieldChange[];
}

export interface AuditFieldChange {
  fieldName: string;
  oldValue: unknown;
  newValue: unknown;
  redacted: boolean;
}
```

### 9.3 Routing

- `/audit-logs` → `AuditListComponent`
- Details: opens as a drawer overlay (no separate route in v1) so the user keeps the list scroll/filters.

Sidebar entry visible only when the user has `AuditLog.View` permission.

## 10. API Design

All under `/api/audit-events`. Authorization: requires permission `AuditLog.View`; export requires `AuditLog.Export`.

### 10.1 List

```
GET /api/audit-events
  ?search=…           (matches sale_number, user name, correlation_id, primary_entity_id)
  &fromDate=YYYY-MM-DD
  &toDate=YYYY-MM-DD
  &userId=…
  &actionType=…       (repeatable)
  &module=…           (repeatable)
  &source=…           (repeatable)
  &outletId=…
  &status=…
  &page=1&pageSize=50  (max 200)
```

Response: `{ items: AuditEventListItem[], total, page, pageSize }`.

The `search` parameter does multi-field OR with these mappings:
- if value parses as GUID → `correlation_id = value`
- exact match on `primary_entity_id`
- ILIKE on `action_summary` (which already contains things like sale numbers)
- ILIKE on `users.name` joined via `real_user_id`/`acting_user_id`

### 10.2 Details

```
GET /api/audit-events/{id}
```

Response: `AuditEventDetails` including all `AuditAffectedEntity` rows and their `AuditFieldChange` rows.

### 10.3 Export

```
POST /api/audit-events/export
  body: same filter shape as list, plus `format: 'csv' | 'xlsx'`
```

- Streams the file back.
- **Self-audits**: writes its own `audit_events` row with `action_type = Export`, `module = Reports`, summary like `"Exported 1,284 audit events (csv) for 2026-04-01 → 2026-04-26"`, before streaming.
- Hard cap: 100,000 rows per export to keep memory bounded; respond 413 if exceeded with a hint to narrow the date range.

### 10.4 Forbidden endpoints

There are deliberately no `PUT`, `PATCH`, or `DELETE` endpoints. Attempting any returns 405.

## 11. UI Behavior

### 11.1 Listing page

Header bar:
- Page title "Audit Log".
- Right side: Export button (disabled when no filter or > limit estimate).

Filter bar (collapsible on mobile):
- Free-text search input (debounced 300 ms).
- Date range picker (default: last 7 days).
- User dropdown (typeahead, fetches `/api/users?search=`).
- Action type multi-select.
- Module multi-select.
- Source multi-select.
- "Reset filters" link.

Table (server-side paginated, 50/page default):

| Date & Time | User | Action Summary | Primary Entity | Affected Records | Source | Action |
|-------------|------|----------------|----------------|------------------|--------|--------|
| 2026-04-26 14:32 | Sabbir Ahmed *(as Cashier)* | Approved Sale #SALE-2026-0042 ($1,240.00) | `Sale #42` | 4 | UI | ▶ View Details |

- Row expand (▶) shows a compact list of the affected entities with op-type chips. No field-level changes here — that's in the details drawer.
- Failed events get a red status badge in the User column.
- Click "View Details" → opens drawer.

### 11.2 Audit Event Details drawer

Width ~ 640 px, slides in from right, dims background.

Header:
- Title: "Audit Event Details"
- Status badge (Success/Failed)
- Close (X)

Top metadata grid:
- Action Summary (large)
- Primary Entity badge — `Sale #42` clickable to that record (when viewer has permission to that module)
- Total affected records (e.g. "4 entities, 11 field changes")
- Performed by — real user name + chip "acting as <Role>" if different
- Timestamp (absolute + relative)
- Correlation ID (monospace, copy button)
- Source chip
- Modules involved (chips)
- Request — `POST /api/sales/42/approve` (when applicable)
- Client info — IP, device, browser, OS

Affected Entities list (one card per `AuditAffectedEntity`):
- Header: entity type, entity id, op-type chip (Insert/Update/Delete), fields-changed count
- Body: a 2-column diff table

| Field | Before | After |
|-------|--------|-------|
| status | `Pending` | `Approved` |
| approved_by | `—` | `7` |
| password_hash | `***REDACTED***` | `***REDACTED***` |

Inserts: only "After" column populated. Deletes: only "Before".

Below the entity list, a small "Show full event JSON" toggle for power users.

### 11.3 Disabled mutations

The page never renders Edit/Delete affordances for audit rows. The backend guarantees the 405 response anyway — frontend just doesn't ship the buttons.

## 12. Export Behavior

- CSV: flat rows of one `audit_event` per line. Does **not** include field-level changes (would explode rows). A second column `affected_entities` carries a JSON-string summary `[{"entityType":"Sale","entityId":"42","op":"Update","changedFields":3}]` for quick scanning.
- XLSX (v1.1): multi-sheet workbook — sheet 1 events, sheet 2 entities, sheet 3 field changes, joined via `audit_event_id`.
- All exports respect filter scope and the viewer's role-scoping (an OutletManager can only export events for their outlet).
- Each export call inserts its own audit row (see §10.3).

## 13. Testing Plan

### 13.1 Unit

- `AuditChangeTrackerInterceptor`:
  - Insert/Update/Delete path each produce expected entity + field rows.
  - Modified entity with no `IsModified` properties produces zero field rows.
  - Sensitive fields are redacted.
  - Audit table writes are NOT themselves recursively audited (prevent infinite loop).
- `AuditService.RecordAsync` correctly fills header from `IAuditContext`, computes counts, inserts atomically.
- `AuditFieldRedaction` catches expected substrings and respects per-entity overrides.
- `UserAgentParser` returns expected device/browser/os for sample UA strings.

### 13.2 Integration

- Hit `POST /api/sales` end-to-end → exactly one `audit_events` row, N entity rows matching the touched DB rows, field changes match diff.
- Hit failing `POST /api/sales` (validation error) → one `audit_events` row with `status = Failed`, no entity rows, error_message populated.
- `POST /api/auth/login` (success) → one event `action_type=Login, module=Auth`. Failed login → one event `status=Failed`.
- Role switch endpoint → one event with `action_type=RoleSwitch`, `acting_role_id != real_role_id`.
- Direct UPDATE attempt against `audit_events` via raw SQL → trigger raises.
- Direct DELETE attempt → trigger raises.
- `EntityState.Modified` on `AuditEvent` in a unit test → DbContext throws.

### 13.3 UI

- List loads, filters narrow correctly, pagination works.
- Drawer renders insert/update/delete cases correctly.
- Redacted fields render as `***REDACTED***` and not the underlying value.
- Export button posts filter, response triggers download, a follow-up list refresh shows the new "Export" event.

### 13.4 Performance

- Seed 1M audit events; verify list query for last 7 days returns in < 300 ms with the indexes in §4.1.
- Verify `correlation_id` lookup (used by drawer's "show siblings" feature in v1.1) is sub-50 ms.

## 14. Implementation Checklist

### Step 1 — Project analysis ✅ (this document)

### Step 2 — Documentation ✅ (this file)

### Step 3 — DB schema / migration

- [ ] Create `RetailPOS.Core/Entities/Audit/` entities + enums.
- [ ] Add three `DbSet`s on `RetailPOSDbContext`.
- [ ] Configure entity mappings in `OnModelCreating` (jsonb columns, indexes per §4).
- [ ] EF migration `AddAuditEventTables`.
- [ ] Raw-SQL migration `AddAuditImmutabilityTriggers` (the plpgsql function + 3 triggers from §4.4).
- [ ] Mark legacy `AuditLog` entity `[Obsolete]` (do not drop yet).

### Step 4 — Backend audit service

- [ ] `IAuditContext` + `AuditContext` (scoped).
- [ ] `IAuditService` + `AuditService`.
- [ ] `AuditFieldRedaction` static registry.
- [ ] `UserAgentParser` (UAParser NuGet).
- [ ] DI wiring in `Program.cs`.

### Step 5 — Automatic logging

- [ ] `AuditChangeTrackerInterceptor` (`SaveChangesInterceptor`).
- [ ] Register interceptor on `RetailPOSDbContext`.
- [ ] `AuditContextMiddleware` registered before authentication (correlation) and reading enriched info after auth.
- [ ] `AuditFlushFilter` action filter for non-CRUD endpoints that still need an event row.
- [ ] Extend `GlobalExceptionHandlerMiddleware` to write Failed audit row in a fresh DbContext.

### Step 6 — Manual logging helper

- [ ] `IAuditService.RecordAsync(AuditEventInput)` covering Login, Logout, RoleSwitch, Export, multi-step business actions (Sale Approve, GRN Post, Stock Adjustment Approve, Permission Change, etc.).
- [ ] Helper extension methods for common shapes: `RecordLoginAsync`, `RecordLogoutAsync`, `RecordRoleSwitchAsync`, `RecordExportAsync`.

### Step 7 — API endpoints

- [ ] `AuditEventsController` with List, Details, Export (CSV in v1).
- [ ] DTOs match §9.2.
- [ ] Permission attributes `AuditLog.View`, `AuditLog.Export`.
- [ ] OutletManager scoping enforced in the query, not just the DTO.
- [ ] Self-audit on Export.

### Step 8 — Frontend

- [ ] `audit.model.ts`, `audit.service.ts`.
- [ ] Sidebar item gated by permission.
- [ ] `audit-list.component` (filters, table, expand, paginator).
- [ ] `audit-details.component` (drawer; diff table; copy correlation ID).
- [ ] Route `/audit-logs`.
- [ ] Wire export button.

### Step 9 — Module wiring (touch each module's services so business actions write meaningful events)

- [ ] **Auth** — Login (success/fail), Logout, RoleSwitch.
- [ ] **Users / Roles** — Create/Update/Delete user; Role create/update; PermissionChange (high-priority chip in UI).
- [ ] **Sales** — SaleCreate, SaleVoid, Hold, Resume, Payment, Print receipt.
- [ ] **Purchase** — PO Create/Approve/Cancel, GRN Create/Post.
- [ ] **Inventory** — StockTransfer Create/Approve, StockAdjustment Create/Approve.
- [ ] **Accounts** — Transaction Create, Bill Create/Pay, Expense Create.
- [ ] **Settings** — any settings change.
- [ ] **System jobs** — every scheduled task uses `AuditContext.ForSystemJob(jobName)`.

### Step 10 — Tests

- [ ] Unit + integration tests per §13.
- [ ] Add a CI smoke test: hit one mutating endpoint per controller, assert an audit row exists.

### Step 11 — Cutover

- [ ] Deploy new tables + interceptor.
- [ ] Verify on staging for 1 week.
- [ ] Drop legacy `audit_logs` table in `RemoveLegacyAuditLog` migration.

---

## Appendix A — Action ↔ Module mapping cheat sheet

| Action source                              | action_type        | module     |
|--------------------------------------------|--------------------|------------|
| `POST /api/auth/login` ok                  | Login              | Auth       |
| `POST /api/auth/login` fail                | Login (Failed)     | Auth       |
| `POST /api/auth/logout`                    | Logout             | Auth       |
| Role-switch endpoint                       | RoleSwitch         | Auth       |
| `POST /api/users`                          | Create             | Admin      |
| `PUT  /api/users/{id}`                     | Update             | Admin      |
| `PUT  /api/roles/{id}/permissions`         | PermissionChange   | Admin      |
| `POST /api/sales`                          | Create / Payment   | Sales      |
| Sale approve flow                          | Approve            | Sales      |
| Hold/park sale                             | Hold               | Sales      |
| Resume held sale                           | Resume             | Sales      |
| `POST /api/purchase-orders`                | Create             | Purchase   |
| PO approve                                 | Approve            | Purchase   |
| GRN post                                   | Create             | Purchase   |
| Stock transfer approve                     | Approve            | Inventory  |
| Stock adjustment approve                   | Adjustment         | Inventory  |
| `POST /api/transactions`                   | Create             | Accounts   |
| Bill payment                               | Payment            | Accounts   |
| Settings save                              | Update             | Settings   |
| Report export                              | Export             | Reports    |
| Audit log export (self-audit)              | Export             | Reports    |
| Nightly inventory rollup job               | Update             | System     |

## Appendix B — Open questions

1. Do we need cryptographic hash chaining in v1? — Currently *no*; metadata column reserved.
2. Do we need to capture **read** access to sensitive screens (e.g. viewing a customer's full payment history)? — Out of scope v1; can be added by sprinkling explicit `RecordAsync` calls.
3. Multi-tenant `business_id` — column added now to avoid a migration later, but not populated until multi-tenant lands.
