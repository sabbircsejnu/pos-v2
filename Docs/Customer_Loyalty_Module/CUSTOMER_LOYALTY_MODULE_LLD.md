# IraniMart POS v2 — Customer Loyalty & Rewards Module
## Low-Level Design (LLD)

| Property | Value |
|---|---|
| Document ID | CL-LLD-001 |
| Version | 1.0 — implementation design baseline |
| Date | 26 October 2026 |
| Status | **Design proposal derived from frozen requirements; P0 implementation gates remain open** |
| Normative source | `CUSTOMER_LOYALTY_MODULE_SRS.md` — CL-SRS-001 v1.0 |
| Frozen scope | Decisions D01–D40; clarifications C01 (VAT-exclusive earnings) and C02 (MediatR/CQRS) |
| Target repository path | `Docs/Customer_Loyalty_Module/CUSTOMER_LOYALTY_MODULE_LLD.md` |
| Architecture | Existing RetailPOS modular monolith, Clean Architecture, MediatR + CQRS, FluentValidation, EF Core, transactional relational DB, durable outbox + RabbitMQ |

> **Normative hierarchy:** The SRS governs business behaviour. This LLD specifies an implementable design, *not* approval to change it. Database names, namespaces, routes and interfaces below are proposed technical contracts; align them with the actual repository before migrations. Three P0 production gates are mandatory (§23). Do not silently alter the approved 15-minute reservation, 1-month downgrade grace, 30-day provisional claim, 12-month base expiry or VAT-exclusive points basis.

## 1. Purpose and design principles

Deliver one Business-scoped loyalty programme linked to existing Customer and POS Sale records. Keep earning/redemption amounts correct under sales, holds, refunds, returns, exchanges, discounts, VAT, tiers, campaigns, expiry, partial allocations, negative debt, duplicate delivery and concurrent checkouts.

**Design invariants (I-01 to I-17):**

1. **I-01 Single membership identity:** `BusinessId + NormalizedPhone` maps to at most one live loyalty identity; historical migrations must be remediated before enforcing it.
2. **I-02 Historical purchaser separation:** a sale's actual `CustomerId` and `LoyaltyMembershipId` are independent, immutable provenance references where available.
3. **I-03 No double award/debit:** a logical source/event produces at most one corresponding ledger movement, including retries and replays.
4. **I-04 Verified redemption:** no redemption debit or monetary loyalty discount without account + checkout + amount-bound successful OTP and a valid reservation.
5. **I-05 Atomic finalization:** completed Sale and consumed redemption commit in the *same database transaction*; external payment is reconciled through a separate state machine.
6. **I-06 Durable earning:** every eligible completed Sale has a same-transaction earned credit *or* an idempotent durable entitlement/outbox record awaiting recovery.
7. **I-07 VAT/discount correctness:** calculate from product/tax/discount/redemption allocation snapshots, subtract included VAT exactly once, exclude non-product charges, floor final invoice points.
8. **I-08 Return bound:** cumulative line refund, loyalty restoration and earned reversal never exceed original persisted allocations.
9. **I-09 Immutable history:** movement ledger is append-only; corrections are compensating movements referencing originals. Materialized balances may be rebuilt.
10. **I-10 Batch correctness:** each available point belongs to an unexpired source batch; FEFO allocation and expiry cannot consume points already allocated to redemption/recovery.
11. **I-11 Negative debt:** returns may create negative balance; future earnings recover it without a second debit; redemption disabled until balance/limits allow.
12. **I-12 Business isolation:** query, write, consumer and export authorization enforce `BusinessId`; outlet history is scoped separately from business-wide balances.
13. **I-13 Policy immutability:** completed transactions retain tier/campaign/monetary/tax policy version and original allocation; backdated Sales use actual `CompletedAtUtc`.
14. **I-14 Financial segregation:** loyalty subledger and accounting GL are separate; accounting failures must not mutate committed sale or loyalty ledger.
15. **I-15 Provisional isolation:** third-party unregistered phone accrues nonredeemable pending entitlements for 30 days; activation requires phone-owner consent and verification.
16. **I-16 Pause integrity:** pause prevents earning and redemption but not return/reversal, expiry-freeze maintenance, accounting, audit and reconciliation.
17. **I-17 No synchronous notification dependency:** OTP verification gates redemption; non-OTP SMS delivery cannot gate normal POS finalization.

## 2. Existing-system discovery and integration assumptions

Before implementing any proposed class/table, inspect actual repo references, namespaces, Sales states, EF Core DbContext, migrations, MediatR/FluentValidation pipeline, Authorization handlers, Customer model, accounting event contract, background/outbox mechanism, and payment abstractions. **Do not add a second pipeline, duplicate Customer table, duplicate Sale finalization endpoint or independent financial period engine.** Existing production data and actual price schema override *technical naming assumptions*, not frozen business semantics.

Dependencies:

| Existing component | Integration boundary |
|---|---|
| Customer Master | Reuse Customer PK; normalized phone lookup; membership and consent stored separately |
| POS Sale/Terminal/Cash Session | Extend existing checkout orchestrator; never bypass session/terminal controls; no stock negativity introduced |
| Sale items / Discount engine | Reuse mutual-exclusion rules for item-vs-overall discount; snapshot authoritative allocations |
| Tax/VAT | Read exact invoice tax treatment, inclusive/exclusive mode and item tax allocations |
| Return/Exchange engine | Existing settlement method and sale-line links; add loyalty adjustments without changing inventory semantics |
| Authorization/User Management | Permission grants, wildcard owner behaviour, selected-Business context for platform admin, Step-5 outlet scope |
| Approval kernel | Reuse maker-checker, no self-approval for manual adjustments and approved merges/recovery |
| Accounting | Durable source events, idempotent journal inbox, FY/period controls and daily reconciliation |
| Outbox/RabbitMQ | Reuse canonical durable delivery, retry and operational dead-letter mechanisms |
| Receipt / PDF / Excel | Extend existing 80mm renderer and scoped export/download pattern |

## 3. Module placement and dependency direction

```text
src/
  Domain/Loyalty/
    Entities/ ValueObjects/ Enums/ Events/ Services/ Policies/
  Application/Loyalty/
    Commands/<Feature>/  Queries/<Feature>/  Dtos/ Validators/
    Behaviours/ Interfaces/ Mapping/
  Infrastructure/Loyalty/
    Persistence/Configurations/ Repositories/ Providers/ Workers/ Projections/
  Api/Features/Loyalty/Controllers/ (thin endpoints → ISender.Send)
  Web/src/app/features/loyalty/ (follow actual Angular workspace layout)
tests/
  Domain.UnitTests/Loyalty/
  Application.UnitTests/Loyalty/
  IntegrationTests/Loyalty/
  Api.ContractTests/Loyalty/
  Web.Tests/Loyalty/
Docs/Customer_Loyalty_Module/
```

Names are illustrative. Dependencies: API → Application → Domain; Infrastructure implements Application contracts; Domain references neither EF Core nor MediatR. Application handlers use `ISender`, validation pipeline, clock/current-user/business abstractions and unit-of-work. No controller/Angular business arithmetic. Do not create a new microservice or new database.

## 4. Bounded contexts and aggregates

| Aggregate / service | Consistency boundary | Core responsibility |
|---|---|---|
| `LoyaltyProgramme` | Business + policy version | State Draft/Active/Paused/Terminated; activation, pauses, versioned settings, approved termination settlement |
| `LoyaltyMembership` | Business, customer identity | Consent/provisional/active/closure state, verified phone, alias/merged identity, current tier/grace |
| `LoyaltyAccount` | One membership | Balance projection, debt, reservations, version concurrency, limit enforcement |
| `LoyaltyEarningBatch` | One source award component | Earned/available/pending/recovered/redeemed/expired/reversed units, expiry clock |
| `LoyaltyCampaign` | Business campaign | Versioned targeting, cap/exclusivity, budget reservations, dates, eligibility |
| `LoyaltyAdjustmentRequest` | Request id | Maker-checker approval and append-only resulting movement |
| `LoyaltyOtpChallenge` | Challenge id | Challenge hash, purpose, scoped intent, attempts, expiry, consumed time |
| `LoyaltyRedemptionReservation` | Checkout id | Account + invoice/finalization intent, atomically FEFO-reserved point allocations, 15-min TTL |
| `LoyaltyFraudAlert` | Event correlation | Detection result, review access, nonblocking monitoring |

Domain services: `EligibleConsiderationCalculator`, `MonetaryAllocationService`, `PointsAccrualCalculator`, `CampaignEvaluator`, `TierEligibilityService`, `FefoAllocator`, `BalanceRecoveryService`, `ReturnLoyaltyReconciler`, `ExchangeLoyaltyReconciler`, `ExpiryClockService`, `LoyaltyReconciliationService` (application orchestration vs pure domain calculation separated). Use UTC for persistence and Business time zone for month/birthday/date-boundary decisions.

## 5. State machines

### 5.1 Programme

`Draft → Active → Paused → Active → Terminated` (termination only after approved balance settlement; no destructive deletion). On first activation set `GoLiveAtUtc` using authorized Business-selected instant. Pause records closed intervals; end bound may be null while paused. A Batch valid when pause starts receives cumulative duration extensions; expired-before-pause batches never revive. While paused: block member enrollment/earning/redemption as relevant, continue returns, accounting, reconciliations, and audit. Manual authorized resume.

### 5.2 Membership

`NotEnrolled → ActiveUnverified → ActiveVerified → ClosurePending → Closed → (fresh consent) Active...` or `NotEnrolled → Provisional → ActiveVerified / ClaimExpired`. Provisional begins with first eligible purchase, never resets its 30-day claim deadline, can accumulate multiple pending awards; owner consent and valid OTP must both be complete **before** deadline. After expiration, pending awards forfeited and campaign cap reservations freed; no revival on later enrollment. Owner-facing activation SMS must be treated as transactional invitation with controlled rate/expiry. Never treat third-party purchaser consent as account-holder consent.

### 5.3 Redemption

`Requested → OtpPending → VerifiedReserved → Consumed` or terminal `Cancelled / Expired / Invalidated`. Verify OTP before atomically allocating points; 15-minute reservation TTL begins at `VerifiedReserved`. Sale Hold, cart/redemption/checkout change, cancellation, membership/program restrictions or TTL release allocations exactly once. Challenge is 6 digits, 5-minute valid, 5 attempts; rate limit 5 requests per phone/hour, resend cooldown 60 seconds. Bind challenge to `BusinessId`, `MembershipId`, `CheckoutId`, stable checkout fingerprint and requested points/value; challenge never permits changing amount. Reverify on changes. Reservation expiration checker and checkout both lock/recheck state atomically; expiration alone must not race a committed sale.

### 5.4 Checkout / external payment

Proposed internal orchestration states: `Preparing → AmountLocked → PaymentInitiated → PaymentConfirmed/PaymentUnknown/PaymentRejected → Finalizing → Completed`, with `NeedsReconciliation` and `CompensationPending` for ambiguous external results. State names must be adapted to the existing payment model. Never hold SQL transaction across external payment API calls. Idempotency keys identify provider attempt and checkout finalization separately. `PaymentUnknown` cannot silently complete or re-charge: query provider by same reference, reconcile, then finalize/compensate. If points reservation is stale, reprice/re-authorize before capturing new payment; if external money was already captured, suspend finalization and perform approved correction/refund workflow. **P0-G2 is not satisfied until provider-specific behaviour is verified.**

## 6. Logical and physical relational schema proposal

Use names below as a mapping specification, not instructions to regenerate existing tables. Recommend EF Core with actual provider-supported decimal/UTC mappings, real FK types (`long` or `Guid` matching current repository), transaction isolation and migrations. All mutable rows have `CreatedAtUtc`, `CreatedBy`, `UpdatedAtUtc`, `RowVersion`/equivalent where supported. Every loyalty-owned table is Business-scoped or has an FK path to a Business-scoped parent, enforced by query guards and write validation.

| Table | Primary fields / key | Critical constraints and indexes |
|---|---|---|
| `LoyaltyProgrammes` | `Id, BusinessId, Status, GoLiveAtUtc, TimeZoneId, CurrentPolicyVersionId` | Unique `BusinessId`; protected status transition |
| `LoyaltyPolicyVersions` | `Id, BusinessId, Version, EffectiveFromUtc, ConfigJson, ApprovedBy, Hash` | Unique `(BusinessId,Version)`; no overlapping effective intervals; immutable once active |
| `LoyaltyMemberships` | `Id, BusinessId, CustomerId?, NormalizedPhone, State, TierId, VerifiedAtUtc, ProvisionalStartUtc, ClaimByUtc` | Unique active identity `(BusinessId,NormalizedPhone)` according to provider filtered-index support; validate duplicates before migration |
| `LoyaltyConsents` | `Id, MembershipId, Type, TermsVersion, Accepted, CapturedAtUtc, ActorId, EvidenceRef` | Append-only changes; no guessed consent |
| `LoyaltyPhoneHistory` | `Id, MembershipId, OldPhoneHash/Masked, NewPhoneHash/Masked, Reason, VerifiedAtUtc` | Audit, access control; avoid unsecured plaintext historical phone |
| `LoyaltyAccountBalances` | `MembershipId, GrossLedgerBalance, ReservedPoints, AvailablePoints, NegativeDebtPoints, RowVersion` | One/account; DB row lock/version; recomputable, not source of truth |
| `LoyaltyPointsLedger` | `Id, MembershipId, SourceType, SourceId, EventKey, MovementType, PointsDelta, OccurredAtUtc, PolicyVersionId, OriginalMovementId?, CorrelationId` | Unique `(BusinessId,EventKey,MovementType,SourceLineKey)`; no UPDATE/DELETE except operationally privileged nonfinancial metadata |
| `LoyaltyEarningBatches` | `Id, MembershipId, SourceSaleId, SourceLineAllocationKey, Awarded, Pending, Available, Redeemed, DebtRecovered, Expired, Reversed, EarnedAtUtc, OriginalExpiresAtUtc, EffectiveExpiresAtUtc` | Unique award provenance; nonnegative component counters; invariants checked transactionally |
| `LoyaltyBatchAllocations` | `Id, BatchId, Kind, DestinationId, Points, Timestamp, EventKey` | FEFO redemption/recovery/expiry/return-restoration allocation, unique cause + batch |
| `LoyaltySaleSnapshots` | `Id, BusinessId, SaleId, MembershipId, PurchaserCustomerId?, CompletedAtUtc, BusinessSaleDate, PolicyVersionId, TierId, SaleFingerprint, TaxMode, ProductGrossMinor, IncludedVatMinor, DiscountMinor, RedeemedValueMinor, ExcludedChargesMinor, EligibleMinor, AwardedPoints` | Unique `(BusinessId,SaleId)`; immutable monetary and policy snapshot; no financial recalculation on date edit |
| `LoyaltySaleLineAllocations` | `Id, SnapshotId, SaleLineId, QuantityUnits, GrossMinor, IncludedVatMinor, DiscountMinor, RedemptionMinor, EligibleMinor, EarnedPoints, ReturnedUnits, ReversedPoints` | Unique `(SnapshotId,SaleLineId)`; return cumulative cap; persisted deterministic remainder |
| `LoyaltyOtpChallenges` | `Id, BusinessId, MembershipId, CheckoutId, FingerprintHash, RequestedPoints, CodeHash, ExpiresUtc, AttemptCount, UsedAtUtc` | TTL/rate limits in persistent store; store secrets encrypted/hashed, never raw in logs |
| `LoyaltyRedemptionReservations` | `Id, BusinessId, MembershipId, CheckoutId, AmountMinor, Points, ChallengeId, State, ReservedAtUtc, ExpiresAtUtc, IdempotencyKey` | One live logical checkout reservation; unique idempotency; lock account/batches |
| `LoyaltyRedemptionAllocations` | `ReservationId, EarningBatchId, Points, OriginalExpiresAtUtc` | Unique `(ReservationId,EarningBatchId)`; total equals reserved points |
| `LoyaltyTiers` | `Id, BusinessId, PolicyVersionId, Name, ThresholdMinor, Multiplier` | Per version ordered threshold; Regular 0/1x baseline |
| `LoyaltyTierHistory` | `Id, MembershipId, FromTierId, ToTierId, EvaluatedAtUtc, EffectiveAtUtc, GraceEpisodeId?, Reason` | Chronological audit; unique transition event |
| `LoyaltyCampaigns` | `Id, BusinessId, Type, Version, StartUtc, EndUtc, ConditionsJson, StackingGroup, Priority, CapPoints, Status` | Effective campaign version snapshots, no out-of-policy retroactive edit |
| `LoyaltyCampaignUsages` | `Id, CampaignId, MembershipId, SaleId, AwardEventKey, AwardedPoints, State` | Unique `(CampaignId,MembershipId,SaleId,RewardType)` where applicable |
| `LoyaltyCampaignBudgetReservations` | `Id, CampaignId, ProvisionalMembershipId, ClaimId, Points, State, ExpiresAtUtc` | Atomic cap counters/reservation; release on return/forfeiture |
| `LoyaltyAdjustmentRequests` | `Id, MembershipId, Points, Direction, Reason, EvidenceRef, Status, RequestedBy, ApprovedBy?, ApprovalAtUtc?` | Approval kernel reference; prohibit self-approval |
| `LoyaltyAccountMerges` | `Id, BusinessId, SourceMembershipId, TargetMembershipId, Status, ApprovedBy, CompletedAtUtc, Reason` | Merge mapping/audit; uniqueness, duplicate reward correction, resolved reservations |
| `LoyaltyFraudAlerts` | `Id, BusinessId, MembershipId?, OutletId?, ActorId?, RuleCode, Severity, EventKey, State, ReviewedBy?` | Alert dedupe and permission-scoped review |
| `LoyaltyNotificationOutbox` / existing Outbox | `Id, BusinessId, EventKey, Channel, TemplateVersion, EncryptedRecipientRef, State, Attempts, NextAttemptUtc` | Unique notification key; consent/eligibility check at delivery |
| `LoyaltyAccountingEvents` / existing Outbox | `Id, BusinessId, EventId, SourceMovementId, PolicyVersionId, Payload, State` | Immutable event schema version; unique source; daily reconciliation |
| `LoyaltyReconciliationRuns` | `Id, BusinessId, Period, Type, TotalsJson, DifferenceMinor, Status, CompletedAtUtc` | Unique run key; drilldown mismatches without rewriting financial history |

**Schema caution:** a `LoyaltyAccountBalances` projection must not count both `ReservedPoints` and redeemed points as separate debits. When ledger is immutable, allocations represent ownership of units, not duplicate signed ledger movements. Rebuild checks must prove `GrossLedgerBalance = Σ PointsDelta` and `Spendable = max(0, eligible unexpired unreserved batch units after debt settlement)` (with policy/closure restrictions). Define exact signed-counter identity in code and property-based tests.

### 6.1 Required indexing and integrity

- Membership: `(BusinessId,NormalizedPhone)` uniqueness, `(BusinessId,CustomerId)` lookup, alias/merge lineage.
- Ledger: `(BusinessId,MembershipId,OccurredAtUtc DESC,Id)`, unique event keys and source provenance; no joins on phone to infer ownership.
- Batches: `(MembershipId,EffectiveExpiresAtUtc,Id)` filtered available/nonexpired; `(SourceSaleId,SourceLineAllocationKey)`.
- Reservations: `(MembershipId,State,ExpiresAtUtc)`, unique `(BusinessId,CheckoutId,IdempotencyKey)`.
- Sale snapshots: `(BusinessId,SaleId)` unique, `(MembershipId,CompletedAtUtc)`.
- Campaigns: `(BusinessId,Status,StartUtc,EndUtc)` plus budget concurrency record.
- Outbox/inbox: `(Status,NextAttemptUtc)` and unique `MessageId/ConsumerName`.
- Use DB-enforced FKs and constraints wherever possible; no unbounded cascading deletion of financial records.

## 7. Calculation engine and currency-safe allocation

### 7.1 Authoritative merchandise eligibility

Compute from immutable line-level facts, not a naïve invoice payable field:

```text
For each product line l:
  GrossIncluded(l)       = product consideration represented in sale snapshot
  IncludedVAT(l)         = VAT component INCLUDED in GrossIncluded(l); otherwise 0
  DiscountAllocated(l)   = actual line discount OR proportional invoice discount
  RedemptionAllocated(l) = proportional applied loyalty redemption to eligible product value
  EligibleMinor(l)       = max(0, GrossIncluded(l)
                                - IncludedVAT(l)
                                - DiscountAllocated(l)
                                - RedemptionAllocated(l))
InvoiceEligibleMinor = sum(EligibleMinor(l))
RawBasePoints = InvoiceEligibleMinor / configuredMinorUnitsPerPoint
GrossEarnedPoints = floor( RawBasePoints * (1 + (TierMultiplier - 1)
                                      + Σ EligibleCampaignMultiplierBonus)
                           + Σ EligibleFixedBonusPoints )
```

Use `decimal` or scaled integers for money, never `double`. A VAT-exclusive line already excluding tax has `IncludedVAT=0`, even if separate tax is added to invoice payable. Delivery, services/non-merchandise charges, tax and gift-card-like/noneligible tender are not earning base. **Acceptance example:** VAT-inclusive merchandise 1,000 − included VAT 100 − allowed discount 200 = eligible 700 → **7 Regular points**. If 100 redeemed points (=৳100 at current policy), eligible=600 → 6 points when redemption otherwise permitted. Validate source invoice schema before implementation: the order of tax/discount math must reproduce authoritative tax allocation and must not double-subtract.

Important reconciliation: `Σ line DiscountAllocated = invoice discount`, `Σ line RedemptionAllocated = redeemed eligible value`, `Σ line IncludedVAT = included VAT`, `Σ line EligibleMinor = InvoiceEligibleMinor`, and `Σ line EarnedPoints = InvoiceAwardedPoints` exactly. Clamp only after validating over-allocation as a business error; do not use clamping to conceal corrupted inputs.

### 7.2 Proportional allocation and deterministic remainder

Allocate invoice discounts proportional to applicable gross product line value, then allocate redeemed value proportional to discount-adjusted eligible line consideration (before redemption), using minor-unit exact arithmetic. Distribute integer-cent remainder by largest fractional remainder, deterministic tie-break by `(SaleLineId,QuantitySegmentId)` ascending; never depend on unordered collection iteration. For whole points, evaluate invoice raw points including eligible campaign benefits, floor once, then allocate earned points across relevant lines using deterministic largest-remainder. Persist quantity-segment allocation or a stable partition so partial-unit returns reverse exact quantities across multiple return orders. For fixed campaign bonus store a distinct campaign-award component with campaign-specific reversal/requalification policy rather than indistinguishably allocating all bonus points.

### 7.3 Returns, exchanges and expiry

- Return engine supplies original `SaleId`, `SaleLineId`, returned units and *existing* refund/credit settlement result; use original immutable allocation snapshot, not current price/policy/tax.
- Refund only paid/financially refundable portion according to existing Refund policy; loyalty-funded portion restores original FEFO batch allocations, never cash.
- Reverse line-assigned base/tier/multiplier awards capped by original units and previous returns; fixed spend thresholds reassessed against remaining eligible amount using *original* campaign version and correctly reverse the applicable bonus.
- If restored batch's original/effective expiry is already elapsed, record restore and same logical transaction expiration; no usable value created.
- Negative balance from already-spent earning reversal is a valid ledger debt; future earning first allocates to debt without a second debit; recovered points never redeem/expire again.
- Exchange: match original returned economic value against replacement, retain prior entitlement/expiry for equivalent value, reverse loss and issue incremental genuine eligible amount under *exchange-time* tier/campaign; equal-value swap does not reset validity or confer extra promotions. Preserve source sale/return/exchange references. New additional amount expiry derives from actual exchange completion.
- FEFO sorts `(EffectiveExpiresAtUtc, EarnedAtUtc, BatchId)` in a locked transaction; campaign-specific expiry and cumulative pause extension reflected in effective expiry.

### 7.4 Configuration lifecycle

Version earning divisor, redemption exchange value (default 1pt=৳1), minimum redemption (100), max redemption ratio (50% of eligible invoice), tier schedules, campaign caps, expiry policies, SMS/provider and fraud thresholds. Changes only future-effective, with permission/approval/audit as applicable. No historic repricing; retain applied version and serialized immutable rule fingerprint. Date-corrected existing Sales do not reprice earned credits. For new backdated Sales, use actual `CompletedAtUtc` for go-live, rate, tier, campaign and earning expiry; keep business date separately.

## 8. MediatR + CQRS application contracts

All write operations are `IRequest<Result<T>>` commands and all reads `IRequest<Result<T>>` queries dispatched via `ISender.Send`. Result/failure shapes and cancellation tokens follow existing application conventions. Event consumers should call application use cases, not controllers. Use MediatR `INotification` only for in-process application signals; **do not treat in-process notifications as durable delivery**.

### 8.1 Commands (proposed)

| Command | Handler responsibility | Permission / guard |
|---|---|---|
| `EnrollLoyaltyMemberCommand` | Consent, normalize phone, match Customer, create ActiveUnverified | `Loyalty.Member.Enroll`, Business active |
| `CreateProvisionalMembershipCommand` | 30-day claim, pending entitlement, invitation outbox | POS permission, verified Business/sale |
| `ActivateProvisionalMembershipCommand` | Owner consent + OTP, release eligible pending once | scoped challenge and deadline |
| `UpdateLoyaltyConsentCommand` | Append terms/marketing history; stop ineligible promos | member authorization |
| `RequestPhoneChangeCommand` / `ConfirmPhoneChangeCommand` | new phone OTP, uniqueness and phone audit | scoped owner verification |
| `RequestRecoveryCommand` / `ApproveRecoveryCommand` | manager identity evidence and approval kernel | explicit permission |
| `RequestMergeCommand` / `ApproveMergeCommand` | no live reservations, dedup transactions/campaigns, preserve history | `Loyalty.AccountMerge.Approve` |
| `ChangeProgrammeStatusCommand` | activate/pause/resume/terminate and clock windows | lifecycle permission, settlement gate |
| `PublishLoyaltyPolicyVersionCommand` | validate future-effective version and conflict | `Loyalty.Settings.Manage` |
| `UpsertTierPolicyCommand` / `ScheduleCampaignCommand` | validate tiers, caps, dates, rules | tier/campaign permissions |
| `PreviewLoyaltyCheckoutCommand`* | calculate read-only snapshot without writes | POS checkout read; *query preferable* |
| `RequestRedemptionOtpCommand` | checkout fingerprint and OTP request rate controls | POS + membership eligibility |
| `VerifyAndReserveRedemptionCommand` | verify OTP, lock account/batches, FEFO reserve for 15 min | POS, OTP + payment guards |
| `ReleaseRedemptionReservationCommand` | idempotent cancel/Hold/edit/expiry release | checkout owner/system |
| `FinalizeSaleWithLoyaltyCommand` | integrate existing Sale orchestrator + consumption + earning/outbox | core POS finalization auth |
| `ApplySaleReturnLoyaltyCommand` | original-line reversal, restore, debt, campaign threshold | event source / return permission |
| `ApplySaleExchangeLoyaltyCommand` | retained economic value + incremental benefit | event source / exchange permission |
| `SubmitManualPointsAdjustmentCommand` / `ApproveManualPointsAdjustmentCommand` | maker-checker, reason, immutable ledger posting | request / approve distinct |
| `CloseLoyaltyMembershipCommand` / `ReactivateLoyaltyMembershipCommand` | settlement, privacy/consent workflow | closure permissions |
| `RunExpiryBatchCommand` / `RunTierReassessmentCommand` | restart-safe jobs, local-date transitions | system identity only |
| `DispatchAccountingEventCommand` / `ReconcileLoyaltyCommand` | outbox, daily comparisons and alerts | system/accounting permission |

*Implement checkout preview as `GetLoyaltyCheckoutPreviewQuery`; listed command only to warn against mutating a preview. Never commit points from frontend preview numbers.

### 8.2 Queries

`FindLoyaltyMemberByPhoneQuery`, `GetLoyaltyMemberQuery`, `GetLoyaltyAccountBalanceQuery`, `GetLoyaltyCheckoutPreviewQuery`, `GetLoyaltyLedgerQuery`, `GetLoyaltyMemberStatementQuery`, `ListLoyaltyMembersQuery`, `ListLoyaltyAdjustmentRequestsQuery`, `ListLoyaltyCampaignsQuery`, `GetLoyaltyTierPolicyQuery`, `GetLoyaltyFraudAlertsQuery`, `GetLoyaltyFinancialExposureQuery`, `GetLoyaltyReconciliationReportQuery`, `GetLoyaltyDashboardQuery`, `GetLoyaltyPolicyHistoryQuery`. Paginate ledger/history; permission-based masking; no raw OTP or unrestricted phone in DTO.

### 8.3 Pipeline order

`Correlation/RequestContext → Authentication + Business selection → Authorization + location/resource scope → FluentValidation → Idempotency guard (writes) → Transaction/unit of work (where appropriate) → Handler → Audit/observability`. Adapt precise ordering to existing handlers, including validation-on-authorization safety. One transaction owner per logical checkout; no nested separate transaction commits in handlers. CancellationToken propagated. Retry transient DB failures only where execution strategy is safe and idempotency key is present.

### 8.4 Handler sketch (illustrative C#)

```csharp
public sealed record GetLoyaltyCheckoutPreviewQuery(
    long BusinessId, long SaleDraftId, long? MembershipId)
    : IRequest<Result<LoyaltyCheckoutPreviewDto>>;

public sealed record VerifyAndReserveRedemptionCommand(
    long BusinessId, Guid CheckoutId, long MembershipId,
    Guid ChallengeId, string Otp, long Points,
    string CheckoutFingerprint, string IdempotencyKey)
    : IRequest<Result<RedemptionReservationDto>>;

public sealed class VerifyAndReserveRedemptionHandler(
    ILoyaltyUnitOfWork uow, IOtpVerifier otp, ICheckoutSnapshotReader checkout,
    IClock clock) : IRequestHandler<VerifyAndReserveRedemptionCommand,
                                  Result<RedemptionReservationDto>>
{
    public async Task<Result<RedemptionReservationDto>> Handle(
        VerifyAndReserveRedemptionCommand cmd, CancellationToken ct)
    {
        // In one DB transaction: verify authoritative checkout + scoped OTP,
        // lock account and eligible earning batches, enforce 100-point minimum,
        // 50% eligible invoice limit, expiry, programme status and debt rules,
        // atomically allocate FEFO points; store 15-minute reservation;
        // consume challenge once and write audit. No provider SMS call here.
        return await uow.ReserveAfterOtpAsync(cmd, clock.UtcNow, ct);
    }
}
```

Avoid exposing a client-controlled `BusinessId` as authoritative. Bind to server-derived authenticated Business selection and compare any route body IDs; example contract above is illustrative.

## 9. API surface (proposed REST contract)

Prefix aligns with actual existing API versioning/conventions; endpoints below are **design candidates**, not asserted existing APIs. Require authentication except provider webhooks with verified signatures. All `BusinessId` and location scope server-derived. Error payload uses existing ProblemDetails envelope with stable machine-readable codes.

| Method and route | Operation | Key validation |
|---|---|---|
| `GET /api/loyalty/members/search?phone=...` | normalized scoped lookup | throttle, masked response, existence leakage controls |
| `POST /api/loyalty/members` | consent-based enrollment | unique phone, terms version |
| `POST /api/loyalty/provisional` | provisional claim | 30-day deadline, source checkout |
| `POST /api/loyalty/provisional/{id}/activate` | owner consent + OTP activation | deadline, exact account OTP |
| `GET /api/loyalty/members/{id}` | member view | business scope/permission |
| `GET /api/loyalty/members/{id}/balance` | account balance | limited cashier fields |
| `GET /api/loyalty/members/{id}/statement` | paginated statement | server filters + scope |
| `GET /api/loyalty/members/{id}/statement/export?format=pdf|xlsx` | audited export | explicit export permission |
| `POST /api/loyalty/checkout/preview` | read-only earning/redemption preview | authoritative cart read, no client financial trust |
| `POST /api/loyalty/redemptions/otp` | scoped OTP challenge | rate limits, checkout fingerprint |
| `POST /api/loyalty/redemptions/reservations` | verify OTP & reserve | 15-min TTL, atomic FEFO |
| `DELETE /api/loyalty/redemptions/reservations/{id}` | release | idempotent |
| Existing `POST .../sales/complete` | extend authoritative sale finalization | redemption invariant + durable earning |
| Existing Return/Exchange completion endpoints | invoke loyalty reconciler | original line snapshot |
| `POST /api/loyalty/adjustments` | submit approval request | reason/evidence required |
| `POST /api/loyalty/adjustments/{id}/approve` | approve + post | no self-approval |
| `GET/POST .../campaigns`, `.../tiers`, `.../policies` | admin management | versioned, scoped, audited |
| `GET /api/loyalty/ledger` | paginated immutable movements | limited sensitive history |
| `GET /api/loyalty/reports/...` | analytics and reconciliation | Business/Outlet granularity |
| `GET /api/loyalty/fraud-alerts` | reviews | Fraud.Review |
| `POST /api/loyalty/programme/activate|pause|resume` | lifecycle transition | authorized and safe gates |
| `POST /api/loyalty/members/{id}/close|reactivate` | closure/consent | settlement & legal retention |

Request headers: existing auth/session, `Idempotency-Key` on value-moving writes; W3C `traceparent` where supported. Error codes: `LOYALTY_PROGRAM_PAUSED`, `LOYALTY_MEMBERSHIP_UNVERIFIED`, `LOYALTY_OTP_EXPIRED`, `LOYALTY_OTP_ATTEMPTS_EXCEEDED`, `LOYALTY_RESERVATION_EXPIRED`, `LOYALTY_INSUFFICIENT_AVAILABLE_POINTS`, `LOYALTY_REDEMPTION_LIMIT`, `LOYALTY_ACCOUNT_DEBT`, `LOYALTY_CHECKOUT_CHANGED`, `LOYALTY_MERGE_CONFLICT`, `LOYALTY_POLICY_CONFLICT`, `LOYALTY_RECONCILIATION_RESTRICTED`. Do not return 401 for a valid session lacking feature permission; return 403 without forcing logout.

## 10. POS checkout orchestration and transaction pseudocode

```text
1. Read authenticated User/Business/Outlet/Terminal/Cash Session;
   validate permissions, active session, stock, immutable cart/version.
2. Resolve actual CustomerId independently from LoyaltyMembershipId.
3. Server calculates monetary line allocations and preview using effective
   sale-completion policy (never trust browser-calculated totals).
4. If redeeming: OTP → verified 15-min FEFO reservation bound to exact
   checkout/cart fingerprint + redemption amount.
5. Prepare external payment attempt with stable provider idempotency key;
   never hold DB locks while calling provider.
6. On confirmed payment (or permitted synchronous cash settlement), open
   database transaction, lock sale checkout and reservation/account rows;
   verify cart fingerprint, reservation unexpired, sale not completed, paid
   amount, unique command key and authoritative settlement state.
7. Persist Completed Sale + associated LoyaltyMembershipId + purchaser ID;
   consume reservation and post one redemption ledger movement;
   persist earning snapshot + one points earning movement/batches OR an
   idempotent durable earning entitlement event; persist accounting and
   notification outbox; commit together.
8. If DB finalization uncertain: query by idempotency/sale/payment refs;
   never blindly repeat provider capture or debit.
9. After commit: render 80mm receipt from committed facts; workers deliver
   non-OTP notifications and accounting messages. Loyalty consumer retries
   cannot duplicate award, redemption or journals.
```

**Failure matrix:**

| Failure | User-visible/operational result | Required recovery |
|---|---|---|
| SMS OTP provider down | Redemption unavailable; normal sale without redemption allowed | no reservation, bounded challenge retry |
| Loyalty read service down, no redemption | Normal sale continues | durable entitlement persisted with sale; status honest until credited |
| Redemption reservation expired | Do not finalize using discounted stale amount | release, reprice and new OTP; if paid externally, reconciliation |
| Cart/discount/customer changes after verification | Old authorization invalid | release and new quote/challenge |
| Two terminals race same points | Only first valid locked allocation succeeds | other request receives insufficient points/conflict |
| Payment uncertain | No blind retries/new capture | provider status query and reconciliation/compensation |
| DB failure before commit | Neither Sale nor redemption committed | retry idempotently after payment reconciliation |
| DB failure after commit, client timeout | Exactly one Sale/debit/award | replay returns authoritative completed receipt |
| Accounting worker down | Sale/points valid | outbox/inbox retries; daily mismatch alert |
| Notification worker down | Sale/points valid | async retry and dedupe, no extra points |
| Campaign cap collision | Atomic committed budget never exceeded | reject bonus or applicable policy; base earning remains |

## 11. OTP and redemption security implementation

- Generate codes via cryptographically secure RNG; hash keyed with server-controlled pepper/HMAC or supported secure verifier, never plaintext log, trace, or database. Encrypt/segregate phone recipient data; do not persist OTP SMS body containing code beyond minimum delivery requirement.
- Enforce attempt and send/request limits atomically per normalized phone + account + Business + appropriate IP/device risk dimensions (rate-limit abuse even with unknown phone). 60s resend; 5 requests/phone/hour; failed attempts max 5; five-minute TTL; one successful consume.
- OTP `purpose` distinguishes redemption, provisional activation and phone change; no purpose cross-use. Redemption binds CheckoutId, membership, points, value, fingerprint, challenge; changes invalidate challenge and reservation.
- Use an SMS adapter (`ISmsSender`, `ISmsProviderResolver`, `IMessageTemplateRenderer`) selecting configured Business provider, stored secrets through existing secure configuration/secret store. Do not expose API keys in policy JSON or client.
- SMS send is necessary to deliver OTP promptly; verification itself uses synchronous authorization API, but do not block DB transaction awaiting SMS provider. OTP send failure denies protected action; non-OTP confirmation retried asynchronously.
- Account recovery requires approved identity checks; high-risk recovery/merge followed by redemption emits fraud alert, not automatic unsupported forfeiture.

## 12. Campaign, tier and provisional processing

### 12.1 Tiers

Regular 0/1.0; Silver 20,000/1.25; Gold 50,000/1.5; Platinum 100,000/2.0 in BDT illustrations. Configurable Business policy. Source of tier spending = unique correctly attributed eligible completed Sale economic values in rolling 12 months, net of completed return/exchange treatment, with actual completion time for new backdated sales. Verified correctly linked historical sales may seed tier spending but **never generate retroactive points**. Upon threshold crossing after sale, promote tier immediately and use new multiplier only from *next* sale. First day of Business-local month, run idempotent downgrade assessment with one-month preserved-benefit grace, once per continuous below-threshold episode. Return updates spending immediately but downgrades only on monthly reassessment. Grace cancels when qualification recovers.

### 12.2 Campaigns

Birthday (first eligible completed birthday-month purchase, once/calendar year with DOB known at purchase); welcome (first eligible completed purchase); additive multipliers; fixed min-spend; product/category. Business-wide (not outlet-targeted under frozen D07). Fixed bonus stacking/exclusivity/priority per campaign; campaigns have per-sale, per-member, campaign-wide atomic caps; base/tier rewards never stopped by programme-wide campaign cap. Version campaign conditions, purchase-time checks, budget usage, return reversals, reclaim after full return if still within original window. Bonus expiry campaign-specific (default 12 months). On later return fixed minimum-spend bonus fully reverses if remaining eligible subtotal below original threshold. Build a deterministic evaluator with sorted priority and deduplicated usage records.

### 12.3 Provisional

One identity per normalized phone/Business; first provisional completed purchase sets 30-day claim time; subsequent purchases do not reset. Purchase-time qualified tier (including verified historical spend) and campaign policy captured as pending entitlement. Campaign budget atomically reserved, not consumed twice; release on return/claim expiry. Birthday bonus only if DOB/eligibility established at purchase; no retroactive claims. Campaign ended before owner activates: honour purchase-time eligible pending award if verified claim within window. Activate only with original phone-holder consent + SMS OTP before deadline; release pending once; original earning/expiration timestamps and campaign-specific rules maintained. Expire/forfeit unclaimed after window; no retroactive resurrection.

## 13. Returns, exchanges, merges and closure transaction patterns

- **Return:** lock source Sale snapshot + return idempotency key + related batch allocations. Assert remaining returnable quantity/value. Apply refund of paid consideration using existing payment/refund engine; restore source redemption batch amounts; expire restored units already past effective expiry; reverse earned line points and campaign bonus per original rules. If spent points unavailable, append debt adjustment; never overwrite prior redemption. Accounting event in same DB/outbox transaction; external money settlement reconciled separately.
- **Exchange:** use original returned value as economic credit against replacement. Keep reward/expiry for retained equal value; reduce entitlement for lower value; award current tier/campaign only on true additional eligible spending. No new expiry for unchanged value. Persist links to both Sale and Exchange/Return references.
- **Merge:** require approved authorization, verified identity, all active reservations resolved; lock both accounts in deterministic ID order, deduplicate unique source Sales, preserve batch origin/expiry/debt/ledger, consolidate without re-crediting original movements, reconcile duplicate identity-limited campaign rewards through explicit corrections, promote tier immediately if threshold crossed, archive/alias source rather than destructive delete.
- **Closure:** closure pending stops new earning, allows authorized point settlement/redemption and returns; settle or obtain informed forfeiture under approved terms, preserve financial/history. Marketing opt-out independent. Re-enrollment uses fresh terms/consent, existing identity, no spontaneous revival of forfeited points. Retention/anonymization undergoes law/finance assessment.

## 14. Notifications and message contracts

Durable outbox event contract (JSON example; exact serialization subject to existing event envelope):

```json
{
  "eventId": "guid",
  "eventType": "loyalty.points-earned.v1",
  "schemaVersion": 1,
  "occurredAtUtc": "2026-10-26T12:00:00Z",
  "businessId": 123,
  "membershipId": 456,
  "sourceSaleId": 789,
  "points": 7,
  "eligibleAmountMinor": 70000,
  "policyVersionId": 10,
  "correlationId": "guid",
  "idempotencyKey": "loyalty:earned:123:789:v1"
}
```

**Proposed events:** `loyalty.member-enrolled.v1`, `loyalty.provisional-invited.v1`, `loyalty.member-activated.v1`, `loyalty.points-earned.v1`, `loyalty.points-redeemed.v1`, `loyalty.points-reversed.v1`, `loyalty.points-restored.v1`, `loyalty.points-expired.v1`, `loyalty.tier-changed.v1`, `loyalty.campaign-awarded.v1`, `loyalty.adjustment-approved.v1`, `loyalty.account-merged.v1`, `loyalty.reconciliation-mismatch.v1`. Do not publish raw OTP, full phone, customer confidential attributes or sensitive payment data. Consumer inbox unique `(ConsumerName,EventId)`, then idempotent side effect + acknowledgment; retry/backoff/dead-letter. TTL/expiry reminders keyed by `MembershipId + ExpiryBatchGroup + ReminderDate + TemplateVersion`, 30 days before expiry. Promotional delivery requires current marketing consent and campaign eligibility at delivery. Earned points only on POS receipt, redeemed confirmation SMS+receipt, tier-change SMS, reminder SMS. Actual receipt uses committed balance; mask member number and show tier, earned, redeemed and available; respect 80mm printer width.

## 15. Accounting event integration and daily reconciliation

Keep loyalty movements separate from GL. Publish durable typed accounting events with `BusinessId`, `SourceMovementId`, `SaleId/ReturnId`, points and monetary value, versioned valuation assumptions, original transaction and accounting effective timestamps, requested journal semantics and schema version. **Do not hardcode journal debit/credit templates as approved before P0-G3 accountant sign-off.** IFRS 15-aligned *material-right* contract liability where applicable; relative standalone selling price, expected redemption and breakage policy must be validated for local standards and tax/VAT handling. Never equate face points value automatically to recognized GL liability.

Accounting consumer uses existing journal governance (permissions, fiscal period lock/close, controlled open-period corrective posting, unique source event), independently retryable. Completed Sales never reverted solely due to accounting outage. Daily reconciliation:

1. Count/source keys: completed loyalty-bearing sales and point movement source events, no duplicates.
2. Batch unit equation: minted/pending/available/redeemed/recovered/expired/reversed/restored reconcile with movement ledger.
3. Reservation invariant: reserved batch units match live reservations; no stale locked units.
4. Points + monetary subledger: opening + issued − redeemed − expired − reversed ± adjustments/restorations = closing (respect debt and nonduplicate allocations).
5. Accounting: expected posted events vs GL journal inbox; face value separate from recognized liability, breakage separately audited.
6. Differences: create traceable reconciliation finding, operational alert, and if **critical** restrict *affected redemption operations*, not normal unrelated Sales.

### Reconciliation evidence

Persist daily signed/run metadata, source event range, cutoff, mismatches, follow-up action and resolution audit. Never silently synthesize a journal to force totals to agree. Financial postings after closed periods follow existing accounting approval and open-period adjustment policies.

## 16. Permissions, audit and multitenancy

Minimum permission taxonomy (adapt to actual permission registration):

`Loyalty.Dashboard.View`, `Loyalty.Member.View`, `Loyalty.Member.Enroll`, `Loyalty.Member.Consent.Manage`, `Loyalty.Member.PhoneChange`, `Loyalty.Member.Recovery.Request`, `Loyalty.Member.Recovery.Approve`, `Loyalty.Member.Close`, `Loyalty.Ledger.View`, `Loyalty.Redemption.Execute`, `Loyalty.Adjustment.Request`, `Loyalty.Adjustment.Approve`, `Loyalty.AccountMerge.Request`, `Loyalty.AccountMerge.Approve`, `Loyalty.Campaign.Manage`, `Loyalty.Tier.Manage`, `Loyalty.Fraud.Review`, `Loyalty.Report.View`, `Loyalty.Statement.Export`, `Loyalty.Settings.Manage`, `Loyalty.Programme.Activate`, `Loyalty.Programme.Pause`, `Loyalty.Programme.Resume`, `Loyalty.Programme.Terminate`, `Loyalty.Accounting.Reconcile`.

Effective Business scope server-derived; Platform Super Admin may select a Business with authorized platform workflow and must not be blocked by inappropriate unconditional `RequireBusinessId()` on platform-level actions. Outlet staff can access Business-wide masked lookup/balance needed to transact, not another outlet's private Sale/refund or confidential fields. Staff assignments follow feature-specific location scope, *not* automatically inherited from unrelated inventory `all_locations`. Sensitive export audited with actor, selected scope, filters, timestamp and file reference. 401 only when unauthenticated, 403 when authenticated but forbidden; do not log user out on 403.

All value-moving actions produce immutable audit with `BusinessId`, `ActorId`, `OutletId?`, `TerminalId?`, `CashSessionId?`, `MembershipId`, `SaleId?`, request/event/correlation IDs, before/after *non-secret* facts, reason, approval version, timestamp, masked identifiers. Encrypt/segregate recipient and identity-check documents; protect account recovery evidence and obey lawful retention. Fraud monitor-and-alert, configurable thresholds; alerts do not falsely presume family-shared number use is wrongdoing.

## 17. Angular UI integration and routes

Use existing AblePro design language and POS component patterns. Only **one** customer phone/search control in POS customer section. Mask phone and show member/tier/balance/preview; Quick Registration modal/drawer; provisional invitation status; inline points input, 50% cap and 100-point minimum validation; OTP verification; 15-minute reservation countdown (server expiry authoritative); receipt from committed facts; Hold/edit releases reserved points. Preview refresh on cart/discount/customer changes. For loyalty outage show clear nonblocking message, *never invent credited balance*.

| Screen | Route |
|---|---|
| Dashboard | `/loyalty/dashboard` |
| Members | `/loyalty/members` |
| Member detail | `/loyalty/members/:id/view` |
| Points Ledger | `/loyalty/ledger` |
| Adjustments | `/loyalty/adjustments` |
| Adjustment detail | `/loyalty/adjustments/:id/view` |
| Campaigns | `/loyalty/campaigns` |
| Campaign editor | `/loyalty/campaigns/:id/edit` |
| Membership Tiers | `/loyalty/tiers` |
| Fraud Monitoring | `/loyalty/fraud-monitoring` |
| Reports | `/loyalty/reports` |
| Settings | `/loyalty/settings` |

Add `Loyalty` tab under existing `/customers/:id/view` (exact existing route to confirm). Route guards are UX only; API always verifies permissions. View/edit detail URLs are refresh/deep-link compatible. Reports: membership, tier mix, recurring sales, points movement, expiry, campaigns, face exposure, recognized liability and daily reconciliation. Statements view/PDF/Excel; paginated, stable order; masks/role scope. Full receipt shows masked member, tier, earned/redeemed/available, but not OTP or unrestricted family member history.

## 18. Background workers and scheduled jobs

| Worker | Trigger/schedule | Idempotency/recovery |
|---|---|---|
| `LoyaltyOutboxPublisher` | frequent polling/reuse existing publisher | claim/checkpoint/unique message id; retry/DLQ |
| `LoyaltyEarnRecoveryWorker` | durable sale entitlement | `BusinessId:SaleId:AwardKind`, load authoritative snapshot; no duplicate |
| `LoyaltyReservationExpiryWorker` | short polling | conditional state transition, row locks, exact-once release |
| `LoyaltyBatchExpiryWorker` | scheduled Business-local boundary | skip pause intervals, effective expiry, unique batch expiry movement |
| `LoyaltyProvisionalClaimExpiryWorker` | short polling | 30-day deadline, forfeit unclaimed, release campaign budget |
| `LoyaltyTierReassessmentWorker` | first of Business-local month | unique `(BusinessId,MembershipId,EvaluationMonth)` and grace episodes |
| `LoyaltyExpiryReminderWorker` | daily | grouped reminders T−30 days, persisted dedupe |
| `LoyaltyNotificationDispatcher` | queue | current consent, transient retry, delivery outcome |
| `LoyaltyAccountingDispatcher` | queue | contract-version, accounting inbox, retry |
| `LoyaltyDailyReconciliationWorker` | Business-local daily | deterministic cutoff, immutable findings, alert |
| `LoyaltyFraudRuleWorker` | event/periodic | rule version, deduplicated nonblocking alerts |

Worker tenancy isolation must not depend on HTTP user ambient context. Acquire explicit authorized system Business scope per message, validate source BusinessId and preserve correlation. Avoid time-of-day ambiguity around DST and use IANA/Windows mappings appropriate to actual runtime.

## 19. Migration and initial enablement plan

1. Inventory Customer phone formats, nulls, country normalization and duplicates **per Business**. Produce consent/identity resolution plan; never assume all Customer records are loyalty members.
2. Rehearse read-only data profiling and duplicate reports; use controlled review/merge and preserve original SaleId/CustomerId. Only enforce live normalized-phone uniqueness after P0-G1 evidence.
3. Apply additive/backward-compatible EF Core migrations with safe index creation, online limitations documented, rollback plan and no wipe of historical data. Database provider specifics require inspection.
4. Seed permission definitions and *disabled* Programme defaults per Business; version policy baseline includes approved earning/rates, tier thresholds, redemption and expiry values. No customer auto-enrollment without consent.
5. Validate correctly linked pre-go-live Sales for initial rolling-tier qualification **only**; no historical earned points, and do not count duplicate SaleIds.
6. Smoke: Owner/Platform Admin/Cashier/Outlet Manager access; invoice normal/discount/VAT; no 401→logout on 403; sale no-loyalty succeeds.
7. Stage end-to-end UAT including provider SMS and payments test mode, operational reconciliation, accountants and managers sign-offs; deploy disabled, authorize Business-local go-live and monitor.

## 20. Testing strategy and acceptance cases

**Mandatory test suites:** Domain calculation unit/property tests; MediatR pipeline/authorization validation tests; EF/provider-backed integration; API contract; Angular checkout; payment fault injection; concurrency load; outbox duplicate/reorder/restart; accounting/subledger balancing; report masking; migrations idempotence; staging UAT.

| Test ID | Case | Expected invariant |
|---|---|---|
| LLD-T01 | Inclusive VAT 1000, VAT 100, discount 200 | Eligible 700, Regular +7; VAT once |
| LLD-T02 | VAT-exclusive 900 + 100 VAT as separate tax | Eligible 900 before discounts; no double subtraction |
| LLD-T03 | 1250 eligible, Gold 1.5x | floor(18.75)=18 |
| LLD-T04 | Three item 4.8 raw each, partial return orders | 14 total, deterministic 5/5/4; caps and order independence |
| LLD-T05 | 2000 eligible less redeemed 100, Regular | 19 new points, only if protected redemption valid |
| LLD-T06 | Two terminals both reserve 200 of balance 300 | ≤300 allocated; one reject/conflict |
| LLD-T07 | OTP 5-min expiry, 5 failures, replay, changed checkout | rejection, audit, no debit |
| LLD-T08 | Hold/cart edit/15-min expiry | release exactly once, reverify |
| LLD-T09 | DB commit timeout after completed sale | retry returns same Sale/ledger movements |
| LLD-T10 | External payment unknown/captured, reservation expiry | no double capture/debit; compensation/reconcile |
| LLD-T11 | Return spent points, new award recovery | one debt, one earning, recovery allocation not extra debit |
| LLD-T12 | Restore points whose original expiry passed | restore + immediately expire, no spendable balance |
| LLD-T13 | Partial return loses minimum-spend threshold | fixed bonus reversed under original policy |
| LLD-T14 | Equal-value exchange with active new campaign | no new bonus or validity reset |
| LLD-T15 | Provisional with 3 sales / claim at 30-day deadline | release eligible pending only once or forfeit deterministically |
| LLD-T16 | Provisional campaign ends before activation | honoured purchase-time award, cap reserved |
| LLD-T17 | Monthly downgrade, one-month grace, return and recovery | correct effective tier, once per episode |
| LLD-T18 | Pause across expiries then two successive pauses | cumulative extension; no expired revival |
| LLD-T19 | Account merge duplicates underlying sale | one spending attribution, preserved batches/debt/expiry |
| LLD-T20 | Wrong Business or outlet route/id | denied, no data leakage or forced logout on 403 |
| LLD-T21 | Accounting consumer offline or duplicate message | sale valid, single journal when recovered, mismatch visible |
| LLD-T22 | Campaign cap simultaneous checkout | atomic budget, base earning continues |
| LLD-T23 | Birthday missing DOB at purchase then later supplied | no retroactive reward |
| LLD-T24 | Version change and backdated new Sale | actual completion policy, original business date retained |
| LLD-T25 | 30-day reminder and marketing opt-out | delivery deduped; marketing stops, transactional OTP stays |
| LLD-T26 | Manager requests/approves own adjustment | self-approval rejected |
| LLD-T27 | Ledger reconstruction after worker restart | immutable movements yield accurate balances/batches |
| LLD-T28 | Return from closed accounting period | permitted open-period adjustment only |

Use SRS AT01–AT30 as binding higher-level tests; no narrower LLD test substitutes for them. Explicitly exercise all 40 decisions and C01/C02 in traceable test cases.

## 21. Observability, NFRs and runbooks

Metrics: `loyalty_checkout_preview_p95`, `loyalty_balance_read_p95`, `loyalty_reservation_conflicts`, `loyalty_otp_send_latency/error_rate`, `loyalty_otp_verify_failures`, `loyalty_pending_entitlement_age`, `loyalty_outbox_oldest_age`, `loyalty_accounting_lag`, `loyalty_reconciliation_mismatch_count`, `loyalty_expiry_worker_lag`, `loyalty_negative_balance_accounts`, `loyalty_campaign_cap_utilization`. Correlate `SaleId`, `BusinessId`, event ID, checkout ID without full phone/OTP. Agree performance p95 from real baseline, not fabricated threshold. Alert on critical reconciliation, processing backlog, reservation leaks, payment ambiguity, and provider outages.

Runbooks: (1) pause/resume with cumulative clock adjustment; (2) no-loyalty normal sale under outage; (3) expired reservation/payment conflict; (4) stuck pending entitlement; (5) DLQ redrive preserving idempotency; (6) finance mismatch and restriction scope; (7) lost phone recovery/merge; (8) migration rollback; (9) safe deploy-disabled rollback without purging ledger. Provide operations permissions and incident audit.

## 22. Phased implementation and acceptance gates

| Phase | Deliverables | Gate before proceeding |
|---|---|---|
| P1 — Foundation | customer profiling, EF migrations, policy versions, permissions, audit, membership/consent/provisional skeleton | **P0-G1 migration/identity**, auth isolation, repeatable migration |
| P2 — Points core | VAT-safe calculator, snapshots, ledger, batches, returns, debt, FEFO, expiry | unit/property + provider integration + reconciliation tests |
| P3 — Checkout | OTP, 15-min reservations, checkout finalization, payment compensation, 80mm receipts | **P0-G2** payment failure and concurrency UAT |
| P4 — Rewards/admin | tiers, grace, campaigns, budgets, notifications, admin/UX, closures, fraud | campaign cap, privacy, permission and worker restart tests |
| P5 — Finance/reporting | typed accounting events, GL adapter, daily reconciler, statements/exports/dashboard | **P0-G3** accountant sign-off and ledger-GL staging reconciliation |
| P6 — Harden/rollout | full regression, load/security/recovery, monitoring, operator runbooks | mandatory SRS tests, staging UAT, deploy disabled, manual Business activation |

Each phase can ship code behind disabled flags but **does not authorize production points redemption or financial posting** before its dependent P0 gates and full release sign-off. No new customer master or microservice.

## 23. Three mandatory P0 implementation/production gates

### P0-G1 — Customer identity and uniqueness

**Required before membership live:** repository/schema data profile; Bangladesh phone normalization tests (`017...`, `88017...`, `+88017...` to same canonical E.164 where valid); duplicate/collision approval review; unique constraint deployment rehearsal; unambiguous existing-customer linkage; distinct purchaser/beneficiary; historical tier spend dedup; migration dry-run/re-run result and signed remediation audit. **Reject:** silent customer merge, points on imported historical Sales, migration that strands historical Sale references.

### P0-G2 — External payment consistency

**Required before redemption live:** inventory actual payment processor semantics (card/bank/mobile/cash/mixed); provider idempotency and status lookup; payment attempt state machine; charge-vs-db-commit fault injection; reservation expiry races; transient SQL failures; exact-once Sale/debit logical outcome; refund/compensation for captured but unfinalizable payments; monitoring and runbook. The SQL transaction cannot encompass external payment call. **Reject:** double capture, completed discounted Sale without committed points debit, redeemed points with no committed Sale, silent unknown payment result.

### P0-G3 — Accounting validation

**Required before financial posting live:** accountant-approved locally applicable recognition policy and breakage/valuation, signed VAT/refund assumptions, policy version IDs, chart of accounts and journal debit/credit mapping for sale/redeem/return/expiry/negative adjustment, closed-period treatment, subledger-to-GL daily reconciliation and replay tests. **Reject:** arbitrary 1pt=৳1 financial liability entry in place of approved accounting valuation; duplicate journals; force-updated historical GL/loyalty ledger.

## 24. SRS decision traceability matrix

| SRS IDs | Design locations |
|---|---|
| D01, D08, D13, D18, D24, D29, D33, D35, D37 | §§4–6, 8–9, 12–13, 16, 19, 23/G1 |
| D02, D03, D04, D09, D10, C01 | §§5–7, 10–11, 18, 20 |
| D05, D25, D28, D31, D32, D39 | §§6–7, 10, 13, 15, 20 |
| D06, D07, D12, D26, D34, D36 | §§4, 6–7, 12, 18, 20 |
| D11, D17, D21, D30 | §§8–9, 11, 16–18, 21 |
| D14, D15 | §§9, 14, 17–18 |
| D16, D40 | §§5, 10, 14–15, 23/G2–G3 |
| D19, D20, D22, D23, D27, D38, C02 | §§3, 5, 7–10, 17–23 |

**Implementation verification:** expand each SRS acceptance criterion into test IDs and link commands, API routes, entity indexes, migrations and release tickets. If repository discovery exposes a semantic mismatch, raise a documented design discrepancy rather than silently overriding the frozen SRS.

## 25. Open technical validations (not business-rule reopenings)

1. Exact provider (SQL Server/PostgreSQL), existing EF context and migration conventions; field types/names and tenant filter behaviour.
2. Precise Sale price/tax/refund/payment schema: inclusive/exclusive VAT flags, discount allocation order, multi-tender allocation, external capture semantics.
3. Existing Customer uniqueness/phone normalization and duplicate remediation evidence (P0-G1).
4. Existing Accounting posting event schema, approved GL mappings, valuation and legal/tax review (P0-G3).
5. Existing outbox, RabbitMQ, inbox, retry/DLQ and SMS secret storage/provider implementation contracts.
6. Business time zone localization, testing of first-of-month and DST handling, tier and birthday eligibility.
7. Performance baseline and agreed p95 thresholds; metrics/reporting storage choices using current platform capability.

These must be resolved against the actual codebase and sign-off evidence. They **do not** authorize changing the 40 approved business decisions or latest C01/C02 clarifications.

---

**End of CL-LLD-001 v1.0 — business baseline preserved; P0 gates remain open.**
