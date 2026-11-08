# IraniMart POS v2 — Product Exchange Module
## Low-Level Design (LLD)

**Document ID:** IM-POS-EX-LLD-001
**Version:** 2.2 — Reconciled with the verified repository and with D-01–D-19 / C-01–C-10 / OC-01–OC-07. Stage 0 evidence corrections applied (`IRANIMART_POS_V2_EXCHANGE_STAGE_0_TECHNICAL_AUDIT.md` §19); no business behaviour changed.
**Date:** 2026-11-07
**Status:** Design. **Nothing in §3 onward exists in code yet.** Every new table, class, endpoint and error code is **Proposed**. Existing artefacts are cited by verified path. Verification items are tagged `VI-nn` (Gap Analysis §4).
**Companions:**
- `IRANIMART_POS_V2_EXCHANGE_SRS.md` v2.0 (requirements FR-EX-nnn, AC-nn)
- `IRANIMART_POS_V2_EXCHANGE_GAP_ANALYSIS.md` v2.0
- `IRANIMART_POS_V2_EXCHANGE_IMPLEMENTATION_PLAN.md` v2.0

### Changes from v1.0 (superseded statements)
| v1.0 statement | v2.0 | Reason |
|---|---|---|
| UUID keys | `bigint` identity keys, like the rest of the schema | Repository convention |
| `PendingSettlement`, provider `Pending/Unknown`, HTTP 202 | Removed. Settlement is synchronous; card and mobile refunds are manual evidence. | D-09 |
| `proof_type`, proof verification columns | Removed. The invoice is scanned or entered and then validated. | D-13 |
| Partial refund marks whole sale `refunded` (risk) | Outdated: refunds are line-level (`returned_quantity`, `partially_refunded`) | Verified code |
| `OriginalSaleId` can link foreign originals (risk) | Outdated: link is business-checked and row-locked. **New links are now rejected** (D-01). | Verified code, D-01 |
| High-value permission "not frozen" | `pos.exchange.override_window` (C-10). No sixth permission. | C-01, C-10 |
| Approval tokens / nonce, unspecified approver | Supervisor credential approval (§11) | D-04, C-02 |
| Journal posting may be deferred (outbox) | Journal created and posted **inside** the Exchange transaction through the existing journal engine | C-04, C-06, D-15 |
| Loyalty port called at commit | No loyalty call. Facts and `loyalty_status` only. | D-05 |
| Quote token signed and short-lived | Preview hash, recomputed and compared at Confirm | No token infrastructure exists; the server recomputes anyway |
| v2.0: window counted between the sale business date and the Exchange business date | Local calendar days in the business time zone; purchase = Day 0; eligibility uses the local confirmation date; the replacement window anchors on the issuing Exchange's local date (§7.1) | OC-07 |
| v2.0: supervisor = same business + permission; refund only on the cashier | + receiving-outlet access (OC-01); approval type `refund` (OC-03 Case B); chain is always a supervisor approval (OC-02); multi-type approval with separate records | OC-01, OC-02, OC-03 |
| v2.0: high-value basis open | `max(return_gross, replacement_gross)` (§10.1) | OC-05 |
| v2.0: phone result `verified` | `not_required` / `skipped_walkin` / `matched_customer_record` (§10.1.1) | OC-06 |

---

## 1. Existing architecture integration

| Concern | Existing component (verified) | Use in Exchange |
|---|---|---|
| Layering | `src/RetailPOS.{Domain,Application,Infrastructure,API}`; MediatR 12 CQRS; FluentValidation via `Behaviors/ValidationBehavior.cs` | New slice `Exchanges` in each layer |
| Business scope | `IBusinessScope.RequireBusinessId()` (`Application/Abstractions/IBusinessScope.cs`) | Every query and command |
| Outlet access | `IUserOutletAccessService` (`API/Services`): `EnforceWriteOutletAsync`, `ResolveAndAuthorizeLocationAsync`; sales read scope `ISaleReadScopeProvider` | Receiving-outlet write (FR-EX-053); history read scope (D-11) |
| Errors | `GlobalExceptionHandlerMiddleware` → `ApiResponse` envelope | Exchange error codes (§24) |
| Return valuation | `SaleFinancialIntentFactory.ShareOfReturn` (`Application/Sales/FinancialPostings/SaleFinancialSnapshots.cs:140-146`): cumulative proportional share, residual on the last portion | Return credit and cost (C-03, EX-09) |
| Returned quantity | `SaleItem.ReturnedQuantity` + `ck_sale_items_returned_quantity` (`Infrastructure/Data/UomModelConfiguration.cs:234`); sale row lock `SaleVoidStore.LockSaleAsync` (`SELECT … FOR UPDATE`) | Shared entitlement (C-03) |
| Pricing | `IPosCartPriceCalculator.CalculateAsync(readFromCache:false)`; `SaleCheckoutCalculator`; `SaleDiscountAuthorizer`; `SaleUomLines.Resolve` | Replacement pricing, VAT, discounts (EX-10, EX-27) |
| Payments | `SalePaymentPlanner` (cash/card/mobile, exact split, tender and change) | Collection rules (EX-07) |
| Cash session | `ISaleCashSessionValidator` (`ValidateActiveForSaleAsync` FOR SHARE + `ValidateFinalGateAsync`); `CashSessionMovementRecorder` (session FOR SHARE + advisory lock on source key); `CashSessionExpectedCashCalculator` | EX-15, D-02 |
| Stock | `Inventory` (xmin); `StockLedgerWriter`; `StockLedgerTransactionType.Exchange = "exchange"` (reserved, unused); `IPackStateLedger`; `SaleUomReversal` (`Sales/Checkout/SaleUomLines.cs`) | EX-05, EX-06, D-08 |
| Cost | `SaleItem.HistoricalUnitCost` (set once); sale-time resolver `SaleCreationStore.ResolveHistoricalUnitCostSnapshotAsync` (business-wide GRN weighted average) | D-08, C-09 |
| Journal engine | `CreateOrGetSystemJournalDraftCommand`, `PostJournalEntryCommand`; open-period check `ManualJournalDraftStore.ValidateOpenPostingPeriodAsync`; joins the ambient transaction (`ManualJournalDraftStore.cs:173`); account slots via `PaymentPostingPolicy.ResolveSlot` + `ResolvePostingAccountAsync` (`API/Services/SaleFinancialJournalPoster.cs:228-249`) | C-04, C-06, D-15 |
| Numbering | `SaleInvoiceNumberAllocator` (atomic `INSERT … ON CONFLICT … RETURNING` on `sale_invoice_counters`); `AllocatedInvoiceNumber.Render`; outlet `InvoicePrefix` | D-19 |
| Credentials | `IPasswordHasher` (`Application/Authentication/Abstractions`) → `BCryptPasswordHasher`; `LoginCommandHandler.BuildEffectivePermissions` (role + overrides); rate limiter policies in `Program.cs` (`auth-login`) | C-02 |
| Supervisor outlet access | `IUserOutletAccess.GetAuthorizedOutletsAsync(UserOutletAccessContext)` (`Application/Identity/LocationAccess/UserOutletAccess.cs`). It is evaluated from an explicit `UserId` using `IUserOutletAccessStore.ListActiveOutletAssignmentsAsync` (`UserOutletAssignment.IsActive`), and returns `IsGlobalAccess` for all-outlet entitlements. | OC-01 |
| Time zone | `ICompanyTimeZoneProvider.GetTimeZoneIdAsync(businessId)`; `CashSessionTime.FindZone` / `BusinessDateFor(utcInstant, zoneId)` | OC-07 |
| Outbox | `IIntegrationOutbox.AppendSourceChangeAsync(IntegrationEnvelope)` (`Application/Integration/DurableDeliveryContracts.cs`; needs only an active transaction; the same as `CashSessionEventWriter.cs:43`). **Not** `AppendAsync`, which requires an atomic audit scope (`IntegrationOutbox.cs:12-17`). `DurableDelivery:Enabled=false` everywhere. | C-04 events (Stage 0 §4) |
| Receipt | `ThermalReceiptComponent` (`ReceiptDocumentDto`); `receipt-print-page.ts`; `SaleReceiptPdfRenderer` (QuestPDF); `ReceiptPrintHistory` (`SaleId` NOT NULL, so it cannot be reused) | EX-12 |
| Permissions | `PermissionCatalog.All` → policies auto-registered (`Program.cs:99-113`); `DbSeeder` role JSON; frontend `role-form.component.ts` | EX-16, D-17 |

**Rule:** `ConfirmExchangeCommand` does **not** call `CreateSaleCommand` or `RefundSaleCommand`. It composes the components above in one transaction (C-04). The CreateSale blocks that are inlined today (stock and packaging decrement, price-change check, salesperson eligibility, cost resolver) are extracted into shared helpers **without behaviour change**, under regression tests (Plan Stage 2).

## 2. Domain model and relationships (Proposed)

```text
ExchangePolicyVersion (business, version_no) ──< Exchange.policy_version_id
Exchange (header, business, receiving outlet, session, number)
 ├─< ExchangeSourceInvoice ──> Sale (original, immutable)
 ├─< ExchangeReturnLine ──> SaleItem | ExchangeReplacementLine (source)
 ├─< ExchangeReplacementLine ─< ExchangeReplacementLineComponent
 ├─< ExchangeLineageLink (return line × replacement line)
 ├─< ExchangeSettlement ──> CashSessionMovement (cash only)
 ├─< ExchangeApproval (window | chain | high_value; supervisor)
 ├─< ExchangeSalespersonAdjustment (completion; reversal)
 ├── ExchangeReceiptSnapshot ─< ExchangeReceiptPrintHistory
 └── ExchangeReversal (0..1) ─< settlements, adjustments, ledger refs
```

The aggregate root is `Exchange`. Original `Sale` and `SaleItem` are referenced, never modified, except `SaleItem.ReturnedQuantity` (the shared entitlement counter; C-03, D-14).

## 3. Database tables, columns and constraints (Proposed; snake_case; `bigint` identity PKs; money `numeric(18,2)`, costs `numeric(18,4)` or the existing cost precision; quantities `numeric(18,6)` per `UomQuantityMath.StorageScale = 6`)

### 3.1 `exchange_policy_versions`
`id`, `business_id` FK, `version_no` int, `is_enabled` bool, `allow_lower_price_exchange` bool NOT NULL DEFAULT false, `exchange_window_days` int, `allow_replacement_reexchange` bool, `replacement_window_days` int NULL, `max_exchange_count_per_lineage` int NULL, `phone_verification_mode` varchar(20) CHECK in (`optional`, `required`), `high_value_approval_threshold` numeric(18,2) NULL (NULL = disabled; CHECK > 0 when set, OC-05), `negative_target_adjustment_mode` varchar(40) NOT NULL DEFAULT `track_only` CHECK in (`track_only`, `deduct_from_exchange_salesperson`), `created_by_user_id`, `created_at_utc`.
- UNIQUE (`business_id`, `version_no`).
- Append-only (no UPDATE/DELETE; DB trigger, the same pattern as the `sale_financial_postings` guard).
- No row means Exchange is not configured (D-16).
- CHECKs: positive day and count values when the related feature is enabled.

### 3.2 `exchange_number_counters`
(`business_id`, `outlet_id`, `business_date` date) PK, `last_sequence` bigint. Same `INSERT … ON CONFLICT DO UPDATE … RETURNING` pattern as `sale_invoice_counters` (D-19).

### 3.3 `exchanges`
| Column | Notes |
|---|---|
| `id`, `business_id`, `exchange_number` varchar(60) | UNIQUE (`business_id`, `exchange_number`) |
| `receiving_outlet_id`, `terminal_id`, `cash_session_id` | NOT NULL FKs; session belongs to the outlet and terminal |
| `business_date` date | = `cash_sessions.business_date` |
| `completed_at_utc` | Actual completion instant (Loyalty D38) |
| `cashier_user_id`, `salesperson_user_id` | Cashier from claims; salesperson validated |
| `customer_id` | Single actual customer (walk-in allowed only when there is one source invoice) |
| `status` | CHECK in (`completed`, `reversed`) |
| `reason_code` | CHECK in (`size_fitting`, `colour_design`, `wrong_product_purchased`, `customer_preference_changed`, `other`); `reason_other_text` required when `other` (CHECK); `internal_note` (never printed) |
| `phone_verification_result` | CHECK in (`not_required`, `skipped_walkin`, `matched_customer_record`); no phone stored (OC-06) |
| `eligibility_local_date` date, `time_zone_id` | Local calendar date of the confirmation instant in the business zone, and the zone used (OC-07). It anchors the replacement window of this Exchange's replacement lines. |
| `high_value_amount`, `high_value_threshold_applied` | `max(return_gross, replacement_gross)` and the threshold from the policy snapshot (OC-05); NULL threshold = disabled |
| Totals | `return_net`, `return_vat`, `return_gross`, `return_cost`, `replacement_net`, `replacement_discount`, `replacement_vat`, `replacement_gross`, `replacement_cost`, `net_adjustment_net`, `net_adjustment_vat`, `net_adjustment_gross` (signed) |
| `settlement_direction`, `settlement_amount` | CHECK (`collect`, `refund`, `none`); CHECK amount = abs(`net_adjustment_gross`), and `none` ⇔ 0 |
| `policy_version_id`, `policy_snapshot` jsonb | Immutable |
| `pricing_snapshot` jsonb, `preview_hash` | Calculation evidence |
| `journal_entry_id` | NOT NULL FK `journal_entries` (synchronous posting, C-04) |
| `loyalty_status` | NOT NULL CHECK in (`module_inactive`, `applied`, `pending_reconciliation`, `not_eligible`); only `module_inactive` is written in this delivery (D-05) |
| `idempotency_key` varchar(120), `request_fingerprint` char(64) | UNIQUE (`business_id`, `idempotency_key`) |
| `reversal_id` | NULL FK `exchange_reversals` |
| `created_at_utc` | |

Indexes: (`business_id`, `business_date`, `receiving_outlet_id`); (`business_id`, `customer_id`, `completed_at_utc`); (`cash_session_id`); (`business_id`, `salesperson_user_id`, `business_date`).

Immutability trigger: only `status` (`completed` → `reversed`) and `reversal_id` may change.

### 3.4 `exchange_source_invoices`
`id`, `exchange_id`, `sale_id` FK, `original_outlet_id`, `customer_id`, `invoice_number_snapshot`, `sale_business_date_snapshot` date. UNIQUE (`exchange_id`, `sale_id`). Index (`sale_id`) for the Sale Details links; index (`invoice_number_snapshot`) for history search.

### 3.5 `exchange_return_lines`
`id`, `exchange_id`, `source_kind` CHECK (`sale_item`, `exchange_replacement_line`), `sale_id` NULL, `sale_item_id` NULL FK, `source_replacement_line_id` NULL FK. CHECK: exactly one source matches `source_kind`.
- Lineage: `root_sale_item_id` NOT NULL, `lineage_depth` int (depth of the *source*: 0 for a sale line).
- Product and UOM: `variant_id`, UOM snapshot columns (same shape as `UomSnapshotColumns`), `returned_quantity` numeric(18,6) > 0 (base units).
- Value: `credit_net`, `credit_vat`, `credit_gross`, `cost_restored` (D-08).
- Packaging and condition: `packaging_reusable` bool, `condition` CHECK = `resellable` (EX-05: only sellable).
- `stock_ledger_entry_id`.

### 3.6 `exchange_replacement_lines`
`id`, `exchange_id`, `variant_id`, UOM snapshot columns, `issued_quantity` numeric(18,6) > 0, `returned_quantity` numeric(18,6) DEFAULT 0, **CHECK (`returned_quantity` >= 0 AND `returned_quantity` <= `issued_quantity`)** (C-03, separate chain entitlement).
- Pricing and VAT: `unit_price`, `discount_amount`, `taxable_amount`, `vat_rate`, `vat_amount`, `tax_code`, `tax_classification`, `tax_inclusive`.
- Cost: `historical_unit_cost` NOT NULL, `packaging_cost`.
- Lineage: `lineage_depth` int (= max source `lineage_depth` + 1).
- `stock_ledger_entry_id`.

### 3.7 `exchange_replacement_line_components`
Packaging consumed by a replacement line (mirrors `sale_item_components`).

### 3.8 `exchange_lineage_links`
(`return_line_id`, `replacement_line_id`) PK. One row per pair within the same Exchange (many-to-many). Depth and root-set evaluation is conservative: a replacement inherits the **maximum** depth and the **union** of roots of all return lines in its Exchange. No value split is stored (Proposed; satisfies "no ambiguous single parent").

### 3.9 `exchange_settlements`
`id`, `exchange_id`, `exchange_reversal_id` NULL, `direction` CHECK (`collect`, `refund`), `method` CHECK (`cash`, `card`, `mobile`), `amount` > 0, `tendered` NULL, `change_given` NULL (cash collect only), `payment_account_id` (resolved slot account), `cash_session_id`, `cash_session_movement_id` NULL (cash only).
- Refund evidence (D-09): `external_reference` varchar(120) **required for card/mobile refunds**, `external_confirmed_by_user_id`, `external_confirmed_at_utc`.
- `created_at_utc`.

### 3.10 `exchange_approvals`
`id`, `business_id`, `approval_type` CHECK (`window`, `chain`, `high_value`, `refund`), `receiving_outlet_id`, `terminal_id`, `cashier_user_id`, `supervisor_user_id` (CHECK `supervisor_user_id <> cashier_user_id`), `permission_used`, `supervisor_outlet_access_basis` CHECK (`assignment`, `global_entitlement`) (OC-01 audit), `reason` NOT NULL, `payload_hash` char(64), `token_hash` char(64) UNIQUE (the random token is never stored in clear), `authentication_group_id` (one supervisor authentication that approved several types, OC-02), `status` CHECK (`granted`, `consumed`, `expired`, `revoked`), `expires_at_utc`, `consumed_by_exchange_id` NULL, `created_at_utc`, `consumed_at_utc`.

`payload_hash`:
- `window`, `chain`, `high_value`: the `previewHash` (§10.1). It covers the source lines, quantities, replacements, totals, `high_value_amount` and `eligibility_local_date`.
- `refund`: SHA-256(`previewHash` + canonical refund settlement {direction, amount, method(s)}) (OC-03).

### 3.11 `exchange_salesperson_adjustments`
`id`, `business_id`, `exchange_id`, `exchange_reversal_id` NULL, `business_date`, `receiving_outlet_id`, `cashier_user_id`, `salesperson_user_id`, `positive_amount_net`, `negative_amount_net`, `signed_amount_net`, `target_effect_amount_net` (the amount that counts toward salesperson target under the mode), `negative_mode`, `policy_version_id`, `created_at_utc`. UNIQUE (`exchange_id`, `exchange_reversal_id`) (NULLS NOT DISTINCT or a partial index). Immutable.

### 3.12 `exchange_receipt_snapshots`, `exchange_receipt_print_history`
- `exchange_receipt_snapshots`: `exchange_id`/`exchange_reversal_id`, `document_version`, `document` jsonb, `created_at_utc`. Immutable.
- `exchange_receipt_print_history`: `exchange_id`, `terminal_id`, `printed_by_user_id`, `action_type` (`print`, `reprint`, `pdf`, `print_attempt_failed`), `printed_at_utc`.

A separate table is needed because `receipt_print_history.sale_id` is NOT NULL with a cascading FK (`RetailPOSDbContext.cs:1458-1473`).

Rendering (VI-08, verified):
- `SaleReceiptPdfRenderer` is sale-specific: it takes `SaleReceiptDocumentReadModel` and hard-codes "Invoice" in private static helpers (:33, :69, :253). A new `ExchangeReceiptPdfRenderer` (QuestPDF, same layout conventions) is needed; the sale renderer is untouched.
- The frontend `thermal-receipt.component` hard-codes "Invoice". The Exchange receipt is a new component reusing the CSS and the document-agnostic `receipt-print-page.ts`.
- Exchange time fields must be `UtcTime.Normalize`d before zone conversion (the sale receipt's raw `SpecifyKind(Utc)` at `GenerateSaleReceiptDocumentCommand.cs:218-223` must not be copied).

### 3.13 `exchange_reversals`
`id`, `exchange_id` UNIQUE, `reversal_number` (`{exchange_number}-RV`, Proposed), `business_date`, `receiving_outlet_id`, `terminal_id`, `cash_session_id`, `actor_user_id`, `reason` NOT NULL, the same total columns as the header (negated), `journal_entry_id` NOT NULL, `idempotency_key`, `request_fingerprint` (UNIQUE (`business_id`, `idempotency_key`)), `completed_at_utc`.

### 3.14 Existing tables touched
| Table | Change | Verification |
|---|---|---|
| `sale_items` | **No schema change.** `returned_quantity` incremented by exchange returns; decremented by reversal. | — |
| `cash_session_movements` | New type values `exchange_cash_in` (+), `exchange_cash_out` (−). **Migration required:** extend `ck_cash_session_movements_type` and `ck_cash_session_movements_effect` (`20261103091638…:257-258`; `CashSessionModelConfiguration.cs:27-32`) and `CashSessionMovementTypes.All`. Add explicit cases to `CashSessionExpectedCashCalculator` (it has no default branch and ignores unknown types, :61-73). The close-snapshot hash is computed once and never re-verified, so it is not affected. | VI-07 verified (Stage 0 §11) |
| `stock_ledgers` | Rows with `transaction_type = 'exchange'`, new `reference_type = 'exchange'` / `'exchange_reversal'` (varchar(30)) | VI-15 verified: no CHECK constraints, so no migration |
| `journal_entries` | New source keys `exchange:{id}:v1`, `exchange-reversal:{id}:v1`; reference type `Exchange` | VI-01 |
| `sales` | None (legacy link columns kept; D-01, D-12) | — |

## 4. Immutable original and Exchange references
- An Exchange **never** updates `sales`, `sale_payments`, original journals or original receipts. Only `sale_items.returned_quantity` changes (C-03).
- Snapshots taken at Confirm: invoice number, original business date, UOM, prices, VAT, credits, costs, policy and receipt document.
- Legacy `sales.original_sale_id` / `replacement_sale_id` are read-only history (D-01, D-12).

## 5. Business policy versioning and snapshots
- `GET /api/pos/exchanges/policy` (`settings.view`): returns the current version and an `isConfigured` flag.
- `PUT /api/pos/exchanges/policy` (`settings.edit`): FluentValidation; inserts version `n+1`. Optimistic check on `expectedVersionNo` → 409 if stale. Audited.
- At Preview and Confirm the handler loads the latest version for the business. Confirm stores `policy_version_id` + `policy_snapshot`. Reversal uses the **original** Exchange snapshot for the salesperson mode, and the current version for eligibility of the reversal itself (the session and period rules are not policy).

## 6. Source-line quantity tracking (C-03)
- **Sale lines:** remaining = `quantity − returned_quantity`, read **after** `SELECT … FROM sales WHERE id = ANY(@ids) ORDER BY id FOR UPDATE` (the same lock `RefundSaleCommand` takes through `LockSaleAsync`). The increment is in the same transaction. The DB CHECK is the final guard.
- **Replacement lines:** remaining = `issued_quantity − returned_quantity` under `SELECT … FROM exchange_replacement_lines … ORDER BY id FOR UPDATE`.
- Quantities are base units, validated by `UomQuantityMath` (scale 6, never rounded; legacy lines whole only).
- Requested quantities are grouped per source line before checking (several request lines on one source line). Reuse the `RefundSaleCommand.ResolveAssessedLines` rules (:327-371): `SaleItemId` is required when a variant repeats; quantity > 0; then precision.
- Remaining returnable for display: the same rule as `SaleReadStore.cs:64` (0 when refunded or voided).
- **Void guard (D-14, verified gap):** `VoidSaleCommand.EnsureVoidable` (:158-171) checks status only, and the void reads the sale **without** a lock (:35).
  - The new `SALE_HAS_RETURNS` check must run against `sale_items.returned_quantity` **after** the conditional `completed → voided` UPDATE (`SaleVoidStore.cs:38-53`) inside the void transaction, or with the sale row locked. This closes the gap between checking and voiding against a concurrent Exchange.
  - With current refund code, `returned_quantity > 0` already implies a refunded status, so existing outcomes don't change (confirm with read-only audit Q5).

## 7. Replacement-chain model
- Replacement `lineage_depth` = max(source depth) + 1. Sale lines have depth 0.
- Re-exchange allowed if `AllowReplacementReExchange` and:
  - `eligibilityLocalDate ≤ source_exchange.eligibility_local_date + ReplacementWindowDays` (§7.1; OC-07); and
  - `source.lineage_depth + 1 ≤ MaxExchangeCountPerLineage`.

  Otherwise a `chain` supervisor approval is needed (§11; OC-02).
- The original window (EX-03) applies only to `sale_item` sources.

### 7.1 Time-zone-aware eligibility dates (OC-07; Proposed)
```text
zoneId               = ICompanyTimeZoneProvider.GetTimeZoneIdAsync(businessId)   // authoritative business zone
eligibilityLocalDate = CashSessionTime.BusinessDateFor(serverUtcNowAtConfirm, zoneId)   // NOT cash_sessions.business_date
originalPurchaseLocalDate = CashSessionTime.BusinessDateFor(<sale purchase instant>, zoneId)   // see VI-17
lastEligibleOriginal = originalPurchaseLocalDate + ExchangeWindowDays   (calendar days; Day 0 = purchase date)
windowValid          = eligibilityLocalDate <= lastEligibleOriginal    // the whole last day is valid
lastEligibleReplacement = source_exchange.eligibility_local_date + ReplacementWindowDays
```
- Date arithmetic uses `DateOnly`/date-only values after zone conversion. No UTC-calendar-date shortcuts; DST is handled by `TimeZoneInfo` conversion.
- **Purchase date (VI-17, Stage 0 §10):** `originalPurchaseLocalDate = company calendar date of UtcTime.Normalize(Sale.SaleDate)`. Why:
  - `SaleDate` is either `now` (regular) or the company-zone midnight of the chosen day for `sales.backdate` cashiers. The POS always sends the picker date, today by default (`pos-screen.component.ts:604,648-660`; `SalesController.cs:402-433`; `CompanyDates.cs:37-40`).
  - **Do not use `SaleDateTime`** (entry instant) or `BusinessDate` (session date).
  - Normalise first because `Npgsql.EnableLegacyTimestampBehavior` can return `Kind=Local` (`Program.cs:32`).
  - There is no separate "Late Sale" entity in code.
  - Data confirmation: read-only audit Q8.
- Preview computes `eligibilityLocalDate` and includes it in `previewHash`. Confirm recomputes it from its own server time. If midnight has passed and the result changes (an approval is now needed, or no longer needed), the hash differs and Confirm returns `409 EXCHANGE_PREVIEW_STALE`.
- `exchanges.eligibility_local_date` and `time_zone_id` are stored. Accounting, numbering and reporting keep `exchanges.business_date` (= cash session business date).

## 8. Exchange number generation (D-19)
`IExchangeNumberAllocator.AllocateAsync(businessId, outletId, businessDate)` (Proposed):
- inside the Confirm transaction, **after** all validation (so a rollback releases the number, the same as the sale allocator);
- the prefix is the receiving outlet's `InvoicePrefix` (missing → `EXCHANGE_OUTLET_PREFIX_MISSING`);
- render `{prefix}X{yyMMdd}{seq}`.

## 9. API endpoints and contracts (Proposed; base `api/pos/exchanges`; `ApiResponse` envelope)
| Method + route | Policy | Request → result |
|---|---|---|
| `GET /policy` | `settings.view` | → `ExchangePolicyDto` (+ `isConfigured`) |
| `PUT /policy` | `settings.edit` | `ExchangePolicyUpdateDto` (+ `expectedVersionNo`) → new version |
| `GET /source-invoice?invoiceNumber=&receivingOutletId=` | `pos.exchange.create` | Exact match, business-scoped (D-10) → `ExchangeSourceInvoiceDto`: invoice number, date, original outlet name, customer (masked phone, walk-in flag), lines with variant/SKU/size/colour, unit, sold/returned/remaining quantity, credit per unit (indicative), eligibility and reason codes, window status |
| `POST /preview` | `pos.exchange.create` | `ExchangePreviewRequest` → `ExchangePreviewDto` (lines, credits, replacement pricing, VAT, difference, settlement direction, required approvals [`window`/`chain`/`high_value`], warnings, `previewHash`). No writes. |
| `POST /approvals` | `pos.exchange.create` (cashier) + rate limit `exchange-approval` | `ExchangeApprovalRequest` (§11) → `[{ approvalId, approvalToken, type, expiresAtUtc }]` (one per approved type) |
| `POST /` | `pos.exchange.create` (+ `.refund` checked when direction = refund) | `ConfirmExchangeRequest` → `ExchangeDto` (201; idempotent replay 200) |
| `GET /` | `sales.view` | Filters per FR-EX-150; `page`, `pageSize` ≤ 100 → paged `ExchangeListItemDto` |
| `GET /{id}` | `sales.view` | `ExchangeDetailDto` |
| `GET /{id}/receipt-document` | `sales.view` | Snapshot `ExchangeReceiptDocumentDto` |
| `GET /{id}/receipt-pdf` | `sales.view` | PDF (80mm or A4 mode) |
| `POST /{id}/reprint`, `POST /{id}/receipt-print-attempt` | `sales.view` | Print history |
| `GET /{id}/reversal-eligibility` | `pos.exchange.reverse` | Blocking reasons, compensation amounts |
| `POST /{id}/reverse` | `pos.exchange.reverse` | `ReverseExchangeRequest` → `ExchangeReversalDto` |
| `GET /by-sale/{saleId}` | `sales.view` | Exchanges that used a sale (Sale Details) |
| `GET /metrics?from=&to=&outletId=` | `sales.view` (owner dashboards keep `dashboard.sales_finance.view`) | `ExchangeMetricsDto` (§23) |

### 9.1 Request shapes (Proposed)
```text
ExchangePreviewRequest {
  receivingOutletId, terminalId,
  sourceSaleIds[1..n],
  returns[{ sourceKind: "sale_item"|"exchange_replacement_line",
            sourceLineId, quantity (base units), conditionSellable: true,
            packagingReusable? }],
  replacements[{ variantId, sellingOptionId?, enteredQuantity, fulfilmentState?,
                 includePackaging?, lineDiscountAmount?, expectedLineGross? }],
  invoiceDiscount?{ amount|percent },
  salespersonUserId, reasonCode, reasonOtherText?, internalNote?,
  verificationPhone?,
  settlementDirectionHint? }

ConfirmExchangeRequest = ExchangePreviewRequest + {
  previewHash, idempotencyKey,
  approvalTokens[],
  settlements[{ direction, method, amount, tendered?, externalReference?,
                externalRefundConfirmed? }] }

ReverseExchangeRequest { reason, terminalId, idempotencyKey,
  settlements[...same shape...], approvalTokens? (none required by policy) }
```
The server never accepts a cashier ID, business ID, prices, VAT, totals, dates or an exchange number from the client.

## 10. Preview and Confirm command flows
### 10.1 `PreviewExchangeQuery` (no writes) — validation order (Proposed)
1. Require the business; the policy must exist and be enabled (D-16).
2. Write-authorise the receiving outlet; check an active session exists for outlet and terminal (not overdue).
3. Load source sales by ID, business-filtered. Validate status and evidence (C-09). Validate same customer / walk-in (D-03).
4. Phone verification per §10.1.1 (OC-06).
5. Validate return lines: source belongs to a source sale; barcode/variant match; remaining quantity.
6. Window and chain against `eligibilityLocalDate` (§7.1, OC-07) → required `window` / `chain` approvals.
7. Return credit = `ShareOfReturn(line.TaxableAmount, already, qty, sold)` + the same for `VatAmount`; cost = `ShareOfReturn(ToCurrency(HistoricalUnitCost × sold), …)`.
8. Price replacements with the existing pipeline: discount XOR and permissions, VAT groups; stock availability check (no lock).
9. **High value (OC-05):** `high_value_amount = max(Σ return_gross, Σ replacement_gross)`. If the threshold is set and `high_value_amount ≥ threshold`, a `high_value` approval is required. The settlement difference and payment split are never used.
10. Difference → direction. Lower-price rule (EX-02). **Refund path (OC-03):** the cashier holds `pos.exchange.refund` (Case A), otherwise a `refund` approval is required (Case B). Drawer-cash indication (non-binding).
11. `previewHash` = SHA-256 of the canonical JSON of {inputs, policy version, `eligibilityLocalDate`, computed lines and totals, `high_value_amount`, required approval types}.

Each requirement is evaluated independently. No approval type substitutes for another.

### 10.1.1 Phone verification classification (OC-06; Proposed)
```text
if policy.PhoneVerificationMode != required              -> result = not_required
else for each source invoice:
   customer = Sale.CustomerId's Customer (same business)
   if customer.IsSystem && customer.CustomerCode == "WALKIN"   (VI-06)
                                                          -> result = skipped_walkin   (never use the WALKIN phone)
   else if customer.PhoneNormalized is null              -> blocked: EXCHANGE_PHONE_VERIFICATION_UNAVAILABLE (VI-16)
   else compare normalize(enteredPhone) with customer.PhoneNormalized
        match    -> result = matched_customer_record
        mismatch -> 422 EXCHANGE_PHONE_VERIFICATION_FAILED
```
- Only the **original sale's customer** is used. Loyalty accounts and family links are never used (none exist in code today; TF-21).
- `Customer` has no verification flag (TF-27), so the result is labelled `matched_customer_record` and never "verified identity" (FR-EX-038).
- **A registered customer with no recorded phone has no defined approved outcome.** VI-16 records it as a capability gap. The pseudo-code blocks it as the safe placeholder, pending that outcome. No new business rule is introduced.
- **Stage 0 finding (VI-16, DN-01):**
  - No OTP, SMS or phone-verification capability exists anywhere.
  - Customer phones are mutable (`UpdateCustomerCommand.cs:28-58`), and sales store **no phone snapshot** (receipts read the live phone, `SaleReadStore.cs:130`).
  - The walk-in record has no phone and is trigger-protected (`20261013041308…:36-68`).

  The `matched_customer_record` branch therefore compares against the *current* record only. Whether it may serve as the `required` mechanism for registered customers is **owner decision DN-01** (Stage 0 report §19). Until it is decided, the `required` + registered-customer path must not be enabled. `optional` mode and the walk-in skip are unaffected.

### 10.2 `ConfirmExchangeCommand` (single transaction; see §18 for lock order)
```text
BEGIN   (IExchangeStore.BeginTransactionAsync: no-op if ambient, CashSessionStore style;
         never RequiredAuditTransaction — Stage 0 §4.3)
 0  Idempotency: find (business, key) → same fingerprint → return stored result;
    different → 409 EXCHANGE_IDEMPOTENCY_MISMATCH
 1  product_variants FOR SHARE, sorted (all return, replacement and packaging variants;
    checkout precedent CreateSaleCommand.cs:138)
 2  Lock source sales FOR UPDATE (ascending id)
 3  Lock source replacement lines FOR UPDATE (ascending id)
 3a Session: ISaleCashSessionValidator.ValidateActiveForSaleAsync (FOR SHARE, 20h)
 4  Lock ALL receiving-outlet inventories in ONE statement ORDER BY id FOR UPDATE
    (UomStores.cs:236 pattern); insert missing rows afterwards (23505 → 409);
    pack-state balances via IPackStateLedger
 5  Recompute the full preview from the locked state → compare previewHash → 409 EXCHANGE_PREVIEW_STALE
 6  Approvals: lock exchange_approvals FOR UPDATE by token hash; status granted, not
    expired, payload_hash == recomputed (refund: incl. settlement hash), type set ==
    required set; supervisor still active, same business, still holds the permission,
    and **still has receiving-outlet access** (OC-01) → mark consumed
 7  Permissions: create; refund → cashier holds pos.exchange.refund (OC-03 Case A)
    OR a consumed `refund` approval (Case B). Claims re-read for this request.
 8  Settlement validation: methods, exact sum, D-09 evidence, account mapping resolvable (C-06)
 9  Cash-out only: drawer lock (§15) → expected cash ≥ cash refund (D-02)
10  Allocate the exchange number (§8)
11  Insert header, source invoices, return lines, replacement lines, components, lineage links
12  sale_items.returned_quantity += ; replacement_lines.returned_quantity +=
13  Inventory: +return qty, −replacement qty; ledger rows; pack-state reconcile
14  Settlements; cash movements (exchange_cash_in / exchange_cash_out) via the recorder
14a accounting_periods (+ financial year) row FOR SHARE for the business date; pre-validate
    Open status and slot mappings via the existing read paths → typed errors (VI-18)
15  Journal: CreateOrGetSystemJournalDraftCommand + PostJournalEntryCommand (period guard)
16  Salesperson adjustment fact
17  Receipt snapshot; audit records; outbox via AppendSourceChangeAsync (pos.exchange.completed v1)
18  ValidateFinalGateAsync (session not overdue)
COMMIT  → invalidate the stock cache for the touched variants (after commit)
ON ANY EXCEPTION → Rollback + ChangeTracker.Clear() (D10 precedent SaleFinancialPostingStore.cs:119)
```
**Early flushes:** the journal store (`ManualJournalDraftStore.cs:105,265`), the account provisioner (`AccountingConfigurationProvisioner.cs:137`), `AuditService.RecordAsync` (:112) and `CashSessionMovementRecorder` (:76,101) call `SaveChanges` themselves. These flushes stay inside the transaction, and the code must not depend on deferred persistence. The only independent commit is the journal store's **failure audit** (`AuditService.RecordFailureAsync`, new scope). It is non-authoritative and accepted.
Any exception rolls back everything (C-04). A unique-violation race on the idempotency key leads to a replay lookup (the `CreateSaleCommand` pattern).

## 11. Supervisor one-time approval technical contract (D-04, C-02, C-10, OC-01, OC-02, OC-03, OC-05)
One supervisor-authentication framework only; no second mechanism.

**Request** `POST /api/pos/exchanges/approvals`:
```text
{ approvals: [ { type: "window"|"chain"|"high_value"|"refund", reason } , ... ],   // 1..4 distinct types
  supervisorLogin, supervisorPassword,
  preview: ExchangePreviewRequest, previewHash,
  refundSettlement?: { amount, methods[] } }      // required when "refund" is listed
```
**Server steps (in this order):**
1. Rate-limit with a new policy `exchange-approval`. The existing auth policies are fixed-window **per remote IP** (`Program.cs:1154-1190`; `auth-login` 5/min); the new policy partitions per IP + cashier. Attempts are audited without the credential; the supervisor ID goes in the audit metadata, because `AuditService` takes the real and acting user from the request (`AuditService.cs:120-125`).
   - **Verified reuse pattern for steps 3–6:** `StockCountAdjustmentApproverPolicy.cs:31-61`. It loads the profile, then checks `IsActive`, `!MustResetPassword` and `BusinessId == scope`, then `LoginCommandHandler.BuildEffectivePermissions`, then `new UserOutletAccessContext(id, IsSuperAdmin:false, IsBusinessOwner: role == "BusinessOwner", businessId, null)` → `IUserOutletAccess.GetAuthorizedOutletsAsync`.
   - Login emails are **globally unique** (`AuthenticationStore.cs:30-38`; unique index), so the lookup is by email, then the business is checked.
2. Recompute the preview from `preview` with the current server time (§7.1). It must equal `previewHash`. Every listed type must be in the **required set**; types that aren't required are rejected. For `refund`, `refundSettlement` must match the preview's refund amount.
3. **Authenticate:** resolve the supervisor by login **within `IBusinessScope.RequireBusinessId()`** (VI-04). The user must be **active**. Verify the password with `IPasswordHasher.Verify`. Failure returns a generic `403 EXCHANGE_APPROVAL_INVALID`, with no hint of which part was wrong.
4. **Distinct identity:** supervisor ≠ cashier (claims), otherwise `EXCHANGE_APPROVAL_INVALID`.
5. **Permission per listed type.** Effective permissions = role + overrides (the same composition as `LoginCommandHandler.BuildEffectivePermissions`, exposed through a port):
   - `window` → `pos.exchange.override_window`
   - `high_value` → `pos.exchange.override_window` (C-10)
   - `chain` → `pos.exchange.override_chain` (OC-02)
   - `refund` → `pos.exchange.refund` (OC-03 Case B)

   Missing for any listed type → `403 EXCHANGE_APPROVAL_NOT_PERMITTED`, and **no** approval is created for any type.
6. **Receiving-outlet access (OC-01):** build a `UserOutletAccessContext` for the supervisor's `UserId` (not the cashier's context, and not any UI-selected outlet). Call `IUserOutletAccess.GetAuthorizedOutletsAsync`, which is backed by active `UserOutletAssignment` rows. Access passes if `IsGlobalAccess` (Business Owner all-outlet entitlement) or the receiving outlet is in the list. Otherwise `403 EXCHANGE_APPROVAL_NOT_PERMITTED`. Record `supervisor_outlet_access_basis`.
7. Insert **one `exchange_approvals` row per type**, sharing an `authentication_group_id`, each with its own reason and token. Token: random 256-bit, stored as a SHA-256 hash. `expires_at_utc = now + ExchangeApprovals:LifetimeSeconds` (technical configuration set in Stage 1; not a business rule). `payload_hash` per §3.10.
8. Return the tokens once. The POS session and cookies/JWT are untouched (no login switch).

**Rules:**
- The request body is excluded from request logging. The password is never persisted, traced or put in the outbox.
- Confirm consumes the approvals (§10.2 step 6) and re-runs steps 4–6 against the current user, role and assignment state. A revoked permission or assignment, or a disabled supervisor, fails Confirm (AC-46d, AC-45).
- A change to any bound input (lines, quantities, prices, VAT, `eligibility_local_date`, `high_value_amount`, or the refund amount or method) changes the hash, which invalidates the affected approvals.
- Concurrent Confirms using the same token: the `FOR UPDATE` on the approval row plus `status = consumed` means exactly one Confirm succeeds.
- Audit per approval: type, reason, cashier, supervisor, permission used, outlet access basis, policy version, `high_value_amount` / threshold (`high_value`), refund amount and method (`refund`), the eligibility and purchase local dates (`window`), and lineage depth (`chain`).

## 12. Authentication and authorization enforcement
| Check | Where |
|---|---|
| Endpoint permission | `[Authorize(Policy = "pos.exchange.create")]` etc. (policies auto-registered from `PermissionCatalog.All`) |
| Business | `IBusinessScope.RequireBusinessId()`; every query filters `business_id`; foreign IDs → 404 |
| Receiving outlet write | `IUserOutletAccessService.EnforceWriteOutletAsync` (the same as sale creation) |
| Source invoice read | Exact-number lookup inside the business only (D-10). **No** `ISaleReadScopeProvider` widening. |
| History read | `sales.view` + read scope on `receiving_outlet_id` (D-11) |
| Refund | `pos.exchange.refund` on the authenticated cashier at Confirm (OC-03 Case A), **or** a consumed `refund` supervisor approval (Case B, §11) |
| Supervisor exceptions | Supervisor permission per type + active + same business + receiving-outlet access through `IUserOutletAccess` on the supervisor's own context (OC-01, OC-02, §11) |
| Reversal | `pos.exchange.reverse` + receiving-outlet write access |
| Disabled user | The existing auth pipeline; Confirm and approval re-read the user state (C-08) |

## 13. Inventory transaction and historical cost handling
- **Return-in** at the receiving outlet:
  - `Inventory.Quantity += returned base qty`;
  - ledger `transaction_type = exchange`, `reference_type = exchange`, `location_id = receiving_outlet_id`, `location_type = outlet`;
  - `movement_value = cost_restored` (D-08) via `StockLedgerWriter.WriteEntryWithValuation`.
- **Missing inventory row** at the receiving outlet (VI-10, partially verified):
  - Existing creation paths add a new `Inventory` row inline: `GrnCompletionStore.cs:93-103`, `StockTransferReceiveStore.cs:89-99`, `StockAdjustmentApprovalStore.cs:181-190`. None of them catch the unique violation on `IX_inventories_variant_id_location_id_location_type`.
  - The Exchange follows the same pattern after the sorted lock (§18 step 6) and maps `23505` to `409 EXCHANGE_CONCURRENCY_CONFLICT`. Verify with T-05.
  - **Never silently skip**, unlike the current refund (`RefundSaleCommand.cs:145-150`), which skips the restock and ledger while the journal still debits Inventory. That is a pre-existing defect, out of scope.
- **Pack state and packaging:** reuse `SaleUomReversal` rules through a location-parameterised overload. A whole sealed or prepared pack with reusable packaging returns in that form; otherwise loose. Packaging components come back only in whole reusable units.
- **Replacement-out:** reuse the checkout stock and packaging decrement (extracted helper); `Quantity ≥ needed`, otherwise `EXCHANGE_STOCK_INSUFFICIENT`. Cost = the authoritative resolver (extracted from `SaleCreationStore.ResolveHistoricalUnitCostSnapshotAsync`), captured on the replacement line.
- **Historical cost (C-09):** `HistoricalUnitCost` if not null. Otherwise accept only **provably matching** immutable evidence: the sale's stock-ledger `sale` movement for that sale/variant/location with reliable valuation, and quantity consistent with the line (VI-05). Otherwise block. Never use current cost.
- The same variant in and out produces two ledger rows; no netting.

## 14. Pricing, VAT and discount calculations
```text
credit_net(line)  = ShareOfReturn(TaxableAmount, returnedSoFar, qty, sold)
credit_vat(line)  = ShareOfReturn(VatAmount,    returnedSoFar, qty, sold)
credit_gross      = credit_net + credit_vat
cost_restored     = ShareOfReturn(ToCurrency(HistoricalUnitCost × sold), returnedSoFar, qty, sold)
                    (+ packaging share per SaleUomReversal rules)
replacement_*     = SaleCheckoutCalculator result (net after discount, VAT by rate group,
                    largest-remainder allocation; TaxInclusive honoured)
net_adjustment_gross = Σ replacement_gross − Σ credit_gross
net_adjustment_net   = Σ replacement_net   − Σ credit_net
net_adjustment_vat   = Σ replacement_vat   − Σ credit_vat
```
- For replacement-line sources, use the line's own `taxable_amount`, `vat_amount`, `historical_unit_cost` and `issued_quantity` with the same function.
- Rounding: `MonetaryRounding.ToCurrency` (2 dp, away from zero); no new rounding rule.
- Source lines without `TaxableAmount`/`VatAmount` are blocked (C-09).

## 15. Cash session validation and drawer availability
- Session: `ValidateActiveForSaleAsync` (FOR SHARE, status, cutoff, audited block) at step 1; `ValidateFinalGateAsync` before commit.
- **Drawer lock (Proposed, VI-12):** `pg_advisory_xact_lock(hashtextextended('cash-drawer:{business}:{cash_session_id}'))`, the same key style as the existing per-source lock (`CashSessionMovementRecorder.cs:35-40`). It is taken by the Exchange for cash-out **and** added to `CashSessionMovementRecorder` for every movement with a negative `CashEffect` (refund payouts, drawer expenses). This adds locking only, never validation. It is always the **last** lock in every flow, so it cannot form a cycle. Then compute `CashSessionExpectedCashCalculator.Calculate(facts).ExpectedClosingCash`, with `exchange_cash_in` and `exchange_cash_out` included (they need explicit cases; §3.14). Require it to be ≥ the cash refund (D-02). No override. A FOR UPDATE on the session row is avoided because checkout holds FOR SHARE on the session before touching inventory, which would create a deadlock cycle.
- Collections: `exchange_cash_in` = cash amount retained (tendered − change).

## 16. Split settlement and payment accounts (EX-07, EX-08, C-06, D-09)
- Collect: 1..n settlements; Σ = difference; cash tender ≥ cash amount; change only on cash.
- Refund: v1 has **one** refund settlement per Exchange (Proposed). Methods are cash, card or mobile. Card and mobile need `externalReference` + `externalRefundConfirmed = true`; the operator and timestamp are recorded.
- Refund authorization (OC-03): Case A (cashier permission) or Case B (a consumed `refund` approval whose payload hash includes this exact amount and method). In both cases the D-02 drawer check, D-09 evidence, C-06 account mapping and all other exceptions still apply.
- Account resolution: `PaymentPostingPolicy.ResolveSlot(method, businessDate, BankClearingActivationDate)` → `ResolvePostingAccountAsync(slot, outletId)`. Rules:
  - the method whitelist makes the generic `_ => Bank` branch unreachable;
  - a missing mapping returns `EXCHANGE_PAYMENT_ACCOUNT_MISSING` (no fallback);
  - the existing pre-clearing-activation `card/mobile → Bank` rule is configured policy, not a fallback (finance confirmation **VI-03**).
- Resolution logic currently lives in `API/Services` (it depends on `API.DTOs.Accounting.PostingAccountSlots`). The Exchange uses an application port (`IExchangeJournalPoster`) implemented beside `SaleFinancialJournalPoster`.

## 17. Accounting journal creation (C-04, C-06, D-15)
One journal per Exchange; one per reversal (negated). Source key `exchange:{id}:v1`. Created and posted with the existing commands **inside the Exchange transaction**.

**Dates (VI-02, verified):** `PostingDate = BusinessDate = exchanges.business_date.ToDateTime(MinValue, DateTimeKind.Utc)`; `TransactionDate = completed_at_utc`. This copies the refund reversal precedent (`SaleFinancialJournalPoster.cs:107-110`; `SaleFinancialSnapshots.cs:265`). `ManualJournalDraftStore.UtcCalendarDate` (:1111-1114) then checks exactly the cash-session business date. **Not** the sale-journal pattern, which posts on the UTC day of the instant.

| Line | Dr | Cr |
|---|---|---|
| Return revenue | Sales Return = Σ credit_net | |
| Return VAT | VAT Payable = Σ credit_vat | |
| Return stock | Inventory = Σ cost_restored | COGS = Σ cost_restored |
| Replacement revenue | Sales Discount = Σ discount | Sales = Σ (replacement_net + discount) |
| Replacement VAT | | VAT Payable = Σ replacement_vat |
| Replacement stock | COGS = Σ replacement_cost | Inventory = Σ replacement_cost |
| Collect | Payment account(s) = Σ collections | |
| Refund | | Refund account = refund amount |

Balance: Dr − Cr = Σ credit_gross − Σ replacement_gross + collect − refund = 0.
- Account slot names are those used by `SaleFinancialJournalPoster` (exact identifiers: VI-01).
- Slot identifiers (verified, `AccountingDtos.cs:103-132`): `sales`, `sales_return`, `sales_discount`, `vat_payable`, `cogs`, `inventory`, `cash`, `bank`, `card_clearing`, `mobile_mfs_clearing`.
- The period guard (`ValidateOpenPostingPeriodAsync`) throws a **generic `InvalidOperationException` with message text** for a non-Open or missing period (`ManualJournalDraftStore.cs:1085-1105`). To get typed codes without parsing messages, the handler **pre-validates under the period row lock (step 14a)**:
  - period status: maps to `EXCHANGE_ACCOUNTING_PERIOD_CLOSED`;
  - slot mappings via `ResolvePostingAccountQuery`, which throws on unmapped slots (:71-107): maps to `EXCHANGE_PAYMENT_ACCOUNT_MISSING` / `EXCHANGE_ACCOUNTING_CONFIGURATION_MISSING`.

  The engine guard remains the final authority. Any engine exception rolls back the whole Exchange (D-15, C-07).
- **Period race (VI-18):** posting reads and period status changes take no lock (`ManualJournalDraftStore.cs:1078-1096`; `AccountingPeriodCommandStore.cs:171-240`; `AccountingPeriodCloseRunCommandStore.cs:25-82,386-491`). The Exchange's `FOR SHARE` on the period row makes a concurrent status `UPDATE` wait (or the Exchange reads the committed Locked/Closed status). This protects Exchange postings only; other flows keep the pre-existing race. Verify with T-02.
- **Finance attention (VI-20):** the provisioned default `vat_payable` account is seeded from the supplier-payable code (`AccountingConfigurationProvisioner.cs:74`).

**Note:** POS sales, voids and refunds use the asynchronous D10 posting pipeline (`sale_financial_postings`). The Exchange deliberately does not, per C-04, C-06 and D-15. `.claude/rules/pos-business-safety.md` also requires journal posting inside the owning transaction. No `sale_financial_postings` change is needed.

## 18. Transaction boundary and row locking (C-03, C-04)
One `IExchangeStore.BeginTransactionAsync` (ambient-aware) in `ConfirmExchangeCommand` / `ReverseExchangeCommand`. Global lock order (Stage 0 §7.2; aligned with checkout `CreateSaleCommand.cs:138→280→352→399`):

1. idempotency lookup (unique index);
2. `product_variants` FOR SHARE, sorted, covering every return, replacement and packaging variant;
3. `sales` FOR UPDATE, ascending ID;
4. `exchanges` FOR UPDATE (reversal only) then `exchange_replacement_lines` FOR UPDATE, ascending ID;
5. cash session **FOR SHARE** (validator);
6. **all** receiving-outlet `inventories` in **one** statement `… ORDER BY id FOR UPDATE` (not per line; not by variant); insert missing rows afterwards;
7. pack-state balances (`IPackStateLedger.GetBalancesForUpdateAsync`);
8. `exchange_approvals` FOR UPDATE;
9. `accounting_periods` (+ financial year) FOR SHARE;
10. `exchange_number_counters` upsert;
11. journal engine (journal row FOR UPDATE, journal number allocator);
12. cash movement: per-source advisory lock + drawer advisory lock (cash-out), **always last**.

Existing flows (verified orders):
- **checkout:** variants SHARE → [sale] → session SHARE → invoice counter → inventory (EF updates by PK) → pack state;
- **refund:** sale UPDATE → variants SHARE → inventory in **request order** → session SHARE + advisory;
- **void:** session SHARE + advisory → conditional sale UPDATE → inventory in **line order** → variants SHARE;
- **transfer / adjustment / GRN:** inventory in line order;
- **UOM config save:** variants UPDATE → inventories ORDER BY id;
- **cash close:** session FOR UPDATE only.

Result: no cycle on the sale, session, counter, variant, period or drawer edges. **A residual, pre-existing inventory-order cycle class** remains with refund, void, transfer, adjustment and GRN, because they lock inventory in request or line order. The Exchange cannot remove it. PostgreSQL deadlock detection aborts one transaction; the Exchange maps this to `409 EXCHANGE_CONCURRENCY_CONFLICT` (retry). Verify with race matrix T-03 (VI-12).

## 19. Idempotency and retry handling
- Key: `idempotencyKey` (≤ 120 characters) in the body, the same as checkout and refund. Fingerprint = SHA-256 of the canonical request (sorted lines, settlements, approvals excluded).
- Replay returns the stored `ExchangeDto` (200). A different fingerprint returns 409.
- The frontend keeps the key until a terminal result (the existing POS pattern).
- Reversal: the same scheme on `exchange_reversals`.
- Derived uniqueness:
  - journal source keys;
  - cash movement `SourceKey = exchange:{id}:settlement:{n}`;
  - ledger entries keyed by line;
  - outbox `MessageId` = deterministic UUIDv5(exchange ID, event type).

## 20. Transactional outbox events
Appended with `IIntegrationOutbox.AppendSourceChangeAsync` inside the transaction (verified: it needs only an active transaction and a matching business; `IntegrationOutbox.cs:19-27`; the cash/sale event writers use it). `AppendAsync` is **not** used: it requires `audit.RequiresAtomicPersistence` (:12-17, :68-73), which only `RequiredAuditTransaction` provides. That refuses to nest and would reject the journal store's Accounts-module audits (`AuditService.cs:86-87`).
- `pos.exchange.completed` (schema 1);
- `pos.exchange.reversed` (schema 1).

Envelope: `BusinessId`, `Producer = "pos"`, `AggregateType = "exchange"`, `AggregateId = id`, correlation ID. Payload: `businessId`, `exchangeId`, `exchangeNumber`, `businessDate`, `receivingOutletId`, `customerId`, `completedAtUtc`, line IDs and amounts, `loyaltyStatus`. No phone, name or credentials.

Rules:
- `DurableDelivery:Enabled=false`, so the events are notifications only. Consumers (future Loyalty, EXL-01) must reconcile from the tables.
- **VI-11 (verified):** `FrozenSystemJournalStore` is used only by payroll postings, not by the sale or journal-command path, so its atomic-scope requirement does not apply. There is no event consumer today (only `OperationalLocationProjectionConsumer` exists).

## 21. Exchange History queries
- `GetExchangeHistoryQuery`: filters (FR-EX-150), `business_id` + read-scope outlets, order `completed_at_utc DESC, id DESC`, `pageSize` ≤ 100, `AsNoTracking`, projected read model.
- Search by original invoice uses `exchange_source_invoices.invoice_number_snapshot`.
- `GetExchangeDetailQuery`: header + children; settlement references masked (last 4 characters); approvals show the supervisor name, type and reason.
- `GetExchangesBySaleQuery`: used by Sale Details.

## 22. Exchange Reversal compensation logic (EX-14, C-05)
1. Idempotency; session FOR SHARE (current active session at the receiving outlet; a new business date is allowed).
2. Lock `exchanges` row: must be `completed` and have no reversal.
3. Lock the Exchange's replacement lines. **If any `returned_quantity > 0`** (consumed by an active downstream Exchange) → `409 EXCHANGE_REVERSAL_BLOCKED_DEPENDENCY`. A downstream Exchange that was itself reversed has already restored its consumed quantity (it decremented `returned_quantity`), so it no longer blocks.
4. Lock source sales and their `sale_items` (ascending): decrement `returned_quantity` by the Exchange's returned quantities (CHECK ≥ 0). Lock the source replacement lines and decrement them the same way.
5. Lock inventories: the returned goods must still be available (`Quantity ≥ returned qty`), otherwise `422 EXCHANGE_REVERSAL_STOCK_UNAVAILABLE`. Add the replacement goods back (the cashier attests that the customer handed them back).
6. Settlement: opposite direction for the same amount (a collection becomes a refund under D-02/D-09 rules; a refund becomes a collection). Methods are chosen at reversal time.
7. Journal: the negated composition, dated on the reversal business date (period guard).
8. Salesperson fact: the negated original amounts, original mode snapshot, reversal business date.
9. Reversal receipt snapshot, audit, outbox `pos.exchange.reversed`.
10. Header `status = reversed`, `reversal_id` set.

There is no partial reversal and no cascade.

### 22.1 Legally mandatory remedy boundary (D-18, OC-04)
- No endpoint lets the ordinary Sales Refund process an Exchange replacement line: `RefundSaleCommand` accepts only `SaleItem` IDs of a `Sale`.
- For an escalated statutory or consumer-rights case, the only **existing** system processes are:
  1. `ReverseExchangeCommand` (subject to all §22 guards), and then
  2. the existing `RefundSaleCommand` on the restored original-sale quantity.

  Both are authorized and audited.
- If either is blocked (downstream Exchange, stock unavailable, closed period, missing mapping), **no automated workaround exists or may be added**. This is recorded as the operational/technical gap G-46.
- Staff must not make manual cash payouts, duplicate refunds or inventory adjustments outside authorized processes.
- A complaint or legal-remedy subsystem is out of scope.
- Stage 0 verifies that the two-step path reconciles: stock, cash, GL and the `returned_quantity` restore.

## 23. Reporting projection integration
| Consumer | Change |
|---|---|
| Owner dashboard `SalesFinanceReadStore` | Add Exchange facts per (business_date, receiving_outlet): `net_adjustment_net` (Net Sales), replacement and return components, COGS (`replacement_cost − return_cost`). Reversal facts on the reversal date. `SalesTargetPerformance.ForShowroom` picks them up through `SalesDayFact.NetSales` (D-06 outlet target). |
| Legacy warning | `UnreversedExchanges` stays for historical linked rows (D-12). B-03 closes only after reconciliation (Stage 5). |
| Mobile `SalesReportReadStore` | **Verified risk (VI-19):** revenue = Σ sale `TotalAmount` − **posted "Sales return" journal debits** by `PostedAt` (`SalesReportReadStore.cs:13-119`). The Exchange journal's Sales Return debit would be netted automatically while the replacement revenue (not a Sale) is never added. Required: add the Exchange replacement gross, **or** exclude Exchange journals by reference type and add the signed Exchange gross adjustment. Use one method only, never both. |
| VAT report | Return VAT (negative, original rate) and replacement VAT (current rate) on the exchange date |
| `GetExchangeMetricsQuery` | Regular sales, replacement value, returned value, signed adjustment, total net; incl. and excl. VAT; salesperson positive, negative and target effect |
| Salesperson facts | `exchange_salesperson_adjustments` (§3.11). Individual target management deferred. |

Projections read authoritative tables at query time (the current pattern). Any future cache or projection may be eventually consistent (C-04).

## 24. Error contracts (Proposed codes; `ApiResponse` errors)
| Code | HTTP | Meaning |
|---|---|---|
| `EXCHANGE_NOT_CONFIGURED` / `EXCHANGE_DISABLED` | 409 | D-16 |
| `EXCHANGE_SOURCE_NOT_FOUND` | 404 | Not found, or foreign business |
| `EXCHANGE_SOURCE_INELIGIBLE` | 422 | Status (voided, refunded), etc. |
| `EXCHANGE_SOURCE_EVIDENCE_MISSING` | 422 | C-09 |
| `EXCHANGE_CUSTOMER_MISMATCH` / `EXCHANGE_WALKIN_MULTI_INVOICE` | 422 | EX-23, D-03 |
| `EXCHANGE_ITEM_MISMATCH` | 422 | EX-26 |
| `EXCHANGE_CONDITION_NOT_SELLABLE` | 422 | EX-05 |
| `EXCHANGE_QUANTITY_EXCEEDS_REMAINING` | 409 | C-03 |
| `EXCHANGE_APPROVAL_REQUIRED` (type list) / `EXCHANGE_APPROVAL_INVALID` | 422 / 403 | C-02 |
| `EXCHANGE_APPROVAL_NOT_PERMITTED` | 403 | The supervisor authenticated but lacks the permission or receiving-outlet access (OC-01, OC-02, OC-03) |
| `EXCHANGE_PHONE_VERIFICATION_UNAVAILABLE` | 422 | Registered customer has no recorded phone while verification is required (VI-16 placeholder) |
| `EXCHANGE_LOWER_PRICE_DISABLED` | 422 | EX-02 |
| `EXCHANGE_REFUND_FORBIDDEN` | 403 | `pos.exchange.refund` missing |
| `EXCHANGE_SETTLEMENT_MISMATCH` / `EXCHANGE_REFUND_EVIDENCE_REQUIRED` | 400 | EX-07, D-09 |
| `EXCHANGE_CASH_INSUFFICIENT` | 422 | D-02 |
| `EXCHANGE_SESSION_REQUIRED` / existing session-overdue code | 409 | EX-15 |
| `EXCHANGE_STOCK_INSUFFICIENT` / `EXCHANGE_STOCK_LOCATION_MISSING` | 422 | EX-06 |
| `EXCHANGE_PAYMENT_ACCOUNT_MISSING` / `EXCHANGE_ACCOUNTING_CONFIGURATION_MISSING` | 422 | C-06 |
| `EXCHANGE_ACCOUNTING_PERIOD_CLOSED` | 422 | D-15 |
| `EXCHANGE_PREVIEW_STALE` / `EXCHANGE_IDEMPOTENCY_MISMATCH` / `EXCHANGE_CONCURRENCY_CONFLICT` | 409 | EX-29, C-04 |
| `EXCHANGE_PHONE_VERIFICATION_FAILED` | 422 | EX-18 |
| `EXCHANGE_REASON_REQUIRED` | 400 | EX-17 |
| `EXCHANGE_OUTLET_PREFIX_MISSING` | 422 | D-19 |
| `EXCHANGE_REVERSAL_BLOCKED_DEPENDENCY` / `EXCHANGE_ALREADY_REVERSED` | 409 | C-05 |
| `EXCHANGE_REVERSAL_STOCK_UNAVAILABLE` | 422 | C-05 |
| `SALE_EXCHANGE_LINK_DEPRECATED` | 400 | D-01 (`POST /api/sales`) |
| `SALE_HAS_RETURNS` | 409 | D-14 (`POST /api/sales/{id}/void`) |

No exception details or stack traces are returned. A 403 never logs the user out.

## 25. Migration design (each needs explicit approval; additive only; rehearse on the isolated Test database first)
| Proposed migration | Contents | Stage |
|---|---|---|
| `PosV2ExchangeFoundation` | §3.1–3.12 tables; triggers (policy append-only, header immutability); CHECKs and indexes | Plan Stage 2 |
| `PosV2ExchangeReversals` | §3.13 + reversal FKs on settlements and adjustments | Plan Stage 4 |
| Required (in `PosV2ExchangeFoundation` or a separate approved migration) | Extend `ck_cash_session_movements_type` and `ck_cash_session_movements_effect` for `exchange_cash_in` (+) / `exchange_cash_out` (−) (VI-07, verified). No stock-ledger constraint change (VI-15: none exist). | Stage 2 |

No changes to `sales` or `sale_items` columns. No data backfill, `DROP` or `TRUNCATE`. Legacy columns are kept. Permission keys are code-only. Template role grants (D-17) are a seeding change that needs separate approval and applies **only to new businesses' templates**. The existing "add missing permission to existing roles" seeder pattern (`DbSeeder.cs:759-768`) must not be used for Exchange keys.

## 26. Test scenarios
**Unit (`tests/RetailPOS.UnitTests`)**
- `ShareOfReturn` across a mix of refund and exchange; residual on the last portion; fractional UOM; legacy whole quantities.
- Replacement XOR discount and limits; VAT groups (inclusive and exclusive).
- Settlement: collect, refund, none; split; change; lower-price disabled; D-09 evidence.
- Policy evaluator:
  - window boundary (OC-07: Day 0, Day N allowed, Day N+1 expired, purchase near local midnight, month-end and 29 Feb, DST zone, session date ≠ local date);
  - replacement window anchored on the issuing Exchange's local date;
  - chain depth;
  - high value (OC-05: every AC-50 case, including equality and zero difference);
  - phone classification (OC-06: every AC-51 case);
  - refund path (OC-03: Case A / Case B).
- Same customer and walk-in (D-03).
- Journal composer balance for all leg combinations; account-missing handling.
- Expected-cash calculator with the new movement types.
- Exchange number rendering.

**PostgreSQL integration (new `PostgresTestDatabase` prefix `Exchange`; patterns from `SaleCreationPostgresConcurrencyTests` and `UomPostgresHttpTests`)**
- Concurrency:
  - refund vs exchange on the last quantity;
  - exchange vs exchange;
  - re-exchange vs reversal;
  - replacement stock last unit vs checkout;
  - exchange cash-out vs refund payout vs drawer expense (never negative);
  - the lock-order matrix (no deadlock).
- Atomicity: fault injection after each write step leaves the database unchanged (AC-40).
- Idempotency: replay, payload mismatch, crash between commit and response.
- Approvals:
  - every AC-31 case; the token is single-use under concurrent confirms; no credential in logs (log capture);
  - OC-01 (AC-46): supervisor assigned or not assigned to the receiving outlet, Business Owner global, assignment revoked between approval and Confirm, cashier UI outlet context ignored;
  - OC-02 (AC-47): the chain cases, incl. chain depth changing before Confirm;
  - OC-03 (AC-48): Case A / Case B, refund amount or method changed after approval, refund plus window plus high-value together (separate records);
  - OC-05 (AC-50): price, discount or VAT change between Preview and Confirm invalidates the `high_value` approval;
  - OC-07 (AC-52): Confirm crossing local midnight re-evaluates.
- Remedy boundary (OC-04, AC-49): reversal then refund of the restored quantity reconciles stock, cash, GL and `returned_quantity`; blocked reversal produces no side effects.
- Tenancy and authorization (C-08): foreign IDs; cross-outlet exact lookup; read scope; disabled or revoked users; `POST /api/sales` legacy link rejected; void with returns rejected.
- Period and accounts: a closed or locked period blocks; missing mapping blocks; the journal balances and is linked.
- Reversal: dependency block; stock unavailable; success compensations on the later date.
- Data compatibility: a sale without allocation or cost evidence is blocked; ledger-evidence path (VI-05).
- Reporting: owner and mobile dashboards and outlet target reconcile with exchange facts; legacy warning unchanged.
- Outbox: one envelope per Exchange; replay is idempotent.

**Frontend (Vitest)**
- Entry points; offline disable; stepper validation; scan vs manual match.
- Approval dialog: no password retained in state after the request; lists every required type with a reason per type (OC-02); refund approval shown only for Case B (OC-03).
- Settlement rendering; idempotency key reuse; receipt component.
- History scope; role-form group; policy form (disabled until saved).
