# P2-B2 Secure Authentication Acceptance

## Scope and verdict

P2-B2 implements native Android sign-in, session restoration, token refresh and
server-authorized outlet discovery using the approved first-party ERP contracts.
It does not enable business-data APIs or implement Product, Inventory, POS,
Stock Take, Super Admin administration, role switching or arbitrary Business
switching. No ERP, Angular, database or migration code was changed.

**Verdict: PARTIALLY PASSED.** Flutter analysis, unit/widget/architecture
tests, non-ignored Dart staging-candidate formatting, dependency lockfile
resolution, both Android debug flavor builds, and live emulator
secure-storage read/write/delete passed. The Android app launched and survived
a process restart. ERP-backed login, token restoration with an active account,
profile/outlet retrieval, refresh and logout remain unverified because no
approved isolated ERP endpoint/test account was configured. JDK 17 and deployed
JWT configuration/revocation verification also remain blocked.

## Implemented design

The auth feature follows the existing feature-first boundaries:

- **Domain:** immutable token, profile, outlet and session-context models;
  typed authentication failures; and the session repository contract.
- **Application:** login, restore and logout use cases plus an immutable
  Riverpod session controller.
- **Infrastructure:** exact ERP DTO mapping, Dio data source, encrypted-token
  vault, serialized credential/session-generation manager, single-flight
  refresh coordinator, authenticated request interceptor and Riverpod
  composition.
- **Presentation:** neutral sign-in, session-loading and authenticated-shell
  pages with protected GoRouter navigation.

Staging accepts only a valid HTTPS ERP base URL. Development defaults to
`http://10.0.2.2:5005/` for the Android emulator; Android cleartext is
permitted only for that exact host in the development flavor. No production
URL, client secret, credential or signing key is embedded. Auth responses and
failures are represented by typed domain errors; no token or password is
written to logs.

### ERP endpoints and contracts

Only the approved existing endpoints are called, preserving their existing
request/response JSON shapes:

| Method | Endpoint | Use |
|---|---|---|
| POST | `/api/auth/login` | Email/password login with `rememberMe: false` |
| POST | `/api/auth/refresh-token` | Rotation using the previous access `token` and current `refreshToken` |
| POST | `/api/auth/logout` | Best-effort remote revocation using the refresh token |
| GET | `/api/auth/me` | Re-fetch and validate the authenticated profile and Business identity |
| GET | `/api/me/outlets` | Re-fetch the caller's server-authorized outlet list |

Business identity comes only from the authenticated profile. Outlet selection
is constrained to `/api/me/outlets`; it is kept in memory and is not treated as
authority for a request. No client-controlled Business header is sent. The
mobile client rejects Super Admin and role-switch contexts.

### Token storage and refresh

`flutter_secure_storage` **11.2.0** stores the access token, refresh token and
expiry as one versioned record under one secure-storage key. This avoids
publishing a half-updated token pair after a process interruption. The Android
plugin's generated registration is present, the Android minimum SDK is 24, and
the plugin's Android implementation uses OS-backed encrypted storage. Storage
errors and corrupt records fail closed; there is no plaintext fallback.

The manifest disables Android backup. Both legacy full-backup rules and
Android 12+ cloud-backup/device-transfer rules exclude shared preferences,
databases and app files. These declarations are checked by an architecture
test.

The credential manager serializes secure-storage operations and uses a session
generation to prevent late refresh/login results from overwriting a newer
session. It cancels requests associated with the previous generation. A
separate unauthenticated Dio client performs refresh, preventing recursion.
Concurrent requests share a refresh for the same generation; replacement
credentials are persisted before waiters proceed. Refresh is never triggered
by 403 and the refresh endpoint is never refreshed recursively. Automatic
replay is limited to eligible GET/HEAD requests and occurs at most once. If the
replay also receives 401, local credentials are cleared.

A definitive refresh failure clears the usable session. A network timeout
during refresh is treated as an ambiguous rotation outcome: the old refresh
token is not retried and interactive re-login is required. Logout attempts
server revocation without refreshing; local credentials are cleared even when
the remote call fails, and the UI reports when remote revocation could not be
confirmed.

The app restores credentials on startup but always re-fetches `/api/auth/me`
and `/api/me/outlets`; it does not trust cached membership or permissions.
Outlet choices from a previous session are not persisted. Passwords are never
stored.

## Files added or updated

The project directory remains untracked in this worktree (see Git status
below); this list identifies the P2-B2 implementation surfaces, not a
tracked-file diff against P2-A.

- App/configuration: `retailpos-mobile/lib/app/bootstrap.dart`,
  `retailpos-mobile/lib/app/router/app_router.dart`,
  `retailpos-mobile/lib/core/config/app_environment.dart`.
- Authentication domain: `retailpos-mobile/lib/features/auth/domain/auth_failure.dart`,
  `auth_models.dart`, `session_repository.dart`.
- Authentication application:
  `retailpos-mobile/lib/features/auth/application/auth_session_controller.dart`,
  `session_use_cases.dart`.
- Authentication infrastructure:
  `retailpos-mobile/lib/features/auth/infrastructure/auth_api_remote_data_source.dart`,
  `auth_providers.dart`, `auth_response_mapper.dart`,
  `auth_session_interceptor.dart`, `credential_session_manager.dart`,
  `refresh_coordinator.dart`, `secure_token_vault.dart`,
  `session_repository_impl.dart`.
- Authentication presentation:
  `retailpos-mobile/lib/features/auth/presentation/authenticated_home_page.dart`,
  `session_loading_page.dart`, `sign_in_page.dart`; the former foundation
  home placeholder is replaced by the authenticated shell.
- Android security/configuration:
  `retailpos-mobile/android/app/build.gradle.kts`,
  `retailpos-mobile/android/app/src/main/AndroidManifest.xml`,
  `retailpos-mobile/android/app/src/main/res/xml/backup_rules.xml`,
  `retailpos-mobile/android/app/src/main/res/xml/data_extraction_rules.xml`.
- Dependencies and setup:
  `retailpos-mobile/pubspec.yaml`,
  `retailpos-mobile/pubspec.lock`,
  `retailpos-mobile/README.md`.
- Tests:
  `retailpos-mobile/test/app_test.dart`,
  `retailpos-mobile/test/core/app_environment_test.dart`,
  `retailpos-mobile/test/features/auth/auth_response_mapper_test.dart`,
  `auth_session_interceptor_test.dart`, `auth_test_fakes.dart`,
  `secure_token_vault_test.dart`, `session_repository_test.dart`,
  `retailpos-mobile/test/architecture/android_security_configuration_test.dart`,
  `retailpos-mobile/test/architecture/import_boundaries_test.dart`.
- This acceptance record:
  `Docs/Mobile/Planning/17_P2_B2_SECURE_AUTHENTICATION_ACCEPTANCE.md`.

## Verification results

| Gate | Result | Evidence |
|---|---|---|
| `flutter pub get --enforce-lockfile` | Passed | Lockfile resolved successfully; no lockfile enforcement error |
| `flutter analyze` | Passed | No issues found |
| Dart formatting | Passed | Git staging-candidate enumeration plus `dart format --output=none --set-exit-if-changed <39 Dart paths>`; 39 files checked, 0 changed |
| `flutter test` | Passed | 33 tests passed, 0 failed; includes auth, widget, storage, URL validation, Android backup and architecture-boundary tests |
| Development Android debug APK | Passed | `flutter build apk --debug --flavor development --dart-define=APP_ENV=development` |
| Staging Android debug APK | Passed | `flutter build apk --debug --flavor staging --dart-define=APP_ENV=staging` |
| Plugin registration/build | Passed | Generated Android plugin registrant includes `FlutterSecureStoragePlugin`; both flavor builds compiled successfully |
| Backup/data-extraction configuration | Passed | Manifest configuration and backup exclusions are asserted in automated tests |
| Emulator app launch/restart | Passed | API 37 AVD booted with software graphics; development app installed/launched, force-stopped and relaunched; activity remained resumed |
| Secure-storage runtime | Passed | App startup initialized plugin; temporary smoke test successfully wrote, read and deleted synthetic non-credential data on Android |
| ERP login/profile/outlets/refresh/logout and active-token restoration | Not verified | No approved development/staging ERP URL or test account configuration was available |
| Device context clearing/live network-failure UX | Not verified live | Unit/widget tests cover outlet validation, logout, timeout/offline and auth errors; no active ERP session was available on device |
| JDK 17 compatibility | Not verified | Builds used Android Studio JBR 25; no local JDK 17 was available and remote CI run could not be confirmed |

The repository-wide `dart format --output=none --set-exit-if-changed .`
traversed generated files under
`build/app/intermediates/desugar_graph/.../bucket_0` after Android builds and
failed when a generated bucket path disappeared during directory enumeration.
The formatter is not Git-aware. The replacement check enumerated all 39
non-ignored Dart files returned by
`git ls-files --cached --others --exclude-standard` and reported 0 files
changed. The workflow's clean-checkout formatter has not run against this
worktree.

The Android APK builds emitted a Java native-access warning from Gradle's
native-platform library under JDK 25; it did not fail either build. The local
Flutter tool selected Android Studio JBR 25; the system `java` command resolves
to JRE 8 and another installed Android OpenJDK is 21. No JDK 17 was found.
The workflow specifies Temurin JDK 17, but the local workflow file is
untracked and the GitHub Actions lookup returned 404, so that exact
JDK/Gradle combination was not tested in CI.

No production or shared ERP database was contacted. No ERP endpoint was
called during the on-device smoke test. Endpoint behavior was tested with
deterministic fake HTTP and secure-storage adapters; no live ERP contract test
was run.

### Deployed JWT configuration and revocation

No approved read-only access to deployed configuration was available. The
repository's non-secret `src/RetailPOS.API/appsettings.json` fallback values
are **60 minutes** for access-token expiry and **7 days** for refresh-token
expiry. Environment-specific deployment overrides were inaccessible; these
are not verified deployed lifetimes.

The inspected implementation issues stateless signed access JWTs and rotates
server-stored refresh tokens, including refresh-token reuse detection. Refresh
token revocation does not establish immediate revocation of already-issued
access JWTs. Deployed token lifetimes and any external JWT revocation/cache
behavior remain unknown; keep this security gate open.

## Security limitations and approval gates

- ERP access-JWT expiry and revocation behavior remains a verification gate
  before any mobile business-data API is enabled.
- Local logout does not prove immediate server-side access-JWT revocation.
- A lost device or compromised token requires server-side account/token
  response; the client only clears local credentials when it next runs.
- Android secure-storage write/read/delete was exercised on API 37 using a
  temporary smoke test and synthetic data. The app's normal startup and
  empty-session restoration were exercised across a force-stop/restart; a
  credentialed restoration remains dependent on the unavailable ERP.
- ERP-backed login/profile/outlet/refresh/logout, active-session context
  clearing and live network-error UX remain unverified on device.
- JDK 17 CI compatibility and deployed JWT expiry/revocation remain pending.
- No release signing, production deployment, refresh-token reuse contract
  changes, MFA, password reset, invitation completion, OAuth/PKCE, Super Admin
  access or role switching is included.
- No business-data API should be enabled until JWT lifetime/revocation,
  server-side authorization and the separate Product ownership remediation
  have passed their respective gates.

## Git diff and change preservation

`git status --short --untracked-files=all` shows the mobile project,
`.github/workflows/flutter-mobile.yml` and this acceptance document as
untracked and not ignored. Source, tests, workflow, lockfile and documentation
are staging candidates. `git check-ignore` confirmed generated root and nested
Android build output, `.dart_tool`, Android `.gradle`, `local.properties`,
key-properties/keystores and `.env.development` are ignored. The mobile
`.gitignore` was updated to exclude `/android/build/`, `/android/app/build/`
and `.env.*` while allowing `.env.example`. No files were staged or committed;
other pre-existing modifications were preserved. The temporary runtime smoke
source was removed after validation.

## Acceptance

**P2-B2C validation closeout: PARTIALLY PASSED.** Emulator app launch/restart,
Android secure-storage runtime read/write/delete, source formatting, analysis,
tests, backup configuration and both Android builds passed. ERP-backed
authentication lifecycle/context/network checks, JDK 17 CI, and deployed JWT
lifetime/revocation remain unverified. Keep business-data APIs disabled until
the ERP and JWT gates are verified.

## P2-B2D local integration preparation

The Development API's existing launch settings define HTTPS on
`https://localhost:5006` and HTTP on `http://localhost:5005`. The running API
was bound only to loopback HTTP on port 5005. To avoid restarting the API (its
startup performs migrations, DDL and seeding), a temporary HTTPS-only bridge
was used to forward the approved authentication routes to that existing
process. The bridge was restricted to the approved login, refresh, logout,
profile and outlet endpoints and was stopped after testing.

| Check | Result |
|---|---|
| Host HTTPS certificate validation | Passed. Windows trusted the existing localhost development certificate; an anonymous `GET /api/auth/me` reached the API and returned the expected 401. |
| Emulator-to-host route | `adb reverse` mapped emulator localhost port 5006 to the host bridge. |
| Emulator TLS validation | Blocked. Chrome reported `NET::ERR_CERT_AUTHORITY_INVALID` for `https://localhost:5006/api/auth/me`. The Windows development certificate is not trusted by the emulator. No certificate-validation bypass was used. |
| Flutter development configuration | The existing HTTPS-only `ERP_BASE_URL_DEVELOPMENT` Dart define was used to build the development debug APK with `https://localhost:5006/`; the build passed. The APK was not installed or used to send credentials because emulator TLS trust failed. |
| ERP authentication flow | Not run. No login credentials or credential-bearing request were sent. |
| Database safety | Not established. The running API was connected to local `retailpos_db`, but the PostgreSQL container/volume differs from the repository compose configuration and contains multiple databases; data provenance and exclusive isolation are unknown. API startup also performs database writes. No database was seeded, migrated or modified during this task, and the API was not restarted. |
| Safe test account | Not available in approved configuration or provided for a verified isolated database. |

The temporary proxy, exported development certificate files, emulator port
reverse and staged public certificate file were removed after the check.
No authentication policy, application source, database, Angular code or
Flutter runtime was modified. No files were staged or committed.

### Prerequisites to resume P2-B2D

1. Provide an emulator-trusted HTTPS certificate chain for the local endpoint,
   or approve a narrowly scoped development-only trust configuration. Do not
   bypass certificate validation.
2. Verify that the API's configured database and its data provenance are
   isolated for authentication integration, and provide an explicitly
   approved test account in that database. Do not restart the current API
   until its startup migration/seeding behavior and target database are
   confirmed safe.
3. After those gates pass, run the development flavor with
   `APP_ENV=development` and
   `ERP_BASE_URL_DEVELOPMENT=https://localhost:5006/`, then verify login,
   restart/restoration, profile, authorized outlets, refresh and logout.

**P2-B2D local integration preparation: BLOCKED.** Host-side HTTPS is valid,
and the development flavor builds with the local HTTPS URL, but emulator TLS
trust, isolated database provenance and an approved test account are
prerequisites before any credential-bearing authentication flow.

## Local development API connectivity

The development Android flavor now defaults to
`http://10.0.2.2:5005/`. Cleartext is permitted only for the exact
`10.0.2.2` host through the development flavor's network-security
configuration; its base policy denies cleartext. No staging overlay or
global cleartext permission was added. Staging continues to require HTTPS,
and certificate validation was not disabled.

| Check | Result |
|---|---|
| Focused Flutter tests | Passed: 4 tests across environment URL policy and Android security configuration |
| Focused Flutter analysis and Dart formatting | Passed: no issues; changed Dart files format cleanly |
| Development Android debug build | Passed: `flutter build apk --debug --flavor development --dart-define=APP_ENV=development` |
| Merged development manifest | Passed: contains the network security config; base cleartext policy is false and the exact emulator host is the only cleartext exception |
| Emulator anonymous API request | Passed: Android emulator requested `http://10.0.2.2:5005/api/auth/me` and received HTTP 401, confirming connectivity without credentials |
| Authentication integration | Not run. No dedicated, approved non-production test account was confirmed or provided; no login credentials were sent. |
| Database provenance/isolation | Still requires confirmation before credentialed testing. Prior investigation found the running API connected to local `retailpos_db`, but could not establish the data provenance or exclusive isolation of its PostgreSQL container/volume. |

No backend process, migration, seeder or database operation was started.
No authentication contract or TLS validation behavior was changed. The
development exception is specific to the Android emulator host and only the
development flavor.

**Connectivity result: PASSED. Credentialed P2-B2D authentication: BLOCKED**
pending verification of database isolation and an approved dedicated
development test account.

## P2-B2D final authentication acceptance

The Product Owner has confirmed that the existing `retailpos_db` is the
intended Development database. The earlier uncertainty about whether it was
the intended database is therefore resolved for this acceptance; no database
was changed. The results below were manually verified by the Product Owner on
the Android emulator. They are user-provided evidence and were not
independently reproduced or automated by the validator. No password or token
was requested, printed, or recorded here.

### On-device evidence

| Scenario | Result and evidence |
|---|---|
| Authorized development-account login | Product Owner manually verified successful login with an authorized Development account. |
| Invalid credentials | Product Owner manually verified that invalid credentials were rejected. |
| Profile and authorized Outlet | Product Owner manually verified that the authenticated user profile and authorized Outlet were displayed. |
| Logout and subsequent login | Product Owner manually verified logout followed by a successful subsequent login. |
| Mike Cashier login | Product Owner manually verified that Mike Cashier authenticated successfully. |
| Force-stop/relaunch and session restoration | Product Owner manually force-stopped and relaunched the app; the previous Mike Cashier authenticated session was restored without re-entering credentials. |
| Live token refresh/rotation | Not verified on-device. No expiry was induced, and no shared token lifetime or device time was changed. |
| Business-data API calls | No Product, Inventory, POS, Stock Count, or other business-data call was made by the validator. Source inspection found the mobile HTTP calls are limited to the five approved authentication/profile/outlet endpoints; the foundation repository is local configuration only. No packet capture or complete backend request trace was available, so this is not an independent wire-level audit of the Product Owner's earlier session. |
| Credential/token logging | No credentials or tokens were printed or logged by the validator. Source inspection found no Dio `LogInterceptor`; the app logger records only the app-start event. A complete device/API log capture was not performed. |

### Automated fake-HTTP evidence

Focused validation was run against the Flutter mobile project:

| Gate | Result |
|---|---|
| Auth mapper, interceptor, secure vault, session repository, environment and Android security tests | Passed: 27 tests, 0 failures |
| Flutter static analysis | Passed: no issues |
| Dart formatting | Passed for the modified Dart files |
| Concurrent refresh | Fake-HTTP test passed: two concurrent 401 requests caused one refresh operation and each eligible request replayed once |
| Refresh rejection / ambiguous timeout | Fake-HTTP tests passed: definitive rejection and timeout clear credentials; the old refresh token is not retried after ambiguous timeout |
| Invalid credentials / 401, 403, 429, offline | Fake-HTTP tests cover typed login failures, profile/session restoration failures, 403 without refresh, 429 without refresh, and network/ambiguous timeout handling. These were not induced against the live ERP. |
| Development APK | Not rebuilt for this acceptance; the immediately preceding connectivity task built the development flavor successfully. |

### Remaining P2-B2D work and gates

- Verify live token rotation only through a safe naturally occurring refresh
  opportunity; do not manipulate time or shared token lifetimes. Single-flight
  behavior remains supported by the passing fake-HTTP test.
- A 60-minute access-token and 7-day refresh-token lifetime are only the
  repository fallback configuration. Deployed JWT lifetime/revocation behavior
  remains unverified.
- CI JDK 17 compatibility remains unverified; previous local builds used
  Android Studio JBR 25.

**P2-B2D final authentication acceptance: PARTIALLY PASSED.** The Product
Owner manually verified authorized-account login, invalid-credential
rejection, profile and authorized-Outlet display, logout and subsequent login,
Mike Cashier authentication, and force-stop/relaunch session restoration.
These are explicitly user-provided manual results, not independently
automated. Focused fake-HTTP authentication tests and static analysis pass.
Live refresh-token rotation, CI execution on JDK 17, and deployed JWT lifetime
and revocation verification remain pending.
