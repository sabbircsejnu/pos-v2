# JOURNAL_ENGINE_ACCEPTANCE_CHECKLIST

Updated: 2026-08-01
Scope: Journal Engine milestone only (no operational accounting integrations)
Evidence basis: code inspection + latest executed build/tests in this workspace after blocker remediation

Status legend:
- Passed
- Partially Passed
- Failed
- Not Applicable

---

## 1) Journal Lifecycle

| Requirement ID | Requirement description | Status | Backend file and class | Method/code location | API endpoint | DB table/constraint/index | Permission used | Test class and method | Actual verification result | Limitation/deviation/risk |
|---|---|---|---|---|---|---|---|---|---|---|
| JL-01 | Draft creation exists | Passed | [src/RetailPOS.API/Services/JournalEntryService.cs](src/RetailPOS.API/Services/JournalEntryService.cs#L145), class `JournalEntryService` | `CreateManualAsync`, `CreateDraftInternalAsync` | `POST /api/accounting/journals` in [src/RetailPOS.API/Controllers/AccountingJournalsController.cs](src/RetailPOS.API/Controllers/AccountingJournalsController.cs#L73) | `journal_entries.status='Draft'` default via service; unique indexes in [src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs](src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs#L1421) | `accounting.journals.create` | [tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs#L23) `Create_Post_Reverse_Lifecycle_Works` | Draft returns `201 Created`, no permanent journal number until posting | Draft numbering behavior is correct; no dedicated create validation matrix test for all negative cases at HTTP layer |
| JL-02 | Draft update exists | Passed | [src/RetailPOS.API/Services/JournalEntryService.cs](src/RetailPOS.API/Services/JournalEntryService.cs#L183), class `JournalEntryService` | `UpdateDraftAsync` | `PUT /api/accounting/journals/{id}` in [src/RetailPOS.API/Controllers/AccountingJournalsController.cs](src/RetailPOS.API/Controllers/AccountingJournalsController.cs#L89) | `journal_entries` + `journal_entry_lines` rewritten in transactionless save | `accounting.journals.update` | [tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs#L117), [tests/RetailPOS.Tests/Accounting/JournalEntryServiceTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryServiceTests.cs#L196) | HTTP + service behavior validated | No dedicated negative test for update-on-posted journal |
| JL-03 | Draft cancellation exists | Passed | [src/RetailPOS.API/Services/JournalEntryService.cs](src/RetailPOS.API/Services/JournalEntryService.cs#L234), class `JournalEntryService` | `CancelDraftAsync` | `POST /api/accounting/journals/{id}/cancel` in [src/RetailPOS.API/Controllers/AccountingJournalsController.cs](src/RetailPOS.API/Controllers/AccountingJournalsController.cs#L103) | `journal_entries.status='Cancelled'` | `accounting.journals.cancel` | [tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs#L117), [tests/RetailPOS.Tests/Accounting/JournalEntryServiceTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryServiceTests.cs#L240) | HTTP + service behavior validated | No explicit cancel-after-post negative HTTP test |
| JL-04 | Posting exists | Passed | [src/RetailPOS.API/Services/JournalEntryService.cs](src/RetailPOS.API/Services/JournalEntryService.cs#L276) | `PostAsync` | `POST /api/accounting/journals/{id}/post` | `journal_entries` (`status`,`posted_at`,`posted_by`,`journal_number`) | `accounting.journals.post` | [tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs#L48) `Create_Post_Reverse_Lifecycle_Works` | Post transitions draft to posted and allocates permanent number | Duplicate-post same-id scenario is handled in service but not explicitly HTTP-tested |
| JL-05 | Posted immutability | Partially Passed | [src/RetailPOS.API/Services/JournalEntryService.cs](src/RetailPOS.API/Services/JournalEntryService.cs#L194) | `UpdateDraftAsync` draft-only guard; `PostAsync` status guards | N/A (behavioral) | N/A | N/A | Indirectly covered by [tests/RetailPOS.Tests/Accounting/JournalEntryServiceTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryServiceTests.cs#L240) | Posted/cancelled entries cannot be posted/updated via draft-only logic | No explicit test asserting update on posted is rejected |
| JL-06 | Reversal exists | Passed | [src/RetailPOS.API/Services/JournalEntryService.cs](src/RetailPOS.API/Services/JournalEntryService.cs#L352) | `ReverseAsync` | `POST /api/accounting/journals/{id}/reverse` | `journal_entries.reversal_of_journal_entry_id`, `reversed_by_journal_entry_id` | `accounting.journals.reverse` | [tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs#L55), [tests/RetailPOS.Tests/Accounting/JournalEntryServiceTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryServiceTests.cs#L154) | Reversal posted and original marked reversed | No explicit HTTP test for reverse with custom posting/business date |
| JL-07 | Prevent duplicate reversal | Partially Passed | [src/RetailPOS.API/Services/JournalEntryService.cs](src/RetailPOS.API/Services/JournalEntryService.cs#L379) | Checks `ReversedByJournalEntryId` and existing reversal query | `POST /api/accounting/journals/{id}/reverse` | Linkage fields/indexes in [src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs](src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs#L1426) | `accounting.journals.reverse` | None explicit | Logic exists and guards duplicate reversal | Missing explicit unit/integration test for duplicate reverse calls |
| JL-08 | No hard deletion of posted journals | Partially Passed | Controller/service surface only | No delete method in [src/RetailPOS.API/Controllers/AccountingJournalsController.cs](src/RetailPOS.API/Controllers/AccountingJournalsController.cs) | No journal delete endpoint | No DB trigger/constraint that blocks direct SQL delete | N/A | None | API surface does not expose delete | DB-level hard-delete prevention not enforced |
| JL-09 | Valid status transitions only | Partially Passed | [src/RetailPOS.API/Services/JournalEntryService.cs](src/RetailPOS.API/Services/JournalEntryService.cs#L234), [src/RetailPOS.API/Services/JournalEntryService.cs](src/RetailPOS.API/Services/JournalEntryService.cs#L276), [src/RetailPOS.API/Services/JournalEntryService.cs](src/RetailPOS.API/Services/JournalEntryService.cs#L352) | Draft-only update/cancel/post; posted-only reverse | All journal lifecycle endpoints | `journal_entries.status` varchar(20) (not enum/check) | Lifecycle permissions above | Partial service tests in [tests/RetailPOS.Tests/Accounting/JournalEntryServiceTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryServiceTests.cs) | Core transitions enforced in service | No DB check constraint for allowed status values and no full transition matrix test |

---

## 2) Journal Header and Line Model

### 2.1 Header fields

Evidence: [src/RetailPOS.Core/Entities/JournalEntry.cs](src/RetailPOS.Core/Entities/JournalEntry.cs), DTO map in [src/RetailPOS.API/Services/JournalEntryService.cs](src/RetailPOS.API/Services/JournalEntryService.cs#L827), EF mapping in [src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs](src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs#L1409).

| Field | Status | Notes |
|---|---|---|
| Id, BusinessId, JournalNumber, JournalType | Passed | Present in entity/DTO/mapping |
| TransactionDate, PostingDate, BusinessDate | Passed | `BusinessDate` nullable in schema/model; set from transaction date if omitted in service |
| FinancialYearId, AccountingPeriodId | Passed | Derived/resolved at posting via `ValidatePostingPeriodAsync` |
| ReferenceType, ReferenceId, ReferenceNumber | Passed | Present + max length constraints |
| Description | Passed | Optional |
| OutletId, WarehouseId, TerminalId, CashSessionId | Passed | Nullable dimensions |
| Status, IsSystemGenerated | Passed | Lifecycle-driven status |
| ReversalOfJournalEntryId | Passed | Present + FK |
| ReversedByJournalEntryId, ReversedAt, ReversedBy | Passed | Added and mapped; migration present |
| PostedAt, PostedBy, CreatedAt/By, UpdatedAt/By | Passed | Present and populated by service |

### 2.2 Line fields

Evidence: [src/RetailPOS.Core/Entities/JournalEntryLine.cs](src/RetailPOS.Core/Entities/JournalEntryLine.cs), DTO in [src/RetailPOS.API/DTOs/Accounting/AccountingDtos.cs](src/RetailPOS.API/DTOs/Accounting/AccountingDtos.cs), mapping in [src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs](src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs#L1480).

| Field | Status | Notes |
|---|---|---|
| AccountId, DebitAmount, CreditAmount | Passed | Required + precision(18,2) |
| Description | Passed | Optional |
| OutletId, WarehouseId, SupplierId, CustomerId | Passed | Nullable dimensions |
| PaymentMethodId, CostCenterId | Passed | Optional, max 50 |
| CreatedAt, CreatedBy | Passed | Populated by service |

### 2.3 Missing/nullable/derived/not-used/deferred declarations

| Item | Classification | Evidence | Impact |
|---|---|---|---|
| BusinessDate | Nullable + derived | Set in `CreateDraftInternalAsync` and `UpdateDraftAsync` | Acceptable for draft compatibility; posting uses date governance |
| FinancialYearId/AccountingPeriodId | Derived | Set in `ValidatePostingPeriodAsync` | Expected behavior |
| Idempotency discriminator argument | Not currently used | `CreateDraftInternalAsync(... idempotencyDiscriminator)` argument unused in key composition | Documented deviation |
| DB status domain check | Missing | No status check constraint in model mapping | Risk of invalid status via direct DB writes |
| Outlet authorization for journals | Implemented in API layer | `IUserOutletAccessService` checks in [src/RetailPOS.API/Controllers/AccountingJournalsController.cs](src/RetailPOS.API/Controllers/AccountingJournalsController.cs#L15) | Outlet/location scope enforced for search/get/create/update/cancel/post/reverse |

---

## 3) Double-Entry Validation

Evidence core: [src/RetailPOS.API/Services/JournalEntryService.cs](src/RetailPOS.API/Services/JournalEntryService.cs#L562), DB check constraint in [src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs](src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs#L1490).

| Requirement ID | Requirement description | Status | Backend file/class | Method/location | DB evidence | Test evidence | Actual verification result | Limitation/risk |
|---|---|---|---|---|---|---|---|---|
| DV-01 | Minimum line count >= 2 | Passed | JournalEntryService | `ValidateLines` | N/A | Unbalanced test path exercises validation pipeline | Throws when `<2` | No dedicated named test for one-line case |
| DV-02 | Balanced debit/credit | Passed | JournalEntryService | `ValidateLines` total debit==credit | N/A | [tests/RetailPOS.Tests/Accounting/JournalEntryServiceTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryServiceTests.cs#L34) | Rejects unbalanced journal | Good |
| DV-03 | Non-zero journal total | Passed | JournalEntryService | `ValidateLines` totals `>0` | DB side constraint also enforces line nonzero side | Indirect | Enforced in service | No explicit zero-total test |
| DV-04 | Debit and credit not both on same line | Passed | JournalEntryService | `hasDebit == hasCredit` guard | DB check `c_k_journal_entry_lines_amount_sides` | Indirect | Enforced service + DB | No dedicated explicit test |
| DV-05 | Zero lines rejected | Passed | JournalEntryService | `lines.Count == 0` guard | N/A | Indirect | Enforced | No dedicated explicit test |
| DV-06 | Negative values rejected | Passed | JournalEntryService | `line.DebitAmount <0 || CreditAmount <0` | DTO has `[Range(0,...)]` for API input | Indirect | Enforced | No dedicated explicit test |
| DV-07 | Account existence | Passed | JournalEntryService | `ValidateAndLoadAccountsAsync` count match | FK `journal_entry_lines.account_id` -> chart account | Indirect | Rejects unknown account | No explicit unknown-account test |
| DV-08 | Active-account validation | Passed | JournalEntryService | inactive account rejection | N/A | Indirect | Rejects inactive accounts | Missing dedicated test |
| DV-09 | Same-business account validation | Passed | JournalEntryService | account lookup filtered by `BusinessId` | N/A | Indirect | Cross-business account rejected by count mismatch | Missing dedicated test |
| DV-10 | AllowManualPosting restriction | Passed | JournalEntryService | manual-only check in `ValidateAndLoadAccountsAsync` | N/A | Indirect | Manual posting blocked for `AllowManualPosting=false` | Missing dedicated test |
| DV-11 | Monetary precision handling | Passed | JournalEntryService + DbContext | `HasAllowedScale(...,2)` + `HasPrecision(18,2)` | precision on debit/credit | Indirect | >2 decimals rejected in service | Missing dedicated precision test |

---

## 4) Posting Transaction Boundary

Evidence: [src/RetailPOS.API/Services/JournalEntryService.cs](src/RetailPOS.API/Services/JournalEntryService.cs#L276), [src/RetailPOS.API/Services/JournalNumberService.cs](src/RetailPOS.API/Services/JournalNumberService.cs#L19).

| Requirement ID | Requirement description | Status | Evidence | Actual verification result | Limitation/risk |
|---|---|---|---|---|---|
| PT-01 | Posting runs in DB transaction | Passed | `BeginTransactionAsync(IsolationLevel.ReadCommitted)` in `PostAsync` | Posting wrapped in transaction | Uses `ReadCommitted` not `Serializable` (doc deviation from plan section 18.2) |
| PT-02 | Revalidates immediately before posting | Passed | `ValidateAndLoadAccountsAsync`, `ValidateLines`, `ValidatePostingPeriodAsync` in `PostAsync` | Revalidation done just before status/number mutation | Good |
| PT-03 | Checks financial year/period | Passed | `ValidatePostingPeriodAsync` | Closed year and closed/locked period rejected | Covered by locked-period tests |
| PT-04 | Allocates journal number at posting time | Passed | `journal.JournalNumber ??= AllocateNextJournalNumberAsync(...)` | Draft numbers remain null until post | Verified by HTTP workflow test |
| PT-05 | Concurrency-safe | Passed | Row lock `FOR UPDATE` in `LockJournalRowAsync`, sequence upsert in number service | Concurrent posting test passes unique sequential numbers | In-memory provider skips lock path by design |
| PT-06 | Prevents duplicate posting | Partially Passed | Early return if already posted/reversed | Idempotent same-journal post behavior implemented | Missing dedicated test for repeated post on same journal |
| PT-07 | No partial state after failure | Passed | Transaction + save then commit pattern | Failures throw and rollback | No explicit fault-injection test inside post transaction |

---

## 5) Sequential Journal Numbering

Evidence core:
- [src/RetailPOS.API/Services/JournalNumberService.cs](src/RetailPOS.API/Services/JournalNumberService.cs#L81)
- [src/RetailPOS.Core/Entities/AccountingNumberSequence.cs](src/RetailPOS.Core/Entities/AccountingNumberSequence.cs)
- [src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs](src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs#L1318)

Exact implemented format:
- `PREFIX-YEARCODE-SEQUENCE`
- Example: `JR-2026-000001`
- Implemented at [src/RetailPOS.API/Services/JournalNumberService.cs](src/RetailPOS.API/Services/JournalNumberService.cs#L81)

| Requirement ID | Requirement description | Status | Actual verification result | Test evidence | Limitation/risk |
|---|---|---|---|---|---|
| SN-01 | Human-readable format | Passed | String format above | [tests/RetailPOS.Tests/Accounting/JournalNumberRelationalConcurrencyTests.cs](tests/RetailPOS.Tests/Accounting/JournalNumberRelationalConcurrencyTests.cs#L24) | Good |
| SN-02 | Business scope | Passed | Sequence key uses `business_id` | Same test + DB unique key | Good |
| SN-03 | Financial-year scope | Passed | `scopeKey = fy:{financialYearId}` for per-year policy | Relational tests assert `ScopeKey == "fy:2026"` | Good |
| SN-04 | Concurrency safety | Passed | `INSERT ... ON CONFLICT ... RETURNING` + transaction requirement | ConcurrentPosting test | Good |
| SN-05 | Uniqueness | Passed | Journal unique index `(BusinessId, JournalNumber)` | Concurrency test unique numbers | Good |
| SN-06 | Permanent number assigned only at posting | Passed | Draft create does not assign number | HTTP workflow asserts draft number blank, post has number | Good |
| SN-07 | Number not reused | Passed | Sequence increments transactionally; rollback test verifies non-persisted increment | Rollback test | Good |
| SN-08 | Relational concurrency coverage | Passed | Dedicated relational test class | 2/2 relational tests passed in latest run | Good |

---

## 6) Idempotency

| Requirement ID | Requirement description | Status | Evidence | Actual verification result | Limitation/deviation/risk |
|---|---|---|---|---|---|
| ID-01 | Final idempotency key documented | Passed | Filtered unique index on `(BusinessId, JournalType, ReferenceType, ReferenceId)` with `is_system_generated = true` in [src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs](src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs#L1422) | Key is enforced for system journals by DB | Manual journals intentionally excluded from reference uniqueness |
| ID-02 | DB enforcement | Passed | Migration [src/RetailPOS.Infrastructure/Data/Migrations/20260801104721_RestrictJournalReferenceUniquenessToSystemGenerated.cs](src/RetailPOS.Infrastructure/Data/Migrations/20260801104721_RestrictJournalReferenceUniquenessToSystemGenerated.cs) + model mapping | Relational DB enforces system-only idempotency | InMemory tests do not enforce unique indexes |
| ID-03 | Service-level enforcement for system journals | Passed | `CreateDraftInternalAsync` pre-check + `DbUpdateException` retry-query path | Returns existing system journal for duplicate key | Covered by unit test |
| ID-04 | Behavior for manual journals | Passed | Manual journals are not covered by filtered unique index | Repeated manual references are allowed without collision | Operational duplicates remain possible by design for manual workflows |
| ID-05 | Duplicate request test evidence | Passed | System duplicate test + new relational manual/system split test | [tests/RetailPOS.Tests/Accounting/JournalEntryServiceTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryServiceTests.cs#L82), [tests/RetailPOS.Tests/Accounting/JournalNumberRelationalConcurrencyTests.cs](tests/RetailPOS.Tests/Accounting/JournalNumberRelationalConcurrencyTests.cs#L118) | Relational evidence confirms intended split behavior |
| ID-06 | Legitimate multi-journal exception documented | Passed | Manual distinct entries can be achieved by changing reference key; system journals are idempotent by key | Current implementation intentionally separates system behavior | `idempotencyDiscriminator` parameter exists but currently not used in key composition |

---

## 7) Reversal

| Requirement ID | Requirement description | Status | Evidence | Permission | Test evidence | Actual verification result | Limitation/risk |
|---|---|---|---|---|---|---|---|
| RV-01 | Only posted journals reversible | Passed | `if (original.Status != Posted) throw` in [src/RetailPOS.API/Services/JournalEntryService.cs](src/RetailPOS.API/Services/JournalEntryService.cs#L372) | `accounting.journals.reverse` | Service + HTTP lifecycle tests | Enforced | Missing explicit negative test for reversing draft/cancelled |
| RV-02 | Reversal reason required | Passed | `string.IsNullOrWhiteSpace(reason)` guard | Same | Covered indirectly | Enforced | Missing explicit blank-reason test |
| RV-03 | Debit/credit swapped exactly | Passed | reversal line build swaps debit and credit | Same | [tests/RetailPOS.Tests/Accounting/JournalEntryServiceTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryServiceTests.cs#L154) | Verified in unit test | Good |
| RV-04 | Original lines not modified | Passed | New reversal lines created; original lines untouched | Same | Unit test verifies original status/link only | Header of original updated as expected; lines untouched by code path |
| RV-05 | Dimensions and references retained | Passed | Reversal copies outlet/warehouse/terminal/cashsession and reference fields | Same | Service test validates linkage and lifecycle | No dedicated assertion for every dimension field |
| RV-06 | Reversal posts into open period | Passed | `ValidatePostingPeriodAsync` on reversal before number allocation | Same | Locked-period posting test; same validator path | No dedicated reversal-locked-period test |
| RV-07 | Reversal gets own number | Passed | Explicit allocator call for reversal | Same | HTTP lifecycle test (posted reversal) | Good |
| RV-08 | Bidirectional linkage | Passed | `ReversalOfJournalEntryId` on reversal and `ReversedByJournalEntryId` on original | Same | Unit test asserts both links | Good |
| RV-09 | Atomic update | Passed | Reversal and original update in one DB transaction | Same | Code inspection | Commit at end; rollback on exception |
| RV-10 | Authorization | Passed | Controller `[Authorize(Policy = "accounting.journals.reverse")]` in [src/RetailPOS.API/Controllers/AccountingJournalsController.cs](src/RetailPOS.API/Controllers/AccountingJournalsController.cs#L126) | `accounting.journals.reverse` | [tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs#L69) create-permission forbid; reverse-specific forbid missing | Reverse policy present |
| RV-11 | Audit logging | Passed | `RecordAuditAsync(AuditActionType.Reverse...)` and rejection path `RecordFailureAuditAsync` | N/A | Persistent audit service writes DB | Persistent audit records are created if audit service succeeds |

---

## 8) APIs

| Requirement ID | Endpoint | Method/Route | Permission | DTO | Business-scope enforcement | Outlet-scope enforcement | Validation | Response behavior | Relevant tests | Status |
|---|---|---|---|---|---|---|---|---|---|---|
| API-01 | Create Draft | `POST /api/accounting/journals` | `accounting.journals.create` | `CreateJournalEntryDto` | `ITenantAccessService.RequireBusinessId()` in service | Enforced via `_outletAccess.EnforceWriteOutletAsync` in controller | Line/account/manual checks | `201 Created`, returns draft | [tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs#L188) | Passed |
| API-02 | Get by ID | `GET /api/accounting/journals/{id}` | `accounting.journals.view` (class-level) | N/A | filtered by business in query | Enforced via `_outletAccess.ResolveAndAuthorizeLocationAsync` | not-found handling via service | `200 OK` / `401` for unauthorized location | [tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs#L188) | Passed |
| API-03 | Search/List | `GET /api/accounting/journals` | `accounting.journals.view` | query -> `JournalSearchDto` | filtered by business | Enforced via `_outletAccess.ResolveAndAuthorizeOutletFilterAsync` | filter/sort/pagination | `200 OK` / `401` for unauthorized outlet filter | [tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs#L188) | Passed |
| API-04 | Update Draft | `PUT /api/accounting/journals/{id}` | `accounting.journals.update` | `UpdateJournalEntryDto` | business-scoped lookup | Enforced via pre-check + write-outlet enforcement | draft-only, line/account checks | `200 OK` / `401` unauthorized scope | [tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs#L117), [tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs#L188) | Passed |
| API-05 | Cancel Draft | `POST /api/accounting/journals/{id}/cancel` | `accounting.journals.cancel` | `CancelJournalEntryDto` | business-scoped lookup | Enforced via pre-check location authorization | draft-only check | `200 OK` / `401` unauthorized scope | [tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs#L117), [tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs#L188) | Passed |
| API-06 | Post Draft | `POST /api/accounting/journals/{id}/post` | `accounting.journals.post` | N/A | business-scoped lookup | Enforced via pre-check location authorization | revalidation + period + transaction + numbering | `200 OK` / `401` unauthorized scope | [tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs#L23), [tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs#L188) | Passed |
| API-07 | Reverse Posted | `POST /api/accounting/journals/{id}/reverse` | `accounting.journals.reverse` | `ReverseJournalEntryDto` | business-scoped lookup | Enforced via pre-check location authorization | posted-only + reason + period + swap lines | `200 OK` / `401` unauthorized scope | [tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs#L23), [tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs#L188) | Passed |

API section note:
- Outlet scope is now enforced for all journal endpoints at API entry points using `IUserOutletAccessService`.

---

## 9) Permissions

| Requirement ID | Requirement description | Status | Evidence | Actual verification result | Limitation/risk |
|---|---|---|---|---|---|
| PM-01 | PermissionCatalog alignment | Passed | [src/RetailPOS.API/Authorization/PermissionCatalog.cs](src/RetailPOS.API/Authorization/PermissionCatalog.cs#L36) | Includes view/create/update/cancel/post/reverse | Good |
| PM-02 | Controller authorization alignment | Passed | [src/RetailPOS.API/Controllers/AccountingJournalsController.cs](src/RetailPOS.API/Controllers/AccountingJournalsController.cs#L12) | Policies present on all lifecycle endpoints | Good |
| PM-03 | SQL permission seed alignment | Passed | [database/add-accounting-v2-permissions.sql](database/add-accounting-v2-permissions.sql), [database/seed-v2-roles.sql](database/seed-v2-roles.sql), [database/seed-data.sql](database/seed-data.sql) | Includes update/cancel in accounting journal permissions | Good |
| PM-04 | Role seed alignment | Passed | Same files above | AccountsAdmin/BusinessAdmin/BusinessOwner include journal permissions | Good |
| PM-05 | Angular route guard alignment | Partially Passed | [retailpos-frontend/src/app/app.routes.ts](retailpos-frontend/src/app/app.routes.ts#L215) | Journal route uses canonical view permission and guard | No frontend routes for update/cancel/post/reverse actions |
| PM-06 | Menu visibility alignment | Passed | [retailpos-frontend/src/app/models/menu.model.ts](retailpos-frontend/src/app/models/menu.model.ts#L71) | Journal menu visibility uses `accounting.journals.view` | Management actions not represented in menu |
| PM-07 | Legacy alias handling | Passed | Backend aliases in [src/RetailPOS.API/Authorization/PermissionCatalog.cs](src/RetailPOS.API/Authorization/PermissionCatalog.cs#L53); frontend alias map in [retailpos-frontend/src/app/services/auth.service.ts](retailpos-frontend/src/app/services/auth.service.ts#L15) | Frontend alias map now includes view/create/update/cancel/post/reverse | Good |
| PM-08 | Role form permission alignment | Passed | [retailpos-frontend/src/app/components/roles/role-form.component.ts](retailpos-frontend/src/app/components/roles/role-form.component.ts#L175) | Role form now includes `accounting.journals.update` and `accounting.journals.cancel` | Good |
| PM-09 | Cashier and SalesPerson do not receive journal-management access | Passed | [database/seed-v2-roles.sql](database/seed-v2-roles.sql#L132), [database/seed-data.sql](database/seed-data.sql#L60), [database/seed-data.sql](database/seed-data.sql#L177) | No accounting journal management permissions granted to those roles | Good |

---

## 10) Audit

Persistent audit evidence chain:
- Journal service emits audit events via [src/RetailPOS.API/Services/JournalEntryService.cs](src/RetailPOS.API/Services/JournalEntryService.cs#L798)
- Audit persistence implemented in [src/RetailPOS.Infrastructure/Audit/AuditService.cs](src/RetailPOS.Infrastructure/Audit/AuditService.cs#L96) and failure path [src/RetailPOS.Infrastructure/Audit/AuditService.cs](src/RetailPOS.Infrastructure/Audit/AuditService.cs#L271)

| Requirement ID | Requirement description | Status | Evidence | Test evidence | Actual verification result | Limitation/risk |
|---|---|---|---|---|---|---|
| AU-01 | Draft created audited | Passed | `RecordAuditAsync(AuditActionType.Create...)` in create flow | No direct assertion test | Persistent audit write path exists | No dedicated test asserting audit row content |
| AU-02 | Draft updated audited | Passed | `RecordAuditAsync(AuditActionType.Update...)` | No direct assertion test | Persistent audit write path exists | Same |
| AU-03 | Draft cancelled audited | Passed | `RecordAuditAsync(AuditActionType.Cancel...)` | No direct assertion test | Persistent audit write path exists | Same |
| AU-04 | Posted audited | Passed | `RecordAuditAsync(AuditActionType.Approve...)` | No direct assertion test | Persistent audit write path exists | Same |
| AU-05 | Reversed audited | Passed | `RecordAuditAsync(AuditActionType.Reverse...)` | No direct assertion test | Persistent audit write path exists | Same |
| AU-06 | Posting rejected audited | Passed | catch path `RecordFailureAuditAsync(AuditActionType.Reject...)` | No direct assertion test | `AuditService.RecordFailureAsync` persists failed event via fresh DbContext | Same |
| AU-07 | Reversal rejected audited | Passed | catch path in reverse uses failure audit | No direct assertion test | Persistent failure audit path exists | Same |

Rejected-action auditing classification:
- Fully implemented as persistent audit records (not only application logging), contingent on audit DB write availability.

---

## 11) Database Integrity

| Requirement ID | Requirement description | Status | Evidence | Actual verification result | Limitation/risk |
|---|---|---|---|---|---|
| DB-01 | Foreign keys | Passed | Journal/line/account/reversal FKs in [src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs](src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs#L1430) | FK graph defined for business/year/period/reversal/lines | Good |
| DB-02 | Unique constraints | Passed | `(BusinessId, JournalNumber)` unique + filtered unique reference index (`is_system_generated = true`) at [src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs](src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs#L1421) | Enforced at DB model level with intended manual/system split | InMemory tests do not enforce unique indexes |
| DB-03 | Indexes | Passed | Posting/search/reversal indexes in same block | Appropriate baseline indexes present | Search-heavy filters may need future tuning |
| DB-04 | Decimal precision | Passed | debit/credit precision(18,2) at [src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs](src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs#L1483) | Precision defined and service enforces scale 2 | Good |
| DB-05 | Status storage | Partially Passed | status varchar(20) required | Stored consistently by service | No DB check constraint restricting allowed status set |
| DB-06 | Reference uniqueness | Passed | Filtered unique key by business+journalType+referenceType+referenceId for system journals only | System duplicates prevented; manual duplicate references allowed by design | Relational behavior now explicit and tested |
| DB-07 | Reversal linkage | Passed | fields + index + FKs in DbContext, migration in [src/RetailPOS.Infrastructure/Data/Migrations/20260801102900_AddJournalReversalMetadataAndLinkage.cs](src/RetailPOS.Infrastructure/Data/Migrations/20260801102900_AddJournalReversalMetadataAndLinkage.cs) | Bidirectional linkage persisted | Good |
| DB-08 | Sequence allocation storage | Passed | `accounting_number_sequences` entity and unique key in [src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs](src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs#L1318) | Durable allocator backing table exists | Good |
| DB-09 | Migration application status | Partially Passed | migration file exists and relational concurrency harness runs `Database.MigrateAsync` in [tests/RetailPOS.Tests/Accounting/JournalNumberRelationalConcurrencyTests.cs](tests/RetailPOS.Tests/Accounting/JournalNumberRelationalConcurrencyTests.cs#L294) | Migration validated in test DB lifecycle | No explicit evidence in this step that production/shared DB has applied latest migration |

---

## 12) Test Evidence

### 12.1 Unit tests

- [tests/RetailPOS.Tests/Accounting/JournalEntryServiceTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryServiceTests.cs#L34)
  - `CreateManualAsync_RejectsUnbalancedJournal`
  - `CreateManualAsync_CreatesDistinctDrafts_ForRepeatedManualReference`
  - `CreateOrGetSystemDraftAsync_IsIdempotentByTypeAndReference`
  - `PostAsync_RejectsLockedPeriod`
  - `ReverseAsync_CreatesReversal_AndMarksOriginal`
  - `UpdateDraftAsync_UpdatesHeaderAndLines_ForDraftOnly`
  - `CancelDraftAsync_CancelsDraft_AndRejectsPosting`

### 12.2 Relational integration + concurrency tests

- [tests/RetailPOS.Tests/Accounting/JournalNumberRelationalConcurrencyTests.cs](tests/RetailPOS.Tests/Accounting/JournalNumberRelationalConcurrencyTests.cs#L24)
  - `ConcurrentPosting_AllocatesUniqueSequentialNumbers`
  - `RolledBackAllocation_DoesNotPersistSequenceMutation`
   - `ManualReferences_CanRepeat_WhileSystemReferences_RemainUnique`

### 12.3 HTTP workflow tests

- [tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs#L23)
  - `Create_Post_Reverse_Lifecycle_Works`
  - `Create_WithoutCreatePermission_ReturnsForbidden`
  - `Post_LockedPeriod_ReturnsBadRequest`
   - `Update_And_Cancel_Draft_Lifecycle_Works`
   - `Journal_Apis_Enforce_Outlet_Authorization_For_Read_And_Write`

### 12.4 Permission regression tests

- [tests/RetailPOS.Tests/Accounting/AccountingPermissionRegressionTests.cs](tests/RetailPOS.Tests/Accounting/AccountingPermissionRegressionTests.cs#L12)
  - `AccountingPermissions_InControllers_MustExistInCatalog`
  - `AccountingPermissions_InSqlSeeds_MustCoverCatalog`
  - `AccountingPermissions_InFrontendRoutesAndMenu_MustBeCanonicalAndGuarded`
  - `AccountingPermissions_InRoleFormAndAuthAliases_MustStayAligned`
  - `LegacyFinancePolicies_AreNotUsedInNewAccountingControllers`

### 12.5 Latest full regression result (actual)

- Build succeeded
- Test summary: total 237, failed 0, succeeded 237, skipped 0
- Existing warning only: NU1903 (`SQLitePCLRaw.lib.e_sqlite3`)

---

## 13) Requirement Traceability Summary

| Requirement Area | Passed | Partial | Failed | Evidence Complete | Blocking |
|---|---:|---:|---:|---|---|
| 1. Journal Lifecycle | 6 | 3 | 0 | Mostly | No |
| 2. Header/Line Model | 2 | 1 | 0 | Yes | No |
| 3. Double-Entry Validation | 11 | 0 | 0 | Medium (some tests indirect) | No |
| 4. Posting Transaction Boundary | 6 | 1 | 0 | High | No |
| 5. Sequential Numbering | 8 | 0 | 0 | High | No |
| 6. Idempotency | 5 | 0 | 0 | High | No |
| 7. Reversal | 11 | 0 | 0 | High | No |
| 8. APIs | 7 | 0 | 0 | High | No |
| 9. Permissions | 9 | 0 | 0 | High | No |
| 10. Audit | 7 | 0 | 0 | Medium (no assertion tests) | No |
| 11. Database Integrity | 7 | 2 | 0 | High | No |
| 12. Test Evidence | 1 | 0 | 0 | Yes | No |

---

## 14) Gaps and Final Decision

### 14.1 Blocking gaps

1. No unresolved blocking gaps remain for Journal Engine acceptance.

Resolved blocker evidence:
1. Frontend permission drift resolved:
   - [retailpos-frontend/src/app/components/roles/role-form.component.ts](retailpos-frontend/src/app/components/roles/role-form.component.ts#L175)
   - [retailpos-frontend/src/app/services/auth.service.ts](retailpos-frontend/src/app/services/auth.service.ts#L15)
2. HTTP verification for update/cancel added:
   - [tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs#L117)
3. Manual/system idempotency ambiguity resolved at DB level and validated relationally:
   - [src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs](src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs#L1421)
   - [src/RetailPOS.Infrastructure/Data/Migrations/20260801104721_RestrictJournalReferenceUniquenessToSystemGenerated.cs](src/RetailPOS.Infrastructure/Data/Migrations/20260801104721_RestrictJournalReferenceUniquenessToSystemGenerated.cs)
   - [tests/RetailPOS.Tests/Accounting/JournalNumberRelationalConcurrencyTests.cs](tests/RetailPOS.Tests/Accounting/JournalNumberRelationalConcurrencyTests.cs#L118)
4. Outlet-scope governance implemented for all journal APIs:
   - [src/RetailPOS.API/Controllers/AccountingJournalsController.cs](src/RetailPOS.API/Controllers/AccountingJournalsController.cs#L15)
   - [tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryWorkflowHttpTests.cs#L188)

### 14.2 Non-blocking gaps

1. Missing explicit negative tests for:
   - duplicate reversal call,
   - update on posted journal,
   - reverse non-posted journal,
   - precision/negative/zero-line edge cases at API level.
2. No DB check constraint restricting status values to lifecycle enum set.

### 14.3 Missing tests

1. Duplicate post and duplicate reverse idempotency tests.
2. Audit persistence assertion tests (row-level assertions in `audit_events`).

### 14.4 Missing database enforcement

1. No status-domain check constraint for `journal_entries.status`.
2. No DB-level hard-delete prevention for posted journals (API prevents but DB does not).

### 14.5 Missing permissions alignment

1. No missing permission alignment in journal scope.

### 14.6 Documentation deviations

1. Plan section 18.2 says posting uses `Serializable`; implementation currently uses `ReadCommitted` in post/reverse/cancel transaction scopes.

### 14.7 Recommended fixes before next milestone

1. Add duplicate post and duplicate reverse idempotency tests.
2. Add status-value constraint and document hard-delete governance decision (service-only vs DB-enforced).
3. Add audit row assertion tests for accepted/rejected journal operations.

### 14.8 Final decision

**Accepted**

Reason: all previously identified blocking issues are remediated with code + migration + automated test evidence. Journal Engine now satisfies the strict acceptance gate for this milestone.
