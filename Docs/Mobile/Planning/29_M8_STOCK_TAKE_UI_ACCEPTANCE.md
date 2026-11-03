# M8 Stock Take UI Acceptance

**Date:** 2026-10-15  
**Scope:** Presentation-only Stock Take workflow in the authenticated Flutter
application  
**Verdict:** **PARTIALLY PASSED** — all requested UI sections and automated
validation are complete. Final Product Owner visual approval and device-level
accessibility review remain open.

## Scope and safety

M8 replaces the generic Stock Take destination with a navigable presentation
workflow. It keeps the existing authenticated shell, route guard and
role-navigation policy. Business and Outlet values are read from the
authenticated ERP session; an Outlet shown in the UI is context, not an
authorization grant.

No Stock Take, catalogue, inventory, audit or history API was called. No local
count persistence, camera integration, sync queue, session creation,
submission, approval, export, inventory adjustment or accounting event was
added. Production routes contain no synthetic session, barcode resolution,
product, count, reconciliation or audit records. Count-changing and
transactional controls are disabled. Synthetic values used to exercise
presentation components exist only in widget tests.

## Screen inventory

All screens are within the existing `/stock-take` destination. The workflow
section is selected with a presentation-only query parameter; the shell's
existing Stock Take navigation selection remains active.

| Screen | Implemented presentation |
|---|---|
| **Overview** | Active/available, pending/review and historical session panels are explicitly unavailable rather than falsely empty. Typed collection panels also support loading, genuinely empty, stale, error and unauthorized states for future authorized data. A labelled lifecycle-status guide is identified as reference material, not live session state. Shortcuts navigate to Create, Offline/Sync and History. |
| **Create Stock Take** | Authenticated Business and authorized Outlet display context; local scope selector for Full outlet, Category or Selected variants; unavailable location/catalogue/scope states; disabled Start action. No session is created. |
| **Scan and count** | Raw text barcode/product search field; leading-zero preservation disclosure; unavailable lookup and resolved-variant identity state; disabled count quantity, increment/decrement, undo, pause/resume, and camera actions. Unknown, ambiguous, duplicate, retired/reassigned and out-of-scope exception states are represented without resolving a barcode. |
| **Offline and synchronization** | Explicit unavailable readiness state and typed state-definition panels for offline-ready, stale catalogue, pending changes, syncing, failed, conflicted, server-accepted and expired-session cases. A state definition is visibly not a current session status; sync action is disabled. |
| **Count Review** | Unavailable count lines and proposed quantities; Product/Variant identity placeholder; blind-count explanation that expected quantities stay hidden; unavailable correction reason, completeness declaration and exception resolution. |
| **Submission and reconciliation** | Disabled Submit and Export actions; typed local-pending, server-accepted, under-review, approved, rejected and posted state definitions; comparison fields show unavailable in production. No variance is computed or inventory posted. |
| **Session History** | Local presentation-only status filter and search field, unavailable-history state, and a reusable actor/action/UTC timeline component. No session, actor or audit entries are loaded. |

Typed presentation models and reusable status, identity, count, exception,
review-line, session-summary, sync, timeline and reconciliation components are
in:

- `retailpos-mobile/lib/features/stock_take/domain/stock_take_presentation_models.dart`
- `retailpos-mobile/lib/features/stock_take/presentation/stock_take_page.dart`

The route obtains identity and Outlet context from the existing authenticated
session in `retailpos-mobile/lib/app/router/app_router.dart`. It introduces
no new role or grant. The Stock Take navigation label now names the real UI
destination rather than calling it a generic preview.

## Navigation and workflow state mapping

| Section | Navigation value | Production data state | User action availability |
|---|---|---|---|
| Overview | `section=overview` or no section | Session list unavailable; no active/empty assertion | Navigate to other UI sections |
| Create | `section=create` | ERP session and catalogue unavailable | Choose a local display scope; Start disabled |
| Scan and count | `section=count` | Active session and pinned catalogue unavailable | Edit raw search text; lookup and count operations disabled |
| Offline and sync | `section=synchronization` | Readiness, queue and server receipt unknown | Retry/sync disabled |
| Count Review | `section=review` | Count lines and exceptions unavailable | Correction/completeness/resolution disabled |
| Submission and reconciliation | `section=reconciliation` | Submission and ERP comparison unavailable | Submit/export disabled |
| History | `section=history` | History and audit timeline unavailable | Local filter/search presentation only |

`/stock-take` remains the only route. Query navigation does not create an ERP
session. The existing authenticated redirect policy still redirects signed-out
users to sign-in and unsupported roles to Profile. This UI-only acceptance
predates the Product Owner Mobile role clarification below; the clarified
Owner/Sales Manager-only rule supersedes the prior Stock Take availability
statement.

**Product Owner Mobile role clarification (2026-10-15):** Stock Take
navigation and route access must be limited to Business Owner and Sales
Manager (including authorized backend `SalesManager`, `OutletManager` and
`Manager` mappings). Salesperson and Cashier must be denied. Future backend
Stock Take endpoints must independently enforce the same server-derived role
allowlist and restrict Business Owner to authorized Business Outlets and
Sales Manager to assigned/authorized Outlets. Existing ERP stock-count
permissions and approval/posting workflow remain unchanged. This updates the
Flutter handoff contract only; this backend thread does not change Flutter.

## Identity, offline and reconciliation semantics

- `ProductId` and `ProductVariantId` remain distinct integer identity fields.
  `Main Product Code` is displayed separately from the exact sellable variant
  barcode; an exact variant identity is required for count presentation.
- Barcode input is a text field and is not parsed or reformatted as a number.
  Test fixtures verify leading-zero preservation. Production lookup is not
  wired.
- Unknown, ambiguous, duplicate, retired/reassigned and out-of-scope barcodes
  are exception categories only. No fuzzy matching, silent mapping or
  cross-scope fallback is provided.
- `Offline-ready` requires a complete authorized catalogue and verified local
  storage in a future implementation. The current application does not claim
  readiness, last-sync time, pending count, server acknowledgement or
  successful sync.
- Local pending work, server acceptance, review/approval, rejection and
  inventory posting are distinct statuses. Server acceptance is not approval;
  approval is not posting. The posted presentation does not claim that mobile
  performed a posting.
- Blind count remains the default presentation: expected quantity is not
  shown during count entry. Reconciliation values are unavailable unless they
  are supplied as isolated test inputs. No completeness inference treats an
  absent variant as zero.
- The existing sign-out/session-expiry route guard remains authoritative.
  Expired-session recovery is described as a state, not an implemented
  reauthorization or unsynced-data recovery procedure.

## Figma evidence and deviations

The prior M1 design review records that the published prototype was inspected
for Stock Take overview, create/scope, simulated scan/count, manual lookup,
quantity adjustment, undo, pause/resume, offline pending, review, history and
comparison. Those screens contain illustrative records and do not prove
production behavior. The screen mapping continues to mark Stock Take visual
approval as pending.

For this task, Figma MCP `get_design_context` was invoked on the supplied Figma
Make project. It returned a notice about source-file resources but did not
provide a usable screen screenshot or node-level layout. A follow-up screenshot
request was not supported for Figma Make files. The M8 layouts therefore reuse
the provisional M1 Material 3 system and are based on SRS/LLD requirements plus
the existing M1 inspection record; no unverified screen detail is claimed as
pixel-accurate Figma fidelity. No design asset was added.

Updated screen mapping:
`Docs/Mobile/Planning/11_FIGMA_TO_FLUTTER_SCREEN_MAPPING.md`.

## Authorization and security boundaries

- Business ID, Outlet name and role are read from the current ERP session.
- The displayed Outlet is selected only by matching the session's selected
  ID against `SessionContext.outlets`; an out-of-list selection is labelled
  unavailable without showing the unauthorized ID or name.
- Existing `RoleNavigation`, authentication redirects, session restoration,
  token handling and sign-out remain unchanged.
- Unauthorized-role UI is defensive; the route guard continues to be the
  enforcement point for mobile navigation. Server-side authorization remains
  mandatory for any future API.
- No camera permission, network request, local database, persistent count,
  fake service or transaction capability was introduced.

## Validation results

All commands ran in `retailpos-mobile/`.

| Validation | Result |
|---|---|
| `dart format --output=none --set-exit-if-changed lib test` | Passed; 77 Dart files checked, no changes required |
| Focused Stock Take widget, route and role-navigation tests | Passed; 19 tests |
| `flutter analyze` | Passed; no issues found |
| Full `flutter test` | Passed; 116 tests |
| Architecture import-boundary and Android security tests | Passed; 3 tests |
| Development debug APK | Passed: `flutter build apk --debug --flavor development --dart-define=APP_ENV=development` |
| Staging debug APK | Passed: `flutter build apk --debug --flavor staging --dart-define=APP_ENV=staging` |

Flutter reported only Windows, Chrome and Edge as connected devices; no
Android emulator was detected. No emulator screenshot or device-level visual
capture was produced. Gradle emitted restricted-native-access and Android SDK
XML-version compatibility warnings, but both debug APK builds completed.

## Remaining approval items

1. Product Owner approval of the Stock Take screen hierarchy, interaction
   affordances and workflow status language.
2. Design/brand approval of provisional M1 token usage; editable Figma Make
   frames/screenshots remain inaccessible through the available MCP screenshot
   tool.
3. Android device review for TalkBack labels/focus order, target size, contrast
   and large-text behavior; widget coverage is not a substitute for manual
   accessibility sign-off.
4. Before any later functional work, resolve existing SRS/LLD decisions for
   stock baseline and movement reconciliation, single-device ownership,
   scope/completeness, barcode uniqueness/aliases, offline validity, local
   protection/retention and correction policy.

## Acceptance verdict

**PARTIALLY PASSED.** The requested M8 screens, typed workflow states,
presentation-only navigation, disabled transaction boundaries, role/session
preservation, tests, analysis and development/staging builds passed. Product
Owner visual approval and manual Android accessibility review remain open.
Stop after M8: no M9, scanner integration, offline persistence, backend work or
stock adjustments were started.
