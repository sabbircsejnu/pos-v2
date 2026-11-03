# M7 Profile & Settings UI Acceptance

**Date:** 2026-10-14  
**Scope:** Presentation refinement of the authenticated Profile and Settings
destinations; no new backend integration  
**Verdict:** **PARTIALLY PASSED** — M7 UI and automated validation are complete.
Final visual/design approval and manual Android accessibility review remain
open.

## Scope and safety boundaries

M7 refines the Profile destination and replaces the generic Settings preview
with a responsive, typed Settings presentation. It preserves the authenticated
ERP session, server-authorized outlet choices, existing role-navigation
behavior, session restoration, and the existing app-bar sign-out flow.

No ERP API, database, migration, authentication protocol, token storage,
notification delivery, catalogue synchronization, or Stock Take behavior was
added. The screen makes no business-data requests. No role switching, demo
identity, fabricated synchronization timestamp, pending-operation count, or
preference-save success is presented.

## Screens and components

### Profile

- Presents the ERP session's identity, role, Business context, and authorized
  outlets.
- Outlet selection choices are built only from `SessionContext.outlets`.
  Selecting an item continues through the existing session controller; the UI
  selection is context, not an authorization grant.
- If the session's selected outlet is not present in that authorized list, the
  screen labels the selected context unavailable and asks the user to select
  an authorized outlet or sign in again. It does not expose the rejected
  outlet name or identifier.
- Missing name, email, Business ID, outlet list, unsupported role, and missing
  ERP session have explicit fallback or unavailable states.
- Sign-out guidance points to the authenticated shell's existing Sign out
  action. It does not claim remote revocation succeeded; the established flow
  reports when revocation cannot be confirmed.

Implemented in `retailpos-mobile/lib/app/presentation/profile_page.dart`.

### Settings

The Settings screen contains typed presentation sections for:

- **Notification preferences:** disabled and marked as requiring ERP support.
- **Scanner preferences:** sound and vibration controls are disabled because
  scanner behavior is not implemented.
- **Offline catalogue and synchronization:** catalogue and last-sync
  information are unavailable; no timestamp or server state is supplied.
- **Pending operations and session:** pending operation count is unavailable.
  A warning explains the future unresolved-work sign-out concern without
  implying offline operations currently exist. Sign-out remains the existing
  app-bar action. A clearly labelled preview dialog illustrates the future
  warning but neither tracks pending work nor performs sign-out.
- **App and support:** support guidance and the configured environment are
  shown. Version `1.0.0+1` matches the current `pubspec.yaml`; it is a
  presentation constant and must be updated if the package version changes.

The notification and scanner switches have no handlers and cannot be changed.
No preferences are currently persisted, so the screen makes no saved-state
claim. The page concludes with an explicit notice that no settings were
changed.

Implemented in:

- `retailpos-mobile/lib/features/settings/domain/settings_presentation_models.dart`
- `retailpos-mobile/lib/features/settings/presentation/settings_page.dart`
- `retailpos-mobile/lib/app/router/app_router.dart`

## Identity, authorization, and role behavior

- Profile role and Business/Outlet context are derived from the existing ERP
  session; no client-provided role or Business/Outlet selector was introduced.
- Only the ERP-authorized outlet list is selectable. A chosen outlet does not
  grant access to business data.
- Existing route guards and `RoleNavigation` remain authoritative. Automated
  coverage verifies a Manager can open Settings and use the existing sign-out
  flow.
- Tokens, secrets, or sensitive authentication material are not displayed.
- No changes were made to session restoration, secure storage, or logout
  mechanics.

## Figma evidence and deviations

The published prototype was previously inspected for Profile and Settings;
the M1 acceptance record notes profile settings toggles and the prototype's
role-switching affordance. The editable Figma Make frames and design resources
were not accessible for frame-level inspection, as documented in
`22_M1_FIGMA_DESIGN_SYSTEM_ACCEPTANCE.md`.

M7 reuses the existing provisional shared Material 3 design system and adds no
assets. Prototype toggles are not treated as evidence of persistence, and its
demo role switching is intentionally excluded. Profile identity and outlet
access use the authenticated ERP session rather than prototype data. The
Settings section layout and disconnected states are reviewable M7 designs,
not claimed pixel-accurate recreations of unavailable editable frames.
Product Owner visual approval and final brand/token approval remain pending.

The screen mapping and mobile README were updated to describe the M7 behavior:

- `Docs/Mobile/Planning/11_FIGMA_TO_FLUTTER_SCREEN_MAPPING.md`
- `retailpos-mobile/README.md`

## Accessibility and responsive review

Automated widget coverage exercises Settings at a compact 320×640 logical
viewport with a 2.0 text scale and checks for layout exceptions. Profile tests
exercise authorized outlet selection and invalid/missing context. Shared state
panel accessibility tests are included in the full Flutter suite.

No emulator/device screenshot or manual TalkBack traversal was performed.
Manual Android screen-reader, focus order, touch-target, contrast, and final
large-text review remain required before visual/accessibility approval.

## Validation results

| Validation | Result |
|---|---|
| `dart format --output=none --set-exit-if-changed lib test` | Passed; no formatting changes required |
| Focused Profile, Settings, and authenticated-shell widget tests | Passed |
| `flutter analyze` | Passed; no issues found |
| Full `flutter test` suite | Passed; 101 tests |
| Architecture import-boundary and Android security configuration tests | Passed; 3 tests |
| Development debug APK: `flutter build apk --debug --flavor development --dart-define=APP_ENV=development` | Passed |
| Staging debug APK: `flutter build apk --debug --flavor staging --dart-define=APP_ENV=staging` | Passed |

The full suite includes Profile identity/outlet behavior, unavailable session
handling, Settings availability and non-persistence, compact large-text
layout, the preview-only future session-exit warning, Manager Settings
navigation, and existing logout behavior. Both APKs
are debug builds; this validation does not certify release signing or
production deployment.

## Remaining approvals

1. Product Owner visual approval of Profile and Settings layout and states.
2. Brand/design-system approval of token semantics and supplied asset
   provenance as tracked in the M1 acceptance record.
3. Manual Android accessibility review, including TalkBack, focus traversal,
   touch targets, contrast, and large-text behavior on a device.
4. Confirm the app-version display should remain synchronized manually or be
   wired to build metadata in a separately approved change.

## Acceptance verdict

**PARTIALLY PASSED.** M7's bounded presentation scope, identity and outlet
authorization guarantees, disconnected-state behavior, automated tests,
analysis, and development/staging debug builds passed. Final visual/design
approval and manual device accessibility review are outstanding. No M8,
business-data integration, or backend work was started.
