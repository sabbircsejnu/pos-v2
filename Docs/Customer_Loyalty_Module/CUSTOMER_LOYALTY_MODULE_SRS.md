# IraniMart POS v2 — Customer Loyalty & Rewards Module
## Software Requirements Specification (SRS)

| Document property | Value |
|---|---|
| Document ID | CL-SRS-001 |
| Version | 1.0 — frozen business baseline |
| Date | 26 October 2026 |
| Status | **Business requirements frozen; technical/production acceptance gates open** |
| Coverage | Approved Decisions **01–40**, plus explicit VAT/net-amount and MediatR/CQRS implementation clarifications |
| Intended location | `Docs/Customer_Loyalty_Module/CUSTOMER_LOYALTY_MODULE_SRS.md` |
| Next deliverables | LLD, database schema, API contracts, phased development and test matrix |

> **Change control:** No approved decision may be silently changed during implementation. Material business changes require explicit approval and a new version of this SRS. The three P0 acceptance gates in §20 are mandatory, not optional enhancements. The latest clarification mandates VAT exclusion and MediatR + CQRS.

## 1. Purpose, scope and outcomes

Deliver a business-scoped loyalty and rewards programme in the existing RetailPOS platform for IraniMart and other hosted businesses. Allow consent-based membership, family-number earning, immediate points on qualifying completed sales, verified redemption, returns/exchanges, configurable tiers and campaigns, financial reconciliation, fraud monitoring and reporting. Reuse existing Customer Master, POS checkout and Accounting. No new customer master is permitted.

**In scope:** Membership and consent; provisional membership; normalized phone identity; balances and immutable ledger; earning, redemption, reservation and OTP; VAT/discount/redemption-adjusted calculation; FEFO and expiry; negative-balance recovery; returns, voids and exchanges; configurable tiers, campaigns, notifications, reports and permissions; customer merge and closure; accounting events and reconciliation; rollout, recoverability and testing.

**Out of scope unless separately approved:** Points-to-cash conversion; cross-business points transfer; retroactive rewards on historical imports; replacing existing Sales/Customer/Accounting modules; implementation of a customer self-service app; offline redemption without server authorization; SMS provider/vendor selection; legal/tax determinations by software.

## 2. Normative vocabulary and system boundaries

- **Business:** Tenant whose loyalty programme, customer phone uniqueness, policy and balances are isolated from other businesses.
- **Customer:** Existing Customer Master record, which may be used as actual purchaser.
- **Loyalty membership / beneficiary:** Business-level loyalty account selected for the sale; may differ from actual purchaser.
- **CompletedAtUtc:** Actual system-completion timestamp for the sale, distinct from a permission-controlled `BusinessSaleDate`.
- **Eligible net product value:** Product selling amounts net of included VAT, permitted product/invoice discounts, allocated loyalty-redemption value and excluded non-product charges; never less than zero. See §5.
- **Earning batch:** Traceable points allocation with original earning timestamp, expiry, provenance and remaining redeemable units.
- **Ledger:** Immutable, idempotent points movement history; allocation references are not duplicate debits.
- **Provisional:** Unverified third-party phone enrolment with pending nonredeemable rewards until owner consent and verification.
- **Program pause:** Business-controlled suspension of earning and redemption, not financial reversals, audit or reconciliation.

The Business's currency is configured by the platform; BDT examples in this SRS illustrate IraniMart business policies. Monetary computations must use decimal/minor-unit-safe arithmetic, not binary floating point.

## 3. Preserved decision register (normative baseline)

| ID | Approved decision |
|---|---|
| D01 | Hybrid enrollment: lookup existing number, consent-based quick registration, normal sale without loyalty permitted. |
| D02 | Amount-based earning; after discount; configurable default **1 point per ৳100**. |
| D03 | **1 point = ৳1**; minimum redemption 100 points, at most 50% of eligible invoice; partial allowed; no cash conversion; SMS OTP mandatory. |
| D04 | Fixed 12-month earning-batch expiry, 30-day advance reminder, FEFO redemption. |
| D05 | Full/partial return reversals; restore attributable redeemed points; negative balances allowed; recover from future earnings; original restoration expiry; automatic exchange/return adjustment. |
| D06 | Configurable membership tiers, rolling 12-month net eligible spending, tier earning multipliers. |
| D07 | Birthday, multiplier, minimum-spend, welcome and product/category campaigns; additive multiplier stacking; business-wide campaigns. |
| D08 | Phone unique per Business; consent-based no-OTP normal enrollment; first redemption verifies SMS OTP; optional name/DOB; marketing consent separate; terms acceptance required. |
| D09 | Immediate points availability after successfully completed sale; none for draft/hold; reversals/idempotency; expiry starts at original earning date. |
| D10 | Whole points floored at invoice level after applicable multipliers; no minimum purchase; earn after loyalty redemption; taxes and non-product charges excluded. |
| D11 | All manual points adjustments require permission-based approval, no self-approval; mandatory reason; immutable audit; limited cashier history; business-wide balance. |
| D12 | Regular (0, 1×), Silver (৳20,000, 1.25×), Gold (৳50,000, 1.5×), Platinum (৳100,000, 2×); immediate upgrade applies next sale; monthly downgrade; **1-month** grace; business-configurable. |
| D13 | New-phone SMS OTP; lost-phone manager-approved recovery with identity checks; controlled permission-approved account merge, preserving history, expiry and audits. |
| D14 | Earned points on receipt; redemption confirmation SMS and receipt; 30-day expiry SMS; consent-based promotional SMS; tier-change SMS; configurable SMS provider; async durable non-OTP notifications. |
| D15 | Full masked loyalty detail on 80mm receipt; View/PDF/Excel statements; full analytics; permission/location-scoped reporting and audited exports. |
| D16 | IFRS 15-aligned contract-liability accounting where applicable; event-driven journals; **daily** reconciliation; configurable valuation; accountant validation required. |
| D17 | OTP 6 digits, 5 minutes, 5 verification attempts, 60-second resend cooldown, 5 requests/phone/hour; one-time transaction-bound secure OTP; configurable monitor-and-alert fraud detection; permission-based alert review. |
| D18 | Existing customers join only with consent; no historical sale points; verified correctly linked historical spend allowed for initial tiers; duplicate review; business-level go-live, idempotent migration. |
| D19 | Pause stops earning/redeeming; freezes expiry; manual resume; versioned policies; termination requires settlement; strict business isolation and retained ledger. |
| D20 | Loyalty in existing checkout Customer section, quick-registration modal/drawer, inline redemption, live preview; POS normal sale continues during loyalty outage; durable earning recovery. |
| D21 | Dedicated Loyalty & Rewards navigation, nine admin screens; Loyalty tab on existing Customer Profile; granular permissions; ID-based routed screens; business-wide balance and authorized outlet-scoped transactions. |
| D22 | Modular monolith in RetailPOS, existing database with isolated loyalty tables; transactional core plus async processing; phased acceptance-gated delivery; durable outbox/retries/reconciliation. |
| D23 | Mandatory critical automated/E2E/security/reconciliation gates; stage/UAT → deploy disabled → controlled authorized manual activation; measured performance baselines; emergency pause/rollback/recovery. |
| D24 | Shared-family purchase spending and points attributed to selected loyalty membership; actual purchaser separately preserved where available; same sale not double-counted. |
| D25 | Proportional line discount and redemption allocation; **invoice-level floor**, deterministic line distribution; original snapshots for partial returns; paid portion refunded and attributable redeemed points restored; cumulative caps. |
| D26 | Birthday once/year on first qualifying purchase in birthday month; welcome on first eligible completed purchase; fixed-bonus stacking, precedence, caps configurable; campaign-specific expiry default 12 months; reclaim on valid full return within campaign window. |
| D27 | Reserve points only after verified OTP, for **15 minutes**; release on Hold/cancel/expiry or relevant checkout changes; atomic concurrent balance check; commit once. |
| D28 | Automatic negative-balance recovery; negative redemption blocked; already-expired restored points re-expire; closed-period corrections in permitted open period; original allocations referenced. |
| D29 | Membership closure offers redemption or informed forfeiture; closure-pending blocks earning but permits settlement/returns; privacy retention review; fresh-consent reactivation; versioned consent; marketing opt-out separate. |
| D30 | Hybrid financial limits: base/tier earnings continue, campaign hard caps; exposure alerts, negative-balance monitoring, affected redemption restriction on critical mismatch; granular financial config permissions. |
| D31 | Base/tier partial returns use original allocated points; fixed minimum-spend bonus reassessed and reversed if threshold lost; original campaign snapshot; cumulative reversal ceiling. |
| D32 | Traceable debt recovery allocations (not a second debit); only unrecovered/unspent points expire; recovery precedes FEFO; gross earned/recovered/redeemed/expired/available separately reported. |
| D33 | Other person's unregistered phone permits provisional membership; **30-day** claim; SMS invitation; owner consent and verification to activate; pending points nonredeemable; existing-family account earning retained. |
| D34 | First-of-month local-time reassessment; retain benefits for **1-month** downgrade grace; returns immediately affect spending, monthly downgrade; cancel grace if recover; once per continuous episode. |
| D35 | Provisional 30-day clock from first purchase; no reset; multiple purchases accrue pending; original earning expiry; return adjusts pending; unclaimed forfeited; consent and OTP must complete before deadline. |
| D36 | Provisional tier and campaign eligibility evaluated at purchase under original snapshots; birthday only with DOB established then; honour valid campaign ending before activation; atomically reserve caps and release on expiry/return. |
| D37 | Merge rolling spend from deduplicated underlying sales; preserve batch/debt/expiry history; immediate post-merge upgrade from next sale; resolve active reservations; audit duplicate campaign correction. |
| D38 | Backdated sale earns under **actual completion** timestamp, including go-live eligibility, campaigns and tier spending; BusinessSaleDate retained for operational reports; existing completed sale date corrections do not reprice points. |
| D39 | Exchange original earning reconciliation, no duplicate equal-value rewards; only genuine incremental spend qualifies for current campaigns/tier; preserve prior expiry for retained value, new expiry on incremental earning. |
| D40 | Atomic sale and loyalty redemption; transactional or durable earning entitlement; accounting failures retry without undoing sale; cumulative pause-based expiry extension; async notification retry; separate actual purchaser and beneficiary IDs. |

**Approved latest clarification C01 — VAT/net calculation:** Explicitly deduct included VAT and discounts and any loyalty redemption from points-eligible merchandise amounts, excluding delivery and non-product charges. The user's reference example is Purchase amount ৳1,000 − VAT ৳100 − discount ৳200 = eligible ৳700, hence **7 base points**. VAT must not be deducted twice where purchase price is already VAT-exclusive. Refund/VAT government recovery or adjustments are governed by tax/accounting rules and are not determined by the loyalty engine.

**Approved latest clarification C02 — mandatory MediatR + CQRS:** Implement commands, queries, handlers and cross-cutting pipelines in the existing Clean Architecture, using MediatR with FluentValidation, transactions, authorization and idempotency. No controller-side points business calculations.

## 4. Actors and permission boundaries

Actors: Business Owner/Admin; authorized Loyalty Manager; Outlet Manager; Cashier; Accounting Manager; Platform Super Admin; customer/phone holder; background workers/system. Permissions must be granular, composable with existing role grants and wildcard/owner policy, and enforced server-side. Cross-business reads/writes denied, including by guessing identifiers. Platform Super Admin operations must use explicit authorized platform/business selection rather than unconditionally requiring a BusinessId.

Indicative permissions: `Loyalty.Dashboard.View`, `Loyalty.Member.View`, `.Enroll`, `Loyalty.Ledger.View`, `Loyalty.Adjustment.Request`, `.Approve`, `Loyalty.Campaign.Manage`, `Loyalty.Tier.Manage`, `Loyalty.Fraud.Review`, `Loyalty.Report.View`, `Loyalty.Settings.Manage`, `Loyalty.AccountMerge.Approve`; additionally distinct phone-recovery, programme-lifecycle, statement export, redemption and customer-closure permissions as needed in LLD. A requestor cannot self-approve an approval-controlled adjustment. Cashiers see balance/tier/sale-related points, not unrestricted historical activities. Balance is business-wide; sale history respects authorized outlet scope. All sensitive exports are audited.

## 5. Functional requirements — points and monetary calculation

### FR-05.1 — Eligibility basis

For each completed eligible sale, derive qualifying **net product consideration** after removing included VAT, approved product-wise **or** overall discount (mutually exclusive per existing POS), applied loyalty redemption and delivery/other non-product charges. Product-level and invoice-level components must reconcile without double-deducting VAT or discounts. Normalize to nonnegative eligible value.

- If displayed/invoice `PurchaseAmount` includes VAT, subtract VAT actually included in that amount once; if product value is already VAT-exclusive, do not subtract that VAT again.
- An invoice's payable figure may include/exclude tax or non-product items depending on the sale schema: **do not blindly equate payable with eligible amount**. Derive from verified item/tax/discount/redemption allocations.
- **Required worked example:** `PurchaseAmount=1000`, `VATIncluded=100`, `Discount=200`, `LoyaltyRedeemedValue=0`, `ExcludedOtherCharges=0` → `EligibleAmount=700`, Regular `floor(700/100)=7` points.
- If that example also redeems 100 points, eligible amount becomes 600 and Regular earns 6 points, provided redemption rules are independently satisfied.
- Exclusion of VAT is a loyalty earning policy. Tax refund treatment remains governed by applicable tax law and accounting controls; do not hardcode a legal assumption that remitted VAT is always nonrecoverable.

### FR-05.2 — Earning

Business default rate 1 point per eligible ৳100; tier multipliers Regular 1, Silver 1.25, Gold 1.5, Platinum 2, with configurable thresholds and rates. Evaluate applicable additive promotional multipliers and eligible fixed bonuses using authoritative policy versions; final invoice-level floor produces whole points. Persist an immutable awarded-point total and deterministic allocation across sold lines/quantities, with exact total equality. No points for Draft/Hold, abandoned checkout or pre-go-live historical imports. Credits become available immediately on successful completion unless provisional pending or offset against negative balance. Maximum exposure/point reward caps apply to campaigns as configured, not arbitrary denial of earned base/tier rewards.

### FR-05.3 — Redemption

Authorized redemption requires member account, minimum 100 points, max 50% of eligible invoice amount, available nonnegative balance, valid SMS OTP bound to Business, membership, checkout and fixed redemption amount, and a concurrency-safe 15-minute reservation after verification. Redeem points as payment-value reduction (1 point = ৳1 default), never cash conversion. Recompute points earning on product consideration remaining **after** redemption. Invalidate OTP/reservation on relevant checkout/amount changes, Hold, cancel or timeout; reauthorize on resume. Consume once in atomic sale completion; failure paths cannot double-spend.

### FR-05.4 — VAT-aware allocation/returns

Overall discounts allocated across lines proportional to original eligible merchandise value; redemption allocated proportional to post-discount eligible line values. Monetary rounding residuals distributed deterministically in minor units. Store invoice/line/quantity tax basis, VAT included/excluded flag, discounts, redemption, eligible amount, awarded points, policy/tier/campaign snapshot and actual completion timestamp. Return uses original snapshots, not current VAT rate or current prices. Reconcile returned paid portion and redeemed-point restoration; never issue a cash refund for loyalty-funded value. Actual VAT refund/credit-note logic remains existing tax/accounting module responsibility. Point reversal cannot exceed original award.

## 6. Functional requirements — membership and identity

**FR-06.1** Phone normalized consistently (BD format including +880 variants), unique per Business for customer/loyalty identity after controlled duplicate remediation. Search existing membership in checkout's Customer section; permit anonymous/non-loyalty sale. Registration via modal/drawer; phone and versioned terms acceptance required, name/DOB optional, marketing permission separately affirmative. Normal self-enrollment does not require registration OTP; phone verified on first redemption. Track verified/unverified state.

**FR-06.2** Existing active membership may be selected for another family member's purchase, without equating purchaser to beneficiary; redemption remains OTP-protected. Store actual CustomerId if available separately from LoyaltyMembershipId. Each sale's tier spend and earned points attributed to one membership only.

**FR-06.3** For another person's unregistered phone, create one provisional identity per Business+normalized phone; request holder consent through SMS; 30 days from first provisional purchase, not renewed by subsequent purchases. Store separate pending points and campaign-cap reservations; prohibit redemption. Activation requires holder consent + OTP before deadline, releases still-eligible pending entitlements with original sale timestamps/expiry. Return/void pending sales reduce pending rewards. Failed claim expires, forfeits pending rewards and releases campaign reservations, preserving audit. No silent owner-consent inference from cashier/purchaser.

**FR-06.4** Phone change requires OTP to new number and uniqueness validation. Lost-phone recovery requires identity evidence and manager approval. Controlled account merge requires explicit permission, resolved reservations, verified ownership and full non-destructive lineage; preserve original historic references, FEFO batch expiry, debt and duplicate-campaign resolution. Spending merged from **unique** original eligible sales, not sum of redundant aggregates. Qualifying higher tier effective after approved merge, benefits from next sale.

**FR-06.5** Customer loyalty closure: pending state stops new earning but allows controlled outstanding balance settlement and returns; redemption or informed forfeiture within applicable programme terms, preserved regulated/financial records, permitted deletion/anonymization after review. Reactivation reuses existing identity with fresh consent; forfeited points do not automatically reappear. Marketing opt-out does not close membership.

## 7. Functional requirements — tiers and campaigns

**FR-07.1** Business-configurable rolling 12-month **net eligible product spending**, aggregated by beneficiary membership and unique sale references. For new sales attribute to actual completion time; for reliable pre-go-live historical purchases use validated true dates as initial tier evidence only (no historical point credits). Initial tiers Regular 0/1×, Silver 20k/1.25×, Gold 50k/1.5×, Platinum 100k/2×. Immediate tier upgrades take effect on **next** sale. First-of-month Business-local downgrade review: one-month grace retaining prior benefits, one grace per continuous downgrade episode. Returns affect rolling net spend immediately, downgrade only at scheduled reassessment. Recovering spending cancels grace.

**FR-07.2** Campaign types: birthday (birthday-month first eligible purchase, once/calendar year); welcome (first completed eligible purchase); multiplier; minimum-spend; product/category. Business-wide campaign scope. Versioned schedules, eligibility, product/category/tier criteria, additive multiplier stacking, configurable fixed-bonus stacking, priorities/exclusivity, promotional expiry (default 12 months), usage and atomic budget caps. Birthday requires DOB established at purchase; no retroactive DOB-based bonuses. Return-aware reversals: line-attributable multiplier reward reversal and full minimum-spend reward cancellation when remaining eligible amount falls below threshold; eligible full-return welcome/birthday rewards reclaimable upon next qualifying sale **inside original campaign window**. All calculations use original policy snapshots. Provisional campaign entitlements reserve budgets atomically until activation, claim expiry or return. Honor a purchase-time-valid campaign even if its campaign closes before valid activation.

**FR-07.3** Exchanges reconcile original points/expiry and original sale return/redemption allocations. Equal-value economic replacement creates no incremental reward or refreshed existing expiry. On genuine incremental eligible payment, apply exchange-completion tier and eligible current campaigns only to incremental value, new earning expiry for incremental points. Lower-value exchange reduces original entitlements. Treat tax/discount allocation through original snapshots and exchange settlement. No duplicate award by exchanging repeatedly.

## 8. Functional requirements — ledger, expiry and negative balances

**FR-08.1** Append-only points ledger with per-source deterministic event identity; complete provenance (Business, customer/membership, sale/line, operator/outlet/terminal, policy version, campaign, timestamps). Store earning batches, original and adjusted expiry, FEFO redemption allocations, returns, expiration, debt allocations, merge adjustments and manual credits/debits. Never silently modify posted points; corrective entries reference original entries.

**FR-08.2** 12 months from earning, campaign-specific override where configured; 30-day reminder. FEFO consumes soonest-expiring available batches first with stable tie-breaker; remaining batch points only expire once, after excluding recovered/redeemed/previously expired units. Restored redeemed points inherit original expiry; those already expired restore/audit then immediately expire with no spendable increase. During programme pause, batches still valid at pause start have expiry frozen; on authorized resume extend by cumulative paused durations; do not revive pre-pause expiry.

**FR-08.3** Returns can make balance negative if original earning already spent. Gross new earnings are recorded; traceable portions automatically allocate to prior debt without a **second points debit**. Only residual eligible batch balance redeemable/expirable. Block redemption until available balance reaches minimum and no negative debt. Reporting distinguishes gross earned, recovered, redeemed, expired, adjusted, pending, reserved and available.

## 9. Functional requirements — programme lifecycle and operational policy

**FR-09.1** Business programme states Draft, Active, Paused and Terminated. Authorized manual activation after approved preflight and configured Business go-live timestamp. Draft inactive. Active allows permitted earning/redemption. Paused disallows new earning/redemption, freezes expiry, but permits return/reversal, accounting reconciliation, audit and security recovery. Resume manually, reschedule unexpired batch expiries cumulatively. Termination requires approved outstanding customer settlement plan, retention and accounting closeout. Business isolation is strict.

**FR-09.2** Version, schedule, audit and apply policy modifications prospectively. No retroactive repricing or mutation of already completed sale award snapshots. Backdated sale uses **actual completion**, not backdated BusinessSaleDate, for go-live, earning, tier and campaign eligibility; original BusinessSaleDate remains for operational reporting. Corrected sale dates do not automatically change points. Historical imported completed sales have zero automated loyalty credits.

**FR-09.3** All manual adjustments require reason, request and separate permission-authorized approval; no self-approval; settlement/negative-account interventions audited. Critical ledger-accounting mismatches restrict affected redemptions, not unrelated ordinary sales.

## 10. POS checkout and transaction reliability

**FR-10.1** Integrate loyalty into existing single Customer search (no duplicate search), membership lookup, quick-registration dialog, masked member/tier/balance, dynamic earning preview recalculated on cart, discount, **VAT**, loyalty redemption and eligibility changes. Inline redemption with OTP challenge, amount entry, limits, confirmation and remaining balance. Mask other persons' data. Keep POS free of extra blocking SMS calls except protected OTP authorization.

**FR-10.2** For same-database sale finalization, in one reliable transaction validate and persist sale, confirmed redemption debit, allocated batches, earning ledger or durable earning entitlement and outbox event as necessary. No successful sale may reflect an uncommitted loyalty redemption. External payment provider capture is not assumed atomic with database transaction; use a defined payment-finalization state machine, external idempotency, compensation and reconciliation to avoid duplicate charge/incorrect completion. See P0-G2.

**FR-10.3** If Loyalty subsystem or SMS provider unavailable, ordinary sale (without redemption) may finish. Durable record of eligible earning/recovery must be persisted at completion, then posted idempotently. Do not falsely advertise delayed credit as already available. OTP verification failure blocks only protected operation, not normal checkout. Hold releases reservation, resumed sale rechecks account and requires new OTP.

**FR-10.4** Both cash-session and terminal existing rules remain: session open required where POS requires it; sale attributed to proper session/completion event; no dependency on cash-session close for loyalty entitlement. Preserve existing 80mm receipt issuance with member phone masked, tier, earned points, redeemed points and current balance/accurate pending status as applicable; no OTP printed. Redemption confirmation SMS is asynchronous and retried.

## 11. Notifications, OTP and abuse monitoring

**FR-11.1** SMS OTP: random 6 digits; 5-minute expiry; max 5 validation attempts/challenge; 60-second resend throttle; 5 requests/phone/hour; server-side limits including relevant additional identifiers; one-time use; no plaintext persistence/logging; securely hashed verification material; bind to Business, membership, checkout, fixed redemption amount and purpose. Secure new-phone verification, recovery and provisional activation. On cart/amount change, invalidate prior authorization and release reservation.

**FR-11.2** Configurable Business-level pluggable SMS provider integration. Durable asynchronous notification processing through outbox/background worker: redemption confirmation SMS + receipt; tier change SMS; 30-day points-expiry SMS; consent-based birthday/campaign marketing SMS; earned points shown on receipt rather than earned-points SMS. Notifications retry with deduplication, bounded policy, dead-letter/alerts; no completed sale/points reversal due solely to notification outage. Expiry and marketing messages respect relevant communication preferences and legal requirements; OTP is a protected operational message.

**FR-11.3** Risk monitor and alert, not automatic blanket block of legitimate family account sharing. Configurable detection for repeated cashier beneficiary use, abnormal redemptions, OTP failures, excessive returns, account merges/recovery and manual adjustment frequency, unusual campaign budget consumption. Explicit review permission, protected audit trail, explainable event references. Mandatory core rate/concurrency/security controls operate regardless of optional monitor-only risk policy.

## 12. Administration UX, URLs and reporting

Nine administration screens in dedicated **Loyalty & Rewards** menu: Dashboard, Members, Points Ledger, Adjustments, Campaigns, Membership Tiers, Fraud Monitoring, Reports, Settings. Suggested Angular routes:

| Screen | URL |
|---|---|
| Dashboard | `/loyalty/dashboard` |
| Members | `/loyalty/members` |
| Member view | `/loyalty/members/:id/view` |
| Ledger | `/loyalty/ledger` |
| Adjustments | `/loyalty/adjustments` |
| Adjustment view | `/loyalty/adjustments/:id/view` |
| Campaigns | `/loyalty/campaigns` |
| Campaign edit | `/loyalty/campaigns/:id/edit` |
| Tiers | `/loyalty/tiers` |
| Fraud Monitoring | `/loyalty/fraud-monitoring` |
| Reports | `/loyalty/reports` |
| Settings | `/loyalty/settings` |

Route identifiers must survive refresh/deep links. Existing Customer detail page has Loyalty tab showing business-scope membership balances and permission-filtered activity. Statement view + PDF and Excel exports, account history and financial audit trails. 80mm POS receipt displays masked phone, member tier, earned/redeemed/balance accurately. Reports: new/verified members, rolling spend and tier distribution; points issued/expired/recovered/redeemed/returned/adjusted/available; outstanding face exposure vs accounting valuation; campaign spend and cap usage; repeat/loyalty-linked sales, average order value; negative balances; fraud alerts; GL subledger reconciliation, idempotent errors and exports. All report queries must enforce business and outlet authorization.

## 13. Accounting and financial requirements

**FR-13.1** Separate loyalty operational ledger/subledger from existing Accounting GL. IFRS 15-aligned deferred revenue/contract liability **where applicable**, using approved policy for transaction price allocation, estimated redemption, breakage and subsequent recognition. Do **not** assume 1 point face value always equals booked liability. Configurable valuation methods/inputs, effective versions and journal mappings require qualified accountant validation for applicable Bangladesh financial reporting/tax requirements.

**FR-13.2** Event-driven journal requests with durable outbox and idempotent consumers; immutable sources; posting/reversal against permitted open period, not rewriting closed-period journals. If Accounting journal fails, keep completed sale and loyalty points intact; retry and reconcile, with operational escalation. Reconcile daily issuance, redemption, expiry, adjustments and GL control-account balances; report mismatches and restrict only affected redemption operations on critical integrity mismatch. No duplicate journals or points in repeated event delivery.

**FR-13.3** Financial safeguards: base/tier rewards not arbitrarily halted by monthly caps; atomic campaign-specific issuance limits; configurable exposure warning/critical thresholds, negative debt and reconciliation alerts; granular permission to change financial policy, with audit. Preserve customer commitments subject to approved programme terms. Tax accounting/refunds are controlled by existing VAT/tax and financial processes, not the loyalty calculation engine.

## 14. Technical and architecture requirements (mandatory)

**TR-14.1 Modular monolith.** Implement in existing RetailPOS solution and transactional database with clearly isolated Loyalty domain/application/infrastructure modules and tables. Do not introduce separate microservice/database without formal ADR and approval. Integrate existing Customer, Sales, Return, Exchange, Accounting, permissions and outbox contracts.

**TR-14.2 MediatR and CQRS required.** Define explicit Application Commands for enrollment, activation, phone change, verification, redemption reservation/commit/release, awarding, reversals, merge, campaign/tiers, adjustments, settings, programme lifecycle and reconciliation. Define Queries for membership lookup, balance, preview, statement, reports, ledger and operational monitoring. Each has a MediatR request, handler and typed response. Preserve existing MediatR pipeline conventions, use FluentValidation in pipeline and business rules in domain services. Read queries must not mutate ledger or trigger new awards. Transactions, authorization and idempotency are enforced in suitable pipeline/application/infrastructure boundaries.

**TR-14.3 CQRS is not separate physical databases.** Commands and Queries may use optimized separate access strategies over same transactional database. Queries may use read DTOs/projections; writes remain authoritative. Async projections never become authoritative for redemption limits, currently available balance or atomic campaign cap enforcement.

**TR-14.4 Reliable delivery.** Outbox writes within same DB transaction as sale entitlement; at-least-once RabbitMQ/background delivery; consumer inbox/idempotency; retries with backoff, dead-letter diagnostics and operator reprocessing. Workers: SMS, points expiry/reminders, monthly tier downgrade, accounting event delivery/reconciliation, reporting projection and provisional claim expiry. Jobs must tolerate restarts and overlapping invocations. Preserve startup/operational observability.

**TR-14.5 Concurrency.** Database-level atomic balance/reservation check, row-level lock or optimistic compare-and-swap + retry with proven consistency; FEFO batch locks, unique source keys, capped return quantities, transactional campaign budget reservations; expired reservations atomically reclaimed. No duplicate checkout redemption under race or dropped messages.

**TR-14.6 Observability and privacy.** Structured, correlated audit logs; metrics for p95 member lookup, balance, preview, checkout; ledger errors, aged outbox, SMS delays, reconciliation gaps, OTP abuse and active reservations. Encrypt/protect phone and personal data per platform security policy; mask customer output and audit exports. No plaintext OTP or sensitive secrets in logs.

## 15. High-level logical data inventory (not yet physical schema)

Proposed bounded entities: `LoyaltyProgramme`, `LoyaltyPolicyVersion`, `LoyaltyMembership`, `LoyaltyConsentRecord`, `LoyaltyProvisionalClaim`, `LoyaltyTier`, `LoyaltyTierHistory`, `LoyaltyPointsLedger`, `LoyaltyEarningBatch`, `LoyaltyRedemptionAllocation`, `LoyaltyRedemptionReservation`, `LoyaltyOtpChallenge`, `LoyaltyCampaign`, `LoyaltyCampaignUsage`, `LoyaltyCampaignBudgetReservation`, `LoyaltyAdjustmentRequest`, `LoyaltyAccountMerge`, `LoyaltyFraudAlert`, `LoyaltyNotificationDelivery`, `LoyaltyReconciliationRun`, plus existing durable outbox/inbox records. Names are LLD proposals, not approved physical table identifiers. Link to existing Customer and Sale/Return/Exchange/Outlet/Terminal/User/Accounting references. All business-scoped entities must carry and enforce BusinessId. Provide unique deterministic keys for idempotency.

## 16. Non-functional requirements

- **Correctness:** Ledger conservation and immutable source references; deterministic rounding in money and points; exact one-time sale award/debit and returns; no negative stock or accounting side effects introduced by loyalty.
- **Availability:** Normal non-redemption sales can continue through loyalty/SMS outage with durable entitlement recovery. Protected redemption must fail closed until verified/committed.
- **Performance:** Measure real-world baseline and agree p95 lookup/preview/finalization targets; demonstrate no unacceptable regression to normal POS checkout; no synchronous SMS confirmation or historical aggregation on checkout hot path.
- **Isolation/security:** BusinessId/OutletId/access rules on APIs and background consumers; wildcard roles and Platform Super Admin correctly authorized; OTP anti-abuse, masking, audit and sensitivity controls.
- **Auditability:** Versioned consent, monetary allocation snapshot, policy/tier versions, challenge/reservation states, source event and actor timestamps, reasons and approvals; auditable exports.
- **Recoverability:** Outbox/inbox, retry, monitoring, daily reconciliation, controlled pause, safe schema/code rollback without deleting committed ledger.
- **Compatibility:** Retain existing Sales discounts (product-wise xor overall), terminals/cash sessions, 80mm receipt, Customer routes, Accounting FY/period controls and existing Clean Architecture/MediatR conventions.

## 17. Key acceptance scenarios

| ID | Test/input | Required observable result |
|---|---|---|
| AT01 | VAT-inclusive purchase 1000, VAT 100, discount 200, no redemption, Regular | Eligible net 700; **7 points**; VAT not double-deducted |
| AT02 | Net 1250, Regular | 12 points (invoice floor) |
| AT03 | Net 1250, Gold 1.5× | 18 points (floor 18.75) |
| AT04 | Product 2000, discount 200, no VAT component, Regular | 18 points |
| AT05 | Eligible product amount 2000, redeem 100 points, Regular | Earnings on remaining 1900 = 19 points; only after OTP+successful sale |
| AT06 | Provisional third-party number, multiple purchases within 30 days | Pending awards nonredeemable; no deadline reset; verified holder activation releases eligible awards once |
| AT07 | Expired provisional claim | Pending rewards forfeited, budget reservations released, audit preserved |
| AT08 | Partial return with proportional discount/VAT/redemption allocations | Paid portion refunded; attributable redeemed points restored with original expiry; original line-earned points reversed without exceeding cap |
| AT09 | 3 equal lines with raw 4.8 points each | 14 total invoice points allocated deterministically 5/5/4; return any subset reverses corresponding original allocation |
| AT10 | Same points redeemed concurrently on two terminals | Only valid serialized reservations succeed; no overspend |
| AT11 | 6-digit OTP wrong 5 times / past 5 min / replay / different amount | Rejected and logged; no debit |
| AT12 | OTP verified, checkout held or changed | Reservation released, old authorization invalidated |
| AT13 | Reserved points 15 min idle | Expire/release once; batches original expiry retained |
| AT14 | Return of points previously redeemed | Negative balance allowed; future earned points first recover debt with no second debit |
| AT15 | Points restored after their original expiry | Restored for audit and immediately expired; no spendable points |
| AT16 | Pause spans expiry date; resume | Unexpired-at-pause batches extended by cumulative duration; previously expired not revived |
| AT17 | Tier threshold crossed in completed sale | New tier next sale; downgrade monthly with one-month grace |
| AT18 | Full return drops minimum-spend campaign below threshold | Entire applicable fixed bonus reversed under original policy |
| AT19 | Equal-value exchange while new campaign active | No duplicate points, no new expiry; incremental spend only qualifies for new campaign/tier |
| AT20 | Historical sale import before programme activation | No backdated points; verified spend may qualify tier |
| AT21 | New permission-based backdated sale completed after go-live | Actual completion determines loyalty policy/expiry; operational business date preserved |
| AT22 | Merge accounts sharing duplicate sale | Deduplicated tier spend, preserved batches and source audit; merge blocked while active reservations unresolved |
| AT23 | Worker receives duplicate sale/Accounting message | Exactly one logical points credit/journal posting |
| AT24 | Accounting offline after successful sale | Sale and loyalty remain valid, retry queued; daily reconciliation exposes failure |
| AT25 | Loyalty unavailable; no redemption | Normal sale succeeds with durable earning entitlement and accurate pending-status UI |
| AT26 | Unauthorized outlet user fetches other-outlet sale history | Denied while permitted business-wide loyalty balance access remains |
| AT27 | Customer withdrawal of marketing consent | Promotion messages stop, membership and points unchanged |
| AT28 | Closure/re-enrollment | Outstanding settlement audited; fresh consent, no automatic revival of forfeited points |
| AT29 | Product line VAT-exclusive with VAT added separately | No double VAT subtraction; earnings on eligible net product consideration |
| AT30 | External payment result unknown during checkout | No duplicate charge/redemption; recoverable finalization or compensation with auditable state |

Additional mandatory tests: loyalty fraud review, campaign cap concurrency, 30-day expiry reminder dedupe, account recovery approval, manual adjustments no self-approval, return after closed GL period, policy-version boundary, report masking/export logs, runtime UAT and replay/restart behaviour.

## 18. Delivery approach and release readiness

Phased implementation with acceptance gates: (1) foundation, migration audit and permissions; (2) earnings/ledger/batches/returns; (3) checkout OTP/redemption/reservations and receipts; (4) tiers/campaigns/notifications/admin; (5) Accounting and reporting; (6) fraud/resilience/performance/production activation. These phases are logical delivery units and do not authorize early production redemption without required controls. Stage/UAT, deploy disabled, controlled Business-local go-live and authorized activation only after quality-gate sign-off. Emergency pause + safe rollback + event reconciliation; never erase committed rewards. Measure and agree p95 baseline before setting numerical acceptance target.

## 19. Assumptions and specification boundaries

- Existing POS sale/payment schemas, price-tax modes and return workflows must be mapped exactly in LLD; there is no assumption that the field named `PayableAmount` is always VAT-exclusive. The illustrated `1000−100−200=700` explicitly describes the user's business example, not a universal invoice tax schema.
- Configurable programme rates, tiers, campaign dates, provider credentials and accounting allocations are versioned Business settings; example values beyond approved defaults are not contractual.
- Refund treatment for VAT collected and remitted is determined by applicable Bangladesh tax/accounting rules. Do not conflate loyalty earning exclusions with tax recoverability.
- Distinguish marketing consent from loyalty terms and identity verification. Preserve applicable financial/audit records subject to lawful retention and privacy controls.
- LLD to define exact repository layout, database DDL/migrations, MediatR handler names/contracts, external provider adapter and Accounting event schema without altering frozen business requirements.

## 20. Three mandatory P0 implementation/production acceptance gates

### P0-G1 — Existing customer identity and duplicate phone remediation

Before activating membership unique constraints and issuing rewards: profile all existing customer phone formats and duplicates by Business; reconcile collision cases under authorized controlled process; preserve original sale/customer IDs; validate normalized-phone uniqueness; test historical tier attribution and merge deduplication. **Gate evidence:** repeatable migration report, conflict resolution audit, clean uniqueness verification and idempotent rehearsal.

### P0-G2 — Atomic checkout with external payment settlement

Specify provider-independent payment finalization state machine and transaction ordering; external payment cannot be assumed enlisted in SQL transaction. Implement external transaction idempotency, points reservation expiry handling, uncertain authorization/capture states, retries, compensation and reconciliation; verify sale-completed/redemption-debited invariant, no double-charges and no redemption without committed sale. **Gate evidence:** fault-injection and concurrent checkout test results, compensating transactions/reconciliation trace and runbook.

### P0-G3 — Accounting policy validation and GL posting contracts

Qualified accountant validates locally applicable IFRS 15 alignment (where applicable), reward fair-value/standalone-price or expected-redemption assumptions, breakage policy, VAT/refund treatment, ledger-to-GL mapping, journal event contracts, corrections and closed-period posting. **Gate evidence:** signed valuation policy and version, approved chart-of-accounts mappings, journal examples for sale/redeem/expiry/return/negative adjustments and reconciled staging ledger vs GL.

## 21. Freeze criteria, traceability and change management

The **business requirements are frozen** at Decisions D01–D40 plus clarifications C01 and C02. These decisions have acceptance-test and design traceability through sections 3–20. Implementation details for the P0 gates are pending evidence, not permission to weaken existing business rules. Any conflict discovered in code/schema must be documented with source, impact, options and explicit approval before deviating. Next LLD must map functional requirements to MediatR commands/queries, domain aggregates, EF migrations, transaction boundaries, OTP challenge protocol, worker events, SQL indexes, Accounting contracts and tests.

**End of SRS v1.0**
