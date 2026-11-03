# IraniMart Business Mobile — M2 Role Dashboard UI Acceptance

**Date:** 2026-10-14  
**Scope:** UI-only Owner, Manager and shared Cashier/Salesperson dashboards  
**Verdict:** **PARTIALLY PASSED** — role compositions, disconnected states,
automated coverage, Android debug builds and deterministic emulator captures
are complete. Final design approval, Cashier-specific policy/design and
manual Android accessibility review remain open.

## 1. Scope and safety

M2 replaces the single generic dashboard preview with distinct role-based
compositions built from shared Flutter presentation components. It preserves
the existing authenticated ERP profile as the source of role, business and
selected authorized outlet context.

No authentication/session behavior, role derivation, login controls, backend,
API, database, migration, business-data request or Stock Take behavior changed.
All dashboard reporting remains disconnected. No prototype metrics, rankings,
targets, stock values or other business records are shown or estimated.

## 2. Implemented dashboard screens and components

### Business Owner

- Business overview header and authenticated business/outlet context.
- Sales summary sections for sales, invoices, units, average invoice and
  returns.
- Seven- and thirty-day trend presentation.
- Outlet performance and top-performing product/design sections.
- Discount and margin section, explicitly limited to fields granted by ERP.
- Authorized-location stock alerts section.
- Stock Take and Insights quick actions that navigate to existing UI preview
  destinations only.

### Manager / Sales Manager

- Manager dashboard scoped in its presentation to the outlet selected from the
  authenticated, authorized ERP session.
- Assigned-outlet summary and sales-versus-target sections.
- Staff performance/leaderboard section, with a clear authorization caveat.
- Sales, invoice, discount and product trend section.
- Read-only Stock Take status and outlet stock-alert sections.
- Stock Take, Insights and Catalogue preview navigation actions.

### Salesperson / Cashier

- Personal sales/activity section, explicitly subject to the approved role
  policy and reporting grant.
- Target progress section which does not imply a missing target is zero.
- Personal invoice and unit activity section.
- Assigned-outlet context and task/Stock Take preview section.
- Stock Take and Alerts preview navigation actions.
- Cashier and Salesperson currently share one explicitly provisional layout.
  No Cashier-specific metrics or behaviors were inferred.

### Shared presentation

- `DashboardSection` provides consistent icon/title/context plus a state panel.
- `DashboardQuickActions` uses wrapping, accessible buttons and discloses that
  actions open previews only.
- Role composition is kept in `RoleDashboardPage`; role values continue to be
  derived by the existing server-profile mapping.
- The responsive section layout uses one column on compact screens and two
  columns when the available dashboard width is at least 660 logical pixels.
- Unknown roles retain the existing unauthorized dashboard state.

## 3. Role-specific behavior and data boundaries

| Role | Distinct sections | Scope shown | Data behavior |
|---|---|---|---|
| Business Owner | Business sales/trends, outlet/product performance, discounts/margin, stock alerts | Authenticated business context | All reporting unavailable; margin only described as grant-dependent |
| Manager / Sales Manager | Assigned-outlet sales/target, staff performance, outlet trends, Stock Take and alerts | Authenticated selected outlet | No other-outlet, staff or target values loaded |
| Salesperson / Cashier | Personal activity, target progress, invoices/units, outlet/tasks | Authenticated selected outlet | No peer ranking or business-wide financial data loaded |

Role-specific quick actions only open the pre-existing role-protected UI preview
routes. They do not create counts, perform transactions, call reporting APIs
or grant access. Existing GoRouter authorization remains in effect.

## 4. Figma evidence and deviations

The dashboard evidence is the published-prototype review recorded in
[11_FIGMA_TO_FLUTTER_SCREEN_MAPPING.md](./11_FIGMA_TO_FLUTTER_SCREEN_MAPPING.md)
and the M1 acceptance report. The Owner Home, Sales Manager assigned-outlet
dashboard and Salesperson personal dashboard were visually inspected there.
Displayed prototype financial, target, leaderboard, inventory and activity
values are illustrative only. The editable Figma Make frames/variables remain
inaccessible, and prototype inspection is not product-owner design approval.

Intentional M2 deviations:

- No sample numbers, currency, target progress bars, outlet rankings, charts or
  stock-alert counts are presented without a reporting response.
- Each dashboard section uses an explicit “Not connected” state rather than a
  fabricated chart, metric or leaderboard.
- Quick actions are labeled as previews and explicitly state that they do not
  start workflows or load business data.
- The Cashier-specific prototype variant and separate approved Cashier metric
  policy are unavailable. Cashier remains mapped to the existing shared
  Cashier/Salesperson role and provisional layout.
- Date/outlet filters are not interactive in this UI-only slice; server-side
  metric definitions, filter authorization and persistence are not implemented.

The shared M1 palette and typography remain provisional pending the
outstanding brand/design approval recorded in the M1 report.

## 5. Loading, empty, unavailable, error and stale states

The shared `ContentStatePanel` supports:

- **Loading:** progress indicator and state message.
- **Empty:** no-results/inbox presentation.
- **Unavailable:** disconnected service/data presentation; used by dashboard
  sections.
- **Error:** error icon and live-region announcement.
- **Stale:** freshness/update icon and live-region announcement.
- **Unauthorized** and **Preview:** existing access and disconnected-preview
  treatments.

The widget tests exercise each shared state. Dashboard UI currently has no
loading request, cached dashboard snapshot, or cached-data timestamp because
no dashboard data source exists. The stale state is a reusable visual
presentation only; it does not claim that stale metrics are cached or show an
age that the app cannot verify.

## 6. Accessibility and responsive checks

Automated checks cover all three role layouts at 360 × 640 logical pixels with
2× text scaling and assert there are no Flutter layout exceptions. Dashboard
actions use the existing outlined-button theme with a minimum 48 × 48 logical
pixel target and retain visible text labels. State panels preserve text
labels, and loading/error/stale state changes are marked as live regions.

The authenticated shell retains its existing compact NavigationBar, wide
NavigationRail and 960dp content constraint. Dashboard sections adapt to the
available width; quick-action buttons wrap rather than overflow.

An Android 17 / API 37 emulator was available. Deterministic Owner, Manager and
shared frontline dashboard screenshots were captured using preview-only
fixture identity/outlet context; no real records or backend were used. The
temporary screenshot entry point was removed after capture. These screenshots
verify rendering only, not Figma pixel fidelity or TalkBack behavior.

Manual TalkBack traversal, keyboard/focus review, color-vision review, and
Product Owner visual sign-off remain outstanding.

## 7. Tests and builds

All commands ran from `retailpos-mobile/`.

| Validation | Result |
|---|---|
| `dart format --output=none --set-exit-if-changed lib test` | Passed; 55 files checked, 0 changed |
| `flutter analyze` | Passed; no issues |
| `flutter test --reporter expanded` | Passed; **61 tests**, 0 failures |
| `flutter test test/architecture/import_boundaries_test.dart --reporter expanded` | Passed; **2 architecture-boundary tests** |
| `flutter build apk --debug --flavor development --dart-define=APP_ENV=development` | Passed; development debug APK built |
| `flutter build apk --debug --flavor staging --dart-define=APP_ENV=staging` | Passed; staging debug APK built |
| Android 17 / API 37 deterministic role captures | Owner, Manager and shared Cashier/Salesperson previews captured |

Gradle emitted its existing Java restricted-native-access warning during
Android builds; both requested APK builds completed successfully.

## 8. Changed files

- `retailpos-mobile/lib/features/dashboard/presentation/role_dashboard_page.dart`
  — role-specific disconnected compositions and preview-only quick actions.
- `retailpos-mobile/lib/shared/design_system/dashboard_components.dart`
  — shared dashboard section and quick-action components (relocated in M3 for
  cross-feature presentation reuse).
- `retailpos-mobile/lib/shared/design_system/content_state_panel.dart`
  — unavailable and stale presentation states.
- `retailpos-mobile/test/features/dashboard/role_dashboard_page_test.dart`
  — role composition, data-boundary and small-screen/large-text widget tests.
- `Docs/Mobile/Planning/11_FIGMA_TO_FLUTTER_SCREEN_MAPPING.md`
  — dashboard implementation/state status.
- `retailpos-mobile/README.md`
  — dashboard preview and no-business-data scope.
- `Docs/Mobile/Planning/23_M2_ROLE_DASHBOARD_UI_ACCEPTANCE.md`
  — this acceptance record.

No dependency, authentication, routing policy, Angular, .NET, database or
Flutter business-data integration changes were made.

## 9. Remaining approval gaps

1. Obtain final visual approval for Owner, Manager and Salesperson dashboards,
   including responsive layouts and disconnected states.
2. Approve the Cashier-specific role policy and layout, or explicitly approve
   the shared provisional frontline layout.
3. Approve metric definitions, target behavior, currency/timezone/reporting
   windows, permissions and field-level access before any dashboard API work.
4. Complete manual Android TalkBack, focus, text scaling and contrast review.
5. Continue the outstanding brand-asset provenance and semantic design-token
   approvals recorded by M1.

## 10. Completion decision

The role-based UI-only dashboards, safe preview navigation, reusable state
presentation, automated role/responsive coverage, all requested Flutter
validation and emulator captures are complete. The dashboards preserve ERP
role/context and contain no business-data calls. Overall acceptance remains
**PARTIALLY PASSED** because role-specific visual approval, Cashier policy and
manual device accessibility approval are still open.

Stop after M2. Do not begin M3, Stock Take integration, or backend work without
separate approval.
