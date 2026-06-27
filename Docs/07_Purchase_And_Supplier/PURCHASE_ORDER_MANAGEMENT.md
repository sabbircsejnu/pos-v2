# Purchase Order Management

## Purpose

This document defines the Purchase Order process, approval workflow, goods receiving process, stock update rules, and audit requirements.

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

## Warehouse Manager

Can:

- Create Purchase Order
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
| Warehouse | Yes |
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
- Warehouse required
- At least one item required

---

## Pending Approval → Approved

Approver reviews and approves PO.

### Result

- Status = Approved
- No stock update

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
Create PO
    ↓
Approve Automatically
    ↓
Generate GRN Automatically
    ↓
Update Warehouse Stock
    ↓
Mark PO Completed
```

### Result

- Stock updated immediately
- System-generated GRN created
- Full audit log maintained

---

# GRN (Goods Received Note)

## Manual GRN

Created against Approved Purchase Orders.

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
| Warehouse |
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
| GRN Created | Yes |
| Purchase & Receive Immediately | Yes |

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

- Reduce warehouse stock
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

## Multi Warehouse Support

Purchase Order must belong to a single warehouse.

Stock updates only affect that warehouse.

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
- Warehouse Purchase Report
- Purchase Return Report

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

When a GRN is created

Then warehouse stock shall increase.

## AC4

Given a user has Immediate Receive permission

When Purchase & Receive Immediately is selected

Then:

- PO shall be completed
- Stock shall be updated
- System GRN shall be generated

## AC5

Given a Purchase Order is partially received

When only part of the quantity is received

Then the system shall:

- Update stock for received quantity
- Keep remaining quantity open
- Mark PO as Partially Received