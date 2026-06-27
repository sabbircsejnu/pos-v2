# Product Form UX Standards

Updated: 2026-06-13

## Overview

This document defines the mandatory UX standard for Product Create/Edit pages in RetailPOS. It establishes a reusable, modern ERP/eCommerce pattern inspired by Shopify, Odoo, Dynamics 365, Zoho Inventory, and ERPNext.

This standard is mandatory for:

- Product Create pages
- Product Edit pages
- Future product-like master data forms

---

## 1. Layout Standard

### 1.1 Section-Based Card Layout

Product forms must use separate cards for each logical section.

Required card sections:

- Basic Information
- Classification
- Pricing
- Inventory
- Variants
- Media

Each card must include:

- A clear title
- A short contextual description/help text
- Consistent spacing and visual grouping

### 1.2 Two-Column Desktop Structure

Desktop view must use a two-column information hierarchy.

- Left Column (~70%):
  - Basic Information
  - Classification
  - Pricing
  - Variants
- Right Column (~30%):
  - Product Status
  - Media Upload
  - Inventory Summary
  - Setup Progress
  - Quick Information

### 1.3 Responsive Behavior

- Desktop: 2-column split layout
- Tablet: reduced split, then stacked where needed
- Mobile: single-column layout with sticky action controls preserved

---

## 2. Form Section Standards

### 2.1 Basic Information

Must include:

- Product Name
- Description
- Product Code

### 2.2 Classification

Must include:

- Category
- Brand (future-ready)
- Tags (future-ready)

### 2.3 Pricing

Must include:

- Selling Price
- Cost Price (permission-controlled)
- Margin (calculated where cost is visible)
- Tax/related pricing fields as applicable

### 2.4 Inventory

Must include (or future-ready placeholders):

- Track Inventory
- Reorder Level
- Opening Stock
- Warehouse

### 2.5 Variants

Variants must not be hidden deep in the form.

Required behavior:

- Dedicated Variant card
- Add Variant action
- Variant list/grid/table
- Bulk variant generation support

Future-ready support:

- Size
- Color
- Material
- Custom attributes

### 2.6 Media

Must include:

- Image upload
- Gallery handling
- Primary image behavior

---

## 3. Sticky Action Standards

Action controls must remain visible while scrolling.

Desktop action set:

- Cancel
- Save Draft
- Save Product

Requirements:

- Actions must be in one horizontal row on desktop
- Save Product is the primary action
- Save Draft is a secondary action
- Mobile may use a sticky bottom action bar with equivalent actions

---

## 4. Permission-Controlled Fields

Sensitive data (such as cost) must follow permission-based visibility.

Standard permission:

- `products.view_cost`

Rules:

- Users without `products.view_cost` must not see cost fields in Product Create/Edit pages
- Users with the permission may view and edit cost fields
- Client-side hiding alone is insufficient; backend APIs must also enforce redaction where required

---

## 5. Product Setup Progress Standard

Product forms should include a setup progress card.

Expected behavior:

- Checklist of key sections
- Completed/Incomplete indicators
- Percentage completion display (e.g., 75% Complete)

Recommended checklist items:

- Basic Information
- Category / Classification
- Pricing
- Variants
- Media

---

## 6. Visual Hierarchy and Spacing

Form design must prioritize density and clarity for operational users.

Required principles:

- No long unstructured single-column forms
- Minimize unnecessary vertical whitespace
- Keep high-frequency fields visible and grouped
- Use consistent card padding and heading styles
- Maintain enterprise-grade readability and scanability

---

## 7. Compliance Checklist

A Product Create/Edit page is compliant only if all are true:

- [ ] Uses section-based cards with title and help text
- [ ] Uses two-column desktop layout (70/30 pattern)
- [ ] Includes sticky action controls (Cancel, Save Draft, Save Product)
- [ ] Uses dedicated Variant card and generation flow
- [ ] Includes media management card
- [ ] Includes setup progress indicator
- [ ] Applies `products.view_cost` permission to cost fields
- [ ] Maintains responsive behavior for desktop/tablet/mobile

---

## 8. Related Documents

- [FRONTEND_ARCHITECTURE.md](FRONTEND_ARCHITECTURE.md)
- [LIST_PAGE_FILTER_STANDARDS.md](LIST_PAGE_FILTER_STANDARDS.md)
- [LIST_STATE_PRESERVATION.md](LIST_STATE_PRESERVATION.md)
