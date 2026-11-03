# IraniMart Business Mobile — M3 Sales Insights UI Acceptance

**Date:** 2026-10-14  
**Scope:** Role-aware Sales Insights presentation only  
**Verdict:** **PARTIALLY PASSED** — role-specific UI, presentation-only period
controls, disconnected data states, Flutter validation and Android emulator
captures are complete. Product visual approval, Cashier policy and manual
device accessibility review remain outstanding.

## 1. Scope and safety

M3 replaces the generic Insights destination preview with authenticated
role-aware presentation for Business Owner, Manager and shared
Cashier/Salesperson roles. The role and selected outlet are derived from the
existing authenticated ERP session. No business reporting repository,
provider, endpoint, request, fake service or insight calculation was added.

All metrics and product/category signals remain unavailable. No prototype
values or records are shown. Period selection changes only local presentation
state; it does not apply a date filter to data or make a request. Login,
session restoration, logout and unrelated navigation behavior are unchanged.
Salesperson/Cashier Insights are reachable from that role's existing “More”
menu; Forecast remains unavailable to that role.

## 2. Screens and role-specific presentation

### Business Owner

- Business-scoped sales trend.
- Authorized outlet performance.
- Category performance and top-performing products.
- Product/variant demand by design, category, colour and size, with movement
  and stock-risk signals kept unavailable until authorized ERP data exists.
- Descriptive discount distribution; the presentation explicitly avoids
  implying that discount dependency proves causality.
- Margin-sensitive section is explicitly grant-dependent. Flutter does not
  load or derive margin values.
- Business context and 7 Days / 30 Days / Custom presentation controls.

### Manager / Sales Manager

- Sales trend and performance scoped to the authenticated selected authorized
  outlet only.
- Product/variant demand signals and descriptive discount insights, scoped to
  the selected authorized outlet.
- No cross-outlet comparisons, owner profitability section, or restricted
  margin information.
- When no selected authorized outlet is present, the screen says so rather
  than broadening scope.

### Salesperson / Cashier

- Own sales history, self-only product insights and personal activity.
- The selected authorized outlet may be named as context; no outlet-level
  metrics are included.
- No peer rankings, business-wide totals, profit/margin insights or other-outlet
  metrics.
- Cashier shares the Salesperson layout provisionally; its separate policy and
  design are not approved.

All role sections use explicit “Not connected” states. No chart, ranking,
distribution, sales total, target, stock value or other metric is fabricated.

## 3. Verified Figma evidence and deviations

The editable [Figma Make project](https://www.figma.com/make/zoXZLtxEqowtazwEWvClP8/IraniMart-Business-App-Prototype)
remains inaccessible for editable frames/variables. The [published prototype](https://merge-help-64706861.figma.site/)
was accessible and the Insights surface was inspected. It visibly presents
“Retail Intelligence” / “Insights”, 7 Days, 30 Days and Custom controls, a
top-category card, Discount Intelligence and Sales Forecasting entries,
product-demand signals and colour popularity. The documented role variants
include Owner/Manager demand and category signals plus Salesperson historical
insights. Prototype numbers and product records are illustrative, not approved
ERP data or metric definitions.

M3 follows the verified warm theme, role-specific scope and period-control
pattern, and reuses the shared M1/M2 cards and state panels. Intentional
deviations:

- Prototype revenue, unit, percentage, product, demand, ranking and colour
  values are omitted.
- No chart line or category/product ranking is drawn without an ERP response.
- Discount information is a disconnected descriptive section; no causal
  conclusion is stated.
- Forecasting is not implemented or linked from this M3 screen; it remains a
  separate, owner-only UI destination for future work.
- Custom is selectable as a presentation choice only; no date picker or report
  query exists.
- Shared palette/type tokens remain provisional; rendered implementation
  fidelity is not Product Owner approval.

The evidence and updated implementation status are recorded in
[11_FIGMA_TO_FLUTTER_SCREEN_MAPPING.md](./11_FIGMA_TO_FLUTTER_SCREEN_MAPPING.md).

## 4. Period controls and content states

The typed `InsightsPeriod` model provides 7 Days, 30 Days and Custom. A
single-selection segmented control defaults to 7 Days; its accessible selected
state updates locally. Supporting copy says reporting data and custom date
selection are disconnected, and the selected-period message states that no
reporting request is made.

The feature reuses shared `ContentStatePanel` states:

| State | M3 behavior |
|---|---|
| Loading | Shared accessible progress panel is available for future authorized reporting; no request is issued now. |
| Empty | Shared no-results treatment is available; not presented as a data response in this disconnected slice. |
| Error | Shared error panel/live-region treatment is available; no service failure is simulated. |
| Unavailable | Used for each role-specific insight section. |
| Stale | Shared stale/freshness treatment is available; no cache or freshness age is claimed. |

## 5. Accessibility and responsive checks

- Automated widget tests cover all three roles at 360 × 640 logical pixels and
  2× text scaling with no Flutter layout exception.
- Period options retain a 48dp minimum height and can scroll horizontally when
  text scaling/viewport width requires it.
- Role scope and disconnected states remain visible text, not icon-only
  indicators. Shared state panels preserve their existing semantic/live-region
  behavior.
- Emulator captures verify the screen rendering, not TalkBack, color-vision
  accessibility or Figma pixel fidelity.
- Manual TalkBack traversal, focus/keyboard review, color-vision review and
  Product Owner visual sign-off remain outstanding.

## 6. Tests and builds

Commands ran from `retailpos-mobile/`.

| Validation | Result |
|---|---|
| `dart format --output=none --set-exit-if-changed lib test` | Passed; 57 Dart files checked, 0 changed. |
| `flutter analyze` | Passed; no issues. |
| `flutter test --reporter expanded` | Passed; **68 tests**, 0 failures. |
| `flutter test test/architecture/import_boundaries_test.dart --reporter expanded` | Passed; **2 architecture-boundary tests**. |
| Development debug APK | Passed: `flutter build apk --debug --flavor development --dart-define=APP_ENV=development`. |
| Staging debug APK | Passed: `flutter build apk --debug --flavor staging --dart-define=APP_ENV=staging`. |
| Android emulator previews | Owner, Manager and shared Salesperson/Cashier pages captured on Android 17 / API 37 with deterministic preview-only context. |

Both Gradle builds completed. Gradle emitted the Java restricted-native-access
warning; it did not prevent either build. Emulator captures used a temporary
UI-only entry point and fixture identity/context, with no ERP records or
backend. The temporary entry point was deleted and the normal development APK
was restored on the emulator.

## 7. Implementation changes

- `retailpos-mobile/lib/features/insights/presentation/sales_insights_page.dart`
  — typed period selection, role-specific sections/context and disconnected
  states.
- `retailpos-mobile/lib/app/router/app_router.dart` — authenticated Insights
  route maps the existing session role and selected authorized outlet.
- `retailpos-mobile/lib/app/navigation/role_navigation.dart` — exposes
  Salesperson/Cashier Insights in “More”; existing bottom tabs are unchanged.
- `retailpos-mobile/lib/core/domain/mobile_role.dart` — shared role model
  relocated out of Dashboard so multiple feature presentations share one
  canonical role without feature-to-feature imports.
- `retailpos-mobile/lib/shared/design_system/dashboard_components.dart` —
  shared M2 section/quick-action components relocated to the shared UI layer
  for reuse by Insights and Dashboard.
- Existing Dashboard, shell, Profile and role tests updated for those shared
  import locations; route navigation coverage confirms Cashier can reach
  self-only Insights from “More”.
- `retailpos-mobile/test/features/insights/sales_insights_page_test.dart` —
  role content, absence of prototype figures, period-only behavior and
  responsive large-text coverage.
- `retailpos-mobile/test/app/role_navigation_test.dart` and
  `retailpos-mobile/test/app/authenticated_shell_test.dart` — role access and
  authenticated navigation coverage.
- `Docs/Mobile/Planning/11_FIGMA_TO_FLUTTER_SCREEN_MAPPING.md` — updated
  implementation status while retaining visual-approval gates.

No dependencies, backend, database, authentication logic or business-data
integration changed. No files were staged or committed.

## 8. Remaining approvals and completion decision

1. Product Owner must approve Owner and Manager hierarchy, scope wording,
   period-control behavior and disconnected screen appearance against the
   published prototype or provide approved editable design specs.
2. Approve Cashier-specific access policy and decide whether the shared
   provisional Cashier/Salesperson presentation is acceptable.
3. Approve ERP metric definitions, field grants, date/window semantics and
   manager/salesperson reporting scope before any reporting integration.
4. Complete manual Android accessibility review and confirm brand asset/token
   provenance and visual approval, as recorded by M1.

M3's UI-only implementation and requested automated/build/emulator validation
are complete, with no reporting endpoint or simulated data. **Final verdict:
PARTIALLY PASSED** pending those product, policy and manual accessibility
approvals.

Stop after M3. Do not begin M4, Stock Take integration or business-data API
work without separate approval.
