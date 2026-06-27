# PDF Generation Module

Updated: 2026-06-13

## Purpose

This document defines the reusable PDF generation architecture for POS documents in RetailPOS v2. The goal is to render professional, branded, permission-aware PDFs from a shared template system instead of building a separate hardcoded PDF for each document type.

Supported output types:

- Purchase Order
- Sales Invoice
- Purchase Invoice
- Sales Return
- Purchase Return
- GRN / Goods Receive Note
- Stock Transfer
- Quotation / Estimate

## Current Project Structure Analysis

The repository already has the right separation for a reusable PDF feature:

- `src/RetailPOS.API` owns authenticated business workflows, document controllers, settings access, and file download responses.
- `src/RetailPOS.Infrastructure` owns persistence and can host any PDF-related data projection helpers if they are reused by multiple modules.
- `retailpos-frontend` already contains routed feature pages, shared services, and permission-aware UI patterns for list/detail screens.

Current gap:

- No PDF rendering package is wired into `RetailPOS.API` yet.
- Document-specific PDF rendering does not exist as a shared module.

Recommended placement:

- Backend rendering and orchestration should start in `src/RetailPOS.API` as a dedicated document module.
- Shared render contracts can live under a new `Documents/Pdf` area inside the API project.
- If the module grows, it can later be extracted into a separate backend project without changing the public API contract.

## Recommended Architecture

Use a single reusable document renderer with document-type-specific data mappers.

### Main layers

1. Document query layer
   - Loads the source entity and all required navigation data.
   - Applies business scoping and authorization checks.

2. Document mapping layer
   - Converts domain entities into a shared PDF view model.
   - Hides fields the user is not allowed to see.

3. Template rendering layer
   - Renders the reusable A4 layout.
   - Keeps the header, party blocks, item table, totals, notes, and footer consistent.

4. File delivery layer
   - Returns the generated PDF as a downloadable file response.
   - Sets a stable filename such as `PO-000123.pdf`.

### Suggested backend folder shape

```text
src/RetailPOS.API/
├── Controllers/
│   └── DocumentsController.cs                 # optional generic document download endpoint
├── DTOs/
│   └── Documents/
├── Documents/
│   ├── Pdf/
│   │   ├── Contracts/
│   │   ├── Templates/
│   │   ├── Mappers/
│   │   ├── Renderers/
│   │   └── Filenames/
│   └── Permissions/
└── Services/
    └── Documents/
```

## Reusable Template Structure

The PDF should be assembled from reusable layout parts so all document types share the same visual standard.

### Common sections

- Header
  - Blue brand bar
  - Document title on the left
  - Company logo on the right

- Document meta bar
  - Document number
  - Document date
  - Optional status / reference tags

- Company block
  - Company name
  - Address
  - Phone
  - Email
  - Website

- Party block
  - Supplier, customer, branch, warehouse, or vendor details
  - Ship To / Bill To split when applicable

- Item table
  - Row number
  - Item name and variant
  - Quantity
  - Unit
  - Rate
  - Discount
  - Tax/VAT
  - Line total

- Totals block
  - Subtotal
  - Discount
  - Tax/VAT
  - Grand total

- Notes block
  - Remarks
  - Special instructions
  - Terms and conditions

- Signature block
  - Prepared by
  - Approved by
  - Received by

- Footer
  - Generated date/time
  - Company identification
  - Optional page number

### Rendering rules

- Default format should be A4 portrait.
- The item table must continue across pages when rows are long.
- The table header must repeat on each new page.
- The totals block must remain at the end of the document, after all item rows.
- The footer must be repeated on every page or rendered consistently at the bottom.

## Template Engine Recommendation

Use a .NET PDF library that supports structured document composition, repeated table headers, page headers/footers, and page flow.

Recommended fit:

- QuestPDF, because it naturally supports reusable components, page headers, repeating table sections, and multi-page flow.

Implementation note:

- The architecture should stay library-agnostic at the service boundary so a future renderer swap does not change controllers or UI contracts.

## Shared Data Model

The document renderer should accept a common shape regardless of document type.

### Base request contract

| Field | Purpose |
|---|---|
| `documentType` | Purchase Order, Sales Invoice, and so on |
| `documentId` | Source entity identifier |
| `documentNumber` | Human-readable reference such as `PO-000123` |
| `documentDate` | Display date, formatted by system settings |
| `businessId` | Tenant/business scope |
| `companyInfo` | Branded company header content |
| `partyInfo` | Supplier/customer/vendor/warehouse block |
| `shipTo` | Shipping destination when required |
| `billTo` | Billing destination when required |
| `items` | Line item collection |
| `summary` | Subtotal, discount, tax, grand total |
| `notes` | Optional notes or instructions |
| `signatures` | Optional approval/receipt signature metadata |
| `visibilityContext` | Permission-sensitive field mask |

### Item line contract

| Field | Purpose |
|---|---|
| `lineNo` | Stable row number |
| `itemName` | Product/service name |
| `sku` | Optional stock keeping unit |
| `variantName` | Optional product variant |
| `description` | Optional line description |
| `quantity` | Ordered or received quantity |
| `unitName` | Unit of measure |
| `rate` | Unit price |
| `discount` | Line discount |
| `taxAmount` | Tax/VAT for the line |
| `lineTotal` | Net line amount |
| `cost` | Hidden unless user has cost permission |

### Type-specific source models

Each document type should map from its own domain model into the common PDF contract:

- Purchase Order -> purchase order entity and items
- Sales Invoice -> sales transaction or bill/invoice source
- Purchase Invoice -> supplier invoice or purchase bill source
- Sales Return -> return transaction source
- Purchase Return -> supplier return source
- GRN -> GRN entity and received items
- Stock Transfer -> transfer source and transfer items
- Quotation / Estimate -> quotation source and quoted items

## Supported Document Profiles

The shared renderer should expose a document profile per type, not a separate hardcoded PDF implementation.

| Document type | Primary party | Typical summary fields | Notes |
|---|---|---|---|
| Purchase Order | Supplier | subtotal, discount, tax, grand total | Include warehouse or branch if relevant |
| Sales Invoice | Customer | subtotal, discount, tax, grand total | May include paid/unpaid status |
| Purchase Invoice | Supplier | subtotal, discount, tax, grand total | May include due date and reference |
| Sales Return | Customer | return total, tax reversal, net credit | Show original invoice reference |
| Purchase Return | Supplier | return total, tax reversal, net debit | Show original purchase reference |
| GRN | Supplier / warehouse | received total, rejected total, variance | Show purchase order reference |
| Stock Transfer | From outlet/warehouse, to outlet/warehouse | transfer quantity, adjustments | Cost-sensitive fields should respect permissions |
| Quotation / Estimate | Customer | estimate subtotal, discount, tax, grand total | Can show validity date |

## API Endpoints

The document feature should expose download endpoints from the API layer. A generic endpoint is preferred, with optional convenience routes per document family.

### Recommended generic endpoint

- `GET /api/documents/{documentType}/{documentId}/pdf`

### Optional convenience endpoints

- `GET /api/purchase-orders/{id}/pdf`
- `GET /api/sales/{id}/pdf`
- `GET /api/purchase-invoices/{id}/pdf`
- `GET /api/sales-returns/{id}/pdf`
- `GET /api/purchase-returns/{id}/pdf`
- `GET /api/grns/{id}/pdf`
- `GET /api/stock-transfers/{id}/pdf`
- `GET /api/quotations/{id}/pdf`

### API response behavior

- Success returns a `FileResult` or equivalent downloadable response.
- `Content-Type` should be `application/pdf`.
- `Content-Disposition` should use `attachment` for direct download.
- The filename should be stable and human-readable, for example `PO-000123.pdf` or `INV-000456.pdf`.

### Suggested endpoint contract

| Input | Purpose |
|---|---|
| `documentType` | Selects the renderer profile |
| `documentId` | Loads the source record |
| Optional `preview` flag | Allows future inline preview support |

## UI Download Behavior

The frontend should not generate PDFs locally. It should request the PDF from the API and trigger a browser download.

### UI rules

- Add a shared `DocumentPdfService` in `retailpos-frontend/src/app/services`.
- Use the service from list pages, detail pages, and action menus.
- Show the download button only when the current user has the matching view permission.
- Disable or hide cost-sensitive details in the UI using the same permission rules as the PDF generator.

### UI flow

1. User opens a document detail page.
2. UI checks permission to view the document.
3. UI requests the PDF endpoint.
4. API returns the PDF file stream.
5. Browser downloads the file using the server-provided filename.

### Frontend integration points

- `retailpos-frontend/src/app/services/`
- `retailpos-frontend/src/app/pages/purchase-orders/`
- `retailpos-frontend/src/app/pages/grn/`
- `retailpos-frontend/src/app/pages/stock-transfers/`
- `retailpos-frontend/src/app/pages/sales/`
- `retailpos-frontend/src/app/pages/accounting/`

## Business Settings and Formatting

The PDF renderer must read visual and formatting values from company/business settings instead of hardcoding them.

### Required settings source data

- Company logo
- Company name
- Address
- Phone
- Email
- Website
- Currency code and symbol
- Date format
- Tax/VAT label

### Formatting rules

- Currency formatting must follow system currency settings.
- Date formatting must follow system date settings.
- Missing logo should fall back to a text brand block.
- Missing optional fields should not break layout.

## Permission Handling

Permission control must be enforced in the API before the PDF is generated and again in the data projection used by the template.

### Access rules

- Users may only download documents they are allowed to view.
- The download endpoint must be protected with the same permission model used by the rest of the API.
- Document visibility should match the module policy for the source document type.

### Cost-sensitive fields

- Purchase cost
- Unit cost
- Margin
- Gross profit
- Internal valuation fields

These fields must be omitted when the user does not have the relevant cost permission, such as `products.view_cost` or the module-specific cost-view permission used by the implementation.

### Recommended enforcement pattern

1. Authorize the request at the controller or endpoint level.
2. Build a permission-aware document snapshot.
3. Remove hidden fields before rendering.
4. Render the PDF from the filtered snapshot only.

## Implementation Notes by Document Type

The document system should use the same template but allow small profile differences:

- Purchase Order
  - Supplier-first party block
  - Expected receipt or warehouse block
  - Optional approval signature

- Sales Invoice
  - Customer-first party block
  - Payment summary or status block if needed

- Purchase Invoice
  - Supplier-first party block
  - Optional due date, reference, and tax references

- Sales Return and Purchase Return
  - Return reference to original document
  - Negative or reversal totals as appropriate

- GRN
  - Purchase order reference
  - Received vs rejected quantity visibility

- Stock Transfer
  - From / to location blocks
  - Transfer reference and movement status

- Quotation / Estimate
  - Validity date
  - Estimate status
  - Optional converted-to-order reference

## Future Extension Guideline

When a new document type is added later, do not clone the entire PDF template.

Instead:

1. Add a new source-to-snapshot mapper.
2. Add a new document profile for labels or small layout differences.
3. Reuse the shared header, table, totals, notes, and footer components.
4. Register the permission requirement for the new document type.
5. Add filename rules for the new reference prefix.

### Extension rules

- Keep the visual language identical across all document PDFs.
- Keep the API contract stable so the frontend uses one download flow.
- Keep the renderer permission-aware so sensitive fields cannot leak through alternate paths.
- Add tests for page breaks, long tables, and cost-field visibility before rollout.

## Suggested Acceptance Criteria

- All supported documents render from one shared template system.
- The table header repeats on every overflow page.
- Totals always appear after the final item row.
- Download endpoints return a clean PDF filename.
- Company branding and formatting come from settings.
- Users without permission cannot access or download restricted documents.
- Cost-sensitive fields are excluded when permission is missing.
