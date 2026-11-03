# Purchase Order Management

## Purpose

This document defines the Purchase Order process, approval workflow, goods receiving process, stock update rules, and audit requirements.

Purchases can be received directly into either an **Outlet** or a **Warehouse** (the PO "destination location"). This supports businesses that operate only Outlets, only Warehouses, or both — no dummy Warehouse is needed for Outlet-only businesses.

---

# Workflow Overview

```text
Draft PO
    ↓
Pending Approval
    ↓
Approved
    ↓
GRN Created
    ↓
Stock Updated
```

Alternative Flow:

```text
Purchase & Receive Immediately
    ↓
Auto Generate GRN
    ↓
Stock Updated
    ↓
Completed
```

---

# User Roles

## Warehouse / Outlet Manager

Can:

- Create Purchase Order (for destination locations they can access)
- Submit for Approval
- Receive Goods
- Create GRN

## Purchase Approver

Can:

- Approve Purchase Orders
- Reject Purchase Orders
- Send Back for Correction

## Administrator

Can:

- Manage all Purchase Orders
- Override Approval Limits
- View Audit Logs

---

# Purchase Order

## Create Purchase Order

### Required Fields

| Field | Required |
|---------|---------|
| Supplier | Yes |
| Location Type (Outlet / Warehouse) | Yes |
| Location (filtered by Location Type) | Yes |
| Purchase Date | Yes |
| Products | Yes |
| Quantity | Yes |
| Unit Cost | Yes |

### Item Fields

| Field |
|---------|
| Product |
| Main Product Code |
| SKU |
| Barcode |
| Quantity |
| Unit |
| Unit Cost |
| Discount |
| Tax |
| Total |

---

# Destination Location (Outlet or Warehouse)

Every Purchase Order has exactly one destination location: an Outlet **or** a Warehouse. Goods received against the PO (Purchase & Receive or manual GRN) are always stocked into that location.

## Business Scenarios

| Business setup | Behaviour |
|---------|---------|
| Outlet-only | Location Type defaults to Outlet; Warehouse is shown as "(none available)". Stock is added directly to the Outlet. No dummy Warehouse is required. |
| Warehouse-only | Location Type defaults to Warehouse. Existing Warehouse-based purchasing is unchanged. |
| Mixed (Outlets and Warehouses) | User chooses the Location Type; the Location dropdown lists only locations of that type. The user's default location is pre-selected when available. |

## Create Purchase Order Screen

- **Location Type**: Outlet / Warehouse. Types with no accessible locations are disabled.
- **Location**: dropdown filtered by the selected Location Type. When only one location of that type exists it is selected automatically.
- The location list comes from `GET /api/me/outlets` (the user's authorized outlets and warehouses), so it contains only locations that:
  - belong to the current Business, and
  - are accessible to the user (`all_locations`, `specific_locations` assignments, or `assigned_only` default outlet / primary warehouse).
- An unswitched Business Owner with the effective `*` permission can select every location in the current Business, even when their stored location scope is `assigned_only` and they have no assignments. This does not grant access to other Businesses. Switched sessions do not inherit this wildcard-based owner grant; wildcard policy permissions alone do not broaden a non-owner's location access.
- API failures and invalid location responses show an error with **Retry locations**, not the empty-location hint. The "No active outlets or warehouses..." hint appears only after a successful, empty response.
- Outlets and Warehouses do not have an active/inactive flag; existing (non-deleted) locations are treated as active.
- When editing an existing PO, its saved destination stays selected even if it is no longer in the user's list; the backend still enforces access on save.

## Location Rules (enforced on the backend)

| Rule | Result when violated |
|---------|---------|
| Location Type must be `outlet` or `warehouse` | 400 Bad Request |
| Location must exist and belong to the current Business | 404 Not Found (`Outlet with ID x not found` / `Warehouse with ID x not found`) |
| User must have write access to the location (`IUserOutletAccessService.EnforceWriteLocationAsync`) | 403 Forbidden |
| Role permission (`purchases.create`, `purchases.edit`, `purchases.receive`, `grn.create`, `grn.receive`) | 403 Forbidden |

Access is checked on PO create, PO update, Purchase & Receive, manual GRN creation and GRN completion. The destination of an existing PO cannot be bypassed during receiving: GRNs always use the PO's saved location.

## API Contract

Create / update / Purchase & Receive requests accept:

```json
{
  "supplierId": 1001,
  "locationType": "outlet",
  "locationId": 12,
  "orderDate": "2026-10-04",
  "items": [ ... ]
}
```

- `locationType` + `locationId` is the preferred contract.
- The legacy `warehouseId` field is still accepted (treated as `locationType = "warehouse"`), so existing Warehouse-based clients keep working.
- List filters: `GET /api/purchase-orders?locationType=outlet&locationId=12` and `POST /api/purchase-orders/search` with `locationType` / `locationId`; the legacy `warehouseId` filter still works.

PO, GRN and pending-GRN responses include:

| Field | Description |
|---------|---------|
| `locationType` | `outlet` or `warehouse` |
| `locationId` | Destination outlet or warehouse id |
| `locationName` | Destination display name |
| `outletId` / `outletName` | Set for Outlet destinations, otherwise null |
| `warehouseId` / `warehouseName` | Set for Warehouse destinations, otherwise null/empty (kept for backward compatibility) |

## Data Model

- `purchase_orders.warehouse_id` is nullable and a nullable `purchase_orders.outlet_id` (FK → `outlets`, restrict delete) was added.
- Check constraint `ck_purchase_orders_single_destination`: exactly one of `warehouse_id` / `outlet_id` is set.
- Purchase & Receive idempotency is unique per destination: `(warehouse_id, idempotency_key)` and `(outlet_id, idempotency_key)`.
- A PO's Business is derived from its destination location (Warehouse or Outlet `business_id`).
- GRNs reference the PO; they do not store their own location.
- Stock uses the existing location-specific `inventories` (`location_type`, `location_id`) and `stock_ledgers` tables. No separate purchase inventory model exists; product/variant identity remains Business-scoped.
- Migration: `20261023072233_PurchaseOrderOutletDestination` (additive only). Existing rows keep their `warehouse_id`; no stock balances or historical PO/GRN references are changed.

---

# Product Lookup Standard for PO Line Items

Because PO lines are variant-based, each variant must be discoverable by business and scanner identifiers.

## Search Inputs (must be supported)

- Main Product Code (parent product code, e.g., `ABA001P`)
- Variant SKU (e.g., `ABA001P-56-BLK`)
- Variant Barcode (EAN-13 compatible numeric barcode)
- Product Name
- Variant attributes (size, color, etc.)

## Search Result Behavior

- Searching by Main Product Code returns all active variants under that parent product.
- Searching by Variant SKU returns the matching variant.
- Searching by Variant Barcode returns the matching variant.
- Searching by Product Name or Variant attributes returns related active variants.

## Dropdown Display Format

- Title:
    - `Product Name - Variant Attributes`
- Meta row:
    - `SKU: [Variant SKU] | Barcode: [Barcode] | Main Code: [Main Product Code] | Cost: [value] | Stock: [value]`

## Identifier Rules for PO

- Every purchasable variant must have:
    - Parent Main Product Code
    - Variant SKU
    - Variant Barcode
- Variant SKU and Barcode must be unique.

---

# Purchase Order Statuses

| Status | Description |
|----------|-------------|
| Draft | Not submitted |
| Pending Approval | Waiting for approval |
| Sent Back | Needs correction |
| Rejected | Rejected |
| Approved | Approved but not received |
| Partially Received | Some quantity received |
| Fully Received | All quantity received |
| Completed | Fully completed |
| Cancelled | Cancelled |

---

# Approval Workflow

## Draft → Pending Approval

User submits PO for approval.

### Validation

- Supplier required
- Destination location (Location Type + Location) required
- At least one item required

---

## Pending Approval → Approved

Approver reviews and approves PO.

### Result

- Status = Approved
- No stock update
- The selected destination location is preserved unchanged for receiving

---

## Pending Approval → Rejected

Approver rejects PO.

### Result

- Status = Rejected

---

# Purchase & Receive Immediately

## Description

Users with proper permission can purchase and receive stock in a single action.

### Process

```text
Create PO (destination = selected Outlet or Warehouse)
    ↓
Approve Automatically
    ↓
Generate GRN Automatically
    ↓
Update Destination Stock (Outlet or Warehouse)
    ↓
Mark PO Completed
```

### Result

- Stock updated immediately at the selected Outlet or Warehouse
- Inventory movements (stock ledger, transaction type `GRN`) recorded against the same location
- System-generated GRN created
- Purchase receipt journal posted with the Outlet or Warehouse dimension
- Full audit log maintained

### Safeguards (unchanged)

- PO, GRN, inventory, stock ledger and journal are written in one database transaction; any failure rolls back everything.
- Duplicate-submission protection: the client sends an `idempotencyKey`; the key is unique per destination location, and a repeated request returns the existing PO/GRN with `isDuplicateRequest = true` without repeating stock effects.
- The action is audited as "Purchase & Receive Immediate", including failures.

---

# GRN (Goods Received Note)

## Manual GRN

Created against Approved Purchase Orders.

Goods are always received into the PO's destination location (Outlet or Warehouse). The GRN screen shows "Receive Into: Outlet/Warehouse: name" and the user cannot change it; the user must have access to that location.

### Supported

- Full Receive
- Partial Receive
- Multiple Receipts
- Damaged Quantity
- Batch Tracking
- Serial Tracking
- Expiry Tracking

---

## Auto Generated GRN

Generated automatically when Purchase & Receive Immediately is used.

### Required Information

| Field |
|---------|
| GRN Number |
| PO Number |
| Supplier |
| Location Type |
| Location |
| Received By |
| Received Date |
| Source |

Source Value:

```text
AUTO_GENERATED_FROM_PURCHASE_ORDER
```

---

# Stock Update Rules

| Action | Stock Updated |
|----------|----------|
| Draft PO | No |
| Pending Approval | No |
| Approved PO | No |
| GRN Created | No (draft receipt) |
| GRN Completed | Yes — at the PO's destination Outlet or Warehouse |
| Purchase & Receive Immediately | Yes — at the selected Outlet or Warehouse |

---

# Partial Receiving

## Example

Ordered:

```text
Product A = 100
```

Received:

```text
First GRN = 60
```

Result:

```text
Received = 60
Remaining = 40
Status = Partially Received
```

Second GRN:

```text
Receive Remaining 40
```

Result:

```text
Received = 100
Status = Fully Received
```

---

# Over Receiving

Default behavior:

```text
Ordered = 100
Receive = 105
```

System should block.

### Exception

Allowed only for users with:

```text
ALLOW_OVER_RECEIVING
```

permission.

---

# Purchase Return

## Purpose

Return goods back to supplier.

### Result

- Reduce stock at the location that originally received the goods (Outlet or Warehouse)
- Create supplier return transaction
- Reference original PO and GRN

---

# Approval Limits

| Role | Approval Limit |
|---------|---------|
| Warehouse Manager | 50,000 |
| Purchase Manager | 200,000 |
| Admin | Unlimited |

### Rule

If PO value exceeds approval limit:

```text
Higher approval required
```

---

# Enterprise Features

## Supplier Cost History

Track:

- Last Purchase Price
- Average Cost
- Highest Cost
- Lowest Cost

---

## Duplicate Purchase Detection

Warn user if:

- Same supplier
- Same product
- Existing PO not received

---

## Multi-Location Support

A Purchase Order belongs to a single destination location: one Outlet or one Warehouse.

Stock updates only affect that location. The same product variant keeps separate stock balances per location.

---

# Audit Log

Track:

- Created By
- Updated By
- Approved By
- Rejected By
- Received By
- Cancelled By

Store:

- User
- Action
- Date Time
- Previous Value
- New Value

---

# Notifications

Trigger notifications for:

- Purchase Submitted
- Purchase Approved
- Purchase Rejected
- Purchase Returned
- Goods Received
- Purchase Completed

---

# Reports

## Purchase Reports

- Purchase Order Report
- Pending Approval Report
- Approved Not Received Report
- GRN Report
- Supplier Purchase Report
- Product Purchase History
- Location Purchase Report (Outlet / Warehouse)
- Purchase Return Report

## Screens and Documents

Purchase Order list, Purchase Order details, GRN list, GRN details, GRN create, and the PO / GRN PDFs show **Location Type** and **Location** instead of assuming a Warehouse:

- PO list: "Location Type" and "Location" columns; the location filter offers All Locations, All Outlets, All Warehouses, or a specific Outlet/Warehouse.
- PO PDF: "Ship To (Outlet)" or "Ship To (Warehouse)" with the location name.
- GRN PDF: "Received At (Outlet)" or "Received At (Warehouse)" with the location name.

---

# Acceptance Criteria

## AC1

Given a user has Purchase permission

When a Purchase Order is created

Then the system shall save the Purchase Order in Draft status.

## AC2

Given a Purchase Order is Pending Approval

When an authorized user approves it

Then the Purchase Order shall move to Approved status.

## AC3

Given a Purchase Order is Approved

When a GRN is completed

Then stock shall increase at the PO's destination Outlet or Warehouse.

## AC4

Given a user has Immediate Receive permission

When Purchase & Receive Immediately is selected

Then:

- PO shall be completed
- Stock shall be updated at the selected Outlet or Warehouse
- System GRN shall be generated

## AC5

Given a Purchase Order is partially received

When only part of the quantity is received

Then the system shall:

- Update stock for received quantity at the PO's destination location
- Keep remaining quantity open
- Mark PO as Partially Received

## AC6 — Outlet-only Business

Given a Business has Outlets but no Warehouses

When a user purchases into an Outlet (draft/approval or Purchase & Receive)

Then the PO, GRN, inventory and stock ledger shall reference that Outlet, and no Warehouse shall be required.

## AC7 — Mixed Business

Given a Business has both Outlets and Warehouses

When the user selects a Location Type

Then the Location dropdown shall list only accessible locations of that type, and stock shall be kept separately per location.

## AC8 — Location access

Given a user is not authorized for a location, or the location belongs to another Business

When the user creates, edits, purchases-and-receives or receives a PO for that location

Then the request shall be rejected (403 for access, 404 for another Business) and no stock shall change.

## AC9 — Backward compatibility

Given existing Warehouse-based Purchase Orders and clients that send only `warehouseId`

When they are listed, approved, received or created

Then they shall continue to work unchanged, with `locationType = warehouse`.