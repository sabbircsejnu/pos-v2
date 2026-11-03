# RetailPOS UI Standardisation Migration Roadmap

This roadmap applies the [frontend audit](./FRONTEND_AUDIT.md). It is a proposal only; no application code or dependencies were changed as part of the audit.

## Guiding constraints

- Keep the current Angular app, feature routes, services, guards, permission model, interceptors, reactive forms, and API contracts.
- Treat Able Pro's Angular full version as the primary visual reference and its Bootstrap version as secondary static reference. Do not replace RetailPOS with either template.
- Retain the current Tailwind-based implementation initially. Do not add Angular Material, Bootstrap, or visual-test dependencies until a concrete need and compatibility review are approved.
- Migrate in small, reviewable steps. Each phase must preserve business behavior and pass its acceptance gate before the next begins.
- Leave both purchased template trees unchanged.

## Phases

| Phase | Scope and sequence | Acceptance gate |
|---|---|---|
| **0. Baseline and design decisions** | Record the current production build and unit-test baseline; inventory the screens and role/context states in scope; capture baseline screenshots at desktop, tablet, and phone widths. Approve semantic token names, typography, density, and accessibility targets based on the Able Pro Angular theme. | Baseline results and screenshots are retained; visual direction is agreed; permission-sensitive and transaction-critical paths have named regression cases. No dependency change. |
| **1. Application shell** | Standardise only presentation in the existing layout, navbar, sidebar, page content area, and Super Admin business-context selector. Use Able Pro vertical-menu/toolbar patterns with light theme, `#4680ff` primary, Inter, 280px/60px sidebar and approximately 74px header targets; validate responsive fit. Keep `MenuService`, menu model, route/permission guards, navigation state, and selector data/API logic unchanged. Preserve the existing POS checkout layout. | Direct navigation, active route, expanded/collapsed state, mobile open/close, permission/capability menu filtering, role/outlet switcher, verified business-context restoration, logout, and guarded redirects behave as before. Shell is usable at the agreed desktop/tablet/mobile viewports and with keyboard navigation. No Material/Bootstrap dependency. |
| **2. Tokens and shared primitives** | Centralise semantic CSS variables and Tailwind mapping. Refactor existing global card/form/button/badge/table/alert rules into the agreed token system. Reuse and restyle existing alert, advanced-filter drawer, searchable dropdown, variant picker, and modal patterns before adding new shared controls. **Phase 2A status:** tokens, Tailwind `rp-*` mapping and the shared button, card, form-control/field, badge, page-header and state components are implemented under `components/shared/ui/` (see [`PHASE_2A_REPORT.md`](./PHASE_2A_REPORT.md)). Legacy global `.btn/.card/.table/.input/.badge` rules were deliberately left untouched and are retired screen by screen. Tables, alerts and modals remain. **Phase 2B status:** the Data Table, dialog/confirm dialog, inline alert and focus trap are implemented, and the existing alert, searchable dropdown, filter drawer and variant picker were restyled on the shared tokens with their contracts unchanged (see [`PHASE_2B_REPORT.md`](./PHASE_2B_REPORT.md)). No feature screen has been migrated yet. **Phase 3A status:** the Product List is migrated to the kit with server-side search/sort/pagination ([`PHASE_3A_REPORT.md`](./PHASE_3A_REPORT.md)). **Phase 3B status:** Product Create/Edit (with its image uploader and variant manager) is migrated ([`PHASE_3B_REPORT.md`](./PHASE_3B_REPORT.md)). | Shared controls have defined normal, hover, focus, disabled, loading, validation/error, and responsive states as applicable. Component tests cover reusable behavior; no duplicated Material/custom system is introduced. Production build and bundle budgets remain acceptable. |
| **3. Pilot screen** | Migrate the product list as the first data-heavy pilot after the shell and primitives. Use Able Pro table/form/card patterns while retaining search, filters, sort, paging, empty/loading/error states, variant behavior, permission-gated actions, and product API calls. Keep the existing feature component and service boundary. | Product list and a representative product-form flow pass targeted unit/integration and browser checks; API requests and payloads are unchanged; responsive table behavior, keyboard access, and screenshot diffs are reviewed and accepted. |
| **4. Standard CRUD families** | Migrate categories, customers, suppliers, outlets, warehouses, and user/role administration in coherent groups. Reuse the pilot's primitives rather than duplicating page-level CSS. Prioritise user/role screens with permission-aware action visibility and validation. | Create/read/update flows and denied/allowed actions match current behavior. Route/menu consistency and role-specific test cases pass; no feature service or authorization policy changes are hidden in style commits. |
| **5. Operational feature modules** | Migrate purchase orders, GRN, inventory, sales details/lists, stock transfers, adjustments, counts, barcodes, and settings in workflow-based batches. Use common page headers, filter bars, status display, tables, forms, and confirmation dialogs. | Each workflow's state transitions, API interactions, audit messages, print/export output, errors, and permissions are verified. Where screens have irreversible or stock-affecting actions, test those outcomes with safe fixtures. |
| **6. Reports and specialist modules** | Migrate reporting, accounting, audit, HR, and Payroll surfaces. Standardise shell, filters, cards, and tables first; preserve specialist inputs, calculation display, period/context selection, approvals, and report exports. | Domain-owner cases cover calculations, period boundaries, filter behavior, permissions, and exports. Dense and wide data remains legible at supported viewport sizes. |
| **7. POS and final consistency pass** | Migrate the POS screen last. Start with tokens and low-risk visual changes; only restructure its composition as a separately reviewed task. Complete remaining feature pages and reconcile shared styles, responsive behavior, keyboard interaction, and accessibility. | Sales creation, tax/pricing, discounts and audit, customer/product lookup, cash session lifecycle, held-sale workflows, receipt document/PDF/print paths, and error recovery pass regression checks. No unresolved high-severity visual, accessibility, or behavior regressions. |
| **8. Release verification** | Run the full frontend build and unit tests, browser regression suite, screenshot comparisons, accessibility checks, and production-size review. Verify deployed static assets and deep-link routing in the target hosting configuration. | All agreed checks pass; visual differences are reviewed rather than blindly accepted; route/permission matrix and rollback point are documented before release. |

## Test plan by layer

### Existing unit and component coverage

- Keep the configured Angular unit-test runner and expand focused specs for the shell, menu rendering, shared buttons/fields/tables/dialogs, and their accessibility states.
- Retain and extend the existing auth guard, menu service, business-context, capability, and route/menu consistency tests. Explicitly test permissions that require `all` versus `any`, capability-hidden entries, and Super Admin behavior without a verified business context.
- Add component-level assertions that migrated forms preserve validation, dirty/touched state, disabled state, server errors, and submit/loading state.
- For the product pilot and other data views, cover filtering/sorting/pagination contracts and loading, empty, error, and populated results.

### Browser-level behavior

Use a controlled test identity/API fixture to verify:

1. Unauthenticated access redirects to login and returns to the intended route after authentication.
2. Authenticated users see only authorized menu groups/actions; direct access to a forbidden route remains blocked.
3. Business-context restoration, context switching, and capability-dependent menu visibility stay consistent.
4. Representative CRUD workflows preserve input validation, request shape, success/error feedback, and navigation.
5. POS scenarios cover the current cashier workflow, with test data isolated from persistent resources.

No end-to-end runner is currently configured in the frontend package. Evaluate a browser runner only when Phase 0 selects the test strategy; do not add a dependency solely for this documentation task.

Phase 1A confirms that the configured API is unavailable at `localhost:5005` in this environment, so authenticated baseline browser cases could not be exercised. The app shell phase must use an approved development API and safe test identity before running those cases. No seeded or production credentials should be inferred from source or workspace selection.

### Visual regression and accessibility

- Capture fixed, deterministic viewport screenshots for the shell, dashboard, product-list pilot, representative form, modal/drawer, and POS. Include phone, tablet, and desktop widths.
- Stabilise test data, font loading, animation, locale/currency, browser version, and device scale before comparing snapshots. Require human review of visual diffs; do not suppress large diffs with permissive thresholds.
- Check keyboard-only operation and visible focus, screen-reader labels/error associations, dialog focus entry/return and Escape behavior, table semantics, color contrast, text zoom, and touch-target sizing.
- Use automated accessibility checks as a gate, not as a substitute for manual keyboard and assistive-technology review.

## Rollback and change management

- Keep each phase in small changesets limited to Angular presentation files and directly related tests/docs. Do not mix visual refactoring with API, permission, or business-rule changes.
- Save a screenshot/build/test baseline at the start of each phase. If a phase fails its gate, revert only that phase's UI changes and preserve unrelated worktree edits.
- Re-run permission, context, and critical workflow tests after changes to shared styles or components, since a shared primitive can affect many feature screens.
- Update this roadmap and the audit findings when a deliberate architecture decision changes the UI stack or introduces a dependency.
