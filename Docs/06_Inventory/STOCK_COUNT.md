# Stock Count Module
## Software Requirements Document (SRD)

**Module:** Inventory Management  
**Feature:** Stock Count  
**Version:** 1.1  
**Status:** Draft

---

# 1. Overview

## Purpose

The Stock Count module enables periodic physical inventory counting for outlets and warehouses.

It captures a point-in-time stock snapshot, supports Excel download and print in the initial release, and enables controlled workflow actions in later phases.

---

# 2. Business Objectives

- Perform periodic stock counting for each location.
- Compare physical inventory with snapshot system stock.
- Maintain auditable stock count history.
- Reduce discrepancies and support operational control.

---

# 3. Scope

## 3.1 Authoritative Phase Plan

This section is the single source of truth for scope.

### Phase 1 (Current Release)

Included:

- Stock Count List
- Create Stock Count (Generate Snapshot)
- View Stock Count
- Download Excel
- Print Stock Count Sheet
- Post-generation inventory movement warning on View screen

Excluded:

- Excel Upload
- Physical Count Save
- Difference Calculation
- Submit
- Approve
- Reject
- Reopen
- Generate Stock Adjustment Draft
- Inventory Freeze
- Barcode Scanner
- Mobile Stock Count
- RFID

### Phase 2

Included:

- Excel Upload
- Physical Count Save
- Difference Calculation
- Submit
- Reject
- Reopen
- Post-generation warning on Approve screen

### Phase 3

Included:

- Approve
- Generate Stock Adjustment Draft
- AdjustmentGenerated and Completed lifecycle handling
- Optional inventory freeze controls
- Mobile and barcode assisted counting

---

# 4. Business Rules

## BR-001 Location Ownership

Every Stock Count belongs to exactly one inventory location.

Location type can be:

- Outlet
- Warehouse

## BR-002 Snapshot Principle

A Stock Count records system stock at generation time.

Future inventory movements must not modify the stored snapshot.

## BR-003 Historical Counts

A location can have multiple Stock Counts over time.

## BR-004 Zero Stock Inclusion

Variants with zero stock must also be included in the generated sheet.

## BR-005 Active Product Scope

Only active products and active variants are included during generation.

## BR-006 Variant Line Rule

Each product variant represents one counting line.

## BR-007 One Active Stock Count Per Location

A location can have only one active Stock Count at a time.

Active statuses are:

- Draft
- Submitted

If an active Stock Count exists for the location, creating another must be blocked.

## BR-008 Snapshot Immutability

After generation, the following snapshot fields are immutable:

- ProductName
- ProductCode
- VariantName
- CurrentStock

These fields must not be changed by UI, API, or background processes.

## BR-009 Post-Generation Movement Warning

If inventory transactions occur after stock count generation timestamp for included variants at that location, the system must show a warning indicator.

---

# 5. User Roles

## Outlet Manager

Can:

- View stock counts for own outlet only
- Create stock counts for own outlet only
- Download and print own outlet stock counts

Cannot:

- Access other outlets or warehouses

## Warehouse Manager

Can:

- View stock counts for own warehouse only
- Create stock counts for own warehouse only
- Download and print own warehouse stock counts

Cannot:

- Access other warehouses or outlets

## Business Owner

Can:

- View stock counts across all locations
- Create stock counts for any authorized location
- Filter by outlet or warehouse

## Custom Users

Access depends on assigned permissions and authorized locations.

---

# 6. Permission Requirements

The module uses the following concrete permission keys:

- StockCount.ViewOwn
- StockCount.ViewAll
- StockCount.Create
- StockCount.Download
- StockCount.Print
- StockCount.Upload
- StockCount.Submit
- StockCount.Approve
- StockCount.Reject
- StockCount.Reopen

## 6.1 Phase Permission Usage

Phase 1:

- StockCount.ViewOwn
- StockCount.ViewAll
- StockCount.Create
- StockCount.Download
- StockCount.Print

Phase 2:

- StockCount.Upload
- StockCount.Submit
- StockCount.Reject
- StockCount.Reopen

Phase 3:

- StockCount.Approve

## 6.2 Location and Tenant Isolation Rules

- Regular users can only see/create stock counts for their default location.
- Outlet Manager can only access own outlet.
- Warehouse Manager can only access own warehouse.
- Business Owner or users with StockCount.ViewAll can access all authorized locations.

Server-side enforcement is mandatory for list, create, view, download, print, upload, submit, approve, reject, reopen, and adjustment draft generation.

---

# 7. Module Structure

The module has three pages.

## 7.1 Stock Count List

Columns:

- Stock Count No.
- Stock Count Date
- Location
- Total Products
- Created By
- Status
- Created Date
- Actions

Actions:

- View
- Download Excel
- Print

Filters:

Basic:

- Search

Advanced:

- Location (if permitted)
- Date Range
- Status

Default behavior:

- Restricted users are auto-filtered to default location.
- Users with StockCount.ViewAll can select/filter all locations.

List UX standards:

- Follow standard Basic and Advanced filter behavior.
- Advanced filters use drawer pattern with Apply and Clear actions.
- Show active advanced-filter count.
- Preserve filter and pagination state in URL query parameters.

## 7.2 Create Stock Count

Purpose:

- Generate snapshot and stock count lines.

Location selection:

- Outlet Manager: own outlet only, readonly.
- Warehouse Manager: own warehouse only, readonly.
- Business Owner or StockCount.ViewAll user: selectable location.

Form:

- Location
- Stock Count Date
- Remarks (optional)

Buttons:

- Generate Snapshot
- Cancel

Post-create actions:

- Download Excel
- Print

Generation logic:

- Retrieve all eligible variants for selected location.
- Save line snapshots including ProductName, ProductCode, VariantName, CurrentStock.
- PhysicalCount and Difference remain empty in Phase 1.
- Enforce one-active-stock-count rule.

## 7.3 View Stock Count

Header:

- Stock Count Number
- Date
- Location
- Created By
- Status
- Total Products
- Remarks

Table:

| Product | Product Code | Variant | Current Stock | Physical Count | Difference | Remarks |

Phase 1 behavior:

- Physical Count remains empty.
- Difference remains empty.
- Remarks remain empty.

Warning behavior:

- Phase 1: show post-generation movement warning on View.
- Phase 2+: show warning on View and Approve.

---

# 8. Excel Format

## Header

- Company Name
- Location Name (Outlet or Warehouse)
- Stock Count Date
- Generated By
- Generated Time
- Counted By
- Signature
- Manager Signature

## Table

| SL | Product Name | Product Code | Variant | Current Stock | Physical Count | Difference | Remarks |

Phase 1:

- Physical Count remains blank.
- Difference remains blank.

---

# 9. Print Layout

- A4 Landscape
- Professional header
- Company logo (future)
- Company name
- Location name
- Stock Count Date
- Page number
- Generated Date and Time

Footer:

- Prepared By
- Checked By
- Approved By

---

# 10. API Contract

Endpoints:

- List
- Create or Generate Snapshot
- View
- Download Excel
- Print
- Upload Excel
- Submit
- Approve
- Reject
- Reopen
- Generate Stock Adjustment Draft

Phase availability:

- Phase 1 active: List, Create or Generate Snapshot, View, Download Excel, Print
- Phase 2 active: Upload Excel, Submit, Reject, Reopen
- Phase 3 active: Approve, Generate Stock Adjustment Draft

Validation requirements:

- Permission check by action
- Business and location authorization check
- One-active-stock-count rule check
- Snapshot immutability enforcement
- State transition validation

---

# 11. Database Design

## 11.1 StockCount

| Field | Description |
|----------|----------------|
| Id | PK |
| StockCountNo | Auto Number |
| BusinessId | FK |
| LocationId | FK |
| LocationType | Outlet or Warehouse |
| StockCountDate | Date |
| Status | State machine status |
| Remarks | Optional |
| TotalItems | Snapshot count |
| CreatedBy | User |
| CreatedAt | DateTime |
| SubmittedBy | Nullable User |
| SubmittedAt | Nullable DateTime |
| ApprovedBy | Nullable User |
| ApprovedAt | Nullable DateTime |
| RejectedBy | Nullable User |
| RejectedAt | Nullable DateTime |
| RejectionReason | Nullable Text |

## 11.2 StockCountLine

| Field | Description |
|----------|----------------|
| Id | PK |
| StockCountId | FK |
| ProductId | FK |
| VariantId | FK |
| ProductName | Snapshot, immutable |
| ProductCode | Snapshot, immutable |
| VariantName | Snapshot, immutable |
| CurrentStock | Snapshot, immutable |
| PhysicalStock | Nullable |
| Difference | Nullable |
| Remarks | Nullable |

## 11.3 Numeric Precision and UOM

- CurrentStock, PhysicalStock, and Difference use Decimal(18,3).
- All quantities are stored in base inventory UOM.
- Display conversions must not alter stored snapshot values.

## 11.4 Indexes and Constraints

Required indexes:

- BusinessId
- LocationId
- StockCountDate
- Status

Required constraints:

- Unique line per variant per stock count: (StockCountId, VariantId)
- Enforce one active stock count (Draft or Submitted) per location

---

# 12. State Machine

Canonical states:

- Draft
- Submitted
- Rejected
- Approved
- AdjustmentGenerated
- Completed

Allowed transitions:

- Draft -> Submitted
- Submitted -> Rejected
- Submitted -> Approved
- Rejected -> Draft (Reopen)
- Approved -> AdjustmentGenerated
- AdjustmentGenerated -> Completed

Phase behavior:

- Phase 1: Draft only
- Phase 2: Submitted and Rejected plus Reopen
- Phase 3: Approved, AdjustmentGenerated, Completed

---

# 13. Future Enhancements

- Variance report views and analytics
- Inventory freeze options
- Mobile counting and barcode-assisted counting
- Extended approval and exception workflows

---

# 14. Non-Functional Requirements

- For 20,000 variants, snapshot generation p95 must be under 30 seconds.
- For 20,000 variants, Excel generation/download preparation p95 must be under 45 seconds.
- List views must support pagination.
- Exports must preserve immutable snapshot values.
- Product snapshot fields must remain unchanged after generation.
- UI must follow standard List -> Create -> View flow and filter standards.
- All actions must respect role, permission, tenant, and location authorization rules.

---

# 15. Explicit Out of Scope

- RFID is not required.
- RFID must not be included in design or implementation.

---

# 16. Success Criteria

The feature is complete for the current phase when:

- Users can generate stock counts for authorized locations only.
- Only one active stock count exists per location.
- Snapshot fields are saved and remain immutable.
- Excel can be downloaded and printed from generated snapshots.
- Unauthorized cross-location access is blocked.
- Post-generation movement warning appears on View when applicable.
- Phase 1 excludes upload, approval, and adjustment generation by design.