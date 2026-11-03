# M1 Figma Design Fidelity and UI System Acceptance

**Date:** 2026-10-14  
**Scope:** Flutter shared design system, sign-in presentation, authenticated shell,
and accessible published-prototype review  
**Verdict:** **PARTIALLY PASSED** — shared UI refinement and automated
validation passed; final Figma/brand approval and visual verification on an
Android device remain blocked.

## 1. Verified design references

- [Figma Make project](https://www.figma.com/make/zoXZLtxEqowtazwEWvClP8/IraniMart-Business-App-Prototype):
  the page was reachable, but its editable frames, variables and design
  resources returned access/resource errors. No unseen frame or token content
  is assumed.
- [Published prototype](https://merge-help-64706861.figma.site/): accessible
  and inspected for Login, Owner/Manager/Salesperson dashboards, role-specific
  Insights, Stock Take creation/count/review, Stock Take History and Stock
  Comparison, Sales Forecast, forecast rationale/production recommendation,
  Notifications, Profile and Settings.
- Prototype UI displayed a warm ivory surface, espresso/brown typography and
  gold primary action. This is evidence for the palette family and action
  treatment only; it is not approval of exact semantic token values or the
  current Flutter rendering.
- Demo sales, stock, barcode, forecast, user, outlet, role and notification
  content was not copied into the app or treated as live ERP data. Prototype
  demo-role selection and profile role switching are intentionally excluded
  from production Flutter; role and permissions continue to come from the
  authenticated ERP profile.
- Prototype interactions inspected include period/horizon controls, the
  notification mark-all-read affordance and stock-history navigation, profile
  settings toggles, stock-history filtering, count scope selection, simulated
  barcode counting, manual product lookup and quantity adjustment, undo,
  pause/resume, and online/offline pending-state changes. Forecast design
  drilldown and the production-recommendation entry point were also inspected.
  Stock submission was not activated. Inspection does not establish backend
  behavior or product-owner approval.

The screen-by-screen evidence and revised mapping are maintained in
[11_FIGMA_TO_FLUTTER_SCREEN_MAPPING.md](./11_FIGMA_TO_FLUTTER_SCREEN_MAPPING.md).

## 2. Screen completion matrix

“Preview” means a Flutter UI-only destination with no business-data
integration. It does not imply a working feature or product-owner visual sign
off.

| Screen | Existing Flutter state after M1 | Published prototype evidence | Remaining gate |
|---|---|---|---|
| Login | Implemented: ERP email/password form, autofill, loading and typed failure/session messages; role selection absent | Login, forgot-password affordance, and demo-role controls inspected | Approve final layout and brand assets. Do not add demo-role selection; no forgot-password API was added. |
| Owner Dashboard | UI-only, explicit no-data preview | Owner Home inspected; all displayed metrics/alerts are illustrative | Approve owner layout, metrics, definitions, filters and states before data integration. |
| Manager Dashboard | UI-only, explicit no-data preview | Assigned-outlet target/KPIs, leaderboard, stock-take summary and quick actions inspected; values are illustrative | Approve outlet scope, targets, leaderboard permissions and states. |
| Salesperson/Cashier Dashboard | UI-only, explicit no-data preview | Salesperson self-sales/target, invoice/unit metrics, outlet rank and weekly signal inspected; values are illustrative | Approve self-only metrics and role mapping. Prototype has no verified Cashier-specific variant. |
| Sales Insights | Disconnected preview; no reporting calls | Owner/Manager demand and category signals and Salesperson historical insights with period controls inspected; illustrative | Approve visual hierarchy, role scope and reporting definitions; keep data disconnected. |
| Product Catalogue | Disconnected preview; no catalogue calls | No rendered catalogue destination inspected | Obtain catalogue search/list design and offline/stale states. |
| Product Details and Variants | Not implemented as a functional screen | Not inspected | Approve Main Product Code, variant, dynamic attribute, barcode, and unavailable states. |
| Notifications | Disconnected preview; no inbox or read-state synchronization | Notification list, unread badge, mark-all-read affordance and stock-history navigation inspected | Approve inbox/read-state design; no notification APIs added. |
| Forecasting | Disconnected preview; no forecasting calls | Horizon selector, prediction range/chart, rationale, design-level colour/size demand and production-recommendation entry point inspected; all content illustrative | Approve historical/prediction/explanation separation and role access before integration. |
| Profile | Implemented using verified identity, role and server-authorized outlet information | Profile and settings inspected, including prototype demo role switching | Approve final visual design. Preserve ERP identity and outlet authorization; omit role switching. |
| Settings | UI-only preview; no settings persistence or feature APIs | Scanner feedback/offline status, sync history and outlet links/toggles inspected | Approve which settings are supported and their persistence semantics. |
| Stock Take and reconciliation | UI-only preview; no scan, count, sync, submit or reconcile behavior | Create/scope selection, offline index state, simulated count, manual lookup/adjustment, undo, pause/resume, offline pending state, review, history and comparison inspected; content illustrative | Approve scan/count/recovery/reconciliation UX. Submit was not activated. Integration remains out of M1 scope and must wait for its backend/API gates. |

Owner, Manager and Salesperson demo-role variants were inspected, but those
prototype controls/data do not approve production authorization or metric
contracts. The Cashier-specific variant, full Catalogue/Details surfaces and
alternate loading/empty/error/offline states are not verified. Screens in the
existing Flutter app remain pending visual approval even when their navigation
or preview is implemented.

## 3. Design tokens and component inventory

### Palette

The existing values remain **provisional**. M1 did not claim editable Figma
variable access or brand-owner approval.

| Token | Value | Current use |
|---|---|---|
| `primaryBrown` | `#75583E` | Primary text actions, focus/border accents |
| `richEspresso` | `#35291F` | Headings and gold-action foreground |
| `brandGold` | `#C69B45` | Primary filled action |
| `champagneGold` | `#E7CF98` | Available accent token; no official asset implied |
| `warmIvory` | `#FAF7F2` | Scaffold and wide-navigation surfaces |
| `softCream` | `#F4EDE3` | Card border and selected navigation indicator |
| `white` | `#FFFFFF` | Cards, fields and compact navigation surface |
| `charcoal` | `#292724` | Body text |
| `mutedTaupe` | `#83776A` | Secondary text and enabled field outline |
| `success` / `warning` / `error` | `#258568` / `#B97820` / `#C34E4E` | Semantic status colors |

### Shared components

- Material 3 system typography remains in use; no font dependency or font asset
  was added. Manrope was observed in the published prototype but is not an
  approved/repository-provided font asset.
- Central theme covers warm scaffold/app bar surfaces; white, low-elevation
  cards with 24dp corners; filled fields with 16dp corners and 16dp content
  padding; and compact NavigationBar/wide NavigationRail.
- Primary FilledButton now uses the visually evidenced gold/espresso treatment,
  with a 52dp minimum height and 16dp corners. Outlined, text and icon buttons
  have explicit 48dp minimum dimensions; outline/text actions use the brown
  accent.
- `ContentStatePanel` remains the shared loading, empty, error, unauthorized
  and disconnected-preview treatment. No chart component or synthetic chart
  data was introduced.
- Sign-in retains its scrollable, autofill-capable form, 480dp content
  constraint, hidden password, existing loading/error states and existing
  authentication calls. It still contains no role selector.
- Authenticated navigation continues to use the server profile, existing
  role-filtered destinations, compact navigation below 700dp and a constrained
  wide-layout content region.

### Assets

The mobile checkout contains `assets/brand/iranimart-logo.png`,
`iranimart-icon.png`, and SVG counterparts. M1 registered the two supplied PNG
assets with Flutter and uses the full logo on sign-in and the monogram in the
authenticated app bar. The full logo is transparent; the monogram background
matches the existing warm-ivory scaffold. No substitute logo was invented.
Asset provenance/usage approval and any additional approved variants still
need confirmation. The repository has no approved font file.

## 4. Implementation changes

- Updated `retailpos-mobile/lib/shared/design_system/app_theme.dart`:
  primary filled buttons now match the observed gold background/espresso text
  treatment, and outlined/text/icon action themes set explicit touch-target
  minimums.
- Registered the supplied `iranimart-logo.png` and `iranimart-icon.png` assets
  in `retailpos-mobile/pubspec.yaml`; rendered the full logo on sign-in and
  the monogram in the authenticated app bar. Authentication calls and role
  derivation are unchanged.
- Extended `retailpos-mobile/test/shared/app_theme_test.dart` to assert field
  outline contrast of at least 3:1, primary action text contrast of at least
  4.5:1, and button minimum dimensions of at least 48 logical pixels.
- Extended `retailpos-mobile/test/app_test.dart` with a small-phone widget test
  using 2x text scale and a simulated 300dp keyboard inset; the form remains
  scrollable without a Flutter layout exception.
- Updated
  [11_FIGMA_TO_FLUTTER_SCREEN_MAPPING.md](./11_FIGMA_TO_FLUTTER_SCREEN_MAPPING.md)
  with the newly verified prototype screens and explicit non-approval/demo
  data caveats.
- Updated `retailpos-mobile/README.md` to describe the present asset use and
  remaining provenance/approval status.
- No dependencies, application authentication behavior, APIs, backend,
  database, migrations, Flutter business-data integrations, or role policy
  changed.

## 5. Accessibility checks

Automated checks passed for:

- Gold primary-action foreground/background contrast meeting the WCAG text
  contrast threshold of **4.5:1**.
- Enabled field-outline contrast meeting the **3:1** non-text component
  threshold against the white input surface.
- Filled, outlined, text and icon button minimum dimensions of at least
  **48 × 48 logical pixels** (filled action height is 52dp).
- Sign-in form layout at a 2x platform text scale on a 390 × 844 logical-pixel
  viewport with a 300dp simulated keyboard inset.
- Existing accessible labels/state messages, password obscuring, autofill and
  role-selector exclusion tests.

Manual TalkBack/keyboard navigation, color-vision review, Android device
text-scale testing and visual confirmation of all controls remain outstanding;
widget assertions are not a substitute for device accessibility sign-off.

## 6. Automated validation

All commands ran from `retailpos-mobile/` and passed:

| Validation | Result |
|---|---|
| `dart format lib/shared/design_system/app_theme.dart test/shared/app_theme_test.dart test/app_test.dart` | Passed; test source formatted. |
| `flutter pub get --enforce-lockfile` | Passed; lockfile honored and no direct dependency changes made. |
| `flutter analyze` | Passed; no issues found. |
| Focused theme, sign-in and authenticated-shell widget tests | Passed; 10 tests in the initial focused run. |
| `flutter test` | Passed; **53 tests**, including unit, widget and architecture tests. |
| `dart format --output=none --set-exit-if-changed lib test` | Passed; 53 files checked, 0 changed. |
| `flutter build apk --debug --flavor development --dart-define=APP_ENV=development` | Passed; development debug APK built. |
| `flutter build apk --debug --flavor staging --dart-define=APP_ENV=staging` | Passed; staging debug APK built. |

Gradle emitted a Java restricted-native-access warning during both builds; it
did not prevent either APK from building.

At validation time, `flutter devices` listed Windows, Chrome and Edge, with no
Android device; `flutter emulators` reported that no available emulator was
detected. Therefore no Android emulator screenshots or on-device visual
comparisons were captured. Existing APKs are build outputs, not visual
acceptance evidence.

## 7. Remaining design and approval blockers

1. Confirm provenance/license, usage approval and required variants for the
   supplied logo and monogram. Do not use the default launcher icon as brand
   artwork.
2. Restore access to editable Figma frames/variables or provide an approved
   export/spec for each planned screen.
3. Approve semantic palette roles, typography, exact spacing/radius/elevation
   tokens and contrast behavior; current shared tokens remain provisional.
4. Approve role-specific Owner, Manager and Salesperson/Cashier layouts and
   Catalogue, Product Details/Variants, Notifications, Forecast, Profile,
   Settings and complete Stock Take/reconciliation screen states.
5. Approve which prototype interactions are real product behavior. Demo role
   switching, values, sample barcode/product records, and demo notifications
   are explicitly not production requirements.
6. Capture screenshots and perform TalkBack/keyboard/device text-scaling checks
   once an Android emulator or physical test device and approved visual
   reference exports are available.

## 8. Recommended M2 screen UI order

Keep M2 focused on completing and reviewing UI across the planned modules;
do not start Stock Take backend integration as a side effect.

1. Resolve the brand/Figma approval blockers and sign off shared tokens.
2. Finalize the Owner Dashboard presentation as the shared dashboard baseline.
3. Apply approved role-specific variations for Manager, then Salesperson and
   the separately approved Cashier variant.
4. Complete Sales Insights presentation and its loading/empty/error/stale
   states.
5. Complete Product Catalogue, then Product Details/Variants and barcode
   presentation.
6. Complete Notifications, including approved read/unread interaction states.
7. Complete Forecasting and clearly distinguish historical metrics,
   predictions and generated explanations.
8. Finalize Profile and Settings.
9. Complete all Stock Take, scan/count, offline/recovery, history and
   reconciliation screens as visual-only UI, then perform the separate API,
   authorization and offline design gates before business-data integration.

## 9. Completion decision

M1's shared theme and accessibility-focused refinements are implemented and
validated without changing authentication or adding business data. The
requested Android flavor builds and all 53 Flutter tests pass. However, M1
cannot be declared fully approved/high-fidelity because editable Figma
specifications, asset provenance/usage approval, complete screen/role-state
evidence, and Android-device visual/accessibility validation are unavailable.
**Final verdict: PARTIALLY PASSED.** Stop here for product/design review; do
not begin Stock Take integration under this acceptance.
