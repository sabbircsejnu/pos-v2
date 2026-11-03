# M9 Full-App Visual QA and Figma Fidelity Acceptance

**Date:** 2026-10-15  
**Scope:** Flutter-only visual, responsive-layout, navigation and accessibility
QA for the M1-M8 mobile UI  
**Verdict:** **PARTIALLY PASSED** — verified UI copy and splash-surface fixes,
automated route/layout coverage and Android builds passed. Device screenshots,
manual Android accessibility review and final brand/Figma approval remain open.

## Review method and evidence

- Reviewed the M1 design-system acceptance, M2-M8 UI acceptance records,
  Flutter routes, role navigation, screens, shared theme/components, widget
  tests, Android launcher resources and launch themes.
- Opened the published
  [Figma prototype](https://merge-help-64706861.figma.site/). Its sign-in view
  visibly uses an espresso logo area, warm light form surface and gold primary
  action. The browser screenshot was inspected for reference only; no synthetic
  demo data was copied into Flutter.
- Invoked Figma MCP on the supplied Figma Make project. It returned source
  resource links but no editable node-level specifications or usable app-screen
  screenshots. No pixel-perfect fidelity is claimed. For other screens, this
  review uses the inspected-screen record in
  [11_FIGMA_TO_FLUTTER_SCREEN_MAPPING.md](./11_FIGMA_TO_FLUTTER_SCREEN_MAPPING.md)
  and the earlier M1 prototype review; those records are not a substitute for
  final Product Owner approval.
- No live ERP credentials, API calls, business records or production
  screenshots were used.

## Screen-by-screen QA matrix

| Screen / role | Visual and functional review | Responsive / accessibility evidence | Outcome |
|---|---|---|---|
| Sign In | Existing branded logo, shared warm palette, labelled email/password fields, obscured password, loading/failure messages and no role selector. The published prototype's dark logo header and form-card composition differ from Flutter; its forgot-password/demo-role controls are intentionally not copied. | Sign-in widget test exercises 390dp, 2× text and keyboard insets; semantic logo label and password obscuring are asserted. | Functional UI passes. Final composition, typography and any approved password-recovery design remain for visual approval. |
| Business Owner Dashboard | Session-derived identity and Business context; no fabricated metrics; disconnected reporting sections. Quick-action labels now name the destinations without the stale “preview” suffix. | Responsive route sweep at 320, 360, 390, 430 and 840dp with 2× text; dashboard page tests also cover small-screen/large-text layout. | Pass within presentation-only scope. Metrics, definitions and layout approval remain open. |
| Sales Manager Dashboard | Authorized Outlet context only; no fabricated outlet KPIs or staff ranking; role navigation continues to omit Forecast. | Manager route/role tests and dashboard small-screen/large-text test. | Pass within presentation-only scope. Assigned-outlet policy and visual approval remain open. |
| Salesperson / Cashier Dashboard | Uses the existing self-scoped provisional frontline presentation, with no fabricated sales; navigation remains role-derived. Cashier shares the current layout and is clearly identified as provisional. | Cashier navigation/profile-context test, salesperson dashboard layout test and responsive owner-route sweep. | Pass with Cashier-specific policy/layout approval outstanding. |
| Sales Insights | Role-sensitive copy and controls; reporting periods are presentation-only; no fabricated trend, category, product or margin values. | Role and small-screen/large-text widget tests; owner route included in the 2× route sweep. | Pass within disconnected scope. Reporting definitions, field grants and visual approval remain open. |
| Product Catalogue, Product Details and Variants | Main Product Code, product/variant identity and raw barcode presentation remain distinct; catalogue, filters and detail states show disconnected/unavailable data rather than sample products. No catalogue API or synchronization. | Catalogue widget tests include 320dp with 2× text, identity/barcode behavior and state panels; owner route included in the 2× route sweep. | Pass within presentation-only scope. Catalogue visual specifications and field authorization remain open. |
| Notifications | Centre, category/read filters, unread-unavailable badge, disabled persistence actions and unavailable history; no fabricated alerts or delivery. | Widget tests cover list/detail, filters, action availability, error/unavailable states and a 320dp/2× layout; route tests cover the authorized role/context. | Pass within disconnected scope. Manual TalkBack and final visual approval remain open. |
| Forecasting | Owner-only route; horizon and sections are presentation-only; actuals, predictions, uncertainty, rationale and demand remain unavailable absent an approved forecast. No AI execution or synthetic forecast values. | Forecast tests cover horizon/state distinctions, unavailable data and 320dp/2× text; owner route included in the 2× route sweep, while route guards deny unapproved roles. | Pass within disconnected scope. Forecast-role/horizon and visual approvals remain open. |
| Profile | ERP-authenticated identity and role; only session-authorized outlets are selectable; invalid selected outlet is not disclosed as a grant. No role switching or token display. | Profile/outlet and session tests plus owner route in the 2× route sweep. | Pass; manual device accessibility and final visual approval remain open. |
| Settings | Backend-dependent preferences, sync state and pending operations are disabled or explicitly unavailable; app environment is informational. No false persistence or sync success. | Settings widget tests include 320dp/2× text and disconnected-state assertions; owner route included in the 2× route sweep. | Pass within presentation-only scope. Local preference persistence and visual approval remain open. |
| Stock Take Overview | Active, pending and historical data are unavailable in production UI; lifecycle guide is identified as reference, not live state. | Stock Take tests cover role states, navigation and 320dp/2× text; all sections are included in owner route sweep. | Pass within presentation-only scope. |
| Stock Take Create | Business/authorized Outlet are context only; selected scope is local presentation; Start remains disabled. | Same route and narrow/large-text test coverage. | Pass; no session creation. |
| Stock Take Scan / Count | Barcode text remains lossless; Product/Variant identity, unknown/ambiguous states and disabled lookup/count controls are explicit. No camera or count persistence. | Widget tests cover leading zeros, 64-bit identifiers, exceptions and narrow/large text. | Pass; no scanner or count transaction. |
| Stock Take Offline / Sync | Stale, pending, failed, conflict, accepted and expired states are labelled; no current sync/readiness claim or fake timestamp. | Sync state tests and responsive owner route sweep. | Pass; no queue or sync worker. |
| Stock Take Review | Count lines, correction and blind-count behavior are unavailable without authorized session data; no expected quantity or fabricated count. | Review/exception tests and responsive owner route sweep. | Pass; no count mutation. |
| Stock Take Reconciliation | Local pending, server accepted, review, approval, rejection and posted states remain distinct; comparison fields unavailable and actions disabled. | Reconciliation tests assert status distinctions and transaction controls remain disabled. | Pass; no submission, approval or inventory posting. |
| Stock Take History | History, actors and audit entries are unavailable; the timeline is a component contract, not live history. | History/timeline and responsive owner route tests. | Pass; no fabricated audit records. |

## Defects fixed

1. Navigation destination metadata and dashboard quick actions still called
   implemented screens “preview” destinations. Updated the screen titles and
   action labels while preserving role access and disconnected-data messaging.
2. Android launch backgrounds used pure white or the platform system background,
   unlike the shared Flutter warm-ivory surface. Added the matching
   `iranimart_warm_ivory` Android color and applied it to both launch backgrounds
   and the normal window background.
3. Added regression coverage for completed destination labels and an
   authenticated, synthetic-session layout sweep. The sweep visits Owner routes
   (including each Stock Take section) at 320, 360, 390, 430 and 840 logical
   pixels with 2× text and checks for layout exceptions.

## Navigation, authorization and safety

- Existing route guards and role-specific navigation were preserved. Tests
  cover signed-out redirects, unsupported roles, Manager Forecast denial,
  Cashier navigation, Notification access and authenticated Stock Take routes.
- Route restoration was exercised by opening direct GoRouter paths and Stock
  Take section query parameters in widget tests. Android system Back/gesture
  behavior was not device-verified because no safe Android target was available.
- Profile Outlet selection still uses only the session's authorized Outlet list;
  display context does not grant data access.
- Notification reads, settings writes, catalogue queries, forecast requests,
  Stock Take creation/count/sync/submission and all inventory mutations remain
  disconnected or disabled. No role-switch control or success-shaped
  transaction feedback was added.
- The responsive sweep uses a fake session repository and the test ERP URI
  `https://erp.test/`; it cannot access live ERP data.

## Android visual capture and brand review

- `flutter devices` reported Windows, Chrome and Edge, with no Android device
  attached.
- One AVD (`Medium_Phone_API_37.0`) is installed. Its
  `C:\Users\sabbi\.android\avd\Medium_Phone.avd\userdata-qemu.img` exists, so
  its existing app/session state cannot be established as clean. It was not
  booted solely for screenshots.
- Consequently no Flutter app emulator screenshots were captured and no
  temporary screenshot harness was created. The published prototype login
  screenshot was reviewed in the browser but is not represented as a Flutter
  screenshot.
- The in-app brand logo and monogram assets are present. The Android
  `mipmap-*/ic_launcher.png` resources still display the default Flutter mark;
  the launch splash now has the correct ivory background but still has no
  IraniMart logo. Launcher/adaptive-icon artwork and splash composition need
  brand-owner approval and a safe Android visual review.
- Product Owner approval is still needed for exact typography, spacing, card
  geometry, icon choices and the differences between the inspected web
  prototype and Flutter's responsive layouts. M1-M8 acceptance records continue
  to mark visual approvals as pending.

## Validation results

All commands ran from `retailpos-mobile/`.

| Validation | Result |
|---|---|
| `dart format --output=none --set-exit-if-changed lib test` | Passed; all 77 Dart files formatted |
| `flutter analyze` | Passed; no issues found |
| `flutter test` | Passed; 119 tests |
| `flutter test test/architecture/import_boundaries_test.dart test/architecture/android_security_configuration_test.dart` | Passed; 3 architecture/security tests |
| Development Android debug APK | Passed: `flutter build apk --debug --flavor development --dart-define=APP_ENV=development` |
| Staging Android debug APK | Passed: `flutter build apk --debug --flavor staging --dart-define=APP_ENV=staging` |
| Android device screenshot / manual TalkBack review | Not run; no attached Android device and the existing AVD data image prevented a safe launch determination |

Both Gradle builds emitted restricted-native-access warnings and completed
successfully.

## Final verdict

**PARTIALLY PASSED.** The screen and route review, identified Flutter UI
corrections, responsive 2× text sweep, full Flutter suite, architecture/security
tests and both Android debug builds passed. Final Figma/brand-owner approval,
Android emulator screenshots, device-level visual QA and manual accessibility
review remain outstanding. No new UI module, API integration or Stock Take
transaction behavior was started. Stop after M9.
