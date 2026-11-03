# RetailPOS Angular UI Standardisation Audit

## Scope and outcome

This is a documentation-only audit of the Angular frontend and the purchased Able Pro references. No application source, backend, mobile, or template source files were changed. The audit is based on the current working tree; there are pre-existing edits in the repository, including Angular files, so this is not a clean-baseline comparison.

The recommended approach is to adopt Able Pro's visual language and reusable Angular patterns incrementally, while keeping RetailPOS's standalone Angular architecture, Tailwind build, existing API services, routes, guards, and menu model. Do not transplant either template application wholesale.

## Verified project and template structure

| Area | Observed structure | Audit implication |
|---|---|---|
| RetailPOS Angular app | [`retailpos-frontend/`](../../retailpos-frontend/) is an Angular standalone application. Its `src/app` contains shared `components`, feature `pages`, `services`, `models`, `guards`, `interceptors`, and `utils`. | Keep existing boundaries and feature code; standardise the presentation layer in place. |
| Able Pro Angular | [`able-pro-angular-v3.3.0/`](../../design-reference/able-pro/able-pro-angular-v3.3.0/) contains **both** `full-version/` and `seed/`. The full version has the rich `src/app/@theme` layout/components and demo pages. | `full-version` is the primary reference. Use the seed to understand the smaller theme foundation; neither is a RetailPOS drop-in. |
| Able Pro Bootstrap | [`able-pro-bootstrap-v9.6.2/`](../../design-reference/able-pro/able-pro-bootstrap-v9.6.2/) has its source directly under `src/` (including `src/html/`, `src/scss/`, and assets) and a `dist/` output. It does **not** have a `full-version/` subdirectory. | Use `src/html` and `src/scss` for secondary visual examples only; this is a Gulp-built static Bootstrap template, not an Angular application. |

## Existing application architecture

- [`package.json`](../../retailpos-frontend/package.json) and [`angular.json`](../../retailpos-frontend/angular.json) show Angular 21 standalone APIs, Tailwind CSS 3.4, RxJS 7.8, and the Angular build/unit-test builders. The application does not currently declare Angular Material, Bootstrap, or a data-grid library.
- The application shell is [`layout.component.html`](../../retailpos-frontend/src/app/components/layout/layout.component.html), with separate navbar, sidebar, and business-context selector components. The layout uses responsive Tailwind classes and hosts the router outlet.
- Navigation data and filtering already have a clear separation: [`menu.model.ts`](../../retailpos-frontend/src/app/models/menu.model.ts) defines the menu, and [`menu.service.ts`](../../retailpos-frontend/src/app/services/menu.service.ts) filters it by role, permission, verified business context, and HR/Payroll capability. Sidebar collapsed/density state and active-route state are also handled there.
- [`app.routes.ts`](../../retailpos-frontend/src/app/app.routes.ts) centralises feature routes and route metadata. `authGuard`, `routePermissionGuard`, role/capability guards and startup restoration are part of the existing navigation contract; see [`auth.guard.ts`](../../retailpos-frontend/src/app/guards/auth.guard.ts) and [`app.config.ts`](../../retailpos-frontend/src/app/app.config.ts).
- [`styles.css`](../../retailpos-frontend/src/styles.css) is the global Tailwind entry point and currently defines shared card, form, button, badge, table, and alert classes. [`tailwind.config.js`](../../retailpos-frontend/tailwind.config.js) extends a neutral palette. Individual feature components also use utility classes and component CSS, so the visual language is not yet fully centralised.
- Feature coverage includes POS, products, users/roles, inventory, purchase orders, goods receiving, sales, accounting, audit, reports, HR/Payroll, and settings. Several existing reusable interaction components are present, including the advanced filter drawer, alerts, searchable dropdown, variant picker, and product-variant cell. The `components/shared` directory is currently small; do not create a competing component framework alongside it.
- HTTP behaviour is wired through the existing session, authentication, and error interceptors in [`app.config.ts`](../../retailpos-frontend/src/app/app.config.ts). Forms and screens use existing Angular forms and services; these must remain the owners of validation, API requests, and business rules during a visual refactor.
- The repository currently has 43 `*.spec.ts` files under `src/app`, including menu, guard, business-context, interceptor, POS, accounting, and HR/Payroll coverage. The Angular test builder is configured; no browser end-to-end or screenshot-regression runner was found in the frontend manifest/configuration.

## Able Pro reference patterns

### Primary: Angular full version

The full version is an Angular 21 application, using Angular Material/CDK and a dedicated `@theme` structure. Reusable references include:

- [`vertical-menu.component.html`](../../design-reference/able-pro/able-pro-angular-v3.3.0/full-version/src/app/@theme/layouts/menu/vertical-menu/vertical-menu.component.html), [`toolbar.component.html`](../../design-reference/able-pro/able-pro-angular-v3.3.0/full-version/src/app/@theme/layouts/toolbar/toolbar.component.html), and the demo admin layout for shell proportions and navigation hierarchy.
- [`card.component.html`](../../design-reference/able-pro/able-pro-angular-v3.3.0/full-version/src/app/@theme/components/card/card.component.html), [`modal-dialog.component.html`](../../design-reference/able-pro/able-pro-angular-v3.3.0/full-version/src/app/@theme/components/modal-dialog/modal-dialog.component.html), and the demo form/table pages for composable surfaces and interaction states.
- [`styles.scss`](../../design-reference/able-pro/able-pro-angular-v3.3.0/full-version/src/styles.scss) and the theme palette SCSS for Inter typography, semantic color palettes, light/dark themes, and Angular Material theme generation.

Treat these as examples of structure, spacing, palette, and state treatment. Their Material components, providers, route setup, demo data, assets, and theme configuration are not part of RetailPOS's current UI stack.

### Secondary: Bootstrap template

The Bootstrap template provides useful static visual examples in `src/html/layouts/`, `src/html/elements/`, `src/html/forms/`, and `src/html/table/`. Its package uses Bootstrap 5.3.7 with Gulp and numerous optional plugins. Prefer the Angular reference when patterns differ. Do not load Bootstrap JavaScript, jQuery-style plugins, or raw Bootstrap page scripts into Angular.

## Angular/dependency compatibility

| Concern | RetailPOS frontend | Able Pro Angular full version | Assessment |
|---|---|---|---|
| Angular | `^21.1.0` framework packages; CLI/build `^21.1.2` | Angular framework, CLI/build, Material and CDK `21.0.1` | Same major generation; template is on an earlier minor/patch line. No inherent major-version blocker is evident, but check peer ranges and build against the exact app lockfile before adding any template package. |
| TypeScript | `~5.9.2` | `5.9.3` | Same minor line; verify the resolved compiler and Angular peer constraints rather than copying template pins. |
| RxJS | `~7.8.0` | `~7.8.2` | Same major/minor line. Avoid changing the app's resolved version just to match the template. |
| UI framework | Tailwind 3.4 and project-owned CSS classes | Angular Material/CDK, SCSS theme generation | The principal integration risk is a second component system and CSS/theme collision, not Angular major incompatibility. |
| Other libraries | Angular core/forms/router and `jsbarcode` | Material/CDK plus charts, calendars, editors, grids, uploaders, and other demos | Do not import the full manifest. Evaluate only a specifically justified capability, its Angular peer range, bundle cost, license, and maintenance. |
| Bootstrap reference | Not declared in RetailPOS | Static Bootstrap 5.3.7/Gulp project | Visual reference only. Bootstrap JS is explicitly out of scope. |

**Dependency recommendation:** do not add dependencies for the initial shell or token work. Preserve Tailwind and the project's current classes as the rendering mechanism. If a later decision requires Angular Material, adopt it deliberately as the single component-system choice for the specified surface, confirm version/peer/build compatibility, and avoid duplicating existing primitives with both Material and custom wrappers.

## Component mapping

| Existing Component / area | Able Pro Reference | Recommended Action | Migration Risk |
|---|---|---|---|
| `LayoutComponent`, `NavbarComponent`, `SidebarComponent`, business-context selector | Angular `demo/layout/admin`, `@theme/layouts/menu/vertical-menu`, and `@theme/layouts/toolbar` | Retain the existing components and state/services; restyle shell dimensions, typography, active states, responsive drawer, and toolbar as a single change. Keep the existing menu model and context selector wired in. | **High** — route outlet, mobile navigation, verified business scope, acting role/outlet context, and permission-filtered menus converge here. |
| `MenuService` and `NAV_SECTIONS` | Angular vertical-menu hierarchy and menu item/group patterns | Map existing menu sections/items into the reference's visual hierarchy; do not replace the permission/capability filtering or use template demo menus. | **High** — visibility must remain consistent with route guards and feature entitlements. |
| `DashboardComponent` | Angular dashboard cards/widgets; Bootstrap `dashboard/index.html` | Restyle existing dashboard cards, headings, and responsive grid. Keep data and actions owned by RetailPOS; do not copy template demo metrics. | **Low–Medium** — mostly presentational, with links and role-dependent actions to preserve. |
| Login and registration components | Angular authentication pages; Bootstrap `pages/login-*` and `register-*` | Borrow page composition, input, validation, and error-state treatments. Retain current auth flow, route guards, and API contract. | **Medium** — authentication/redirect behaviour and validation feedback are sensitive. |
| Product list and product form | Angular table/form demos; Bootstrap `table/tbl_*` and `forms/form2_*` | Use as the pilot after shell/primitives. Keep filtering, sorting, pagination, variant selection, upload, validation, and service calls unchanged while adopting shared surfaces and controls. | **Medium–High** — many interactive states and catalog API behaviour. |
| Users, roles, outlets, warehouses, and categories | Angular forms/tables/cards; Bootstrap form and table examples | Consolidate repeated visual patterns into shared project primitives; preserve permission-based actions, role-permission editing, and existing route contracts. | **High** for users/roles; **Medium** for the other CRUD screens — authorization is directly surfaced in these screens. |
| POS screen | Angular toolbar/card/form patterns; Bootstrap dashboard, form, and table examples | Defer until the end. Apply tokens and carefully isolated low-level styling first; do not restructure cashier workflow as part of a theme pass. | **Very High** — sales, pricing, discount audit, customer/product lookup, cash session, held sales, receipt generation/printing, and API state are tightly coupled. |
| Inventory, purchase order, GRN, sales, stock transfer/count/adjustment screens | Angular table/form/detail patterns; Bootstrap `table/` and `forms/` | Migrate one coherent screen family at a time. Keep business action sequencing, error/loading/empty states, and permission-gated controls intact. | **High** — multi-step domain workflows and stock/business effects. |
| Reports, accounting, HR and Payroll | Angular dashboard/chart/table/form references; Bootstrap chart/table/forms | Standardise shared page chrome and table/filter presentation first; migrate specialist reporting and workflow components only with domain-owner regression cases. | **High** — large screen surface, dense data, specialised permissions and calculations. |
| `AdvancedFilterDrawerComponent`, `SearchableDropdownComponent`, `VariantSearchPickerComponent`, alert and change-password modal | Angular modal/card/form patterns; Bootstrap `elements/ac_modal.html` and `forms/` | Reuse and restyle existing interaction components; standardise focus, labels, keyboard handling, loading, empty, and error states. Do not create another generic copy. | **Medium** — interaction/accessibility regressions can affect many callers. |

Risk ratings are relative migration/regression risk, not security severity.

## Proposed central design system

Use Able Pro as the visual reference, with RetailPOS semantics and its existing Tailwind stack:

| System area | Recommendation |
|---|---|
| Tokens and themes | Define CSS custom properties in the existing [`src/styles.css`](../../retailpos-frontend/src/styles.css), grouped by semantic role (brand/primary, success, warning, danger, info, surface, text, border, focus, overlay). Map Tailwind utilities to those variables where useful; avoid literal color values scattered through templates. |
| Typography | Adopt the template's Inter-based hierarchy after checking local font asset delivery and performance. Define body, page title, section title, label, helper, and dense-data sizes/weights centrally. Keep barcode/receipt print typography intentionally separate. |
| Spacing and surfaces | Establish a consistent 4px-based spacing scale, content widths, page gutters, card radius, border, and elevation. Use compact density for data-heavy POS/ERP tables without making touch controls too small. |
| Buttons and status | Standardise primary, secondary, outline, destructive, quiet, disabled, loading, and focus-visible states. Define status badges/alerts semantically and do not rely on color alone. |
| Forms | Share label, required, hint, error, disabled, read-only, select, date, and validation layout patterns. Keep Angular form bindings, validators, server errors, and submission state in each feature. |
| Tables | Standardise header/body typography, row density, alignment, sorting affordances, empty/loading/error states, responsive overflow, and keyboard-accessible row actions. Do not adopt a grid dependency without a concrete need. |
| Modals/drawers | Standardise width, overlay, heading/actions, focus entry/return, Escape handling, scroll containment, and mobile full-screen behaviour using current Angular components. |
| Navigation | Adopt Able Pro's hierarchy, active marker, spacing, and responsive visual language while retaining the current permission-filtered RetailPOS data and state model. |

**Placement:** keep global tokens and Tailwind base/component-layer rules in `retailpos-frontend/src/styles.css` and the Tailwind mapping in `tailwind.config.js`. Keep feature-neutral Angular components alongside existing components, incrementally under `src/app/components/shared/` (or a clearly named `ui/` child only when a real reusable component is introduced). Feature-specific wrappers and behaviour remain in their existing feature directories. This makes shared styling discoverable without moving business components or introducing a parallel package.

## Risks and mitigations

1. **Authorization and menu parity:** styling must not convert hidden menu items into visible unguarded links or remove action-level checks. Preserve `routePermissionGuard`, capability guards, `NAV_SECTIONS` permission metadata, and verified business-context requirements. Verify both allowed and denied states after every shell/menu change.
2. **Routing and startup:** preserve route paths, redirects, return URLs, initial profile refresh, business-context restoration, and router outlet structure. Avoid copying Able Pro demo routes or replacing the current route table.
3. **Reactive forms and API integration:** treat template changes as presentation-only. Preserve form control names, validators, dirty/touched handling, disabled conditions, API payloads, subscriptions, interceptor behaviour, and loading/error/success states.
4. **Business workflows:** POS and accounting/stock workflows need domain regression coverage before structural changes. Preserve pricing, discounts, cash sessions, transaction idempotency, audit behavior, receipts, report filters, and calculations.
5. **Accessibility:** validate keyboard navigation, visible focus, labels and error associations, modal focus management, accessible names for icon-only controls, table semantics, color contrast, zoom, and reduced motion. Template appearance alone is not evidence of accessibility.
6. **Responsiveness:** test narrow cashier/tablet and desktop layouts; dense tables, context selectors, menus, overlays, and action bars need explicit overflow and touch-target checks.
7. **CSS and bundle integration:** Tailwind utilities, component CSS, and any future Material styles can conflict or inflate bundles. Add no template-wide stylesheet or dependency bundle; check production bundle budgets and inspect computed styles per pilot.
8. **Purchased source integrity:** read and reference the design-reference sources, but do not edit, copy-over, or reformat their files.

## Validation recommendations

- Before migration, record `npm run build` and `npm test` results and capture a small set of stable current screenshots. Existing unit specs are an asset; add focused tests where a migrated reusable component currently lacks coverage.
- At each phase, compare route/menu permissions and auth/session/context behaviour, then run the frontend build and targeted tests. Retain the existing Angular test runner unless it proves insufficient.
- Add browser end-to-end coverage for login/redirect, role- and permission-filtered navigation, business-context restoration, representative list/form CRUD, and critical transaction paths (especially POS), using controlled API fixtures.
- Introduce visual-regression tooling only after dependency compatibility is assessed. Capture deterministic screenshots for the shell, dashboard, one list/form pilot, POS, and a modal at desktop, tablet, and phone viewports. Stabilise data, fonts, animations, and browser versions; review diffs rather than accepting broad thresholds.
- Run an accessibility pass (automated checks plus keyboard/manual verification) on the shell and shared primitives before migrating feature screens; repeat for complex pages.
- Compare production bundle size and build budgets before/after any dependency or global-style changes.

The ordered implementation sequence and phase gates are in the [migration roadmap](./MIGRATION_ROADMAP.md).
