# POS Sale Phase 1 Implementation Specification

Owner: Engineering Team
Last Updated: 2026-07-05
Status: Draft for Review
Applies To: Backend API, Infrastructure, Frontend POS Screen, Database, Seed Data, Audit and Event Flows

## 1. Overview

This document defines the implementation specification for POS Sale Phase 1 in IraniMart POS v2.

This is an implementation-oriented engineering specification for GitHub Copilot Coding Agent and future developers. It defines technical scope, required architecture, data design, APIs, business rules, and acceptance criteria.

The implementation must follow existing project architecture and reuse existing modules where available. Duplicate entities, duplicate transaction logic, and duplicate pricing or inventory behavior are not allowed.

Primary implementation baseline already exists in:
- [src/RetailPOS.API/Services/SaleService.cs](src/RetailPOS.API/Services/SaleService.cs)
- [src/RetailPOS.API/Controllers/SalesController.cs](src/RetailPOS.API/Controllers/SalesController.cs)
- [src/RetailPOS.API/Controllers/PosController.cs](src/RetailPOS.API/Controllers/PosController.cs)
- [src/RetailPOS.API/Services/PosLookupService.cs](src/RetailPOS.API/Services/PosLookupService.cs)
- [retailpos-frontend/src/app/pages/pos/pos-screen.component.ts](retailpos-frontend/src/app/pages/pos/pos-screen.component.ts)
- [retailpos-frontend/src/app/pages/pos/pos-screen.component.html](retailpos-frontend/src/app/pages/pos/pos-screen.component.html)

## 2. Scope

Phase 1 scope is checkout-first POS execution with Draft to Completed and Void lifecycle support.

IN-SCOPE-01: Open POS sale page.
IN-SCOPE-02: Search and scan products, add to cart, update cart quantities, remove lines.
IN-SCOPE-03: Validate current outlet stock and block negative stock.
IN-SCOPE-04: Default customer behavior uses seeded Walk-in Customer record.
IN-SCOPE-05: Customer search and quick create or edit from POS using existing customer permission model.
IN-SCOPE-06: Line discount and invoice discount.
IN-SCOPE-07: Totals calculation (subtotal, discount, tax, grand total, paid, due, change).
IN-SCOPE-08: Payment required before completion.
IN-SCOPE-09: Payment methods: Cash, Card, Mobile Banking.
IN-SCOPE-10: Split payment support.
IN-SCOPE-11: Immediate inventory deduction after successful sale completion.
IN-SCOPE-12: Configurable invoice number generation.
IN-SCOPE-13: Receipt print and reprint support.
IN-SCOPE-14: 80mm thermal receipt default, A4 invoice option.
IN-SCOPE-15: Terminal-aware sale tracking.
IN-SCOPE-16: Void completed invoice with permission and policy window.
IN-SCOPE-17: Audit logs for critical actions.
IN-SCOPE-18: POS lifecycle domain events.
IN-SCOPE-19: Permission-controlled backdated POS sale creation using explicit SalesDate.

## 3. Out of Scope

OOS-01: Full return workflow implementation.
OOS-02: Full exchange workflow implementation.
OOS-03: Promotion engine execution.
OOS-04: Coupon and loyalty engine completion.
OOS-05: Full offline synchronization engine and conflict resolution.
OOS-06: Receipt template designer UI.
OOS-07: Hold and recall full feature expansion beyond structural compatibility.

## 4. Architecture Principles

AP-01: Checkout-first architecture, not CRUD-first.
AP-02: Reuse existing Sale aggregate and checkout transaction orchestration.
AP-03: Preserve layered architecture Core to Infrastructure to API to Frontend.
AP-04: Event-driven lifecycle with explicit sale state transition events.
AP-05: Configuration-first behavior for invoice, receipt, print, tax, and terminal defaults.
AP-06: Terminal-aware by design for multi-terminal outlets.
AP-07: Offline-ready by including client transaction key and idempotent processing patterns.
AP-08: Immutable enterprise audit trail for critical actions.
AP-09: Performance-sensitive POS paths use existing cache-enabled lookup and pricing services.
AP-10: No duplicate business logic across controllers, services, or frontend.

## 5. UI and UX Specification

UX-01: POS screen must follow existing IraniMart style tokens and controls.
UX-02: Must reuse global styles and utility classes from [retailpos-frontend/src/styles.css](retailpos-frontend/src/styles.css).
UX-03: Must preserve existing visual language in POS page and not introduce a conflicting design system.
UX-04: Product scan input must remain primary interaction entry point.
UX-05: Repeated barcode scan of same variant must increment quantity instead of duplicate block warning.
UX-06: Customer section must default to Walk-in Customer and visibly show active selected customer.
UX-07: Payment summary panel must remain permanently visible during checkout.
UX-08: Complete Sale action must be disabled when payment is insufficient.
UX-09: Quick customer create and edit controls must be permission-aware.
UX-10: Print and Reprint actions must be visible according to permission and sale state.

## 6. Screen Layout

SL-01: Top bar includes Outlet, Terminal, Cashier, Date and Time, and online status indicator placeholder.
SL-02: Main left area contains barcode and product search plus cart item table.
SL-03: Right panel contains subtotal, discount, tax, grand total, paid, due, change, payment controls, and Complete Sale button.
SL-04: Bottom action area includes New Sale, Print, Reprint, Void, Customer, Discount actions.
SL-05: Receipt modal remains available after successful completion with print trigger.
SL-06: Layout must support desktop and responsive behavior for standard tablet widths used by POS counters.

## 7. User Workflow

WF-01: Cashier opens POS page.
WF-02: POS initializes outlet, terminal, cashier context and default Walk-in Customer.
WF-03: Cashier scans barcode or searches product and adds lines.
WF-04: If same barcode scanned again, quantity of existing line increases by one.
WF-05: Cashier adjusts quantity and discounts as needed.
WF-06: Customer may be searched and selected when permitted.
WF-07: Customer may be quick-created or edited when permitted.
WF-08: Cashier selects payment method or split payments.
WF-09: System validates full payment, stock availability, and business rules.
WF-10: On success, sale completes in one transaction, inventory deducts, receipt generates, print starts, audit and events persist.
WF-11: Reprint is available after completion.
WF-12: Void is available only to authorized users within configured void window.

## 8. Component Specifications

COMP-01: PosShellComponent
- Responsibility: Compose full POS screen and orchestrate child components.
- Reuse: Existing [retailpos-frontend/src/app/pages/pos/pos-screen.component.ts](retailpos-frontend/src/app/pages/pos/pos-screen.component.ts).

COMP-02: PosTopBarComponent
- Responsibility: Show outlet, terminal, cashier, date and status metadata.

COMP-03: PosSearchComponent
- Responsibility: Barcode scan input, textual search, category filter entry.
- Dependency: [retailpos-frontend/src/app/services/pos.service.ts](retailpos-frontend/src/app/services/pos.service.ts).

COMP-04: PosCartComponent
- Responsibility: Cart line rendering, quantity update, remove line, line discount.

COMP-05: PosSummaryPaymentComponent
- Responsibility: Totals, payment method selection, split payment, complete action enablement.

COMP-06: PosCustomerPanelComponent
- Responsibility: Show default Walk-in Customer, search, select, clear, quick create or edit actions.
- Permissions: existing customers permission checks.

COMP-07: PosActionBarComponent
- Responsibility: New Sale, Print, Reprint, Void, Customer, Discount actions.

COMP-08: PosReceiptComponent
- Responsibility: Render receipt layout for 80mm and A4 output modes.

COMP-09: PosVoidDialogComponent
- Responsibility: Capture void reason and execute void action under permission and policy checks.

## 9. Functional Requirements

FR-001: System shall open POS sale page under sales.create permission.
FR-002: System shall support product lookup by barcode.
FR-003: System shall support product lookup by product name.
FR-004: System shall support product lookup by product code.
FR-005: System shall support product lookup by variant SKU.
FR-006: System shall support category and brand-aware search filters where data exists.
FR-007: System shall add scanned product to cart.
FR-008: System shall increment quantity when scanning same product again.
FR-009: System shall allow manual add from search results.
FR-010: System shall allow update quantity for each cart line.
FR-011: System shall allow remove cart line.
FR-012: System shall allow line discount input.
FR-013: System shall allow invoice-level discount input.
FR-014: System shall calculate subtotal, discount, tax, grand total, paid, due, change in real time.
FR-015: System shall use selling price as the only allowed sale unit price source.
FR-016: System shall block price override in Phase 1.
FR-017: System shall default selected customer to seeded Walk-in Customer record.
FR-018: System shall allow customer search based on customers.view permission.
FR-019: System shall allow quick create customer based on customers.create permission.
FR-020: System shall allow quick update customer based on customers.edit permission.
FR-021: System shall support payment methods Cash, Card, Mobile Banking.
FR-022: System shall support split payment across multiple payment methods.
FR-023: System shall allow sale completion only when full amount is paid.
FR-024: System shall complete sale in one backend transaction.
FR-025: System shall deduct outlet inventory immediately after successful sale commit.
FR-026: System shall generate configurable invoice number.
FR-027: System shall auto-print receipt after successful completion when auto-print enabled.
FR-028: System shall support 80mm thermal receipt output.
FR-029: System shall support A4 invoice output.
FR-030: System shall support receipt reprint for completed sales.
FR-031: System shall support sale void with permission and policy checks.
FR-032: System shall track outlet, terminal, and cashier per sale.
FR-033: System shall emit lifecycle events for critical sale stages.
FR-034: System shall persist audit records for critical sale actions.
FR-035: System shall preserve idempotency behavior for repeated checkout submissions.
FR-036: System shall allow backdated POS sale date selection only for users with sales.backdate permission.
FR-037: System shall hide Sales Order Date selector for users without sales.backdate permission.
FR-038: System shall default SalesDate to current system date and time when no SalesDate is supplied.
FR-039: System shall reject future SalesDate values for all users.
FR-040: System shall persist both SalesDate and CreatedAt for POS sales, where CreatedAt is always server time.

## 10. Business Rules

BR-001: Allowed sale states include Draft, Hold, Completed, Cancelled or Void, Returned, Exchange.
BR-002: Phase 1 implementation must execute Draft in-progress behavior and Completed and Void behavior only.
BR-003: Hold support may remain structural and is not required to be fully expanded.
BR-004: Return and Exchange execution are excluded from Phase 1.
BR-005: Negative stock is never allowed.
BR-006: Product price source is selling price only.
BR-007: Credit sale is not allowed in Phase 1.
BR-008: Payment must be fully collected before completion.
BR-009: Outlet stock scope must be used for POS sale validation.
BR-010: Tax behavior is configuration-driven.
BR-011: Walk-in Customer is a seeded system customer and cannot be null fallback.
BR-012: Walk-in Customer must be non-editable and non-deletable.
BR-013: Walk-in Customer must always remain active.
BR-014: Auto-print is enabled by default unless explicitly disabled in config.
BR-015: Void action requires sales.void permission only in Phase 1 and is not restricted by same-day or configured time window.
BR-016: Terminal association is mandatory for each completed sale.
BR-017: Backdated sales require sales.backdate permission and never allow future dates.
BR-018: SalesDate drives invoice date, sale reporting date, inventory transaction date, and accounting posting date.

## 11. Validation Rules

VR-001: Cart must contain at least one line before completion.
VR-002: Every cart line quantity must be greater than zero.
VR-003: Every cart line unit price must be greater than or equal to zero and sourced from selling price pipeline.
VR-004: Line discount must be greater than or equal to zero and less than or equal to line subtotal.
VR-005: Invoice discount must be greater than or equal to zero and less than or equal to subtotal.
VR-006: Payment total must equal net payable total for completion.
VR-007: For cash payment, tendered amount must be greater than or equal to payment amount.
VR-008: Stock availability must be revalidated server-side at commit time.
VR-009: Sale completion must fail when any line would cause negative stock.
VR-010: Customer referenced in sale must exist and be active.
VR-011: If no explicit customer selected, Walk-in Customer ID must be injected before save.
VR-012: Idempotency key must be accepted and duplicate submission must return existing sale response.
VR-013: Void reason must be mandatory for void action.
VR-014: Void action must fail if sale is not in completed status or if void reason is missing.
VR-015: If request contains backdated SalesDate and caller lacks sales.backdate, sale creation must fail.
VR-016: If request SalesDate is in the future, sale creation must fail.
VR-017: If SalesDate is omitted, server must set SalesDate to current system date/time.

## 12. Permission Requirements

PER-001: POS page access requires sales.create.
PER-002: Sales list and receipt view require sales.view.
PER-003: Sale completion action requires sales.create.
PER-004: Sale void action requires sales.void.
PER-005: Customer search from POS requires customers.view.
PER-006: Customer quick create from POS requires customers.create.
PER-007: Customer quick update from POS requires customers.edit.
PER-008: sales.create shall not implicitly grant customers permissions.
PER-009: Reprint action shall require sales.view at minimum and additional sales.reprint permission if introduced.
PER-010: Invoice configuration changes require settings.edit.
PER-011: Backdated POS sale creation requires sales.backdate.

Permission baseline source:
- [src/RetailPOS.API/Authorization/PermissionCatalog.cs](src/RetailPOS.API/Authorization/PermissionCatalog.cs)
- [retailpos-frontend/src/app/app.routes.ts](retailpos-frontend/src/app/app.routes.ts)

## 13. Database Design

DB-001: Reuse existing tables sales, sale_items, sale_payments, inventories, stock_ledgers, customers.
DB-002: Add table pos_terminals.
DB-003: Add table invoice_number_configurations.
DB-004: Add table receipt_print_histories.
DB-005: Add table sale_voids or equivalent immutable void journal.
DB-006: Add terminal_id to sales.
DB-007: Add optional policy reference fields required for void governance.
DB-008: Add customer schema fields for Walk-in system behavior: customer_code, is_system, is_active if missing.
DB-009: Add constraints to protect Walk-in system customer from delete and edit operations.
DB-010: Ensure existing idempotency unique index on outlet_id and idempotency_key remains active.

Assumption A-DB-01:
Current schema already contains sales idempotency index and split payment entities.

Assumption A-DB-02:
Walk-in protection is enforced in service and API layers, with optional DB trigger for hard protection in production.

## 14. Entity Relationships

REL-001: Sale belongs to Outlet.
REL-002: Sale belongs to Terminal.
REL-003: Sale belongs to Cashier User.
REL-004: Sale belongs to Customer.
REL-005: Sale has many SaleItem rows.
REL-006: Sale has many SalePayment rows.
REL-007: SaleItem references ProductVariant.
REL-008: Inventory references ProductVariant and outlet location key.
REL-009: StockLedger references sale document for stock-out and stock-in from void or refund semantics.
REL-010: ReceiptPrintHistory references Sale and Terminal.
REL-011: SaleVoid references original Sale and user who voided.

## 15. API Specifications

API-001: GET /api/pos/lookup
- Purpose: barcode and SKU and variant lookup with effective price and stock hint.
- Reuse existing [src/RetailPOS.API/Controllers/PosController.cs](src/RetailPOS.API/Controllers/PosController.cs).

API-002: GET /api/pos/stock-hint
- Purpose: fast stock hint for UI.
- Rule: display hint only, not final stock gate.

API-003: POST /api/pos/cart-prices
- Purpose: batch pricing for cart lines.

API-004: POST /api/sales
- Purpose: complete sale transaction.
- Input: outlet, terminal, cashier, customer, lines, discounts, tax, payments, idempotency key.
- Behavior: commit sale, deduct stock, write ledger, publish events, write audit.

API-005: GET /api/sales/{id}
- Purpose: sale details.

API-006: GET /api/sales/{id}/receipt
- Purpose: receipt model retrieval.

API-007: POST /api/sales/{id}/void
- Purpose: void completed sale.
- Validation: sales.void permission and mandatory void reason. No same-day or configured window restriction in Phase 1.

API-008: POST /api/sales/{id}/reprint
- Purpose: register receipt reprint and return printable payload.
- Note: New endpoint required in Phase 1.

API-009: GET /api/settings/receipt
- Purpose: receipt configuration read.

API-010: PUT /api/settings/receipt
- Purpose: receipt configuration update.

API-011: GET /api/settings/invoice-number
- Purpose: invoice numbering configuration read.
- Note: New endpoint required in Phase 1.

API-012: PUT /api/settings/invoice-number
- Purpose: invoice numbering configuration update.
- Note: New endpoint required in Phase 1.

API-013: GET /api/customers/search
- Purpose: customer lookup.
- Permission: customers.view only.

API-014: POST /api/customers
- Purpose: quick customer create from POS.
- Permission: customers.create only.

API-015: PUT /api/customers/{id}
- Purpose: quick customer edit from POS.
- Permission: customers.edit only.

## 16. Service Responsibilities

SRV-001: SaleService
- Orchestrates sale transaction, stock validation, deduction, ledger writes, idempotency handling.
- Reuse existing [src/RetailPOS.API/Services/SaleService.cs](src/RetailPOS.API/Services/SaleService.cs).

SRV-002: PosLookupService
- Provides low-latency lookup and stock hints.
- Reuse existing [src/RetailPOS.API/Services/PosLookupService.cs](src/RetailPOS.API/Services/PosLookupService.cs).

SRV-003: InvoiceNumberService
- New service for per-outlet sequence generation with atomic increment.

SRV-004: ReceiptPrintService
- New service for receipt print and reprint registration and payload shaping.

SRV-005: TerminalService
- New service for terminal resolution and validation per outlet.

SRV-006: VoidPolicyService
- No dedicated void-window enforcement service is required in Phase 1.
- If a VoidPolicyService already exists, it must be limited to validating sale state and mandatory reason only.

SRV-007: CustomerService
- Reuse existing customer create and update and search logic, no permission bypass.

SRV-008: AuditService
- Reuse existing immutable audit event write and scope flush behavior.
- Source: [src/RetailPOS.Infrastructure/Audit/AuditService.cs](src/RetailPOS.Infrastructure/Audit/AuditService.cs).

## 17. Domain Events

EVT-001: SaleStarted
EVT-002: ProductAddedToCart
EVT-003: QuantityChanged
EVT-004: CustomerSelected
EVT-005: LineDiscountApplied
EVT-006: InvoiceDiscountApplied
EVT-007: PaymentAdded
EVT-008: PaymentCompleted
EVT-009: SaleCompleted
EVT-010: InventoryDeducted
EVT-011: ReceiptGenerated
EVT-012: ReceiptPrinted
EVT-013: SaleVoided

Event implementation guidance:
- Reuse current publisher pattern from [src/RetailPOS.API/Services/ISaleEventPublisher.cs](src/RetailPOS.API/Services/ISaleEventPublisher.cs).
- Extend from completed-only event to lifecycle event contracts.
- Publish after commit for persistence-dependent events.
- Ensure event failures do not break user-facing completion response in Phase 1.

## 18. Audit Requirements

AR-001: Every sale completion must produce audit event entries with actor and outlet and terminal context.
AR-002: Every void action must produce audit event entries with reason and before-after status values.
AR-003: Every reprint action must produce audit event entries.
AR-004: Failed completion attempts must produce failure audit events where supported by middleware.
AR-005: Audit records are immutable and append-only.
AR-006: Sensitive fields must remain redacted.
AR-007: Correlation ID from request context must be retained.
AR-008: Backdated sale creation must write audit details including user, selected SalesDate, CreatedAt, outlet, terminal, and sale number.

Reference implementation:
- [src/RetailPOS.API/Middleware/AuditContextMiddleware.cs](src/RetailPOS.API/Middleware/AuditContextMiddleware.cs)
- [src/RetailPOS.Infrastructure/Audit/AuditChangeTrackerInterceptor.cs](src/RetailPOS.Infrastructure/Audit/AuditChangeTrackerInterceptor.cs)
- [src/RetailPOS.Infrastructure/Audit/AuditService.cs](src/RetailPOS.Infrastructure/Audit/AuditService.cs)

## 19. Configuration Requirements

CR-001: Invoice format must be configuration-driven by outlet.
CR-002: Auto-print must be configuration-driven and default true.
CR-003: Receipt size must be configuration-driven with default 80mm.
CR-004: Tax behavior must be configuration-driven.
CR-005: Void policy window must be configuration-driven.
CR-006: Terminal behavior defaults must be configuration-driven.
CR-007: Configuration updates must be auditable.

Reuse settings infrastructure:
- [src/RetailPOS.API/Services/SettingsService.cs](src/RetailPOS.API/Services/SettingsService.cs)
- [src/RetailPOS.API/Settings/AppSettings.cs](src/RetailPOS.API/Settings/AppSettings.cs)

## 20. Receipt Requirements

RR-001: Receipt generation is mandatory after successful sale completion.
RR-002: Auto-print starts immediately after completion when enabled.
RR-003: System supports 80mm thermal default rendering.
RR-004: System supports A4 invoice rendering for full-page output.
RR-005: Reprint is supported for completed sales.
RR-006: Every print and reprint action is tracked in receipt print history.
RR-007: Receipt must include sale number, outlet, terminal, cashier, customer, lines, totals, payment summary.
RR-008: Receipt content must match committed sale data snapshots.

## 21. Inventory Integration

IR-001: Stock check must be outlet-scoped using inventory records for location type outlet.
IR-002: Final stock gate occurs only in server-side sale transaction commit.
IR-003: Stock hint API is non-authoritative and UI advisory only.
IR-004: On sale completion, inventory quantity is decremented immediately.
IR-005: Stock ledger out entries are mandatory for completed sale lines.
IR-006: On void, stock restoration and stock ledger in entries are mandatory.
IR-007: Concurrency conflicts must fail safely and request retry.

Reuse:
- [src/RetailPOS.API/Services/SaleService.cs](src/RetailPOS.API/Services/SaleService.cs)
- [src/RetailPOS.Core/Entities/Inventory.cs](src/RetailPOS.Core/Entities/Inventory.cs)

## 22. Performance Requirements

PR-001: POS page initial load target is less than 2 seconds under normal LAN conditions.
PR-002: Product lookup target is less than 100 ms where cache hit is available.
PR-003: Barcode scan to cart update target is less than 100 ms where cache hit is available.
PR-004: Complete sale API target is less than 1 second for typical cart sizes up to 20 lines.
PR-005: Print command start target is less than 2 seconds.
PR-006: API path must avoid unnecessary deep entity loading on hot paths.
PR-007: Cache invalidation must occur for affected variant stock hints after completion and void.

## 23. Error Handling

ER-001: Validation errors return standardized API response with clear message and detail list.
ER-002: Insufficient stock returns business error and no partial commit.
ER-003: Payment mismatch returns validation error and no partial commit.
ER-004: Idempotent duplicate submission returns original successful sale response.
ER-005: Concurrency conflict returns retryable message.
ER-006: Print failure after sale commit does not roll back sale but must be logged and auditable.
ER-007: Unauthorized customer operations return permission error without side effects.
ER-008: Void request returns business error when target sale is not completed or when void reason is missing.

## 24. Acceptance Criteria

AC-001: Cashier can complete sale with full payment and inventory deducts correctly.
AC-002: Repeated barcode scan increments existing cart quantity.
AC-003: Sale cannot complete with insufficient payment.
AC-004: Sale cannot complete when any line has insufficient stock.
AC-005: Sale persists terminal, outlet, cashier, and non-null customer (Walk-in fallback).
AC-006: Walk-in Customer is seeded and protected from edit and delete.
AC-007: Customer actions remain controlled by customers permissions only.
AC-008: Any user with sales.void can void any completed POS sale at any time, and the void operation restores inventory, writes stock ledger IN entries, and records full audit.
AC-009: Receipt auto-print and reprint workflows are functional and tracked.
AC-010: Domain events are emitted for required lifecycle stages.
AC-011: Audit records exist for critical actions and are immutable.
AC-012: Existing POS and sales flows remain backward compatible with current APIs where possible.

## 25. Copilot Implementation Rules

CA-001: Do not introduce duplicate sale checkout logic outside SaleService orchestration path.
CA-002: Do not introduce duplicate pricing logic in frontend; use existing pricing APIs.
CA-003: Do not bypass inventory validation from server transaction layer.
CA-004: Do not bypass customers permission model by linking it to sales.create.
CA-005: Do not store null customer for POS sales. Always assign Walk-in Customer when no explicit customer is selected.
CA-006: Do not allow Walk-in Customer modification or deletion paths.
CA-007: Reuse existing response envelope models and error handling patterns.
CA-008: Reuse existing audit infrastructure and append-only audit semantics.
CA-009: Reuse existing POS lookup caching architecture.
CA-010: Preserve existing UI template style and route guard patterns.
CA-011: Introduce new entities only when required by this specification and avoid overlaps with existing entities.
CA-012: Keep implementation Phase 1 scoped. Do not implement out-of-scope return or exchange or promotion workflows.

## Assumptions Log

ASM-001: Existing sale payment and held sale entities remain valid and reused.
ASM-002: Existing sales idempotency unique index remains active and enforced.
ASM-003: Terminal table and invoice configuration are introduced in this phase as approved architecture decisions.
ASM-004: Event publisher remains in-process in Phase 1 unless separately approved for outbox.
ASM-005: A4 output may initially be implemented using existing document rendering patterns and receipt view adaptations.

## Implementation Notes for Developers

NOTE-001: Begin implementation by aligning schema and seed data with Walk-in and terminal and invoice requirements before frontend behavior changes.
NOTE-002: Keep sale completion transaction atomic. Print and event side effects may occur post-commit but must be tracked.
NOTE-003: Add tests for Walk-in fallback and non-null customer persistence and permission boundaries and scan increment behavior.
NOTE-004: Validate both backend and frontend builds before merge.

