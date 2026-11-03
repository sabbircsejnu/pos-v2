# RetailPOS UI Design System

## Status and scope

This document defines the approved visual and component contracts for the Angular 21 + Tailwind application, grounded in the purchased Able Pro Angular 3.3.0 `full-version` theme. Shared tokens and UI-kit foundations are implemented in `retailpos-frontend/`; feature screens are migrated deliberately, not presumed to use the new styling everywhere.

> **Update (Phase 1B):** the shell-scoped light tokens described below are now implemented in [`styles.css`](../../retailpos-frontend/src/styles.css) under `.rp-shell[data-theme='light']`, with no change to global base/shared rules or `tailwind.config.js`. See [`PHASE_1B_REPORT.md`](./PHASE_1B_REPORT.md) for what was applied and the contrast adjustments (filled buttons use primary-700 and active labels primary-900 instead of the raw `#4680ff`, which is 3.6:1 on white).

> **Update (Phase 2A):** the token set was extended (full colour ramps, semantic roles, typography, 4px spacing scale, radii, shadows, component sizes, compact/comfortable density) and moved to `:root, [data-theme='light']`; only the `--rp-shell-*` dimensions remain scoped to `.rp-shell`. Values are unchanged, so the shell is unaffected. The component contracts below are implemented in `components/shared/ui/`. **Update (Phase 2B):** the Data Table, dialog/confirm dialog, inline alert and focus trap are added and the existing alert, dropdown, filter drawer and variant picker use the same tokens: [`PHASE_2B_REPORT.md`](./PHASE_2B_REPORT.md). Token reference, usage examples and reuse guidance: [`PHASE_2A_REPORT.md`](./PHASE_2A_REPORT.md).

### Approved shell direction

The Phase 1A design decisions are:

- Default appearance is **light**, using Able Pro Angular's blue primary palette at **`#4680ff`**.
- Typography is **Inter**, using the locally packaged Able Pro variable font after asset/license and loading review.
- Sidebar visual targets are **280px expanded / 60px collapsed**; the header target is approximately **74px**. Both are responsive targets to validate, not fixed dimensions to impose on every viewport.
- Keep the existing Angular 21 standalone + Tailwind 3.4 architecture. Add neither Angular Material nor Bootstrap.
- Preserve the current POS checkout interaction layout; the shell migration must not restructure the checkout screen.
- Dark mode is deferred. Keep the token model theme-scoped so a later light/dark switch can be added without introducing another styling framework.
- Use semantic CSS variables under the shell's explicit theme scope and the existing Tailwind stack. Do not replace the global neutral `primary` palette, rewrite shared primitives, or change global base styles as part of the shell-only phase.

The approved primary and shell dimensions are directly from the purchased template. Their fit to current nav density and viewport behavior remains subject to Phase 1B browser validation.

## Reference hierarchy

1. **Primary:** [`able-pro-angular-v3.3.0/full-version`](../../design-reference/able-pro/able-pro-angular-v3.3.0/full-version/), especially `src/app/@theme/styles/`, `src/app/@theme/layouts/`, and `src/app/@theme/components/`.
2. **Secondary:** [`able-pro-bootstrap-v9.6.2/src`](../../design-reference/able-pro/able-pro-bootstrap-v9.6.2/src/) for static examples only. Bootstrap is not a second Angular component system.

Useful primary references:

- [`theme-palette-colors.scss`](../../design-reference/able-pro/able-pro-angular-v3.3.0/full-version/src/app/@theme/styles/theme-palette-colors.scss) and [`theme-dark-palette-colors.scss`](../../design-reference/able-pro/able-pro-angular-v3.3.0/full-version/src/app/@theme/styles/theme-dark-palette-colors.scss): palettes for light and dark themes.
- [`theme.scss`](../../design-reference/able-pro/able-pro-angular-v3.3.0/full-version/src/app/@theme/styles/theme.scss): typography configuration and generated palette utility/custom-property pattern.
- [`_variables.scss`](../../design-reference/able-pro/able-pro-angular-v3.3.0/full-version/src/scss/_variables.scss), [`_menu.scss`](../../design-reference/able-pro/able-pro-angular-v3.3.0/full-version/src/app/@theme/styles/layouts/_menu.scss), and [`toolbar.component.scss`](../../design-reference/able-pro/able-pro-angular-v3.3.0/full-version/src/app/@theme/layouts/toolbar/toolbar.component.scss): shell geometry, navigation, and responsive toolbar.
- [`card.component.html`](../../design-reference/able-pro/able-pro-angular-v3.3.0/full-version/src/app/@theme/components/card/card.component.html), [`card.component.scss`](../../design-reference/able-pro/able-pro-angular-v3.3.0/full-version/src/app/@theme/components/card/card.component.scss), [`modal-dialog.component.html`](../../design-reference/able-pro/able-pro-angular-v3.3.0/full-version/src/app/@theme/components/modal-dialog/modal-dialog.component.html), and the demo form/table pages: component composition and states.

Able Pro uses Angular Material/CDK internally. Reference its appearance and interaction patterns only; RetailPOS uses Tailwind and project-owned CSS and adds no Material or Bootstrap dependency.

## Visual tokens

### Colour

Able Pro's default Angular theme selects the blue palette as primary, grey as accent/surface, and red as warning. The actual palette values below are from the purchased theme SCSS.

| Semantic token role | Able Pro source palette | Key values | Use |
|---|---|---|---|
| Brand / primary | Blue | `50 #e9f0ff`, `100 #dae6ff`, `200 #b5ccff`, `300 #90b3ff`, `400 #6b99ff`, `500 #4680ff`, `600 #2281df`, `700 #1c76da`, `800 #176cd6`, `900 #0d59cf` | Primary actions, active navigation, links, and focus accents. Use a consistent contrast pairing; the reference palette marks 500–900 as white foreground. |
| Neutral / accent (light) | Grey | `50 #ffffff`, `100 #f8f9fa`, `200 #f3f5f7`, `300 #dbe0e5`, `400 #bec8d0`, `500 #8996a4`, `600 #5b6b79`, `700 #3e4853`, `800 #1d2630`, `900 #131920` | Light surfaces, borders, secondary text, and dark text. |
| Neutral / accent (dark) | Grey dark | `50 #1d2630`, `100 #131920`, `200 #1d2630`, `300 #3e4853`, `400 #5b6b79`, `500 #8996a4`, `600 #bec8d0`, `700 #dbe0e5`, `800 #f3f5f7`, `900 #f8f9fa` | Dark surfaces and appropriately reversed text/border roles. |
| Success | Green | `50 #e6f5f0`, `100 #c0e5d9`, `200 #96d4bf`, `300 #6bc2a5`, `400 #4cb592`, `500 #2ca87f`, `600 #27a077`, `700 #21976c`, `800 #1b8d62`, `900 #107d4f` | Successful completion and positive status. |
| Warning | Yellow | `50 #fcf1e0`, `100 #f7dcb3`, `200 #f2c580`, `300 #edad4d`, `400 #e99c26`, `500 #e58a00`, `600 #e28200`, `700 #de7700`, `800 #da6d00`, `900 #d35a00` | Attention and caution states. |
| Danger / error | Red (Able Pro `warn`) | `50 #fbe5e5`, `100 #f5bebe`, `200 #ee9393`, `300 #e76767`, `400 #e14747`, `500 #dc2626`, `600 #d82222`, `700 #d31c1c`, `800 #ce1717`, `900 #c50d0d` | Validation errors, destructive actions, and failed states. Name the app token semantically; do not expose Angular Material's `warn` name as the app contract. |
| Info | Blue | Use the primary blue ramp with a pale background and readable foreground. | Informational messages. The reference has no separate standalone info ramp in its generated non-Material semantic classes. |

The reference palette also contains purple, indigo, pink, orange, teal, and cyan. They should not become additional app-wide meanings without a concrete RetailPOS semantic need. Status must not be conveyed by colour alone.

**Surface roles:** the light grey palette anchors page/surface shades (`#f8f9fa`, `#f3f5f7`) and borders (`#dbe0e5`); white is the light card surface. The reference's global header background is `rgba(248, 249, 250, 0.7)`. Dark-theme neutral anchors include `#131920` and `#1d2630`, with light text shades `#f3f5f7` and `#f8f9fa`. The actual theme also reverses primary text contrast in dark mode; do not simply reuse light-mode foreground values.

### Typography

- The Angular theme uses the locally supplied variable Inter font (`'Inter var'`) with normal and italic faces, `font-display: swap`, and variable weights 100–900.
- Reference typography levels include 14px body text at 1.5–1.57 line height; 12px captions at 1.66; and a 500-weight heading scale from 1.5rem through 2.25rem. Toolbar/label examples use 14–16px and weights 500–600.
- Proposed app roles: body, page title, section title, field label, helper/error, table header/body, and compact metadata. Preserve readable line height for dense ERP forms and tabular data.
- Reuse the purchased local font only after checking license/asset handling and loading cost. Receipt/barcode output is a separate print surface and must retain its deliberate fixed-width/print typography.

### Spacing and geometry

Able Pro does not define one standalone spacing-token table. Its SCSS uses recurring 4px-based increments alongside component-specific spacing. Observed values include 4px toolbar/icon margins, 8px gaps/radii, 10–16px control/navigation padding, 20px card content padding, and 24–25px shell gutters. Treat these as reference points, not as a claim that every existing value belongs to a formal scale.

| Element | Actual reference value |
|---|---|
| Header height variable | 74px |
| Sidebar width | 280px |
| Collapsed sidebar width | 60px |
| Collapsed active sidebar width | 300px |
| Toolbar content padding | `10px 25px` |
| Toolbar item height | 54px; 46px at viewport widths up to 576px |
| Sidebar section/menu padding | Examples include `16px 10px 16px 24px`, menu rows `14px 20px`, and nested rows 10–12px vertical |
| Card content | 20px padding in the Angular card component |
| General radius variable | 8px |
| Other component radii | Modal image 6px; menu row 5px; small badges/pills use 2px/4px/fully rounded shapes in specific contexts |

RetailPOS's future spacing tokens should map to these observed increments and preserve a compact data-entry density where the workflow needs it; do not impose the template's 74px header or 280px sidebar onto the current shell without phase-1 viewport review.

### Borders, shadows, and surfaces

- Use the light/dark accent grey ramp for neutral borders and surface separation.
- The general template radius is 8px, with smaller 5–6px radii on certain controls and dialogs.
- The layout sidebar shadow is explicitly `none`; the theme uses elevation selectively (for example, its configuration panel uses `rgba(19, 25, 32, 0.08) 0 8px 24px`).
- Cards use Angular Material surface/theme defaults with 20px content padding; a standalone card border/shadow value is not declared by the inspected `card.component.scss`. Do not invent one as a purchased-template “exact value.”
- RetailPOS tokens should express border, radius, and elevation separately so dense tables and dialogs can use different levels without page-specific arbitrary values.

### Breakpoints and density

The template custom grid declares `xs 0`, `sm 576px`, `md 768px`, `lg 992px`, `xl 1200px`, and `xxl 1400px`. Its toolbar additionally uses 567px/576px and navigation has a 1024px transition in some components.

The current Tailwind configuration retains Tailwind's standard breakpoints (sm 640, md 768, lg 1024, xl 1280, 2xl 1536). Do **not** replace them during this foundation: changing existing breakpoint keys could change many current screens. A later shell-specific migration should align only after usage and viewport regression checks.

The Angular template mixes Material table defaults with separate Ag-Grid demos; the inspected theme does not establish one application-wide row-height token. Do not assert a numeric Able Pro table density. For RetailPOS, define compact and comfortable table modes during the product pilot and approve the resulting row height from actual browser screenshots and pointer/keyboard usability.

## Central token strategy for Tailwind

### Current implementation

1. Token definitions live beside the Tailwind entry in [`retailpos-frontend/src/styles.css`](../../retailpos-frontend/src/styles.css), with utility-name mapping in [`retailpos-frontend/tailwind.config.js`](../../retailpos-frontend/tailwind.config.js).
2. The semantic `--rp-*` properties are defined for the light theme and consumed by the shell and shared UI kit. New component styles must consume those properties rather than duplicate palette, spacing, radius, typography or elevation literals.
3. Keep existing `primary` neutral shades and generic `.card`, `.btn`, `.input`, `.table`, `.alert`, and `.badge` rules intact until a screen is deliberately migrated and reviewed.
4. Add dark-theme role values only as one corresponding semantic set; do not create independent raw-value sources in CSS and Tailwind configuration.

### Token and legacy-style boundary

The semantic tokens in [`styles.css`](../../retailpos-frontend/src/styles.css) are consumed by the shell and namespaced shared UI-kit components. Existing `.btn`, `.card`, `.input`, `.table`, `.alert`, `.badge` and Tailwind `primary` consumers are legacy surfaces: do not change their shared rules or redefine their colors as part of component adoption. Migrate a screen as a deliberate, reviewed block and keep using the semantic tokens.

## Shared UI component contracts

These are presentation and interaction standards; existing RetailPOS components/services remain responsible for feature state, validation, authorization, and API behavior.

| Component standard | Visual contract | Interaction and accessibility contract | Reuse / scope |
|---|---|---|---|
| Buttons | Able Pro primary blue, neutral secondary/outline, quiet/tertiary, success, and destructive variants; consistent height, 8px general radius, spacing, icon gap, and loading/disabled states. Keep destructive and success colors semantic. | Native button semantics, visible focus, accessible name for icon-only controls, disabled state that matches the current action eligibility, no color-only meaning. Loading must prevent duplicate submission without losing the label/state. | Refactor existing `.btn` classes in place only when consumers are inventoried; preserve action permissions and event handlers. |
| Form fields | Shared filled and outline field presentations, compact/comfortable sizes, inset labels, optional prefix/suffix and clear action, helper/error text and character counter. Use token-backed surfaces and active underline. | Preserve Angular forms, validators, touched/dirty and server errors; keep labels/errors programmatically associated; retain native required/disabled/readonly behavior. Do not apply filled labels to checkables or file inputs. | Use `app-ui-form-field` plus native controls decorated with `appUiControl`; features own validators and API errors. |
| Date picker | Company-format text input and token-backed calendar popup, with month navigation, selected/today/min/max states and a compact trigger icon. | Strictly parse the configured date format and real calendar dates; support required/optional values, bounds, manual entry, Escape/outside close and focus restoration. Preserve date-only ISO values and existing timestamp semantics. | Use `app-ui-date-picker` for new date-only fields. Keep existing `appCompanyDate` fields unchanged until feature migration; compose date ranges from two controls and feature-owned range validation. |
| Cards | White/light or dark neutral surface, 20px content-padding reference, 8px general radius, restrained border/elevation, clear header/content/action regions. | Heading hierarchy remains semantic; loading/empty/error content is announced and not confused with empty business data. | Reuse existing `.card` / `.card-elevated` contracts and the Able Pro composition pattern; avoid wrapping cards around every data element. |
| Data tables | Neutral header, readable row/column alignment, compact/comfortable density, consistent borders, sorting and rounded semantic row-action icons, responsive horizontal overflow. Do not invent a template-specific row height; verify pilot screenshots. | Use real table semantics for tabular data, accessible sort state, row-specific action names, keyboard reachability, non-color-only status, and clear loading/empty/error/pagination state. | Use current semantic tables and `appUiActionButton` in `uiTableRowActions`; features keep permission checks, handlers and confirmation flow. Retain existing sorting/filter/pagination logic. |
| Steppers | Reusable horizontal and vertical step headers with numbered markers, optional descriptions and active/completed/pending/error states. | Provide Previous/Next, roving keyboard navigation, screen-reader state and optional completed-step navigation. A feature guard controls validation before advancing. | Use `app-ui-stepper` only for genuinely sequential workflows. Form state, validation, saves and API calls stay with the feature. |
| Modals and drawers | Consistent overlay, surface, radius, width, title, content spacing, action order, and mobile full-screen treatment. Use 6px only where matching the referenced modal pattern; general component radius remains 8px. | Focus enters and returns correctly, Escape/close rules are explicit, focus is contained while open, background is not operable, destructive confirmation names the consequence. | Restyle current modal/drawer components; use Angular-managed events and state, not Bootstrap JavaScript. |
| Dropdowns | Match field/menu surface, border, radius, elevation, selected/hover/focus/disabled states; align and size against trigger; avoid clipping in scrollable cards. | Keyboard navigation, Escape, correct expanded/selected semantics, outside-click handling and focus restoration; selected context must be visible. | Reuse current searchable dropdown and navbar context selectors; preserve route/context and business scope logic. |
| Alerts | Semantic success, warning, danger, and info surface/text/border combinations based on palette ramps; use icon and text, not color alone. | Errors remain visible and actionable; live-region behavior is appropriate to urgency and does not repeatedly announce background updates. | Reuse existing alert component and `.alert-*` classes; avoid duplicate notification APIs. |
| Status badges | Compact pill or subtle neutral/semantic chip with consistent typography and spacing; use Able Pro palette ramps and contrast pairings. | Include readable status text; do not make a color or icon the only status signal. | Existing `.badge-*` classes are a starting point; map business states centrally and consistently. |
| Page headers | Consistent title, optional description/breadcrumb, context, and primary actions. Follow the reference's modest typography hierarchy and align with the content container. | Heading level reflects page structure; action names are clear, available actions remain permission-gated, and narrow layouts wrap without hiding actions. | Add shared presentation only when the shell phase defines ownership; preserve page-specific titles/context. |
| Filter panels | Visually grouped, compact controls with a clear applied-filter count, reset action, and responsive collapse/drawer behavior. | Labels remain available on narrow screens; active filters are discoverable/removable; apply/reset do not change request semantics or drop filter state accidentally. | Reuse the existing advanced-filter drawer and feature filter logic. |

## Ready-for-phase-1 checklist

- [x] Primary Angular template and actual directory structure verified.
- [x] Existing Tailwind/shared style surface inventoried.
- [x] Baseline unit suite completed: 43 files, 229 tests passed.
- [x] Production build completed without warnings; initial bundle is below configured budget.
- [x] Token names and theme values documented; existing legacy component rules remain unchanged.
- [x] Authorization, context, and HTTP regression tests are identified in the Phase 0 report.
- [ ] Capture baseline browser screenshots at the selected viewports before shell implementation.
- [ ] Agree shell-specific light/dark scope and responsive behavior with product/design owners.

The unchecked items are phase-1 entry work, not claims of completed visual validation.

## Required reusable UI standards

These four shared patterns are the required foundations for future UI work and controlled migration.
They follow the local Able Pro Angular v3.3.0 references for filled inputs, the date picker calendar,
invoice-list action icons, and horizontal/vertical steppers. Reuse the shared UI kit; do not port
Able Pro/Material implementation code or migrate unrelated screens as part of adopting a pattern.

### Filled form controls

Use `app-ui-form-field` with `appearance="filled"` for text-like controls. It keeps native Reactive
Forms and `ngModel` controls in place and supports compact/comfortable density, inset labels, hint and
validation text, required/disabled/readonly states, optional clearing, prefix/suffix content, and an
optional character counter. Keep checkboxes, radios and file inputs in their suitable existing
patterns; do not force filled labels onto them.

```html
<app-ui-form-field label="Amount" appearance="filled" size="compact"
                   [required]="true" [clearable]="true" [counter]="true" [maxLength]="8">
  <span uiFieldPrefix>$</span>
  <input appUiControl formControlName="amount" maxlength="8" />
  <span uiFieldSuffix>USD</span>
</app-ui-form-field>
```

The feature still owns validators, submission and API errors. Add `appUiControl` to native projected
controls so labels and errors are associated without introducing a second form value accessor.

### Company date picker

Use `app-ui-date-picker` for new date-only fields. It reads the configured `CompanyDateService`
format, strictly parses the typed value (including real month lengths and leap years), and retains
date-only ISO (`yyyy-MM-dd`) form values. Bounds are inclusive ISO dates. The popup opens from the
input or calendar button, closes on selection, Escape, or outside click, and restores focus on
keyboard dismissal/selection. Existing `appCompanyDate` controls remain unchanged until a feature
is deliberately migrated. Date ranges use two date-picker controls and a feature-owned range
validator; keep any existing endpoint/timezone representation unchanged.

```html
<app-ui-date-picker label="Start date" formControlName="startDate"
                    [required]="true" min="2026-01-01" max="2026-12-31" />
<app-ui-date-picker label="End date" formControlName="endDate"
                    [min]="form.controls.startDate.value ?? ''" />
```

The feature owns cross-field range validation and any save/payload conversion. Do not rely on the
browser's locale-dependent date parsing.

### Table action icons

Use `appUiActionButton` inside the existing `uiTableRowActions` template. It provides a shared
rounded icon-button size, semantic view/edit/delete styling, tooltip and keyboard focus treatment.
The feature keeps permission checks and action handlers in its template. Use the shared confirm
dialog before performing destructive work.

```html
<ng-template uiTableRowActions let-row>
  @if (canView(row)) {
    <button appUiActionButton action="view" [attr.aria-label]="'View ' + row.name"
            (click)="view(row)">
      <i class="fas fa-eye" aria-hidden="true"></i>
    </button>
  }
  @if (canEdit(row)) {
    <button appUiActionButton action="edit" [attr.aria-label]="'Edit ' + row.name"
            (click)="edit(row)">
      <i class="fas fa-pen" aria-hidden="true"></i>
    </button>
  }
</ng-template>
```

For delete actions, keep the same directive and route the feature's click handler through
`app-ui-confirm-dialog`; the shared UI layer does not authorize or execute business operations.

### Stepper

Use `app-ui-stepper` only when a multi-section workflow benefits from sequential navigation. Both
horizontal and vertical layouts are supported, with numbered markers, descriptions, completed/error
states, navigation controls, accessible keyboard operation and optional navigation back to completed
steps. The feature owns the form groups, touched-state behavior, saving, and API calls. Supply
`canAdvance` when moving forward must validate the active section; it should mark the relevant
feature-owned controls as touched before returning `false`.

```html
<app-ui-stepper orientation="horizontal" [canAdvance]="canAdvanceStep"
                [allowCompletedNavigation]="true">
  <ng-template appUiStep="Personal" description="Employee details" [completed]="personalComplete()">
    <!-- Feature-owned form controls -->
  </ng-template>
  <ng-template appUiStep="Employment" description="Role and dates" [error]="employmentInvalid()">
    <!-- Feature-owned form controls -->
  </ng-template>
</app-ui-stepper>
```

Use vertical orientation for workflows with long or descriptive sections. Do not automatically
convert all forms, and do not restructure Employee Edit without first checking its current form
architecture and preserving values, validation, permissions and submit semantics.
