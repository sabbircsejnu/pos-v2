# STOCK TRANSFER IMPLEMENTATION PLAN

## 1. Objective

Implement the Stock Transfer Management module according to `Docs/06_Inventory/STOCK_TRANSFER_MANAGEMENT.md` while preserving existing behavior in inventory, GRN/purchase receiving, stock adjustment, and product modules.

This plan is intentionally phased and reviewable. No bulk implementation is proposed.

---

## 2. Existing System Findings

## 2.1 Inventory and Ledger Foundation

Current state:
- `Inventory` is a single quantity per `(VariantId, LocationId, LocationType)`.
- `StockLedger` already exists and is used as immutable movement history in GRN, stock adjustments, and stock transfers.
- Current transfer logic writes ledger rows for dispatch (`transfer_out`) and receipt (`transfer_in`).

Findings:
- Existing model does not represent explicit inventory state buckets (`available`, `in_transit`, etc.).
- In practice, source quantity is reduced on dispatch, destination quantity is increased on receive, which aligns with core movement requirement.

## 2.2 Existing Stock Transfer Module (Already Implemented)

Current backend exists:
- Entities: `StockTransfer`, `StockTransferItem`.
- Data access: `IStockTransferRepository`, `StockTransferRepository`.
- Service: `IStockTransferService`, `StockTransferService`.
- API: `StockTransfersController` with endpoints for list/search/get/create/update/approve/reject/send/receive/cancel.

Current frontend exists:
- Pages: transfer list/create/details.
- Service/model: `stock-transfer.service.ts`, `stock-transfer.model.ts`.
- Routing and menu wiring already present.

Major gaps vs new requirement:
- No requisition module/entities/workflow.
- No transfer number generation field.
- No transfer type field (direct/requisition/return).
- No related requisition linkage.
- No line-level requested qty, transfer qty, accepted qty, rejected qty, unit cost, remarks.
- No partial receive flow.
- No explicit rejected stock return transfer creation.
- No complete transfer audit fields (dispatched by/date, received by/date, rejected by/date).
- Status model currently is simplified (`pending`, `approved`, `in_transit`, `received`, etc.) and not aligned to new detailed lifecycle.

## 2.3 Warehouse / Outlet / Location Access

Current state:
- Location abstraction already exists via `LocationType` + `LocationId` patterns.
- `IUserOutletAccessService` supports both outlet and warehouse authorization paths.
- Transfer logic already uses both outlet and warehouse locations with a shared engine.

Findings:
- Existing pattern already supports location-agnostic transfer engine; this should be preserved and expanded.

## 2.4 Product / Variant and Inventory Validation

Current state:
- Variant validation is already performed in service layer.
- Transfer create/update validates stock availability at source.

Findings:
- Base validation exists, but must be expanded for new states and receiving rules.

## 2.5 Stock Adjustment and GRN Pattern Reuse

Current state:
- Stock adjustment module has robust lifecycle, audit events, and ledger integration.
- GRN module has inventory updates and ledger writes in transactional operations.

Findings:
- These modules provide strong implementation patterns for:
  - transaction boundaries,
  - status transitions,
  - audit + log consistency,
  - inventory and ledger atomicity.

## 2.6 Permissions and Role System

Current state:
- Permission catalog contains stock transfer permissions (`view/create/edit/delete/approve/cancel`).
- Angular role UI includes stock transfer permission group.
- Route guards and API policies are in place.

Gaps:
- No requisition-specific permissions.
- Existing transfer action permissions are coarse; receive/dispatch/reject/partial receive may need finer granularity.

## 2.7 Existing Reporting

Current state:
- Stock transfer report endpoint and frontend report page exist.

Findings:
- Report summary currently assumes statuses like `pending`/`completed`, which is not aligned with current transfer statuses and future lifecycle states.
- No dedicated requisition fulfillment report, in-transit report, rejected stock report, or return transfer report yet.

## 2.8 Existing Tests

Current state:
- Stock adjustment workflow HTTP tests exist.
- No dedicated stock transfer workflow tests found.

Gap:
- Need transfer lifecycle + requisition + permissions + ledger tests before rollout.

---

## 3. Required Backend Changes

## 3.1 Domain and Entity Model

Enhance transfer domain to support source-of-truth requirements:
- Add transfer header fields:
  - `TransferNo`
  - `TransferType` (Direct, Requisition, ReturnTransfer)
  - `SourceLocationType`, `SourceLocationId`
  - `DestinationLocationType`, `DestinationLocationId`
  - `RelatedRequisitionId` (nullable)
  - `Notes`
  - audit fields for each lifecycle stage (Created/Submitted/Approved/Dispatched/Received/Rejected/Cancelled)
- Add transfer line fields:
  - `RequestedQuantity`
  - `TransferQuantity`
  - `AcceptedQuantity`
  - `RejectedQuantity`
  - `UnitCost`
  - `Remarks`
- Add requisition entities:
  - `StockRequisition`
  - `StockRequisitionLine`
- Add status constants/enums for transfer and requisition lifecycle.
- Preserve existing records via migration-safe defaults and compatibility mapping where needed.

## 3.2 Service and Workflow Logic

Implement strict state-machine-like lifecycle methods:
- Requisition:
  - Draft -> Submitted -> Approved/Rejected -> PartiallyFulfilled/FullyFulfilled -> Closed
- Transfer:
  - Draft -> Submitted -> InTransit -> Received/PartiallyReceived/Rejected
  - Cancelled only allowed before dispatch according to business rule

Add guarded operations:
- `DispatchTransfer`
- `ReceiveTransfer` with accepted/rejected quantities
- `RejectTransfer`
- `CreateReturnTransfer` for rejected quantities
- `CreateTransferFromRequisition`

Enforce validations:
- Prevent same source and destination.
- Prevent transfer qty > available source stock.
- Prevent receiving before dispatch.
- Prevent edits after dispatch unless explicitly allowed.
- Validate receiving quantities per line (`accepted + rejected <= transfer`).

## 3.3 Ledger and Inventory Source-of-Truth

Ensure all stock-changing actions are transactionally coupled with ledger writes:
- Dispatch:
  - Source inventory decreases
  - Ledger: transfer_out
- Receive full/partial:
  - Destination increases by accepted only
  - Ledger: transfer_in for accepted only
- Reject handling:
  - Destination must not increase for rejected qty
  - Rejected qty must be represented via return transfer flow and corresponding ledger entries

---

## 4. Required Database Changes

New/updated schema items (phase-wise):

- `stock_transfers`:
  - add transfer number, transfer type, related requisition id
  - rename/alias from/to location fields toward source/destination naming
  - add extended audit columns (submitted/approved/dispatched/received/rejected/cancelled user+datetime)
  - add optional notes
- `stock_transfer_items`:
  - add requested/transfer/accepted/rejected quantities
  - add unit cost and remarks
- New requisition tables:
  - `stock_requisitions`
  - `stock_requisition_lines`
- Indexes:
  - transfer number unique
  - status/date/location indexes for list and reporting
  - requisition number unique and status/date indexes
- Foreign keys:
  - requisition -> transfer linkage
  - user references for lifecycle audit columns

Migration strategy:
- Additive first, then controlled refactor, avoiding destructive changes to preserve current data.
- Backfill defaults for existing transfer records.

---

## 5. Required API Endpoints

## 5.1 Transfer Endpoints

Keep existing endpoint style and add/adjust:
- `GET /api/stock-transfers`
- `POST /api/stock-transfers/search`
- `GET /api/stock-transfers/{id}`
- `POST /api/stock-transfers` (create draft/direct/requisition-linked)
- `PUT /api/stock-transfers/{id}` (limited pre-dispatch)
- `POST /api/stock-transfers/{id}/submit`
- `POST /api/stock-transfers/{id}/dispatch`
- `POST /api/stock-transfers/{id}/receive` (accept/reject quantities payload)
- `POST /api/stock-transfers/{id}/cancel`
- `POST /api/stock-transfers/{id}/create-return` (or auto-create in receive flow)

## 5.2 Requisition Endpoints

Add new module API:
- `GET /api/stock-requisitions`
- `POST /api/stock-requisitions/search`
- `GET /api/stock-requisitions/{id}`
- `POST /api/stock-requisitions`
- `PUT /api/stock-requisitions/{id}`
- `POST /api/stock-requisitions/{id}/submit`
- `POST /api/stock-requisitions/{id}/approve`
- `POST /api/stock-requisitions/{id}/reject`
- `POST /api/stock-requisitions/{id}/convert-to-transfer`

## 5.3 Error and Contract Consistency

- Continue using existing `ApiResponse<T>` envelope and exception middleware.
- Add explicit frontend-friendly validation messages for all transition failures.
- Ensure Swagger reflects new request/response contracts and status transitions.

---

## 6. Required Frontend Pages and Components

## 6.1 Transfer Module Enhancements

Extend existing pages:
- Transfer list:
  - new statuses and filters
  - type badge (direct/requisition/return)
  - in-transit and partially received visibility
- Create transfer:
  - source/destination location type+id
  - line-level requested/transfer qty and unit cost
  - validation messages from backend
- Transfer details:
  - lifecycle timeline with audit stamps
  - dispatch action
  - receive modal with per-line accept/reject
  - partial receive support
  - return transfer visibility

## 6.2 Requisition Module (New)

Add pages/components:
- Requisition list
- Requisition create/edit/details
- Requisition approve/reject actions
- Convert requisition to transfer flow
- Fulfillment progress UI

## 6.3 Reporting UI

Add/extend report pages:
- In Transit report
- Transfer summary (aligned to new statuses)
- Requisition fulfillment report
- Rejected stock report
- Return transfer report

---

## 7. Required Permissions

Add and integrate new permissions (proposal):
- Requisition:
  - `stock_requisitions.view`
  - `stock_requisitions.create`
  - `stock_requisitions.edit`
  - `stock_requisitions.approve`
  - `stock_requisitions.reject`
  - `stock_requisitions.convert_to_transfer`
- Transfer refinement:
  - `stock_transfers.dispatch`
  - `stock_transfers.receive`
  - `stock_transfers.reject_receive` (if separated)
  - `stock_transfers.return_create`

Role mapping target:
- Business Owner: full transfer + requisition + reports.
- Warehouse Manager: create/review/dispatch/receive within authorized locations.
- Outlet Manager: create requisition, outlet transfer create where permitted, receive/partial/reject at destination.

Also update:
- API authorization policies
- frontend route/menu/action guards
- role form categories
- seed SQL and any default role bootstrapping

---

## 8. Inventory Movement Rules (Implementation Contract)

These rules are mandatory in implementation:

1. Dispatch rule:
- On `InTransit`, decrease source inventory by transfer quantity.
- Write ledger movement (`transfer_out`).

2. Acceptance rule:
- On receive, increase destination inventory only by accepted quantity.
- Write ledger movement (`transfer_in`) for accepted quantity only.

3. Partial acceptance:
- accepted affects destination, rejected does not.
- transfer status -> `PartiallyReceived` when mixed result.

4. Rejection rule:
- Rejected quantity must never increase destination inventory.
- Rejected quantity must be handled via return transfer process.

5. Source-of-truth rule:
- All stock changes must be backed by ledger writes in same DB transaction.

6. Engine rule:
- Transfer engine must remain location-agnostic (`SourceLocationType/Id`, `DestinationLocationType/Id`) and reusable for warehouse-outlet, outlet-outlet, warehouse-warehouse, and returns.

---

## 9. Testing Plan

## 9.1 Backend Automated Tests

Add integration/HTTP tests for:
- Transfer draft/create/update validations
- Dispatch inventory and ledger effects
- Receive full/partial/reject behavior
- No receive before dispatch
- No over-transfer
- Same source/destination rejection
- Edit restrictions after dispatch
- Requisition lifecycle and fulfillment transitions
- Return transfer generation from rejected quantities
- Permission boundary tests for Business Owner/Warehouse Manager/Outlet Manager

## 9.2 Frontend Validation Tests

- Build and route guard checks
- Action visibility by permission and status
- Form validation behavior
- Receive modal partial acceptance calculations

## 9.3 Regression Verification

After each phase:
- Backend build: `dotnet build RetailPOS.slnx -c Release`
- Frontend build: run from `retailpos-frontend` using `npm run -s build`
- Smoke existing inventory, stock adjustment, GRN, and product flows

---

## 10. Step-by-Step Implementation Phases

## Phase 1: Database Model Foundation

Scope:
- Entities, enums/status constants, EF configuration, migration, relationships.

Deliverables:
- New/updated transfer and requisition entities.
- Audit/lifecycle fields added.
- Migration generated and reviewed.

Validation:
- Backend builds.
- Migration applies cleanly.

## Phase 2: Backend Business Logic

Scope:
- Services, repositories, validation rules, lifecycle transitions, ledger integration.

Deliverables:
- Dispatch/receive/partial/reject logic.
- Requisition to transfer fulfillment logic skeleton.
- Strict transactional stock+ledger consistency.

Validation:
- Targeted integration tests for lifecycle and ledger.

## Phase 3: API Contracts and Endpoints

Scope:
- DTOs, controllers, request/response models, Swagger correctness.

Deliverables:
- New requisition endpoints.
- Updated transfer endpoints for detailed receive flow.
- Error message contract completeness.

Validation:
- Swagger loads and contracts are correct.
- API smoke tests pass.

## Phase 4: Permissions and Role Integration

Scope:
- Permission catalog, policies, role templates/seeds, frontend role mapping.

Deliverables:
- Requisition and granular transfer permissions wired end-to-end.
- Role access rules for Business Owner, Warehouse Manager, Outlet Manager.

Validation:
- Unauthorized/authorized scenarios tested.

## Phase 5: Angular Transfer UX

Scope:
- Transfer list/create/details enhancements.

Deliverables:
- Dispatch and receive UI with per-line accept/reject.
- Partial receive and lifecycle timeline support.
- Proper frontend error display.

Validation:
- Frontend build and manual workflow checks.

## Phase 6: Requisition Workflow UI + API Completion

Scope:
- Requisition pages and convert-to-transfer flow.

Deliverables:
- Requisition list/create/details.
- Approval/rejection and fulfillment handling.
- Partial fulfillment indicators.

Validation:
- End-to-end requisition -> transfer -> receive flow.

## Phase 7: Reports

Scope:
- In transit, transfer summary, requisition fulfillment, rejected stock, return transfer reporting.

Deliverables:
- Backend report queries + API routes.
- Frontend report pages/filters/exports.

Validation:
- Report totals reconcile with ledger and transfer data.

## Phase 8: Final QA and Documentation

Scope:
- Full testing pass, bug fixes, UX polish, docs update.

Deliverables:
- Stable feature set.
- Updated docs for transfer/requisition workflows and permissions.

Validation:
- Backend/frontend builds pass.
- Regression checklist signed off.

---

## 11. Risks and Mitigation

Key risks:
- Breaking existing stock transfer UI/API contracts.
- Status mismatch across reports and UI.
- Data migration complexity with existing records.
- Permission drift between backend and frontend.

Mitigation:
- Additive migration first and compatibility DTO mapping where necessary.
- Phase-gated tests and builds after each phase.
- Keep API response envelope and controller style consistent with existing modules.

---

## 12. Recommended Execution Note

Implement this plan strictly phase-by-phase and pause after each phase for review/approval before proceeding to the next phase.
