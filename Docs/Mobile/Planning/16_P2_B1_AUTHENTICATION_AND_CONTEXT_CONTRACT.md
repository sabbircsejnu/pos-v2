# P2-B1 Authentication and Business/Outlet Context Contract

**Status:** Planning recommendation for explicit approval; no authentication or ERP connectivity has been implemented in Flutter by this document.

**Scope:** Read-only review of the existing .NET authentication/context implementation and Angular consumers, plus a proposed native-client contract. This is not approval to access Product, Inventory, POS, or other business-data APIs.

## 1. Executive recommendation

1. **No supported OAuth/OIDC identity provider or authorization-code/PKCE flow was found in the inspected solution.** For the first native sign-in slice, reuse the existing first-party email/password endpoint and the server's rotating refresh-token implementation. This is a public native client: embed no client secret, and do not describe the existing credential flow as OAuth or PKCE.
2. Reuse the existing request/response shapes for `POST /api/auth/login`, `POST /api/auth/refresh-token`, `POST /api/auth/logout`, `POST /api/auth/logout-all`, `GET /api/auth/me`, and `GET /api/me/outlets`. Do not send a caller-chosen BusinessId or OutletId as proof of authorization.
3. Store **both** access and refresh tokens using Android OS-backed secure storage. The current refresh endpoint requires both tokens, and an expired access JWT must be retained across process restarts so the client can refresh. Keep parsed profile/context and any future local business data separately, scoped to the signed-in user and authorized Business/Outlet.
4. Serialize refresh attempts in the client. The server atomically permits one rotation and rejects reuse; it does not provide a replay grace response. On an ambiguous refresh timeout, do not retry the old refresh token automatically; clear the local session and require sign-in unless a separately approved recovery contract is added.
5. Treat logout and account disable as **refresh-session revocation, not immediate access-token revocation**. Existing access JWTs remain valid until expiry in the current implementation. Confirm production token lifetimes and obtain explicit security approval for this residual window, or add server-side access-token revocation/shorter expiry before mobile business-data APIs are enabled.
6. Derive regular-user Business identity from the authenticated profile/token and authorized locations from `GET /api/me/outlets`. The selected Outlet is a client-selected filter only; every API accepting an Outlet/Location must independently authorize it.
7. Do not enable mobile Super Admin/global administration or mobile role switching in the initial native release. If either is needed later, require an explicit permission-controlled and audited contract. Existing Product APIs remain outside this approval because Product Business ownership/isolation is a separate remediation track.
8. Keep shared authentication routes unversioned for compatibility in the initial slice; require new mobile business-data endpoints to use an explicitly versioned contract (recommended `/api/mobile/v1/...`) and server-side resource, permission, Business, and Outlet enforcement before use.

### Readiness

| Contract area | Current evidence | Recommendation | Approval state |
|---|---|---|---|
| Native credential login | Existing first-party email/password endpoint; no IdP flow found | Reuse only after accepting public-client/password-flow risks; no client secret | Requires security approval |
| Rotating refresh | Hashed token persistence, relational transaction, reuse rejection, concurrent-rotation test | Reuse; implement client single-flight and never blindly retry a rotated token | Technically supported; client behavior needs approval |
| Android token storage | P2-A has no auth or secure-storage implementation | Android Keystore-backed encrypted storage for both tokens, with backup exclusion | Requires platform/security approval and dependency resolution |
| Business identity | Auth profile contains BusinessId; normal users have no Business-selection endpoint | Use server profile; fail closed if required Business scope is absent | Supported for context discovery, not evidence that all business APIs are isolated |
| Outlet identity | Existing authorized-location endpoint delegates to server access service | Present only returned eligible locations; reauthorize every scoped API request | Supported for discovery; API-by-API enforcement still required |
| Super Admin | Explicit server validation exists for selected Business context | Exclude from mobile business operations initially; require separate approval for any later mobile admin workflow | Requires business/security approval |
| Immediate revocation | Logout revokes refresh token(s); observed tests prove access JWT remains valid | Accept bounded access-token window only after production TTL confirmation, or change server behavior | Blocking security decision before business-data access |
| Versioning | Inspected routes are unversioned; no API-versioning setup found in searched API project | Preserve shared auth routes; version new mobile business APIs | Proposed ADR-API-01 resolution |

## 2. Evidence classification and boundaries

- **VERIFIED** means supported by inspected implementation or an existing automated test. Unless specifically noted, test coverage was inspected but not executed in this planning task.
- **PROPOSED** means a client/API policy recommended for approval; it is not implemented.
- **UNKNOWN** means the reviewed sources do not establish the behavior (for example, deployment-specific overrides).
- **BLOCKED** means the mobile feature must not rely on the behavior until the listed remediation or approval is complete.
- No production or shared database was accessed. No application code, tests, configuration, database, Flutter runtime, or migration was changed. No test suite was run for this planning deliverable.
- Existing tests cover relational refresh-token behavior with SQLite and hosted API characterization with a test factory. This is not PostgreSQL verification or production validation.

## 3. Verified existing authentication behavior

### 3.1 Login and credentials

**VERIFIED —** `POST /api/auth/login` accepts an email, password, and optional/default-false `rememberMe`. Email and password have API validation. The handler checks that the account exists, is active, and has a valid password. A user who must reset/set up a password is rejected with a distinct invitation-completion message. Unknown email, inactive account, and wrong password share `"Invalid email or password"`, while the password-setup condition is distinguishable.

The endpoint returns `401 { "message": "..." }` for credential errors, `429` when rate-limited, and a generic `500 { "message": ... }` for unexpected login failures. Successful login is audited; failure auditing is attempted. Login is limited to five attempts per IP per minute, with no queue.

**VERIFIED — RememberMe currently has no lifetime effect.** `RememberMe` is passed into the application command, but the credentials issuer always uses configured token lifetimes. The checked-in default and appsettings value are 60 minutes for access and 7 days for refresh; production overrides were not inspected and are **UNKNOWN**.

**PROPOSED —** Mobile sends `rememberMe: false` for compatibility, but the server must not treat this field as a native security control. Mobile should follow the fixed server refresh expiry until a separately approved session-duration feature exists. Do not persist the user's password.

### 3.2 Access JWT

**VERIFIED —** The API validates JWT signature, issuer, audience, and lifetime with zero clock skew. Token generation uses HS256 and includes user identity, role, effective `permission` claims, and Business/Outlet claims when present. The checked-in audience is the shared `RetailPOSClient`; the JWT configuration reviewed does not identify Android versus Angular client types.

The token lifetime is configured in minutes, defaulting to 60. An access token is stateless in the reviewed bearer-authentication setup: the request authentication pipeline validates the JWT but does not perform an observed database session/revocation lookup. The tests explicitly assert that an access JWT remains valid after logout. Account/permission changes therefore do not rewrite already issued access tokens.

**VERIFIED —** Refresh recomputes permission claims from the current persisted profile. This means changed permissions are reflected after a successful refresh, not immediately in an already-issued access token.

### 3.3 Refresh rotation and concurrency

**VERIFIED —** `POST /api/auth/refresh-token` accepts both:

```json
{
  "token": "<previous access JWT, possibly expired>",
  "refreshToken": "<current refresh token>"
}
```

The handler validates the JWT's signature, issuer, audience, and algorithm while deliberately not enforcing its expiry; it extracts the user ID, loads the current active profile, recomputes permissions, and attempts to rotate the refresh token for that user. The server cannot refresh from the refresh token alone under the current contract.

Refresh tokens contain 64 random bytes encoded as Base64. The database stores a SHA-256 hash, not the presented raw token. Relational rotation uses a transaction and a conditional update that claims only an unrevoked, unexpired current token, then inserts the replacement. Reuse of an already-revoked token is rejected and a reuse attempt is audited. The losing side of a simultaneous rotation receives an unauthorized response; no grace/replay response was found.

**Security/availability consequence —** If a refresh response is lost after the server commits rotation, the client's old refresh token is already unusable and the replacement may be unknown to that client. Automatic retry of that old token can cause a forced sign-in and creates a reuse signal. Server-side reuse handling observed in `RefreshTokenStore` detects the old token but does not revoke the replacement token family. An idempotent recovery/grace contract is not currently evidenced.

**PROPOSED —** Use a single-flight refresh operation across all in-flight requests. Queue failed requests behind one refresh future, replay each original request at most once after success, never refresh on `403`, and never let refresh itself trigger an interceptor loop. On a `401` from refresh, clear all local credentials/context. On transport timeout after refresh was sent, do not automatically retry with the old token; fail closed and require interactive sign-in unless an approved server recovery mechanism is added.

### 3.4 Logout, account state, and device loss

**VERIFIED —** `POST /api/auth/logout` requires a valid bearer access token and accepts `{ "refreshToken": "..." }`. It revokes only an active refresh token belonging to the authenticated user and returns a success message. `POST /api/auth/logout-all` also requires an access token and revokes all active refresh tokens for that user. Both actions are audited. Neither invalidates already-issued access JWTs; existing tests characterize that behavior.

**VERIFIED —** Refresh rejects an inactive/missing user before rotating. `GET /api/auth/me` uses the persisted profile query; the existing characterization test shows `/me` can still return a profile for an inactive user while that user's refresh is rejected. The refresh handler itself does not revoke tokens when it sees an inactive profile. Whether every user-disable administrative path separately revokes all refresh tokens is **UNKNOWN** and requires inspection/approval before a revocation guarantee is promised.

**PROPOSED —** On normal logout, attempt server revocation, then clear local credentials and context regardless of network outcome. Clearly distinguish “signed out on this device” from “all sessions revoked.” For a lost device, the user should sign in on a trusted device and invoke logout-all if available; this invalidates refresh sessions but does not immediately invalidate a stolen access JWT. The current API has no verified per-device session listing/revoke endpoint, device identifier registration, push-based wipe, or token binding.

### 3.5 Role switching

**VERIFIED —** `POST /api/auth/switch-role` and `POST /api/auth/return-owner` require authentication. The role-switch application flow permits a fixed set of acting roles and verifies that a selected outlet belongs to the persisted BusinessOwner's Business. The server audits role-switch success/failure.

**VERIFIED —** A refresh issued for a role-switched session is generated from the user's persisted real role, not the acting-role context. Existing tests characterize refresh as returning the owner/real role. Role switching also issues additional credentials; it is not the same thing as selecting an Outlet or a Super Admin Business context.

**PROPOSED —** Exclude these endpoints from P2-B2 and from the initial mobile navigation. If a later mobile requirement needs role switching, first resolve role persistence across refresh, define its permissions and outlet scope, and test that switching cannot amplify authorization.

## 4. Business and Outlet context contract

### 4.1 Regular Business users

**VERIFIED —** `/api/auth/me` returns the persisted profile fields (`id`, nullable `businessId`, role, permissions, optional profile outlet, and role-switch metadata). For a regular account, no request-supplied BusinessId should override this identity. The inspected `TenantAccessService` and `UserOutletAccessService` provide reusable server-side Business and location access enforcement, but their presence does not prove that every existing API endpoint invokes them.

**VERIFIED —** `GET /api/me/outlets` requires authentication and returns the centralized outlet-access service's authorized set in an `ApiResponse<AuthorizedOutletsDto>`:

```json
{
  "success": true,
  "message": "...",
  "data": {
    "outlets": [{ "id": 1, "name": "...", "type": "outlet" }],
    "warehouses": [{ "id": 2, "name": "...", "type": "warehouse" }],
    "destinationOutlets": [],
    "destinationWarehouses": [],
    "defaultOutletId": 1,
    "isBusinessOwner": false,
    "isGlobalAccess": false,
    "accessScope": "assigned_only",
    "defaultLocationId": 1,
    "defaultLocationType": "outlet"
  }
}
```

The `data` fields shown are the DTO's defined members; values and access-scope metadata depend on the server-derived user's permissions. `AuthorizedLocationDto` identifies only `id`, `name`, and `type`.

**PROPOSED — Client context DTO (composed locally, no new server DTO):**

```text
MobileSessionContext
  userId: int64
  businessId: int64?              // from /api/auth/me; required before Business data access
  roleName: string?
  permissions: string[]
  selectedOutletId: int64?        // local selection from authorized `outlets`
  authorizedOutlets: AuthorizedLocation[]
  contextRevision: local generation counter
```

This is a client-side composition of `/api/auth/me` and `/api/me/outlets`, not an authorization credential. Do not invent a Business display name when `/me` does not return one. Do not persist a selected Outlet outside the authenticated user/Business partition. Clear the context on logout, account change, invalid credentials, or Business change; cancel/ignore responses from the old context. Future local catalogue data must be partitioned by server-confirmed Business and any outlet-specific data by authorized Outlet.

Every future scoped API must check identity, permission, Business ownership and/or Outlet access on the server. A hidden screen, route guard, cached list, JWT claim alone, or client-supplied OutletId is never sufficient. A client must fail closed if the profile lacks the Business scope required for a business operation.

### 4.2 Super Admin Business context

**VERIFIED —** `POST /api/auth/business-context` is restricted to the Super Admin role and accepts `{ "businessId": <positive id> }` or `{ "businessId": null }`. The handler rechecks that the persisted account is active and Super Admin and, for a non-null selection, that the Business exists and is active. It returns `{ "businessId": ..., "businessName": ... }`; `null` clears selection.

For subsequent requests, `BusinessContextMiddleware` accepts one positive `X-Business-Id`, validates it against the current persisted Super Admin and active Business, and places the validated value in request scope. Missing header means no selected Business; invalid shape returns `400`, unauthenticated use returns `401`, and unauthorized selection returns `403`. This selection is request-scoped, not a general Business-owner switching endpoint or durable server session.

**VERIFIED —** The Angular interceptor strips caller-supplied `X-Business-Id` and only adds a selection that the Business-context service has first validated and persisted for the same Super Admin. It revalidates a stored context before releasing Business-scoped calls. This is useful client behavior, but the API middleware remains the security boundary.

**PROPOSED —** Initial native release does not expose Super Admin global administration or Business selection. If product/business owners approve mobile Super Admin use later, use the existing validation endpoint and `X-Business-Id` only for specifically approved endpoints; every endpoint must independently validate the request context and audit the administrative action. Do not treat a local “selected Business” value as permission, and clear it on identity changes. Generic Product APIs are explicitly excluded until the Product Business-isolation remediation is implemented and verified.

### 4.3 Outlet selection and access enforcement

**PROPOSED —** The native app may let a user select among returned, feature-eligible outlets. Selection affects UI filters and outbound request context only. Each endpoint that receives `OutletId`/`LocationId` must call the existing access service (or equivalent centralized authorization) before executing the data query. If access is later revoked, a stale local selection must receive a server denial and be removed from the usable context. The client must not infer that BusinessOwner/global-access flags bypass endpoint checks.

## 5. Exact endpoint compatibility

The endpoint shapes below are **VERIFIED existing API contracts** unless explicitly marked proposed. The API's current controller routes are unversioned.

| Endpoint | Request | Successful response / error | Native recommendation |
|---|---|---|---|
| `POST /api/auth/login` | `{ "email": "...", "password": "...", "rememberMe": false }` | `200 LoginResponseDto` | Reuse. No client secret. Do not persist password. Treat `MustResetPassword`/invitation-required errors as blocked sign-in, not as permission to bypass onboarding. |
| `POST /api/auth/refresh-token` | `{ "token": "<access JWT>", "refreshToken": "<refresh token>" }` | `200 LoginResponseDto`; invalid, expired, inactive or replayed credentials return `401 { "message": "..." }` | Reuse only with the exact serialized single-flight policy; retain both tokens securely. |
| `POST /api/auth/logout` | Bearer access token + `{ "refreshToken": "..." }` | `200 { "message": "Logged out successfully" }`; requires unexpired access token | Reuse on best-effort basis; clear local session immediately. |
| `POST /api/auth/logout-all` | Bearer access token; no body | `200 { "message": "All sessions logged out successfully" }` | Reserve for explicit “log out all devices”; warn that access JWTs remain valid until expiry. |
| `GET /api/auth/me` | Bearer access token | `200 UserInfoDto`; missing profile returns `404` | Reuse to verify/refetch profile; do not trust a previously cached profile as authority. |
| `GET /api/me/outlets` | Bearer access token | `200 ApiResponse<AuthorizedOutletsDto>` | Reuse for server-authorized location choices, not as a substitute for per-request authorization. |
| `POST /api/auth/business-context` | Super Admin bearer token + `{ "businessId": 5 }` or `{ "businessId": null }` | `200 { "businessId": 5, "businessName": "..." }`, `400`/`403` for invalid/unauthorized context | Excluded from initial native scope; use only after explicit admin-use approval. |

`LoginResponseDto` uses `token`, `refreshToken`, `expiresAt`, and `user`; the user includes identity, nullable Business/Outlet IDs, role, permissions, role-switch state, and `mustResetPassword`. `expiresAt` is the access-token expiry, not refresh-token expiry. The server response does not include refresh expiry or a device/session identifier. The native app should not infer refresh expiry from `expiresAt`.

**Error convention compatibility:** AuthController login/refresh errors commonly use bare `{ "message": ... }`; `/api/me/outlets` uses `ApiResponse`; global exception handling uses `ApiResponse` with `success=false`, `message`, and sometimes `errors`. Authentication middleware may return an empty `401`/`403`. Native error parsing must accept these established shapes, map status codes first, and never display raw server exception text as trusted guidance.

**Angular compatibility:** Angular consumes the same token/user response fields, posts `{refreshToken}` to logout, and uses `/api/auth/business-context` plus `X-Business-Id` for verified Super Admin scoping. The inspected Angular source contains no production call to `/api/auth/refresh-token` (only a test explicitly exercises the interceptor's unscoped-path handling); automatic refresh is therefore **not verified/currently evident** in that client. Do not alter Angular as part of P2-B2 unless a shared API contract changes.

## 6. Proposed native security policy

### 6.1 Credential and token lifecycle

- Use TLS-only API transport and normal platform certificate validation. No cleartext development or production credentials.
- Never ship a client secret, private API key, or shared signing material in the APK.
- Keep the access JWT in memory for normal request assembly and store the exact current access JWT and refresh token in Android OS-backed encrypted secure storage so refresh can work after process death. Refresh requires the old access JWT even after its expiry.
- Use `flutter_secure_storage` or an equivalent reviewed implementation only after resolving the exact stable release against the P2-A Flutter 3.47.7 / Dart 3.13.5 baseline. Do not put either token in SharedPreferences, SQLite, logs, analytics, crash reports, clipboard, or ordinary backups.
- Require Android Keystore-backed encryption, exclude secure-storage material from cloud/device backup, avoid exporting tokens through debug tooling, and clear credentials on logout, unrecoverable refresh failure, account switch, or explicit session invalidation.
- The app may use local biometric/device authentication to gate access to locally stored credentials only if approved; it is not server authentication and must not be represented as identity proof. Avoid adding biometric prompts to every API request.
- Persist no password. If invitation completion/password setup is not approved for the initial native slice, explain that the account must complete the existing onboarding path; do not loop on login.

### 6.2 Refresh coordinator

1. On startup, read token pair from secure storage and validate the locally stored access expiry only as a scheduling hint; the server remains authoritative.
2. Use the access token until a response indicates it is invalid/expired (or refresh shortly before expiry if the approved policy chooses proactive refresh).
3. For concurrent `401` responses, share exactly one in-flight refresh future. Other requests wait. Refresh the old pair once.
4. On refresh success, atomically replace both stored tokens and the profile from the response before replaying queued requests. Replay each original request at most once.
5. Do not refresh on `403`; do not retry credential/login/logout/context endpoints through the same interceptor; do not retry a refresh after an ambiguous network failure using the old token.
6. On a definitive refresh rejection, erase token pair and Business/Outlet context, transition to signed-out state, and require login. Log only a redacted reason/status, never token material.

### 6.3 Scope and context isolation

- The authenticated server profile supplies regular-user identity/Business context. No client-supplied BusinessId may elevate scope.
- Locations come from `/api/me/outlets`; a chosen Outlet is a contextual preference only and every API validates it on every request.
- P2-B2 may read `/me` and authorized outlets only. It must not call Product, Inventory, POS, stock, sales, accounting, or HR/Payroll endpoints.
- Future cache keys must include the server-confirmed user and Business and, where data is location-specific, Outlet and catalogue/data revision. On logout/account/Business change, clear sensitive in-memory state and prevent old-context requests from updating the new session.
- Keep Super Admin global access and role switching disabled for the first native operational release. If later authorized, keep those capabilities behind distinct permission checks and audited server routes; do not use UI checks alone.

### 6.4 Access-token revocation and account changes

**Current limitation:** Current-user deactivation blocks refresh, but access tokens already issued can still pass stateless JWT validation until expiry. Logout/logout-all similarly revoke refresh only. Permission changes are reflected at refresh, not on already-issued tokens. The default expiry is 60 minutes in source/config, but actual production override is **UNKNOWN**.

**Approval gate:** Before any mobile business-data access, the security owner must choose one of:

- Accept and document the maximum confirmed deployed access-token lifetime as the residual access window after logout/deactivation/permission removal; or
- Require server-side access-token/session revocation checks for sensitive APIs and ensure account disable/permission revocation invalidates relevant refresh sessions; or
- Approve a shorter access-token lifetime and its operational impact, while still documenting that expiry—not immediate revocation—is the enforcement mechanism.

Do not claim immediate session termination unless verified by HTTP tests against the deployed configuration.

## 7. Threat and control matrix

| Threat | Existing control/evidence | Proposed native control | Residual/approval |
|---|---|---|---|
| Credential interception | Access JWT and password auth; production TLS deployment not verified here | TLS-only; platform certificate validation; no secrets in URLs/logs | Verify deployment TLS and supported TLS policy |
| Password guessing | Login fixed-window limit: 5/IP/minute; refresh: 10/IP/minute | Respect server throttles; bounded UI attempts; no retry loops | Confirm proxy/IP partition configuration and production policy |
| Refresh-token disclosure | 64-byte random token; SHA-256 hash stored server-side | Android Keystore-backed secure storage; redact all logs/crash data | Native storage package/config needs review |
| Refresh concurrency/replay | Atomic rotation; reuse rejected/audited; no replay grace | Single-flight; at-most-once replay of business request; fail closed after ambiguous refresh | Recovery after lost rotation response is not supported; approval required |
| Stolen access JWT | JWT lifetime/issuer/audience/signature validation | Keep in memory where possible; secure persisted copy needed by refresh endpoint | Token remains valid through expiry after logout, disable, or permission change |
| Stolen device | No verified device registry or per-device revoke API | OS-backed storage; local wipe on logout; incident instructions invoke logout-all from trusted session | Remote per-device wipe/revoke and immediate access revocation are unavailable |
| Cross-Business request | Middleware validates `X-Business-Id` only for active Super Admin; TenantAccessService exists | No normal-user Business header; fail closed on missing context; require server-side resource checks | Existing Product API isolation is a separate blocking track |
| Foreign Outlet access | Central outlet access service and `/api/me/outlets` list | UI offers returned locations; every scoped request reauthorizes | Verify each future endpoint individually |
| Stale permissions | Refresh recomputes effective permissions | Refresh on expiry; clear on rejection; use least-privilege server policies | Already-issued JWT permissions remain until expiry |
| Role/context confusion | Role-switch claims differ from Super Admin Business context | Exclude role switching and mobile Super Admin from initial scope | Any future capability requires a separate, audited approval |
| Token leakage via diagnostics | Existing API audit patterns avoid logging raw refresh tokens in inspected tests | Redacted structured logs; tests assert no access/refresh token in outputs | Verify crash/analytics providers before integration |
| Account disable | Refresh checks persisted active status | Fail closed on refresh; locally clear on `401` | Current access JWT and existing refresh rows may remain usable under certain transitions; disable-flow revocation is not fully verified |

## 8. Required automated tests

### .NET/API

Reuse and extend current test patterns; add actual HTTP/API contract coverage where command-only tests are insufficient.

1. **Login:** success DTO/casing and expiry; unknown account vs bad password same `401`; inactive account rejected; invitation/password-setup state rejected; `rememberMe` behavior explicitly characterized as fixed lifetime (or changed only under separately approved work); rate limiting returns `429`; no credential/token leakage into logs/audit.
2. **Refresh:** valid current pair rotates once; expired access JWT is accepted only when signature/issuer/audience/algorithm are valid and refresh belongs to that user; wrong user/token, expired refresh, revoked/reused refresh, inactive account all reject; effective permissions are recomputed; replacement hash—not raw token—is persisted.
3. **Concurrency and lost response:** concurrent refresh requests yield exactly one successful rotation; replay is rejected; explicitly characterize the lost-response case and absence/presence of an approved recovery contract.
4. **Logout:** logout revokes only the authenticated user's matching refresh token; logout-all revokes all of that user's refresh sessions; other users' sessions remain unchanged; assert access JWT remains valid until expiry unless a revocation change is separately implemented.
5. **Account/permission changes:** inactive user's refresh is rejected; test whether account-disable workflow revokes refresh sessions; assert/record current access-token behavior for deactivation and permission changes.
6. **Business context:** ordinary roles and inactive Super Admin are rejected; active Super Admin may select only active Business; `null` clears; malformed/repeated header, missing auth, foreign/inactive Business receive established status; authenticated profile reflects validated request-scoped selection only.
7. **Outlet access:** `/api/me/outlets` returns only the access service's authorized locations; direct foreign Outlet/Location IDs are rejected by each future mobile endpoint before data query; authorization is independent of Angular/client context.
8. **Role switch:** fixed allowed-role list, same-Business Outlet constraint, no token on denial, audit event, and current refresh-to-real-role behavior. No mobile call site before separate approval.
9. **API errors:** regression assertions for the existing distinct error shapes and status codes. If a versioned mobile facade is later added, contract tests verify it maps errors without exposing exception/token contents.
10. **Database provider:** current auth relational tests use SQLite. Add PostgreSQL-backed coverage for provider-specific concurrency/transaction behavior before production refresh guarantees rely on it; use isolated test DB only, never production/shared data.

### Flutter

1. Auth state transitions: signed-out, signing-in, authenticated, refreshing, expired/revoked, and transport failure.
2. Secure storage adapter: write/read/replace tokens atomically; clear on logout/account change/rejection; errors are surfaced; no fallback to plaintext storage.
3. Refresh coordinator: many simultaneous unauthorized requests invoke exactly one refresh; all waiters receive the new token; requests replay at most once; no refresh loop on refresh endpoint; no refresh on `403`.
4. Refresh timeout/replay: do not send the old refresh token a second time after ambiguous outcome; move to recoverable signed-out state without leaking the response/token.
5. Token persistence after process restart: both fields needed by the existing refresh request remain securely available; profile is refetched from server rather than trusted as authority.
6. Context isolation: selected location must exist in the server-authorized list; context changes invalidate old requests/cache; logout and user switch clear scoped context; no normal user can set Business scope via request header.
7. DTO/error compatibility: existing camelCase fields, nullable IDs, bare `{message}` vs `ApiResponse`, `401` vs `403` vs `429`, invalid/missing JSON and generic server errors.
8. Privacy: unit tests ensure tokens/passwords are absent from structured logs, error messages, analytics payloads, and test-visible diagnostics.
9. Architecture: repository/use-case/provider boundaries, fake Dio adapter, fake secure storage, route gating, and no business-data feature dependency in P2-B2.
10. Android validation: pinned toolchain, minSdk/API 36 configuration, release-like build without signing secrets, secure-storage plugin compatibility, and backup/data-extraction policy checks.

## 9. Existing automated evidence inspected

**Inspected, not executed for this planning task:**

| Evidence | Coverage observed | Limitation |
|---|---|---|
| `AuthenticationLifecycleCharacterizationTests` | Login claims/hash storage; invalid credentials; command secret formatting; refresh permission recomputation; logout leaves access JWT valid; inactive-user `/me`; invitation, role-switch and logout HTTP behavior | Test-factory/in-process coverage, not production/PostgreSQL |
| `AuthenticationRefreshRotationRelationalTests.ConcurrentRefreshRequests_OnlyOneCanRotateTheCurrentToken` | Two simultaneous relational refresh attempts produce one active replacement and one winner | Uses SQLite; no PostgreSQL run |
| `RefreshTokenRelationalLifecycleTests` | Rotation rollback, reuse rejection, expired/inactive refresh, permission recomputation, logout ownership and uniqueness/FK constraints | Uses SQLite; not provider-specific PostgreSQL evidence |
| `RoleSwitchRelationalTests` | Role switch/return claims, refresh hashes, invalid role and foreign-Business Outlet rejection | Does not establish native-client role-switch suitability |
| `ValidateBusinessContextQueryHandlerTests` | Active Super Admin-only Business selection; null clears; inactive Business rejected | Handler unit tests; not complete API/middleware end-to-end coverage |
| Angular `auth.service.spec.ts` and `business-context.service.spec.ts` | Logout token handling and Business-context restoration/selection behaviors | Browser localStorage is not an acceptable Android secure-storage model; no evidenced Angular auto-refresh flow |

No PostgreSQL auth test, production endpoint test, device/session revocation test, native secure-storage test, or native token-refresh test was executed in this task.

## 10. ADR recommendations and approvals

### ADR-AUTH-01 — Native authentication and token lifecycle

**Recommendation (PROPOSED):** Reuse first-party email/password login plus existing rotating refresh tokens for initial Android authentication, with no embedded client secret and no claim of OAuth/PKCE. Store the access+refresh token pair using Android OS-backed secure storage. Implement single-flight refresh and fail closed on replay/ambiguous network failure. Keep existing logout endpoints but describe their refresh-only revocation semantics accurately.

- **Repository evidence:** Existing login, HS256 access JWT, refresh rotation/hash storage/reuse detection, logout/logout-all; no IdP/PKCE integration found. Source and tests are listed below.
- **Trade-off:** Fastest compatibility path and uses existing server security behavior; native app is a public client with a password-entry flow and bearer tokens. Device binding and immediate access-token revocation are not provided.
- **Security/data-integrity impact:** Correct secure storage and serialized refresh reduce token leakage/replay; no design removes residual access-token validity after logout/deactivation.
- **Approval required:** Security owner explicitly approves direct first-party password auth for a native public client; confirms deployed access and refresh lifetimes; chooses acceptable access-token revocation window; approves Android secure-storage/backup policy; decides whether invitation/password setup and multi-device logout are in initial scope.
- **Blocking classification:** Blocks real sign-in until security approval and production TTL/revocation behavior are confirmed. No OAuth/PKCE may be claimed without a supported IdP.
- **Required tests:** Section 8 login, refresh, concurrency/replay, lifecycle, storage, and privacy suites.

### ADR-AUTH-02 — Business/Outlet context and authorization

**Recommendation (PROPOSED):** Regular user's Business comes from the current server profile; authorized locations come from `/api/me/outlets`; selected Outlet remains a local/UI filter. Every endpoint must reauthorize Business ownership, permission, and Outlet access server-side. Do not enable mobile Super Admin/global Business selection or role switching in the first operational release. Clear local context on identity changes.

- **Repository evidence:** JWT/profile Business/Outlet claims; active-Super-Admin-only `business-context`; validated `X-Business-Id` middleware; centralized `TenantAccessService`/`UserOutletAccessService`; Angular strips arbitrary caller headers and restores only validated selection.
- **Trade-off:** Reuses existing context discovery without changing normal-user identity semantics; defers administrative/global and acting-role workflows.
- **Security/data-integrity impact:** Prevents client-controlled context from being treated as authorization, subject to endpoint-by-endpoint server enforcement. It does not cure generic Product API cross-Business exposure.
- **Approval required:** Business/security owners approve Super Admin exclusion; approve profile-derived Business and server-authorized Outlet list as context sources; approve local cache partition/clear policy; confirm whether a selected Outlet is required for sign-in or only for features that need it.
- **Blocking classification:** Blocks mobile Business-data access until the relevant API endpoint is proven to enforce the contract. Does not block non-business Flutter shell work.
- **Required tests:** Section 8 Business context, outlet access, cache/context invalidation, and cross-Business denial tests.

### ADR-API-01 — Versioned mobile APIs

**Recommendation (PROPOSED):** Preserve shared auth endpoints and DTOs above for P2-B2 compatibility. Place future mobile-specific business APIs under `/api/mobile/v1/...` (or an explicitly approved equivalent), with server-side versioned contract tests. Do not pass existing generic Product endpoints off as a mobile-safe API; Product isolation remains separately blocked.

- **Repository evidence:** Inspected Auth and `/api/me/outlets` routes are unversioned; no versioning registration was found in the searched API project.
- **Trade-off:** Avoids needless auth-contract duplication now while protecting future mobile business-data contracts from silent breaking changes.
- **Security/data-integrity impact:** Version prefix alone is not a security boundary; authorization remains required on every action.
- **Approval required:** API owner approves version prefix and whether shared auth stays version-neutral.
- **Blocking classification:** Non-blocking for login/context-only P2-B2; blocking before new mobile business-data API contracts are frozen.
- **Required tests:** OpenAPI route inventory and request/response compatibility tests for each versioned API.

## 11. Narrow P2-B2 implementation proposal

**Objective:** Implement native sign-in/session shell and context discovery only, after ADR-AUTH-01 and ADR-AUTH-02 approval.

**Scope:**

1. Add the Flutter auth domain/application/infrastructure/presentation pieces to the existing P2-A structure: session repository, immutable session state, sign-in, refresh coordinator, local sign-out, current-profile fetch, and authorized-outlet fetch.
2. Resolve and pin an Android-compatible secure-storage package against Flutter 3.47.7 / Dart 3.13.5; securely persist the current access and refresh tokens; document backup exclusion and token redaction.
3. Reuse only the exact shared endpoints listed in Section 5. Do not add client-secret authentication, role switching, Super Admin Business selection, Product/Inventory/POS calls, or business cache tables.
4. Keep app states and errors explicit: signed out, loading, authenticated, refreshing, expired/revoked, unauthorized, forbidden, throttled, and offline/transport error.
5. Add unit/widget tests plus fake-HTTP/secure-storage tests, Android debug/staging builds, and architecture checks. Add .NET HTTP coverage only if current endpoint contracts prove insufficient to verify the approved decision.

**Dependencies:** Approved ADR-AUTH-01 and ADR-AUTH-02; confirmed deployed JWT/refresh expiry and revocation behavior; approved storage/backup policy; P2-A reviewed baseline. API owner agreement that `/api/auth` remains compatible for this slice.

**Explicit exclusions:** Product catalogue, stock, count sessions, notifications, dashboards, sales, accounting, HR/Payroll, role switching, Super Admin global actions, migrations, and any promise of immediate JWT revocation.

**Acceptance gates:**

- Token pair survives process restart only in approved secure storage; no token/password appears in logs or ordinary storage.
- Concurrent unauthorized calls cause one refresh and at most one replay each; ambiguous refresh failure never replays the old refresh token.
- Login/refresh/logout match established wire contracts; error statuses/shapes tested.
- Business and Outlet context is refetched and treated as non-authoritative client state; context is cleared on user/logout change.
- No Product, Inventory, POS, or other business-data endpoint is called.
- `flutter analyze`, format check, Flutter tests, architecture tests, and Android debug/staging builds pass on the pinned P2-A toolchain.

## 12. Open decisions and blockers

| ID | Decision/blocker | Required owner/action | Effect |
|---|---|---|---|
| AUTH-01a | Approve first-party email/password flow for a native public client; no supported IdP/PKCE was found | Security/identity owner | Blocks real sign-in |
| AUTH-01b | Confirm production access/refresh TTL overrides; choose acceptable JWT validity after logout, deactivation, or permission change | Security/operations owner | Blocks mobile business-data access; access expiry is only a source default until confirmed |
| AUTH-01c | Choose response-loss behavior for committed refresh rotation; current client cannot recover an unknown replacement and server has no grace contract | Security/API owner | Blocks robust unattended session continuity; fail-closed re-login is available if accepted |
| AUTH-01d | Confirm every account-disable path revokes refresh sessions or explicitly accept that the refresh handler only rejects while the account is inactive | Identity/API owner | Blocks any claim of complete session revocation |
| AUTH-01e | Approve invitation completion/password setup in native app or require existing onboarding before native sign-in | Product/security owner | Determines login UX and P2-B2 scope |
| AUTH-02a | Approve profile-derived Business + `/api/me/outlets` context, with server reauthorization on every scoped request | Business/security owner | Blocks business-data access |
| AUTH-02b | Explicitly exclude mobile Super Admin/role switching initially or define audited permissions and server policy | Product/security owner | Blocks any admin/global mobile workflow |
| AUTH-02c | Approve context/cache clearing and partitioning on account/Business/Outlet change | Data owner/security owner | Blocks local business-data persistence |
| API-01 | Approve `/api/mobile/v1` for future mobile business-data endpoints while leaving shared auth routes compatible | API owner | Non-blocking for P2-B2 auth-only slice |
| DATA-ISOLATION | Complete and verify Product Business ownership/isolation; generic Product APIs are not approved for mobile | Backend/security owner | Blocks catalogue/variant/barcode/mobile stock features |
| TOOL-SECURE | Resolve and review exact secure-storage dependency/configuration on the pinned Flutter/Android toolchain | Mobile/security owner | Blocks token persistence implementation |

## 13. Source references

### .NET API/application/infrastructure

- [AuthController.cs](../../../src/RetailPOS.API/Controllers/AuthController.cs) — login `32–82`; refresh `152–218`; logout/logout-all `220–296`; role switch/return `298–392`; `/me` `394–418`; Business context `420–450`; response mapping follows.
- [LoginRequestDto.cs](../../../src/RetailPOS.API/DTOs/Auth/LoginRequestDto.cs) — email, password and `RememberMe`.
- [LoginResponseDto.cs](../../../src/RetailPOS.API/DTOs/Auth/LoginResponseDto.cs) — `LoginResponseDto` and `UserInfoDto` wire members.
- [RefreshTokenRequestDto.cs](../../../src/RetailPOS.API/DTOs/Auth/RefreshTokenRequestDto.cs) — refresh request requires both `Token` and `RefreshToken`; logout request carries refresh token.
- [AuthenticationCommands.cs](../../../src/RetailPOS.Application/Authentication/Commands/AuthenticationCommands.cs) — login credential/active/reset checks and effective permission construction.
- [RefreshTokenCommands.cs](../../../src/RetailPOS.Application/Authentication/Commands/RefreshTokenCommands.cs) — old JWT validation without lifetime check, current profile check, permission recomputation and rotation.
- [JwtAuthenticationTokenService.cs](../../../src/RetailPOS.Infrastructure/Authentication/JwtAuthenticationTokenService.cs) — claims, HS256, random refresh-token generation, issuer/audience/algorithm validation.
- [AuthenticationCredentialsIssuer.cs](../../../src/RetailPOS.Infrastructure/Authentication/AuthenticationCredentialsIssuer.cs) and [JwtTokenSettings.cs](../../../src/RetailPOS.Infrastructure/Authentication/JwtTokenSettings.cs) — fixed configured token lifetimes; role-switch refresh issues the persisted real role.
- [RefreshTokenStore.cs](../../../src/RetailPOS.Infrastructure/Authentication/RefreshTokenStore.cs) — hashed-token persistence, transactional conditional rotation, reuse result and revocation methods.
- [Program.cs](../../../src/RetailPOS.API/Program.cs) — JWT validation and rate limits; authentication → BusinessContextMiddleware → authorization order.
- [BusinessContextMiddleware.cs](../../../src/RetailPOS.API/Middleware/BusinessContextMiddleware.cs) — `X-Business-Id` validation/status behavior.
- [ValidateBusinessContextQuery.cs](../../../src/RetailPOS.Application/Authentication/Queries/ValidateBusinessContextQuery.cs) — active Super Admin and active Business validation.
- [UserOutletsController.cs](../../../src/RetailPOS.API/Controllers/UserOutletsController.cs), [AuthorizedOutletsDto.cs](../../../src/RetailPOS.API/DTOs/Outlet/AuthorizedOutletsDto.cs), and [UserOutletAccessService.cs](../../../src/RetailPOS.API/Services/UserOutletAccessService.cs) — authorized-location endpoint and DTO/service delegation.
- [TenantAccessService.cs](../../../src/RetailPOS.API/Services/TenantAccessService.cs) and [RoleSwitchContext.cs](../../../src/RetailPOS.API/Services/RoleSwitchContext.cs) — Business context and effective identity hooks; use by a particular endpoint must still be verified.
- [GlobalExceptionHandlerMiddleware.cs](../../../src/RetailPOS.API/Middleware/GlobalExceptionHandlerMiddleware.cs) — standardized exception response shape; endpoint-specific controller responses differ.
- [appsettings.json](../../../src/RetailPOS.API/appsettings.json) — checked-in JWT issuer/audience and default lifetimes; production configuration remains unverified.

### Existing tests and Angular compatibility

- [AuthenticationLifecycleCharacterizationTests.cs](../../../tests/RetailPOS.IntegrationTests/Auth/AuthenticationLifecycleCharacterizationTests.cs) — login/refresh/`/me`/logout HTTP and lifecycle characterizations, including access JWT remaining valid after logout.
- [AuthenticationRefreshRotationRelationalTests.cs](../../../tests/RetailPOS.IntegrationTests/Auth/AuthenticationRefreshRotationRelationalTests.cs) and [RefreshTokenRelationalLifecycleTests.cs](../../../tests/RetailPOS.IntegrationTests/Auth/RefreshTokenRelationalLifecycleTests.cs) — SQLite relational rotation, concurrency, replay, expiry, permission and logout behavior.
- [RoleSwitchRelationalTests.cs](../../../tests/RetailPOS.IntegrationTests/Auth/RoleSwitchRelationalTests.cs) and [ValidateBusinessContextQueryHandlerTests.cs](../../../tests/RetailPOS.UnitTests/Auth/ValidateBusinessContextQueryHandlerTests.cs) — role-switch and Super Admin context rules.
- [auth.service.ts](../../../retailpos-frontend/src/app/services/auth.service.ts), [auth.interceptor.ts](../../../retailpos-frontend/src/app/interceptors/auth.interceptor.ts), and [business-context.service.ts](../../../retailpos-frontend/src/app/services/business-context.service.ts) — current web token/context storage and verified-context header behavior.
- [03_FLUTTER_ARCHITECTURE.md](./03_FLUTTER_ARCHITECTURE.md), [07_PHASED_DEVELOPMENT_PLAN.md](./07_PHASED_DEVELOPMENT_PLAN.md), [12_PHASE_0_REVIEW_AND_PROPOSED_ADR_RESOLUTIONS.md](./12_PHASE_0_REVIEW_AND_PROPOSED_ADR_RESOLUTIONS.md), and [15_P2_A_FLUTTER_FOUNDATION_BOOTSTRAP.md](./15_P2_A_FLUTTER_FOUNDATION_BOOTSTRAP.md) — accepted planning direction and P2-A foundation baseline.
