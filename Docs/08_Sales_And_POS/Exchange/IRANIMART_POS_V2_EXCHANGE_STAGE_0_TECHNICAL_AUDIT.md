# IraniMart POS v2 — Product Exchange: Stage 0 Technical Audit

**Document ID:** IM-POS-EX-S0-001
**Version:** 1.0
**Date:** 2026-11-07
**Audit baseline:** branch `staging`, HEAD `3aa5f986`, working tree with pre-existing user changes (`src/RetailPOS.Infrastructure/Data/RetailPOSDbContextFactory.cs`, `src/RetailPOS.Infrastructure/RetailPOS.Infrastructure.csproj`), preserved.
**Inputs:** SRS v2.1, LLD v2.1, Gap Analysis v2.1, Implementation Plan v2.1 (same folder); 67 approved decisions (EX-01–31, D-01–19, C-01–10, OC-01–07).

**Verdict:** **CONDITIONAL PASS** (§20).

Notation:
- Line numbers are from the audit baseline.
- **[INF]** marks inference from code and documented engine semantics, not observed at runtime.
- Nothing here is proven by a database test. Every database-dependent item is listed in §17.

---

## 1. Executive summary
- **The architecture supports the approved design.** All relevant writers share one scoped `RetailPOSDbContext`, and no execution strategy prevents user transactions. The journal engine, the journal-number allocator, the cash-movement recorder and the outbox append **join an ambient transaction**. Posting the Exchange journal synchronously inside the Exchange transaction (C-04) is therefore **feasible by static evidence**. It still needs a database spike (T-01) to close TB-03.
- **Design corrections found (applied to LLD v2.2):**
  1. **Outbox method:** use `AppendSourceChangeAsync`, not `AppendAsync`, because `AppendAsync` requires an atomic audit scope that cannot coexist with the Exchange's own transaction.
  2. **Lock order:** align with checkout (product variants → sales → session → inventory sorted by **id** in one statement → pack state → ...).
  3. **Accounting period race:** neither posting nor period close/lock locks the period row. The Exchange will take `FOR SHARE` on the period row before posting (this protects only the Exchange; the existing flows keep the race).
  4. **Cash movement types:** the new types need a migration that extends two existing CHECK constraints, and the expected-cash calculator ignores unknown types silently.
  5. **Purchase date:** use the company-calendar date of `Sale.SaleDate`, not `SaleDateTime`. For backdate-permitted cashiers `SaleDate` is the chosen local day; `SaleDateTime` is the entry instant.
  6. **Mobile dashboard:** it automatically nets posted "Sales return" journal debits, so it must be extended or the return leg would be counted with no replacement revenue.
  7. **Receipt:** the sale receipt renderer is sale-specific; a separate Exchange renderer is needed.
- **One new business-decision blocker (DN-01), limited in scope:**
  - Customers have no verified phone and no OTP capability. Sales keep no phone snapshot, and customer phones can be edited later.
  - So `PhoneVerificationMode = required` for **registered** customers can't be met by a trustworthy mechanism. Under this task's instruction, phone matching must not silently stand in for required verification.
  - Walk-in skip (OC-06) and `optional` mode are unaffected.
- **Confirmed safety defect for Exchange (D-14):** `VoidSaleCommand.EnsureVoidable` checks status only (`VoidSaleCommand.cs:158-171`). A sale whose quantity is consumed by an Exchange stays `completed` and could be voided, restocking everything a second time. The void guard must ship before the first Exchange commit.
- **Pre-existing risks outside Exchange scope (reported, not fixed):**
  - inventory rows are locked in request or line order by refund, void, transfer, adjustment and GRN (deadlock-prone among themselves);
  - refunds silently skip restocking when the inventory row is missing, while the journal still debits Inventory;
  - repack execute and reverse use opposite lock orders;
  - the sale receipt applies `SpecifyKind(Utc)` without normalising under the legacy-timestamp switch;
  - the provisioned default `VatPayableAccountId` is seeded from the supplier-payable code (`AccountingConfigurationProvisioner.cs:74`); finance should confirm.
- **Baseline checks run (local, no database):** unit tests **1,054 passed / 0 failed**; full solution build **succeeded, 0 errors** (58 pre-existing warnings). See §2.

## 2. Audit scope and safety confirmation
| Item | Status |
|---|---|
| Application source, migrations, configuration | **Read only.** No edits. |
| Databases (Dev/Test/Prod) | **Not accessed.** No SQL executed. No integration tests run. |
| Builds and tests run | `dotnet test tests/RetailPOS.UnitTests -c Release` → **Passed 1054, Failed 0** (Release; the project references only Domain + Application, so it has no database). `dotnet build RetailPOS.slnx -c Release` → **Build succeeded, 0 errors, 58 warnings** (pre-existing). Output went only to the local `bin/obj` folders. |
| Documentation changed | This report (new); LLD v2.2; Gap Analysis v2.2; Implementation Plan v2.2 (Exchange folder only) |
| Git | Nothing staged, committed, reset or stashed. Pre-existing user changes preserved. |

## 3. Repository and architecture findings
| Fact | Evidence |
|---|---|
| `RetailPOSDbContext` is scoped, with no `EnableRetryOnFailure` or execution strategy | `API/Program.cs:63-66`; no `EnableRetryOnFailure` in src |
| Audit interceptor, `IAuditContext` and `IAuditService` are scoped | `Program.cs:37-39,65` |
| Journal, outbox, cash-recorder and frozen-journal services are scoped | `Program.cs:380,631,638,642,948,1075`; `JournalNumberAllocatorRegistration.cs:10` |
| MediatR with default registration; a nested `ISender` resolves from the same scope **[INF]**; the D10 worker relies on this | `Application/DependencyInjection.cs:26-30`; `SaleFinancialJournalPoster.cs:205-209` |
| Transaction-begin styles differ: `SaleCreationStore`/`SaleVoidStore` begin unconditionally; `CashSessionStore`/`SaleFinancialPostingStore` are no-ops inside an ambient transaction | `SaleCreationStore.cs:16-17`; `SaleVoidStore.cs:11-12`; `CashSessionStore.cs:17-23`; `SaleFinancialPostingStore.cs:12-18` |
| No dashboard projection tables; dashboards read live tables; the only integration consumer is `OperationalLocationProjectionConsumer` | `RetailPOSDbContext.cs:159`; `Program.cs:299-300` |
| `DurableDelivery:Enabled = false` in Development, Staging and Production; the dispatcher is registered only when enabled | `appsettings.*.json`; `Program.cs:644-663` |
| `Npgsql.EnableLegacyTimestampBehavior = true` (timestamptz can be read as `Kind=Local`) | `Program.cs:32`; `Domain/Entities/UtcTime.cs:3-15` |

## 4. Transaction boundary analysis (TB-03)

### 4.1 Component behaviour inside an outer transaction
| Component | Joins ambient tx? | Own SaveChanges? | Own commit? | Evidence |
|---|---|---|---|---|
| `ManualJournalDraftStore.CreateOrGetSystemDraftAsync` | Yes (never begins one) | **Yes** (:105) | No | `ManualJournalDraftStore.cs:37-144` |
| `ManualJournalDraftStore.PostInternalAsync` | Yes (`ownsTransaction = CurrentTransaction is null`) | **Yes** (:265) | Only if it owns the tx | `:166-314`, `:173-180`, `:266-267` |
| `JournalNumberAllocator` | **Requires** a tx | Raw SQL bound to the tx | No | `JournalNumberAllocator.cs:40-81` |
| `AuditService.RecordAsync` (non-atomic) | Yes | **Yes** (:112) | No | `AuditService.cs:80-112` |
| `AuditService.RecordFailureAsync` | **No**: new DI scope and new DbContext | Yes | **Yes, independent** | `AuditService.cs:293-306` (used by `ManualJournalDraftStore.RecordFailureAuditAsync` :1335-1355) |
| `ResolvePostingAccountQuery` → `AccountingConfigurationProvisioner` | Yes | **Yes**, on every resolution (:137) plus conditional saves | No | `AccountingConfigurationProvisioner.cs:37,90,137,170,189,204` |
| `CashSessionMovementRecorder` | Yes (requires a tx for its `FOR SHARE`) | **Yes** (:76, :101) | No | `CashSessionMovementRecorder.cs:30-101`; `CashSessionStore.cs:93-99` |
| `IntegrationOutbox.AppendSourceChangeAsync` | **Requires** a tx; business must match | No (Add only) | No | `IntegrationOutbox.cs:19-27,38-48` |
| `IntegrationOutbox.AppendAsync` | Requires a tx **and** `audit.RequiresAtomicPersistence` | No | No | `IntegrationOutbox.cs:12-17,68-73` |
| `RequiredAuditTransaction.ExecuteAsync` | **Refuses** an existing tx or scope; must be the outermost | Yes | **Yes** | `RequiredAuditTransaction.cs:45-77` |
| `SaleCashSessionValidator.ValidateActiveForSaleAsync` | Requires a tx (FOR SHARE) | Only on overdue (audit) | No | `SaleCashSessionValidator.cs:25-50,90`; fire-and-forget publishers at :74-79 (non-DB **[INF]**) |

### 4.2 Proposed Exchange call flow (all inside one transaction)
```text
ConfirmExchangeCommand
 └─ IExchangeStore.BeginTransactionAsync      (no-op if ambient; CashSessionStore style)
    ├─ locks (§7)                               SELECT … FOR SHARE/UPDATE
    ├─ inserts: header, lines, settlements, approvals consumed, salesperson facts, receipt snapshot
    ├─ UPDATE sale_items.returned_quantity / exchange_replacement_lines.returned_quantity
    ├─ inventories (+/−), stock_ledgers, pack state            (shared DbContext)
    ├─ CashSessionMovementRecorder (exchange_cash_in/out)      (SaveChanges, inside tx)
    ├─ ResolvePostingAccountQuery ×n                           (SaveChanges, inside tx)
    ├─ ISender: CreateOrGetSystemJournalDraftCommand           (SaveChanges, inside tx)
    ├─ ISender: PostJournalEntryCommand                        (joins tx; period check; number)
    ├─ IIntegrationOutbox.AppendSourceChangeAsync              (Add only)
    ├─ SaveChanges
    └─ Commit                     on any exception: Rollback + ChangeTracker.Clear()
```
All ten authoritative write groups (header and lines; returned quantity; stock; cash; settlement; journal; salesperson facts; approval consumption; idempotency; outbox) can be persisted in this one transaction. This is the same pattern the D10 worker uses today (`SaleFinancialPostingCommands.cs:57-139`).

### 4.3 Writes outside the transaction / risks
1. `AuditService.RecordFailureAsync` commits a **failure audit** independently. It is not an authoritative business write; it is acceptable and must be documented.
2. Early `SaveChanges` calls by the journal store, provisioner, audit service and recorder flush the caller's tracked entities before commit. They stay inside the transaction. On failure the handler must `Rollback` **and** `ChangeTracker.Clear()` (D10 precedent `SaleFinancialPostingStore.cs:119`).
3. The journal store's draft-reuse path catches `DbUpdateException` and re-queries (`ManualJournalDraftStore.cs:107-126`). Inside an outer PostgreSQL transaction this depends on EF Core's savepoint around `SaveChanges` **[INF]**. A unique source key per Exchange makes the path unreachable in normal flow. **Covered by T-01.**
4. `RequiredAuditTransaction` must **not** be used. It refuses to nest, and its module check (`AuditService.cs:86-87`) would reject the journal store's `Accounts`-module audits, which the store swallows (losing journal audit evidence).
5. The journal engine throws `InvalidOperationException` with message text for period, account and other failures (`ManualJournalDraftStore.cs:1085-1105`; `ResolvePostingAccountQuery.cs:71-107`). Mapping by message is fragile. **Proposed:** pre-validate the period and account mappings under lock with the existing read paths, to produce typed Exchange error codes. The engine guard stays authoritative.

**Conclusion:** feasible by static inspection. **TB-03 remains open** until T-01 (§17) proves:
- commit and rollback together across all write groups;
- no early commit;
- savepoint behaviour on the draft-reuse path.

## 5. Accounting journal and period-date analysis (VI-01, VI-02, VI-11, D-15, C-07)
- **Period check:** `ValidateOpenPostingPeriodAsync` checks `UtcCalendarDate(journal.PostingDate)`, and also `BusinessDate` when they differ (`ManualJournalDraftStore.cs:995-1016`). `UtcCalendarDate` converts `Local` to UTC and treats any other kind as UTC (:1111-1114).
- **Sale journals** post on `TransactionAtUtc = SaleDateTime ?? SaleDate` (`SaleFinancialSnapshots.cs:160`). The period is checked on the **UTC** day of the instant (existing D10 behaviour, not changed).
- **Reversal journals** post on `AccountingDate` = company-zone date of the server time, as a UTC-kind midnight (`SaleFinancialJournalPoster.cs:107-110`; `SaleFinancialSnapshots.cs:265`). `UtcCalendarDate` then returns exactly the company-zone accounting date.
- **Exchange design (verified):** set `PostingDate = BusinessDate = exchanges.business_date` (the cash-session business date) as a UTC-kind midnight, exactly like reversals. Then the period check evaluates the correct Exchange accounting date.
- **Eligibility date vs accounting date (OC-07):** eligibility uses the local confirmation date (§10). Accounting uses the session business date. They are separate fields (`eligibility_local_date` vs `business_date`); there is no confusion in the design.
- **Backdated sales:** irrelevant to Exchange posting, which is always dated by the Exchange session. For eligibility see §10.
- **Exchange reversal posting:** the same rule with the reversal's session business date.
- **Concurrency gap (new VI-18):**
  - Period reads in the engine are plain queries with no lock and no concurrency token (`ManualJournalDraftStore.cs:1078-1096`; no xmin on `AccountingPeriod`).
  - Status changes take no lock: `AccountingPeriodCommandStore.UpdateStatusAsync` (:171-240); close-run prepare and execute (`AccountingPeriodCloseRunCommandStore.cs:25-82,386-491`).
  - So a posting can commit into a period that a concurrent transaction locked or closed.
  - **Mitigation for Exchange (Proposed):** inside the Exchange transaction, `SELECT … FROM accounting_periods WHERE id = @p FOR SHARE` (and the financial year) before posting, then re-validate the status. A concurrent `UPDATE` of the status then waits for the Exchange to commit; if it committed first, the Exchange reads `Locked`/`Closed` and aborts.
  - This reuses the existing period data and adds no subsystem (D-15, C-07). The race for other posting flows is pre-existing and out of scope.
  - Verify with T-02.
- **Payment slots and accounts:** see §11.
- **Audit scope (VI-11):** use `AppendSourceChangeAsync` (the same as `CashSessionEventWriter.cs:43`). This needs no atomic audit scope. Verified statically.

## 6. Shared refund/exchange quantity analysis (C-03)
- **Single writer today:** `RefundSaleCommand.cs:237-238` updates `ReturnedQuantity` and status together (:239). The CHECK `ck_sale_items_returned_quantity` enforces `0 ≤ returned ≤ quantity` (`UomModelConfiguration.cs:234`; migration `20261107084952…:149-152`). The one-off backfill came from refund posting snapshots (:136-147).
- **Validation pattern to reuse:** `ResolveAssessedLines` (:327-371):
  - `SaleItemId` is required when a variant repeats;
  - legacy (non-UOM) lines must be whole (`EnsureLegacyWholeQuantity`, `UomCore.cs:266`);
  - UOM lines are limited to scale 6;
  - requests are grouped per sale item and checked against `Quantity − ReturnedQuantity`;
  - quantities are base units.
- **Locking:** refund locks `sales` `FOR UPDATE` first (`SaleVoidStore.cs:55-61`). An Exchange that locks the same sale rows first serialises with refunds on the same sale. There is no cycle on the sale edge.
- **Remaining returnable (read side):** `SaleReadStore.cs:64` returns 0 when the sale is refunded or voided. The Exchange lookup must use the same rule.
- **Void (D-14):** confirmed gap. `EnsureVoidable` (`VoidSaleCommand.cs:158-171`) and the conditional update (`SaleVoidStore.cs:38-53`) check status only. The void restocks full `item.Quantity` (:69-79). **Required:** reject when any `ReturnedQuantity > 0`. With the current refund code, a sale with returned quantity always has status `partially_refunded`/`refunded`, so the new guard changes no current outcome **[INF; confirm with Q5 of §18]**.
- **Void lock gap:** void reads the sale **without** a lock (:35) and relies on the conditional status UPDATE. An Exchange holding `sales FOR UPDATE` blocks that UPDATE until commit. After commit, the new void guard re-reads `ReturnedQuantity`. The guard must be evaluated **after** the conditional update (or with the sale locked) to avoid a time-of-check gap. *Design note in LLD §6.*
- **Exchange reversal:** decrement `ReturnedQuantity` under the same sale lock. The CHECK prevents a negative value.
- **UOM risks:**
  - Use base units (`UomQuantityMath.StorageScale = 6`, never rounded).
  - Legacy lines are whole only.
  - Return cost and value use the cumulative `ShareOfReturn`, so mixed refund and exchange sequences stay exact.
  - Pack state on return reuses `SaleUomReversal` logic, parameterised by location.

## 7. Lock ordering and deadlock risks (TB-04, VI-12)

### 7.1 Existing orders (evidence)
| Workflow | Order |
|---|---|
| Checkout | product_variants FOR SHARE sorted (:138) → [legacy original sale FOR UPDATE] → cash_sessions FOR SHARE (:280) → invoice counter upsert (:285) → `SaveChanges` updates inventories (xmin) (:352; EF orders the UPDATEs by PK **[INF]**) → pack state (:399-405) → ledger/posting → final gate |
| Refund | sales FOR UPDATE (:75) → insert refund → variants FOR SHARE (:130) → inventory FOR UPDATE **in request order** (:145) → pack state / packaging → posting → session FOR SHARE + advisory(sourceKey) (:235) |
| Void | sale read without a lock → session FOR SHARE + advisory → conditional sales UPDATE → inventory FOR UPDATE in line order (:71) → **then** variants FOR SHARE (:85) |
| Transfer dispatch / receive, adjustment, GRN | inventory updates in line order, interleaved with variants FOR SHARE and pack state |
| UOM config save | product_variants FOR UPDATE → inventories of the variant ORDER BY id FOR UPDATE (`UomStores.cs:234-255`) |
| Repack | execute: components sorted, source last; reverse: source first (opposite) |
| Cash close | session FOR UPDATE only; waits on nothing else |

### 7.2 Exchange order (adopted; consistent with checkout)
1. idempotency lookup (unique index);
2. `product_variants` FOR SHARE, **sorted**, covering every return, replacement and packaging variant;
3. source `sales` FOR UPDATE, ascending ID;
4. `exchanges` (reversal) and source `exchange_replacement_lines` FOR UPDATE, ascending ID;
5. `cash_sessions` FOR SHARE (validator);
6. **all** receiving-outlet `inventories` in **one** statement, `WHERE variant_id = ANY(@v) AND location… ORDER BY id FOR UPDATE` (the `UomStores.cs:236` pattern; matches EF's PK update order); insert missing rows afterwards (23505 → 409);
7. `pack_state_balances` (`PackStateLedger`: stock row first, then balances);
8. `exchange_approvals` FOR UPDATE;
9. `accounting_periods` (+ financial year) FOR SHARE (§5);
10. `exchange_number_counters` upsert (Exchange-only table, so no cross-workflow edge);
11. journal engine (journal row FOR UPDATE, journal number allocator);
12. cash movement: session FOR SHARE (already held) + `pg_advisory_xact_lock` on the source key + **drawer key** (cash-out), always last.

### 7.3 Deadlock analysis
| Cycle | With | Status |
|---|---|---|
| Sale-row edge | refund (sale first) | **None**: both lock sales first |
| Session edge | close (FOR UPDATE), refund/void (SHARE) | **None**: close waits on nothing else; SHARE locks are compatible **[INF]** |
| Counter edge | checkout's `sale_invoice_counters` | **None**: the Exchange uses its own counter table |
| Variants edge | UOM config save | **None** with step 2 (variants before inventory). Void has this cycle today (pre-existing). |
| Inventory edge | refund (request order), void (line order), transfer, adjustment, GRN | **Residual, pre-existing class.** Any two of these flows can already deadlock on two shared variants. The Exchange's id-sorted single-statement lock is consistent with checkout but cannot remove cycles caused by other flows. PostgreSQL detects the deadlock and aborts one transaction; the Exchange maps it to `409 EXCHANGE_CONCURRENCY_CONFLICT` (retry). |
| Drawer edge | refund / drawer expense payouts | **None** if the drawer advisory key is always taken last (also inside `CashSessionMovementRecorder` for negative movements) |
| Period edge | period status change / close run | **None**: those transactions update only period and close-run rows and wait on nothing the Exchange holds **[INF]** |

**TB-04 stays open** until T-03 (race matrix) passes.

## 8. Historical data compatibility findings (TB-02, C-09)
| Fact | Evidence | Recoverable from immutable records? |
|---|---|---|
| `sale_items.taxable_amount`, `vat_amount`, `vat_rate`, `tax_code`, `tax_inclusive`, `historical_unit_cost` exist since InitialBaseline (2026-10-04), all **nullable** | `20261004095137_InitialBaseline.cs:2857-2862` | Only where populated |
| UOM snapshot columns, `packaging_cost`, `sale_return_lines` since `PosV2UomFoundation` (2026-11-06); older lines have NULL UOM codes and are treated as legacy whole units | `20261106234558…:296-415,762-786` | Yes (NULL = legacy whole unit) |
| `returned_quantity` added 2026-11-07, backfilled from refund posting snapshots | `20261107084952…:49-53,136-147` | Yes, if snapshots were complete (Q5) |
| Invoice-discount share is folded into `taxable_amount` | `SaleCheckoutCalculator.cs:161-162` | Yes, where the allocation exists |
| Sale stock-ledger rows store `unit_cost = HistoricalUnitCost ?? 0` and are unreliable when 0 | `SaleCreationAdapters.cs:140-162`; `StockLedgerWriter.cs:147-181` | **No:** when `HistoricalUnitCost` is NULL the ledger shows 0, so it is not independent evidence for v2 sales. It may help only for rows written by older code (Q3). |
| `sale_financial_postings` sale snapshot holds only an aggregate `Cost`; reversal lines carry per-line cost | `SaleFinancialSnapshots.cs:44,56-63,166` | Aggregate only, so per-line cost is not recoverable from it |
| Legacy exchange links: `original_sale_id`/`replacement_sale_id` bigint, **no FK, no index** | baseline :2439-2440; snapshot | Yes (read) |
| Customer on sale: `CustomerId` nullable; walk-in is a system customer; **no phone snapshot on sale** | `Sale.cs:25`; `SaleReadStore.cs:130` | Customer ID only; the phone at the time of purchase is **not** recoverable |

**Conclusion:**
- C-09's "provably matching stock-out/COGS evidence" path is **weak for v2 sales**: a NULL cost yields a 0-cost ledger row.
- The eligibility rule stays: block lines without `HistoricalUnitCost` or without the taxable/VAT allocation.
- The size of the affected population is unknown until the read-only audit (§18) runs.

## 9. Phone verification capability (TB-07, VI-16)
- `Customer` has `Phone`/`PhoneNormalized`, with uniqueness per business on non-null values. There is **no verified-phone flag, OTP or verification evidence** (`Customer.cs:5-22`).
- A repository search finds no OTP, SMS, password-reset or phone-verification code. The only matches are HR `VerifiedAtUtc` and an "otp" redaction key (`Audit/AuditFieldRedaction.cs:24`).
- **Customer phones can be changed later** (`UpdateCustomerCommand.cs:28-58`). **Sales store no phone snapshot**; receipts read the live customer phone (`SaleReadStore.cs:130`).
- **Walk-in:** created with no phone or email. A database trigger blocks changing its identity, phone or email (`20261013041308_RestoreAuditAndWalkInDatabaseProtections.cs:36-68`). OC-06's walk-in skip is implementable, and the synthetic record has no phone to misuse.
- **No loyalty or membership phone exists** separate from `Customer` (`Customer.LoyaltyPoints` integer only). There is no confusion between purchaser and loyalty phone today.
- **Authentication OTP:** none exists. Login is email + BCrypt password (`AuthenticationCommands.cs:46-56`). It is not reusable for customer phone verification.
- **Gap (DN-01, business decision required; not decided here):** with existing capabilities, `PhoneVerificationMode = required` for a **registered** customer can only compare the entered number with the customer's **current** recorded phone. That proves neither identity nor the phone at purchase time. Per this task's instruction, that match must not silently replace required verification. SRS FR-EX-035/038 (v2.1) currently define the match with an honest label. The owner must decide which applies (§19).
- Stage 0 introduces no OTP service.

## 10. Original purchase date findings (TB-07, VI-17)
- **Field population** (`CreateSaleCommand.cs:269-307`):
  - `now = UtcNow`; `saleDate = request.SalesDate ?? now`;
  - `SaleDateTime = now`; `SaleDate = saleDate`; `CreatedAt = now`;
  - `InvoiceIssuedAtUtc` = allocator time; `BusinessDate = session.BusinessDate`.
- **Backdate path:**
  - `SalesController.cs:402-433`: `sales.backdate` is required for past dates; future dates are rejected; a chosen date becomes company-zone midnight (`CompanyDates.StartUtc`, `CompanyDates.cs:37-40`); without permission `SalesDate = now`.
  - The **POS always sends** the date picker value (default today) as `YYYY-MM-DDT00:00:00` (`pos-screen.component.ts:604,648-660`).
  - So for backdate-permitted cashiers, `SaleDate` is **local midnight of the chosen day**, even for today's sales.
- **There is no "Late Sale" or `EntryType` concept in code** (search). The master SRS "Late Sale" maps to backdated sales.
- **Conclusion (VI-17):**
  - `SaleDateTime` is the **entry instant**, not the purchase date, for backdated sales.
  - The approved Day 0 is a **local calendar date**, and `CompanyCalendarDate(UtcTime.Normalize(Sale.SaleDate))` equals:
    - the local date of `now`, for regular sales;
    - the chosen local date, for backdated sales;
    - the stored value, for legacy rows (`SaleDate` is NOT NULL).
  - **Authoritative source: `Sale.SaleDate`**, converted to the company calendar date after `UtcTime.Normalize` (the legacy-timestamp switch can return `Kind=Local`).
  - `SaleDateTime` and `BusinessDate` must **not** be used for Day 0.
  - Confirm on data with Q8 (§18): rows where `SaleDate` is after `SaleDateTime`, or the date differs unexpectedly.
- **Replacement window anchor:** `exchanges.eligibility_local_date` (stored at confirmation). No existing field is involved.
- **Pre-existing receipt defect (out of scope):** `GenerateSaleReceiptDocumentCommand.cs:218-223` applies `SpecifyKind(Utc)` without normalising. The Exchange receipt must not copy this.

## 11. COA and payment mapping findings (C-06)
- **Slot policy:** `PaymentPostingPolicy.ResolveSlot` (`PaymentPostingPolicy.cs:7-24`):
  - cash → `cash`, bank → `bank`;
  - card → `card_clearing` and mobile/mfs → `mobile_mfs_clearing` on or after `AccountingConfiguration.BankClearingActivationDate`, otherwise `bank`;
  - unknown → `bank`.
- **Slot names:** `PostingAccountSlots` (`AccountingDtos.cs:103-132`): `sales`, `sales_return`, `sales_discount`, `vat_payable`, `cogs`, `inventory`, `cash`, `bank`, `card_clearing`, `mobile_mfs_clearing`.
- **Resolution:** `ResolvePostingAccountQuery`:
  - the business comes from the tenant scope (:39);
  - outlet overrides apply only to cash, petty cash and bank (:40-64, :122-123);
  - an **unmapped slot throws** `InvalidOperationException` (:71-73), as do inactive or wrong-type accounts (:89-107), so there is no silent fallback for unmapped slots;
  - the `_ => bank` branch is unreachable through the Exchange method whitelist.
- **Cross-business leakage:** resolution is tenant-scoped. No leakage path found.
- **Split payments:** one debit line per payment (`SaleFinancialJournalPoster.cs:34-41`); the planner checks the exact sum (`SalePaymentPlanner.cs:36-95`).
- **Duplicate settlement:** prevented by the Exchange idempotency key + journal source key + cash-movement `SourceKey` dedup (`CashSessionMovementRecorder.cs:42-46`).
- **Cash-session totals:** the calculator includes cash sales by `CashSessionId` and movements by type. **It has no default branch, so unknown movement types are silently ignored** (`CashSessionExpectedCashCalculator.cs:61-73`). It must be extended for `exchange_cash_in`/`exchange_cash_out`.
- **The `cash_session_movements` CHECK constraints** `ck_cash_session_movements_type` and `ck_cash_session_movements_effect` allow only 4 types (`20261103091638…:257-258`; `CashSessionModelConfiguration.cs:27-32`). **A migration is required** (TB-01). The close-snapshot hash is computed once at close and never re-verified (`CloseCashSessionCommand.cs:275-296`), so it doesn't break.
- **Reuse:** the journal composer, `PostingAccountSlots`, `PaymentPostingPolicy` and `IAccountingConfigurationService` live in `RetailPOS.API`. The Exchange needs an Application port implemented next to `SaleFinancialJournalPoster`, which the LLD already plans.
- **Finance attention (VI-20):** `AccountingConfigurationProvisioner.cs:74` seeds the default `VatPayableAccountId` from the supplier-payable code. Exchange VAT legs would post there unless reconfigured. This is a pre-existing configuration matter; finance must confirm before release.
- **Refund evidence:** existing refunds store no reference. The Exchange adds its own fields (D-09).

## 12. Supervisor approval feasibility (C-02, OC-01/02/03, C-10)
| Need | Existing capability | Feasible |
|---|---|---|
| Credential check | `IPasswordHasher.Verify` (BCrypt) | Yes |
| Supervisor lookup | Login matches `user.Email == email` (`AuthenticationStore.cs:30-38`); **email is globally unique** (snapshot index) | Yes. Look up by email, then require `BusinessId == scope` |
| Status checks | `IsActive`, `MustResetPassword` (`AuthenticationCommands.cs:46-56`) | Yes |
| Effective permissions | `LoginCommandHandler.BuildEffectivePermissions(rolePermissions, overrides)` (internal static; already reused by `StockCountAdjustmentApproverPolicy.cs:42-44`) | Yes |
| Outlet access for **another** user (OC-01) | **Existing pattern** `StockCountAdjustmentApproverPolicy.cs:31-61`: profile → active, not reset, same business → permissions → `new UserOutletAccessContext(id, IsSuperAdmin:false, IsBusinessOwner: role=="BusinessOwner", businessId, null)` → `IUserOutletAccess.GetAuthorizedOutletsAsync`; also `MobileStockTakeSubmissionActorPolicy.cs:44` | Yes. Reuse the pattern. |
| Rate limiting | Fixed-window **per remote IP** (`Program.cs:1154-1190`; `auth-login` 5/min) | Yes. Add a new `exchange-approval` policy (per IP + cashier); configuration only |
| No session switch | The approval endpoint returns tokens only; the JWT is untouched | Yes (design) |
| Audit | `IAuditService` records the real/acting user from the request (`AuditService.cs:120-125`); the supervisor must go in Metadata/Entities | Yes |
| One-time / payload-bound / expiry / multi-type | New `exchange_approvals` table (LLD §3.10) | Yes (new code) |

**Five permissions preserved.** High value maps to `pos.exchange.override_window`.

## 13. Exchange number and receipt integration
- **Prefix:**
  - `outlets.invoice_prefix` varchar(12), unique per business when not null;
  - regex `^[A-Z][A-Z0-9]{0,9}$` with a digit-collision rule (`OutletPosSetup.cs:71-91`);
  - locked once invoiced sales exist (`OutletPosSetupApplier.cs:102-107`).
  - Exchange numbers `{Prefix}X{yyMMdd}{seq}` cannot collide with sale numbers `{Prefix}{yyMMdd}{seq}`, because the `X` follows a prefix whose next character for sales is a digit **[INF]**.
  - Outlets with a NULL prefix → `EXCHANGE_OUTLET_PREFIX_MISSING` (Q9).
- **Counter:** a separate `exchange_number_counters` (business, outlet, business_date) mirrors `sale_invoice_counters` (snapshot 15784-15806; `SaleInvoiceNumberAllocator.cs:32-49`).
- **Receipt:**
  - `ReceiptPrintHistory.SaleId` is required with a cascading FK (`RetailPOSDbContext.cs:1458-1473`), so a **separate** `exchange_receipt_print_history` is needed (as planned).
  - `SaleReceiptPdfRenderer` is sale-specific: it takes a `SaleReceiptDocumentReadModel` and hard-codes "Invoice" (:33, :69, :253) in private static helpers. A **new `ExchangeReceiptPdfRenderer`** is needed. It reuses QuestPDF and the layout conventions without touching the sale renderer.
  - Frontend `thermal-receipt.component` hard-codes "Invoice" (html :16, :21). `receipt-print-page.ts` is document-agnostic and reusable.
- **No historical sale records are modified.**

## 14. Dashboard and outbox integration (B-03)
- **Outbox:** `AppendSourceChangeAsync` inside the Exchange transaction. The dispatcher is disabled and there are no consumers, so events are durable rows only. The tables remain the source of truth.
- **Owner dashboard:**
  - net sales = Σ(TotalAmount − Tax) of sales in the period by `BusinessDate` (fallback `SaleDate`), minus **trusted reversal postings** on `AccountingDate` (`SalesFinanceReadStore.cs:93-193`; `OwnerDashboardSales.cs:29-91`);
  - outlet target = `SalesDayFact.NetSales` (`SalesTargetPerformance.cs:39-53`);
  - Exchange facts must be **added** per (business_date, receiving outlet). They are not in `sale_financial_postings`, so there is no double count with refunds.
- **Mobile report:** revenue = Σ TotalAmount minus **posted "Sales return" journal debits by `PostedAt`** (`SalesReportReadStore.cs:13-119`). **Risk (VI-19):** the Exchange journal's Sales Return debit would be subtracted automatically while replacement revenue (not a Sale) is never added, understating revenue. The mobile store must add the Exchange replacement revenue, or exclude Exchange journals and add the signed Exchange facts.
- **Legacy linked sales:** the `UnreversedExchanges` count (`SalesFinanceReadStore.cs:200-202`) stays. New Exchanges create no sale links (D-01), so the two sets never overlap. B-03 stays open until the S5-05 reconciliation.
- **Salesperson:** no aggregation exists; the new `exchange_salesperson_adjustments` is the only source (D-06).

## 15. VI-01–VI-17 verification matrix (+ new VI-18–VI-20)
| VI | Question | Decisions | Evidence | Finding | Risk | Static enough? | DB test? | Remaining action | Status |
|---|---|---|---|---|---|---|---|---|---|
| VI-01 | Slot IDs, engine errors, synchronous post + rollback | C-04, C-06 | §4, §11 | Slots verified; errors are text-based `InvalidOperationException`; engine joins the ambient tx | High | No | Yes (T-01) | T-01; typed pre-validation | Requires Database Test |
| VI-02 | Posting-date / period day mapping | D-15 | §5 | Use the session business date as a UTC-kind midnight (reversal precedent) → correct day | Medium | Yes | Boundary test in Stage 3 | Implement + test | Verified by Static Inspection |
| VI-03 | Pre-activation card/mobile → bank rule acceptable under C-06 | C-06 | §11 | Configured rule; unmapped slots throw | Low | Yes (code) | No | Finance acknowledgement | Partially Verified |
| VI-04 | Supervisor lookup and outlet context | C-02, OC-01 | §12 | Email globally unique; approver pattern exists | Medium | Yes | Integration test (Stage 2) | Implement | Verified by Static Inspection |
| VI-05 | Historical evidence population | C-09 | §8 | Nullable fields; ledger not independent evidence | High | No | Read-only audit | §18 plan; approval | Requires Read-Only Data Audit |
| VI-06 | Walk-in identification | D-03, OC-06 | §9 | `IsSystem` + `CustomerCode = WALKIN`; trigger-protected; no phone | Low | Yes | Q7 uniqueness | — | Verified by Static Inspection |
| VI-07 | Cash movement CHECKs, close snapshot | D-02, EX-15 | §11 | Two CHECKs need a migration; calculator lacks a default; hash unaffected | High | Yes | After migration (T-04) | Migration + calculator cases | Verified by Static Inspection |
| VI-08 | PDF renderer reuse | EX-12 | §13 | Sale-specific → new renderer | Low | Yes | No | Implement | Verified by Static Inspection |
| VI-09 | Barcode ambiguity | EX-26 | barcode → variant only | UX choice of line when the variant repeats | Low | Yes | No | S1-05 UX contract | Partially Verified |
| VI-10 | Missing inventory row at receiving outlet | EX-06 | creation paths at GRN/transfer/adjustment; no 23505 handling; refund skips silently | Create a row via the existing pattern; map 23505 → 409 | Medium | Partly | Yes (T-05) | T-05 | Partially Verified |
| VI-11 | Outbox / audit scope | C-04 | §4 | `AppendSourceChangeAsync` works in a plain tx; `RequiredAuditTransaction` is unusable | Medium | Yes | Covered by T-01 | Update LLD | Verified by Static Inspection |
| VI-12 | Lock order / deadlocks | C-03, D-02 | §7 | Checkout-consistent order defined; residual pre-existing inventory-order cycles | High | No | Yes (T-03) | T-03 | Requires Database Test |
| VI-13 | Dashboard bases | EX-20 | §14 | Owner by BusinessDate + trusted reversals; mobile by TotalAmount − posted Sales Return debits | Medium | Yes | Reconciliation (Stage 5) | Design per §14 | Verified by Static Inspection |
| VI-14 | Extract CreateSale blocks safely | Reuse | inlined blocks identified (§7.1) | Extraction must preserve order and behaviour | Medium | No | Regression (existing PG suites) | Stage 2 with full regression | Partially Verified |
| VI-15 | Stock-ledger CHECKs | EX-06 | no CHECK in any migration | No migration needed for `exchange` type/reference | Low | Yes | No | — | Verified by Static Inspection |
| VI-16 | Phone verification capability | OC-06, EX-18 | §9 | No verification capability; phone mutable; no sale snapshot | High (for required mode) | Yes | No | **DN-01** owner decision | Blocked (scope: `required` mode for registered customers) |
| VI-17 | Purchase-date source | OC-07 | §10 | Company calendar date of normalised `SaleDate`; not `SaleDateTime` | Medium | Mostly | Read-only audit Q8 | Q8 | Partially Verified |
| **VI-18** (new) | Period close race with posting | D-15, C-07 | §5 | No lock on the period row in posting or status change | High | Yes | Yes (T-02) | Exchange period FOR SHARE; T-02 | Requires Database Test |
| **VI-19** (new) | Mobile report auto-nets Sales Return journals | EX-20 | §14 | The Exchange return leg would be netted without replacement revenue | Medium | Yes | Reconciliation | Extend the mobile store | Verified by Static Inspection |
| **VI-20** (new) | Default VAT payable mapping | C-06 | `AccountingConfigurationProvisioner.cs:74` | VAT payable seeded from supplier-payable code | Medium | Yes | Q10 | Finance confirmation | Partially Verified |

## 16. TB-01–TB-07 blocker matrix (+ TB-08)
| TB | Category | Blocker | Status | Exit criteria | Blocks |
|---|---|---|---|---|---|
| TB-01 | Migration authorization gate | Exchange tables + cash-movement CHECK extension need approved migrations | **Open** | Owner approval + Test-DB rehearsal | Stage 2 onward (schema) |
| TB-02 | Data verification | Read-only historical audit needs DB access approval | **Open** | §18 executed with approval; report | Release/activation per business (C-09); not coding |
| TB-03 | Code architecture | Synchronous journal inside the Exchange tx | **Open (feasible by static evidence)** | T-01 passes | Stage 3 Confirm |
| TB-04 | Code architecture / test | Lock order and deadlocks | **Open (order defined)** | T-03 passes | Stage 3 Confirm |
| TB-05 | Business decision | OC-01–OC-07 | **Resolved** (decision only) | — | — |
| TB-06 | Seeding authorization | Role template grants | **Open** | Seeding approval | Stage 5 / UAT |
| TB-07 | Business decision + data | Phone capability (VI-16) and purchase date (VI-17) | **Open.** VI-17 resolved statically pending Q8; VI-16 needs **DN-01** | DN-01 decided; Q8 clean | Phone `required` mode for registered customers only; window evaluation needs Q8 before activation |
| **TB-08** (new) | Test infrastructure | DB-backed tests need an isolated PostgreSQL via `RETAILPOS_TEST_POSTGRES` (guards refuse port 5435 and `retailpos_db*`; `InfrastructureTestSettings.cs:55-75`) and approval to run them | **Open** | Approval + configured isolated Test server | T-01…T-05; Stage 3 exit |

## 17. Proposed database-backed verification tests (not executed; need authorization)
All tests run **only** against the isolated Test PostgreSQL configured by `RETAILPOS_TEST_POSTGRES`. Each test creates a **disposable database** from the migrated template (`PostgresTestDatabase.CreateAsync`) and drops it afterwards; the guards forbid Development names and ports. Writes happen only in that throw-away database. The tests require adding test code (a code change), so they belong to Stage 2/3 under approval, or to a spike branch. None touch shared data.

| ID | Purpose | Scenario | Pass criteria |
|---|---|---|---|
| T-01 | TB-03 atomicity | In one tx: insert a dummy aggregate row, update `sale_items.returned_quantity`, inventory + ledger, a cash movement (existing type), `ResolvePostingAccountQuery`, create + post a balanced journal via the existing commands, `AppendSourceChangeAsync`; then (a) commit, (b) throw after the journal post and roll back | (a) all rows present; (b) **no** rows present, journal number not consumed, no early commit; failure audit is the only independent row |
| T-02 | VI-18 period race | Tx A: lock the period FOR SHARE and post; Tx B: `UpdateStatusAsync` to Locked concurrently (barrier) | B waits for A, or A aborts with a period error; never a journal in a Locked period |
| T-03 | VI-12 race matrix | Exchange vs refund (same sale, last unit); Exchange vs checkout (last replacement unit); Exchange vs Exchange; Exchange cash-out vs refund payout vs drawer expense; Exchange vs UOM config save; reversal vs re-exchange | One winner per contested resource; deadlocks only surface as a 409 retry; drawer never negative; CHECK never violated |
| T-04 | VI-07 cash | After the migration: Exchange movements are accepted by the CHECKs, included in expected cash, and the close snapshot totals match | Exact totals |
| T-05 | VI-10 inventory row | Two Exchanges both creating the missing receiving-outlet row for the same variant | One succeeds; the other gets 409 (23505 mapped) and succeeds on retry |

## 18. Read-only historical data audit plan (not executed; needs explicit approval)
| Item | Plan |
|---|---|
| Database | The database whose historical sales Exchange will serve: **Production, via a read replica or a restored copy** (preferred), or Development only for rehearsal. The owner chooses. |
| Access | A dedicated **read-only role** (`SELECT` only on the listed tables); `SET default_transaction_read_only = on`; `statement_timeout` 60 s; run off-peak |
| Privacy | **Aggregate counts only.** No names, phones, emails or IDs exported, except sample sale IDs on request. Results stored in the Stage 0 annex. |
| Performance | Sequential scans of `sale_items`/`sales`/`stock_ledgers` may be heavy. Run on a replica or copy, or with a per-business filter. |

**Queries (categories; exact SQL is prepared in the annex when approved):**

| Q | Purpose | Tables |
|---|---|---|
| Q1 | Sales and lines per business by status and month (population baseline) | sales, sale_items |
| Q2 | Lines missing `taxable_amount`, `vat_amount` or `historical_unit_cost` (per business and month, and still returnable) | sale_items, sales |
| Q3 | For Q2 lines missing cost: matching `stock_ledgers` rows (`reference_type='sale'`, same sale and variant) with `unit_cost > 0` and `is_cost_valuation_reliable` | stock_ledgers |
| Q4 | Allocation consistency: Σ line (taxable + vat) vs sale `TotalAmount` | sales, sale_items |
| Q5 | `returned_quantity` vs Σ `sale_return_lines.returned_quantity`; sales `completed` with `returned_quantity > 0` (void-guard impact) | sale_items, sale_return_lines, sales |
| Q6 | Legacy links: counts and statuses of `original_sale_id`/`replacement_sale_id` | sales |
| Q7 | Walk-in customers per business (expect 1); registered customers without a phone; sales by walk-in vs registered | customers, sales |
| Q8 | `SaleDate` vs `SaleDateTime`: NULL `SaleDateTime`; `SaleDate` later than `SaleDateTime`; company-date differences (backdate share) | sales |
| Q9 | Outlets without `invoice_prefix` | outlets |
| Q10 | Accounting configuration completeness per business for the 10 slots; whether VAT payable equals the supplier-payable account | accounting_configurations, chart_accounts |
| Q11 | Open accounting periods covering the current date per business | accounting_periods, financial_years |

Expected output: a counts table per query. These are inputs to the activation decision per business (C-09: affected lines are blocked, never backfilled).

## 19. SRS/LLD compatibility findings
| # | Area | Finding | Action |
|---|---|---|---|
| 1 | Outbox | LLD §20 said `AppendAsync` | **LLD corrected** → `AppendSourceChangeAsync` |
| 2 | Lock order | LLD §18 order differed from checkout and locked inventory by variant | **LLD corrected** (§7.2 order) |
| 3 | Period | LLD relied on the engine check only | **LLD corrected**: period row `FOR SHARE` + typed pre-validation (VI-18) |
| 4 | Transaction | LLD did not note early `SaveChanges` and the rollback clear | **LLD corrected** (§10.2, §18) |
| 5 | Purchase date | LLD §7.1 proposed `SaleDate` "instant" | **LLD corrected**: company calendar *date* of normalised `SaleDate`; never `SaleDateTime` |
| 6 | Cash | LLD said "no DDL if varchar; verify" | **LLD corrected**: CHECK migration required; calculator cases |
| 7 | Dashboards | Mobile store auto-netting not covered | **LLD corrected** (§23) |
| 8 | Receipt | Renderer reuse unspecified | **LLD corrected**: new Exchange renderer |
| 9 | Supervisor | LLD lacked the concrete pattern | **LLD corrected**: `StockCountAdjustmentApproverPolicy` pattern, email lookup, per-IP rate limit |
| 10 | Phone (`required`, registered) | SRS FR-EX-035/038 define a record match with an honest label. This task's instruction forbids silently replacing required verification with matching, and no trustworthy mechanism exists. | **Not changed. Business decision DN-01 required.** Options to present (not decided here): (a) treat the record match (FR-EX-035) as the approved `required` mechanism; (b) make `required` unavailable for registered customers until a verification capability exists; (c) approve a separate verification capability (new feature). |
| 11 | Void guard | Confirmed missing | Already planned (S2-08) |
| 12 | C-09 ledger evidence | Weak for v2 sales | Gap updated; eligibility rule unchanged |

No approved business decision was changed.

## 20. Implementation readiness assessment
**Verdict: CONDITIONAL PASS.**
- The architecture is understood and supports the approved design by static evidence. No critical assumption is contradicted.
- Named database tests, one scoped business decision, the data audit and migration and seeding approvals remain mandatory before the affected functionality is completed or activated.

| Unresolved item | Evidence | Risk | Next action | Authorization | Scope blocked |
|---|---|---|---|---|---|
| TB-01 migrations | §4, §11, LLD §25 | — | Approve the `PosV2ExchangeFoundation` design | Owner (migration gate) | Stage 2 schema work |
| TB-08 + T-01 (TB-03) | §4 | High | Run T-01 on the isolated Test DB | Owner (test DB + test code) | Stage 3 Confirm |
| T-02 (VI-18) | §5 | High | Run T-02 | as above | Stage 3 Confirm |
| T-03 (TB-04) | §7 | High | Run the race matrix | as above | Stage 3 exit |
| T-04, T-05 | §11, VI-10 | Medium | Run after the migration | as above | Stage 3 exit |
| TB-02 data audit | §8, §18 | High (data) | Execute §18 | Owner (DB read access) | Per-business activation |
| DN-01 phone `required` | §9 | Medium | Owner decision | Owner (business) | Phone `required` mode for registered customers only |
| TB-06 seeding | — | Low | Approve templates | Owner (seeding) | Stage 5 / UAT |
| VI-03, VI-20 finance | §11 | Medium | Finance acknowledgement | Finance | Release |

| Stage | Readiness | Reason |
|---|---|---|
| 1 Contracts | **Ready** | Stage 0 evidence incorporated (LLD v2.2); DN-01 recorded |
| 2 Foundations | **Conditionally ready** | Needs TB-01 approval for the schema. Pure code (policy evaluator, date and high-value calculators, permission keys, void-guard logic) can proceed with unit tests. Phone `required` path waits for DN-01. |
| 3 Atomic processing | **Conditionally ready** | Coding is possible; completion needs T-01…T-05 (TB-03, TB-04, TB-08) |
| 4 Reversal and UI | **Conditionally ready** | Depends on Stage 3 schema and flows; UI can be scaffolded against contracts |
| 5 Reporting and verification | **Blocked** until Stages 3–4 exist; needs TB-02 and TB-06 for release |
