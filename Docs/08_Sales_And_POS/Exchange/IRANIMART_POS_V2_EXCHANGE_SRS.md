# IraniMart POS v2 — Product Exchange Module
## Software Requirements Specification (SRS)

**Document ID:** IM-POS-EX-SRS-001
**Version:** 2.1 — Reconciled baseline (EX-01–EX-31, D-01–D-19, C-01–C-10, OC-01–OC-07; 67 approved decisions)
**Date:** 2026-11-07
**Status:** All 67 business and implementation decisions approved and frozen; **no business clarification remains open** (§26.3). Implementation not started.
**Related:**
- `IRANIMART_POS_V2_EXCHANGE_LLD.md` (v2.0)
- `IRANIMART_POS_V2_EXCHANGE_GAP_ANALYSIS.md` (v2.0)
- `IRANIMART_POS_V2_EXCHANGE_IMPLEMENTATION_PLAN.md` (v2.0)
- POS Master SRS/LLD v1.2
- `Docs/Customer_Loyalty_Module/`
- `Docs/Unit_Of_Measurement/01_UOM_SRS.md` (D08)
- `Docs/03_User_Management/OUTLET_ASSIGNMENT_RULES.md`

> **Authority.** EX-01–EX-31 (business), D-01–D-19 (implementation) and C-01–C-10 (conflict resolution) are approved and final.
> - Text marked *Interpretation* is a technical reading of an approved decision. It is not a new rule.
> - The clarifications in §26.3 (OC-01–OC-07) are approved owner decisions and are incorporated in the requirements.
> - Example amounts are illustrative only.

### Change log
| Version | Date | Change |
|---|---|---|
| 1.0 | 2026-11-07 | Approved business baseline EX-01–EX-31. |
| 2.0 | 2026-11-07 | Reconciled with D-01–D-19 and C-01–C-10 and with the verified repository. Changes: <br>• Proof-type selection removed (D-13). <br>• Five permissions only; high-value approval maps to `pos.exchange.override_window` (C-01, C-10). <br>• Supervisor credential approval added (D-04, C-02). <br>• Walk-in rule (D-03). <br>• Expected-drawer-cash rule (D-02). <br>• Loyalty runtime deferred (D-05). <br>• Mandatory initial policy (D-16). <br>• Synchronous accounting inside the Exchange transaction (C-04, C-06, D-15). <br>• Data-compatibility rules (C-09). <br>• Strict reversal (C-05). <br>v1.0 FR numbering is superseded by the numbering below. |
| 2.1 | 2026-11-07 | OC-01–OC-07 approved and incorporated. No renumbering; additions are FR-EX-027–029, 036–038, 116, 165 and AC-46–AC-52. Changes: <br>• Supervisor must have receiving-outlet access (OC-01). <br>• Chain override is a supervisor approval (OC-02). <br>• Two refund-authorization paths (OC-03). <br>• Legal-remedy escalation boundary (OC-04). <br>• High-value amount = max(return credit, replacement value) (OC-05). <br>• Walk-in phone-verification exception (OC-06). <br>• Window day counting from the local calendar date of purchase, Day 0 (OC-07); this replaces the v2.0 window interpretation in FR-EX-033. |

---

## 1. Purpose, scope, assumptions and exclusions

### 1.1 Purpose
Provide an auditable, secure, online-only, line-level exchange for goods sold at any outlet of the same business. A customer, or their representative, returns sellable goods from one or more original invoices and receives one or more replacement goods.
- Original invoices stay immutable.
- Only the signed difference is settled.
- The Exchange is recognised on its own business date at the receiving (Exchange) outlet.
- A dedicated Exchange receipt is issued.

### 1.2 In scope (this delivery)
- Entry from the POS quick action and from Sales History / Sale Details.
- Exchange of one or more original invoices: one invoice of any customer, or several invoices of the same registered customer.
- Partial quantities, multiple return and replacement lines, and repeat exchanges of the same invoice within the remaining quantity.
- Re-exchange of replacement items within the chain policy.
- Cross-outlet exchange within the same business.
- Configurable policy: window, chain, lower-price, phone verification, high-value threshold, negative target mode.
- Supervisor credential approval for window and chain overrides, high-value exchanges, and refunds by a cashier without refund permission (OC-02, OC-03, OC-05).
- Settlement:
  - additional collection by cash, card or mobile, including split;
  - refund by a supported method;
  - zero-difference exchanges.
- Cash-session binding, the 20-hour guard and the expected-drawer-cash refund limit.
- Stock movements at the Exchange outlet, using historical return cost.
- VAT and accounting journals dated on the exchange date.
- Salesperson attribution facts and outlet target integration.
- Exchange reporting and dashboard netting.
- Exchange History.
- 80mm receipt and PDF.
- Controlled reversal.
- Idempotency, concurrency safety and audit.

### 1.3 Assumptions (verified against the repository; see Gap Analysis §3)
- Original sale lines carry immutable allocations: `TaxableAmount`, `VatAmount` and `HistoricalUnitCost`, plus UOM snapshot columns.
- Sale lines already track cumulative `ReturnedQuantity`, protected by a database CHECK constraint and the sale row lock that refunds use.
- Business dates come from the cash session (company time zone). Original sales keep their own `BusinessDate`.
- Supported payment methods are `cash`, `card` and `mobile`. No payment-provider API integration exists.
- The Customer Loyalty Module is not implemented; checkout earns no points.

### 1.4 Exclusions (this delivery)
- Damaged or unsellable goods: rejected; no quarantine, repair or write-off flow (EX-05).
- Offline exchange or an offline queue (EX-28).
- Exchanges without the original invoice. A phone number alone is not sufficient (EX-24).
- Combining invoices of different customers, or several walk-in invoices (EX-23, D-03).
- Editing or deleting a completed Exchange (EX-14).
- Ordinary Sales Refund of Exchange replacement items (D-18).
- Loyalty runtime effects: points, redemption, OTP, tiers, campaigns, liability journals (D-05).
- Payment-provider API integration (D-09).
- A separate Exchange accounting-period closing subsystem (D-15, C-07).
- Automatic migration, backfill or reclassification of historical data (C-09, D-12).
- Store credit, restocking fees and gift receipts (not approved).

---

## 2. Business activation and policy configuration

| ID | Requirement | Source |
|---|---|---|
| FR-EX-001 | Exchange is **disabled for every business** until the Business Owner, or an admin with `settings.edit`, saves the first Exchange Policy. Until then the entry points are hidden or disabled with a reason, and every Exchange API returns `EXCHANGE_NOT_CONFIGURED`. | D-16 |
| FR-EX-002 | The Exchange Policy is business-scoped and **versioned (append-only)**. Each saved change creates a new version; earlier versions are never edited. | D-16, EX-03 |
| FR-EX-003 | Policy fields: <br>• `AllowLowerPriceExchange` (default **false**) <br>• `ExchangeWindowDays` <br>• `AllowReplacementReExchange` <br>• `ReplacementWindowDays` <br>• `MaxExchangeCountPerLineage` <br>• `PhoneVerificationMode` (`optional` / `required`) <br>• `HighValueApprovalThreshold` (empty = disabled) <br>• `NegativeTargetAdjustmentMode` (`track_only` default / `deduct_from_exchange_salesperson`) <br>• `IsEnabled` | D-16, D-07, EX-02/03/04/18 |
| FR-EX-004 | No numeric value is activated automatically. The UI may suggest example values (window 7 days, chain 2, replacement window 7 days), but the owner must enter and save them. | D-16 |
| FR-EX-005 | Each Exchange stores the policy version it applied, as an immutable snapshot. Later policy changes never alter completed Exchanges. | D-16, EX-14 |

## 3. Roles, permissions and supervisor approval

### 3.1 Permissions (exactly five Exchange permissions)
| Permission | Allows |
|---|---|
| `pos.exchange.create` | Start, look up, preview and confirm an Exchange; exact invoice lookup across business outlets (D-10) |
| `pos.exchange.refund` | Confirm an Exchange whose settlement direction is **refund**, as the cashier (in addition to `create`; OC-03 Case A). Also allows acting as supervisor for a **refund approval** for a cashier without it (OC-03 Case B). |
| `pos.exchange.override_window` | Act as supervisor for an **expired original-window override**, and for **high-value approval** (C-10) |
| `pos.exchange.override_chain` | Act as supervisor for a **replacement-chain override** (window or count). This is always an independent supervisor approval, never the cashier's own permission (OC-02). |
| `pos.exchange.reverse` | Reverse a completed Exchange |

| ID | Requirement | Source |
|---|---|---|
| FR-EX-010 | No other Exchange permission exists. `pos.exchange.approve_high_value` and `pos.exchange.view` must not be created. | EX-16, C-01, C-10, D-11 |
| FR-EX-011 | Exchange History, Details and receipts use the existing `sales.view` with business and receiving-outlet read scope. | D-11 |
| FR-EX-012 | All permissions are checked on the server, on the authenticated principal, at every request **and again at Confirm**. | EX-16, C-08 |
| FR-EX-013 | Role Create/Edit shows a "Product Exchange" permission group with the five keys. | EX-16 |
| FR-EX-014 | **Built-in role templates** for new businesses: <br>• Salesman: `create` <br>• OutletManager: `create`, `refund`, `override_window`, `override_chain`, `reverse` <br>• Business Owner: full authority (already all permissions) <br>Existing roles of existing businesses are **not** expanded automatically; owners grant the permissions through Role Edit. | D-17 |

### 3.2 Supervisor credential approval (window override, chain override, high-value, refund for a cashier without refund permission)
| ID | Requirement | Source |
|---|---|---|
| FR-EX-020 | When an Exchange needs any of the following, an **independent supervisor** must authenticate with their own credentials **on the Exchange screen at the cashier's counter**. The POS login session is never switched. <br>• a window override; <br>• a chain override; <br>• a high-value approval; <br>• a refund approval (cashier lacks `pos.exchange.refund`; OC-03 Case B). | D-04, C-02, EX-03/04/18, OC-02, OC-03 |
| FR-EX-021 | The supervisor must: <br>• be an **active** user of the **same active business**; <br>• hold the required existing permission (window → `override_window`; high-value → `override_window`; chain → `override_chain`; refund → `refund`); <br>• have **authorized access to the receiving outlet** (FR-EX-027); <br>• be a **different user** from the cashier. <br>The cashier's own permissions never replace a required supervisor approval. | C-02, C-10, D-04, OC-01, OC-02, OC-03 |
| FR-EX-022 | A mandatory approval reason is recorded per approval type. | C-02, EX-03/04 |
| FR-EX-023 | The approval is **one-time and short-lived**. It is bound to the business, receiving outlet, cashier, approval type and an immutable hash of the Exchange payload (source lines, quantities, replacements, settlement direction and values). | C-02 |
| FR-EX-024 | An approval becomes invalid on payload change, expiry, use, or when the supervisor's account or permission is disabled or revoked. Confirm re-validates every approval. | C-02 |
| FR-EX-025 | Passwords and credentials are never stored, logged, echoed or put in tokens. Failed attempts are rate-limited and audited without credential content. | C-02 |
| FR-EX-026 | Window override and high-value approval are **separate approval types and separate audit records**, even when the same supervisor grants both. High-value approval does not authorise refund, chain override or reversal. | C-10 |
| FR-EX-027 | **Supervisor outlet authorization (OC-01):** <br>• The supervisor's access to the receiving outlet is decided by the server from the supervisor's own active outlet assignments, or from an entitlement to all of the business's outlets (Business Owner). <br>• The outlet selected on the cashier's screen is never evidence of the supervisor's authorization. <br>• A revoked or ended assignment takes effect at approval and again at Confirm. <br>• This applies to every supervisor-authorized Exchange exception. | OC-01 |
| FR-EX-028 | **Multiple exceptions (OC-02):** <br>• Each required exception type needs its own approval with its own reason. <br>• One secure supervisor authentication may approve several explicitly listed types only if the supervisor holds every required permission. Each type is still recorded as a separate approval. <br>• The cashier can never approve their own exception. <br>• Confirm independently re-validates chain depth, window, high-value amount, refund need, approvals and policy. A changed requirement invalidates the affected approvals. | OC-02, C-02 |
| FR-EX-029 | **High-value amount (OC-05):** `HighValueAmount = MAX(TotalReturnCredit, TotalReplacementFinalValue)`. <br>• `TotalReturnCredit` = Σ return credit gross of all returned lines (original allocation, after discounts, incl. VAT; FR-EX-070). <br>• `TotalReplacementFinalValue` = Σ server-calculated replacement gross of all replacement lines (after discounts, incl. VAT). <br>• Both cover the whole Exchange (all invoices and lines). <br>• When the policy threshold is set (a positive amount), approval is required if `HighValueAmount ≥ HighValueApprovalThreshold`. <br>• The settlement difference and the split of payment methods are **not** used. <br>• The amount is recalculated at Confirm, and the approval is bound to the exact values. | OC-05, EX-18, C-10 |

## 4. Original invoice eligibility

| ID | Requirement | Source |
|---|---|---|
| FR-EX-030 | The customer or a representative presents the original printed receipt, or a genuine IraniMart-generated digital/PDF receipt, with the goods. | EX-24, EX-25 |
| FR-EX-031 | **Simplified verification (D-13):** the cashier scans or enters the invoice number from the presented receipt. The system validates the invoice and the returned items. There is **no** proof-type selection, no receipt checkbox, no QR authenticity check and no separate declaration. Invoice lookup by phone number or customer name is **not** offered for Exchange. | D-13, EX-24/25 |
| FR-EX-032 | A source invoice must: <br>• belong to the active business; <br>• have status `completed` or `partially_refunded`; <br>• not be voided or fully refunded. <br>Foreign-business invoices are reported as not found. | EX-24, C-08 |
| FR-EX-033 | **Exchange window day counting (OC-07):** <br>• Calendar days in the business's configured local time zone. The **original purchase local date is Day 0**. <br>• `LastEligibleDate = OriginalPurchaseLocalDate + ExchangeWindowDays`. The whole last eligible date is valid; the time of purchase does not shorten it. <br>• Example: sale on 1 Nov 2026 with a 7-day window has a last eligible date of 8 Nov. An Exchange on 8 Nov is allowed; on 9 Nov the window has expired. <br>• An expired window needs a supervisor window override (`pos.exchange.override_window`, FR-EX-020/021/027). | EX-03, OC-07 |
| FR-EX-034 | **Data compatibility (C-09):** a source line is eligible only if it has authoritative evidence: <br>• line taxable and VAT allocation; <br>• historical cost: the saved `HistoricalUnitCost`, or provably matching immutable stock-out/COGS evidence. <br>Missing evidence blocks that line with `EXCHANGE_SOURCE_EVIDENCE_MISSING`. Current cost or price is **never** substituted. | C-09, EX-09 |
| FR-EX-035 | Phone verification follows `PhoneVerificationMode`. When `required` and the source invoice belongs to a **registered** customer: <br>• the cashier enters the phone number given by the customer or representative; <br>• the server compares it with the phone recorded on the **original sale's customer**, never a loyalty account holder or another linked person; <br>• a mismatch blocks the Exchange. <br>The number is masked in every display and log. How this match is labelled, and the capability limitation, are covered in FR-EX-038. | EX-18, OC-06 |
| FR-EX-036 | **Eligibility date vs accounting date (OC-07):** <br>• Window eligibility uses the **actual local date of Exchange confirmation** (server time converted to the business time zone). It never uses the cash session business date. <br>• Financial posting, numbering and reporting keep using the cash session business date (EX-01, D-19). The two dates may legitimately differ. <br>• Confirm recalculates eligibility authoritatively, including when midnight passes between Preview and Confirm. <br>• The dates used and the policy version are recorded. <br>• The 20-hour session guard is unchanged. | OC-07, EX-15 |
| FR-EX-037 | **Walk-in phone exception (OC-06):** <br>• When verification is `required` and the source invoice is a walk-in sale with no phone attributable to the actual purchaser, phone verification is **skipped for that invoice**. The Exchange proceeds if all other rules pass (product presented, receipt presented, exact invoice lookup, product/variant match, remaining quantity, window and every other rule). <br>• A new phone number is never required for such an Exchange, and a newly entered number is never proof of the original purchaser. <br>• The shared synthetic `WALKIN` customer and any phone stored on it are never used as proof. <br>• The D-03 single-walk-in-invoice limit is unchanged. | OC-06, D-03 |
| FR-EX-038 | **Verification result labelling (OC-06):** <br>• The system records which outcome applied: `not_required`, `skipped_walkin` or `matched_customer_record`. <br>• It never records or displays a phone as "verified identity" just because a number is present or an unverified record matches. <br>• Customer phones currently carry no verification evidence (Gap Analysis G-45, VI-16). This limitation is documented, not hidden. | OC-06 |

## 5. Same-customer and walk-in rules

| ID | Requirement | Source |
|---|---|---|
| FR-EX-040 | A **single** invoice may be exchanged whatever its customer, including the business's walk-in customer. | D-03 |
| FR-EX-041 | Several invoices may be combined only when **all** belong to the **same registered (non-walk-in) customer** of the same business. | EX-22, EX-23, D-03 |
| FR-EX-042 | The shared synthetic walk-in customer is never proof of the same customer. Two or more walk-in invoices cannot be combined. | EX-23, D-03 |
| FR-EX-043 | A shared loyalty phone or family link is not proof of the same customer. | EX-23 |

## 6. Cross-outlet exchange

| ID | Requirement | Source |
|---|---|---|
| FR-EX-050 | An Exchange may use invoices sold at any outlet of the same business. | EX-06 |
| FR-EX-051 | Users with `pos.exchange.create` may look up an **exact invoice number** across the business's authorised outlets, for Exchange only. The lookup returns only Exchange-relevant data and grants no general cross-outlet Sales History access. | D-10 |
| FR-EX-052 | Return stock-in, replacement stock-out, settlement, cash session, accounting, salesperson attribution and reporting all belong to the **Exchange (receiving) outlet**. The original outlet's history is unchanged. | EX-06 |
| FR-EX-053 | The cashier must have write access to the receiving outlet under the existing outlet-assignment rules. | EX-06, C-08 |

## 7. Product condition and stock handling

| ID | Requirement | Source |
|---|---|---|
| FR-EX-060 | Each returned line must be confirmed as sellable and in good condition. Damaged or unsellable items cannot enter the Exchange. | EX-05 |
| FR-EX-061 | Accepted returned goods go straight into the receiving outlet's sellable stock. There is no quarantine. | EX-05, EX-06 |
| FR-EX-062 | Returned stock is valued at the original line's historical cost share (D-08). Replacement stock-out uses the authoritative replacement cost resolver. | D-08, C-09 |
| FR-EX-063 | Replacements need sufficient sellable stock at the receiving outlet. **Negative stock is never allowed.** | EX-06 |
| FR-EX-064 | Approved UOM rules apply: base-unit quantities, permitted fractional precision, whole-unit legacy lines, pack/packaging restoration per UOM D08 (RET-001…RET-007). | UOM D08 |

## 8. Original return credit and replacement pricing

| ID | Requirement | Source |
|---|---|---|
| FR-EX-070 | Return credit is the **original allocated paid value** (line taxable amount after line and invoice discount allocation, plus line VAT), shared proportionally for the returned quantity. The last portion absorbs the rounding residual, so all refunds and exchanges of a line add up exactly to the original. | EX-09, C-03 |
| FR-EX-071 | Current selling price, current discounts and current VAT are never used for return credit. | EX-09 |
| FR-EX-072 | Replacement lines use the price applicable on the exchange date, with discount and VAT calculated by the server. | EX-10 |

## 9. Discount and VAT handling

| ID | Requirement | Source |
|---|---|---|
| FR-EX-080 | Replacement discounts are product-wise **or** overall, never both, under the existing POS discount permissions and limits. | EX-27 |
| FR-EX-081 | Replacement discounts cannot change the historical return credit. | EX-27 |
| FR-EX-082 | VAT appears separately for the return leg (original rate and amount) and the replacement leg (current rate). Reports distinguish VAT-inclusive transaction value from net-of-VAT revenue. | EX-09/10/20 |

## 10. Partial and multi-invoice exchanges

| ID | Requirement | Source |
|---|---|---|
| FR-EX-090 | One Exchange may contain 1..N source invoices, 1..N return lines and 1..N replacement lines, with at least one of each. The counts need not match. | EX-21, EX-22 |
| FR-EX-091 | Remaining returnable quantity per original line = sold quantity − all quantities already returned **by refunds and exchanges together** (one shared entitlement). Over-return and concurrent double use are impossible. | C-03, EX-03 |
| FR-EX-092 | An invoice may be used in several Exchanges while quantity remains. | EX-03 |
| FR-EX-093 | Source-invoice and source-line traceability is kept for every return line. | EX-22 |
| FR-EX-094 | Returned items are selected by barcode scan or manual invoice-line selection. Product, SKU, variant (size/colour) and remaining quantity must match. | EX-26 |
| FR-EX-095 | An Exchange never changes the original sale's status. Voiding any sale with returned quantity > 0 (from a refund or an Exchange) is rejected with `SALE_HAS_RETURNS`. Existing refund behaviour, including its status labels, stays unchanged. | D-14 |

## 11. Replacement re-exchange and chain limits

| ID | Requirement | Source |
|---|---|---|
| FR-EX-100 | When `AllowReplacementReExchange` is on, a replacement item may be exchanged again within its replacement window and within `MaxExchangeCountPerLineage` along its lineage. <br>The replacement window starts from the **actual local confirmation date of the Exchange that issued the replacement**, never the original sale date (OC-07): `LastEligibleDate = ReplacementExchangeLocalDate + ReplacementWindowDays`. | EX-04, OC-07 |
| FR-EX-101 | **Chain override (OC-02):** exceeding the replacement window or the maximum chain count needs an independent **supervisor** chain override (`pos.exchange.override_chain`) with a mandatory reason. <br>• It is not a permission check on the logged-in cashier, and the cashier cannot self-approve. <br>• The approval is short-lived, Exchange-specific, payload-bound and one-time (§3.2). <br>• The supervisor must meet OC-01. | EX-04, C-02, OC-02, OC-01 |
| FR-EX-102 | Replacement-chain entitlement is tracked **per replacement line**, separately from the original sale line's entitlement. | C-03 |
| FR-EX-103 | Replacement items **cannot** be refunded through the ordinary Sales Refund flow. The available remedies are re-exchange and controlled reversal. For a legally mandated remedy see §17.5. | D-18 |

## 12. Additional payments and refunds

| ID | Requirement | Source |
|---|---|---|
| FR-EX-110 | **Positive difference:** collect by `cash`, `card` or `mobile`, including split payments. Split amounts must equal the difference exactly. Cash tender and change are recorded. | EX-07 |
| FR-EX-111 | **Negative difference:** allowed only when `AllowLowerPriceExchange` is on **and** the refund is authorized through one of the two paths in FR-EX-116. Otherwise the Exchange is rejected with no changes. | EX-02, OC-03 |
| FR-EX-112 | The refund may use any supported method, whatever was used originally. | EX-08 |
| FR-EX-113 | **Card and mobile refunds** are done outside the system and recorded by hand: method, amount, mandatory reference/evidence text, operator, timestamp and an explicit confirmation that the external refund succeeded. The system does not claim provider verification. | D-09 |
| FR-EX-114 | **Zero difference:** no money moves. A settled Exchange is still recorded. | EX-15 |
| FR-EX-115 | Payment and refund accounts come from the business's existing payment-account and COA mappings. If a required mapping is missing, Confirm is blocked; there is no generic or suspense fallback. | C-06 |
| FR-EX-116 | **Refund authorization paths (OC-03):** <br>**Case A:** the initiating cashier holds `pos.exchange.refund`. No extra supervisor approval is needed just because it is a refund. <br>**Case B:** the cashier lacks it. An independently authenticated supervisor holding `pos.exchange.refund` (and meeting OC-01, different user) authorizes the refund for that Exchange only. <br>Case B rules: <br>• The approval is bound to the Exchange payload **and** the refund amount and method(s). <br>• It is one-time, expires, is audited and is invalidated when any of these change. <br>• It is re-validated at Confirm. <br>• The cashier's permanent permissions never change. <br>Neither path bypasses: window override, chain override, high-value approval, the D-02 drawer-cash limit, D-09 non-cash evidence, account mapping, stock and quantity validation, or business policy. | OC-03, EX-02, D-02, D-09 |

## 13. Cash session and drawer availability

| ID | Requirement | Source |
|---|---|---|
| FR-EX-120 | Every Exchange, including zero-difference, needs an active cash session for the receiving outlet and terminal, under the existing 20-hour guard. It is re-checked at Confirm. | EX-15 |
| FR-EX-121 | A cash refund may not exceed the session's **system-calculated expected drawer cash** at the moment of Confirm, checked inside the transaction. The drawer may never go negative, and there is no shortage override. | EX-31, D-02 |
| FR-EX-122 | No physical cash-count screen is needed per refund. The cashier still checks the actual cash before paying (operating procedure). | D-02 |
| FR-EX-123 | Cash collected and paid out by an Exchange is part of the receiving session's expected cash and close reconciliation. The original sale's session is never changed. | EX-15, EX-06 |

## 14. Exchange preview and confirmation

| ID | Requirement | Source |
|---|---|---|
| FR-EX-130 | The workflow is mandatory: **Preview → Review → Confirm**. Preview writes nothing. | EX-29 |
| FR-EX-131 | The review shows: <br>• source invoices; returned and replacement lines; quantity and unit; <br>• return credit, replacement value, discounts and VAT; <br>• the signed difference and settlement; <br>• outlet, cashier, salesperson, reason and approvals. | EX-29 |
| FR-EX-132 | Confirm re-validates everything on the server: <br>• permissions and approvals; session and 20-hour guard; <br>• source eligibility and remaining quantity; prices, discounts and VAT; <br>• stock; drawer cash; payment accounts; <br>• accounting period; idempotency. <br>If the recalculated values differ from the reviewed preview, Confirm returns a conflict and requires a new preview. | EX-29, C-04 |
| FR-EX-133 | All authoritative changes of one Exchange commit **atomically or not at all**. No partially completed Exchange is ever visible. | C-04 |
| FR-EX-134 | A repeated Confirm with the same idempotency key returns the original result. A different payload with a used key is rejected. | EX-29, C-04 |
| FR-EX-135 | A reason is mandatory: **Size/Fitting**, **Colour/Design**, **Wrong Product Purchased**, **Customer Preference Changed** or **Other**. Other requires details. An internal note may be added and is never printed. | EX-17 |
| FR-EX-136 | The cashier selects the Exchange salesperson from the existing eligible-salesperson list. The cashier comes from the login. | EX-11 |
| FR-EX-137 | Exchange is online-only. When the client is offline the entry and Confirm are disabled, and no request is ever queued for later. | EX-28 |

## 15. Exchange numbering and receipts

| ID | Requirement | Source |
|---|---|---|
| FR-EX-140 | Exchange number = `{OutletPrefix}X{yyMMdd}{Sequence}`: <br>• `OutletPrefix` is the receiving outlet's invoice prefix; <br>• `yyMMdd` is the cash-session business date; <br>• `Sequence` is the daily sequence per (business, receiving outlet, business date), no padding. <br>It is generated on the server at successful Confirm, concurrency-safe and unique within the business. | D-19 |
| FR-EX-141 | Original invoice numbers are kept separately, as references; they are never rewritten. | D-19, EX-12 |
| FR-EX-142 | A dedicated Exchange receipt (80mm and PDF) shows: <br>• the Exchange number, date, outlet, cashier and salesperson; <br>• every original invoice number; <br>• returned and replacement lines with quantities and amounts; <br>• VAT, the signed difference, settlement methods and refund reference where recorded. <br>It is an immutable snapshot taken at completion; reprints use the same snapshot. Internal notes are never printed. | EX-12, EX-17 |
| FR-EX-143 | A receipt print failure never undoes an Exchange. Reprints are audited. | EX-12 |
| FR-EX-144 | The receipt shows no loyalty points while Loyalty is inactive. | D-05 |

## 16. Exchange History

| ID | Requirement | Source |
|---|---|---|
| FR-EX-150 | Exchange History search by: Exchange number, original invoice number, customer, date range, receiving outlet, salesperson, cashier, reason, status and settlement direction. Results are paginated and bounded. | EX-30, EX-20 |
| FR-EX-151 | Detail shows source invoices and lines, lineage, replacements, settlements (with masked references), approvals (supervisor, type, reason; no credentials), receipt history and reversal links. | EX-30 |
| FR-EX-152 | Sale Details links to every Exchange that used the sale. | EX-19, EX-30 |
| FR-EX-153 | Access: `sales.view` with business and receiving-outlet read scope (D-11). | D-11 |

## 17. Exchange reversal

| ID | Requirement | Source |
|---|---|---|
| FR-EX-160 | A completed Exchange cannot be edited or deleted. Correction is only by **controlled reversal** of the whole Exchange, which needs `pos.exchange.reverse` and a mandatory reason. | EX-14 |
| FR-EX-161 | Reversal is blocked if any replacement quantity of the Exchange has been used by an active downstream Exchange. There is no automatic cascade and no partial reversal. | C-05 |
| FR-EX-162 | Reversal checks: <br>• the returned goods are still available in the receiving outlet's sellable stock; <br>• the customer hands back the replacement goods; <br>• settlement compensation is possible (a cash refund of a collection needs drawer cash per D-02); <br>• there is an active session on the current business date; <br>• the accounting period is open; <br>• payment accounts are mapped. | C-05, D-02, D-15 |
| FR-EX-163 | Reversal posts **compensating** stock, settlement, accounting, salesperson-attribution and reporting facts on the **reversal business date**. Quantity entitlements are restored. The original Exchange and its receipts are preserved and marked reversed. | EX-14 |
| FR-EX-164 | **Change-of-mind vs legally mandatory remedy (D-18, OC-04):** <br>• The D-18 restriction (replacement items cannot use the ordinary Sales Refund flow) applies to normal change-of-mind requests. It must **not** be presented as overriding a legally mandatory customer remedy. <br>• For a possible statutory or consumer-rights complaint: <br>1. the cashier escalates to the OutletManager or Business Owner; <br>2. management reviews the circumstances and applicable requirements; <br>3. any required remedy is handled through an **existing authorized, auditable** process when one is available. <br>• The available system process today is controlled Exchange Reversal (FR-EX-160–163), after which the restored original-invoice quantity may be handled by the existing Sales Refund flow. <br>• Original sale and Exchange history are preserved. Any financial compensation must be traceable and reconciled. | D-18, OC-04 |
| FR-EX-165 | **Remedy limitations (OC-04):** <br>• Escalation documentation is not itself a financial remedy. <br>• Where no existing authorized process can deliver the required remedy safely, this is a documented operational/technical gap (Gap Analysis G-46). Examples: reversal blocked by a downstream Exchange or by unavailable stock, or a remedy other than reversal. <br>• Staff must not silently pay cash, create duplicate refunds or adjust inventory outside an authorized accounting process. <br>• No complaint or legal-remedy subsystem is part of this delivery. | OC-04 |

## 18. Salesperson and target attribution

| ID | Requirement | Source |
|---|---|---|
| FR-EX-170 | Each completed Exchange and each reversal records an immutable **salesperson attribution fact**: <br>• exchange/reversal ID, business date, receiving outlet; <br>• cashier, salesperson; <br>• positive amount, negative amount and signed net-of-VAT adjustment; <br>• applied `NegativeTargetAdjustmentMode` and policy version. | D-06, EX-01, EX-11 |
| FR-EX-171 | A positive adjustment increases the Exchange salesperson's attribution. | EX-01, D-07 |
| FR-EX-172 | A negative adjustment always reduces financial Net Sales. Under `track_only` (the default) it does **not** reduce the Exchange salesperson's target achievement. Under `deduct_from_exchange_salesperson` it does. | D-07 |
| FR-EX-173 | The original sale's salesperson and outlet attribution are never changed. | EX-01, EX-11 |
| FR-EX-174 | Outlet target achievement includes the signed Exchange adjustment at the receiving outlet on the exchange business date. Full individual Salesperson Target Management is deferred. | D-06 |

## 19. Financial reporting and dashboards

| ID | Requirement | Source |
|---|---|---|
| FR-EX-180 | Report, by business date and outlet: regular sales; Exchange replacement value; Exchange returned value (negative); signed Exchange adjustment; total net sales. Both VAT-inclusive and net-of-VAT measures, never mixed. | EX-20 |
| FR-EX-181 | Owner dashboard and mobile sales dashboard net sales include signed Exchange adjustments on the exchange business date, at the receiving outlet. Reversals apply on the reversal date. Original-date figures are never rewritten. | EX-01, EX-20 |
| FR-EX-182 | Historical legacy-linked sales (`OriginalSaleId`/`ReplacementSaleId`) are kept and not reclassified. The legacy warning stays. Blocker B-03 is closed only after reporting reconciliation is verified. | D-12 |
| FR-EX-183 | Dashboard and intelligence projections may be eventually consistent. Authoritative financial facts are never deferred. | C-04 |
| FR-EX-184 | Normal sale creation can no longer create legacy links: a request with `OriginalSaleId` gets `400 SALE_EXCHANGE_LINK_DEPRECATED`. | D-01 |

## 20. Deferred Loyalty integration

| ID | Requirement | Source |
|---|---|---|
| FR-EX-190 | The approved loyalty semantics (EX-13; Loyalty SRS D05, D25, D31, D39, AT08/AT09/AT14/AT15/AT19) stay part of the design. **Runtime integration is deferred** to task EXL-01. | EX-13, D-05 |
| FR-EX-191 | Not implemented now: points earning or reversal, redemption, loyalty OTP, tiers, campaigns, liability journals, reservations. There is no fake loyalty service and no synthetic point adjustment. | D-05 |
| FR-EX-192 | Each Exchange stores the immutable facts future integration needs (customer, source lines, credits, replacement values, actual completion time) and records `loyalty_status = module_inactive`. | D-05 |
| FR-EX-193 | Exchanges completed before Loyalty activation never receive retrospective points (Loyalty D18, FR-09.2). | D-05 |
| FR-EX-194 | Exchange must complete without any runtime dependency on Loyalty. | D-05 |

## 21. Accounting period restrictions

| ID | Requirement | Source |
|---|---|---|
| FR-EX-200 | Exchange Confirm and Reversal post their journals through the existing journal engine, inside the Exchange transaction. They are therefore subject to the **existing accounting-period guard** (posting only into an Open period). A closed, locked or missing period blocks Confirm or Reversal with no changes (`EXCHANGE_ACCOUNTING_PERIOD_CLOSED`). | D-15, C-07, C-04 |
| FR-EX-201 | No Exchange-specific period-closing subsystem is created. Limitations of the existing guard are documented in the Gap Analysis (G-25). | D-15, C-07 |
| FR-EX-202 | Exchange accounting is dated on the exchange or reversal business date, **never** the original sale's date. | EX-01, EX-14 |

## 22. Data compatibility

| ID | Requirement | Source |
|---|---|---|
| FR-EX-210 | Existing Sales, Refund, Customer and Cost data are preserved. There is no automatic migration or backfill. | C-09 |
| FR-EX-211 | Old invoices stay eligible when authoritative evidence exists (FR-EX-034). Otherwise the affected line is blocked with an explicit reason. | C-09 |
| FR-EX-212 | A read-only compatibility audit (Implementation Plan Stage 0) quantifies how many existing sales lack evidence before release. | C-09 |
| FR-EX-213 | Legacy link columns and their data are preserved (D-01, D-12). | D-01, D-12 |

## 23. Security and audit requirements

| ID | Requirement | Source |
|---|---|---|
| FR-EX-220 | The business is always resolved from the server-side scope, never from client input. Foreign invoice, exchange, outlet or customer IDs return "not found". | C-08 |
| FR-EX-221 | Cashier, business, prices, discounts, VAT, totals, dates, exchange number and settlement totals are never accepted from the client as authoritative. | EX-10, C-08 |
| FR-EX-222 | Disabled users and revoked permissions take effect at Confirm, approval and reversal. | C-08, C-02 |
| FR-EX-223 | Audit records cover: lookup, preview, approval granted or denied, confirm, rejection reason, receipt print/reprint and reversal. Each holds actor, supervisor (if any), business, outlet, correlation ID and timestamps. No credentials, OTPs or full phone numbers. | EX-14, C-02 |
| FR-EX-224 | A 403 response never forces logout. | Core rules |

## 24. Non-functional requirements

| Area | Requirement |
|---|---|
| Consistency | Single PostgreSQL transaction for all authoritative writes (C-04). Stable lock order. DB constraints are the last line of defence for quantities and stock. |
| Idempotency | Confirm and reversal need an idempotency key scoped to the business. Payloads are bound by fingerprint. |
| Concurrency | Concurrent refund and exchange, exchange and exchange, and exchange and sale on the same line or stock: at most one succeeds, the other gets a conflict (409). |
| Performance | Indexed exact-invoice lookup; bounded, paginated history. Latency targets are set after a measured baseline. |
| Availability | Online-only, with a clear network-failure message. |
| Observability | Structured logs with correlation IDs; alerts on failed confirms caused by configuration, period or evidence problems. |
| Compatibility | .NET 10 Clean Architecture, MediatR/CQRS, EF Core 9/Npgsql, Angular 21 standalone and signals, existing UI kit. No new frameworks. |

## 25. Acceptance criteria

| ID | Scenario | Expected result | Decisions |
|---|---|---|---|
| AC-01 | Oct 10 sale ৳10,000; Nov 7 return credit ৳2,000, replacement ৳2,200 | October unchanged; Nov 7 net +৳200 at the receiving outlet; Exchange salesperson attribution +৳200; Exchange receipt references the invoice | EX-01, EX-11, EX-12 |
| AC-02 | Replacement ৳1,700, lower-price disabled | Confirm rejected, nothing changed | EX-02 |
| AC-03 | Lower-price enabled, refund ৳300 by a supported method, cashier has `refund` | Nov 7 −৳300; refund recorded; reports correct | EX-02, EX-08 |
| AC-04 | Cash refund ৳300, expected drawer cash ৳200 | Cash refund blocked; another supported method allowed; no negative drawer | EX-31, D-02 |
| AC-05 | Original paid ৳1,800 after 10% discount on list ৳2,000 | Return credit ৳1,800 (with original VAT allocation) | EX-09 |
| AC-06 | Two invoices of the same registered customer, several return and replacement lines | One Exchange, one settlement, one receipt; per-invoice traceability | EX-21, EX-22 |
| AC-07 | Two invoices of different customers (even with the same loyalty phone) | Rejected | EX-23 |
| AC-08 | Original at outlet A, Exchange at outlet B | Stock, settlement, accounting and attribution at B; A unchanged | EX-06 |
| AC-09 | Barcode mismatch, or quantity already returned | Blocked; nothing posted | EX-26, C-03 |
| AC-10 | Window expired; supervisor approves with reason | Allowed if all other checks pass; supervisor and reason stored; blocked without approval | EX-03, C-02 |
| AC-11 | Replacement within its window and chain limit | Allowed; lineage kept; consumed quantity cannot be reused | EX-04 |
| AC-12 | Damaged item | Rejected; nothing posted | EX-05 |
| AC-13 | Representative presents the receipt; cashier enters the invoice number; validation passes | Allowed | EX-24, D-13 |
| AC-14 | Customer offers only a phone number | No phone-based invoice lookup exists; Exchange cannot start | EX-24, D-13 |
| AC-15 | No active session (zero difference) | Blocked | EX-15 |
| AC-16 | Session age ≥ 20 hours | Blocked | EX-15 |
| AC-17 | Client offline | Entry and Confirm disabled; nothing queued | EX-28 |
| AC-18 | Double click, or retry with the same key | Exactly one Exchange; no duplicate stock, cash, journal or attribution | EX-29, C-04 |
| AC-19 | Two terminals (refund vs exchange, or exchange vs exchange) claim the last quantity | One succeeds; the other gets 409 | C-03 |
| AC-20 | Reverse an Exchange whose replacement was re-exchanged | Blocked; nothing changed | C-05 |
| AC-21 | Reversal on a later date | Compensations dated on the reversal date; history not rewritten | EX-14 |
| AC-22 | Equal-value Exchange during an active campaign | **Deferred to EXL-01** | EX-13, D-05 |
| AC-23 | Partial return where loyalty redemption was used | **Deferred to EXL-01** | EX-13, D-05 |
| AC-24 | Replacement has a product-wise discount; overall discount attempted | Rejected; server VAT correct | EX-27 |
| AC-25 | Reason Other without details | Rejected | EX-17 |
| AC-26 | Cashier has receiving-outlet rights; invoice belongs to another business | Not found; nothing disclosed or changed | C-08 |
| AC-27 | Receipt print fails after commit | Exchange stays completed; reprint allowed | EX-12 |
| AC-28 | Card or mobile refund without reference or confirmation | Rejected; with them, recorded as manual evidence (no provider claim) | D-09 |
| AC-29 | `POST /api/sales` with `OriginalSaleId` | 400 `SALE_EXCHANGE_LINK_DEPRECATED`; existing links unchanged | D-01 |
| AC-30 | Two walk-in invoices combined / one walk-in invoice | Rejected / allowed | D-03 |
| AC-31 | Supervisor approval: <br>• (a) same user as cashier; <br>• (b) wrong credentials; <br>• (c) missing permission; <br>• (d) other business; <br>• (e) reused; <br>• (f) expired; <br>• (g) payload changed; <br>• (h) permission revoked before Confirm; <br>• (i) no access to the receiving outlet | All rejected; no credentials logged; POS session unchanged | D-04, C-02, OC-01 |
| AC-32 | High-value Exchange | Supervisor with `override_window` approves; recorded as type `high_value`, separate from `window` | C-10 |
| AC-33 | Void a sale that has returned quantity > 0 (refund or exchange) | Rejected | D-14 |
| AC-34 | Business without a saved Exchange Policy | Exchange unavailable | D-16 |
| AC-35 | Ordinary Sales Refund of a replacement item | Not possible | D-18 |
| AC-36 | Concurrent confirms at the same outlet and date | Unique sequential numbers `{Prefix}X{yyMMdd}{n}` | D-19 |
| AC-37 | Payment-account mapping missing for the settlement method | Confirm blocked; nothing changed | C-06 |
| AC-38 | Accounting period not open for the exchange or reversal business date | Confirm or reversal blocked; nothing changed | D-15, C-07 |
| AC-39 | Source line without historical cost or allocation evidence | Line blocked with explicit reason; no current-cost substitution | C-09 |
| AC-40 | Simulated failure after the stock write inside Confirm | Whole Exchange rolled back | C-04 |
| AC-41 | Outlet-restricted cashier: exact lookup of another outlet's invoice | Exchange data returned; Sales History of that outlet still hidden | D-10 |
| AC-42 | Exchange History for an outlet-restricted user | Only exchanges whose receiving outlet is in the user's read scope | D-11 |
| AC-43 | Negative adjustment under `track_only` | Net sales reduced; salesperson target achievement not reduced; fact recorded | D-07 |
| AC-44 | Loyalty inactive | `loyalty_status = module_inactive`; no points on the receipt | D-05 |
| AC-45 | Disabled or revoked cashier or supervisor | Confirm, approval and reversal rejected | C-08 |
| AC-46 | Supervisor outlet access: <br>(a) same business, assigned to the receiving outlet → allowed; <br>(b) assigned only to another outlet → rejected; <br>(c) Business Owner (all outlets) → allowed; <br>(d) assignment revoked after approval and before Confirm → Confirm rejected; <br>(e) cashier's selected outlet context used as the only evidence → not accepted | As stated | OC-01 |
| AC-47 | Chain override: <br>(a) limit exceeded, no supervisor approval; <br>(b) supervisor lacks `override_chain`; <br>(c) supervisor lacks outlet access; <br>(d) cashier self-approval; <br>(e) expired approval; <br>(f) payload changed; <br>(g) concurrent reuse of one approval; <br>(h) chain depth changes before Confirm | (a)–(g) rejected; (g): only one Confirm consumes the approval; (h): recalculated and approval invalidated if the requirement changed | OC-02 |
| AC-48 | Refund: <br>(a) cashier has `refund` → no refund approval needed; <br>(b) cashier lacks it, valid supervisor refund approval → allowed; <br>(c) invalid supervisor; <br>(d) expired approval; <br>(e) refund amount or method changed after approval; <br>(f) refund plus window plus high-value exceptions together | (c)–(e) rejected. (f): each exception needs its own approval record; a refund approval alone does not satisfy the others; the D-02 drawer limit and D-09 evidence still apply. | OC-03 |
| AC-49 | Customer raises a possible legally mandatory remedy for a replacement item | The ordinary Sales Refund of the replacement stays unavailable. The cashier escalates. Management uses controlled reversal, if eligible, then the existing refund. If blocked, the case is recorded as an operational gap; no cash or inventory change is made outside authorized processes. | OC-04, D-18 |
| AC-50 | High value (threshold 10,000): <br>(a) return 8,000 / replacement 9,000 → amount 9,000, no approval; <br>(b) 12,000 / 9,000 → 12,000, approval; <br>(c) 5,000 / 5,000 → 5,000; <br>(d) 50,000 / 50,000 → 50,000, approval (zero difference); <br>(e) amount exactly 10,000 → approval; <br>(f) 9,999.99 → none; <br>(g) multiple invoices and lines aggregated; <br>(h) threshold not set → none; <br>(i) price, discount or VAT changed between Preview and Confirm | Approval need follows `max(return credit, replacement value) ≥ threshold`. Split payments are irrelevant. (i): recalculated; approval invalidated when the bound values change. | OC-05, C-10 |
| AC-51 | Phone verification `required`: <br>(a) walk-in invoice without a purchaser phone → skipped; <br>(b) walk-in with the synthetic `WALKIN` phone → not used, skipped; <br>(c) registered customer, matching phone → allowed (`matched_customer_record`); <br>(d) mismatch → blocked; <br>(e) mode `optional` → not required; <br>(f) newly entered phone for a walk-in → not treated as proof; <br>(g) loyalty phone of another person → not accepted as a match | As stated | OC-06, D-03 |
| AC-52 | Window dates (company time zone): <br>(a) same-day Exchange allowed; <br>(b) last eligible date (Day N) allowed; <br>(c) Day N+1 expired; <br>(d) purchase at 23:50 local counts as that local date; <br>(e) session business date ≠ local confirmation date → eligibility uses the local date, posting uses the session date; <br>(f) replacement window counted from the issuing Exchange's local date; <br>(g) month-end and 29 Feb boundaries; <br>(h) DST transition (test zone with DST); <br>(i) supervisor window override; <br>(j) Confirm after crossing midnight → re-evaluated | As stated | OC-07 |

## 26. Deferred / out-of-scope features and clarifications

### 26.1 Deferred
- **EXL-01** Loyalty runtime integration (D-05). AC-22/AC-23 are its release gates.
- Individual Salesperson Target Management (D-06).
- Receipt QR or authenticity verification (D-13).
- Payment-provider integration and provider-verified refunds (D-09).
- Drawer guard for existing refunds and drawer expenses (outside Exchange scope).

### 26.2 Out of scope
Damaged-goods workflows, offline exchange, store credit, restocking fees, cross-business exchange, and editing or deleting completed Exchanges.

### 26.3 Clarifications — all Approved / Resolved (2026-11-07)
No business clarification remains open. Resolution of a clarification does not mean code is implemented (see Gap Analysis).

| ID | Approved option | Resolution | Requirements | Tests |
|---|---|---|---|---|
| OC-01 | A | Supervisor must have server-verified access to the receiving outlet (assignment, or Business Owner all-outlet entitlement) | FR-EX-021, FR-EX-027 | AC-31(i), AC-46 |
| OC-02 | A | Chain override always needs an independent supervisor with `override_chain`; multiple exceptions are approved and recorded separately | FR-EX-028, FR-EX-101 | AC-47 |
| OC-03 | B | Refund: cashier with `refund` (Case A), or supervisor refund approval bound to the amount and method (Case B); no bypass of other controls | FR-EX-020, FR-EX-021, FR-EX-111, FR-EX-116 | AC-48 |
| OC-04 | A | Change-of-mind restriction vs legal remedy escalation through existing authorized processes; gap documented; no new subsystem | FR-EX-164, FR-EX-165 | AC-49 |
| OC-05 | C | `HighValueAmount = MAX(TotalReturnCredit, TotalReplacementFinalValue) ≥ threshold` | FR-EX-029 | AC-50 |
| OC-06 | A | Walk-in invoices without an attributable phone skip verification; registered customers are matched against the original customer's record; honest result labels | FR-EX-035, FR-EX-037, FR-EX-038 | AC-51 |
| OC-07 | A | Local calendar days, purchase = Day 0, the whole last day valid; replacement window from the issuing Exchange's local date; eligibility date ≠ accounting date | FR-EX-033, FR-EX-036, FR-EX-100 | AC-52 |
