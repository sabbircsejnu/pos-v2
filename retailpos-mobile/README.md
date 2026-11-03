# IraniMart Business Mobile

Android-only Flutter client for IraniMart Business.

## Toolchain

- Flutter 3.47.7 stable
- Dart 3.13.5 (bundled with Flutter)
- Android SDK Platform 36 and Build Tools 36.0.0
- Android NDK 28.2.13676358
- JDK 17 for Android builds
- `flutter_riverpod` 3.4.3, `go_router` 18.0.2, `dio` 5.11.1
- `flutter_secure_storage` 11.2.0 (Android minimum SDK 24)

The app commits `pubspec.lock` and pins direct dependency versions. Review
dependency changes and run all checks before upgrading packages.

## Local setup

Install the pinned Flutter release, Android SDK Platform 36 / Build Tools
36.0.0 / NDK 28.2.13676358, Android command-line tools and JDK 17. From this
directory run:

```powershell
flutter pub get --enforce-lockfile
flutter analyze
dart format --output=none --set-exit-if-changed .
flutter test
flutter build apk --debug --flavor development --dart-define=APP_ENV=development
flutter build apk --debug --flavor staging --dart-define=APP_ENV=staging
```

The Android `development` and `staging` flavors have distinct application IDs.
For Android Emulator development, the development flavor defaults to
`http://10.0.2.2:5005/`. Its Android network-security configuration permits
cleartext only to the exact `10.0.2.2` host; all other hosts remain cleartext
disabled. This is for local development only. Staging has no cleartext
exception and accepts only an approved ERP HTTPS base URL. No credentials,
production URL, signing key or other secret belongs in source control:

```powershell
flutter run --flavor development `
  --dart-define=APP_ENV=development
```

The API must already be running in the isolated development environment; the
emulator reaches the host through `10.0.2.2`. For staging, provide
`ERP_BASE_URL_STAGING` out-of-band with the `staging` flavor and
`APP_ENV=staging`. Staging accepts HTTPS only. Only debug Android builds are
configured; release signing and production deployment configuration are not
included.

## Authentication and scope

Authentication uses only the existing ERP endpoints:

- `POST /api/auth/login`
- `POST /api/auth/refresh-token`
- `POST /api/auth/logout`
- `GET /api/auth/me`
- `GET /api/me/outlets`

The token pair is stored as one Android Keystore-backed encrypted record.
Android backup and device-transfer rules exclude app storage, and
`android:allowBackup` is disabled. The app never persists passwords and this
slice does not call business-data APIs.

The UI provides a responsive authenticated shell, role-aware navigation,
role-composed Owner, Manager and shared Cashier/Salesperson dashboard
presentations, role-aware Sales Insights, sign-in, profile/outlet context and
no-data destination previews.
Business context comes from the server profile; outlet selection is limited
to the server-authorized outlet list. The provisional Material 3 theme uses
the documented warm brown/ivory/gold palette and the supplied PNG logo and
monogram in `assets/brand/`; semantic token approval, asset provenance and
final typography remain outstanding. The supplied logo and monogram are used
as provided; no new logo or uninspected screen was recreated. The published
prototype contains illustrative business values which are not used by the
Flutter app.

Catalogue search/filter and product/variant identity presentations are
available for UI review, but catalogue, inventory, POS, local database, stock
take, business reporting and forecasting APIs remain out of scope. The
Notification Centre is a presentation-only inbox with list/detail/history
states, category/type filtering and disabled read/preference actions; it makes
no notification request and contains no synthetic production records.
Stock Take now has presentation-only overview, create, scan/count, offline
sync, review, submission/reconciliation and history sections under the
existing role-guarded route. The authenticated Business and authorized Outlet
are display context only. Session creation, camera scanning, catalogue lookup,
count edits, durable offline operations, synchronization, submission, ERP
review, exports and inventory adjustments are unavailable; transaction
controls are disabled and production sections show no fabricated sessions or
counts. Test-only widget fixtures exercise variant identity, raw leading-zero
barcodes, workflow states and reconciliation distinctions.
Sales Forecasting is also presentation-only: its 1/2/3-month horizon and
what-if inputs are local controls, forecast facts remain unavailable, and no
model, AI explanation, simulation or production action is run.
Catalogue controls issue no business-data requests and display no product
records. Dashboard and Insights sections use disconnected states instead of
illustrative metrics. Insights period controls change only local presentation
state; quick actions open existing UI previews only.

Profile presents the identity, role, business and authorized outlets from the
restored ERP session. Outlet choices are restricted to the session's authorized
list; the selected context is not an authorization grant. Settings show the
build environment and version plus disconnected notification/scanner
preferences, offline catalogue/synchronization and pending-operation states.
No preference is persisted in this release; unavailable controls remain
disabled and display no fabricated sync or pending-operation values. Sign out
continues to use the existing authenticated-shell session flow.

## Architecture and validation

`lib/app/` owns bootstrap and routing. Shared role identity, configuration,
typed errors, safe logging and technical networking primitives live under
`lib/core/`.
Features are organized under
`lib/features/<feature>/{domain,application,infrastructure,presentation}/`.
Domain code has no Flutter, Riverpod, Dio or persistence dependencies.
Automated import-boundary tests enforce layer direction.
