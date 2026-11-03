# M6 Sales Forecasting UI Acceptance

**Verdict: PARTIALLY PASSED**

M6 delivers the owner-authorized, presentation-only forecasting screens and
passes the Flutter checks listed below. Final visual approval, the forecast
horizon boundary decision, and device-level accessibility review remain open.
No forecasting service, AI, business-data API, database, or ERP behavior was
implemented.

## Scope and implementation

The `/forecast` destination now renders the Sales Forecasting presentation
instead of the generic feature preview. It derives role and business/outlet
display context from the authenticated ERP session. The existing route guard
and `RoleNavigation` policy remain unchanged: Business Owner access is retained;
Manager and Salesperson/Cashier access is not granted.

Implemented in:

- `retailpos-mobile/lib/features/forecast/domain/forecast_presentation_models.dart`
  - Typed 1/2/3-month horizon and Category/Product/Colour/Size demand
    dimensions.
  - Forecast coverage and quality states, provenance metadata, and chart-point
    contracts.
- `retailpos-mobile/lib/features/forecast/presentation/sales_forecast_page.dart`
  - Forecast Overview, Explanation, and Recommendations sections.
  - Local-only horizon/dimension controls, authorized-context display, explicit
    unavailable states, metadata presentation, and a chart presentation that
    distinguishes observed actuals, predictions, and uncertainty bands.
  - Blank what-if assumption fields and disabled simulation affordance; inputs
    are not validated, calculated, saved, or submitted.
- `retailpos-mobile/lib/app/router/app_router.dart`
  - The forecast route builds the new screen with the authenticated role,
    business ID, and selected outlet display context.
- `retailpos-mobile/test/features/forecast/sales_forecast_page_test.dart`
  and `retailpos-mobile/test/app/forecast_route_test.dart`
  - Presentation, local interaction, role restriction, chart semantics,
    metadata, non-execution, and compact/large-text coverage.
- `retailpos-mobile/README.md` and
  `Docs/Mobile/Planning/11_FIGMA_TO_FLUTTER_SCREEN_MAPPING.md`
  - Forecast UI-only behavior and evidence status updated.

## Horizon, data and interaction behavior

- The UI offers **1 month**, **2 months**, and **3 months**, reflecting the
  SRS forecasting language and the horizon controls previously observed in the
  published prototype. Selection changes only local presentation state; it
  sends no request and performs no calculation.
- Whether these labels mean calendar months or rolling-day periods, and the
  associated boundaries and filter behavior, still require Product Owner
  approval. No 7/14/30-day controls were added.
- The production screen has no historical values, predictions, uncertainty
  values, product rankings, demand results, or recommendation quantities.
  Overview charts and Category, Product, Colour, and Size demand are explicitly
  unavailable until an authorized forecast response exists.
- Chart points and forecast metadata are exercised only by deterministic
  widget-test inputs. They are not wired to a production/demo data source.
- Provenance fields include generation time, model version, training cutoff,
  history coverage, quality, coverage, and fallback indication. No provenance
  is implied when values are unavailable.
- Explanation separates forecast facts from an optional AI narrative. No AI
  explanation is generated, and no causal claim is inferred.
- Recommendation what-if fields are local text inputs only. Simulation is
  disabled; purchase orders, production requests, and stock adjustments cannot
  be initiated.
- The Business/Outlet card is display context only and does not grant access or
  alter server-side authorization.

## Figma evidence and deviations

The published prototype had previously been visually inspected for the
forecast horizon, Actual vs Predicted chart, rationale, and production
what-if interaction pattern. Its figures and recommendations are illustrative
and are not used as application data. The earlier screen mapping records
Forecast as visually inspected but still pending Product Owner approval.

During this M6 work, the published page exposed Insights; navigation to the
Forecast screen timed out, and the Figma Make editor did not expose inspectable
frames. Accordingly, this implementation follows the recorded prototype
evidence and SRS/LLD contracts, but does **not** claim fresh frame-level
verification or final Figma fidelity. Empty/disconnected states, the section
layout, and unavailable controls are implementation choices pending review.

## Accessibility and responsive checks

- Forecast section and horizon controls use minimum segment heights of 48 dp;
  what-if fields use standard labeled Material inputs.
- The chart exposes a semantic summary with counts for observed points,
  prediction points, and uncertainty ranges; its canvas is excluded from
  duplicate semantics.
- Widget tests exercised a 320 x 640 logical-pixel viewport with 2x text
  scaling and asserted that the page builds without a Flutter exception.
- Widget tests verify selectable controls and their visible local state.
- Manual contrast measurement, TalkBack traversal, keyboard behavior, and
  physical-device touch-target review have not been completed and remain
  accessibility approval items.

An Android emulator was available, but no screenshot was captured: launching
the normal app could restore an existing authenticated session and expose
business records, while the project has no isolated emulator-only synthetic
forecast route. Test-only synthetic chart inputs remain confined to widget
tests.

## Validation results

| Check | Result |
|---|---|
| `dart format --output=none --set-exit-if-changed lib test` | Passed; 69 files unchanged |
| `flutter analyze` | Passed; no issues found |
| `flutter test --reporter expanded` | Passed; 94 tests |
| Forecast page and route tests | Passed; 10 focused widget/route tests |
| Architecture import-boundary and Android security tests | Passed; 3 tests |
| Development Android debug APK | Passed; `build/app/outputs/flutter-apk/app-development-debug.apk` |
| Staging Android debug APK | Passed; `build/app/outputs/flutter-apk/app-staging-debug.apk` |
| Emulator screenshot | Not captured; see accessibility and screenshot note above |

The all-directory formatter invocation (`dart format --output=none
--set-exit-if-changed .`) was not usable after Android builds because Dart
traversed generated Gradle intermediates with a stale/missing generated path.
The source and test trees were independently checked successfully.

## Open approvals and release gates

1. Approve the forecast screen layout and visual treatment against the current
   Figma frames; confirm the status and content of the unavailable/error/
   insufficient-data states.
2. Decide whether 1/2/3-month horizons are calendar-month or rolling-period
   windows and approve period boundaries and filters.
3. Confirm the role policy for forecasting before any real forecast API is
   designed. The current app remains Business Owner-only.
4. Review accessible contrast, TalkBack/keyboard traversal, and touch targets
   on representative Android devices and text scales.
5. Define and authorize a safe screenshot workflow using isolated,
   deterministic, non-business test state before capturing emulator imagery.
6. Keep all forecast, explanation, recommendation, and demand data disconnected
   until server-side authorization, field-level access, provenance, quality,
   uncertainty, and data-coverage contracts are approved.

M6 is complete within its UI-only boundary. Do not proceed to M7, forecasting
backend work, or Stock Take integration without separate direction.
