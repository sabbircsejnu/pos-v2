# RetailPOS UI Standardisation — Phase 1B Report (Application Shell)

## Status: PARTIALLY PASSED

The Able Pro-inspired light shell is **implemented**, and every automated gate passed (build, full unit suite, allowlist check). **Authenticated browser verification is still pending**: the development API (`localhost:5005`) was unavailable and no safe test account was provided, so the real shell, dashboard, product list, forms, modals and POS were *not* visually verified. See "Validation limits".

## Files changed (all within the Phase 1A allowlist)

| File | Change |
|---|---|
| `retailpos-frontend/src/styles.css` | Appended shell-scoped light tokens under `.rp-shell[data-theme='light']` (colours, radius, shadows, font stack, header/sidebar dimensions). Pre-existing receipt/quick-customer rules untouched; no change to base or shared primitives. |
| `components/layout/layout.component.html` / `.css` | Shell root with `data-theme="light"` and `rp-shell--collapsed`; skip link; focusable `<main id="main-content">`; content offsets driven by tokens. The pre-existing `<app-business-context-selector>` insertion is preserved. |
| `components/layout/navbar.component.html` / `.css` | Able Pro-style translucent 74px header (64px below `lg`), offset by the sidebar width; restyled search, icon buttons, outlet switcher, user menu and popovers; ARIA names/state; Escape closes menus. |
| `components/layout/sidebar.component.html` / `.css` | 280px sidebar / 60px collapsed rail; brand header; uppercase section captions; soft-blue active row with accent bar; hover/focus-visible/disabled states; off-canvas drawer + backdrop below `lg`; semantic `nav`/`ul`/`li`, `aria-current`, `aria-expanded`. |
| `components/layout/business-context-selector.component.ts` | Inline template + component `styles` only; class logic, subscriptions, API calls and `data-testid`s unchanged. |
| 4 new specs in `components/layout/` | `layout`, `navbar`, `sidebar`, `business-context-selector` (+33 tests). |

Not touched: `MenuService`, `menu.model.ts`, routes, guards, interceptors, auth/business services, `layout.component.ts`, feature pages, POS, `tailwind.config.js`, package manifests, backend, Flutter, Able Pro sources. No new dependencies; no Material/Bootstrap.

## Validation results

| Gate | Result |
|---|---|
| Production build | **PASS**, no warnings. Initial 2.51 MB raw / 390.69 kB transfer (baseline 2.50 MB / 389.05 kB); styles 74.25 kB (baseline 76.66 kB). |
| Unit tests | **PASS — 47 files / 262 tests** (baseline 43 / 229; +4 files / +33 tests). No existing test modified or weakened. |
| Allowlist | **PASS** — only 5 modified shell files + 4 new specs vs. the pre-change snapshot; `git diff --check` clean. |
| Stale class references | None remaining (`topbar-shell`, `app-sidebar` class, `nav-item`, `--sb-*` etc.). |
| Synthetic geometry check | **PASS** (see below). Not the real app. |
| Authenticated browser checks | **NOT RUN** (blocked). |

Synthetic harness (static markup mirroring the templates, loaded with the real built/component CSS; kept outside the repo and since deleted) measured after transitions settled: 1440px → sidebar 280, header 74 starting at x=280; collapsed → sidebar 60, header from x=60, labels/captions hidden; 1024px → no overflow, switcher label collapses to icon; 768/390px → header 64, drawer off-canvas and `visibility:hidden` (out of tab order), brand + search icon + close button shown; drawer open → 280px at z-index 55; no horizontal scroll at any size.

## Deliberate deviations and behaviour notes (presentation-only)

- **Contrast:** raw `#4680ff` is 3.6:1 on white. It is used for the brand mark, accent bar and active icon; filled buttons use `#1c76da` (4.5:1) and active labels `#0d59cf`.
- **Inter** is declared first in the shell font stack but **not bundled** (adding the template's font files is outside the allowlist/licence review); it falls back to system UI fonts until the asset is added.
- Sidebar width is now fixed at 280px; density only changes row height (previously also changed width).
- Removed the sidebar's unbound "Search here" input (no handler); the header search is the real one.
- Sidebar's own toggle is now a mobile-only close button; the header button toggles at every size.
- Outlet switcher is now reachable below `md` (icon-only); previously hidden entirely.
- Expanded groups no longer clip at 540px (raised to 2400px).
- Top bar element changed from `<nav>` to `<header>`; the "SaaS ERP Platform" subtitle was dropped (brand sits in the sidebar from `lg`).
- The notification badge "3" is a pre-existing hard-coded placeholder and was left as is.

## Risks requiring review

1. **POS and fixed offsets (highest):** header grows 56 → 74px at `lg`, and `pos-screen.component.css` hard-codes `calc(100vh - 64px)` (also `global-search` uses `100vh - 3.5rem`). POS already slightly exceeded the viewport, so it may scroll ~10–18px more on desktop. Needs authenticated verification; fixing it means a (separately approved) POS/feature CSS change.
2. **Sticky page headers** (`sticky top-2` in PO/transfer/product forms) sit behind the fixed header; this existed at 56px and is slightly more noticeable at 74px.
3. **Pre-existing edge case:** if the desktop rail is collapsed and the viewport is then narrowed, the drawer shows every group expanded (`isGroupCollapsed`/`toggleGroup` in `sidebar.component.ts`, outside the allowlist).
4. Content padding rose to 16px / 24px (`lg`) from 16 / 20px, and page background is now `#f8f9fa` (was `gray-50`).
5. Layout shifts animate for 200ms when crossing the `lg` breakpoint (reduced-motion respected).

## Remaining before Phase 1B can be marked fully passed

- Run against an approved development API with a safe test account; capture before/after shell, dashboard, product list, form, modal/drawer and POS at 1440×900, 768×1024, 390×844 (no data mutation).
- Manual keyboard/focus pass (skip link, drawer Escape/backdrop, menus) and screen-reader spot check.
- Decide on bundling Inter and on any POS offset adjustment.
