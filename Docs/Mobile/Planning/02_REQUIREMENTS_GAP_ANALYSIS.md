# Requirements Gap Analysis

**Normative inputs:** SRS v1.4 and LLD v1.4.  
**Status:** current-state statements are marked **VERIFIED**, **UNKNOWN**, or **BLOCKED**; proposed capability is **PROPOSED**. LLD endpoint/schema examples are design inputs, not deployed APIs.

## Capability gaps

| Requirement area | Existing evidence | Gap / implementation implication | Status |
|---|---|---|---|
| AUTH — sign-in, refresh, session and role access | ERP login/refresh/me/logout and permission claims exist. | Native public-client flow and mobile token lifecycle are not approved; add mobile-specific secure session behavior only after auth ADR. Never embed a client secret. | **VERIFIED / BLOCKED** |
| AUTH — business/outlet context | Tenant and outlet access services exist. | Audit controller-by-controller scoping; define context switch and selection contract for a user with multiple authorized outlets. | **VERIFIED / BLOCKED** |
| DASH — role-based dashboards | Sales summary/trend/top-product/outlet/payment reports and Angular dashboard exist. | Mobile API should compose authorized metrics without duplicating calculation; owner, manager and salesperson metric definitions/targets are incomplete or unverified. | **VERIFIED / UNKNOWN** |
| STK — session, scan and count workflow | ERP stock-count aggregate/workflow and barcode lookup exist. | No offline session/revision pinning, durable client operations, operation idempotency/partial receipts, unknown-barcode quarantine or offline conflict protocol. | **VERIFIED (gap)** |
| SYNC — incremental/resumable sync | Durable integration delivery foundations exist for other event families. | No catalogue revision/delta/tombstone/cursor API or mobile count batch acknowledgement protocol. A transport outbox is not a replacement for client operation idempotency. | **VERIFIED (gap)** |
| CAT — identity and attributes | `Product.ProductCode` is documented and used as the master product code; `ProductVariant.ProductId` is a required parent FK. Variation-option joins and JSONB attributes both exist. Product and variant barcodes have separate uniqueness constraints. | Reuse ProductCode as Main Product Code; do not add a duplicate column. Audit nullable legacy codes, join/JSONB consistency, product ownership/scope and cross-table barcode collisions. Only unique eligible variant barcodes resolve to VariantId; parent product barcode is not an exact variant. | **VERIFIED / BLOCKED** |
| PROD — product browsing/detail/search | Existing product/variant search and lookup endpoints exist. | Mobile-safe list/detail DTOs, search semantics, permissions, cache policy and bounded payloads need contract definition. | **VERIFIED / PROPOSED** |
| INS — sales insights/profitability | Sales reports and permission-protected accounting reports exist. | Clarify metric definitions, attribution, discount treatment, cost history and allowed mobile roles. Do not infer profit where the user lacks permission. | **VERIFIED / UNKNOWN** |
| FCST — forecasting | No feature found. Historical sales/inventory records exist. | Define outcomes/horizons, training data quality, stock-out treatment, validation baseline and role access. Keep historical facts, statistical predictions and generated explanations distinct. | **VERIFIED (gap) / UNKNOWN** |
| NOT — notifications | Subscription reminder entity and generic durable outbox/worker infrastructure exist. | No verified configurable rule catalogue, shared inbox/read state or complete business-event producer pipeline. Build a shared module, not a separate service by default. | **VERIFIED (gap)** |
| ARCH — architecture/security/non-functional | Modular .NET/Angular patterns and audit foundations exist. | Specify versioned mobile API, telemetry/privacy, offline threat model, storage encryption, device revocation, retention, migration strategy and mobile CI/release. | **VERIFIED / PROPOSED** |
| Figma/brand | Shared Make brief exposes design tokens and screen inventory; published prototype provides limited visual evidence. Product owner states official logos were supplied, but the files were not found in the repository. | Place approved owner-provided assets in proposed `retailpos-mobile/assets/brand/`; seek asset handoff and visual approval for every unverified screen. Do not infer layouts from prompt text. | **VERIFIED brief/owner statement / PENDING approval** |

## Requirement-level decisions and validation

| Requirement cluster | Required evidence before implementation | Planned validation |
|---|---|---|
| Authentication and authorization (AUTH-*) | Approved native-client flow; token scopes/claims/lifetime/rotation/revocation; endpoint-to-role matrix; business/outlet isolation review. | Auth integration tests for login/refresh/logout/revocation; negative cross-business/outlet tests; mobile secure-storage and navigation tests. |
| Dashboard and metrics (DASH-*) | Metric dictionary and role matrix; existing report reuse map; data freshness and time-zone rules. | API contract tests against reference queries; role and scope tests; golden metric fixtures. |
| Stock take (STK-*) | Stable product/variant IDs, main-code semantics, session snapshot rules, offline lease, unknown-barcode handling, duplicate scan and conflict rules. | Domain/property tests; unique operation replay tests; concurrent submission tests; device offline/restore tests; ERP ledger reconciliation. |
| Sync (SYNC-*) | Revision source, snapshot/delta ordering, tombstone semantics, cursor expiry and resumability; acknowledgement retention. | Randomized delta-vs-full-snapshot equivalence; interrupted page/batch resume; duplicate and out-of-order delivery; corrupted local DB recovery. |
| Product catalogue (CAT/PROD-*) | Authoritative tenant visibility, identifier uniqueness, barcode normalization and dynamic attribute schema. | Import/data-quality audit; cross-table uniqueness tests; old-client/new-server compatibility tests. |
| Insights/forecast (INS/FCST-*) | Metric definitions, cost coverage, data readiness report and prediction evaluation criteria. | Reconciliation to ERP source for facts; time-based backtesting for forecasts; no leakage tests; explanation provenance checks. |
| Notifications (NOT-*) | Complete type→producer→condition→recipient→inbox matrix; transaction boundary; recipient scope; dedupe/retry/dead-letter/replay policy. | Same-transaction rollback tests; rule boundary tests; dedupe/retry/dead-letter/redrive; recipient isolation and read-sync tests. |
| Release/non-functional (ARCH/NFR-*) | Supported Android/device policy, privacy/retention, crash/telemetry, signing and rollout/rollback. | Release pipeline, security review, offline stress, accessibility, upgrade/restore, staged rollout and monitoring gates. |

## Explicit missing or unproven capabilities

- **VERIFIED missing:** mobile catalogue snapshot/delta API, catalogue revision pinning contract, offline operation receipt protocol, mobile notification inbox/configuration, forecasting feature, official stock-count PDF export.
- **VERIFIED partial:** product/variant/barcode/dynamic-attribute data; ERP stock-count lifecycle/export; reports; generic outbox/audit foundations.
- **VERIFIED:** `Product.ProductCode` is the existing Main Product Code semantic; `ProductVariant.ProductId` is the required parent relationship. **UNKNOWN/BLOCKED:** legacy ProductCode quality, business/outlet catalogue visibility, cross-table barcode collisions, and consistency between JSONB attributes and variation-option joins. Sales-target data, historical cost completeness, forecast readiness and business notification event completeness remain unknown. Official logos are confirmed as owner-supplied but source files are not yet located in the repository.
- **BLOCKED:** design and implementation of catalogue sync until product identity/scope decisions are approved; mobile sign-in until native public-client flow is approved; offline submission until lease/baseline/concurrency rules and durable server receipts are specified.

## Scope guardrails

1. Keep ERP product, sale, stock ledger, count approval and financial calculations authoritative.
2. Mobile may capture offline counts and preserve immutable operation evidence; it must not directly post stock or invent inventory corrections.
3. All role-specific values remain backed by shared server-owned metric definitions and permission checks.
4. Notifications are a shared web/mobile capability; avoid duplicating user-facing notifications or deploying a standalone service absent measured need.
5. Forecasts are not historical facts. Label prediction horizon/model/version and keep any AI explanation optional and non-authoritative.
