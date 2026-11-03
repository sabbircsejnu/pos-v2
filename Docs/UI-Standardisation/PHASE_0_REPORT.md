# RetailPOS UI Standardisation — Phase 0 Report

## Status: PARTIALLY PASSED

Phase 0 baseline validation and design documentation are complete. Application behavior remains unchanged because this phase made no source, CSS, Tailwind configuration, dependency, backend, Flutter, or purchased-template changes. Phase 1 is ready to begin its shell design/implementation work after baseline browser screenshots and the remaining shell decisions are completed.

## Scope and working-tree safety

- Audited the existing working tree before and after validation. At baseline, `git status --short` reported **907 entries**: 178 modified, 284 deleted, and 445 untracked. This is a heavily dirty checkout, not a clean baseline.
- **168** reported entries were under `retailpos-frontend/`; existing work in the Angular app was not modified or reverted.
- `design-reference/` is present as an untracked tree in this checkout. It was inspected only; no purchased template files were changed.
- The existing `Docs/UI-Standardisation/` audit/roadmap are also already in the working tree. This phase adds this report and [`DESIGN_SYSTEM.md`](./DESIGN_SYSTEM.md) without rewriting unrelated files.
- No backend or Flutter files were changed.
- Phase 0 did not run browser/screenshots. Phase 1A captured the login page only at the agreed three viewport sizes; no authenticated shell/page baseline or visual-regression comparison was possible. See [`PHASE_1A_REPORT.md`](./PHASE_1A_REPORT.md).

## Baseline validation

Commands were run from `retailpos-frontend/`, where the Angular `package.json` lives.

| Check | Result | Evidence |
|---|---|---|
| Production build | **PASS** | `npm run build` completed successfully on 22 Oct 2026. Angular reported `Application bundle generation complete.` No build warnings or errors were reported. |
| Initial bundle | **PASS** | Raw initial total **2.50 MB** (main JS **2.42 MB**, styles **76.66 kB**); estimated transfer size **389.05 kB** (main **379.35 kB**, styles **9.70 kB**). |
| Configured initial bundle budget | **PASS** | `angular.json` sets production initial warning at **4 MB** and error at **6 MB**; measured raw total is below both and the build emitted no budget warning. Per-component style budgets are 10 kB warning / 20 kB error and are separate from the aggregate styles chunk. |
| Unit tests | **PASS** | `npm test -- --watch=false` completed with **43 test files passed, 229 tests passed, 0 reported failures** (25.15s test-run duration). |
| Test stderr | **Informational** | Some tests intentionally exercise HTTP 400/403/503/network-error behavior and log corresponding messages. Their suites passed; these messages are not baseline test failures. |
| Browser/visual validation | **NOT RUN** | No screenshots or browser checks were performed. Visual regression coverage is not claimed. |

The build and test commands were initially attempted at the repository root, which has no frontend `package.json`; npm returned `ENOENT`. Both commands were then run from the correct Angular application directory and passed. This was a command-location correction, not an application failure.

### Phase 1A revalidation

On 22 Oct 2026, Phase 1A reran the Angular production build and unit suite from `retailpos-frontend/`: build passed with the same **2.50 MB** raw initial total and no warnings; **43 test files / 229 tests passed**. The app could be served on loopback at `http://localhost:4200/`. This does not replace the earlier baseline; the latest evidence and authenticated-browser blockers are recorded in [`PHASE_1A_REPORT.md`](./PHASE_1A_REPORT.md).

## Existing UI architecture and reusable assets

- The app uses Angular 21 standalone components, Tailwind CSS 3.4, component CSS, and a single global Tailwind stylesheet. It does not declare Angular Material, Bootstrap, or a grid framework.
- [`styles.css`](../../retailpos-frontend/src/styles.css) already provides base typography/background rules and shared `.card`, `.card-elevated`, `.form-container`, `.input`, `.select`, `.textarea`, `.btn-*`, `.badge-*`, `.label`, `.table`, `.alert-*`, and scrollbar utility classes. It also contains receipt and cashier-specific global classes.
- [`tailwind.config.js`](../../retailpos-frontend/tailwind.config.js) has a neutral `primary` palette plus soft/elevated shadows and animation utilities. Its standard breakpoints differ from Able Pro's custom grid.
- Existing reusable Angular components include the application layout/navbar/sidebar/business-context selector; alert, advanced-filter drawer, searchable dropdown, variant picker, product-variant cell, product image uploader/lightbox, and modal-oriented workflows.
- The full reusable component and route/permission architecture is documented in the existing [frontend audit](./FRONTEND_AUDIT.md).

## Able Pro extraction

The primary reference is the Angular template `design-reference/able-pro/able-pro-angular-v3.3.0/full-version/`. Its source shows:

- Default blue primary (`#4680ff` at 500), grey accent/surfaces, red warning, and dedicated green/yellow semantic ramps; light and dark neutral ramps are defined separately.
- Locally supplied variable Inter typography, body text around 14px, 12px captions, and mostly 500–600 weights for headings/navigation/actions.
- An 8px general radius; a 280px sidebar (60px collapsed); a 74px header; 20px card padding; toolbar rows at 54px, reduced to 46px on narrow screens; selective rather than universal elevation.
- Grid breakpoints at 576, 768, 992, 1200, and 1400px. Component SCSS also uses 567, 576, 768, 1024px thresholds.
- No single declared global data-table row-height/density token; the full version has both Material table and separate grid demos.

The detailed, source-backed values and token proposal are in [`DESIGN_SYSTEM.md`](./DESIGN_SYSTEM.md). The Bootstrap package remains a secondary static reference and contributes no Angular runtime dependency.

## Token and component decision

**Decision: document, do not apply CSS tokens during Phase 0.**

The global stylesheet changes the body/headings and defines common class selectors that are already used by application screens. The Tailwind neutral `primary` scale also differs from Able Pro's blue. Replacing these values or adding unscoped properties consumed by existing classes would immediately alter current pages and could not be verified visually without the required browser baseline. Therefore, this phase establishes semantic names, actual Able Pro values, light/dark scopes, and Tailwind mapping strategy in documentation only.

The proposed component contracts cover buttons, form fields, cards, tables, modals, dropdowns, alerts, status badges, page headers, and filter panels. They explicitly preserve the current Angular services, forms, menu model, and interaction components rather than creating a duplicate UI system.

## Regression protection

### Existing automated coverage observed

- Routing and auth guard restoration/denial: [`auth.guard.spec.ts`](../../retailpos-frontend/src/app/guards/auth.guard.spec.ts).
- Permission-filtered menus, role/permission combinations, and HR/Payroll capability gates: [`menu.service.spec.ts`](../../retailpos-frontend/src/app/services/menu.service.spec.ts), [`menu.service.owner-wildcard.spec.ts`](../../retailpos-frontend/src/app/services/menu.service.owner-wildcard.spec.ts), and [`menu-route-consistency.spec.ts`](../../retailpos-frontend/src/app/models/menu-route-consistency.spec.ts).
- Super Admin context restoration, selection validation, invalid/stale contexts, route activation, and no unverified scope: [`business-context.service.spec.ts`](../../retailpos-frontend/src/app/services/business-context.service.spec.ts) and [`business-context-startup.spec.ts`](../../retailpos-frontend/src/app/business-context-startup.spec.ts).
- Unauthorized 403 handling without logout/navigation and session refresh behavior: [`session.interceptor.spec.ts`](../../retailpos-frontend/src/app/interceptors/session.interceptor.spec.ts); logout and permission evaluation: [`auth.service.spec.ts`](../../retailpos-frontend/src/app/services/auth.service.spec.ts).
- POS has a component spec ([`pos-screen.component.spec.ts`](../../retailpos-frontend/src/app/pages/pos/pos-screen.component.spec.ts)); other existing specs cover accounting, HR, Payroll, barcodes, and domain pages.
- The complete current run passed. This proves the existing suites pass in this worktree; it does not prove every route, permission, form, browser, or visual state is covered.

### Regression cases to retain/extend before shell and shared-style changes

1. **Routing and authentication:** public/login guards, return URL, valid session, expired-session single-flight refresh, and unrecoverable refresh redirect. Verify direct deep links and reload.
2. **Menu and route permissions:** visible menu entries match the route permissions; missing permission/capability stays hidden; unauthorized direct route stays blocked; all/any permission modes and wildcard owner behavior remain correct.
3. **Super Admin without business context:** business-scoped menu actions remain hidden, guarded routes do not initialize unscoped screens, and a saved context is used only after server verification.
4. **Business/outlet switching:** context selection is server-confirmed before the active context changes; header propagation and acting outlet/role remain correct; stale/denied selection does not silently become active.
5. **Unauthorized response behavior:** API 403 keeps the session, current route, and form/page state; does not force logout; presents the permission error once.
6. **API and form behavior:** preserve field names, validators, payloads, request sequencing, interceptor behavior, loading/empty/error/success states, duplicate-submit protection, and business-context headers. Use feature-specific API/form tests as each screen is migrated.
7. **Shared UI interaction/accessibility:** add focused component tests for focus visibility, keyboard operation, modal focus entry/return and Escape policy, labels/error association, disabled/loading actions, and responsive filter/drawer state. No dedicated shared design-system component suite was identified.
8. **Browser and visual regression:** before Phase 1 changes, capture approved shell/dashboard/product pilot/POS/modal screenshots with deterministic data at desktop/tablet/phone sizes. Add browser checks for the permission and context flows. These checks were not run in Phase 0 and must not be treated as existing coverage.

### Pre-existing failures

**None observed in the Phase 0 test run.** All 43 suites and 229 tests passed. Expected simulated HTTP error logs are described above. No other application failure is inferred from the root-directory npm invocation error.

## Phase 1 entry gate

**Ready to begin, with two explicit pre-implementation tasks:**

1. Capture baseline screenshots at agreed desktop, tablet, and phone widths and retain them for review.
2. Confirm shell-specific design decisions (theme scope, responsive breakpoints, menu behavior and density) without changing the permission/context contracts.

Once these are complete, Phase 1 should restyle only the existing application shell components and navigation presentation. Keep routes, auth, permissions, capabilities, context restoration, and API services unchanged. The phase's acceptance gate and subsequent migration sequence remain in the [migration roadmap](./MIGRATION_ROADMAP.md).
