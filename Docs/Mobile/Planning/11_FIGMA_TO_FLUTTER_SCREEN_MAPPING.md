# Figma to Flutter Screen Mapping

**Status:** the Figma Make editor remains inaccessible (failed/403 resources), but the published prototype was inspected during M1 for Login, Owner Home, Stock Take, Insights, Forecast, Forecast rationale/production recommendation, Notifications, Profile/Settings, Stock Take History, and Stock Comparison. Inspection is not product-owner approval. Any screen/state not directly inspected and approved remains **PENDING VISUAL APPROVAL**. Do not treat LLD wireframes or prompt text as proof of final layout.

References:

- Figma Make: `https://www.figma.com/make/zoXZLtxEqowtazwEWvClP8/IraniMart-Business-App-Prototype` — **PARTIALLY ACCESSIBLE** shared design brief/screen inventory was documented earlier; current editor page reports failed/403 resources, so editable frames and variables could not be inspected during P2-C.
- Published prototype: `https://merge-help-64706861.figma.site/` — **VERIFIED ACCESS** to the listed M1 surfaces and interactions. Owner, Manager and Salesperson demo variants were inspected; the Cashier variant and alternate/error/loading states were not verified. The displayed figures, product rows, profile identity, role switching, stock counts and notification records are demo content, not application data or approved behavior.

## Observed design surface

| Surface | Evidence observed | Confidence/status |
|---|---|---|
| Login branding | IraniMart Business heading/identity and short dashboard/count/forecast tagline | **VERIFIED** on published login page. |
| Login controls | Email and password inputs, sign-in, forgot-password, and Business Owner / Sales Manager / Salesperson demo-role controls | **VERIFIED** on published prototype. **Intentional deviation:** per Product Owner decision (2026-10-14), Flutter login contains no role selector, demo-role selector, or role-switch control. The ERP establishes the role and permissions; mobile role-aware navigation uses only the verified ERP profile. |
| Visual treatment | Login uses warm ivory/light background/card, dark espresso/brown typography and gold highlights/button. The Make brief defines a broader proposed palette below. | **VERIFIED visually on login / PENDING global token approval** |
| Role dashboards | Owner Home, Sales Manager outlet dashboard and Salesperson personal dashboard were inspected through the prototype's demo role switch. All financial, target, ranking and inventory values are illustrative. | **VISUALLY INSPECTED; FINAL APPROVAL PENDING**. Cashier variant and live role/permission behavior remain unverified. |
| Stock Take flow | Inspected scope selection/create, offline-ready index state, scan/count, manual lookup, simulated scans, undo, pause/resume, online/offline pending changes, blind count review and manual quantity adjustment. Submit was not activated. All products/counts/session values are demo content. | **VISUALLY INSPECTED; FINAL APPROVAL PENDING**. No UX evidence is a production API or offline-integrity contract. |
| Insights and Forecast | Owner/Manager demand and category signals, Salesperson historical insight, period filters, Forecast horizon, actual-vs-predicted chart, rationale and production what-if interactions were inspected. Displayed figures and recommendations are illustrative. | **VISUALLY INSPECTED; FINAL APPROVAL PENDING**. Not data contracts or approved model behavior. |
| Notifications and Profile/Settings | Inspected notification list, unread count, mark-all-read affordance, notification-to-stock-history navigation, and Profile/Settings content including demo role switching and settings toggles. | **VISUALLY INSPECTED; FINAL APPROVAL PENDING**. Do not ship prototype role switching; production role remains ERP-authoritative. |
| Catalogue, Product Details/Variants, other roles | No rendered destination/screen was reached and inspected. | **NOT VERIFIED**; do not infer layout from the brief. |
| Logo/assets/tokens | The mobile checkout now contains `retailpos-mobile/assets/brand/iranimart-logo.png`, `iranimart-icon.png` and SVG counterparts; the PNG logo and monogram are used in Flutter. Asset provenance/usage approval is not recorded. Login palette family and gold primary action are visually evident; editable variables, exact token mapping and complete type/spacing specs are unavailable. | **ASSETS PRESENT / provenance and design approval pending / PROVISIONAL tokens** |

## P2-C screen acceptance evidence

Status below distinguishes a rendered UI preview from a functional feature.
The published prototype is visual reference evidence only; it is not approval
of the current Flutter rendering.

| Screen | Flutter implementation | Visual evidence | Product Owner approval | Acceptance status |
|---|---|---|---|---|
| Sign in | Implemented: email/password, loading and typed error/session states | Prototype login inspected; current Flutter rendering not signed off | Role-selector exclusion approved 2026-10-14; overall visual approval pending | Implemented; design approval pending |
| Business/Outlet context | Implemented in Profile using verified profile and authorized outlet list | Not verified against approved screen | Pending | Implemented; design approval pending |
| Owner dashboard | UI-only role composition: sales summary/trends, outlet performance, product ranking, discount/margin insights and stock alerts; all sections disconnected | Owner Home inspected; sample metric and alert values are illustrative | Pending | UI composition implemented; data disconnected; design approval pending |
| Manager dashboard | UI-only assigned-outlet composition: sales/target, staff performance, trends, Stock Take status and alerts; uses authenticated selected outlet context | Assigned-outlet target, KPIs, staff leaderboard and stock-take summary inspected; sample values are illustrative | Pending | UI composition implemented; data disconnected; design approval pending |
| Salesperson/Cashier dashboard | UI-only self-activity, target, invoice/unit and task sections; Cashier/Salesperson share a provisional layout | Salesperson self-sales, target attainment, invoice/unit metrics and outlet rank inspected; sample values are illustrative | Pending; Cashier-specific variant/policy not verified | UI composition implemented; data disconnected; design approval pending |
| Catalogue | Implemented as a presentation-only responsive search/filter layout with explicit disconnected catalogue state; no products or business values are loaded | No catalogue screen reached; proposed UI is not claimed as Figma-faithful | Pending | UI reviewable; no catalogue API, lookup or synchronization |
| Product Details / Variants | Reusable identity cards display product/variant IDs, Main Product Code, separate product/variant barcode fields and arbitrary attribute/value pairs | No details or variant screen reached; layout proposed from SRS/LLD identity requirements | Pending | Components are data-driven; no catalogue records or details route is connected |
| Stock Take | Implemented as a presentation-only overview, create form, scan/count, sync, review, submission/reconciliation and history screens. No sessions, catalogue or counts are loaded. | Earlier M1 review inspected overview/create, simulated counting, review, history and comparison. Displayed records/counts are illustrative; Figma Make editable frames remain unavailable. | Pending | M8 screen flow implemented; every production route state is disconnected; design approval pending |
| Scan/count | Raw text barcode entry, variant identity contract, disabled quantity/undo/pause/resume controls and unknown/ambiguous/duplicate/retired/out-of-scope exception states | Simulated scan, manual lookup, undo, pause/resume and offline pending state inspected; not evidence for camera or production behavior | Pending | Presentation only; no scanner, catalogue lookup, count mutation or persistence |
| Conflict resolution | Explicit stale, failed, expired-session and conflict UI state definitions; no operation is resolved or merged | Not verified | Pending | State components implemented; no sync/recovery operation is connected |
| Reconciliation/status | Distinct local-pending, server-accepted, under-review, approved, rejected and posted state presentations; comparison fields remain unavailable on production route | History and stock-comparison surfaces inspected, but no approved mobile screen contract or live data verified | Pending | UI definitions only; no submission, approval, variance calculation or inventory posting |
| Stock Take history | Filter/search presentation, unavailable empty-data state and actor/action/time timeline component | History and Stock Comparison inspected; all records and quantities are illustrative | Pending | Filters are local presentation controls; no session history or audit records loaded |
| Insights | Role-aware UI-only Owner, Manager and self-only Cashier/Salesperson presentation with local period selection; all reporting sections disconnected | Owner/Manager demand signals and Salesperson historical facts plus period filters inspected; illustrative | Pending; Cashier policy remains provisional | UI implemented; no reporting integration; visual approval pending |
| Forecast | Owner-only presentation screen with local 1/2/3-month horizon, historical/prediction/uncertainty chart contract, category/product/colour/size demand sections, provenance/explanation and non-executing what-if inputs; no forecast data | Horizons, actual-vs-predicted range/chart, rationale and production what-if interactions inspected; values and recommendations are illustrative | Pending; calendar-month interpretation, tokens and role/data policy need approval | UI implemented; no forecast request, calculation, AI or business action |
| Notifications | Responsive presentation-only Notification Centre with category/type filters, notification history and detail components; inbox, counts and mutations remain disconnected | List, unread count, mark-all-read affordance and stock-history navigation inspected; prototype records are illustrative | Pending | Screen and states implemented; no API, delivery, persistence or real notification records |
| Profile | Implemented: authenticated ERP identity, role and server-authorized outlet selection; missing and out-of-list outlet context are explicit | Prototype profile/settings inspected; prototype role-switch controls intentionally excluded from production | Pending | Implemented; design approval pending |
| Settings | Implemented: typed settings sections for notification preferences, scanner feedback, offline catalogue/sync, pending operations/session exit, version/environment and support | Prototype scanner feedback/offline status, sync history and outlet links/toggles inspected. Prototype toggles are not evidence of persistence or live sync behavior. | Pending | Presentation implemented; notification/scanner preferences and offline status remain disabled/unavailable pending persistence or backend support |

## Requirement-to-screen plan (not a claim of Figma coverage)

| Planned Flutter route/screen | SRS/LLD basis | Figma status | Design/implementation gate |
|---|---|---|---|
| Sign in | AUTH-001..006; LLD auth route | Login layout and basic controls are **VERIFIED**; role selection in the prototype is explicitly rejected by the Product Owner | Keep only email/password, sign-in, loading and necessary error/session UI. Never let the client select or override role. ERP profile remains authoritative; obtain design assets/tokens and approve final form states. |
| Business/outlet context | AUTH-002, AUTH-004; SRS concepts | **PENDING VISUAL APPROVAL** | Define authorized selection, permission states and context-switch warning. |
| Role dashboard (Owner) | DASH-001, DASH-004..006 | A Home dashboard was visually inspected but includes illustrative values. | Flutter shows an explicit no-data preview; no metric cards or sample values copied. Owner role layouts, metrics, grants and states still need approval. |
| Role dashboard (Sales Manager) | DASH-002, DASH-004..006 | Assigned-outlet target/KPIs, staff leaderboard and stock-take summary visually inspected; all prototype values are illustrative. | Obtain approval for assigned-outlet scope, target definitions, leaderboard permissions and no-data/error states before adding reporting. |
| Role dashboard (Salesperson) | DASH-003..006 | Self-sales, target attainment, invoice/unit metrics, outlet rank and historical product insight visually inspected; all values are illustrative. | Obtain approval for self-only data, target/no-target state and reporting layout before adding reporting. Cashier-specific variant remains unverified. |
| Catalogue search/detail | CAT-002..004, CAT-013; PROD-001..012 | **NOT VERIFIED**; proposed controls follow SRS/LLD identity requirements only | Search fields cover Main Product Code, product name, SKU and raw barcode; category/size/colour/dynamic-attribute filters are presentation-only. Preserve product vs variant identity, separate barcode roles, and explicit unavailable/empty/error/unauthorized/stale/unknown/ambiguous states. Obtain visual and behavior approval before data integration. |
| Stock-take session list/create | STK-001..003, STK-009, CAT-006 | A Stock Take overview was visually inspected; displayed counts and sessions are illustrative. | M8 provides overview and create-scope presentation, but no production session or catalogue data and no session creation. Revision, baseline and lifecycle remain unimplemented; obtain design approval before integration. |
| Scan/count screen | STK-004..008, STK-010..012, SYNC-001..006 | Simulated scanner, manual lookup, quantity display, undo/pause, feedback, offline pending and count review were inspected. Prototype demonstration does not verify camera behavior. | M8 presents lookup, count editing and pause/undo controls as unavailable, with no camera, barcode resolution or persistence. Camera focus/duplicate throttle, audible/haptic options, functional count controls and accessible outcome signals remain future validation. |
| Unknown/conflict resolution | STK-011..013, SYNC-008, CAT-004/008/012 | **PENDING VISUAL APPROVAL** | M8 presents unknown, ambiguous and conflict states without claiming resolution; production data and resolution actions remain unavailable. Show auditable authorized recovery actions only after approval. |
| Submit/reconciliation/status | STK-013..020, SYNC-003..008 | **PENDING VISUAL APPROVAL** | M8 distinguishes local pending, server accepted, under review, approved and posted as presentation states; no submission, approval or posting occurs. Confirm final status semantics before integration. |
| Sales Insights | INS-001..002 | **VISUALLY INSPECTED; FINAL APPROVAL PENDING** | Role-aware UI implemented; keep metrics unavailable until ERP-authoritative reporting and permissions are approved. |
| Forecast | FCST-001..008 | **VISUALLY INSPECTED; FINAL APPROVAL PENDING** | Keep one/two/three-month horizon choices as local presentation only; show actuals, estimates and uncertainty distinctly; expose model provenance and data coverage; keep AI rationale optional and grounded; what-if controls must not calculate or execute business actions. Calendar vs rolling month behavior and authorized roles remain open decisions. |
| Notification centre | NOT-001, NOT-006..014, NOT-021 | **PENDING VISUAL APPROVAL** | Shared read/unread, severity, event time, offline/revoked link states. |
| Profile/settings | PRF-001, AUTH-004, SYNC-007 | **PENDING VISUAL APPROVAL** | Sync revision/status, scanner preferences, secure logout and pending-operation decision. |

## Proposed design tokens and asset handoff

Values below are from the shared Figma Make design brief/LLD and are **PROPOSED** until the brand owner approves semantic roles, typography and contrast. P2-C applies them only as a provisional Material 3 theme and avoids gold-on-ivory small text.

| Token | Proposed value |
|---|---|
| Primary Brown | `#75583E` |
| Rich Espresso | `#35291F` |
| Brand Gold | `#C69B45` |
| Champagne Gold | `#E7CF98` |
| Warm Ivory | `#FAF7F2` |
| Soft Cream | `#F4EDE3` |
| White | `#FFFFFF` |
| Charcoal | `#292724` |
| Muted Taupe | `#83776A` |
| Success / Warning / Error | `#258568` / `#B97820` / `#C34E4E` |

1. Confirm provenance, usage approval and required variants for the supplied `retailpos-mobile/assets/brand/` artwork. The Flutter `ic_launcher.png` files remain default launcher artwork, not IraniMart marks.
2. Obtain editable Figma view/export; validate screen mapping and capture typography, spacing, radius, elevation, icons and any other token families.
3. Brand owner approves semantic token roles and contrast/accessibility before UI implementation. Any unverified screens/states remain pending visual approval.
4. Preserve approved logo artwork; do not recreate from an approximation. Record asset source/license and required variants.

## Mapping exit gate

For remaining UI work, obtain visual approval or owner-approved replacement specs for every MVP screen and state. Dashboard loading, empty, unavailable, error and stale states are represented by shared state panels; current dashboards show unavailable/disconnected, and do not load cached metrics. The Product Owner's 2026-10-14 decision is final for login role controls and overrides any contrary prototype element: exclude role selectors/demo roles from the production login. Flutter uses the local PNG logo and monogram while their provenance and usage approval are confirmed; no prototype role switching is included. Validate all visible controls against SRS requirements, specify screen-reader behavior, and base visual assertions only on approved assets/tokens—not unseen Figma content.
