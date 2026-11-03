# P2-C UI and Navigation Acceptance

## Scope and verdict

P2-C adds a provisional Flutter design system, sign-in presentation, responsive
authenticated shell, role-aware navigation and UI-only destination previews.
Authentication behavior and the existing authentication endpoints are
unchanged. No business-data APIs, backend code, database schema, migrations or
seed data were added or changed.

**Verdict: PARTIALLY PASSED.** Flutter formatting, static analysis, unit/widget
and architecture tests, and development and staging Android debug builds
passed. Final design approval is still pending: the Figma Make editor's
resources were inaccessible, official brand assets were not located, and
several prototype screens and design tokens have not been approved.

## Implemented screens and components

- Provisional Material 3 theme and reusable palette, typography and component
  styling in `retailpos-mobile/lib/shared/design_system/`.
- Unified the sign-in and authenticated app-bar title as plain product-name
  text rather than a logo-like typographic lockup; official logo artwork was
  not available in the repository to integrate.
- Strengthened the enabled text-field outline using the existing muted taupe
  token. Its contrast against the white field surface exceeds the 3:1
  non-text component threshold; this does not approve the provisional palette.
- Responsive sign-in presentation with scrollable form layout, autofill,
  obscured password, and existing typed authentication errors.
- Authenticated shell with Bottom Navigation on compact layouts and
  NavigationRail on wider layouts, plus a role-filtered More sections menu.
- Role-specific dashboard previews with verified profile/business context and
  explicit no-data messaging. No sample sales, profit, forecast or inventory
  values are presented as real data.
- Destination preview pages for catalogue, stock take, insights, forecast,
  notifications and settings. They disclose that data services are not
  connected and make no business-data requests.
- Profile page preserving server identity and server-authorized outlet choices,
  including loading, empty, error and unauthorized states; existing logout
  remains available.
- Protected GoRouter destinations and unsupported-role handling.

## Design and asset verification

The published Figma prototype was accessible; Home and Stock Take were
visually inspected. The observed shared navigation was Home / Stock Take /
Insights / Profile. The Home and Stock Take examples contained illustrative
business values and counts; none were copied into the app.

The Figma Make editor returned failed/403 resources, so editable frames and
variables could not be inspected. The official logo files were not located.
The only launcher images found are the default Flutter launcher icon, not
IraniMart brand artwork. A Product Owner handoff with the original logo files
and provenance is required; no substitute logo was created.
The implemented warm ivory, brown, espresso and gold palette is provisional;
semantic token mapping, typography, contrast and uninspected layouts remain
pending brand-owner/design approval. This implementation does not claim full
Figma fidelity or reproduce unverified screens. See
[11_FIGMA_TO_FLUTTER_SCREEN_MAPPING.md](./11_FIGMA_TO_FLUTTER_SCREEN_MAPPING.md)
for the observed evidence and remaining design gates.

### Design-system review

- Typography uses the standard Material 3 text theme with no custom font
  because no approved font asset/specification was available. The palette
  remains provisional; gold is not used as small text on ivory.
- Spacing and component shapes follow a repeated 4/8/12/16/20/24/32
  logical-pixel rhythm and 16/24-pixel field/button/card radii, but those
  values are not claimed as approved Figma tokens.
- Buttons, text fields, cards, app bars, compact NavigationBar and wide
  NavigationRail are themed centrally in `AppTheme`. Loading, empty, error,
  unauthorized and preview messages reuse `ContentStatePanel`.
- Layout uses scrollable sign-in content, a 480-pixel form maximum, a
  960-pixel authenticated content maximum, and a 700-pixel navigation
  breakpoint. These are implementation choices pending design approval.
- Labels and semantic state announcements are present in the relevant
  controls/panels. The muted-taupe enabled field outline is approximately
  4.36:1 against white; a complete device/screen-reader and text-scale audit
  remains outstanding.

### Screen-by-screen acceptance matrix

“Implemented” below may mean a UI-only preview; it does not imply the backed
business capability exists. Visual inspection of a prototype is not approval
of the Flutter screen.

| Screen | Implementation state | Visual verification | Product Owner approval | Acceptance status |
|---|---|---|---|---|
| Sign in | Implemented: email/password and auth loading/error/session states | Prototype login inspected; current Flutter rendering not signed off | Role-selector exclusion approved 2026-10-14; overall visual approval pending | Implemented; design approval pending |
| Business/Outlet context | Implemented in Profile from verified ERP context and authorized outlet list | Not verified against approved screen | Pending | Implemented; design approval pending |
| Owner dashboard | UI-only no-data preview | Prototype Home inspected; Owner-specific layout not verified | Pending | Preview only; design approval pending |
| Manager dashboard | UI-only no-data preview | Not verified | Pending | Preview only; design approval pending |
| Salesperson dashboard | UI-only no-data preview | Not verified | Pending | Preview only; design approval pending |
| Catalogue | Disconnected preview; no catalogue functionality | Not verified | Pending | Preview only; functional screen not implemented |
| Stock Take | Disconnected preview; no stock-take functionality | Prototype overview inspected; current screen not signed off | Pending | Preview only; functional screen not implemented |
| Scan/count | Not implemented | Not verified | Pending | Not implemented; approval pending |
| Conflict resolution | Not implemented | Not verified | Pending | Not implemented; approval pending |
| Reconciliation/status | Not implemented | Not verified | Pending | Not implemented; approval pending |
| Insights | Disconnected preview; no reporting functionality | Not verified | Pending | Preview only; functional screen not implemented |
| Forecast | Disconnected preview; no forecasting functionality | Not verified | Pending | Preview only; functional screen not implemented |
| Notifications | Disconnected preview; no inbox functionality | Not verified | Pending | Preview only; functional screen not implemented |
| Profile | Implemented: verified identity and authorized outlet selection | Not verified against approved screen | Pending | Implemented; design approval pending |
| Settings | Disconnected preview; no additional settings functionality | Not verified | Pending | Preview only; functional screen not implemented |
| Shared loading/empty/error/unauthorized states | Implemented through reusable content-state panel; not every backend-driven screen/state exists | Component covered by widget tests; full-screen visual review pending | Pending | UI primitives implemented; screen-level approval pending |

## Role-navigation matrix

Role recognition is derived from the authenticated server profile and controls
navigation visibility only; it is not an authorization mechanism. The current
profile contract does not provide a complete grants model. Server APIs remain
responsible for authorization.

### Product Owner login decision

On 2026-10-14, the Product Owner explicitly decided that the login screen must
not show Owner, Manager or Salesperson selection, demo-role selectors, or
role-switching controls, even where those are visible in the Figma prototype.
The Flutter login presents only email/password authentication and the
necessary loading, error and session UI. After successful authentication,
role-aware navigation derives the role from the verified ERP profile; the
client cannot choose, override or elevate it. Business and Outlet context
remain server-authoritative.

| Server role | Destinations available in the UI | Primary navigation |
|---|---|---|
| Business Owner | Dashboard, Stock Take, Insights, Forecast, Catalogue, Notifications, Profile, Settings | Home, Stock Take, Insights, Profile |
| Manager / Sales Manager / Branch Manager | Dashboard, Stock Take, Insights, Catalogue, Notifications, Profile, Settings | Home, Stock Take, Insights, Profile |
| Cashier / Salesperson / Sales Associate | Dashboard, Stock Take, Notifications, Profile, Settings | Home, Stock Take, Alerts, Profile |
| Unsupported or unrecognized role | Profile and unauthorized dashboard state only | Profile |

No Super Admin workflow, role switching or business-data access was added.

## Validation results

| Gate | Result | Evidence |
|---|---|---|
| Dart formatting | Passed | `dart format --output=none --set-exit-if-changed lib test`; 53 files checked, 0 changed after formatting the two edited Dart files |
| `flutter analyze` | Passed | No issues found |
| Architecture boundary tests | Passed | 2 tests passed |
| `flutter test` | Passed | 50 tests passed, 0 failed; includes UI, navigation, auth-related widget, theme contrast and architecture tests |
| Development Android debug APK | Passed | `flutter build apk --debug --flavor development --dart-define=APP_ENV=development` completed successfully |
| Staging Android debug APK | Passed | `flutter build apk --debug --flavor staging --dart-define=APP_ENV=staging` completed successfully |
| Business-data API exclusion | Passed by implementation scope | Destination screens are UI previews only; no business-data API integration was introduced |
| Login has no role-selection UI | Passed | Signed-out widget test asserts role labels and common selector controls are absent |
| Enabled text field outline contrast | Passed | Theme test checks at least 3:1 contrast against the field surface; measured muted-taupe/white contrast is approximately 4.36:1 |

## Authentication regression status

Automated fake-session widget coverage verifies startup session restoration,
role-specific navigation, protected/unsupported destinations, profile context
and logout returning to sign-in. Compact navigation, wide NavigationRail,
unsupported-role routing and manager denial of the forecast deep link are
covered. The complete Flutter suite passed after the design review changes.

This is automated UI regression evidence, not a new live emulator or ERP
authentication run. P2-B2D remains **PARTIALLY PASSED** for its separately
recorded live refresh, JDK 17 CI execution and deployed JWT lifetime/revocation
verification gates; this P2-C work does not close or change those gates. See
[17_P2_B2_SECURE_AUTHENTICATION_ACCEPTANCE.md](./17_P2_B2_SECURE_AUTHENTICATION_ACCEPTANCE.md).

## Remaining blockers and completion gate

- Obtain an accessible approved Figma/design handoff for uninspected screens,
  responsive variants and UI states.
- Product Owner to provide official logo assets and provenance; the repository
  currently contains only Flutter's default launcher icon.
- Obtain approval for semantic design tokens, typography and accessibility
  contrast, and Flutter screen-by-screen visual review.
- Continue to keep business-data APIs disabled until separately authorized.

## Acceptance status

P2-C implementation and the requested UI/regression validation are complete
within the bounded UI-only scope. Overall acceptance remains
**PARTIALLY PASSED** because final Figma/screen approval, official brand asset
handoff and design-token approval remain unavailable. This status has not
changed. No subsequent backend business-data integration phase is started by
this acceptance.
