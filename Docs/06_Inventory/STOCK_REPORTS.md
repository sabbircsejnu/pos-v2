# STOCK_REPORTS

## Overview

The Stock Reports module provides complete visibility into inventory levels, stock movements, inventory valuation, replenishment requirements, and inventory audit trails across all outlets and warehouses.

The reporting system must support operational users, inventory managers, accountants, auditors, procurement teams, and business owners by providing accurate and actionable inventory insights.

---

# Objectives

The Stock Reports module must enable users to:

- Monitor real-time stock availability
- Track inventory movements
- Investigate stock discrepancies
- Measure inventory value
- Support stock audits
- Improve procurement planning
- Reduce stock shortages
- Identify dead and slow-moving inventory
- Improve inventory turnover
- Support business decision-making through inventory analytics

---

# Scope

This document covers:

- Current Stock Reporting
- Inventory Ledger Reporting
- Stock Movement Reporting
- Inventory Valuation Reporting
- Replenishment Reporting
- Inventory Audit Reporting
- Inventory Performance Reporting
- Inventory KPI Dashboard

---

# Global Report Features

## Common Filters

All stock reports should support the following filters where applicable:

- Outlet
- Warehouse
- Product
- Product Variant
- Category
- Sub Category
- Brand
- Supplier
- Barcode
- SKU
- Product Status
- Date Range
- Batch Number
- Serial Number

## Common Actions

All stock reports should support:

- Search
- Sort
- Column Selection
- Export to Excel
- Export to CSV
- Export to PDF
- Print
- Save Filter Presets
- Report Refresh
- Drill Down Navigation

## Permissions

### Inventory Manager

Full access to all stock reports.

### Store Manager

Access to assigned outlet reports.

### Accountant

Access to stock valuation reports.

### Business Owner

Access to all reports.

### Cashier

No access by default.

---

# Current Stock Report

## Purpose

Provides the current inventory position of all products.

## Business Value

Allows users to identify available stock and make informed purchasing and sales decisions.

## Filters

- Outlet
- Warehouse
- Category
- Brand
- Product
- Stock Status

## Columns

| Column | Description |
|----------|----------|
| Product Code | Unique product identifier |
| Barcode | Product barcode |
| Product Name | Product name |
| Category | Product category |
| Brand | Product brand |
| Outlet | Stock location |
| Warehouse | Storage location |
| Available Quantity | Available stock |
| Reserved Quantity | Reserved stock |
| Reorder Level | Configured reorder level |
| Unit Cost | Latest inventory cost |
| Stock Value | Quantity × Unit Cost |
| Last Purchase Date | Last purchase transaction |
| Last Sale Date | Last sales transaction |

## Summary Metrics

- Total Products
- Total Quantity
- Total Inventory Value

---

# Product Ledger Report

## Purpose

Provides complete inventory transaction history for a product.

## Business Importance

This is the primary inventory audit report.

Every stock movement must be traceable through the product ledger.

## Filters

- Product
- Outlet
- Warehouse
- Date Range
- Transaction Type

## Columns

| Column | Description |
|----------|----------|
| Transaction Date | Transaction timestamp |
| Transaction Type | Movement source |
| Reference Number | Related document |
| Product | Product name |
| Opening Quantity | Quantity before transaction |
| Stock In | Incoming quantity |
| Stock Out | Outgoing quantity |
| Closing Quantity | Quantity after transaction |
| Unit Cost | Transaction cost |
| Transaction Value | Total value |
| Performed By | User |
| Remarks | Additional notes |

## Supported Transaction Types

- Opening Stock
- Purchase
- Purchase Return
- Sale
- Sales Return
- Stock Transfer In
- Stock Transfer Out
- Stock Adjustment Increase
- Stock Adjustment Decrease
- Stock Count Variance
- Manufacturing
- Consumption

---

# Stock Movement Report

## Purpose

Shows inventory movement during a selected period.

## Columns

| Column | Description |
|----------|----------|
| Product | Product name |
| Opening Stock | Starting quantity |
| Stock In | Incoming quantity |
| Stock Out | Outgoing quantity |
| Closing Stock | Ending quantity |
| Net Movement | Total movement |

## Summary Metrics

- Total Incoming Quantity
- Total Outgoing Quantity
- Net Movement

---

# Stock Valuation Report

## Purpose

Provides the monetary value of inventory.

## Supported Valuation Methods

- FIFO
- Weighted Average Cost
- Standard Cost

## Columns

| Column | Description |
|----------|----------|
| Product | Product name |
| Quantity | Available quantity |
| Unit Cost | Cost per unit |
| Inventory Value | Total value |

## Summary Metrics

- Total Inventory Value

---

# Outlet Wise Stock Report

## Purpose

Compares stock positions across outlets.

## Columns

| Column | Description |
|----------|----------|
| Outlet | Outlet name |
| Product | Product |
| Quantity | Available stock |
| Stock Value | Inventory value |

---

# Low Stock Report

## Purpose

Identifies products below configured reorder levels.

## Rule

Current Stock < Reorder Level

## Columns

- Product
- Current Stock
- Reorder Level
- Suggested Order Quantity

---

# Out Of Stock Report

## Purpose

Identifies products that are unavailable.

## Rule

Current Stock = 0

## Columns

- Product
- Category
- Brand
- Last Purchase Date
- Last Sale Date

---

# Negative Stock Report

## Purpose

Identifies inventory inconsistencies requiring investigation.

## Rule

Current Stock < 0

## Severity

Critical

---

# Stock Adjustment Report

## Purpose

Tracks all manual inventory adjustments.

## Columns

- Date
- Product
- Adjustment Type
- Previous Quantity
- Adjusted Quantity
- Variance
- Reason
- User

---

# Stock Transfer Report

## Purpose

Tracks inventory movement between locations.

## Columns

- Transfer Number
- Date
- From Outlet
- To Outlet
- Product
- Quantity
- Status

---

# Stock Take Variance Report

## Purpose

Compares physical stock against system stock.

## Columns

- Product
- System Quantity
- Physical Quantity
- Variance Quantity
- Variance Value
- Investigation Status

---

# Reorder Report

## Purpose

Supports purchasing decisions.

## Rule

Current Stock <= Reorder Level

## Columns

- Product
- Current Stock
- Reorder Level
- Suggested Order Quantity
- Preferred Supplier

---

# Dead Stock Report

## Purpose

Identifies inventory that is not selling.

## Default Rule

No sales activity for 180 days.

## Columns

- Product
- Current Stock
- Stock Value
- Last Sale Date
- Days Since Last Sale

---

# Slow Moving Stock Report

## Purpose

Identifies products with low sales velocity.

## Columns

- Product
- Sales Quantity
- Average Monthly Sales
- Current Stock

---

# Fast Moving Stock Report

## Purpose

Identifies high-demand products.

## Columns

- Product
- Sales Quantity
- Sales Value
- Average Daily Sales

---

# Inventory Ageing Report

## Purpose

Measures inventory age distribution.

## Age Buckets

- 0-30 Days
- 31-60 Days
- 61-90 Days
- 91-180 Days
- 181-365 Days
- 365+ Days

---

# Batch Tracking Report

## Purpose

Tracks batch-controlled inventory.

## Columns

- Batch Number
- Product
- Manufacturing Date
- Expiry Date
- Available Quantity
- Remaining Quantity

---

# Expiry Report

## Purpose

Identifies products approaching expiry.

## Alert Groups

- Expiring Within 30 Days
- Expiring Within 60 Days
- Expiring Within 90 Days

---

# Inventory KPI Dashboard

## Inventory Health

- Total Inventory Value
- Total Inventory Quantity
- Total Active Products

## Risk Indicators

- Low Stock Count
- Out Of Stock Count
- Negative Stock Count

## Inventory Efficiency

- Inventory Turnover Ratio
- Dead Stock Value
- Slow Moving Stock Value

## Demand Analysis

- Fast Moving Products
- Top Selling Products
- Reorder Recommendations

---

# Audit Requirements

The system must:

- Record report generation time
- Record report generated by user
- Record report exports
- Maintain historical consistency
- Support inventory audits and investigations

---

# Acceptance Criteria

### AC-01

Users can generate stock reports using available filters.

### AC-02

Users can export reports in Excel, CSV, and PDF formats.

### AC-03

Users can drill down into stock transactions from reports.

### AC-04

Product Ledger must provide a complete audit trail for every inventory movement.

### AC-05

Reports must support multi-outlet and multi-warehouse operations.

### AC-06

Inventory valuation must be calculated according to the configured valuation method.

### AC-07

Role-based permissions must control report visibility.

### AC-08

Dashboard KPIs must refresh using real-time inventory data.