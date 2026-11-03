# Backend Readiness and Parallel-Development Proposal

**Prepared:** 2026-10-15  
**Status:** Historical readiness proposal. Its pre-approval gate is superseded for the current release by the Product Owner decision and implementation acceptance in [30_MOBILE_SALES_DASHBOARD_API_ACCEPTANCE.md](./30_MOBILE_SALES_DASHBOARD_API_ACCEPTANCE.md).  
**Scope:** Read-only assessment of the current repository, the Mobile SRS/LLD, and the existing mobile security/remediation planning documents.

> No API, application, Flutter, database, migration, or test files were changed for this assessment. No Development or Production database was accessed. No worktree or branch was created or switched, and no files were staged or committed.
>
> The Product Owner subsequently approved a single-business, multi-outlet deployment model and deferred Product ownership B1–B4 restructuring for this release. Read the later acceptance document for the current decision; the B1–B4 blockers below describe the former multi-tenant prerequisite and are not current-release entry criteria.

## Executive decision

**Backend readiness for general mobile business-data integration is blocked.** There are useful ERP authentication, sales-reporting, inventory, product, and stock-count capabilities, but the presence of an endpoint or a permission policy does not establish that its data is safe for a mobile client. New mobile business APIs must apply server-side permission and resource-scope checks and return purpose-built DTOs; Flutter must not treat client-selected roles, Business IDs, or Outlet IDs as authority.

Key status:

- **A1 — POS and stock-bearing variant-search location containment:** protections are present in the reviewed paths, but acceptance evidence is incomplete.
- **A2 — Inventory caller-supplied location containment:** the later closeout in `20_A2_INVENTORY_LOCATION_SECURITY_ACCEPTANCE.md` supersedes the earlier A2 status in the catalogue assessment. It reports a pass for its eight enumerated routes, including focused real-PostgreSQL HTTP verification. This does not close A3 or certify other inventory surfaces.
- **A3 — Remaining stock-read scope audit:** open. Direct-ID, aggregate, summary, ledger/report and related routes are not collectively certified as scoped.
- **Product ownership B1–B4:** blocked. The ownership/identifier decision and data profile are unapproved; additive ownership, controlled backfill and universal enforcement are not implemented.
- **Native authentication/context:** P2-B1 remains an approval proposal. P2-B2 is partially passed; live ERP lifecycle and deployed JWT lifetime/revocation gates remain open. The SRS's identity-provider/approved PKCE requirement and the proposed first-party password-flow recommendation are not yet reconciled by an approved decision.
- **Mobile business API contracts:** no approved, implemented `/api/mobile/v1` contract was found. Dashboard, shared notification inbox, forecasting and mobile catalogue sync APIs are not established by the inspected API controller inventory.

The A1–A3 inventory-remediation labels and product-ownership B1–B4 labels are separate from the **P2-B1 authentication/context** and **P2-B2 secure-authentication** phase labels. This proposal uses the prefixes `A1–A3`, `CAT-B1–B4`, `P2-B1`, and `P2-B2` where needed to avoid conflating them.

## Evidence boundary and checkout caveat

The assessment used the Mobile SRS/LLD and `Docs/Mobile/Planning/01`–`28` as present in the worktree, the security documentation, and API controller/DTO source. These Mobile documents are currently untracked in this checkout. The assessment did not execute tests or query databases. Source findings describe this checkout, not a deployed environment.

The current checkout is on `staging`, ahead of its upstream by one commit, and is substantially dirty: backend API/Application/Infrastructure/test paths contain many pre-existing changes, while `Docs/Mobile/` is currently untracked as a directory. The A2 closeout and the earlier catalogue assessment therefore describe evidence recorded at their respective review points; the A2 closeout is the newer, narrower result. Before implementation, the workstream must reconcile these documents and verify the relevant security fixes on an agreed immutable baseline. Do not infer that uncommitted changes are approved or safe to carry into a new worktree.

## Current API inventory

“Reuse” below means reuse existing domain/query behavior where its authorization contract is verified. It does **not** mean exposing the existing ERP route unchanged to Flutter.

| Capability | Existing API surface and current evidence | Safe reuse / remediation |
|---|---|---|
| Authentication and context | `POST /api/auth/login`, `refresh-token`, `logout`, `logout-all`, `GET /api/auth/me`, and `GET /api/me/outlets`; role/business-context operations also exist. Refresh tokens rotate and are persisted hashed; authorization uses claims/policies and context services. | Reuse only after Security approval of the native-client flow, deployed access/refresh lifetimes and residual access-token validity after logout/deactivation, plus client refresh/replay behavior. `/api/me/outlets` is a context-discovery source, not a replacement for server-side authorization on each resource request. Do not enable mobile Super Admin/global-business selection or role switching by default. |
| Sales and reports | `SalesController` exposes sale list/detail/search, receipt, `today/summary`, and sale commands. `SalesReportsController` exposes `summary`, `top-products`, `by-outlet`, `by-payment-method`, and `daily-trend` under `reports.sales`. The report controller resolves/authorizes Outlet filters; the cross-outlet report narrows non-owner output. | Best existing read-model source for an initial sales dashboard/insights slice. Do not call these endpoints “mobile-safe” without verifying role-specific outlet/self scope, Business context, date/time boundaries, definitions and sensitive-field grants. In particular, salesperson self-only data and owner/manager multi-outlet access need explicit tests. Do not return cost/profit unless the server independently authorizes those fields. |
| Inventory and stock reports | `InventoryController` has list/detail, Outlet/Warehouse reads, low/out-of-stock, expiry, variant total, valuation, summary, search, and attribute filters. `InventoryReportsController` and stock ledger/transaction/report controllers add broad report and export surfaces. A2 passed for the eight routes recorded in its acceptance document. | Reuse only the A2-authorized query path for the exact accepted behavior. A3 remains open for unscoped direct-ID/aggregate and other report/ledger/reference reads; do not use them for a mobile dashboard, insight, stock hint, or stock take until route-by-route scope is proved. No caller-supplied location is authority. |
| Products, variants and categories | `ProductsController`, `ProductVariationsController`, `ProductImagesController`, `CategoriesController`, barcode and variant-search routes are protected by product/category policies. `Product.ProductCode` is the current semantic match for Main Product Code; `ProductVariant.ProductId` provides parent identity and inventory/counts use variant IDs. | **Do not expose the general catalogue to Flutter yet.** Product/Category ownership is not established in the inspected model; policy protection is not Business isolation. Product, variant, barcode, image, nested variation and search routes require the approved CAT-B1–B4 ownership and scope controls. The Main Product Code / variant barcode contract also requires resolution of identifier collisions and attribute projection. |
| Stock take / stock count | `StockCountsController` (`/api/stock-counts`) supports search, detail, create, upload/download/print and lifecycle actions including submit, approve, reject and reopen. The controller requires authentication and uses named `StockCount.*` grants and a create-scope provider; existing ERP lifecycle and adjustment posting remain authoritative. | Reuse the existing StockCount aggregate and review/approval/adjustment path, not a second stock master or a direct mobile inventory write. The generic ERP routes are not the offline mobile protocol: a mobile session, pinned catalogue/baseline, durable operation receipts, idempotency, replay/recovery, concurrency policy and scoped negative tests remain to be contracted and implemented. Validate every read and transition at resource level. |
| Business dashboards | No general Owner/Manager/Salesperson dashboard controller/route was found in the API controller inventory. HR and Payroll dashboard controllers exist, but do not satisfy the mobile business dashboard requirements. Sales reports provide candidate query sources. | New mobile read model/API is needed. Derive the effective role and grants from the authenticated principal; do not accept a role in the route as authorization. Keep all metric formulas, access filtering and currency/time-zone semantics server-owned. |
| Notifications | No shared business inbox/read-state/configuration controller was found. Existing subscription reminders and integration outbox/inbox/worker infrastructure are reusable foundations; verified notification producers and the shared notification policy/inbox module are absent. | New module/API is needed. Verify each source event is transactionally durable before enabling a rule; implement idempotent recipient fan-out and per-user read state. Start with in-app inbox; push/email/SMS are outside the initial proposal. |
| Forecasting | No forecasting controller or forecast API was found. | New capability and governed read contract are needed. Establish data readiness and back-tested baseline/uncertainty before production recommendations. Keep output advisory, provenance-bearing and explicitly non-authoritative. |

Existing API DTO families include auth request/response, sales/report, inventory, product/variant and stock-count DTOs. They are ERP contracts, not approved mobile DTOs. In particular, product responses include fields such as cost, stock and media that must not be copied wholesale into a mobile payload.

## Security blockers and dependencies

| Gate | Current assessment | Required before dependent mobile use |
|---|---|---|
| Native authentication (P2-B1/P2-B2) | The P2-B1 document records no supported IdP/PKCE flow found and proposes first-party password login plus rotating refresh only if approved. P2-B2 is partially passed; local Flutter checks/manual UI evidence do not prove live token rotation, deployed JWT TTL/revocation, or the full ERP lifecycle. | Security/Identity approval must resolve the SRS AUTH-001 requirement versus the proposed password-flow deviation, confirm deployed token expiry and revocation behavior, approve Android token/backup handling and refresh-timeout semantics, and close ERP-backed acceptance. No embedded client secret. |
| A1 | Existing POS and stock-bearing variant-search protections are reported in place, but explicit denied-location coverage for both variant-search paths and full acceptance evidence are incomplete. | Complete the documented route-specific positive/negative tests, including no stock-quantity disclosure, omitted/foreign location behavior and cache-key isolation. |
| A2 | Passed for the eight routes and scenarios in the later A2 closeout; six focused real-PostgreSQL HTTP tests are reported passed. | Keep scope limited to those routes. Do not generalize the pass to A3, other inventory/report routes or product ownership. Use only the dedicated disposable A2 PostgreSQL target for approved future integration tests. |
| A3 | Open; direct-ID, aggregate and remaining stock-read surfaces require a complete route/read-model audit. | Inventory every inventory, ledger, report, export, stock-hint and reference read; make authorized scope explicit in application query inputs and persistence predicates; add denial/non-disclosure tests. Do not include unverified stock metrics in mobile dashboards or insights. |
| CAT-B1–B4 | No approved Product/Category ownership policy or read-only legacy profile; no ownership schema, validated backfill or universal enforcement. Live legacy data quality is unknown. | Product/Security/ERP/Inventory/DBA owners approve ownership, shared/system products, identifier rules and migration policy; DBA supplies an approved read-only profile; only then design additive schema, controlled quarantine/backfill and end-to-end Business isolation tests. Do not infer ownership from stock, outlet, creator or code. |
| Permission and resource scope | Existing permission-policy gates are the coarse authorization layer. Security docs record permission-catalog drift and incomplete uniform policy/resource-scope validation. `[Authorize]` or a frontend guard alone does not prove Business/Outlet isolation. | Confirm all required policies are registered and granted consistently; independently authorize Business, Outlet/Warehouse, entity and sensitive fields in each handler/query. Test direct HTTP requests, not only UI navigation. |
| API compatibility | Existing auth and business routes are predominantly unversioned; the proposed `/api/mobile/v1` prefix and error/versioning policy are not approved. | Approve API/versioning ADR, DTO allowlist, auth/error envelope, compatibility and deprecation policy before freezing Flutter consumption. |
| Database/test safety | The A2 planning record documents a dedicated PostgreSQL 16 disposable test environment. No database was accessed for this assessment. | Any approved integration run must target only the dedicated disposable instance and test-created databases. Never use Development, Production or a server/volume of unknown provenance. |

## Recommended workstream order

This sequence prioritizes role dashboards and Sales Insights while respecting the catalogue and stock authorization gates. It is a recommendation, not permission to start work.

1. **Product Owner / Architecture / Security decision gate.** Approve the mobile scope, role-to-metric and permission matrix, metric definitions, native authentication resolution, API versioning, Business/Outlet context, and product ownership decision owners. Record unresolved decisions as blocked rather than defaulting them.
2. **Security readiness closeout.** Reconcile the current dirty checkout to an approved baseline; complete A1 acceptance and A3 route-scope audit. Close P2-B1/P2-B2 security and deployed-token gates. A2 remains limited to its accepted route set. This work can proceed independently of CAT-B2 schema work only when changes are isolated and reviewed.
3. **Owner/Manager/Salesperson dashboard and Sales Insights (read-only first).** Reuse existing sales-report query logic behind mobile-specific server DTOs. Start with metric definitions and scope tests; omit cost/profit, product-level details and stock metrics until their separate grants and scope gates pass. Manager scope is authorized outlets; Salesperson is self-only; Owner scope is limited to the authenticated Business. Dashboards must report currency, business-time-zone window, freshness, definitions and refund/void/tax treatment.
4. **Catalogue contract and ownership remediation.** Resolve CAT-B1 and complete the approved read-only data profile before any ownership migration design. Then sequence additive schema/compatibility (CAT-B2), signed and auditable backfill/quarantine (CAT-B3), and universal scoped enforcement/uniqueness/admin audit (CAT-B4). Only after acceptance should the mobile manifest/snapshot/delta contract or catalogue data be enabled.
5. **Stock-take backend vertical slice.** After A3 and the catalogue identity/revision contract are accepted, adapt the existing stock-count lifecycle with server-owned session scope, pinned revision/baseline, durable idempotent operation receipts, conflict/replay behavior and submission into existing ERP review/approval. No mobile posting to inventory or adjustment ledger.
6. **Shared Notifications.** Build an in-app inbox and Angular configuration around verified transactional source events, scoped recipients, deduplication, durable read state and audit. Enable only event types whose producers and policy semantics are accepted. Notifications may be designed earlier but not represented as operational before producer gates pass.
7. **Forecasting.** First prove historical data quality and time-based baseline/backtest performance. Then publish a versioned, uncertainty/provenance-bearing advisory contract. Production recommendations and any AI narrative remain separately gated.
8. **End-to-end integration and staged release.** Contract tests, disposable-PostgreSQL integration, role/outlet denial tests, mobile client verification, operational monitoring, rollback and Product Owner UAT precede release.

## Shared contracts Flutter will eventually consume

These are contract families to agree and version, not existing mobile DTOs. Use explicit allowlisted DTOs; never serialize EF/domain entities or reuse broad ERP DTOs as-is.

| Contract family | Minimum contents and constraints |
|---|---|
| Session and authorized context | Stable user ID, effective role/capabilities, authorized Business identity, authorized Outlet/Warehouse choices and selected context where applicable. Context is server-derived; a submitted Outlet is a filter that must be reauthorized. No client-selected Business or role becomes authority. |
| Dashboard / sales insights | Metric code, value, currency/unit, reporting window and business time zone, metric definition/version, as-of/freshness, comparison window and provenance. Include only metrics granted to that principal. Include explicit inclusion rules for refunds, voids, tax, discounts and targets. |
| Catalogue projection | Stable ERP `ProductId` and `VariantId` (existing `long` identifiers unless an approved decision changes them), Main Product Code (`Product.ProductCode`), product/variant status, allowlisted names, canonical attribute IDs/labels/values and eligible primary variant barcode. Preserve barcode strings losslessly. Require Business-bound revision, schema version, cursor and tombstones; reject ambiguous identity rather than guessing. |
| Stock-take session and operation receipt | Opaque server session ID/reference, authorized location/scope, pinned catalogue revision and baseline policy, lifecycle/revision, client operation ID/sequence, per-operation accepted/duplicate/rejected/conflict outcome, durable cursor and actionable reason. The server calculates accepted totals and ERP reconciliation; client totals never post inventory. |
| Notification inbox | Stable notification ID/type and type version, safe display fields, occurred/created time, approved deep-link resource identity, read state and pagination cursor. Every fetch/read mutation is recipient- and scope-authorized; reauthorize a linked resource when opened. |
| Forecast | Run/model version, generation/as-of time, training cutoff/history coverage, horizon, scope, forecast value/range, quality/backtest metadata, fallback/low-confidence flag, assumptions and advisory-only status. No invented values on sparse history. |
| Cross-cutting envelope | UTC timestamps, stable bounded pagination, explicit empty/error/stale states, correlation ID, documented validation/authorization/conflict error codes, and backward-compatible schema evolution. Do not put secrets, refresh tokens, or unnecessary personal/customer data in payloads or logs. |

Keep the ERP's current `long` product and variant IDs unless an approved ADR and compatibility analysis say otherwise. The LLD's illustrative UUIDs and operation payload examples are not deployed schemas.

## Contract handoff process

1. **Agree requirements before endpoint work.** Product Owner signs role/metric definitions, data visibility, notification semantics and stock-count rules. Security signs principal, permission, Business/Outlet scope, token, denial and sensitive-field behavior. API/Architecture signs route/version, DTO, error, pagination and compatibility conventions.
2. **Publish the contract first.** Maintain a reviewed OpenAPI document and example fixtures under a proposed `Docs/Mobile/Contracts/` directory. Mark every route and field as approved/proposed, identify grants and scope inputs, document freshness/date semantics and state transitions. No contract is frozen from a UI mock or LLD example alone.
3. **Review API and Flutter consumers together.** API owner maps the approved schema to typed server DTOs and authorization tests. Flutter owner confirms field/state coverage, maps its repository models and uses agreed fixtures/mocks until a backend endpoint is approved for integration. Avoid parallel private DTO definitions.
4. **Gate contract changes.** A PR changing a contract updates OpenAPI, fixtures, DTO mapping, requirement traceability and consumer contract tests together. Require API owner, Flutter integration owner, Security for auth/scope changes, and Product Owner for behavior/metric changes.
5. **Verify before enabling.** Run focused server contract/authorization tests, cross-Business/Outlet denial tests, and PostgreSQL tests for persistence/concurrency behavior on the dedicated disposable test environment. Then Flutter validates decoding, loading/empty/stale/error states and denied-field behavior against the same fixtures.
6. **Version and retire deliberately.** Additive compatible changes may remain in v1; breaking semantics require a new approved version or documented migration period. Deprecation includes telemetry, client adoption and a rollback/disable plan. Do not let a route prefix substitute for authorization.

## Worktree and branch isolation proposal

**Recommended names (proposal only):**

- Branch: `feature/mobile-backend-api`
- Sibling worktree: `..\pos-v2-mobile-backend-api` (relative to the current repository root)
- Base: an explicitly approved immutable commit containing the accepted Mobile SRS/LLD and the security-remediation checkpoint needed by the selected slice.

Do **not** create this worktree from the current dirty `staging` checkout. The checkout has extensive pre-existing backend changes and untracked `Docs/Mobile/`; a new worktree would not inherit uncommitted files. First coordinate with the owners of the current changes and agree which work is accepted and how the mobile specifications/remediation evidence are preserved in a reviewable baseline. Then, only after approval, the operator may run an equivalent of:

```powershell
git worktree add -b feature/mobile-backend-api ..\pos-v2-mobile-backend-api <approved-base-commit>
```

Do not run that command as part of this proposal. Keep Flutter UI implementation on its own branch/worktree. Do not cherry-pick unreviewed security or UI changes; use normal reviewed commits and contract handoff. Keep shared planning edits narrow and coordinate them to avoid concurrent changes to the SRS, LLD, API contract and acceptance records.

## Likely Flutter/backend overlap

| Surface | Why coordination is needed | Isolation rule |
|---|---|---|
| `retailpos-mobile/` and `.github/workflows/flutter-mobile.yml` | Flutter UI, models, local repositories, routes and CI are owned by the separate UI workstream. | Backend worktree must not modify Flutter source, tests, assets, Android configuration or mobile CI. Provide fixtures/contracts through the agreed handoff. |
| `Docs/Mobile/IRANIMART_BUSINESS_MOBILE_SRS_v1.4.md`, `IRANIMART_BUSINESS_MOBILE_LLD_v1.4.md`, and Planning `23`–`28` UI acceptance reports | Shared requirement/acceptance evidence; currently untracked in this checkout and likely changing with UI review. | Do not rewrite UI acceptance reports. Contract-impacting edits require owner review and traceability updates. |
| Auth, claims, permission catalog, `Program.cs`, middleware and outlet-access services | Shared security boundary and mobile auth/context dependency; existing backend changes already touch broad authorization surfaces. | Require focused review and cross-client regression; never relax server authorization to satisfy UI navigation. |
| Product, inventory, sales/report and stock-count controllers, handlers, DTOs and tests | Existing Angular clients depend on these routes, while mobile must use approved DTOs and can introduce authorization changes. | Prefer additive mobile contracts; preserve ERP route and response compatibility. Close A1/A3 and CAT ownership gates before expanding data access. |
| Notification/outbox and forecasting data contracts | New shared functionality may affect Angular as well as Flutter and depends on verified business event semantics. | Treat as shared ERP capabilities, with Angular/API and mobile contract owners reviewing the same event and inbox semantics. |

## First safe implementation slice — proposed, not authorized

After the Product Owner and Security gates are signed, the recommended first backend implementation is a **read-only, mobile-specific sales-summary dashboard contract** for Owner, Manager and Salesperson:

- Add a purpose-built versioned response/handler over existing sales-report query logic; do not proxy or broaden a generic ERP endpoint.
- Derive effective role, grants and Business scope from the authenticated server context. Validate any Outlet filter through the existing authorized-location service. Never accept a client role or Business ID as scope.
- Start with only Product-Owner-approved non-sensitive sales aggregates. Exclude cost/profit, product-level or customer-level detail, inventory/stock metrics and unapproved targets. Add those only after their field grant, data-scope and metric gates pass.
- Define currency, business-time-zone period boundaries, tax/refund/void/discount treatment, comparison window and freshness. No Flutter-side reimplementation of financial formulas.
- Prove Owner/Manager/Salesperson positive and negative cases, including cross-Business, unauthorized Outlet, salesperson self-only scope, forbidden sensitive fields and date-boundary behavior. Use the dedicated disposable PostgreSQL environment for any approved database-backed tests.
- Make no schema change in this slice. Do not modify Flutter UI in this backend workstream; hand off the reviewed OpenAPI contract and fixtures.

This slice is **not safe to start yet**: authentication/deployed-token gates and role/metric approval are unresolved, and any stock-based dashboard widget additionally depends on A3. Product catalogue integration remains separately blocked by CAT-B1–B4.

## Product Owner review requested

Before any implementation, request decisions on:

1. The role-to-metric and sensitive-field matrix, including whether gross profit/margin is in scope and which roles may see it.
2. Native authentication: approve a supported identity-provider/PKCE approach, or explicitly resolve the SRS AUTH-001 deviation and approve the proposed first-party public-client flow; confirm deployed JWT/refresh expiry and revocation behavior.
3. Product/Category ownership, shared/system-product policy, identifier rules and authorized data-profile owner for CAT-B1–B4.
4. Dashboard filters, business time zone, targets, refunds/voids/tax definitions, Salesperson self-scope and manager multi-Outlet rules.
5. Approval to prepare an OpenAPI contract and, after this review, a separate isolated backend worktree based on a named clean commit.

**Stop point:** no API, DTO, database or migration implementation is authorized by this document.

## Source records reviewed

- [Mobile SRS v1.4](../IRANIMART_BUSINESS_MOBILE_SRS_v1.4.md) and [Mobile LLD v1.4](../IRANIMART_BUSINESS_MOBILE_LLD_v1.4.md)
- [Existing System Assessment](./01_EXISTING_SYSTEM_ASSESSMENT.md), [Backend API and Database Plan](./04_BACKEND_API_AND_DATABASE_PLAN.md), and [Phased Development Plan](./07_PHASED_DEVELOPMENT_PLAN.md)
- [P2-B1 Authentication and Business/Outlet Context Contract](./16_P2_B1_AUTHENTICATION_AND_CONTEXT_CONTRACT.md), [P2-B2 Secure Authentication Acceptance](./17_P2_B2_SECURE_AUTHENTICATION_ACCEPTANCE.md), and [P2-C UI and Navigation Acceptance](./18_P2_C_UI_AND_NAVIGATION_ACCEPTANCE.md)
- [P3 Catalogue Backend Readiness](./19_P3_CATALOGUE_BACKEND_READINESS.md), [A2 Inventory Location Security Acceptance](./20_A2_INVENTORY_LOCATION_SECURITY_ACCEPTANCE.md), [A2 Disposable PostgreSQL Test Environment](./21_A2_POSTGRES_TEST_ENVIRONMENT.md), and [Product Isolation Remediation Plan](./14_PRODUCT_ISOLATION_REMEDIATION_PLAN.md)
- [Authorization](../../02_Security/AUTHORIZATION.md), [Permission Model](../../02_Security/PERMISSION_MODEL.md), [Data Isolation](../../02_Security/DATA_ISOLATION.md), and [Security Checklist](../../02_Security/SECURITY_CHECKLIST.md)
