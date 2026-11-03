# RetailPOS UI Standardisation — Phase 2A Report (Design Tokens and Core Shared UI)

## Status: PASSED for automated gates; authenticated browser verification NOT performed

Centralised tokens and the core shared UI kit are implemented in production Angular code. Build, the full unit suite and `git diff --check` pass. No existing screen adopts the kit yet, so no feature behaviour changed. Visual checks were done only on a **synthetic harness** (see "Validation limits"); there was no authenticated browser verification.

## Results

| Gate | Result |
|---|---|
| Targeted kit specs | **PASS** — 4 files / 61 tests (`components/shared/ui/*.spec.ts`) |
| Full unit suite | **PASS — 51 files / 323 tests** (Phase 1B baseline 47 / 262; +4 files, +61 tests). No existing test edited or weakened. |
| Production build | **PASS**, no warnings, no budget warnings |
| Bundle | Initial total 2.51 MB raw (unchanged) / 392.00 kB transfer (+1.31 kB). `main` JS is byte-identical to Phase 1B (hash `O3WNEUIM`, 2.43 MB / 381.02 kB) because no screen imports the kit yet. Global CSS 74.25 → 81.48 kB raw (9.67 → 10.98 kB transfer): tokens plus the `ui-control`/`ui-check` classes. |
| `git diff --check` | **PASS** (tracked files); no trailing whitespace in the new files |
| Emitted CSS | Existing `.btn-primary`, `.table th`, `.badge-success`, `.input`, receipt/quick-customer and shell rules still present; new `ui-*` classes survive purging |

## Design tokens added (`retailpos-frontend/src/styles.css`)

Defined on `:root, [data-theme='light']`. Defining custom properties restyles nothing; only the shell and `ui-*`/shared components consume them. Phase 1B values are unchanged; only the `--rp-shell-*` dimensions stay scoped to `.rp-shell`.

| Group | Tokens |
|---|---|
| Colour ramps (Able Pro values) | `--rp-color-primary-50…900` (`#4680ff` = 500), `neutral-0…900`, `success/warning/danger-50…900` (subset), `primary-soft` |
| Action colours | `--rp-color-primary` (accents/icons), `--rp-color-action` / `-hover` / `-active` (primary-700/800/900), `--rp-color-on-action`, `--rp-color-primary-fg` (links/outline text), `--rp-color-secondary`, `-secondary-bg(-hover)`, `--rp-color-danger-action(-hover/-active)` |
| Status | `--rp-color-status-{success,warning,danger,info,neutral}-{bg,border,fg,icon}` |
| Surfaces / text / borders | `--rp-color-surface-{page,card,muted,hover,sidebar,header}`, `--rp-color-text-{primary,secondary,inverse,link}`, `--rp-color-border`, `--rp-color-border-control` (neutral-500, 3.0:1), `-control-hover` |
| Focus and disabled | `--rp-color-focus-ring`, `--rp-focus-ring-width/offset`, `--rp-color-disabled-{bg,fg,border}` |
| Typography | `--rp-font-family-base` (Inter first, existing system-font fallback; `-shell` kept as alias), `-mono`, weights, and size/line pairs for page title (24px), section heading (16px), body (14px), label (13px), helper (12px), table header (12px) / cell (14px), breadcrumb (13px) |
| Spacing | `--rp-space-0…12` (4px base: 0, 4, 8, 12, 16, 20, 24, 32, 40, 48) |
| Shape / elevation | `--rp-radius-{sm,md,control,card,pill}` (4, 6, 8, 12px, pill), `--rp-shadow-{none,popover,drawer,modal}` (cards are flat with a 1px border, as in Able Pro) |
| Component sizing / density | `--rp-control-height-{sm,md,lg}` (32/40/44), `--rp-control-height`, `--rp-control-padding-x`, `--rp-card-padding`, `--rp-form-gap`, `--rp-form-label-gap`, `--rp-table-cell-padding-{x,y}`. `data-density="compact"` on any container tightens them (control 34px, card padding 16px, ...). |

Accessibility decisions (measured contrast, computed from the token values): white on `action` 4.51:1, hover 5.06:1, active 6.27:1; danger button 5.31:1; links/outline `primary-fg` 5.06:1; secondary text 5.49:1; error text 5.31:1; status badges 4.59–13.70:1; control border 3.02:1, focus ring 3.63:1 (non-text 3:1). The raw `#4680ff` is 3.63:1, so it is used for accents/icons only, never for text or filled buttons. Control borders use neutral-500 rather than Able Pro's lighter neutral-300 to meet 3:1.

**Tailwind (`tailwind.config.js`, additive only):** `rp-*` colours (`bg-rp-card`, `text-rp-ink-secondary`, `border-rp-border`, status bg/fg, ...), `rounded-rp-control`, `rounded-rp-card`, `shadow-rp-popover`, `font-rp`, and a safelist so `ui-control`/`ui-check`/`ui-form-stack` are not purged before first use. The existing `primary` palette and utilities are untouched.

## Shared components (`retailpos-frontend/src/app/components/shared/ui/`)

Inspection found no existing button, card, badge, header, form-control or state component (only `product-variant-cell` in `shared/`; alert, searchable dropdown and filter drawer serve different purposes), while `.btn` appears in ~66 files and loading/empty/error text in ~190, so each piece is justified. Everything uses its own `ui-`/`appUi` namespace so it cannot collide with legacy `.btn/.card/.badge/.input` or the shell's component-scoped `.rp-*` classes. No services were added.

| Piece | Selector | Notes |
|---|---|---|
| `UiButtonComponent` | `button[appUiButton], a[appUiButton]` | Decorates the native element (no wrapper). Variants `primary/secondary/outline/ghost/danger`, sizes `sm/md/lg`, `loading`, `iconOnly`, `block`. Native `[disabled]` stays the disabled mechanism. `type` defaults to `button`. While `loading`: spinner, `aria-busy` + `aria-disabled`, still focusable, and the click is swallowed in the capture phase before any consumer handler or form submission (duplicate-submit protection; works for links). Dev-mode warning for icon-only buttons without a name. |
| `UiControlDirective` | `input/select/textarea[appUiControl]` | Attribute directive, **not** a value accessor: Reactive Forms and `ngModel` are untouched. Adds `ui-control` (or `ui-check__input` for checkbox/radio), stable `id`, `aria-invalid` after touched/dirty, `aria-required`, merged `aria-describedby`. Covers text, number, select, textarea, search (built-in icon), checkbox, radio, disabled and read-only. |
| `UiFormFieldComponent` | `app-ui-form-field` | Label (linked by `for`), required marker (visible `*` plus screen-reader text), hint, live error region. Messages come from the projected control's validators (defaults for required/email/min/max/length/pattern), `[messages]` per key, string errors from `setErrors`, or `[error]` from an API. Default change detection on purpose (form state is not signal based). |
| `UiCardComponent` | `app-ui-card` | Title/subtitle (heading level 2–4), `[uiCardActions]`, content, `[uiCardFooter]`, `padded`, `loading` (shows the standard loading state, content stays alive, `aria-busy`). |
| `UiBadgeComponent` | `app-ui-badge` | `success/warning/danger/info/neutral`. Status is always text; non-neutral variants also show a distinct icon (`aria-hidden`), so colour is never the only cue. |
| `UiPageHeaderComponent` | `app-ui-page-header` | Real `h1`/`h2`, description, breadcrumb `nav` (links for ancestors, `aria-current="page"` last), `[uiPageSecondaryActions]` and `[uiPagePrimaryActions]` slots; actions stack full-width on phones with the primary first. |
| `UiStateComponent` | `app-ui-state` | `kind="loading|empty|error"`, title/message (sensible defaults), compact mode, projected action area. Loading is `role="status"`, error `role="alert"`, empty is plain content. |
| `index.ts` | | Re-exports everything and `UI_KIT` (a 7-item array for `imports: [...UI_KIT]`). |

Global classes (in `@layer components`, so Tailwind utilities can still override): `.ui-control`, `.ui-control--invalid`, `.ui-check`, `.ui-check__input`, `.ui-form-stack`.

### Usage examples

```ts
@Component({ standalone: true, imports: [ReactiveFormsModule, ...UI_KIT], templateUrl: './product-edit.html' })
export class ProductEditComponent { form = inject(FormBuilder).nonNullable.group({ name: ['', Validators.required] }); }
```

```html
<app-ui-page-header title="New product" description="Add an item to the catalogue"
    [breadcrumbs]="[{ label: 'Products', link: '/products' }, { label: 'New' }]">
  <a uiPageSecondaryActions appUiButton variant="outline" routerLink="/products">Cancel</a>
  @if (canCreate()) {
    <button uiPagePrimaryActions appUiButton type="submit" form="product-form" [loading]="saving()">Save</button>
  }
</app-ui-page-header>

<app-ui-card title="Details" [loading]="loading()">
  <form id="product-form" class="ui-form-stack" [formGroup]="form" (ngSubmit)="save()" data-density="compact">
    <app-ui-form-field label="Name" hint="Shown on receipts" [required]="true" [error]="serverErrors().name">
      <input appUiControl formControlName="name" />
    </app-ui-form-field>
    <app-ui-form-field label="Category">
      <select appUiControl formControlName="categoryId">...</select>
    </app-ui-form-field>
    <label class="ui-check"><input appUiControl type="checkbox" formControlName="active" /> <span>Active</span></label>
    <app-ui-badge variant="warning">Draft</app-ui-badge>
  </form>
</app-ui-card>

@if (loadFailed()) {
  <app-ui-state kind="error" title="Could not load products" [message]="error()">
    <button appUiButton variant="outline" (click)="reload()">Retry</button>
  </app-ui-state>
} @else if (!items().length) {
  <app-ui-state title="No products yet"><a appUiButton routerLink="/products/new">Add product</a></app-ui-state>
}
```

### Reuse guidance

- New screens and any screen being migrated use the kit; screens not yet migrated keep their legacy classes. Migrate a whole screen block at once; do not mix both systems inside one block.
- Features keep ownership of permissions (`@if (canCreate())`, route guards), validators, payloads, loading/error flags and retry handlers. Shared UI only presents.
- Need something the kit lacks (tables, alerts, modals are next)? Extend `components/shared/ui/` using tokens; do not add page-local copies or new global `.btn/.card/.table` rules.
- Pair `data-density="compact"` with dense ERP screens; leave comfortable as the default for forms and dashboards.

## Copilot governance

`.github/copilot-instructions.md` (pre-existing, untracked, infrastructure section only) received an appended **UI Design System (Angular frontend)** section with 11 rules (tokens, shared kit reuse, Able Pro language, no hard-coded values, no duplicates or legacy-class edits, page/form/list patterns, permission preservation, accessibility/responsive, logic/presentation separation, no new UI frameworks without approval, verification honesty). The original 15 lines are unchanged.

## Tests added (61, `components/shared/ui/`)

- `ui-button.component.spec.ts` (19): all variants, sizes/block, default `type="button"`, native disabled, loading (aria, spinner, swallowed click, still focusable, recovery), icon-only name and dev warning, explicit submit still submits, duplicate-submit blocking inside a form, link buttons.
- `ui-form-controls.spec.ts` (15): real Reactive Forms (text/email/select/textarea/checkbox/radio/readonly) and `ngModel`; label `for`, required semantics, `aria-describedby`, errors only after interaction, validator/custom/API/`setErrors` messages, value/dirty/disabled bindings preserved.
- `ui-display-components.spec.ts` (23): card projection/headings/loading/flush; badge variants, text and distinct icons; page header heading level, breadcrumbs, action slots; state roles, defaults, retry projection.
- `ui-kit.spec.ts` (4): `UI_KIT` composes header + card + form field + badge + state; header submit button outside the form; duplicate-submit blocking; feature-owned retry.

Two issues surfaced and were fixed by the tests: `UiFormFieldComponent` initially used `OnPush` and never re-rendered when a control became touched (now default change detection), and plain-property test hosts caused `NG0100` under zoneless fixtures (hosts now use signals).

## Validation limits (read this)

- **No authenticated browser verification.** The development API is unavailable and no safe test account exists; authentication was not bypassed.
- Visual checks used a **synthetic static harness** (outside the repo, since deleted): the kit's real component CSS mechanically converted from `:host`, the real built global CSS, hand-mirrored component DOM, and the Font Awesome CDN. It verified computed styles (colours, 40/34px density, 32/44px sizes, 12px bordered flat cards, focus ring `2px solid #4680ff`, invalid/disabled/read-only states), no horizontal overflow, and the stacked mobile page-header layout (screenshot lagged one resize behind, so mobile was confirmed by both a screenshot and measured layout). It does **not** prove the Angular templates render identically (those are covered by the unit specs) and says nothing about existing screens.
- No visual-regression tooling was run. Hover/active states are verified by CSS rule presence and computed values, not interaction screenshots. No assistive-technology testing was done.
- Because nothing imports the kit yet, "no global CSS regressions" rests on: new class names are all namespaced, the legacy rules are present in the emitted CSS, and the token block only declares custom properties. A real before/after comparison of existing screens is still pending the authenticated environment.

## Risks and follow-ups

1. **Stylesheet weight:** +7.2 kB raw / +1.3 kB transfer of global CSS for tokens and control classes; negligible against budgets.
2. **Hover/focus/ARIA behaviour in real screens** is unverified until a screen adopts the kit in an authenticated session (Phase 2B/3 pilot).
3. **Two systems coexist** (legacy `.btn/.card/...` and the kit) by design; the Copilot rules prevent mixing within a screen, and the roadmap retires legacy rules per screen.
4. **Inter** is still not bundled (licence review pending); the stack falls back to system fonts.
5. **Not built (outside the Phase 2A list):** tables, alerts/toasts, modals, dropdown/searchable select. Control-border colour (neutral-500) is intentionally darker than Able Pro's for WCAG 1.4.11; confirm with design.

## Changed-file inventory

Modified (pre-existing dirty or tracked files, targeted edits only):
- `retailpos-frontend/src/styles.css` — Phase 1B token block replaced by the extended `:root` token set + `[data-density]` + shell dimensions; appended `ui-*` control classes. All earlier hunks (receipt, quick-customer CSS) kept.
- `retailpos-frontend/tailwind.config.js` — +36 lines, additive (`rp-*` colours/radii/shadow/font, safelist).
- `.github/copilot-instructions.md` — appended UI section.
- `Docs/UI-Standardisation/DESIGN_SYSTEM.md`, `MIGRATION_ROADMAP.md` — update notes.

Created:
- `retailpos-frontend/src/app/components/shared/ui/`: `index.ts`, `ui-badge.component.ts`, `ui-button.component.ts`, `ui-card.component.ts`, `ui-control.directive.ts`, `ui-form-field.component.ts`, `ui-page-header.component.ts`, `ui-state.component.ts`, and 4 specs (`ui-button`, `ui-form-controls`, `ui-display-components`, `ui-kit`).
- `Docs/UI-Standardisation/PHASE_2A_REPORT.md`.

Not touched: shell components (`layout/`), `MenuService`, routes, guards, interceptors, auth/business services, every feature page, POS, `angular.json`, package manifests, backend (`src/`), Flutter, `design-reference/`. Repository status count moved 916 → 917 (the only new tracked-file change is `tailwind.config.js`; the new files sit inside already-untracked directories). No commit was made.
