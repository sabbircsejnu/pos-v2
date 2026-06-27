# List Page Filter Standards

Updated: 2026-06-13

## Overview

This document defines the **application-wide reusable filtering standard** for all list, grid, and report pages in RetailPOS. The goal is to ensure a consistent, scalable, and user-friendly filtering experience across modules.

This standard is mandatory for:

- List pages
- Grid pages
- Report pages
- Management pages with searchable/filterable datasets

---

## 1. Purpose

The filtering UX must:

- Keep frequently used filters always visible.
- Move less frequently used filters into a structured Advanced Filter drawer.
- Clearly indicate when advanced filters are active.
- Preserve filter, sort, and pagination state across navigation.
- Behave predictably on desktop and mobile/tablet layouts.

---

## 2. Basic Filters (Always Visible)

Basic filters are high-frequency controls and must remain visible on the page header or filter bar at all times.

### Standard Basic Filters

Use these where applicable:

- Search
- Status
- Category
- Date Range

### Rules

- Basic filters must be available without opening any drawer or modal.
- Basic filters should update result sets using the page's standard query/filter flow.
- Basic filters must use consistent labels and placement across modules.
- If a module does not support one of the standard basic filters, omit it cleanly rather than replacing it with unrelated controls.

---

## 3. Advanced Filters (Drawer-Based)

Less frequently used or module-specific filters must be grouped under **Advanced Filters**.

### Common Advanced Filter Examples

- Price Range
- Variants
- Supplier
- Warehouse
- Stock Range
- Custom module-specific filters

### Rules

- Do not overload the main list header with rarely used filters.
- Keep advanced controls inside the Advanced Filter drawer.
- Group related controls logically within the drawer (for example, inventory-focused or supplier-focused groups).
- New module-specific advanced filters must be added inside the drawer unless they become high-frequency and justify promotion to Basic Filters.

---

## 4. Advanced Filter Drawer Standard

The Advanced Filter drawer is the canonical container for extended filtering controls.

### Behaviour

- Opens from the **right side**.
- Must follow the existing **Audit Event Details** drawer design pattern (structure, spacing, close behavior, and interaction conventions).

### Required Actions

The drawer must provide these actions:

- Apply Filters
- Clear Filters
- Close Drawer

### Interaction Rules

- **Apply Filters**: applies selected advanced filter values and refreshes the dataset.
- **Clear Filters**: resets advanced filter values to default/empty state.
- **Close Drawer**: closes without introducing inconsistent state behavior.
- Drawer action placement and button hierarchy should remain consistent across modules.

---

## 5. Active Filter Indicators

Pages must visibly indicate when advanced filters are active.

### Indicator Requirement

- Show the Advanced Filters trigger label with a dynamic active count.

### Examples

- Advanced Filters
- Advanced Filters (2)

### Rules

- Count must reflect only active advanced filters (not basic filters).
- Count updates immediately when filters are applied or cleared.
- If no advanced filters are active, show the base label without count.

---

## 6. State Preservation (Mandatory)

All filter behavior must comply with:

- [LIST_STATE_PRESERVATION.md](LIST_STATE_PRESERVATION.md)

### Mandatory Preservation Scope

When returning to a list/grid/report page, the following must remain unchanged:

- Filters (basic and advanced)
- Sorting
- Pagination

### Implementation Expectation

- Use the same list-state strategy defined in [LIST_STATE_PRESERVATION.md](LIST_STATE_PRESERVATION.md), with URL query parameters as the preferred mechanism.
- Advanced filter values must be included in the page state model and restored consistently.

---

## 7. Responsive Behaviour

Filtering UX must adapt by screen size while keeping behavior consistent.

### Desktop

- Advanced filters open in a right-side drawer.

### Tablet/Mobile

- Advanced filters open in a full-width drawer or responsive filter panel.
- Controls and action buttons must remain accessible without layout breakage.

### Responsive Rules

- Maintain action parity across device sizes (Apply, Clear, Close).
- Preserve identical filter semantics across breakpoints.
- Avoid device-specific behavior that changes filtering results or state logic.

---

## 8. Standard Adoption Scope (Future Usage)

This standard applies immediately to all existing and future modules, including:

- Product
- Inventory
- Purchase
- Sales
- Customer
- User Management
- Reports
- Audit Log
- Any future modules

No new list/grid/report page should be delivered without compliance with this document.

---

## 9. Compliance Checklist

A page is compliant only if all items below are true:

- [ ] Basic filters are visible and follow the standard.
- [ ] Less-used filters are inside the Advanced Filter drawer.
- [ ] Drawer opens from the right on desktop and follows the Audit Event Details pattern.
- [ ] Drawer includes Apply Filters, Clear Filters, and Close Drawer actions.
- [ ] Advanced filter active count is visible and accurate.
- [ ] Filter, sorting, and pagination state are preserved per LIST_STATE_PRESERVATION.
- [ ] Responsive behavior is implemented for desktop, tablet, and mobile.

---

## 10. Related Documents

- [FRONTEND_ARCHITECTURE.md](FRONTEND_ARCHITECTURE.md)
- [LIST_STATE_PRESERVATION.md](LIST_STATE_PRESERVATION.md)
- [REPORT_FILTER_STANDARDS.md](../11_Reports_And_Dashboard/REPORT_FILTER_STANDARDS.md)
