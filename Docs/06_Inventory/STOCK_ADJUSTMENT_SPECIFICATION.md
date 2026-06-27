# Stock Adjustment Module Specification

## Purpose

Stock Adjustment is used to reconcile inventory discrepancies between recorded system stock and actual physical stock.

Stock Adjustment SHALL be used only for inventory correction transactions.

Stock Adjustment SHALL NOT be used for:

* Purchase Receiving
* Sales Processing
* Stock Transfer
* Customer Returns
* Supplier Returns

These operations update inventory through their respective transaction workflows.

---

# Location Access Model

Inventory operation permissions and inventory location access are separate concerns.

## Permission

Permissions determine which operations a user is authorized to perform.

Examples:

* inventory.view
* stock_adjustments.create
* stock_adjustments.edit
* stock_adjustments.approve

## Location Scope

Location Scope determines which warehouses and outlets a user is authorized to access.

Every user SHALL have exactly one Location Scope.

Supported scopes:

* AssignedLocationsOnly
* SelectedLocations
* AllLocations

---

# AssignedLocationsOnly

Users may access only locations explicitly assigned to them.

Examples:

* Warehouse Manager
* Outlet Manager

The system SHALL NOT expose any location outside the assigned locations list.

---

# SelectedLocations

Users may access only locations selected by administrators.

Examples:

* Operations Manager
* Regional Manager

The system SHALL NOT expose locations outside the selected location list.

---

# AllLocations

Users may access all warehouses and outlets within the business.

Examples:

* Business Owner
* System Administrator

Default outlet assignment SHALL NOT be required for users operating under AllLocations scope.

---

# Stock Adjustment Workflow

Supported statuses:

* Draft
* PendingApproval
* Approved
* Rejected
* Cancelled

Inventory quantities SHALL NOT be updated while the adjustment remains in Draft or PendingApproval status.

Inventory quantities SHALL be updated only when the adjustment reaches Approved status.

Rejected and Cancelled adjustments SHALL NOT modify inventory quantities.

---

# Multi Product Support

A Stock Adjustment transaction SHALL support multiple products.

A Stock Adjustment SHALL contain one or more adjustment lines.

Each line SHALL represent a single product variant.

Duplicate product variants SHALL NOT be allowed within the same adjustment transaction.

---

# Product Search

Product search SHALL support:

* Main Product Code
* SKU
* Barcode
* Product Name
* Variant Attributes

The search experience SHALL be consistent with:

* Purchase Order
* Stock Transfer
* Sales

modules.

---

# Adjustment Line Structure

Each adjustment line SHALL contain:

* Product Variant
* Main Product Code
* SKU
* Current Stock
* Adjustment Quantity
* Projected Stock
* Reason
* Notes

Projected Stock SHALL be calculated as:

Projected Stock = Current Stock + Adjustment Quantity

---

# Adjustment Reasons

Supported system reasons:

* Found
* Damaged
* Expired
* Lost
* Stolen
* OpeningBalanceCorrection
* StockCountCorrection
* SystemCorrection
* Other

Administrators SHALL be able to manage reason master data.

---

# Validation Rules

The following fields are required:

* Location Type
* Location
* Product Variant
* Adjustment Quantity
* Reason

Adjustment Quantity SHALL NOT be zero.

Negative stock SHALL NOT be allowed unless the business inventory configuration explicitly enables negative inventory.

---

# Permission Structure

## Inventory

* inventory.view

## Stock Adjustment

* stock_adjustments.view
* stock_adjustments.create
* stock_adjustments.edit
* stock_adjustments.delete
* stock_adjustments.approve
* stock_adjustments.reject

---

# Audit Requirements

The system SHALL create an audit record for every adjustment lifecycle event.

Captured fields SHALL include:

* Adjustment Number
* Status
* User
* Role
* Warehouse/Outlet
* Product Variant
* Previous Quantity
* Adjustment Quantity
* New Quantity
* Reason
* Timestamp
* Approval Information

Audit records SHALL be immutable.
