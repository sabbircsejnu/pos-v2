# REPORT_FILTER_STANDARDS

## Overview

This document defines the standard filtering behaviour for all reports across IraniMart POS V2.

The purpose is to ensure that all reports use consistent, predictable, and user-friendly filtering behaviour.

---

# Scope

This standard applies to:

- Sales Reports
- Inventory Reports
- Purchase Reports
- Supplier Reports
- Customer Reports
- Financial Reports
- Accounting Reports
- Dashboard Drill-Down Reports
- Audit Reports
- Custom Reports

---

# Core Principles

## Consistency

All reports must use the same filter behaviour and layout pattern.

## Accuracy

Filters must always apply to the full dataset before pagination.

## Usability

Users should be able to quickly find, apply, reset, and understand filters.

---

# Standard Filter Fields

Common report filters may include:

- Outlet
- Warehouse
- Date Range
- Date From
- Date To
- Product
- Product Variant
- Category
- Sub Category
- Brand
- Supplier
- Customer
- Payment Method
- Status
- User
- Barcode
- SKU
- Reference Number

---

# Date Filter Standards

Every date-based report should support:

- Today
- Yesterday
- Last 7 Days
- Last 30 Days
- This Month
- Last Month
- Custom Date Range

---

# Date Range Behaviour

When a predefined date range is selected:

- Date From should update automatically
- Date To should update automatically
- Report data should not refresh until user clicks Get Report unless auto-refresh is specifically required

When Custom Date Range is selected:

- Date From must be editable
- Date To must be editable

---

# Outlet Filter

Reports must support outlet filtering where applicable.

Options should include:

- All Outlets
- Specific Outlet

Store managers should only see outlets they are allowed to access.

---

# Warehouse Filter

Reports must support warehouse filtering where applicable.

Options should include:

- All Warehouses
- Specific Warehouse

Warehouse options may depend on selected outlet.

---

# Product Filter

Product filter should support:

- Product Name
- Product Code
- SKU
- Barcode

Product search should be searchable for large product lists.

---

# Filter Application Rule

Filters must apply to:

- Table data
- Sticky footer totals
- Summary cards
- Exported data
- Charts
- Dashboard drill-downs

---

# Pagination Rule

Filters must be applied before pagination.

Correct order:

1. Apply filters
2. Calculate totals
3. Apply pagination
4. Display visible rows

---

# Reset Filter

Every report should provide a clear reset option.

Reset should:

- Clear all optional filters
- Restore default date range
- Restore default outlet selection
- Refresh report data after user clicks Get Report

---

# Saved Filters

Where applicable, users should be able to save frequently used filter combinations.

Saved filters should include:

- Filter name
- Filter values
- Created by user
- Created date

---

# Default Filter Values

Default values should be sensible.

Recommended defaults:

- Outlet: All permitted outlets
- Date Range: Today
- Status: All
- Product: Empty
- Customer: Empty
- Supplier: Empty

---

# Filter Validation

The system must validate:

- Date From cannot be greater than Date To
- Required filters must be selected
- Invalid values must not be submitted
- User must not access unauthorized outlet or warehouse data

---

# Large Dataset Behaviour

For large dropdowns, the system should use searchable dropdowns or server-side search.

Applicable fields:

- Product
- Customer
- Supplier
- Barcode
- SKU

---

# Filter Visibility

Filters should be visible at the top of the report.

For reports with many filters:

- Show primary filters by default
- Put advanced filters under expandable Advanced Filter section

---

# Audit Requirements

For exported reports, the applied filters should be included in the export header where applicable.

Example:

Report: Summary Sales Report  
Outlet: All Outlets  
Date Range: Today  
Date From: 07/06/2026  
Date To: 07/06/2026  

---

# Acceptance Criteria

## AC-01

All report filters follow the same layout and behaviour.

## AC-02

Filters apply to the full dataset before pagination.

## AC-03

Footer totals update based on filtered data.

## AC-04

Exported reports use the same filters as the screen report.

## AC-05

Unauthorized outlet or warehouse data is not visible.

## AC-06

Date range validation prevents invalid date selection.

## AC-07

Large dropdowns support searchable selection.

## AC-08

Reset filter restores default report state.