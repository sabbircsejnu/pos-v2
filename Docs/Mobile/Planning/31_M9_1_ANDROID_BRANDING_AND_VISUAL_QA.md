# M9.1 Android Branding and Visual QA Acceptance

**Date:** 2026-10-15
**Scope:** Integrate supplied Android branding assets and perform isolated
Flutter visual QA.
**Verdict:** **PARTIALLY PASSED**

## Branding implementation

Only the Product Owner-supplied assets in `retailpos-mobile/assets/brand/` were
used; artwork was not redrawn or recoloured.

- Android manifest launcher and round-icon references now point to IraniMart
  resources.
- Legacy launcher density resources are provided at mdpi through xxxhdpi.
- Android 8.0+ adaptive and round-adaptive icons use a transparent foreground
  made from the supplied monogram artwork, padded within the adaptive-icon safe
  area, over the configured warm-ivory background.
- Pre-Android-12 launch backgrounds display the supplied logo at density-aware
  sizes. Android 12+ splash themes use the supplied transparent monogram and
  warm-ivory background.
- Shared main resources preserve development and staging flavor compatibility.
- `test/architecture/android_branding_resources_test.dart` checks manifest
  references, adaptive layers, density dimensions, and splash resource
  references.

## Isolated emulator and screenshot evidence

A separate clean `IraniMart_M91_CleanQA` AVD was used; the pre-existing
`Medium_Phone_API_37.0` AVD was not started, reset, or changed. The clean AVD
reported Android 17, 1080×2400 pixels at 420 dpi. Wi-Fi and mobile data were
disabled. The test-only entry point supplied an in-memory synthetic session,
used no ERP base URI, and did not restore authentication or call live services.
Its temporary source file was removed after capture.

Screenshots are synthetic previews, not live business data. The debug build
ribbons are visible in the images. The synthetic-preview banner is visible on
many routes; some nested screens show their own route heading instead. Their
fixtures and filenames are still limited to the isolated QA harness.

| Coverage | Screenshot |
|---|---|
| Signed-out Sign In | [sign-in.png](./31_M9_1_SCREENSHOTS/sign-in.png) |
| Business Owner Dashboard | [owner-dashboard.png](./31_M9_1_SCREENSHOTS/owner-dashboard.png) |
| Sales Manager Dashboard | [manager-dashboard.png](./31_M9_1_SCREENSHOTS/manager-dashboard.png) |
| Salesperson/Cashier Dashboard | [salesperson-dashboard.png](./31_M9_1_SCREENSHOTS/salesperson-dashboard.png) |
| Sales Insights | [owner-insights.png](./31_M9_1_SCREENSHOTS/owner-insights.png) |
| Product Catalogue | [owner-catalogue.png](./31_M9_1_SCREENSHOTS/owner-catalogue.png) |
| Notifications | [owner-notifications.png](./31_M9_1_SCREENSHOTS/owner-notifications.png) |
| Forecasting unavailable state | [owner-forecast.png](./31_M9_1_SCREENSHOTS/owner-forecast.png) |
| Profile | [owner-profile.png](./31_M9_1_SCREENSHOTS/owner-profile.png) |
| Settings | [owner-settings.png](./31_M9_1_SCREENSHOTS/owner-settings.png) |
| Stock Take Overview | [owner-stock-overview.png](./31_M9_1_SCREENSHOTS/owner-stock-overview.png) |
| Stock Take Scan / Count | [owner-stock-count.png](./31_M9_1_SCREENSHOTS/owner-stock-count.png) |
| Stock Take Reconciliation | [owner-stock-reconciliation.png](./31_M9_1_SCREENSHOTS/owner-stock-reconciliation.png) |

The captures show disconnected/unavailable business-data states rather than
invented metrics, catalogue records, notifications, forecasts, or stock
transactions. Manager and Salesperson/Cashier navigation remained role-derived;
the Manager preview did not expose Forecasting.

## Visual and accessibility review

- Reviewed the sign-in logo and adaptive foreground resource against the
  supplied artwork. The icon source preserves its transparent monogram and
  supplies clear padding for adaptive masking.
- Confirmed the app's warm-ivory native splash resource wiring by automated
  resource tests and successful flavor builds.
- The Pixel launcher remained on its home screen after attempted app-drawer
  navigation, so a usable launcher-mask screenshot was not obtained. A cold
  launch screenshot was intercepted by the emulator's Play Store `Updating...`
  system screen; no splash screenshot is claimed.
- Emulator captures cover the default 1080×2400 / 420 dpi configuration only.
  No manual emulator pass was completed at 320dp, with 2× system text, or with
  the keyboard open. Existing widget tests exercise narrow layouts and large
  text, but do not replace those device checks.
- No manual TalkBack traversal or final Product Owner visual/Figma sign-off was
  completed. Pixel-perfect Figma fidelity is not claimed.

## Validation

| Check | Result |
|---|---|
| `dart format lib test` | Passed; 78 files checked, 0 changed |
| `flutter test --reporter expanded` | Passed; 122 tests |
| `flutter analyze` | Passed; no issues found |
| Architecture and Android security tests | Passed; 6 tests, including branding-resource checks |
| Development debug APK | Passed: `flutter build apk --debug --flavor development --dart-define=APP_ENV=development` |
| Staging debug APK | Passed: `flutter build apk --debug --flavor staging --dart-define=APP_ENV=staging` |

## Remaining approval items

1. Manually verify the adaptive icon under launcher masking and inspect the
   native splash on a device/AVD without an interfering system update.
2. Complete device visual checks at 320dp, 2× text scaling, and with the
   keyboard visible; perform TalkBack and touch-target review.
3. Obtain Product Owner approval of final brand placement and remaining
   Figma-to-Flutter visual differences.

**Acceptance verdict: PARTIALLY PASSED.** Supplied branding resources are
implemented, covered by automated checks, and build in both Android flavors.
Safe synthetic screenshots and core role previews were captured. Final launcher
mask/splash observation and manual accessibility/design approvals remain open.
