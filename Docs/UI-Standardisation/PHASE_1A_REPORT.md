# RetailPOS UI Standardisation — Phase 1A Evidence and Shell Plan

## Status: PARTIALLY PASSED

**Planning, source review, approved design decisions, and app build/unit validation passed.** The frontend could be served locally, but the configured API was unavailable and no safe test account was supplied. Consequently only the public login page was captured; the authenticated shell and requested feature-page screenshot baseline remains blocked. No application implementation code was changed.

## 1. Worktree safety and pre-existing Angular changes

At the Phase 1A worktree inspection, `git status --short -- retailpos-frontend` reported **168 Angular paths: 57 modified and 111 untracked**. The UI-standardisation docs and Able Pro template are also untracked in this checkout. These are pre-existing workspace conditions, not changes made by Phase 1A.

High-impact pre-existing paths relevant to the shell and its behavior:

| Path | Existing status | Phase 1B handling |
|---|---|---|
| `retailpos-frontend/src/app/components/layout/layout.component.html` | Modified | Preserve current edits; review the existing diff before adding only the approved shell presentation changes. |
| `retailpos-frontend/src/app/components/layout/layout.component.ts` | Modified | Do not change route-event subscription/active-route synchronization for presentation. |
| `retailpos-frontend/src/app/components/layout/business-context-selector.component.ts` | Untracked | Existing file contains both an inline template and context-loading/selection behavior. Preserve the file; any UI update is limited to template presentation/semantics. |
| `retailpos-frontend/src/app/services/menu.service.ts` | Modified | Do not change authentication/context/capability filtering, route state, mobile close, or density/collapse behavior. |
| `retailpos-frontend/src/app/models/menu.model.ts` | Modified | Do not change navigation data, routes, roles, permissions, or capabilities. |
| `retailpos-frontend/src/app/guards/auth.guard.ts` | Modified | Do not change auth, permission, or verified-context guard behavior. |
| `retailpos-frontend/src/app/guards/hr-payroll-capability.guard.ts` | Untracked | Existing capability guard; do not change it. |
| `retailpos-frontend/src/app/app.routes.ts` | Modified | Do not change route definitions or route guards. |
| `retailpos-frontend/src/styles.css` | Modified | Preserve existing edits; future token additions must be scoped to the shell and must not rewrite global/base/shared primitive styles. |
| `retailpos-frontend/tailwind.config.js` | Clean at inspection | No change is currently required; retain existing palette and breakpoints. |

The navbar and sidebar TS, HTML, and component CSS were clean at inspection. Other modified/untracked Angular files exist within `src/app`; the 168-path checkout is too dirty to treat as a clean branch baseline.

### Isolation and review protocol for Phase 1B

1. Before editing, recheck `git status --short` and inspect the current diffs of every allowlisted file. Do not assume the existing status shown above is still current.
2. Work only within the allowlist in section 3. Do not reset, clean, stash, revert, or replace existing untracked files. Do not stage the whole Angular app or docs folder.
3. For dirty tracked files (`layout.component.html`, `styles.css`), integrate small targeted edits with the existing content. Review the before/after diff to verify every pre-existing hunk remains. Use explicit-path/hunk staging only if a commit is later requested.
4. Keep TS behavior changes out of a visual change. In the context selector, alter only inline template classes/ARIA markup; preserve class methods, subscriptions, request context, API calls, signals, and error paths.
5. Keep the POS screen and all feature modules untouched. Review `git diff --check`, exact staged/unstaged file lists, screenshots, and the permission/context regression results before acceptance.
6. Do not copy or modify purchased template files, and add no component library or screenshot dependency in Phase 1B.

## 2. Browser baseline inventory

The Angular dev server was run on loopback at `http://localhost:4200/`. The frontend configured API URL is `http://localhost:5005/api`; a local port check reported it unavailable, and the browser recorded `ERR_CONNECTION_REFUSED` while the app redirected to `/login?returnUrl=%2Fdashboard`.

No safe test account was provided. No seeded/production credential was inferred or used. No sign-in, write API call, production transaction, or persistent business-data change was attempted.

| Requested page | 1440 × 900 | 768 × 1024 | 390 × 844 | Result |
|---|---|---|---|---|
| Application shell | Blocked | Blocked | Blocked | Requires authenticated session; shell was not rendered. |
| Dashboard | Blocked | Blocked | Blocked | Requires authenticated session and app/API access. |
| Product list | Blocked | Blocked | Blocked | Requires authenticated session/API data. |
| Representative form | Blocked | Blocked | Blocked | Requires authenticated session/API data. |
| Modal/filter drawer | Blocked | Blocked | Blocked | Requires an authenticated feature screen and a safe interaction state. |
| POS checkout | Blocked | Blocked | Blocked | Requires authenticated session and safe backend data; deliberately did not enter checkout. |
| Login page (supplemental only) | Captured in browser session | Captured in browser session | Captured in browser session | Public page rendered at all target viewport sizes; this is **not** a shell or feature-page baseline. |

The login screenshots were returned inline by the browser tool during this session; no image files were written to the repository. Screenshots for the six requested authenticated surfaces have **not** been captured. No visual-regression comparison was run. Do not use the supplemental login images as evidence for authenticated layouts.

## 3. Shell assessment and Phase 1B file allowlist

The application shell is Angular standalone UI rendered by [`LayoutComponent`](../../retailpos-frontend/src/app/components/layout/layout.component.ts). It currently composes a 56px fixed topbar, responsive sidebar, Super Admin business-context selector, and routed page content. Tailwind utility classes provide much of its geometry; sidebar and navbar CSS files supply a few local overrides.

| Phase 1B file | Current state | Presentation-only purpose |
|---|---|---|
| `retailpos-frontend/src/styles.css` | Modified before Phase 1A | Add only the small set of shell-scoped light theme variables required by the approved design. Do not change `body`, headings, existing `.card`/`.btn`/form/table/alert/badge classes, receipt styles, or other global rules. |
| `retailpos-frontend/src/app/components/layout/layout.component.html` | Modified before Phase 1A | Add/adjust the theme scope on the existing shell root and main content spacing. Keep selector and router-outlet placement, layout bindings, and collapse-dependent content offset. |
| `retailpos-frontend/src/app/components/layout/layout.component.css` | Clean at inspection | Add local shell-only structural styles if Tailwind utilities cannot express the final responsive geometry cleanly. |
| `retailpos-frontend/src/app/components/layout/navbar.component.html` | Clean at inspection | Align header height, toolbar groups, controls, and mobile arrangement to approved Able Pro direction; preserve all existing bound actions and content. |
| `retailpos-frontend/src/app/components/layout/navbar.component.css` | Clean at inspection | Scope header surface, border, blur/elevation, responsive adjustments, and theme styling to the navbar component. |
| `retailpos-frontend/src/app/components/layout/sidebar.component.html` | Clean at inspection | Restyle brand, navigation groups/items, active/expanded/disabled states, collapse presentation, and mobile drawer/backdrop. Add only necessary accessible attributes; preserve menu model and navigation handlers. |
| `retailpos-frontend/src/app/components/layout/sidebar.component.css` | Clean at inspection | Align dimensions (280px expanded / 60px collapsed targets), colors, spacing, active marker, transitions, and responsive rules to Able Pro; all dimensions remain subject to the screenshot gate. |
| `retailpos-frontend/src/app/components/layout/business-context-selector.component.ts` | Pre-existing untracked file | Restyle only the inline template and accessible labels/states. Preserve Super Admin gating, no-auto-selection rule, verified `select()` flow, list API context, loading/error states, and dashboard navigation. |
| `retailpos-frontend/src/app/components/layout/layout.component.spec.ts` | New test proposed | Cover composition and router outlet/shell bindings without re-testing authorization logic. |
| `retailpos-frontend/src/app/components/layout/navbar.component.spec.ts` | New test proposed | Cover accessible toggle names and that existing search, role/outlet switch, logout, and user-menu actions remain bound. |
| `retailpos-frontend/src/app/components/layout/sidebar.component.spec.ts` | New test proposed | Cover active/expanded state display, collapsed labels, disabled items, and mobile close after navigation using a stubbed router/service. |
| `retailpos-frontend/src/app/components/layout/business-context-selector.component.spec.ts` | New test proposed | Cover visibility only for Super Admin, disabled/restoring state, active/pending values, errors, and Apply dispatch without changing the service contract. |

**Explicitly not in the Phase 1B edit allowlist:** `MenuService`, `menu.model.ts`, `app.routes.ts`, `auth.guard.ts`, `hr-payroll-capability.guard.ts`, auth/session/error interceptors and services, feature page components/services/styles, `PosScreenComponent`, `tailwind.config.js`, dependency manifests/lockfiles, backend, mobile, and every purchased template file.

No standalone template/selector HTML or CSS file exists: its template is inline in the existing TS component. If that file changes, review the diff to prove only HTML presentation/accessibility changed.

## 4. Final shell design decisions

These are approved visual targets for the light-theme first pass; browser validation may require responsive adjustment:

- Use Able Pro Angular 3.3.0 **full-version** as the visual reference, not as source to transplant.
- Light appearance is the default. Use template primary blue **`#4680ff`**, neutral light surfaces, and the purchased theme's semantic ramps as detailed in [`DESIGN_SYSTEM.md`](./DESIGN_SYSTEM.md).
- Use Inter typography after local font asset/license and load verification; do not add an external font dependency.
- Aim for a **280px expanded / 60px collapsed** sidebar and approximately **74px** header. At 768px and 390px, the navigation should use an overlay drawer rather than permanently consuming the page width. Validate exact transitions visually; do not blindly copy Able Pro's breakpoint values because the current app uses Tailwind's different defaults.
- Keep light/dark-compatible semantic CSS variable roles inside the shell's explicit theme scope. Dark mode is deferred; no mode switch or dark palette styles are required in 1B.
- Preserve current POS checkout interaction layout and every routed page structure.
- Keep current Tailwind 3.4 and Angular 21. Do not add Angular Material, Bootstrap, a new styling framework, or browser-test dependencies during 1B.
- Leave current sidebar density/collapse persistence, navigation section and route selection behavior, menu permission filtering, business context, outlet/role switching, and menu data unchanged.

## 5. Regression-risk assessment

| Risk | Rating | Why it matters | Required containment |
|---|---|---|---|
| Permission-filtered menu and route authorization | **Very high** | Styling can accidentally expose hidden items or replace guarded navigation. Several relevant routes/menu files are already dirty. | No `MenuService`, menu model, route, guard, or permission edits. Test role, wildcard, route-direct-access, and capability cases. |
| Super Admin business context | **Very high** | Context selector is a pre-existing untracked component; unverified context must not enable scoped routes/API calls. | Preserve selector class logic and service call; no context-selection change. Verify the existing context/startup specs and approved safe browser identity before 1B acceptance. |
| Login/logout, token refresh, unauthorized 403 | **High** | Navbar actions are attached to current auth services; unauthorized responses must not force logout. | Preserve handlers and interceptors; rerun auth, session-interceptor, and route guard tests. |
| Menu active/expanded state and navigation | **High** | Active route sync is in the dirty layout TS; expanded section is persisted in the sidebar; collapsed/mobile states share service signals. | Keep layout/sidebar TS behavior out of scope. Add presentational binding tests and click/keyboard browser cases. |
| Business/outlet switching | **High** | Navbar sends existing role/outlet switch requests and returns to dashboard; selector sends verified business selection requests. | Only edit markup/classes/ARIA. Test success/error/disabled states without changing payloads or sequence. |
| Responsive overlay and accessibility | **Medium–High** | Drawer/backdrop, hidden labels, icon buttons, focus order, keyboard Escape, and click-outside may regress as geometry changes. | Explicit keyboard/focus checks at all viewport targets; use semantic controls and accessible names, and preserve existing behavior. |
| API/form contracts | **Low for the proposed file allowlist; high if scope expands** | Shell work should not call feature APIs except existing context/outlet/header paths. | No service method/request changes. Fail review if the diff reaches feature pages or API/service code. |
| POS checkout | **Very high / excluded** | Core transaction interactions and persistent business data are involved. | Do not modify POS component/template/styles or run a transaction. Validate only that shell visual offsets do not cover it once a safe authenticated session is available. |
| Worktree integration | **High** | 168 existing Angular paths are dirty/untracked, including the shell template, selector, styles, menu, guards, and routes. | Inspect exact diffs, maintain an allowlist, preserve current hunks, stage only reviewed paths/hunks if later requested, and never clean/reset/stash. |

## 6. Detailed Phase 1B implementation plan

1. **Reconfirm and protect the baseline.** Recheck `git status --short`; record the current diffs/hashes of the allowlisted files. Confirm API availability and obtain a user-approved safe test account through the approved local development process. Do not reuse any inferred seed credential.
2. **Capture authenticated before screenshots.** At 1440 × 900, 768 × 1024, and 390 × 844, capture shell, dashboard, product list, one representative form, a modal/filter drawer, and POS checkout. Use stable read-only test data. Store images in an approved review location outside the purchased template and do not perform create/submit/checkout actions.
3. **Add scoped theme values only.** Define the approved primary, neutral/surface, text, border, focus, semantic, radius, and elevation variables under the shell's root scope in `styles.css`. Keep default global values and existing Tailwind palette/breakpoints untouched. No dark styles beyond semantic structure; no switch.
4. **Restyle shell geometry.** Update `layout.component.html/css` to scope the shell and set responsive topbar/content offsets. Tune against existing behavior; the main content must not be obscured by fixed nav.
5. **Restyle toolbar/navbar.** Update only the navbar HTML/CSS for light surface, target height, spacing, controls, and mobile arrangement. Keep all current search, role/outlet switch, profile menu, settings, and logout bindings.
6. **Restyle sidebar/drawer.** Update only sidebar HTML/CSS for 280px/60px targets, section and row hierarchy, active marker, labels/icons, focus/hover/disabled states, and overlay drawer. Preserve menu service signals, routes, click handlers, expanded-group persistence, and current density behavior. Implement accessible names/expanded state, Escape-to-close and focus return only if the changes do not bypass existing state/handlers; add focused tests.
7. **Restyle business-context selector.** In the pre-existing inline template, change only presentation and accessibility markup. Preserve role gate, no automatic business selection, server verification, request context, busy/error display, and dashboard navigation.
8. **Add focused shell tests.** Add component specs listed in the allowlist. Keep existing auth/permission/context tests as the authority for behavior; do not rewrite or relax them to accommodate visual changes.
9. **Run behavior validation.** Run focused shell specs, then the complete existing Angular test suite and production build. Rerun auth, route/menu consistency, capability, business-context startup/service, session interceptor 403, and POS existing specs; verify no application source outside the allowlist changed.
10. **Compare browser evidence.** Repeat the same six pages/viewports and compare to before screenshots. Review responsive overflow, page offsets, menu state and target sizes. Manually test keyboard navigation, focus visibility, Escape/backdrop behavior, screen-reader names, and active/expanded state. Do not claim visual regression coverage until before/after images and results exist.
11. **Review and acceptance.** Review the complete diff against the allowlist, run `git diff --check`, inspect status to ensure unrelated work remains, and have the reviewer compare screenshots and regression results. Do not stage unrelated paths or commit without an explicit request.

### Test matrix

| Area | Automated checks | Browser/manual checks |
|---|---|---|
| Layout | Outlet/rendering, shell bindings, no unexpected route changes | Main content offsets, sticky header, no overlap or horizontal page scroll at all target sizes |
| Navbar | Accessible control names; existing action bindings | Tab order, search, profile menu, role/outlet menu, close behavior, logout action wiring (do not actually log out a shared identity without coordination) |
| Sidebar | Active and expanded group, persisted visual state, disabled items, mobile close | 280/60 target at desktop, overlay at tablet/mobile, active marker, scrolling, keyboard focus, Escape/backdrop/focus return |
| Context selector | Super Admin-only rendering, pending/active value, restore/busy/error/apply state | Confirm display and disabled state with safe Super Admin identity; do not change to another context unless explicitly approved and isolated test data is used |
| Auth/permissions | Existing guard, menu, route consistency, auth, session/interceptor, context and capability specs | Allowed/denied role menu, direct protected URL, verified/unverified Super Admin context, 403 without logout |
| Pages unaffected | Existing POS and representative feature specs | Dashboard, products, form, modal/drawer, POS before/after screenshots; no data mutation |

The frontend manifest currently has no Playwright/Cypress/WebDriver dependency or configured e2e script. Phase 1B should first use the approved integrated browser/manual runner if available. A repeatable CI browser runner is a separate dependency decision; do not add one without compatibility and approval review.

## 7. Phase 1B acceptance checklist

- [ ] Approved API is reachable on the isolated development environment; safe test identity confirmed without using production or inferred seed credentials.
- [ ] Before screenshots for all six requested screens are captured at all three target viewports and stored in the approved review location.
- [ ] Only the section 3 allowlist has implementation changes; no route, guard, menu data/filtering, service/API, feature-page, POS, backend/mobile, or purchased-template changes.
- [ ] Approved light theme uses Able Pro blue `#4680ff` and Inter with scoped semantic tokens; existing global page styles remain unchanged.
- [ ] Expanded/collapsed sidebar and header targets are visually validated and adjusted if needed at desktop/tablet/mobile.
- [ ] Mobile drawer, active/expanded state, keyboard navigation, focus, accessible labels, and close behavior pass automated/manual checks.
- [ ] Business/outlet selector retains server-verified selection, permission scope, error, and busy behavior.
- [ ] Existing auth, route permission, wildcard, capability, Super Admin context, logout, and 403-without-logout tests pass unchanged.
- [ ] Focused shell tests, full 43-file unit suite, and production build pass; bundle warnings/budgets are recorded.
- [ ] After screenshots at matching viewports are reviewed and no unapproved regressions remain.
- [ ] POS interaction and all API contracts are unchanged; no transaction or persistent business data was created.
- [ ] Exact diff/status and `git diff --check` are reviewed; unrelated pre-existing changes remain untouched.

Phase 1A does not satisfy the blocked authenticated screenshot checklist items and does not start Phase 1B implementation.
