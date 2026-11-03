# M5 Notifications UI Acceptance

**Scope:** Flutter presentation-only Notification Centre. No notification
service, delivery, business-data integration, persistence, or backend changes.

## Implemented screens and components

- Replaced the generic Notifications destination with a responsive Notification
  Centre showing signed-in Business and selected authorized Outlet context.
- Added All, Unread, and six SRS category filters, plus a typed notification
  type selector covering the 28 candidate types in the SRS.
- Added reusable notification history/list, notification detail, unread badge,
  severity/category labels, safe destination state, and preference panels.
- Added a role-gated bell affordance in the authenticated shell. It reports the
  unread count as unavailable until an authorized inbox exists.
- Production route starts with no notification records and an explicit
  disconnected state. Loading, empty, unavailable, error, stale, unauthorized,
  authorized, expired and inaccessible states are present in the presentation
  components.

## Categories, type and state behavior

- Category labels follow the SRS v1.4 catalogue: Sales, Inventory, Purchase,
  Accounting, Forecasting and System. Stock Take lifecycle types are grouped
  under Inventory, as specified by the SRS.
- Type filtering is exact to a typed `NotificationType`; stable SRS codes stay
  available in the model while user-facing labels are formatted for display.
- Notification list items show title, summary, category, severity, UTC event
  time and read/unread state. Details show the typed event and its
  reauthorization-gated destination state.
- Mark-read actions on preview components are callback-driven and explicitly
  preview-only. The live route has no inbox records, and Mark all read,
  read-state mutation and optional preferences remain disabled/not connected.
  No control reports a server save or successful delivery.
- Destination identifiers are closed typed route keys, not arbitrary URLs.
  Even the authorized presentation state says that the destination still
  requires current ERP authorization; actual navigation is not connected.

## Figma evidence and deviations

- The previously inspected published prototype exposed a notification list,
  unread count, Mark all read affordance and notification-to-Stock-Take-history
  navigation. Its displayed records and counts are illustrative.
- No new Figma screen or editable design variables were inspected for M5.
  Current visual tokens remain provisional; final visual approval is pending.
- The Flutter Centre deliberately omits prototype notification records and
  count, shows a disconnected inbox, and disables server-dependent actions.
  Detail/history layouts and explicit exception states are SRS-driven and are
  not claimed as pixel-matched Figma designs.

## Role and security boundaries

- Notification access follows existing ERP-derived `RoleNavigation` grants;
  the route test verifies a Salesperson can open the Centre and an unsupported
  role is redirected by the existing router.
- Business/outlet values are display context only and are explicitly not
  treated as authorization grants.
- No notification APIs, push/FCM, delivery, outbox, worker, database, migration,
  or authentication/token changes were made. Test fixtures are synthetic and
  confined to widget tests; no production fixture repository or role switching
  was added.
- Existing owner/manager/frontline role grants and notification eligibility
  remain server-authoritative and are not broadened by this UI.

## Accessibility and responsive checks

- Bell and unread count have screen-reader labels; unread severity is conveyed
  by text as well as visual treatment.
- Category chips and type selection wrap/expand in constrained layouts.
- A 320×640 viewport with 2× text scaling is covered by a widget test without
  a layout exception. Severity/category labels and notification actions remain
  text-labeled controls.
- No emulator screenshot was captured: the emulator was available, but
  displaying a notification record there would require a live authorized inbox
  or adding a production preview-data path. The deterministic synthetic
  presentation fixtures were validated in widget tests instead.
- Product Owner visual/accessibility approval and device-level screen-reader
  review remain outstanding.

## Validation results

| Validation | Result |
|---|---|
| Dart formatting check on changed Flutter files | Passed; 6 files, no formatting changes required |
| Focused M5 widget and route tests | Passed; 10 tests |
| Full Flutter test suite | Passed; 84 tests |
| Clean Architecture import-boundary tests | Passed; 2 tests |
| `flutter analyze` | Passed; no issues found |
| Development Android debug APK | Passed; `app-development-debug.apk` |
| Staging Android debug APK | Passed; `app-staging-debug.apk` |
| Emulator | Available (`emulator-5554`); no M5 screenshot captured for the reason above |

## Outstanding approvals and verdict

- Product Owner approval is required for the final visual layout and provisional
  design tokens, including the category/type-filter treatment and detail page.
- Confirm role-specific notification visibility and whether Cashier shares the
  existing frontline grant.
- Confirm the approved server inbox, per-recipient read-state contract and
  destination authorization contract before enabling any action.
- Device-level accessibility review and a reviewable emulator screenshot remain
  open approval items.

**Verdict: PARTIALLY PASSED.** The bounded presentation-only M5 implementation
and automated validation passed. Final visual/accessibility approval and all
server-backed notification behavior remain intentionally pending.

M5 is complete within scope. No M6, notification integration, or Stock Take
work is started by this acceptance.
