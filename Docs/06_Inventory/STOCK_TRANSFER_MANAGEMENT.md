# STOCK TRANSFER MANAGEMENT

## 1. PURPOSE

The Stock Transfer Management module enables controlled movement of inventory between inventory locations within a business.

The primary objectives are:

* Accurate inventory tracking
* Controlled stock movement
* Complete audit trail
* Transfer acknowledgement process
* Inventory accountability
* Prevention of stock discrepancies
* Real-time visibility of stock movement

The module must support both requisition-based and direct transfers while maintaining full inventory traceability.

---

# 2. BUSINESS CONTEXT

Inventory is maintained across multiple locations.

An inventory location may be:

* Warehouse
* Outlet

Inventory can be transferred between any supported locations.

Examples:

* Warehouse → Outlet
* Outlet → Outlet
* Warehouse → Warehouse (Future)
* Outlet → Warehouse (Return Transfer)
* Inter-Business Transfer (Future)

The same transfer engine must support all location combinations.

---

# 3. INVENTORY LOCATION CONCEPT

The system shall treat all stock-holding locations as Inventory Locations.

## Inventory Location Types

### Warehouse

Typically used for:

* Purchase Receiving
* Stock Adjustment
* Central Inventory Storage
* Stock Distribution

### Outlet

Typically used for:

* Retail Sales
* Local Inventory Storage
* Stock Requests
* Stock Receiving

All inventory transactions must occur between inventory locations.

---

# 4. TRANSFER TYPES

## 4.1 Requisition-Based Transfer

Used when a destination location requests inventory.

### Flow

Destination Location
→ Creates Requisition
→ Source Location Reviews
→ Transfer Created
→ Stock Dispatched
→ Stock Received

Example:

Outlet A requests stock from Main Warehouse.

Main Warehouse fulfills the request through a transfer.

---

## 4.2 Direct Transfer

Used when inventory is moved without a requisition.

### Flow

Source Location
→ Creates Transfer
→ Dispatches Stock
→ Destination Receives Stock

Example:

Main Warehouse transfers stock directly to Outlet A.

Example:

Outlet A transfers stock directly to Outlet B.

---

# 5. STOCK REQUISITION

## Purpose

Allows a destination location to request inventory from another location.

---

## Requisition Header

| Field               | Description      |
| ------------------- | ---------------- |
| Requisition No      | System Generated |
| Requesting Location | Outlet/Warehouse |
| Requested By        | User             |
| Request Date        | Date and Time    |
| Status              | Workflow Status  |
| Notes               | Optional         |

---

## Requisition Lines

| Field              | Description |
| ------------------ | ----------- |
| Product            |             |
| Variant            |             |
| Requested Quantity |             |
| Remarks            |             |

---

## Requisition Statuses

### Draft

Editable.

### Submitted

Awaiting review.

### Approved

Approved for fulfillment.

### Rejected

Request rejected.

### Partially Fulfilled

Some requested quantities transferred.

### Fully Fulfilled

All requested quantities transferred.

### Closed

No further action allowed.

---

# 6. STOCK TRANSFER

## Purpose

Represents movement of inventory between two inventory locations.

---

## Transfer Header

| Field                     | Description        |
| ------------------------- | ------------------ |
| Transfer No               | System Generated   |
| Transfer Type             | Direct/Requisition |
| Source Location Type      | Warehouse/Outlet   |
| Source Location           | Inventory Location |
| Destination Location Type | Warehouse/Outlet   |
| Destination Location      | Inventory Location |
| Related Requisition       | Optional           |
| Created By                | User               |
| Created Date              | Date and Time      |
| Status                    | Transfer Status    |
| Notes                     | Optional           |

---

## Transfer Lines

| Field              | Description |
| ------------------ | ----------- |
| Product            |             |
| Variant            |             |
| Requested Quantity |             |
| Transfer Quantity  |             |
| Unit Cost          |             |
| Remarks            |             |

---

# 7. TRANSFER LIFECYCLE

## Draft

Transfer created.

Inventory remains unchanged.

---

## Submitted

Transfer ready for dispatch.

Inventory remains unchanged.

---

## In Transit

Source location dispatches inventory.

### Inventory Impact

Source Location Stock:

Available Stock -= Transfer Quantity

Destination Location Stock:

No Change

Inventory is now considered In Transit.

Transfer remains open until receiving is completed.

---

## Received

Destination location accepts all quantities.

Transfer completed.

Destination inventory increased.

---

## Partially Received

Destination location accepts only part of the transfer.

Accepted quantity added to inventory.

Rejected quantity handled through return process.

---

## Rejected

Destination rejects all items.

No inventory added to destination.

Rejected quantities returned to source.

---

## Cancelled

Transfer cancelled before dispatch.

No inventory movement occurs.

---

# 8. INVENTORY MOVEMENT RULES

## Dispatch Rule

When transfer status becomes In Transit:

Source Inventory -= Transfer Quantity

Destination Inventory remains unchanged.

---

## Acceptance Rule

When transfer is received:

Destination Inventory += Accepted Quantity

---

## Partial Acceptance Rule

Only accepted quantities increase destination inventory.

Rejected quantities do not affect destination inventory.

---

## Rejection Rule

Rejected quantities must never increase destination inventory.

---

# 9. RECEIVING PROCESS

Receiving is performed by the destination location.

---

## Full Acceptance

Example:

Transferred Quantity = 100

Accepted Quantity = 100

Rejected Quantity = 0

Result:

Destination Stock += 100

Transfer Status = Received

---

## Partial Acceptance

Example:

Transferred Quantity = 100

Accepted Quantity = 90

Rejected Quantity = 10

Result:

Destination Stock += 90

Rejected Quantity = 10

Transfer Status = Partially Received

---

## Full Rejection

Example:

Transferred Quantity = 100

Accepted Quantity = 0

Rejected Quantity = 100

Result:

Destination Stock Unchanged

Transfer Status = Rejected

---

# 10. REJECTED STOCK HANDLING

## Recommended Approach

Rejected quantities must create a Return Transfer.

### Flow

Destination Location
→ Return Transfer
→ Source Location

Benefits:

* Complete audit trail
* Inventory accountability
* Accurate stock tracking
* Enterprise scalability

Direct inventory restoration is not recommended.

---

# 11. RETURN TRANSFER

## Purpose

Used to return inventory back to the original source location.

Examples:

* Damaged stock
* Wrong product supplied
* Excess stock returned
* Rejected transfer quantities

Return Transfers follow the same lifecycle as standard transfers.

---

# 12. INVENTORY MOVEMENT LEDGER

Every inventory movement must generate ledger records.

Ledger entries must be immutable.

---

## Supported Movement Types

* Purchase Receive
* Stock Adjustment
* Transfer Out
* Transfer In
* Return Transfer Out
* Return Transfer In
* Transfer Rejection
* Inventory Correction

---

## Ledger Fields

| Field                |
| -------------------- |
| Movement Id          |
| Product              |
| Variant              |
| Quantity             |
| Source Location      |
| Destination Location |
| Movement Type        |
| Reference Type       |
| Reference Number     |
| Created Date         |
| Created By           |

---

# 13. PERMISSIONS

## Business Owner

Can:

* View all transfers
* Create transfers
* Approve transfers
* Dispatch transfers
* Receive transfers
* View reports

---

## Warehouse Manager

Can:

* Create requisitions
* Review requisitions
* Create transfers
* Dispatch transfers
* Receive transfers
* View warehouse inventory

---

## Outlet Manager

Can:

* Create requisitions
* Create outlet-to-outlet transfers
* Receive transfers
* Accept transfers
* Reject transfers
* Partially receive transfers
* View outlet inventory

---

# 14. AUDIT TRAIL

The system must maintain complete audit history.

---

## Audit Information

* Created By
* Created Date
* Updated By
* Updated Date
* Approved By
* Approved Date
* Dispatched By
* Dispatched Date
* Received By
* Received Date
* Rejected By
* Rejected Date

All status changes must be logged.

Audit history must not be editable.

---

# 15. NOTIFICATIONS

The system shall generate notifications for:

## Requisition Submitted

Notify source location manager.

---

## Transfer Created

Notify destination location.

---

## Transfer Dispatched

Notify destination location.

---

## Transfer Received

Notify source location.

---

## Transfer Rejected

Notify source location.

---

## Return Transfer Created

Notify receiving location.

---

# 16. REPORTING

The system shall provide the following reports.

---

## Transfer Summary Report

Filter by:

* Date Range
* Source Location
* Destination Location
* Status

---

## In Transit Report

Displays all transfers currently in transit.

---

## Requisition Fulfillment Report

Shows:

* Requested Quantity
* Fulfilled Quantity
* Pending Quantity

---

## Rejected Stock Report

Displays rejected quantities by location.

---

## Return Transfer Report

Displays returned inventory.

---

## Stock Movement Report

Complete inventory movement history.

---

# 17. FUTURE SCALABILITY

The design must support future enhancements without database redesign.

Future features include:

* Multiple Warehouses
* Multiple Outlets
* Warehouse to Warehouse Transfers
* Outlet to Outlet Transfers
* Outlet to Warehouse Transfers
* Inter-Business Transfers
* Transfer Approval Workflow
* Multi-Level Approval
* Batch Tracking
* Lot Tracking
* Serial Number Tracking
* Barcode Scanning
* Mobile Receiving
* Vehicle Delivery Tracking

---

# 18. INVENTORY OWNERSHIP PRINCIPLE

A product can only belong to one inventory state at a time.

Possible inventory states:

* Available
* Reserved
* In Transit
* Damaged
* Returned

When a transfer is dispatched:

Available Stock → In Transit Stock

When accepted:

In Transit Stock → Available Stock

This principle must be enforced throughout the system to prevent stock duplication and inventory inconsistencies.

---

# 19. SOURCE OF TRUTH

Inventory quantities must always be derived from inventory transactions and movement ledger records.

The Inventory Movement Ledger shall be considered the system's source of truth for stock movement history and audit purposes.

---

# 20. SOURCE AND DESTINATION LOCATION RULES

## 20.1 Source Location Selection Rule

By default, users must not manually select source location during stock transfer creation.

Source location must be resolved from the user's assigned/default outlet or warehouse.

Source location must resolve from the user's Default Inventory Location.

The source location must be shown as read-only information in the create UI.

Only users with permission `stock_transfers.transfer_from_any_location` may manually select or change source location type and source location.

---

## 20.2 Destination Location Rule

Destination location is selected by the user.

Destination options may include any outlet/warehouse in business scope (not limited to assigned locations).

Destination location must not be the same as source location.

If source and destination are the same, transfer creation must be blocked.

---

## 20.3 Permission Enforcement Rule

Source location restrictions must be enforced by both frontend and backend.

UI read-only behavior alone is not sufficient.

Backend must reject create/update requests when a user without `stock_transfers.transfer_from_any_location` attempts to submit a source location different from their assigned/default authorized source location.

Users with `stock_transfers.transfer_from_any_location` may select any source location from their authorized scope.

---

## 20.4 Receive and Reject Rule

Receive/reject actions are allowed only when the transfer destination equals the current user's Default Inventory Location.

Backend must enforce this destination match rule.

---

# 21. PRODUCT SEARCH AND ITEM ENTRY RULES

## 21.1 Product Search Rule

Product search/add must remain disabled until all of the following are true:

* Source location is resolved
* Destination location is selected
* Source and destination are different

Reason:

Available source stock and destination current stock depend on selected source/destination locations.

---

## 21.2 Transfer Item Rule

Product-wise remarks are not required in stock transfer create UI.

Unit cost/cost price is not required in stock transfer create UI.

Requested Quantity is relevant only for requisition-based transfer and must be read-only.

Direct transfer must not display Requested Quantity.

---

## 21.3 Transfer Item Columns

### Direct Transfer

Use only:

* Product
* SKU / Variant
* Available at Source
* Current Stock at Destination (if available)
* Transfer Qty
* Action

### Requisition-Based Transfer

Use only:

* Product
* SKU / Variant
* Requested Qty (read-only)
* Available at Source
* Current Stock at Destination (if available)
* Transfer Qty
* Action

---

## 21.4 Quantity Validation

Transfer Qty must be greater than 0.

Transfer Qty must not exceed Available at Source.

Inline validation must be shown on invalid lines.

Submit and Submit & Dispatch actions must remain disabled while any line is invalid.
