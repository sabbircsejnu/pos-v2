# BARCODE_LABEL_MODULE.md

# Barcode Label Module

## Document Information

| Field   | Value                    |
| ------- | ------------------------ |
| Module  | Barcode Label Module     |
| System  | IraniMart POS v2         |
| Status  | Approved Requirement     |
| Version | 1.0                      |
| Type    | Functional Specification |

---

# 1. Overview

The Barcode Label Module provides a centralized barcode generation and printing system for product variants throughout the platform.

The system operates using Product Variants as the primary inventory unit.

All barcode printing activities will therefore be based on variants rather than master products.

The module must support:

* Retail Stores
* Fashion Stores
* Borka Shops
* Hijab Shops
* Panjabi Stores
* Cosmetics
* Perfumes
* Supermarkets
* Warehouses

---

# 2. Business Objectives

The module must allow users to:

* Generate barcode labels for variants
* Print labels individually
* Print labels in bulk
* Print labels after stock receiving
* Print labels from inventory
* Generate print-ready PDFs
* Print directly to thermal printers
* Maintain multiple barcode templates
* Support business-specific templates

---

# 3. Core Business Rule

## Variant-Based Barcode Printing

All operational modules use Product Variants.

Examples:

* Sales
* Purchase Orders
* Inventory
* Goods Receiving
* Stock Transfers
* Stock Adjustments

Therefore:

Barcode labels must always represent a Product Variant.

Example:

Product:

```text
Borka Embro Gher Panel Black
```

Variant:

```text
Color: Black
Size: 56
Fabric: Korean Georgette

SKU: ABA424P-BLK-56
Barcode: CM00181
```

The printed barcode label should represent the Variant.

---

# 4. Barcode Label Center

## Menu

```text
Catalog
 └── Barcode Labels
```

Purpose:

Centralized barcode generation and printing screen.

---

# 5. Search Functionality

Users must be able to search by:

* Variant SKU
* Variant Barcode
* Product Name
* Product Code

---

# 6. Search Result Display

Autocomplete results should display:

```text
Borka Embro Gher Panel Black

SKU: ABA424P-BLK-56

Color: Black
Size: 56
Fabric: Korean Georgette

Barcode: CM00181
```

---

# 7. Barcode Print Queue

Selected variants must be added to a print queue.

Columns:

| Column   |
| -------- |
| Product  |
| Variant  |
| SKU      |
| Barcode  |
| Price    |
| Quantity |
| Action   |

---

# 8. Barcode Templates

The system must support multiple templates.

---

## Template 1 – Small Barcode Label

Recommended for:

* Product stickers
* Warehouse labels

Contents:

* SKU
* Barcode

Example:

```text
SKU: ABA424P-BLK-56

████████████
```

---

## Template 2 – Retail Price Label

Recommended for:

* Retail shelves
* Borka stores
* Hijab stores
* Panjabi stores

Contents:

* Product Name
* Variant Information
* Barcode
* Selling Price

Example:

```text
Borka Embro Gher Panel Black

Black | Size 56

████████████

MRP: 7490
```

---

## Template 3 – Detailed Variant Label

Contents:

* Product Name
* Variant Information
* SKU
* Barcode
* Price

Example:

```text
Borka Embro Gher Panel Black

Color: Black
Size: 56
Fabric: Korean Georgette

SKU: ABA424P-BLK-56

████████████

MRP: 7490
```

---

# 9. Supported Label Sizes

## Small

40mm × 25mm

## Standard

50mm × 25mm

## Large

60mm × 40mm

## Shelf Label

80mm × 50mm

## A4 Sheet

Multiple labels per page.

Default:

```text
60mm × 40mm
```

---

# 10. Barcode Quantity

Users must be able to define how many labels will be generated.

Example:

```text
Variant:
Black | Size 56

Quantity:
25
```

Result:

25 labels generated.

---

# 11. Barcode Format

Supported formats:

## Code 128

Default barcode standard.

Advantages:

* Compact
* Fast scanning
* Industry standard
* Supports alphanumeric values

---

# 12. PDF Generation

The system must support PDF generation.

Requirements:

* Multiple labels per page
* Print-ready layout
* Automatic page breaks
* Consistent spacing
* Proper alignment

Recommended Library:

```text
QuestPDF
```

---

# 13. Printing Entry Points

Barcode printing should be available from multiple locations.

---

## Product Details

Action:

```text
Print Barcode
```

---

## Inventory

Actions:

```text
Print Barcode
Bulk Print Barcode
```

---

## Purchase Receiving (GRN)

Actions:

```text
Receive
Receive & Print Labels
```

This is expected to be the most frequently used workflow.

---

## Stock Adjustment

Action:

```text
Print Barcode
```

---

## Stock Transfer

Action:

```text
Print Barcode
```

---

# 14. Bulk Barcode Printing

Users must be able to:

* Select multiple variants
* Set quantities per variant
* Generate a single PDF

Example:

| SKU            | Quantity |
| -------------- | -------- |
| ABA424P-BLK-56 | 20       |
| ABA424P-BLK-58 | 15       |
| ABA917P-100ML  | 10       |

---

# 15. Thermal Printer Support

The initial release must support:

* Zebra Printers
* XPrinter
* TSC Printers
* Generic Thermal Printers

Supported label sizes:

* 40mm × 25mm
* 50mm × 25mm
* 60mm × 40mm
* 80mm × 50mm

Users should be able to print directly without generating an A4 PDF.

---

# 16. Company Branding Support

Businesses may optionally display:

* Company Name
* Company Logo

Example:

```text
AHIR BORKA BAZAR

[LOGO]

Borka Embro Gher Panel Black

Black | Size 56

████████████

MRP: 7490
```

---

# 17. Template Management

Businesses should be able to create multiple barcode templates.

Examples:

* Retail Label
* Warehouse Label
* Shelf Label
* Product Sticker

Each template should allow enabling/disabling:

* Product Name
* SKU
* Barcode
* Price
* Variant Information
* Company Name
* Company Logo

---

# 18. Template Designer

A visual template designer must be provided.

Users should be able to:

* Select label size
* Select paper size
* Rearrange fields
* Preview labels

Supported fields:

* Product Name
* Variant SKU
* Barcode
* Variant Attributes
* Selling Price
* Company Name
* Company Logo

---

# 19. Direct Printing

Supported printing methods:

## PDF Printing

Generate PDF and print manually.

## Browser Printing

Print directly from browser.

## Thermal Printing

Print directly to connected thermal printers.

---

# 20. Business-Specific Templates

Each business should maintain its own templates.

Example:

```text
Business A
 ├── Retail Label
 ├── Warehouse Label

Business B
 ├── Product Sticker
 ├── Shelf Label
```

Templates must be isolated per business.

---

# 21. Permissions

New permissions:

```text
barcode.view
barcode.print
barcode.bulk_print
barcode.template_manage
```

---

# 22. Audit Log

The system must track:

* User
* Business
* Outlet
* Print Date
* Variant SKU
* Barcode
* Quantity Printed

---

# 23. Database Design

## BarcodeTemplates

Stores barcode templates.

## BarcodeTemplateFields

Stores template field configuration.

## BarcodePrintHistory

Stores print history.

---

# 24. Acceptance Criteria

* User can search variants.
* User can select variants.
* User can define print quantity.
* User can select label template.
* User can select label size.
* User can generate PDF.
* User can print directly.
* Thermal printers are supported.
* Multiple templates are supported.
* Business-specific templates are supported.
* Audit logs are recorded.
* Bulk printing is supported.
* Printing is available from Product Details, Inventory, GRN, Stock Transfer, and Stock Adjustment modules.
