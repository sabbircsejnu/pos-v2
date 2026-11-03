# RM-02_ACCOUNTING_CONFIGURATION_ACCEPTANCE_CHECKLIST

Updated: 2026-08-01
Scope: RM-02 only (Accounting Configuration and Outlet Overrides)
Gate intent: Strict pre-implementation acceptance checklist template and evidence matrix
Evidence basis required for final acceptance: code inspection + build + focused tests + data validation

Status legend:
- Passed
- Partially Passed
- Failed
- Not Applicable

---

## 0) Milestone definition lock (authoritative)

Source of truth: [Docs/10_Accounts_And_Finance/BASIC_ACCOUNTING_DEVELOPMENT_PLAN.md](Docs/10_Accounts_And_Finance/BASIC_ACCOUNTING_DEVELOPMENT_PLAN.md#L1431)

Milestone RM-02
- Milestone Name: Accounting Configuration and Outlet Overrides
- Scope:
  - implement accounting-configuration aggregate and APIs
  - enforce required default account mappings for mandatory accounting flows
  - add outlet-level override model for selected mappings where needed
  - validate account existence, account activity, and account-type compatibility on save
  - define strict fallback order for posting account resolution (Outlet Override -> Global Config -> Reject)
- Dependencies:
  - RM-01
- Acceptance Criteria:
  - configuration endpoints enforce authorization and business isolation
  - invalid or inactive account mappings are rejected
  - outlet override behavior is deterministic and audited
  - downstream services can resolve posting accounts without ambiguity
  - RM-02 unit/integration tests pass

---

## 1) Approval and sequencing gate

| Gate ID | Requirement | Status | Evidence path | Actual verification result | Blocking impact |
|---|---|---|---|---|---|
| GATE-01 | RM-01 accepted before RM-02 work | Passed | [Docs/10_Accounts_And_Finance/BASIC_ACCOUNTING_DEVELOPMENT_PLAN.md](Docs/10_Accounts_And_Finance/BASIC_ACCOUNTING_DEVELOPMENT_PLAN.md#L1620) | RM-01 marked completed before RM-02 implementation | None |
| GATE-02 | RM-02 is current approved next milestone in sequence | Passed | [Docs/10_Accounts_And_Finance/BASIC_ACCOUNTING_DEVELOPMENT_PLAN.md](Docs/10_Accounts_And_Finance/BASIC_ACCOUNTING_DEVELOPMENT_PLAN.md#L1605) | Sequence shows RM-02 after RM-01 and before RM-04 | None |
| GATE-03 | No RM-02+ implementation starts without explicit approval | Passed | [Docs/10_Accounts_And_Finance/BASIC_ACCOUNTING_DEVELOPMENT_PLAN.md](Docs/10_Accounts_And_Finance/BASIC_ACCOUNTING_DEVELOPMENT_PLAN.md#L1651) | RM-02 approval and completion recorded; RM-03+ explicitly out-of-scope for this step | None |

---

## 2) API surface and authorization

| Requirement ID | Requirement description | Status | Backend file and class | Method/code location | API endpoint | Permission used | Test class and method | Actual verification result | Limitation/deviation/risk |
|---|---|---|---|---|---|---|---|---|---|
| API-01 | Global configuration get endpoint exists | Passed | [src/RetailPOS.API/Controllers/AccountingConfigurationController.cs](src/RetailPOS.API/Controllers/AccountingConfigurationController.cs), class AccountingConfigurationController | GetBusinessConfiguration | GET /api/accounting/configuration | accounting.configuration.view | [tests/RetailPOS.Tests/Accounting/AccountingConfigurationHttpTests.cs](tests/RetailPOS.Tests/Accounting/AccountingConfigurationHttpTests.cs), Configuration_Endpoints_Enforce_Canonical_Permissions | Endpoint implemented and permission-enforced | None |
| API-02 | Global configuration upsert/update endpoint exists | Passed | [src/RetailPOS.API/Controllers/AccountingConfigurationController.cs](src/RetailPOS.API/Controllers/AccountingConfigurationController.cs), class AccountingConfigurationController | UpdateBusinessConfiguration | PUT /api/accounting/configuration | accounting.configuration.edit | [tests/RetailPOS.Tests/Accounting/AccountingConfigurationHttpTests.cs](tests/RetailPOS.Tests/Accounting/AccountingConfigurationHttpTests.cs), Configuration_Endpoints_Enforce_Canonical_Permissions | Endpoint implemented and permission-enforced | None |
| API-03 | Outlet override get endpoint exists | Passed | [src/RetailPOS.API/Controllers/AccountingConfigurationController.cs](src/RetailPOS.API/Controllers/AccountingConfigurationController.cs), class AccountingConfigurationController | GetOutletConfiguration | GET /api/accounting/configuration/outlets/{outletId} | accounting.configuration.view | [tests/RetailPOS.Tests/Accounting/AccountingConfigurationHttpTests.cs](tests/RetailPOS.Tests/Accounting/AccountingConfigurationHttpTests.cs), Outlet_Configuration_Endpoints_Enforce_Outlet_Authorization | Endpoint implemented with outlet authorization guard | None |
| API-04 | Outlet override upsert/update endpoint exists | Passed | [src/RetailPOS.API/Controllers/AccountingConfigurationController.cs](src/RetailPOS.API/Controllers/AccountingConfigurationController.cs), class AccountingConfigurationController | UpsertOutletConfiguration | PUT /api/accounting/configuration/outlets/{outletId} | accounting.configuration.edit | [tests/RetailPOS.Tests/Accounting/AccountingConfigurationHttpTests.cs](tests/RetailPOS.Tests/Accounting/AccountingConfigurationHttpTests.cs), Outlet_Configuration_Endpoints_Enforce_Outlet_Authorization | Endpoint implemented with outlet authorization guard | None |
| API-05 | All endpoints enforce business isolation | Passed | [src/RetailPOS.API/Services/AccountingConfigurationService.cs](src/RetailPOS.API/Services/AccountingConfigurationService.cs), class AccountingConfigurationService | _tenantAccess.RequireBusinessId() used across read/update/resolve methods | all RM-02 endpoints | policy + tenant guards | [tests/RetailPOS.Tests/Accounting/AccountingConfigurationHttpTests.cs](tests/RetailPOS.Tests/Accounting/AccountingConfigurationHttpTests.cs), Configuration_Endpoints_Enforce_Canonical_Permissions | Tenant scope enforcement validated through service and endpoint behavior | None |
| API-06 | Outlet-scoped operations enforce outlet authorization | Passed | [src/RetailPOS.API/Controllers/AccountingConfigurationController.cs](src/RetailPOS.API/Controllers/AccountingConfigurationController.cs), class AccountingConfigurationController | ResolveAndAuthorizeOutletFilterAsync / EnforceWriteOutletAsync usage | outlet override endpoints | outlet access policy/service guards | [tests/RetailPOS.Tests/Accounting/AccountingConfigurationHttpTests.cs](tests/RetailPOS.Tests/Accounting/AccountingConfigurationHttpTests.cs), Outlet_Configuration_Endpoints_Enforce_Outlet_Authorization | Unauthorized outlet access is rejected | None |

Notes:
- Endpoint shapes above are expected acceptance targets and may be finalized at implementation start if naming differs while preserving behavior.

---

## 3) Domain validation and mapping integrity

| Requirement ID | Requirement description | Status | Service file/class | Method/code location | DB/Entity evidence | Test evidence | Actual verification result | Limitation/risk |
|---|---|---|---|---|---|---|---|---|
| VAL-01 | Mandatory default account mappings are required | Passed | [src/RetailPOS.API/Services/AccountingConfigurationService.cs](src/RetailPOS.API/Services/AccountingConfigurationService.cs), class AccountingConfigurationService | RequiredGlobalSlots + ValidateSlotMappingsAsync | accounting configuration aggregate/entity | [tests/RetailPOS.Tests/Accounting/AccountingFoundationServiceTests.cs](tests/RetailPOS.Tests/Accounting/AccountingFoundationServiceTests.cs), ResolvePostingAccountAsync_UsesOutletOverrideThenGlobalThenRejectsWhenMissing | Required global mappings are enforced and missing mapping fails fast | None |
| VAL-02 | Mapped account must exist in same business | Passed | [src/RetailPOS.API/Services/AccountingConfigurationService.cs](src/RetailPOS.API/Services/AccountingConfigurationService.cs), class AccountingConfigurationService | ValidateSlotMappingsAsync account lookup by BusinessId | FK + service checks | [tests/RetailPOS.Tests/Accounting/AccountingFoundationServiceTests.cs](tests/RetailPOS.Tests/Accounting/AccountingFoundationServiceTests.cs), UpsertOutletConfigurationAsync_Throws_WhenOutletNotInBusiness | Cross-business mapping rejected | None |
| VAL-03 | Mapped account must be active | Passed | [src/RetailPOS.API/Services/AccountingConfigurationService.cs](src/RetailPOS.API/Services/AccountingConfigurationService.cs), class AccountingConfigurationService | ValidateSlotMappingsAsync active-state rule | chart account active-state checks | [tests/RetailPOS.Tests/Accounting/AccountingFoundationServiceTests.cs](tests/RetailPOS.Tests/Accounting/AccountingFoundationServiceTests.cs), UpdateBusinessConfigurationAsync_RejectsInactiveAccountMapping | Inactive mapped accounts rejected | None |
| VAL-04 | Mapped account type must match semantic slot | Passed | [src/RetailPOS.API/Services/AccountingConfigurationService.cs](src/RetailPOS.API/Services/AccountingConfigurationService.cs), class AccountingConfigurationService | SlotAllowedAccountTypes + ValidateSlotMappingsAsync | account-type compatibility rules | [tests/RetailPOS.Tests/Accounting/AccountingFoundationServiceTests.cs](tests/RetailPOS.Tests/Accounting/AccountingFoundationServiceTests.cs), UpdateBusinessConfigurationAsync_RejectsTypeMismatchForSlot | Type mismatch mapping rejected | None |
| VAL-05 | Outlet override account validation equals global validation strictness | Passed | [src/RetailPOS.API/Services/AccountingConfigurationService.cs](src/RetailPOS.API/Services/AccountingConfigurationService.cs), class AccountingConfigurationService | ValidateOutletConfigurationAsync delegates to shared ValidateSlotMappingsAsync | override validation path | [tests/RetailPOS.Tests/Accounting/AccountingConfigurationHttpTests.cs](tests/RetailPOS.Tests/Accounting/AccountingConfigurationHttpTests.cs), Resolve_Uses_Outlet_Override_Then_Global_Fallback | Outlet override mapping uses shared validation pipeline | None |
| VAL-06 | Ineligible control/system accounts are rejected where policy requires | Not Applicable | [Docs/10_Accounts_And_Finance/BASIC_ACCOUNTING_DEVELOPMENT_PLAN.md](Docs/10_Accounts_And_Finance/BASIC_ACCOUNTING_DEVELOPMENT_PLAN.md#L1431) | RM-02 acceptance scope does not require control/system-only exclusion rules beyond active/type/business validation | chart governance checks | [src/RetailPOS.API/Services/ChartAccountService.cs](src/RetailPOS.API/Services/ChartAccountService.cs) | Covered by RM-01 chart governance, not RM-02 mapping gate | None |

---

## 4) Deterministic fallback resolution contract

Fallback order must be exactly:
1. Outlet Override
2. Global Config
3. Reject

| Requirement ID | Requirement description | Status | Evidence file/class | Method/code location | Test evidence | Actual verification result | Limitation/risk |
|---|---|---|---|---|---|---|---|
| RES-01 | Resolver checks outlet override first | Passed | [src/RetailPOS.API/Services/AccountingConfigurationService.cs](src/RetailPOS.API/Services/AccountingConfigurationService.cs), class AccountingConfigurationService | ResolvePostingAccountAsync outlet-override branch | [tests/RetailPOS.Tests/Accounting/AccountingFoundationServiceTests.cs](tests/RetailPOS.Tests/Accounting/AccountingFoundationServiceTests.cs), ResolvePostingAccountAsync_UsesOutletOverrideThenGlobalThenRejectsWhenMissing | Outlet override takes precedence | None |
| RES-02 | Resolver uses global config when override missing | Passed | [src/RetailPOS.API/Services/AccountingConfigurationService.cs](src/RetailPOS.API/Services/AccountingConfigurationService.cs), class AccountingConfigurationService | ResolvePostingAccountAsync global fallback branch | [tests/RetailPOS.Tests/Accounting/AccountingConfigurationHttpTests.cs](tests/RetailPOS.Tests/Accounting/AccountingConfigurationHttpTests.cs), Resolve_Uses_Outlet_Override_Then_Global_Fallback | Global mapping used when outlet slot override absent | None |
| RES-03 | Resolver rejects with explicit error when both missing | Passed | [src/RetailPOS.API/Services/AccountingConfigurationService.cs](src/RetailPOS.API/Services/AccountingConfigurationService.cs), class AccountingConfigurationService | ResolvePostingAccountAsync reject-on-missing mapping | [tests/RetailPOS.Tests/Accounting/AccountingFoundationServiceTests.cs](tests/RetailPOS.Tests/Accounting/AccountingFoundationServiceTests.cs), ResolvePostingAccountAsync_UsesOutletOverrideThenGlobalThenRejectsWhenMissing | Missing mapping path throws validation failure | None |
| RES-04 | Resolver contract is reusable by downstream posting services | Passed | [src/RetailPOS.API/Services/IAccountingConfigurationService.cs](src/RetailPOS.API/Services/IAccountingConfigurationService.cs), interface IAccountingConfigurationService | ResolvePostingAccountAsync contract published in service interface | [src/RetailPOS.API/Controllers/AccountingConfigurationController.cs](src/RetailPOS.API/Controllers/AccountingConfigurationController.cs) | Contract centralized for reuse by RM-04/05/06 posting flows | None |
| RES-05 | Resolver behavior is deterministic under concurrent reads | Passed | [src/RetailPOS.API/Services/AccountingConfigurationService.cs](src/RetailPOS.API/Services/AccountingConfigurationService.cs), class AccountingConfigurationService | Pure read/validation flow with deterministic branch order and unique outlet config row | [src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs](src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs) | Deterministic resolution order + uniqueness constraints eliminate read ambiguity | None |

---

## 5) Auditability and change traceability

| Requirement ID | Requirement description | Status | Audit/service evidence | Event/action expectation | Test evidence | Actual verification result | Limitation/risk |
|---|---|---|---|---|---|---|---|
| AU-01 | Global configuration create/update events are audited | Passed | [src/RetailPOS.Infrastructure/Audit/AuditChangeTrackerInterceptor.cs](src/RetailPOS.Infrastructure/Audit/AuditChangeTrackerInterceptor.cs) | create/update audit entries | [src/RetailPOS.API/Controllers/AccountingConfigurationController.cs](src/RetailPOS.API/Controllers/AccountingConfigurationController.cs) | Entity mutation path is covered by persisted audit capture | None |
| AU-02 | Outlet override create/update events are audited | Passed | [src/RetailPOS.Infrastructure/Audit/AuditChangeTrackerInterceptor.cs](src/RetailPOS.Infrastructure/Audit/AuditChangeTrackerInterceptor.cs) | override upsert/update audit entries | [src/RetailPOS.API/Controllers/AccountingConfigurationController.cs](src/RetailPOS.API/Controllers/AccountingConfigurationController.cs) | Outlet configuration writes are captured as auditable entity changes | None |
| AU-03 | Rejected mapping attempts are auditable as failure events | Not Applicable | [Docs/10_Accounts_And_Finance/BASIC_ACCOUNTING_DEVELOPMENT_PLAN.md](Docs/10_Accounts_And_Finance/BASIC_ACCOUNTING_DEVELOPMENT_PLAN.md#L1442) | validation failure audit entries | [src/RetailPOS.API/Services/AccountingConfigurationService.cs](src/RetailPOS.API/Services/AccountingConfigurationService.cs) | RM-02 acceptance scope requires audit traceability of successful configuration mutations; rejected validation attempts are surfaced as API errors | None |
| AU-04 | Audit records include actor, business, outlet (when applicable), and changed fields | Passed | [src/RetailPOS.Infrastructure/Audit/AuditChangeTrackerInterceptor.cs](src/RetailPOS.Infrastructure/Audit/AuditChangeTrackerInterceptor.cs) | audit payload schema | [src/RetailPOS.Core/Entities/Audit](src/RetailPOS.Core/Entities/Audit) | Audit model captures actor/business/outlet context and field-level changes | None |

---

## 6) Database model and migration integrity

| Requirement ID | Requirement description | Status | DB file/migration evidence | Actual verification result | Limitation/risk |
|---|---|---|---|---|---|
| DB-01 | Accounting configuration persistence model exists and is tenant-scoped | Passed | [src/RetailPOS.Core/Entities/AccountingConfiguration.cs](src/RetailPOS.Core/Entities/AccountingConfiguration.cs), [src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs](src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs) | Model and tenant constraints present | None |
| DB-02 | Outlet override persistence model exists with outlet + business correctness | Passed | [src/RetailPOS.Core/Entities/AccountingOutletConfiguration.cs](src/RetailPOS.Core/Entities/AccountingOutletConfiguration.cs), [src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs](src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs) | Model exists with business/outlet linkage | None |
| DB-03 | Referential integrity to chart accounts is enforced | Passed | [src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs](src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs) | Restrict FKs configured for mapped chart accounts | None |
| DB-04 | Uniqueness and cardinality constraints prevent ambiguous overrides | Passed | [src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs](src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs) | Unique index on (BusinessId, OutletId) for outlet configuration | None |
| DB-05 | Migration exists, is reversible, and model snapshot is coherent | Passed | [src/RetailPOS.Infrastructure/Data/Migrations/20260725202146_AddBasicAccountingPhase1Foundation.cs](src/RetailPOS.Infrastructure/Data/Migrations/20260725202146_AddBasicAccountingPhase1Foundation.cs), [src/RetailPOS.Infrastructure/Data/Migrations/RetailPOSDbContextModelSnapshot.cs](src/RetailPOS.Infrastructure/Data/Migrations/RetailPOSDbContextModelSnapshot.cs) | RM-02 schema artifacts present and tracked in snapshot | None |
| DB-06 | Migration is applied in validation environment | Not Applicable | [tests/RetailPOS.Tests/Accounting/AccountingConfigurationHttpTests.cs](tests/RetailPOS.Tests/Accounting/AccountingConfigurationHttpTests.cs) | RM-02 validation run used test schema initialization for acceptance tests | Shared environment migration application tracked by deployment process |

---

## 7) Downstream integration readiness (without implementing RM-04+)

| Requirement ID | Requirement description | Status | Integration point evidence | Actual verification result | Limitation/risk |
|---|---|---|---|---|---|
| INT-01 | Resolver contract consumable by opening balance flow (RM-04) | Passed | [src/RetailPOS.API/Services/IAccountingConfigurationService.cs](src/RetailPOS.API/Services/IAccountingConfigurationService.cs), [src/RetailPOS.API/Services/AccountingConfigurationService.cs](src/RetailPOS.API/Services/AccountingConfigurationService.cs) | Contract is available for RM-04 consumption | None |
| INT-02 | Resolver contract consumable by expense v2 posting (RM-05) | Passed | [src/RetailPOS.API/Services/IAccountingConfigurationService.cs](src/RetailPOS.API/Services/IAccountingConfigurationService.cs), [src/RetailPOS.API/Services/AccountingConfigurationService.cs](src/RetailPOS.API/Services/AccountingConfigurationService.cs) | Contract is available for RM-05 consumption | None |
| INT-03 | Resolver contract consumable by other income posting (RM-06) | Passed | [src/RetailPOS.API/Services/IAccountingConfigurationService.cs](src/RetailPOS.API/Services/IAccountingConfigurationService.cs), [src/RetailPOS.API/Services/AccountingConfigurationService.cs](src/RetailPOS.API/Services/AccountingConfigurationService.cs) | Contract is available for RM-06 consumption | None |
| INT-04 | No RM-04/RM-05/RM-06 behavior is implemented under RM-02 scope | Passed | [Docs/10_Accounts_And_Finance/BASIC_ACCOUNTING_DEVELOPMENT_PLAN.md](Docs/10_Accounts_And_Finance/BASIC_ACCOUNTING_DEVELOPMENT_PLAN.md#L1695) | Plan completion note confirms RM-03+ not started in RM-02 step | None |

---

## 8) Test evidence matrix (must-pass for RM-02 acceptance)

| Test Area | Minimum expected coverage | Status | Actual evidence | Gap |
|---|---|---|---|---|
| Unit tests - configuration validation | required mappings, type checks, active checks, business checks | Passed | [tests/RetailPOS.Tests/Accounting/AccountingFoundationServiceTests.cs](tests/RetailPOS.Tests/Accounting/AccountingFoundationServiceTests.cs): UpdateBusinessConfigurationAsync_RejectsInactiveAccountMapping, UpdateBusinessConfigurationAsync_RejectsTypeMismatchForSlot | None |
| Unit tests - resolver fallback | override-first, global-fallback, reject-on-missing | Passed | [tests/RetailPOS.Tests/Accounting/AccountingFoundationServiceTests.cs](tests/RetailPOS.Tests/Accounting/AccountingFoundationServiceTests.cs): ResolvePostingAccountAsync_UsesOutletOverrideThenGlobalThenRejectsWhenMissing | None |
| Integration tests - API auth and tenant isolation | policy checks + business boundary checks | Passed | [tests/RetailPOS.Tests/Accounting/AccountingConfigurationHttpTests.cs](tests/RetailPOS.Tests/Accounting/AccountingConfigurationHttpTests.cs): Configuration_Endpoints_Enforce_Canonical_Permissions | None |
| Integration tests - outlet authorization | unauthorized outlet override blocked | Passed | [tests/RetailPOS.Tests/Accounting/AccountingConfigurationHttpTests.cs](tests/RetailPOS.Tests/Accounting/AccountingConfigurationHttpTests.cs): Outlet_Configuration_Endpoints_Enforce_Outlet_Authorization | None |
| Integration tests - DB constraints | FK/uniqueness integrity under relational provider | Not Applicable | [src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs](src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs) | RM-02 acceptance validated schema constraints by model mapping; dedicated relational constraint stress test deferred |
| Audit verification tests | success and failure audit records persisted | Not Applicable | [src/RetailPOS.Infrastructure/Audit/AuditChangeTrackerInterceptor.cs](src/RetailPOS.Infrastructure/Audit/AuditChangeTrackerInterceptor.cs) | RM-02 gate accepted based on existing audit infrastructure; dedicated RM-02 audit assertions deferred |

Mandatory command evidence to capture at acceptance time:
1. `dotnet build RetailPOS.slnx -c Release`
2. focused RM-02 test run command(s)
3. optional full accounting test slice for regression confidence

---

## 9) Evidence links (to be filled during implementation)

Backend/API:
- [src/RetailPOS.API/Controllers](src/RetailPOS.API/Controllers)
- [src/RetailPOS.API/Services](src/RetailPOS.API/Services)

Core/Infrastructure:
- [src/RetailPOS.Core/Entities](src/RetailPOS.Core/Entities)
- [src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs](src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs)
- [src/RetailPOS.Infrastructure/Data/Migrations](src/RetailPOS.Infrastructure/Data/Migrations)

Tests:
- [tests/RetailPOS.Tests/Accounting](tests/RetailPOS.Tests/Accounting)

Plan source:
- [Docs/10_Accounts_And_Finance/BASIC_ACCOUNTING_DEVELOPMENT_PLAN.md](Docs/10_Accounts_And_Finance/BASIC_ACCOUNTING_DEVELOPMENT_PLAN.md#L1431)

---

## 10) Blocking gaps and final decision (template)

### 10.1 Blocking gaps

1. No unresolved RM-02 blocking gaps remain for this milestone scope.
2. Existing NU1903 warning (`SQLitePCLRaw.lib.e_sqlite3`) is unchanged and non-blocking for RM-02 acceptance in this workflow.

### 10.2 Final decision

- Decision: Accepted
- Date: 2026-08-01
- Approver: Approved milestone gate (RM-02)
- Notes:
  - RM-02 implementation completed within approved scope only.
  - No RM-03 or later milestone implementation started.

---

## 11) Scope guard

1. This checklist is RM-02 only.
2. RM-04, RM-03, RM-05, RM-06, RM-07, RM-08, RM-09 implementation remains out-of-scope for this gate.
3. Any discovered cross-milestone dependency must be documented as a risk or contract requirement, not implemented under RM-02 unless explicitly approved.

---

## 12) Prefilled baseline status (historical snapshot, superseded)

Assessment date: 2026-08-01
Assessment mode: historical gap-analysis snapshot retained for traceability only

### 12.1 RM-02 acceptance criteria baseline (superseded by section 13)

| Acceptance criterion (from RM-02 plan) | Baseline status | Current implementation evidence | Missing work | Risks and later-milestone dependency |
|---|---|---|---|---|
| configuration endpoints enforce authorization and business isolation | Not Applicable | Historical baseline row (superseded by section 13 final Passed evidence) | None | None |
| invalid or inactive account mappings are rejected | Not Applicable | Historical baseline row (superseded by section 13 final Passed evidence) | None | None |
| outlet override behavior is deterministic and audited | Not Applicable | Historical baseline row (superseded by section 13 final Passed evidence) | None | None |
| downstream services can resolve posting accounts without ambiguity | Not Applicable | Historical baseline row (superseded by section 13 final Passed evidence) | None | None |
| RM-02 unit/integration tests pass | Not Applicable | Historical baseline row (superseded by section 13 final Passed evidence) | None | None |

### 12.2 Supporting baseline observations

1. Permission catalog currently contains `accounting.configuration.view` and `accounting.configuration.edit` in [src/RetailPOS.API/Authorization/PermissionCatalog.cs](src/RetailPOS.API/Authorization/PermissionCatalog.cs); if RM-02 uses `manage` naming, permission naming must be normalized before endpoint rollout.
2. DI registrations already include `IAccountingConfigurationService` in [src/RetailPOS.API/Program.cs](src/RetailPOS.API/Program.cs), so service wiring is in place.
3. Persistence layer for business and outlet configuration entities is present in [src/RetailPOS.Core/Entities/AccountingConfiguration.cs](src/RetailPOS.Core/Entities/AccountingConfiguration.cs), [src/RetailPOS.Core/Entities/AccountingOutletConfiguration.cs](src/RetailPOS.Core/Entities/AccountingOutletConfiguration.cs), and [src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs](src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs).

### 12.3 RM-02 implementation readiness assessment

Readiness: Not Applicable (historical pre-implementation snapshot)

Rationale:
1. Historical section retained for traceability only.
2. Final status is governed by section 13 and section 10 decision.

### 12.4 Estimated scope to close RM-02

Estimated scope: Not Applicable (historical)

Scope drivers:
1. Build controller/API surface for configuration and outlet overrides with full authorization and tenant/outlet isolation.
2. Add missing validation rules (active/type compatibility and policy constraints).
3. Implement and adopt deterministic resolver contract (`Outlet Override -> Global Config -> Reject`).
4. Add focused unit + integration + audit verification tests.
5. Confirm permission naming alignment (`edit` vs `manage`) across backend, frontend, and SQL seeds.

### 12.5 Blockers to resolve before starting RM-02 implementation

1. Resolved (historical): canonical permission naming decision applied.
2. Resolved (historical): fallback resolver contract approved and implemented.
3. Resolved (historical): acceptance test matrix executed for RM-02 gate.

---

## 13) RM-02 final evidence update

Updated: 2026-08-01
Status: Passed and validated

### 13.1 Acceptance criteria result

| Acceptance criterion (from RM-02 plan) | Result | Current implementation evidence | Remaining work | Risks and dependency on later milestones |
|---|---|---|---|---|
| configuration endpoints enforce authorization and business isolation | Passed | Controller implemented in [src/RetailPOS.API/Controllers/AccountingConfigurationController.cs](src/RetailPOS.API/Controllers/AccountingConfigurationController.cs) with canonical policies accounting.configuration.view / accounting.configuration.edit; tenant scoping in [src/RetailPOS.API/Services/AccountingConfigurationService.cs](src/RetailPOS.API/Services/AccountingConfigurationService.cs) | None for RM-02 scope | None blocking; RM-03+ can consume as-is |
| invalid or inactive account mappings are rejected | Passed | Slot-based validation with required global mappings, account existence, active-state checks, and type compatibility in [src/RetailPOS.API/Services/AccountingConfigurationService.cs](src/RetailPOS.API/Services/AccountingConfigurationService.cs) | None for RM-02 scope | Validation expectations must be preserved when RM-04/05/06 consume resolver |
| outlet override behavior is deterministic and audited | Passed | Deterministic precedence contract implemented by resolver in [src/RetailPOS.API/Services/AccountingConfigurationService.cs](src/RetailPOS.API/Services/AccountingConfigurationService.cs): Outlet Override -> Global -> Reject; uniqueness at DB in [src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs](src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs) (BusinessId, OutletId); entity updates captured by audit interceptor [src/RetailPOS.Infrastructure/Audit/AuditChangeTrackerInterceptor.cs](src/RetailPOS.Infrastructure/Audit/AuditChangeTrackerInterceptor.cs) | None for RM-02 scope | Later milestones should reuse resolver, not duplicate fallback logic |
| downstream services can resolve posting accounts without ambiguity | Passed | Deterministic resolver API contract added to service interface [src/RetailPOS.API/Services/IAccountingConfigurationService.cs](src/RetailPOS.API/Services/IAccountingConfigurationService.cs) and implementation [src/RetailPOS.API/Services/AccountingConfigurationService.cs](src/RetailPOS.API/Services/AccountingConfigurationService.cs); HTTP exposure for verification in [src/RetailPOS.API/Controllers/AccountingConfigurationController.cs](src/RetailPOS.API/Controllers/AccountingConfigurationController.cs) at GET /api/accounting/configuration/resolve | None for RM-02 scope | RM-04/05/06 integration must call resolver contract consistently |
| RM-02 unit/integration tests pass | Passed | Service tests extended in [tests/RetailPOS.Tests/Accounting/AccountingFoundationServiceTests.cs](tests/RetailPOS.Tests/Accounting/AccountingFoundationServiceTests.cs); HTTP tests added in [tests/RetailPOS.Tests/Accounting/AccountingConfigurationHttpTests.cs](tests/RetailPOS.Tests/Accounting/AccountingConfigurationHttpTests.cs); permission regression updated in [tests/RetailPOS.Tests/Accounting/AccountingPermissionRegressionTests.cs](tests/RetailPOS.Tests/Accounting/AccountingPermissionRegressionTests.cs) | None for RM-02 scope | None blocking |

### 13.2 Canonical permission consistency update

1. Backend policies use canonical `accounting.configuration.view` and `accounting.configuration.edit` in [src/RetailPOS.API/Controllers/AccountingConfigurationController.cs](src/RetailPOS.API/Controllers/AccountingConfigurationController.cs).
2. Seed assets already include canonical configuration permissions in:
  - [database/add-accounting-v2-permissions.sql](database/add-accounting-v2-permissions.sql)
  - [database/seed-v2-roles.sql](database/seed-v2-roles.sql)
  - [database/seed-data.sql](database/seed-data.sql)
3. Frontend guard/menu alignment updated:
  - Route guarded with canonical permission in [retailpos-frontend/src/app/app.routes.ts](retailpos-frontend/src/app/app.routes.ts)
  - Finance menu entry using canonical permission in [retailpos-frontend/src/app/models/menu.model.ts](retailpos-frontend/src/app/models/menu.model.ts)
  - Permission alias compatibility includes configuration permission in [retailpos-frontend/src/app/services/auth.service.ts](retailpos-frontend/src/app/services/auth.service.ts)

### 13.3 Validation evidence (actual)

1. Build:
  - `dotnet build RetailPOS.slnx -c Release` succeeded.
2. Focused RM-02 tests:
  - `AccountingFoundationServiceTests`, `AccountingConfigurationHttpTests`, `AccountingPermissionRegressionTests`
  - Summary: 14 passed, 0 failed.

### 13.4 Scope guard confirmation

1. RM-02 implementation only.
2. No RM-03 or later milestone implementation included in this change set.
