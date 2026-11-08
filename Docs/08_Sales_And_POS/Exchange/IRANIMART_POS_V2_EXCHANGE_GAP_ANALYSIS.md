# IraniMart POS v2 — Product Exchange: Gap Analysis

**Document ID:** IM-POS-EX-GAP-001
**Version:** 2.2 (Stage 0 results incorporated: §4.1, §4.2, §8)
**Date:** 2026-11-07
**Status:** All 67 decisions are approved: EX-01–EX-31, D-01–D-19, C-01–C-10 and clarifications OC-01–OC-07 (SRS §26.3). No code is implemented. **Stage 0 audit complete: CONDITIONAL PASS.** Twenty verification items (§4: VI-01…VI-20, with statuses in §4.1) and the technical blockers (§8) remain. **One new business decision (DN-01, §4.2) is raised by Stage 0** for phone verification in `required` mode for registered customers. Resolving a decision does not close an implementation gap.
**Companions:** SRS v2.0, LLD v2.0, Implementation Plan v2.0 (same folder).
**Audit baseline:** branch `staging`, HEAD `3aa5f986`, read-only. Paths are relative to the repo root.

**Status categories (used exactly):**
- Implemented
- Partial
- Missing
- Conflicting
- Verification Required
- Resolved by Decision
- Deferred / Out of Scope

A business decision being approved never marks an implementation gap as fixed.

---

## 1. Executive summary
1. **No Exchange module exists.** There is only the legacy one-to-one link `Sale.OriginalSaleId`/`ReplacementSaleId`. It moves no stock or money, mutates the original sale row and is double-counted on the dashboard (B-03). D-01 deprecates it for new sales.
2. **The refund engine is the right foundation (C-03):**
   - line-level `sale_items.returned_quantity` with a DB CHECK;
   - sale row `FOR UPDATE`;
   - cumulative `ShareOfReturn` rounding;
   - multiple refunds per sale, idempotent by key and fingerprint;
   - snapshot-based VAT and cost.
3. **Reusable as-is:**
   - pricing, VAT and discount pipeline (`IPosCartPriceCalculator`, `SaleCheckoutCalculator`, `SaleDiscountAuthorizer`);
   - cash-session validator and 20-hour guard;
   - stock ledger writer;
   - journal engine (joins the ambient transaction);
   - invoice-counter allocator pattern;
   - BCrypt `IPasswordHasher`;
   - `IIntegrationOutbox`;
   - shared Angular UI kit and POS services.
4. **Missing capabilities:**
   - supervisor credential approval (C-02);
   - drawer-sufficiency guard (D-02);
   - restocking at a different outlet (EX-06; refunds hard-code `sale.OutletId`);
   - exact cross-outlet invoice lookup (D-10);
   - salesperson facts (D-06);
   - exchange-aware dashboards (EX-20);
   - offline detection in the frontend (EX-28);
   - void guard for returned quantity (D-14).
5. **Design change from earlier drafts:** the Exchange journal is posted **synchronously inside the Exchange transaction** through the existing journal engine (C-04, C-06, D-15). POS sales, voids and refunds keep the approved asynchronous D10 pipeline. This is verified as technically supported (§3, TF-14); the remaining checks are VI-01, VI-02 and VI-11.
6. **Implementation may start only with Stage 0** (read-only compatibility audit, Plan §2). Coding stages are gated on Stage 0 exit and migration approval. OC-01–OC-07 are approved (2026-11-07), so they no longer gate coding. Their implementation gaps remain (G-04, G-39, G-45, G-46, G-47).

## 2. Evidence base
| Area | Verified files |
|---|---|
| Refund engine | `src/RetailPOS.Application/Sales/Commands/RefundSaleCommand.cs`; `src/RetailPOS.Infrastructure/Sales/SaleVoidStore.cs` (sale FOR UPDATE :55-61; inventory FOR UPDATE + xmin :69-84); `Infrastructure/Data/UomModelConfiguration.cs:160-203,234` |
| Return valuation | `Application/Sales/FinancialPostings/SaleFinancialSnapshots.cs:140-146,234-243,317-334` |
| Checkout | `Application/Sales/Commands/CreateSaleCommand.cs`; `Sales/Checkout/{SaleCheckoutCalculator,SalePaymentPlanner,SaleRequestFingerprint}.cs`; `SaleDiscountAuthorizer`; `Infrastructure/Sales/{SaleCreationStore,SaleInvoiceNumberAllocator}.cs`; `Application/Sales/Abstractions/ISaleCreationServices.cs:15-30` |
| Void | `Application/Sales/Commands/VoidSaleCommand.cs:158-171`; `SaleVoidStore.cs:38-53` |
| Legacy exchange | `CreateSaleCommand.cs:225-227,298,416-420,522-547`; `API/Controllers/SalesController.cs:459-464`; `SaleCreationStore.cs:81-97` |
| Cash session | `Domain/Entities/CashSessionEntities.cs`; `Infrastructure/Pos/{SaleCashSessionValidator,CashSessionMovementRecorder,SaleCashRefundRecorder,CashSessionStore}.cs`; `Application/Pos/CashSessionExpectedCashCalculator.cs`; `Domain/Entities/BusinessPosSettings.cs:9-10` |
| Accounting | `Domain/Entities/{SaleFinancialPosting,AccountingPeriod}.cs`; `API/Services/{SaleFinancialJournalPoster,PaymentPostingPolicy}.cs`; `Infrastructure/AccountingJournals/{ManualJournalDraftStore,FrozenSystemJournalStore,JournalNumberAllocator}.cs`; `Application/AccountingCloseRuns/Queries/AccountingPeriodCloseFinancialGateEvaluator.cs` |
| Inventory/UOM | `Domain/Entities/{Inventory,StockLedgerTransactionType}.cs`; `Infrastructure/Inventory/StockLedgerWriter.cs`; `API/Services/SaleCreationAdapters.cs:142-163,213-252`; `Sales/Checkout/SaleUomLines.cs:113-205`; `Application/Uom/UomCore.cs` |
| Auth | `Application/Authentication/{Abstractions/IPasswordHasher.cs,Commands/AuthenticationCommands.cs}`; `Infrastructure/Authentication/BCryptPasswordHasher.cs`; `API/Controllers/AuthController.cs` (rate limiting); `API/Program.cs:1154+` |
| Permissions | `Application/Identity/Permissions/PermissionCatalog.cs`; `API/Program.cs:99-113`; `Infrastructure/Data/DbSeeder.cs`; `retailpos-frontend/src/app/components/roles/role-form.component.ts` |
| Reporting | `Infrastructure/OwnerDashboards/SalesFinanceReadStore.cs`; `Application/OwnerDashboards/{SalesFinanceQueries,SalesTargetPerformance,OwnerDashboardContracts}.cs`; `Infrastructure/SalesReports/SalesReportReadStore.cs`; `Domain/Entities/OutletSalesTarget.cs` |
| Outbox | `Application/Integration/DurableDeliveryContracts.cs`; `Infrastructure/Integration/*` |
| Receipt | `Domain/Entities/ReceiptPrintHistory.cs`; `Application/Sales/Commands/GenerateSaleReceiptDocumentCommand.cs`; `Infrastructure/Sales/SaleReceiptPdfRenderer.cs`; frontend `pages/pos/{thermal-receipt.component.ts,receipt-print-page.ts}` |
| Frontend | `pages/pos/pos-screen.component.{ts,html}` (2742/864 lines); `pages/sales/{sales-list,sale-details}.component`; `components/shared/ui/*`; `services/{sale,pos,cash-session}.service.ts` |

## 3. Verified code and schema facts (TF)
| ID | Fact | Evidence |
|---|---|---|
| TF-01 | Refunds are line-level. `returned_quantity` is protected by CHECK `0 ≤ returned ≤ quantity`. Status is `partially_refunded` or `refunded`. Multiple refunds are allowed with an idempotency key. | `RefundSaleCommand.cs:102-109,234-239`; `UomModelConfiguration.cs:234` |
| TF-02 | Refund locks the sale row FOR UPDATE, then inventory FOR UPDATE (xmin), then the session (FOR SHARE, inside the recorder). | `SaleVoidStore.cs:55-84`; `CashSessionMovementRecorder.cs:136-146` |
| TF-03 | `SaleItem.TaxableAmount`, `VatAmount` and `HistoricalUnitCost` are **nullable**. A missing allocation leads to `MissingEvidence`; a missing cost makes the refund ledger writer throw. | `SaleItem.cs:11-21`; `RefundSaleCommand.cs:295-314`; `SaleCreationAdapters.cs:223-225` |
| TF-04 | The invoice-discount share is folded into `TaxableAmount`; there is no separate column. | `SaleCheckoutCalculator.cs:161-162` |
| TF-05 | Refund and void stock and ledger writes target `sale.OutletId`. | `SaleCreationAdapters.cs:213-252`; `SaleUomLines.cs:113-205` |
| TF-06 | Void accepts only `completed` sales. A partially refunded sale is already rejected. A sale used by an Exchange would remain `completed`. | `VoidSaleCommand.cs:158-171` |
| TF-07 | Payment methods: `cash`, `card`, `mobile` only. No provider reference or status. | `SalePaymentPlanner.cs:22-31`; `SalePayment.cs` |
| TF-08 | No mid-session cash count and no payout ≤ balance guard anywhere. Expected cash is calculated by `CashSessionExpectedCashCalculator`. | `CashSessionExpectedCashCalculator.cs:76`; recorders |
| TF-09 | Business date comes from the cash session (`date`), company time zone. `sales.business_date` is also `date`. | Migration 20261103091638; `CreateSaleCommand.cs:301` |
| TF-10 | Every walk-in sale uses one system `WALKIN` customer per business. | `CreateSaleCommand.cs:101-116,549-578` |
| TF-11 | Sales search has no invoice-number or phone filter. | `SaleReadStore.cs:185-241` |
| TF-12 | Selling options have no barcode; a barcode resolves to a variant. | `ProductVariant.cs:12`; `PosController.cs:330` |
| TF-13 | Invoice number = `{OutletPrefix}{yyMMdd}{seq}`, no padding; atomic counter per (business, outlet, business_date); a missing prefix is an error. | `ISaleCreationServices.cs:15-24`; `SaleInvoiceNumberAllocator.cs:22-30,77` |
| TF-14 | The journal engine joins an ambient transaction (`ownsTransaction = CurrentTransaction is null`). The D10 worker already runs `CreateOrGetSystemJournalDraftCommand` + `PostJournalEntryCommand` inside its own transaction. | `ManualJournalDraftStore.cs:173-178`; `SaleFinancialJournalPoster.cs:198-209`; `SaleFinancialPostingCommands.cs:57-129` |
| TF-15 | The open-period guard lives in the journal posting path: `ValidateOpenPostingPeriodAsync` → `ResolveOpenPeriodContextAsync` (posting date and business date). No read-side "is period open" port exists. | `ManualJournalDraftStore.cs:995-1016` |
| TF-16 | The period-close gate evaluator has no gate for pending or failed sale postings. | `AccountingPeriodCloseFinancialGateEvaluator.cs` |
| TF-17 | Payment slot resolution: cash → Cash; card/mobile → clearing slots after `BankClearingActivationDate`, otherwise Bank; unknown → Bank. The account comes from `ResolvePostingAccountAsync(slot, outletId)`. | `PaymentPostingPolicy.cs`; `SaleFinancialJournalPoster.cs:228-249` |
| TF-18 | Password verification: `IPasswordHasher` (BCrypt). Login checks `IsActive` and the password; effective permissions = role + overrides. Login is rate-limited (`auth-login`). No account lockout and no step-up approval exist. | `AuthenticationCommands.cs:7-66`; `AuthController.cs:33`; `Program.cs:1154` |
| TF-19 | `ReceiptPrintHistory.SaleId` is non-nullable. Sale receipts are rendered live, not snapshotted. | `ReceiptPrintHistory.cs`; `GenerateSaleReceiptDocumentCommand.cs` |
| TF-20 | Outbox: `IIntegrationOutbox.AppendAsync`; the envelope payload must contain a matching `businessId`; durable delivery is disabled. | `DurableDeliveryContracts.cs`; `.claude/rules/messaging-cache.md` |
| TF-21 | Loyalty: not implemented; checkout `earnedPoints = 0`; the legacy integer endpoints are feature-flagged off. | `CreateSaleCommand.cs:449-451`; `CustomersController.cs:148-157` |
| TF-22 | Only outlet targets exist (`OutletSalesTargetVersion`). Achievement uses `SalesDayFact.NetSales`. | `OutletSalesTarget.cs`; `SalesTargetPerformance.cs:46-51` |
| TF-23 | The owner dashboard nets refunds on their accounting date and warns about legacy linked sales (`UnreversedExchanges`). | `SalesFinanceReadStore.cs:183-207`; `SalesFinanceQueries.cs:92-93` |
| TF-24 | The POS screen is one 2742-line component with an inline pay panel. Barcode handling is in that component. There is no frontend offline detection. | `pos-screen.component.ts` |
| TF-25 | No Exchange permission keys, tables, endpoints or UI exist. `StockLedgerTransactionType.Exchange` is reserved but never written. | Repository search |
| TF-26 | Outlet access is evaluated from an explicit user context: `IUserOutletAccess.GetAuthorizedOutletsAsync(UserOutletAccessContext)` → `IUserOutletAccessStore.ListActiveOutletAssignmentsAsync(userId)` (`UserOutletAssignment.IsActive`), returning `IsGlobalAccess` for all-outlet entitlements. It is reusable for a supervisor (OC-01). | `Application/Identity/LocationAccess/{UserOutletAccess,IUserOutletAccessStore}.cs`; `Domain/Entities/UserOutletAssignment.cs` |
| TF-27 | `Customer` has `Phone`/`PhoneNormalized` but **no verified-phone flag, OTP or verification evidence**. `WALKIN` is identified by `IsSystem` + `CustomerCode`. | `Domain/Entities/Customer.cs` |
| TF-28 | Business zone: `ICompanyTimeZoneProvider.GetTimeZoneIdAsync`. Conversion: `CashSessionTime.FindZone` / `BusinessDateFor(utc, zone)`. Sale timestamps: `SaleDateTime = now`, `SaleDate = request.SalesDate ?? now`, `BusinessDate` = session date. | `Application/Settings/Abstractions/ICompanyTimeZoneProvider.cs`; `Application/Pos/CashSessionTime.cs`; `CreateSaleCommand.cs:270,302-306` |

## 4. Verification issues (technical; not business decisions)
| ID | Item | Why it matters | Resolve in |
|---|---|---|---|
| VI-01 | Exact account-slot identifiers and the error types thrown by the journal engine for missing configuration; prove that a synchronous post inside the Exchange transaction rolls back cleanly | C-04, C-06 | Stage 0 |
| VI-02 | Posting-date handling: `ManualJournalDraftStore` uses `UtcCalendarDate(PostingDate)`. Map the Exchange business date (company zone) so the period check uses the correct day. | D-15 | Stage 0 |
| VI-03 | Finance confirmation that the configured pre-activation `card/mobile → Bank` slot rule is policy, not a "generic fallback" under C-06 | C-06 | Stage 0 |
| VI-04 | Supervisor lookup: login identifier (email) and business association of `User`; SuperAdmin excluded. Also, how to build a `UserOutletAccessContext` for the supervisor (non-switched, own `UserId`) so that `IsGlobalAccess` reflects the Business Owner entitlement and revoked assignments are excluded (TF-26). | C-02, OC-01 | Stage 0 |
| VI-05 | Read-only data audit: count of sales and lines lacking `TaxableAmount`/`VatAmount`/`HistoricalUnitCost`, and whether `sale` stock-ledger rows give provably matching cost evidence. Needs approved read access to the target database. | C-09 | Stage 0 |
| VI-06 | Walk-in detection rule (`Customer.IsSystem` + `CustomerCode = WALKIN`) and its uniqueness per business | D-03 | Stage 0 |
| VI-07 | CHECK constraints on `cash_session_movements` type; `CashSessionCloseSnapshot` hash/formula compatibility with new movement types | D-02, EX-15 | Stage 0 |
| VI-08 | Receipt PDF renderer reuse for the Exchange document layout | EX-12 | Stage 0 |
| VI-09 | Barcode ambiguity when several invoice lines share a variant (choose a line) | EX-26 | Stage 1 |
| VI-10 | Existing creation path for a missing inventory row at the receiving outlet (otherwise block) | EX-06 | Stage 0 |
| VI-11 | Audit atomic-scope prerequisite for outbox and frozen-journal writes inside a new command (`FrozenSystemJournalStore.RequiresAtomicPersistence`; master gap analysis note) | C-04 | Stage 0 |
| VI-12 | Lock-order and deadlock analysis across checkout, refund, void, drawer expense and Exchange, including the proposed drawer advisory lock in `CashSessionMovementRecorder` | C-03, D-02 | Stage 0 analysis; Stage 5 race tests |
| VI-13 | Owner vs mobile dashboard revenue bases (net vs gross; accounting date vs posted date) | EX-20 | Stage 0 |
| VI-14 | Extraction of inlined CreateSale blocks (stock and packaging decrement, price-change check, salesperson eligibility, cost resolver) without behaviour change | Reuse | Stage 2 |
| VI-15 | CHECK constraints on stock-ledger transaction or reference type values | EX-06 | Stage 0 |
| VI-16 | **Phone verification capability:** customer phones carry no verification evidence (TF-27). Determine whether a trustworthy verification mechanism exists for registered customers. Also determine the outcome for a registered customer with no recorded phone; the LLD's placeholder blocks it (`EXCHANGE_PHONE_VERIFICATION_UNAVAILABLE`). Results are never labelled "verified identity". | OC-06, EX-18 | Stage 0 |
| VI-17 | **Original purchase instant** for Day 0: confirm `Sale.SaleDate` represents the actual purchase for regular, backdated (`sales.backdate`) and Late sales; check `DateTime.Kind` handling before zone conversion | OC-07 | Stage 0 |
| VI-18 | **Period race:** neither journal posting nor period status changes lock the accounting period row | D-15, C-07 | Stage 0 (found); T-02 |
| VI-19 | **Mobile report** automatically nets posted "Sales return" journal debits; the Exchange return leg would be netted without replacement revenue | EX-20 | Stage 0 (found); Stage 5 |
| VI-20 | Default `vat_payable` mapping is seeded from the supplier-payable code (`AccountingConfigurationProvisioner.cs:74`) | C-06 | Stage 0 (found); finance / Q10 |

### 4.1 Stage 0 results (2026-11-07; details in `IRANIMART_POS_V2_EXCHANGE_STAGE_0_TECHNICAL_AUDIT.md` §15)
| VI | Status | Key finding |
|---|---|---|
| VI-01 | Requires Database Test (T-01) | Slots verified; engine joins the ambient transaction; engine errors are text-based → typed pre-validation |
| VI-02 | Verified by Static Inspection | Post on the session business date as a UTC-kind midnight (reversal precedent) |
| VI-03 | Partially Verified (behaviour documented 2026-11-08, Plan §12.5) | Configured rule: card/mobile → Bank before `BankClearingActivationDate`, clearing slots from it, by business date. Unmapped slots throw. **Finance acknowledgement pending (release gate).** |
| VI-04 | Verified by Static Inspection | Email globally unique; `StockCountAdjustmentApproverPolicy` pattern reusable |
| VI-05 | Requires Read-Only Data Audit | Stage 0 §18 plan; ledger is no independent cost evidence for v2 sales |
| VI-06 | Verified by Static Inspection | `IsSystem` + `WALKIN`; trigger-protected; no phone |
| VI-07 | Verified by Static Inspection | **Migration required** for two CHECKs; calculator needs explicit cases; hash unaffected |
| VI-08 | Verified by Static Inspection | New Exchange PDF renderer + thermal component |
| VI-09 | Partially Verified | Barcode → variant only; line-choice UX in S1-05 |
| VI-10 | Partially Verified | Inline creation pattern exists; no 23505 handling → map to 409; T-05 |
| VI-11 | Verified by Static Inspection | Use `AppendSourceChangeAsync`; never `RequiredAuditTransaction` |
| VI-12 | Requires Database Test (T-03) | Checkout-aligned order; residual pre-existing inventory-order cycles |
| VI-13 | Verified by Static Inspection | Owner: BusinessDate + trusted reversals; mobile: TotalAmount − posted Sales Return debits |
| VI-14 | Partially Verified | Inlined blocks identified; regression-gated extraction |
| VI-15 | Verified by Static Inspection | No stock-ledger CHECKs |
| VI-16 | **Blocked** (scope: `required` mode, registered customers) | No verification capability; phone mutable; no sale snapshot → **DN-01** |
| VI-17 | Partially Verified | Company calendar date of normalised `SaleDate`; not `SaleDateTime`; read-only audit Q8 |
| VI-18 | Requires Database Test (T-02) | Exchange takes period row FOR SHARE (Proposed) |
| VI-19 | Verified by Static Inspection | Mobile store must be extended (LLD §23) |
| VI-20 | **Verified (defect confirmed, pre-existing)**, 2026-11-08 | The default chart has no output-VAT account. New businesses get `vat_payable` = 2000 Supplier Payables, so trade payables are overstated and VAT reconciliation cannot reconcile. Correction proposed in Plan §12.5 (new businesses only; existing ones changed only by a Finance decision). **Release blocker until Finance decides.** |

### 4.2 Business decision needed (raised by Stage 0)
| ID | Question | Options (not decided here) | Scope blocked |
|---|---|---|---|
| **DN-01** | `PhoneVerificationMode = required` for **registered** customers. No trustworthy verification capability exists (no OTP, no verified flag, phone editable, no phone snapshot on sales). The SRS v2.1 FR-EX-035/038 record-match must not silently replace required verification. | (a) Approve the current-record match (labelled `matched_customer_record`) as the `required` mechanism. <br>(b) `required` cannot be enabled for registered customers until a verification capability exists. <br>(c) Approve a separate verification capability (new feature). | `required` + registered-customer path only |

### 4.3 DN-01 resolution (delegated by the owner, 2026-11-07)
**Decision:** until a trustworthy phone-verification capability exists, the `required` phone-verification mode is **not offered**.
- **Policy:** `SaveExchangePolicyCommand` rejects `phoneVerificationMode = required` with `422 EXCHANGE_PHONE_VERIFICATION_UNAVAILABLE`. The settings UI shows the option disabled, with the reason.
- **No silent downgrade:** a value is never converted, and `ck_exchange_policy_versions_phone_mode` still accepts `required`, so a persisted value cannot be quietly changed. If a `required` policy ever exists:
  - registered-customer exchanges fail closed (`422 EXCHANGE_PHONE_VERIFICATION_UNAVAILABLE`);
  - walk-in invoices keep the OC-06 skip (`skipped_walkin`).
- **Normal workflow:** `optional` mode records `not_required`. The receipt, eligibility and product checks still apply in full.
- **No false claim:** no phone match is ever recorded as verified identity, and no OTP or SMS platform was built.
- **Tests:** `ExchangeRulesTests` (phone classification); `UomPostgresHttpTests.Exchange_Rules_*` (policy rejection).
- **Remaining gap:** EX-18's configurable *required* verification is unavailable until a verification capability is approved (G-45). This is recorded as a **release limitation, not a hidden pass**.

### 4.4 Post-implementation hardening (2026-11-08)
The review findings L1b, L3, L4 and L6 are **closed in code** with PostgreSQL tests. Root causes and evidence are in Implementation Plan §12.3.

| Finding | Closed by |
|---|---|
| L4 | `exchange_reversals.retained_packaging_cost`. Dashboard COGS now reconciles with the stock ledger and journals after a reversal with fractional packaging. |
| L6 | The terminal is revalidated at Confirm and Reverse: active, of the receiving outlet and business, locked FOR SHARE, and bound into the preview hash. |
| L1b | Per-login and per-cashier lockout (`exchange_approval_failures`, hashed keys) without account enumeration. The per-IP limiter is kept as the per-source control. |
| L3 | `by-sale` answers a sale outside the user's outlets exactly like a missing sale (404). |

The I-12 reporting defaults are resolved against approved rules (Plan §12.4); none changes a business rule. Two new migrations were added; they are tested on disposable databases only (Plan §12.3). Browser UAT was **executed with automation on 2026-11-08** in the persistent synthetic UAT environment:
- 25 of 25 Playwright/Edge tests passed;
- 31 of 43 checklist IDs passed in the browser;
- 12 are covered by PostgreSQL or Vitest tests;
- database reconciliation OK.

Four browser defects were found and fixed (DEF-01 to DEF-04, Plan §12.6). DEF-01 (onboarding role templates lacked the cash-session keys) was pre-existing and also affected POS. Existing roles in shared environments are left for the owner to decide (EXB-08). Human UAT sign-off is pending.

## 5. Gap register
Priority: **P0** blocks the foundation; **P1** core flow; **P2** reporting, history and reversal; **P3** polish.
Verification: **Code-verified** / **Doc-only** / **Pending (VI)**.

| Gap | Description | Decisions | Existing evidence | Required change | Components | Pri | Depends on | Status | Verification | Acceptance |
|---|---|---|---|---|---|---|---|---|---|---|
| G-01 | Exchange aggregate and schema | EX-12, EX-21, EX-22 | TF-25 | New tables (LLD §3) and entities | Domain, Infra, migration | P0 | Stage 0 | Missing | Code-verified | AC-06 |
| G-02 | Versioned business policy; disabled until saved | D-16, EX-02/03/04/18, D-07 | `BusinessPosSettings.Version` is only a counter; versioned pattern `OutletSalesTargetVersion` | `exchange_policy_versions`, GET/PUT policy, settings UI | App, API, FE | P0 | G-01 | Missing | Code-verified | AC-34 |
| G-03 | Five permissions, role group, template grants | EX-16, D-17, C-01, C-10, D-11 | TF-25; catalog auto-policy | Catalog keys; role-form group; seed **templates only** | App, FE, seeding | P0 | — | Missing | Code-verified | AC-31, AC-32 |
| G-04 | Supervisor credential approval: types `window`, `chain`, `high_value`, `refund`; supervisor receiving-outlet access; multi-type single authentication with separate records | D-04, C-02, C-10, OC-01, OC-02, OC-03, OC-05 | TF-18 (hasher and rate limiter exist; **no step-up mechanism exists**); TF-26 (outlet access reusable) | Approval endpoint, `exchange_approvals`, rate-limit policy, supervisor outlet check, consume and re-check at Confirm | App, Infra, API, FE | P0 | G-01, G-03; VI-04 | Missing (decisions OC-01/02/03 Resolved by Decision) | Pending VI-04 | AC-10, AC-31, AC-32, AC-46, AC-47, AC-48 |
| G-05 | Shared returnable quantity | C-03, EX-03 | TF-01, TF-02 (refund side implemented) | Exchange consumes `sale_items.returned_quantity` under the same lock | App, Infra | P0 | G-01 | Partial | Code-verified | AC-09, AC-19 |
| G-06 | Replacement-chain entitlement and lineage | EX-04, C-03 | None | `exchange_replacement_lines.returned_quantity` + CHECK; lineage links; window and depth | Domain, App | P2 | G-01 | Missing | Code-verified | AC-11 |
| G-07 | Original return credit | EX-09 | `ShareOfReturn` (TF-01/03/04) | Reuse; block lines without evidence | App | P1 | G-24 | Partial (component Implemented) | Code-verified | AC-05, AC-39 |
| G-08 | Replacement pricing, discount and VAT | EX-10, EX-27 | Pricing pipeline Implemented; price-change check inlined in CreateSale | Extract the helper; reuse | App | P1 | VI-14 | Partial | Code-verified | AC-24 |
| G-09 | Stock-in and stock-out at the receiving outlet | EX-06, EX-05, D-08 | TF-05 (refund uses the original outlet) | Location-parameterised ledger and UOM reversal for Exchange; refund unchanged | App, Infra | P0 | VI-10, VI-15 | Conflicting | Code-verified | AC-08 |
| G-10 | Exact cross-outlet invoice lookup | D-10 | TF-11 | New bounded exact-number query; minimal DTO | App, Infra, API | P1 | G-03 | Missing | Code-verified | AC-41 |
| G-11 | Same customer / walk-in rule | EX-23, D-03 | TF-10 | Validator | App | P1 | VI-06 | Missing | Code-verified | AC-07, AC-30 |
| G-12 | Receipt-based verification (simplified) | EX-24, EX-25, D-13 | — | Scan or enter the invoice number + validation; no proof type, checkbox or QR | App, FE | P1 | G-10 | Resolved by Decision (design); implementation Missing | Doc-only | AC-13, AC-14 |
| G-13 | Sellable condition only | EX-05 | Refund accepts `non_resellable` | The Exchange accepts only `resellable` (CHECK) | App, Domain | P1 | G-01 | Partial | Code-verified | AC-12 |
| G-14 | Collection (split) | EX-07 | `SalePaymentPlanner` (TF-07) | Exchange settlement planner (collect reuses the rules) | App | P1 | G-01 | Partial | Code-verified | AC-01, AC-06 |
| G-15 | Refund by any method with manual evidence; two authorization paths (cashier permission / supervisor refund approval) | EX-08, D-09, EX-02, OC-03 | Refund always goes back to the original tenders proportionally | Exchange-owned refund settlement and evidence; Case A/B evaluation; refund engine unchanged | App, FE | P1 | G-14, G-04 | Conflicting (vs refund engine) → resolved by separate design | Code-verified | AC-03, AC-28, AC-44, AC-48 |
| G-16 | Drawer sufficiency | EX-31, D-02 | TF-08 (no guard) | Drawer advisory lock + expected-cash check | App, Infra | P1 | VI-07, VI-12 | Missing | Pending VI-12 | AC-04 |
| G-17 | Exchange cash movements in expected cash and close | EX-15, D-02 | Movement types are refund and expense only | `exchange_cash_in` / `exchange_cash_out`; calculator update | Domain, App | P1 | VI-07 | Missing | Pending VI-07 | AC-04 |
| G-18 | Active session + 20-hour guard | EX-15 | `ISaleCashSessionValidator` | Reuse | App | P1 | — | Implemented (reusable) | Code-verified | AC-15, AC-16 |
| G-19 | Synchronous Exchange journal | C-04, C-06, EX-01 | TF-14 (engine joins the ambient transaction) | `IExchangeJournalPoster` (API/Services port impl); composition (LLD §17) | App, API | P1 | VI-01, VI-02, VI-11 | Missing | Pending VI-01 | AC-01, AC-37, AC-40 |
| G-20 | Payment/COA mapping without fallback | C-06 | TF-17 | Method whitelist; block if mapping missing | App, API | P1 | VI-03 | Partial | Pending VI-03 | AC-37 |
| G-21 | Accounting period guard at Confirm and Reversal | D-15, C-07 | TF-15 (guard exists in the posting path) | Reused by synchronous posting; map the error; optional preview warning via the period read store | App, API | P1 | G-19, VI-02 | Partial | Pending VI-02 | AC-38 |
| G-22 | Exchange number | D-19 | TF-13 (pattern) | `exchange_number_counters` + allocator | Infra | P1 | G-01 | Missing | Code-verified | AC-36 |
| G-23 | Exchange receipt 80mm/PDF snapshot | EX-12 | TF-19; `ThermalReceiptComponent` title hard-coded | Snapshot table; exchange receipt component; PDF; print history table | App, Infra, FE | P1 | VI-08 | Partial | Code-verified | AC-27 |
| G-24 | Historical evidence compatibility | C-09 | TF-03 | Evidence rule (LLD §13); read-only data audit | App; Stage 0 audit | P0 | VI-05 | Verification Required | Pending VI-05 | AC-39 |
| G-25 | Period-close gate for pending/failed sale postings | D-15, C-07 | TF-16 | None for Exchange (synchronous); limitation documented for D10 flows | — | — | — | Deferred / Out of Scope | Code-verified | — |
| G-26 | Void guard for returned quantity | D-14 | TF-06 | `VoidSaleCommand` rejects when any `returned_quantity > 0` (`SALE_HAS_RETURNS`) | App | P0 | — | Missing | Code-verified | AC-33 |
| G-27 | Legacy link deprecation | D-01 | `CreateSaleCommand.cs:522-547` accepts the link | Reject `OriginalSaleId` (400); keep data; update the 2 legacy tests | App, API, tests | P0 | — | Missing | Code-verified | AC-29 |
| G-28 | Legacy reporting warning / B-03 | D-12 | TF-23 | Keep the warning; close B-03 only after Stage 5 reconciliation | Reporting | P2 | G-29 | Partial | Code-verified | AC-01 (dashboard) |
| G-29 | Dashboard and report netting of Exchange facts | EX-20, EX-01 | TF-23 | Add facts to owner and mobile read stores, VAT report, metrics query | Infra, App, FE | P2 | G-19, VI-13 | Missing | Pending VI-13 | AC-01, AC-03 |
| G-30 | Salesperson facts and outlet target integration | D-06, D-07, EX-11 | TF-22 | `exchange_salesperson_adjustments`; outlet target via NetSales | Domain, App | P2 | G-01, G-29 | Missing | Code-verified | AC-01, AC-43 |
| G-31 | Exchange History and Sale Details links | EX-30, D-11 | None | Queries + UI; `sales.view` scope | App, API, FE | P2 | G-01 | Missing | Code-verified | AC-42 |
| G-32 | Strict controlled reversal | EX-14, C-05 | None | `ReverseExchangeCommand` (LLD §22) | All | P2 | G-06, G-16, G-19 | Missing | Code-verified | AC-20, AC-21 |
| G-33 | Replacement items not refundable via Sales Refund | D-18 | The refund command accepts only `SaleItem` IDs of a sale | No change; add a regression test; FR-EX-164 path | Tests | P2 | G-01 | Resolved by Decision | Code-verified | AC-35 |
| G-34 | Loyalty runtime | EX-13, D-05 | TF-21 | Facts + `loyalty_status`; EXL-01 later | — | — | Loyalty module | Deferred / Out of Scope | Code-verified | AC-22, AC-23 (EXL-01), AC-44 |
| G-35 | Online-only client | EX-28 | TF-24 | `ConnectivityService`; disable entry and Confirm | FE | P1 | — | Missing | Code-verified | AC-17 |
| G-36 | Entry points (POS, Sales History/Details) | EX-19 | None | Buttons + route | FE | P1 | G-35 | Missing | Code-verified | — |
| G-37 | Preview → Review → Confirm; atomic and idempotent | EX-29, C-04 | Patterns in CreateSale/Refund | Preview query, Confirm command, preview hash | App, API, FE | P1 | G-01…G-23 | Missing | Code-verified | AC-18, AC-40 |
| G-38 | Reason codes | EX-17 | Refund reason is free text | Enum + Other text + internal note | Domain, App, FE | P1 | G-01 | Missing | Code-verified | AC-25 |
| G-39 | Phone verification classification and high-value threshold (`max(return credit, replacement value) ≥ threshold`) | EX-18, C-10, OC-05, OC-06 | `Customer.Phone` (TF-27) | Policy-driven checks (LLD §10.1, §10.1.1) | App | P1 | G-02, VI-06, VI-16 | Missing (OC-05/06 Resolved by Decision) | Doc-only | AC-31, AC-32, AC-50, AC-51 |
| G-40 | Authorization test coverage | C-08 | Existing tenancy tests (pattern) | Test suite (LLD §26) | Tests | P1 | All | Missing | — | AC-26, AC-41, AC-42, AC-45 |
| G-41 | Outbox events in transaction | C-04, D-05 | TF-20 | Append `pos.exchange.completed`/`reversed` | App | P2 | VI-11 | Verification Required | Pending VI-11 | — |
| G-42 | Missing inventory row at receiving outlet | EX-06 | Refund silently skips (TF-05 area) | Existing creation path or block | Infra | P1 | VI-10 | Verification Required | Pending VI-10 | AC-08 |
| G-43 | Global lock order / deadlock freedom | C-03, C-04 | TF-02 | Lock order (LLD §18) + race tests | App, Infra | P0 | VI-12 | Verification Required | Pending VI-12 | AC-19 |
| G-44 | Cross-module documentation consistency | — | §7 | Pointers added (this update) | Docs | — | — | Resolved by Decision | Doc-only | — |
| G-45 | Trustworthy phone-verification capability for registered customers | OC-06, EX-18 | TF-27; Stage 0 §9: no OTP or verified flag, phone editable, no sale snapshot | Honest result labels; the `required` + registered path depends on **DN-01**. No claim of verified identity. | App | P1 | VI-16, DN-01 | Verification Required (blocked for `required` + registered) | Code-verified (Stage 0) | AC-51 |
| G-48 | Cash-movement CHECK constraints and expected-cash calculator cases for Exchange cash | D-02, EX-15 | Stage 0 §11 | Migration extending two CHECKs; explicit calculator cases | Infra, App, migration | P1 | TB-01 | Missing | Code-verified (Stage 0) | AC-04 |
| G-49 | Period row lock for Exchange postings | D-15, C-07 | Stage 0 §5 (VI-18) | Period FOR SHARE + typed pre-validation | Infra, App | P1 | T-02 | Missing | Pending T-02 | AC-38 |
| G-50 | Mobile report netting of the Exchange Sales Return journal | EX-20 | Stage 0 §14 (VI-19) | Extend `SalesReportReadStore` with one method | Infra | P2 | G-29 | Missing | Code-verified (Stage 0) | AC-01 |
| G-46 | Legally mandatory remedy for replacement items | D-18, OC-04 | Only reversal + existing refund are available (LLD §22.1) | Document the escalation; verify that the two-step path reconciles; record the gap when blocked; **no new subsystem** | Docs, tests | P2 | G-32 | Verification Required (subsystem Deferred / Out of Scope) | Doc-only | AC-49 |
| G-47 | Time-zone-aware eligibility dates (purchase Day 0, local confirmation date, replacement anchor) | OC-07, EX-03, EX-04 | TF-28 (zone provider and conversion exist); v2.0 design used business dates | `eligibility_local_date`, `time_zone_id`, evaluator (LLD §7.1) | App, Domain | P1 | VI-17 | Missing (OC-07 Resolved by Decision) | Pending VI-17 | AC-52 |

## 6. Outdated assumptions in Exchange LLD v1.0 (resolved in LLD v2.0)
| # | v1.0 statement | Repository fact | Resolution |
|---|---|---|---|
| L1 | Partial refund marks the whole sale `refunded` | TF-01 | Removed |
| L2 | `OriginalSaleId` links foreign originals | Business-checked + row-locked (`CreateSaleCommand.cs:522-547`) | Deprecated for new sales (D-01) |
| L3 | `BusinessDate` type mismatch | TF-09 | Removed |
| L4 | Business date per outlet time zone | Company time zone since 5 Nov | LLD uses the session business date |
| L5 | UUID keys | `bigint` | LLD v2.0 |
| L6 | Payment-provider states / PendingSettlement | TF-07 | Removed (D-09) |
| L7 | Loyalty port call | TF-21 | Facts only (D-05) |
| L8 | Salesperson targets exist | TF-22 | Facts + outlet target (D-06) |
| L9 | Approval kernel / nonce | HR-only kernel; no step-up | Supervisor credential approval (C-02) |
| L10 | Receipt snapshot | TF-19 | New Exchange snapshot |
| L11 | Receiving-outlet stock | TF-05 | Exchange-specific location writes |
| L12 | Physical cash source | TF-08 | Expected cash (D-02) |
| L13 | Invoice search | TF-11 | Exact lookup (D-10) |
| L14 | Versioned settings exist | Counter only | New version table |
| L15 | Customer identity open | TF-10 | D-03 |
| L16 | Generic posting port / deferred posting | TF-14 | Synchronous journal (C-04) |
| L17 | `SalePaymentPlanner` handles refunds | Cannot express a negative payable | Exchange settlement planner |
| L18 | Cash GL vs drawer mismatch open (progress record D07/L611) | Allocated sales enforce the cash share | Exchange uses its own evidence (D-09) |

## 7. Conflict resolution report (documentation)
| # | Previous contradictory statement | Location | Approved decision | Change made | Remaining uncertainty |
|---|---|---|---|---|---|
| 1 | "record proof type and verification actor"; `proof_type` columns | SRS v1.0 FR-EX-003; LLD v1.0 §3.1, §5.1, §5.3 | D-13 | Removed; scan/enter + validate | None |
| 2 | High-value permission "not frozen; introduce explicitly"; plan v1.0 recommended `pos.exchange.approve_high_value` | LLD v1.0 §15; SRS v1.0 FR-EX-035; Plan v1.0 D-04/D-17; Gap v1.1 D-04 | C-01, C-10 | Mapped to `pos.exchange.override_window`; sixth key removed everywhere | None |
| 3 | Override by the logged-in user's own permission | Plan v1.0 D-04; Gap v1.1 D-04 | D-04 (B), C-02 | Supervisor credential approval | OC-01 (outlet scope), OC-02 (chain) |
| 4 | Alternative `pos.exchange.view` | Plan v1.0 D-11; Gap v1.1 D-11 | D-11 | Removed; `sales.view` | None |
| 5 | `PendingSettlement`, provider Pending/Unknown, 202 | LLD v1.0 §3.3, §3.5, §6, §9, §12.2; SRS v1.0 FR-EX-017, AC-28 | D-09 | Removed; manual evidence; AC-28 rewritten | None |
| 6 | Deferred (outbox) journal posting for Exchange | LLD v1.0 §10.1, §12.2; Gap v1.1 §4.5; Plan v1.0 §3, §5 | C-04, C-06, D-15 | Synchronous journal inside the Exchange transaction | VI-01, VI-02, VI-11 |
| 7 | "Physically available cash" requires an unspecified mechanism | SRS v1.0 EX-31/FR-EX-018; LLD v1.0 §9 | D-02 | Expected drawer cash under lock | VI-12 |
| 8 | Loyalty OTP retained; loyalty port at commit | SRS v1.0 EX-18; LLD v1.0 §11, §12.2 | D-05 | Deferred to EXL-01; no loyalty call | None |
| 9 | Customer provides an invoice number without proof → system blocks | SRS v1.0 AC-14 | D-13 | AC-14 rewritten (no phone-based lookup) | Receipt presence is an operating procedure |
| 10 | Independent "durable line-level consumption ledger" | LLD v1.0 §1.2(4), §3.3, §7.1 | C-03 | Shared `sale_items.returned_quantity`; separate counter only for replacement lines | None |
| 11 | Seeder may add missing permissions to existing roles | Plan v1.0 D-17 note | D-17 | Templates for new businesses only; `DbSeeder.cs:759-768` pattern forbidden for Exchange | None |
| 12 | "Create missing inventory row" (proposal) | Gap v1.1 §4.6 | EX-06 (no invention) | VI-10: existing path or block | VI-10 |
| 13 | Roadmap: `POST /api/sales/{id}/exchange`, store credit, restocking fee | `Docs/00_Overview/IMPLEMENTATION_ROADMAP.md` §5.5 | EX-01–EX-31 | Superseded note added | None |
| 14 | "No Returns/Exchange SRD exists" | `Docs/Owner_Dashboards/IMPLEMENTATION_GAP_ANALYSIS.md` D-01b/B-03; `.claude/autonomous/BLOCKERS.md` B-03 | D-12 | Updated: the SRS exists; B-03 stays open until reconciliation | None |
| 15 | "Exchange = replacement Sale with original-sale link" | `Docs/10_Accounts_And_Finance/ACCOUNTING_OPERATIONAL_CONTROLS_PERMISSIONS_AND_AUDIT_GAP_ASSESSMENT.md` row Exchange | D-01 | Labelled legacy; pointer added | None |
| 16 | "Returns/exchanges have a separate SRD" | POS Master SRS v1.2 §2 (line 46) | — | Pointer to the Exchange SRS added; standalone Returns policy (deferred payout, partial-refund cash policy) unchanged | None |
| 17 | "Return / Exchange" with no permission keys | `Docs/03_User_Management/POS_v2_Role_Definition.md` | EX-16, D-17 | Pointer added | None |
| 18 | RET-006 "Exchange links original and replacement **sale**" | `Docs/Unit_Of_Measurement/01_UOM_SRS.md:198` | EX-12, D-01 | **Not edited** (another module's approved SRS). Intent (link, no duplicate revenue/COGS reversal) is met by Exchange lineage. | Low: wording only |
| 19 | Historical gap rows (FR-SALE-01 partial refund; N-1 link authorization) | `POS_MASTER_BLUEPRINT_GAP_ANALYSIS.md` | — | **Not edited** (historical record, superseded by the progress record and this document) | None |
| 20 | Searches for login switching, QR verification, automatic policy activation, historical migration, current-cost fallback, cascading reversal, offline exchange, direct gateway integration, Exchange-specific COA, original-date posting, closing B-03 early | All `Docs/` | Various | **No other occurrences found** outside the items above | None |
| 21 | Supervisor = same business + permission only; outlet scope "open" | SRS v2.0 FR-EX-021, §26.3 OC-01; LLD v2.0 §11 step 4 | OC-01 | Receiving-outlet access added (FR-EX-027; LLD §11 step 6) | None |
| 22 | Refund checked only on the cashier ("no supervisor route") | SRS v2.0 FR-EX-111, OC-03; LLD v2.0 §12 | OC-03 | Case A/B (FR-EX-116; LLD §11, §12, §16) | None |
| 23 | Window = difference of business dates; replacement window from the Exchange business date | SRS v2.0 FR-EX-033, FR-EX-100; LLD v2.0 §7 | OC-07 | Local calendar Day-0 rule; eligibility date ≠ session business date (FR-EX-036; LLD §7.1) | VI-17 |
| 24 | High-value basis open; phone result "verified"; legal-remedy path pending confirmation | SRS v2.0 §26.3 OC-04/05/06; LLD v2.0 §3.3 | OC-04, OC-05, OC-06 | FR-EX-029, FR-EX-037/038, FR-EX-164/165; LLD §10.1, §10.1.1, §22.1 | VI-16 |

## 8. Technical blockers (separate from business decisions)
| ID | Blocker | Blocks | Exit |
|---|---|---|---|
| TB-01 | Migrations need explicit approval and a Test-DB rehearsal (`CLAUDE.md` hard gate) | Stage 2+ | Approval |
| TB-02 | The C-09 data audit (VI-05) needs approved read access to a representative database | Stage 0 exit; release | Approval + audit report |
| TB-03 | Synchronous journal feasibility (VI-01, VI-02, VI-11) | Stage 3 | Stage 0 proof (spike test on the Test DB) |
| TB-04 | Lock-order safety incl. drawer lock (VI-12) | Stage 3 | Stage 0 analysis; Stage 5 race tests |
| TB-05 | ~~Owner clarifications OC-01…OC-07~~ | — | **Resolved 2026-11-07** (all approved). This closes only the decision gap; the implementation gaps G-04, G-39, G-45, G-46, G-47 remain open. |
| TB-06 | Role template seeding (D-17) needs separate seeding approval | Stage 5 / UAT | Approval |
| TB-07 | Phone-verification capability (VI-16) and the purchase-instant source (VI-17) | Phone `required` mode for registered customers (DN-01); window activation (Q8) | **Open.** Stage 0: VI-17 resolved statically (normalised `SaleDate`) pending Q8; VI-16 needs **DN-01** |
| TB-08 | Test infrastructure: DB-backed tests T-01…T-05 need the isolated PostgreSQL (`RETAILPOS_TEST_POSTGRES`; guards refuse port 5435 and `retailpos_db*`) and approval to add and run the test code | Stage 3 exit (TB-03, TB-04, VI-18, VI-10, VI-07) | Approval + configured isolated server |

**Stage 0 status of the existing blockers:**
- TB-01: open.
- TB-02: open (plan in Stage 0 §18).
- TB-03: open; feasible by static evidence; needs T-01.
- TB-04: open; order defined; needs T-03.
- TB-05: resolved.
- TB-06: open.

## 9. Decision status
- **EX-01–EX-31, D-01–D-19, C-01–C-10, OC-01–OC-07 (67):** approved, documented and traced (Implementation Plan Appendix A).
- **Business decisions or clarifications unresolved:** none.
- **Technical items unresolved:** see §4.1 (statuses) and TB-01…TB-04, TB-06, TB-07, TB-08. No blocker is closed by Stage 0 without its exit evidence.
- **New business decision raised by Stage 0:** DN-01 (§4.2). It concerns a capability gap, not a reopening of OC-06.
