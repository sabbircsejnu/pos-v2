# IraniMart POS v2 — Implementation Progress

| Property | Value |
|---|---|
| Document ID | IM-POS-IMPL-001 |
| Started | 2026-11-02 |
| Authority | SRS v1.2 → LLD v1.2 → Loyalty SRS/LLD → prototype → [Rev 3 Gap Analysis](./POS_MASTER_BLUEPRINT_GAP_ANALYSIS.md) → existing code |
| Gap analysis | Not modified. Rows below reference its IDs and track actual resolution. |
| Current position | **Phase 0–3.2 and Phase 4 implemented. STOPPED for owner review of §12–§13. Development migrated to 68 (P21, P3, P31, P32, P4 applied 3 Nov 2026 under owner authorisation, §13). Financial gates D06/D07 OPEN. Phase 5 is NOT started.** |

## 1. Phase status

| Phase | Status |
|---|---|
| 0 — Preparation | **Done** (this document, harness hardening, migration-chain check) |
| 1 — P0 Checkout hardening | **Done** for server-authoritative checkout |
| 2 — Settings, catalogue/customer tenancy, tax, discounts, terminals, D13 numbering | **Done** — see §7. Migrations `…P1…` and `…P2…` are **applied to Development** (backup + rehearsal first) |
| 2.1 — Stabilization (A–H) | **Done** — see §8. All backend tests green. New migration `…P21PricingRuleTenancy` is **generated, not applied to Development** |
| 3 — Cash Session Management & Financial Reconciliation | **Implemented, verified on the Test stack** — see §9. Migration `…P3PosV2P3CashSessionManagement` is **generated, not applied to Development**. D10 async sale GL and the scheduled variance worker were completed in 3.1; browser verification is outstanding |
| 3.1 — D10 asynchronous accounting & variance worker | **Implemented, verified on the Test stack** — see §10. New migration `…P31SaleFinancialPostings` is **generated and rehearsed, not applied to Development** |
| 3.2 — Financial accounting corrections, reversal dates, permissions & hybrid worker | **Implemented, verified on the Test stack** — see §11. Full integration suite 2,103/2,103 passed. New migration `…P32FinancialPostingOperations` is **generated and rehearsed on restored Development data, not applied to Development** |
| 4 — POS UI/UX completion & operational workflows | **Implemented, verified on the Test stack and in a real browser** — see §12. Full suite 2,107/2,108 (one contract test updated and re-passed alone; no full re-run). Migration `…P4SaleSalesperson` added. D06/D07 OPEN; Daily Targets not configured (no approved contract) |
| Development deployment | **P21 → P3 → P31 → P32 → P4 applied to Development** after fresh backup and rehearsal — see §13 |
| 5–8 | Not started |

## 2. Phase 0 — verification results

**Documents.** SRS v1.2 §18.1 carries D1–D14. D12.1–D12.5 are not labelled individually in the decision index; they are expressed as FR-LATE-01…, FR-SEC-01, FR-REPORT-04…06 (SRS §21–§24) and are treated as the D12 sub-decisions. No contradiction with earlier approved requirements was found that needs a business decision.

**Repository state (verified).**
- Working tree is heavily modified relative to `HEAD`: ~1,047 changed paths. `tests/RetailPOS.Tests/**` is deleted in the working tree while `tests/RetailPOS.IntegrationTests/**` and `tests/RetailPOS.UnitTests/**` are untracked; 122 migration files (61 migrations) are untracked, and the 45 tracked migration files are the old January chain that the working tree deleted. **`HEAD` therefore cannot reproduce the schema.** Fix is a source-control action (stage and commit the baseline chain); it was not done here because committing was not requested.
- `dotnet ef migrations has-pending-model-changes` → "No changes" before this phase's edits, so the snapshot matches the model.
- The Development PostgreSQL container (`iranimart-dev-postgres-1`, port 5435) was never touched. The Test stack's PostgreSQL container (`iranimart-test-postgres-1`, 55432) had been stopped for 47 h and was **started** (`docker start`); nothing else in either stack was changed.

**Gap-analysis assumptions re-verified against code** (all confirmed): checkout trusted client `CashierId`, `UnitPrice`, `Discount`, `Tax`; handler never called the pricing pipeline; single-method cash sales stored no tender; line VAT did not sum to `Sale.Tax` with an invoice discount; idempotency race loser surfaced `DbUpdateException`; the original-sale (exchange) link was silently ignored when the sale was not found and was never authorized; legacy `add-points`/`redeem-points` were open to `customers.view`.

**Test-harness safety (Phase 0 deliverable).**
- Two hard-coded Development fallbacks (`SaleCreationPostgresConcurrencyTests`, `JournalNumberRelationalConcurrencyTests`) removed.
- `PostgresTestDatabase.CreateAsync` — the single entry point for every disposable database — now rejects port 5435 and any `retailpos_db*` database; `InfrastructureTestSettings.RequireIsolatedPostgres()` throws when `RETAILPOS_TEST_POSTGRES` is unset. Covered by two new tests in `InfrastructureConfigurationTests`.
- The Sale PostgreSQL harness now builds its schema with `MigrateAsync()` (was `EnsureCreatedAsync()`), so triggers, partial indexes and the new migration are exercised. Documented in `Docs/Development/LOCAL_INFRASTRUCTURE.md` §8.

## 3. Phase 1 — what changed

### Requirements covered
SRS FR-SALE-02, FR-POS-07, FR-PAY-01, FR-SEC-01 (held-cart identity), FR-SEC-02, FR-SEC-03, FR-AUTH-01 (cashier claim-derived), AC-12, AC-26, AC-29 (idempotency part); LLD §8.1, §10, §18.1, §18.6, §21.1, §21.4. Gap rows: FR-POS-07, *Cashier derived from auth*, *Server-authoritative price*, *D07 VAT* (allocation), FR-SALE-02 (payments, idempotency), FR-CUST-02 (legacy loyalty endpoints), exchange-link authorization (N-6).

### Backend
| Area | Change |
|---|---|
| Cashier identity | `SalesController.Create` takes the cashier from the authenticated principal. A submitted `cashierId` is optional and rejected (403) when it differs. Same rule for `HeldSalesController.Hold`; `GET held-sales/cashier/{id}` is limited to the caller. |
| Pricing | `CreateSaleCommandHandler` re-prices every line inside the transaction through `IPosCartPriceCalculator` (`readFromCache:false`). The client `unitPrice` is only an assertion: any difference → **409 `SALE_PRICE_CHANGED`** carrying `[{variantId, submittedUnitPrice, currentUnitPrice}]`. Applied pricing rule id/name come from the server. Unknown or non-active products → 400 `SALE_VARIANT_NOT_SELLABLE`. |
| Discounts | Manual line discount and manual invoice discount remain supported, bounded (`0 ≤ d ≤ amount`, 2-dp) and **mutually exclusive** (400 `SALE_DISCOUNT_CONFLICT`). |
| Tax / totals | New pure `SaleCheckoutCalculator`: server tax from the existing `TaxSettings` (exclusive and inclusive supported), invoice discount and VAT allocated to lines by largest remainder so `Σ SaleItem.VatAmount == Sale.Tax` and `Σ TaxableAmount` reconcile exactly. Client `tax` is an assertion (tolerance 0.01) → 409 `SALE_TAX_MISMATCH` with the server value. Totals are never taken from the client. |
| Payments (D14) | New `SalePaymentPlanner`: method whitelist `cash|card|mobile`; Σ allocations must equal the server payable **exactly** (tolerance removed); cash requires `tendered ≥ amount`; tender only on cash rows; persisted per payment: `Tendered` (= CashTendered), **new** `ChangeGiven`, **new** `NetCashRetained` (= allocated amount). Single-method cash sales now send/persist the tender (`cashTendered`), fixing the lost-change defect. Receipt/read models use the stored change. |
| Idempotency | Key now mandatory (`[Required]` on the DTO, 400 `IDEMPOTENCY_KEY_REQUIRED` in the handler). SHA-256 request fingerprint stored on the sale (**new** `idempotency_fingerprint`). Replay with same payload returns the original sale; different payload → 409 `IDEMPOTENCY_KEY_PAYLOAD_MISMATCH`; the concurrent race loser (unique-index violation) rolls back and returns the winner's sale instead of an exception. Scope stays `(OutletId, Key)` (outlet ⇒ business). |
| Exchange link | The original sale must exist, belong to the **same business as the sale's outlet** (otherwise reported as not found), be `completed` and not already replaced; the controller also requires the caller to be authorized for the original sale's outlet (403). The original row is locked `FOR UPDATE` on PostgreSQL so two cashiers cannot both replace it. |
| Legacy loyalty | `add-points` / `redeem-points` now require `customers.edit` **and** `FeatureFlags:LegacyLoyaltyPointsMutationEnabled` (default off → 403). Legacy earning inside checkout is untouched (retired in Phase 7 with the Loyalty module). |
| Errors | `SaleCheckoutValidationException` (400) and `SaleCheckoutConflictException` (409) carry stable `errorCode`; mapped in `GlobalExceptionHandlerMiddleware`. |

### Frontend (`retailpos-frontend`)
POS screen: no longer sends `cashierId`; sends `cashTendered` for single cash sales; one idempotency key per distinct cart payload (retry reuses it, any edit issues a new one); on `SALE_PRICE_CHANGED` the cart adopts the current server prices (percentage discounts recalculated) and asks the cashier to review. `CreateSaleDto.cashierId` is optional/deprecated. No layout changes in this phase.

### EF Core migration (generated, **not applied** anywhere)
`src/RetailPOS.Infrastructure/Data/Migrations/20261102162334_PosV2P1CheckoutPaymentEvidenceAndIdempotency.cs` (+ Designer, snapshot updated).

| Item | Detail |
|---|---|
| Schema | `sales.idempotency_fingerprint varchar(64) NULL`; `sale_payments.change_given numeric(10,2) NULL`; `sale_payments.net_cash_retained numeric(10,2) NULL`. Additive, nullable, no index, no drop. |
| Existing-data impact | Existing rows keep NULL fingerprint (replay of a legacy key is treated as a plain replay). The migration back-fills only the new columns from stored facts: cash rows `net_cash_retained = amount`; cash rows with a stored tender `change_given = GREATEST(tendered − amount, 0)`. Cash rows without a stored tender keep NULL tender/change (unknown evidence is not fabricated). |
| Duplicate/orphan diagnostics | None required for these columns. Diagnostics needed for **Phase 2** (invoice uniqueness): empty/duplicate `sales.sale_number`, customers without business, products without owner. |
| Rollback | `Down()` drops the three columns (data in them is derivable/lost only for sales created after deployment). Take a backup before applying; the migration was rehearsed only on disposable databases. |
| Approval | **Required before applying to Development/Production. Not applied.** |

### Files changed (this phase)
- Domain/Infrastructure: `Sale.cs`, `SalePayment.cs`, `RetailPOSDbContext.cs`, `SaleCreationStore.cs`, `SaleReadStore.cs`, migration + snapshot.
- Application: `Sales/Checkout/*` (new), `CreateSaleCommand.cs`, `ISaleCreationStore.cs`, `ISaleCreationServices.cs`, `GenerateSaleReceiptDocumentCommand.cs`, `DependencyInjection.cs`.
- API: `SalesController.cs`, `HeldSalesController.cs`, `CustomersController.cs`, `CreateSaleDto.cs`, `HeldSaleDtos.cs`, `GlobalExceptionHandlerMiddleware.cs`.
- Frontend: `pos-screen.component.ts/.spec.ts`, `sale.model.ts`.
- Tests: `InfrastructureTestSettings.cs`, `PostgresTestDatabase.cs`, `InfrastructureConfigurationTests.cs`, `SaleCreationPostgresConcurrencyTests.cs`, `JournalNumberRelationalConcurrencyTests.cs`, `MigrationChainValidationTests.cs`, `SaleCreationCommandTestFactory.cs`, `SaleCreationRelationalBaselineTests.cs`, `SaleService*Tests.cs`, `SalesCreateTerminalHttpTests.cs`, `HeldSalesControllerTests.cs`, loyalty characterization tests; **new**: `SaleCheckoutAuthorityTests`, `SalesCheckoutAuthorizationHttpTests`, `LegacyLoyaltyEndpointGateTests`, `LegacyLoyaltyEnabledApiFactory`, `SaleCheckoutCalculatorTests` (unit).
- Docs: `Docs/Development/LOCAL_INFRASTRUCTURE.md` §8, this file.

## 4. Verification (actual results, 2 Nov 2026)

| Suite | Result |
|---|---|
| `dotnet test tests/RetailPOS.UnitTests` | **565 passed, 0 failed** (24 new: calculator, payment planner, fingerprint) |
| Sale PostgreSQL tests on the **migration-built** isolated Test database (`SaleCreationPostgresConcurrencyTests`, incl. new exchange-lock and evidence tests) | **11 passed**; the exchange test was shown to fail when the row lock is removed |
| New HTTP/handler tests (`SalesCheckoutAuthorizationHttpTests` 7, `SaleCheckoutAuthorityTests` 13, `LegacyLoyaltyEndpointGateTests` 4, held-sale controller 3) | all passed |
| Angular `ng test` (full) / `ng build` | **1,130 / 1,130 passed**; production build succeeded |
| `dotnet test tests/RetailPOS.IntegrationTests` (full, 1,947 tests, 26 min) | 1,926 passed, 21 failed. One failure was caused by this phase (`MigrationChainValidationTests` hard-codes the newest migration id) — fixed and re-run green. The remaining **20** are the pre-existing/environmental failures listed in §5; none involve files changed here. |

## 5. Open items, risks and remaining gaps

**Pre-existing failures (not caused by this phase; not hidden).** `PosLookupHttpTests` ×4 (403 on lookup/cart-prices) failed identically **before** any change in this phase. The same 403/“User not found” family also fails in `ProductVariantSearchHttpTests` ×3, `StockLocationAuthorizationHttpTests` ×3, `StockCountPhase1HttpTests.Download…`, `UserOutletAccessBehaviorTests.SuperAdmin…`; `InfrastructureConfigurationTests` ×4 (Staging/Production appsettings contain `Port=5435` and a Development fallback — changing Staging/Production configuration needs approval); `CleanArchitectureDependencyTests` (`MobileSingleBusinessReadinessGuard` references `RetailPOSDbContext`); `LiveInfrastructureTests` ×3 (RabbitMQ/Redis/Elasticsearch **Test** containers are stopped; only the Test PostgreSQL was started). These were inspected, but only the `PosLookup` group has a recorded pre-change baseline.

**Decisions / assumptions to confirm before Phase 2** (no business policy was invented):
1. **Tax source.** Server tax uses the existing global `TaxSettings` (what the POS applied before). `Product.TaxRate` (used by `/api/pos/cart-prices`) is ignored at checkout, as before. SRS D07 says “generic tax components … preserve the existing business policy”; confirm global rate vs per-product rate. `PricesIncludeTax=true` is now honoured by the server but the POS UI still computes exclusive tax, so enabling it would yield `SALE_TAX_MISMATCH` until the Phase 4 UI work.
2. **Manual discounts.** The SRS requires mutual exclusivity but defines no discount permission or cap; cashiers keep the existing ability (bounded by the line/invoice amount). A `sales.discount` permission/limit would be a new policy.
3. **Quantity-tier pricing rules.** Server price is computed at the real quantity; the POS prices at quantity 1, so a tiered rule yields one `SALE_PRICE_CHANGED` round-trip that the UI now resolves automatically.
4. **Customer tenancy (FR-CUST-01/04, P0) is not fixed.** `customers` has no `BusinessId`; search, create, Walk-in and loyalty endpoints remain global. This needs the additive schema + diagnostics of Phase 2; only the loyalty mutation endpoints were closed here. The POS “add points” buttons on the customer pages will now show a 403 message until Phase 7.
5. **Held-cart list by outlet** still shows every cashier's carts for the outlet (used by Recall). Restricting it needs an approved manager permission (FR-SEC-01).
6. Not in Phase 1 scope and still open: outbox/async GL (D10), session locking (D2/D4), invoice allocator (D13), void/refund cash effects, Late Sales, terminals UI, prototype layout (see Gap Analysis §I).
7. **Source control.** The untracked baseline migration chain and the relocated test projects must be committed before anyone can rebuild the database from `HEAD`.

## 6. Recommended next phase (Phase 1 note, superseded by §7.14)
Phase 2 has been executed; see §7.

## 7. Phase 2 — Settings, tenancy, tax, discounts, terminals, invoice numbering (2 Nov 2026)

### 7.1 Requirements covered
FR-CUST-04 / customer tenancy (P0), D6 catalogue ownership (FR-CAT-01), D3/D9 Business POS settings, P2.1 product-tax policy, P2.2 discount authorization, FR-TERM-04 terminals inside Outlet create/edit, D13 invoice numbering (FR-INV-01), migration-chain reproducibility. Not built (by instruction): cash-session lifecycle, Late Sales UI, loyalty module, manager-approval workflow.

### 7.2 Development database diagnostics (read-only, Step 1)
Target `iranimart-dev-postgres-1`, DB `retailpos_db`, port 5435 (Development, local Docker). Before the migration: 61 migrations applied; 1 Business, 3 Outlets, 6 customers (1 Walk-in + 5 with `+1-555-100x` phones, no business column), 11 categories, 8 products, 24 variants, 3 sales (all with empty `sale_number`, so the new unique index ignores them), 7 sale items, 2 terminals; **no orphans** and no cross-business links. A single Business made every backfill unambiguous. Data was **not** modified until the gated deployment in §7.9.

### 7.3 Tenant isolation (customers, catalogue)
* New shared port `IBusinessScope` (`RequireBusinessId()`; a Super Admin must select a Business — no implicit global view). Every Customer, Category, Product, Variant, identifier-lookup, SKU, maintenance, combination, POS lookup and POS cart-pricing store now filters by it; creates stamp `BusinessId`.
* Customers: unique `(BusinessId, CustomerCode)` and `(BusinessId, PhoneNormalized)`. Phones are normalised (Bangladesh mobile `01…`/`8801…`/`+8801…` → `+8801XXXXXXXXX`, otherwise `+` and 8–15 digits). Create/Update/Quick-Add answer **409 `CUSTOMER_PHONE_EXISTS`** with `{id,name,phone}`; a number that cannot be normalised is rejected, except that an unchanged legacy phone stays editable. A **Walk-in per Business** is created on demand; `customers_protect_walkin` also protects `business_id`. Legacy loyalty mutation endpoints remain disabled.
* Catalogue: `Category/Product/ProductVariant.BusinessId` with composite FKs `(CategoryId|ProductId|ParentCategoryId, BusinessId)`; SKU, barcode and product code are unique **per Business**. Checkout rejects variants of another Business (`FindUnsellableVariantIds`) and pricing/lookup caches are keyed by Business (and tax policy) so one Business can never read another's cached price. Database triggers additionally require `sales.customer`, `sales.outlet` and `sale_items.variant` to belong to the sale's Business. `SaleItem` snapshots are untouched.

### 7.4 Business POS settings (`business_pos_settings`, audited)
`GET/PUT /api/settings/pos`: max cash-session duration (1–20, default 20), default IANA time zone (default `Asia/Dhaka`; Outlet override stored as `outlets.time_zone_id`), tax switch/default rate/name/prices-include-tax, `MaxManualDiscountPercent` (default 0). Optimistic `version`, `updated_by`, audit event with field-level before/after. Until a Business saves its row the legacy tax JSON is used so tax behaviour is unchanged. `/api/settings/tax` now reads/writes the same Business row. **The 20 h value is stored and exposed as the snapshot contract only; session enforcement is Phase 3** (the existing cash-session code still reads the legacy 15 h JSON setting).

### 7.5 P2.1 Product-tax policy
One resolver (`TaxResolver`) used by cart pricing, POS lookup, checkout and snapshots: Business switch → product classification (`exempt`/`zero_rated` ⇒ 0%) → product rate (explicit `0` = zero-rated, **not** missing) → Business default. `products.tax_rate` is now nullable (+`tax_classification`). Checkout groups lines by rate, taxes each group **after** discounts and allocates tax to lines by largest remainder (single-rate sales give exactly the Phase 1 result); inclusive and exclusive prices supported. Sale items snapshot rate, classification, taxable and VAT; the VAT report now emits reversal rows per line (mixed-rate sales). `/api/pos/cart-prices` and checkout use the same code, so they cannot disagree; the Angular POS now ports the algorithm (shared test vectors with C#) so `PricesIncludeTax` no longer causes `SALE_TAX_MISMATCH`.

### 7.6 P2.2 Discount authorization
New permissions `sales.discount.apply`, `sales.discount.override_limit`, `sales.discount.configure`. Server rules (`SaleDiscountAuthorizer`): no permission ⇒ **403 `SALE_DISCOUNT_NOT_PERMITTED`**; effective % (fixed amounts converted against the pre-manual line/invoice base; automatic rule discounts never count) above `MaxManualDiscountPercent` without override ⇒ **409 `SALE_DISCOUNT_LIMIT_EXCEEDED`** `{limitPercent,effectivePercent,scope}`; never silently reduced; every manual discount writes an audit event (cashier, scope, amounts, effective %, limit, override used). Line vs invoice exclusivity unchanged. Default limit 0 ⇒ manual discounts disabled until a Business configures it; no role limits invented, no approval workflow. Seed roles: `Admin` gets apply+configure, `OutletManager` apply; wildcard owners get all; **existing Dev role rows were not rewritten** (assign via Roles UI).

### 7.7 Terminals in Outlet create/edit
`POST/PUT /api/outlets` accept `invoicePrefix`, `timeZoneId`, `terminals[]` (new permission `outlets.terminals.manage`; omitted = untouched; rows never deleted by omission); `DELETE /api/outlets/{id}/terminals/{tid}` only for terminals without sessions/sales/receipts. Deactivation is refused while a cash session is open (open covers overdue). Checkout: **no default fallback** — one active terminal is auto-selected, several ⇒ `SALE_TERMINAL_REQUIRED`, none ⇒ `SALE_NO_ACTIVE_TERMINAL`. Angular: outlet form rebuilt on the UI kit with an editable, **not-persisted-until-save** “Counter 1” suggestion; POS enforces the same selection rules.

### 7.8 D13 invoice numbering
`{OutletPrefix}{yyMMdd}{DailySequence}` (no separators/padding), counter `sale_invoice_counters(BusinessId, OutletId, BusinessDate)` advanced with one atomic `INSERT … ON CONFLICT … RETURNING` inside the sale transaction (rolled-back sales return the number; issued numbers are never reused). Sales store `invoice_prefix`, `invoice_sequence`, `invoice_issued_at_utc`; unique `(BusinessId, SaleNumber)` and `(BusinessId, OutletId, BusinessDate, InvoiceSequence)`. Outlet prefix: letters/digits, starts with a letter, ≤10, unique per Business, rejected when one prefix extends another with digits only (the only way two numbers could collide), **locked after the first invoice**. The same allocator signature takes the business date, so Late Sales can pass the original date. The legacy file counter no longer feeds sales (its settings page is read-only/legacy).

### 7.9 Schema, migrations and Development deployment
* `20261102162334_PosV2P1CheckoutPaymentEvidenceAndIdempotency` (Phase 1) and `20261102181136_PosV2P2TenancyTaxSettingsInvoiceNumbering` (hand-edited: nullable → backfill → NOT NULL, aborts with a clear error instead of guessing when several Businesses already hold catalogue data).
* Backfill: all customers/catalogue/sales → the single Business; `phone_normalized` computed with the C# rules (duplicates inside a Business keep the lowest id, others get NULL; raw phone untouched); `products.tax_rate = 0` → NULL; provisional outlet prefixes `O001…` (editable until first invoice).
* **SQL review** (`backups/phase2/phase1-phase2-from-20261023072233.sql`, git-ignored): no `DROP TABLE/COLUMN`, `DELETE`, `TRUNCATE`; only FK re-creation and replacing the global unique indexes of product code/barcode, variant SKU/barcode and customer code by per-Business ones.
* **Gates (all met before applying):** target identified and confirmed Development/non-production; source reproducible (clean copy of exactly the files git would track builds, `MigrationChainValidationTests` and “no pending model changes” pass there); SQL reviewed; verified backup `backups/phase2/retailpos_db-pre-phase2-20261102-190815.dump` (SHA-256 `D3CFCEC3…962CB`, restored into a scratch DB on the Test PostgreSQL: 61/6/8/3 rows matched); **rehearsal on that restored copy of the real Dev data** (both migrations applied, results checked); additive-only; order verified; constraints/tests green. Applied to Dev on 2 Nov 2026 19:3x via `dotnet ef database update --connection …` (credentials from the git-ignored `.env.dev`, never printed). **Post-migration Dev check:** 63 history rows; row counts unchanged (6 customers, 8 products, 24 variants, 11 categories, 3 sales, 7 items); 0 NULL `business_id`; 0 unvalidated constraints; triggers and new tables present; backfilled phones `+15551001…5`; product rates `8.50/8.50/8.50/NULL/NULL/5.00/8.50/7.50`; outlet prefixes `O001/O002/O003`. No reset, `EnsureDeleted`, volume or destructive action was used.

### 7.10 Verification (actual results)
| Suite | Result |
|---|---|
| `tests/RetailPOS.UnitTests` | **653 passed, 0 failed** (+88: tax resolver, discount authorizer, settings/outlet rules, invoice format, phone normaliser, POS tax/price/lookup, calculator mixed rates) |
| `tests/RetailPOS.IntegrationTests` (full, 1,989 tests, 33 min; Test stack: PostgreSQL only) | **1,960 passed, 29 failed → after fixing 9 test-seed/data problems, 20 failures remain**, identical by name to the 20 listed in §5 (environmental or pre-existing). Re-run of the 9 fixed classes: 46/46 green |
| New Phase 2 integration tests | `Phase2CheckoutTests` (tax override/fallback/zero/exempt/mixed/inclusive/cart-vs-checkout, discount denied/over-limit/within/override+audit, rule discounts, foreign customer/variant, per-Business Walk-in, structured invoice fields, terminal 0/1/many), `Phase2PostgresTests` (**allocator concurrency 24 parallel → gapless 1..24, day reset, per-outlet independence, rollback gives number back, no-prefix/no-transaction guards, cross-Business FK/trigger/unique rejections, legacy-data upgrade backfill, multi-Business abort**), `OutletPosSetupStoreTests`, `BusinessPosSettingsStoreTests`, customer store isolation tests — all green; the PostgreSQL tests found and fixed a real allocator bug (`SqlQuery` composition) and an outlet-create tracking leak |
| Angular `ng test` / `ng build` | **90 test files all passed**; production build succeeded (one existing-style warning: `pos-screen.component.css` is 426 bytes over its 10 kB budget) |

**The 20 remaining failures, individually** (none touched by Phase 2 logic; none newly introduced): `PosLookupHttpTests` ×4, `ProductVariantSearchHttpTests` ×3, `StockLocationAuthorizationHttpTests` ×3 and `UserOutletAccessBehaviorTests.SuperAdmin…` ×1 (Super-Admin/test-user authorization returns 403 / “User not found” — same assertion and status as the Phase 1 closing run); `StockCountPhase1HttpTests.Download…` ×1; `InfrastructureConfigurationTests` ×4 (Staging/Production appsettings still contain the 5435 Development fallback — needs approval); `CleanArchitectureDependencyTests` ×1; `LiveInfrastructureTests` ×3 (RabbitMQ/Redis/Elasticsearch Test containers stopped). A baseline taken **before any change** exists only for the `PosLookup` group (§5); for the others the evidence is the Phase 1 closing run and the unchanged failure message. Because those tests fail before reaching the tenancy logic, **Super-Admin-with-explicit-Business behaviour is covered by unit/handler tests only, not by those HTTP tests**.

### 7.11 UI prototype conformity
Outlet form (terminals inside the form, “Counter 1” suggestion), POS terminal/discount/tax states and the settings “POS & Tax” tab were built from the prototype and the design-system kit. **No visual or browser verification was performed** (390/768/1440 px, focus, contrast are unchecked). Terminal rows are a styled form list, not `app-ui-data-table` — needs design review.

### 7.12 Decisions / risks for the owner
1. **Product tax values retained** — products with `8.5/5/7.5` now apply those rates at checkout (previously ignored); `0` became “not configured”. Revert SQL is trivial if unwanted.
2. **Provisional outlet prefixes** `O001–O003` allow existing outlets to sell; owners should set real prefixes before the first real invoice.
3. **Default manual-discount limit is 0%** and existing Dev roles lack the new permissions, so manual discounts stay off until a Business configures the limit and assigns the permissions.
4. Barcode/SKU/product-code uniqueness is now per Business.
5. Price rules and outlet overrides have no Business column (global rules apply to every Business) — recommend scoping in Phase 3/4.
6. The Angular `/settings` route needs `settings.edit`, so a user with only `sales.discount.configure` cannot reach the discount limit field.
7. Redis price/lookup caches may serve product-level tax/price changes for up to 5–10 min (settings changes are keyed and immediate); checkout always re-prices without cache.
8. `SixLabors.ImageSharp 3.1.12` reports known high-severity advisories (pre-existing, untouched).
9. Cash-session limits (15 h legacy vs 20 h Business setting) diverge until Phase 3.

### 7.13 Source control — reproducibility and staging plan (nothing staged, committed or pushed)
Working tree: HEAD `86509f72`; hundreds of modified/deleted/untracked paths; the 122+ baseline migrations, the relocated test projects and Phase 0–2 work are all **untracked/uncommitted**. Verified: a copy of exactly `git ls-files -co --exclude-standard` (src, tests, slnx) builds and passes the migration-chain test. Suggested commits, in order (stage by path, never `git add -A`):
1. `src/RetailPOS.Domain` + `src/RetailPOS.Infrastructure/Data/Migrations/*` baseline chain + `RetailPOS.slnx` + `tests/**` project moves (reproducible build base);
2. Phase 0/1: checkout/harness changes and migration `…P1…`;
3. Phase 2 backend (tenancy, settings, tax, discounts, terminals, D13) + migration `…P2…`;
4. Phase 2 tests;
5. `retailpos-frontend/**` (excluding `dist`, `.angular`, `node_modules`);
6. `Docs/08_Sales_And_POS/**`, `Docs/Development/**`.
**Do not stage:** `design-reference/` (purchased Able Pro, ~2,200 untracked files, not git-ignored — add it to `.gitignore` or keep it out), `src/RetailPOS.API/system-settings.json` (runtime settings file), `backups/` (ignored), `TestResults/`, `docker/.env.*`, any `*.dump`. `database/*.sql` seed scripts and `retailpos-mobile/` are separate decisions.

### 7.14 Recommended next step
Phase 3 (cash-session lifecycle: 20 h snapshot, overdue, close/reconcile, timezone/business-date) once you confirm §7.12, and decide whether to fix the 20 failures' shared cause (test-user/Super-Admin seeding) first.

### 7.15 Main files changed
Application: `Abstractions/IBusinessScope`, `Customers/*`, `Products/*`, `Pos/*`, `Sales/Checkout/{TaxResolver,SaleCheckoutCalculator,SaleDiscountAuthorizer,SaleCheckoutSettingsProvider,SaleCheckoutExceptions}`, `Sales/Commands/CreateSaleCommand`, `Settings/**/BusinessPos*`, `Outlets/OutletPosSetup`, `Identity/Permissions/PermissionCatalog`. Infrastructure: `RetailPOSDbContext`, `Customers/*`, `Products/*`, `Repositories/Product*`, `Categories/CategoryCatalog`, `Pos/*`, `Sales/{SaleInvoiceNumberAllocator,SaleCreationStore,HeldSaleStore}`, `Settings/BusinessPosSettingsStore`, `Outlets/{OutletPosSetupApplier,OutletWriteStore,OutletReadStore}`, `AccountingReports/VatReportReadStore`, `Data/DbSeeder`, migration `…P2…`. API: `SettingsController`, `OutletsController`, `SalesController`, `PosController`, `PricingController`, DTOs, `BusinessScopeAdapter`, `BusinessPosSettingsAuditWriter`, `GlobalExceptionHandlerMiddleware`, `Program.cs`. Frontend: POS screen (+`pos-tax/pos-discount/pos-terminals/pos-sale-errors`), outlet form, settings page, product form/detail/list, role form, services/models. Tests: ~100 test files re-seeded with `BusinessId`; new suites listed in §7.10.

## 8. Phase 2.1 — Stabilization (2 Nov 2026)

Scope: A–H only (fix the 20 failures, reproducibility, pricing tenancy, tax-zero semantics, discount-route authorization, cache invalidation, legacy-loyalty safety, security advisory). **No Phase 3 / cash-session / Loyalty-module work, no Git staging/commit/push, nothing applied to Development, no destructive DB action, no test weakened or deleted** (assertions were only changed where the fixture or an expectation was provably stale — each case is listed below).

### 8.1 Baseline at the start
HEAD `86509f72`; Development DB 63 migrations (last `…P2…`); unit 653 passed; integration 1,960 / 1,989 passed (20 failing, §7.10); Angular 90 files passed; solution builds.

### 8.2 Workstream A — the 20 failures: cause and result
| Failing tests (before) | Root cause | Fix | After |
|---|---|---|---|
| `PosLookupHttpTests` ×4, `StockLocationAuthorizationHttpTests` ×3 (StockHint + 2 VariantSearch), `UserOutletAccessBehaviorTests.SuperAdmin…` ×1 | Tests acted as Super Admin users (7003, 9999, 7703) that were **never seeded**, so `UserOutletAccess` correctly answered “User not found” (403). The fixtures also gave the Super Admin a `businessId` **claim**, which a real Super Admin token does not have. | New `TestSuperAdmin` helper: seeded active user with the *Super Admin* role, token **without** a `businessId` claim (`X-Test-BusinessId: none`), Business chosen only through the validated `X-Business-Id` header (the production path through `BusinessContextMiddleware`). `StockLocation…` fixture also placed a Business-1 variant's stock at a Business-2 outlet (impossible data); the Super Admin tests now use an unassigned outlet **inside** the selected Business and a new test asserts the foreign outlet is refused. | green |
| `ProductVariantSearchHttpTests` ×3 (`Search_…`, `GetById_…`, `GetBySkuAndBarcode_…`) | `GetById`/`Search` resolve the caller's stock locations, which needs the user row (777) — not seeded in that fixture. `GetBySkuAndBarcode` then showed an **expectation that predates the stock-status feature**: only the by-id read enriches stock (`not_initialized`), SKU/barcode reads report `unavailable`. | Seed Business + user 777. The test now asserts the documented difference (`not_initialized` vs `unavailable`) and compares the rest of the payload byte-for-byte (stock fields excluded). | green |
| `StockCountPhase1HttpTests.Download…` ×1 | Asserted the Excel date format `yyyy-mm-dd`, but the company date format defaults to `dd/MM/yyyy`; the export correctly follows the company setting. | The test pins the company date format to `yyyy-MM-dd` (and restores it) instead of relying on the default. | green |
| `InfrastructureConfigurationTests` ×4 | `appsettings.Staging.json` / `appsettings.Production.json` carried a `ConnectionStrings` block pointing at the Development database (`127.0.0.1:5435`). | Blocks removed. Staging/Production now **fail startup** until the deployment supplies `ConnectionStrings:DefaultConnection` (no silent fallback to Development). **This is a Staging/Production configuration change — see §8.11.** | green |
| `CleanArchitectureDependencyTests` ×1 | `MobileSingleBusinessReadinessGuard` (API layer) referenced `RetailPOSDbContext`. | Interface moved to `Application/Mobile`, implementation to `Infrastructure/Mobile`; DI and four Mobile controllers updated. | green |
| `LiveInfrastructureTests` ×3 | Test Redis/RabbitMQ/Elasticsearch containers were stopped. | Started the isolated Test stack from `docker/` (`compose.test.yml`, project `iranimart-test`); no Development container touched. | green |

**Product defects found while fixing A (not just fixtures):**
* **Super Admin without a Business context could list every Business's outlets/warehouses** through all ~25 callers of `UserOutletAccess` (stock adjustments, transfers, reports, …). It now fails closed with `BusinessContextRequiredException` (HTTP 400, `business_context_required`); with a validated Business it sees only that Business. Covered by unit-level and HTTP tests (stock-hint, both variant-search routes, POS lookup, cart-prices).
* `ProductVariantLookupController` swallowed that exception into a 500; it now lets it reach the global handler (400).
* An unknown/unavailable Business, or a regular user sending `X-Business-Id`, is refused (403) — tested.

### 8.3 Workstream C — Business-scoped pricing rules and outlet overrides
* `price_rules.business_id` and `outlet_price_overrides.business_id` (FK → `businesses`, `RESTRICT`); rule index is now `(business_id, is_active, rule_type, target_id)`.
* Every store (`PriceRuleCommandStore/ReadStore`, `OutletPriceOverrideCommandStore/ReadStore`, `PosCartPricingStore`) is scoped by `IBusinessScope`: list/get/create/update/delete, name-uniqueness, target-existence (variant/product/category/outlet of **the same Business**) and cart pricing ignore other Businesses' rules and overrides. Create stamps the Business; another Business's id behaves as “not found”.
* **Migration `20261102212702_PosV2P21PricingRuleTenancy` — generated and hand-edited, NOT applied to Development.** Add nullable → backfill → `NOT NULL`: overrides inherit their **outlet's** Business (abort if the variant belongs to another Business); rules take the single Business when exactly one exists, otherwise the migration **aborts with a clear message instead of guessing**. Two PostgreSQL triggers reject an override whose outlet/variant belongs to another Business and a rule whose target (by type) does; global rules need no target.
* **Rehearsal (disposable databases only):** (a) full chain on a fresh isolated PG (`MigrationChainValidationTests`, now pinned to the new id); (b) 6 PG tests — trigger rejections for every rule type and the override outlet/variant, backfill of a single-Business database, derivation of the override owner from its outlet with several Businesses, refusal of ownerless rules with several Businesses, refusal of an override whose outlet and variant disagree; (c) a **read-only dump of the real Development database restored into a scratch DB on the Test server and migrated**: 63 → 64 history rows, both columns `NOT NULL`, both triggers present, products/sales/customers (8/3/6) unchanged, then dropped. Development holds 0 rules and 0 overrides today, so applying it later is unambiguous.
* Tests: `PricingTenancyStoreTests` (4), `PricingTenancyPostgresTests` (6), updated relational/PG/HTTP seeds. Quantity-tier selection, priority order and “rule discounts do not count against the manual-discount limit” are covered by the existing (green) tests. Rule-name uniqueness is enforced per Business in the store; no new DB unique index was added.

### 8.4 Workstream D — product tax, zero versus “not configured”
Semantics (unchanged, now fully covered): `NULL` = not configured → Business default; `0` = explicit 0 % (classified `zero_rated`); `exempt`/`zero_rated` classification forces 0 % regardless of rate; the Business master switch off charges nothing (`none`).
* **Evidence about the Phase 2 migration** (`UPDATE products SET tax_rate = NULL WHERE tax_rate = 0`): the pre-Phase-2 backup, restored to a scratch DB on the Test server, held 8 products with rates `8.50 ×4, 5.00, 7.50, 0.00 ×2`. The two `0.00` rows are *Classic Crew T-Shirt* and *Summer Floral Dress* (ids 4, 5), created from `DbSeeder`, which assigns `TaxRate = 0m` to exactly those two apparel items. Pre-Phase-2 checkout **ignored the product rate** (§5.1), so no historical sale amount changed; but the migration turned a seeded explicit zero into “not configured”, so **future** sales of those two products use the Business default rate. Development today: ids 4 and 5 are `NULL`, others unchanged.
* **No data was changed and no classification was invented.** If the apparel really is zero-rated, set it from the product form (explicit `0` or *zero-rated*, both round-trip). Optional correction SQL, **not applied**: `UPDATE products SET tax_rate = 0 WHERE id IN (4,5);`. **Before applying the Phase 2 migration to any other environment, run `SELECT id, name FROM products WHERE tax_rate = 0` and decide per product.**
* Added: 5 HTTP tests (create with omitted/0/exempt, update round-trip 0 → 0 → null → zero_rated → standard, out-of-range −1 / 100.01 rejected, unknown classification rejected) and a rule-resolver/checkout/cart suite already in `Phase2RuleTests`/`Phase2CheckoutTests`; 6 Angular specs prove the product form sends `0`, `null`, exempt and zero-rated unchanged. Receipts print the per-line snapshot taken at checkout (rate, tax amount, classification), which those tests cover.

### 8.5 Workstream E — discount-configuration route authorization
* Angular: new route `/settings/discounts` guarded by `sales.discount.configure`, menu entry “Discount Limit”, a page that sends **only** `maxManualDiscountPercent`; `/settings` and `/settings/company` still need `settings.edit` (so the discount permission unlocks nothing else). The discount field moved out of the full Settings page; a user holding both permissions saves the limit from the new screen. 403/409 are shown inline (409 reloads the stored limit).
* API (already enforced, now tested): `GET /api/settings/pos` allows `settings.view`/`sales.create`/`sales.discount.configure`; `PUT` needs `sales.discount.configure` **only for the limit** and `settings.edit` for every other field; mixing a forbidden field makes the whole request 403 and writes nothing; an empty body is 400. `PosSettingsAuthorizationHttpTests` (12 cases) plus Angular specs for the new page and routing.

### 8.6 Workstream F — price/lookup cache invalidation
**Problem found:** invalidation existed only for stock; the old `InvalidatePrice/InvalidateLookup` keys were never built with the Business/tax scope introduced in Phase 2 and nothing called them, so product price/tax edits, rule and override changes were served stale for 5–10 min.
**Mechanism:** every price/lookup key now embeds a **version token** per Business (`pos:ver:{b}`) and per Outlet (`pos:ver:{b}:{o}`) stored in the shared distributed cache. EF interceptors (`PosPricingSaveChangesInterceptor` + `PosPricingTransactionInterceptor`) record which scopes a unit of work dirties — product/variant → Business; price rule → Business, or only the target outlet for an outlet rule (re-targeting invalidates old and new); outlet override → that outlet — and replace the token **only after the commit** (right after `SaveChanges` when no transaction is open, otherwise on transaction commit; rollback or a failed save drops it). Orphaned entries simply expire by TTL; nothing is scanned or deleted. Tax-policy changes were already part of the key.
* **Resilient:** a token read failure returns “no token” → the request skips the cache entirely and reads the database (Redis outage = slower, never stale); invalidation retries 3×, then logs an error and relies on TTL. **Idempotent** (each bump is a fresh token). **Checkout never reads the cache** (it re-prices with `readFromCache:false`), so money is always computed from the database.
* **Tests:** 3 new unit tests (checkout ignores a stale cached price, unavailable backend bypasses the cache, lookup key follows the version) plus updated key assertions, and 12 new integration tests — token scoping per Business/Outlet, outage and retry, post-commit semantics (explicit transaction commit/rollback, failed save), and HTTP end-to-end: price change → lookup, tax change → lookup, rule → lookup/cart-prices, override → own outlet only, other Business unaffected, and 5 rounds of concurrent readers around a committing writer (every reader that starts after the commit sees the new price).
* **Remaining eventual-consistency window:** (1) all 3 invalidation attempts fail while reads still succeed → stale up to 5 min (prices) / 10 min (lookups); (2) changes that bypass EF (manual SQL, `database/*.sql`, another application) are not seen; (3) without Redis the in-memory distributed cache is per process (single-instance dev only); (4) lookup entries also carry a cached stock quantity, still governed by the existing stock invalidation/30 s TTL. Cost: 1–2 extra cache reads per lookup/cart-price request.

### 8.7 Workstream G — legacy loyalty earning safety
* Checkout no longer earns legacy integer points and void no longer deducts them (`loyaltyPointsAwarded` stays in the contract, always 0); the existing add/redeem-points endpoints stay behind `FeatureFlags:LegacyLoyaltyPointsMutationEnabled` (off by default) **and** `customers.edit`. New `GET /api/customers/loyalty-capabilities` (needs `customers.view`) tells the UI whether mutation is available.
* Angular: Add/Redeem points are hidden unless the flag is on **and** the user has `customers.edit`; the stored balance is shown as **“Legacy points (read-only)”**; no POS-screen change was needed (no earn/redeem UI there). Capability failure means “disabled”.
* Tests: three existing expectations that encoded the old deduction (`SaleCreationRelationalBaselineTests` ×2, `SaleCreationPostgresConcurrencyTests` ×1) now assert the balance is untouched; new checkout test (customer sale leaves 40 points at 40, event reports 0), flag-closed/open capability tests, and Angular specs for the service and both customer screens.

### 8.8 Workstream H — `SixLabors.ImageSharp 3.1.12` advisories
Reviewed against the GitHub advisory database: GHSA-gwg2-r3hj-4w44 (medium, ICC CLUT allocation), GHSA-j3p4-wp97-rph4 (high, `HistogramEqualization`), GHSA-j9gm-c75j-xc9q (high, TIFF T4 encoder), GHSA-jjfr-hcj7-qf5w (high, TIFF T6 encoder), GHSA-wmxv-xphr-5c9g (medium, BigTIFF decoder spin) and a sixth reported since the brief, **GHSA-v76p-62qx-wwq2** (high, tiled fax-TIFF decoder OOB write). Fixed in **3.2.0** (or 4.1.2).
* **The upgrade was tried and rejected:** 3.2.0 compiles and passes the image tests, but it adds a build-time licence check — **a Release build/publish fails** with “No Six Labors license found” unless a licence key/file is supplied (Debug only warns). Committing it would silently break production builds, and the licence is a commercial decision, so `RetailPOS.Infrastructure.csproj` stays at **3.1.12** and the NU1902/NU1903 warnings remain (deliberately not suppressed).
* **Exposure analysis (`src` usage):** the only attacker-reachable use is `ProductImageProcessor` (authenticated product-media upload; the declared MIME type is client-controlled but ImageSharp sniffs the real format, so a BigTIFF labelled `image/jpeg` reached the decoder). Barcode labels only encode generated pixel data to PNG. `HistogramEqualization` and the TIFF **encoders** are never called; ICC profiles are never parsed.
* **Mitigation shipped:** the upload decoder now registers **only JPEG, PNG and WebP** (exactly what the endpoint allows), so TIFF/BigTIFF/GIF/BMP payloads are rejected as unknown formats before any TIFF code runs (closes GHSA-wmxv and GHSA-v76p; encoder advisories were already unreachable). 4 tests: BigTIFF, Group-4 TIFF and GIF are refused within 10 s; a PNG still yields the medium/thumb WebP variants. **Residual risk:** GHSA-gwg2 (ICC allocation) in JPEG/PNG metadata — not reachable because no code reads ICC data.
* **Decision needed:** obtain a Six Labors licence (or evaluate a replacement library) and then move to 3.2.0 / 4.1.2.

### 8.9 Workstream B — Git, migration chain and staging (nothing staged, committed or pushed)
* **Reproducibility (re-verified on the final tree):** a copy of exactly `git ls-files -co --exclude-standard` for `src`, `tests`, `RetailPOS.slnx` (2,135 files) builds with 0 errors and passes `MigrationChainValidationTests`, the two PG tenancy suites, `CleanArchitectureDependencyTests` and `InfrastructureConfigurationTests` (52 tests). **64 migrations** on disk; `dotnet ef migrations has-pending-model-changes` → “No changes”; a fresh isolated PostgreSQL applies the whole chain with `MigrateAsync` (idempotent second call) in that test.
* **Working tree:** 5,245 changed paths — `design-reference/` 2,238, `src/` 1,858, `tests/` 465, `retailpos-frontend/` 387, `Docs/` 145, `retailpos-mobile/` 133, `docker/` 7, `database/` 5, `.github/` 3, others 5. Almost all of the backend is **untracked**, including the baseline migration chain; Phase 0–2.1 live in the same files, so **commits cannot be split per phase and still build** — only per area.
* **Staging manifest (stage by path; never `git add -A`; deletions via `git add -u -- <path>`):**

| # | Commit | Pathspecs (approx. count) | Notes |
|---|---|---|---|
| 1 | Build base + backend + migrations | `RetailPOS.slnx`; `src/RetailPOS.Domain/**` (147); `src/RetailPOS.Application/**` (692); `src/RetailPOS.Infrastructure/**` (595, incl. `Data/Migrations/**` = 64 migrations + designers + snapshot); `src/RetailPOS.API/**` **except** `system-settings.json` (362); deletions `src/RetailPOS.Core/**` (62) | Verified by the clean-copy build. Optionally commit `Data/Migrations/**` first |
| 2 | Tests | `tests/RetailPOS.UnitTests/**` (105), `tests/RetailPOS.IntegrationTests/**` (333); deletions `tests/RetailPOS.Tests/**` (27) | Needs the Test stack + user-secrets |
| 3 | Frontend | `retailpos-frontend/**` except `dist/`, `.angular/`, `node_modules/` (387) | Phase 2.1 part: 21 files (§8.12) |
| 4 | Docs | `Docs/08_Sales_And_POS/**`, `Docs/Development/**`, `Docs/02_Security/**`, `Docs/UI-Standardisation/**`, other `Docs/**` as reviewed (145) | |
| 5 | Infrastructure files | `docker/compose.dev.yml`, `docker/compose.test.yml`, `docker/env.*.example`, `docker/Set-TestUserSecrets.ps1` (no secrets — checked), `.github/**` (3), `.gitignore` | Review the `.gitignore` diff first |
| — | Separate owner decisions | `retailpos-mobile/**` (133), `database/*.sql` (5), `docker-compose.a2-postgres.yml` (retired stack), `RBAC_PERMISSION_CONSISTENCY_AUDIT.md` | |

* **Hygiene report / must NOT be staged:** `design-reference/` (2,238 untracked files of the purchased Able Pro kit, **not** git-ignored); **`src/RetailPOS.API/system-settings.json` is a TRACKED file modified by the running app** (never stage; recommend `git rm --cached` + ignore + commit an `.example`); `backups/` and all dumps (git-ignored ✔), `docker/.env.dev` / `.env.test` (ignored ✔), `bin/ obj/ node_modules/ dist/ build/` (ignored ✔), `TestResults/`, `*.trx`. **`.gitignore` recommendations:** `/design-reference/`, `src/RetailPOS.API/system-settings.json`, `TestResults/`, `*.trx`.

### 8.10 Verification (actual results, 2 Nov 2026)
| Suite | Result |
|---|---|
| `tests/RetailPOS.UnitTests` | **656 passed, 0 failed** (653 → +3) |
| `tests/RetailPOS.IntegrationTests` (full, Test stack: PostgreSQL, RabbitMQ, Redis, Elasticsearch up; 31 min) | **2,042 passed, 0 failed** (was 1,960 / 1,989); includes `LiveInfrastructureTests`, `InfrastructureConfigurationTests`, `CleanArchitectureDependencyTests`, the previous flaky `P306…` test and every PG migration/tenancy test |
| Angular `ng test` | **95 files / 1,267 tests passed** (90 → +5 spec files) |
| `ng build` | Succeeded; the same pre-existing warning (`pos-screen.component.css` 426 B over its 10 kB budget) |
| Solution build | 0 errors; warnings: ImageSharp NU1902/NU1903 (§8.8) |
No visual or authenticated-browser verification was performed (390/768/1440 px, focus, contrast remain unchecked).

### 8.11 Behaviour changes and decisions for the owner
1. **Staging/Production `appsettings` no longer contain a connection string** — deployments must supply `ConnectionStrings:DefaultConnection` (this is what the test demanded; it fails startup rather than falling back to Development). Confirm your deployment config does.
2. **Super Admin must select a Business** for location-scoped endpoints (400 otherwise). Any screen that relied on “all Businesses” must send `X-Business-Id`.
3. **Apply `…P21PricingRuleTenancy` to Development?** Needs approval (backup first; rehearsal already done; 0 rules/overrides today). After it, price rules and overrides created from the UI belong to the selected Business.
4. **Apparel tax (§8.4):** are products 4 and 5 zero-rated? Run the inventory query before migrating other environments.
5. **ImageSharp licence (§8.8):** buy/provide a Six Labors licence, or choose another library, then upgrade.
6. Resolved from §7.12: item 5 (rules/overrides now per Business), 6 (separate discount route), 7 (cache invalidation); item 8 mitigated, not closed.
7. Decide `design-reference/` and `system-settings.json` handling (§8.9).

### 8.12 Development / Staging / Production change status
* **Development database:** unchanged (63 migrations, same rows). Only read-only `pg_dump`/`SELECT`; all rehearsals ran on disposable databases on the Test server and were dropped.
* **Staging/Production:** only the `appsettings` change in §8.11-1; no infrastructure change.
* **Not done on purpose:** no commit/stage/push, no Phase 3, no Loyalty module, no cash-session work.

### 8.13 Phase 3 readiness
Ready from the engineering side: all 2,042 backend and 1,267 frontend tests are green, the migration chain is reproducible from the tracked file set, and tenancy now covers customers, catalogue, pricing, locations and caches. Preconditions I recommend before Phase 3: owner answers to §8.11 (3, 4 and 5 especially), approval to apply `…P21…` to Development, and a first commit of the backend/test base so Phase 3 starts from a recoverable history.

### 8.14 Main files changed (82 files since the Phase 2 documentation)
Application: `Identity/LocationAccess/UserOutletAccess`, `Mobile/IMobileSingleBusinessReadinessGuard`, `Pos/Abstractions/IPosPricingCacheVersion`, `Pos/Queries/{CalculatePosCartPrices,GetPosProductLookup}Query`, `Sales/Commands/{CreateSale,VoidSale}Command`. Domain: `PriceRule`, `OutletPriceOverride`. Infrastructure: `Pricing/*` (4 stores), `Pos/{PosCartPricingStore,PosPricingCacheVersion,PosPricingCacheInvalidation}`, `Mobile/MobileSingleBusinessReadinessGuard`, `Products/ProductImageProcessor`, `RetailPOSDbContext`, migration `…P21PricingRuleTenancy` (+ snapshot), `RetailPOS.Infrastructure.csproj` (unchanged version). API: `appsettings.{Staging,Production}.json`, `Program.cs`, `CustomersController` (+`LoyaltyCapabilitiesDto`), `ProductVariantLookupController`, four Mobile controllers, `IPosCacheService` (comments). Frontend: `app.routes`, `menu.model`, `pages/settings/{pos-discount-settings,settings-page}`, `pages/customers/*`, `customer.service/model`, `product-form.spec` + specs. Tests: `TestSuperAdmin`/`TestAuthHandler`/`RetailPosApiFactory`, `Pricing/*` (6 files), `Settings/PosSettingsAuthorizationHttpTests`, `Products/ProductImageProcessorSecurityTests`, updated seeds and expectations listed in §8.2/§8.7.

## 9. Phase 3 — Cash Session Management & Financial Reconciliation (3 Nov 2026)

**Status: implemented and verified on the isolated Test stack; STOPPED for owner review. Phase 4 and Loyalty are not started. Nothing is staged, committed or pushed. Migrations `…P21…` and `…P3…` are generated and rehearsed but NOT applied to Development (Dev is at 63 migrations, last P2).**

### 9.1 What was delivered
| Area | Result |
|---|---|
| Opening (D3/D4/D9) | Server-authoritative, idempotent open. Terminal resolved server-side. `BusinessDate` is a `date` derived from the Outlet/Business IANA zone (`Asia/Dhaka` default). `time_zone_id` and `duration_snapshot_hours` (Business setting, 1–20, default 20) are snapshotted on the session. Cutoff = `OpenedAt + snapshot`. Overdue is derived, never stored. Warnings at 2 h and 1 h. Client `businessDate` is no longer accepted. |
| Sale vs close serialization | Sale takes `FOR SHARE` on the session row in its own transaction and rechecks status and cutoff (again just before commit). Close takes `FOR UPDATE`, computes facts, conditional update `WHERE status='open'`. Lock order: session → sale/expense → inventory. DB triggers refuse sales or cash movements on a non-open session (`CASH_SESSION_CLOSED`) and make movements and close snapshots append-only (`CASH_SESSION_FACT_IMMUTABLE`). No physical commit-time guarantee is claimed beyond lock + trigger. |
| Expected cash | Opening + Σ net cash retained of all sales in the session + Σ cash movements (refund and drawer-expense payouts negative, reversals positive, corrections signed). Voided sales are no longer subtracted by the calculator; a void without a payout creates no outflow. Legacy payments without D14 evidence count the allocated amount. Immutable close snapshot with hash. |
| Refunds (D1) | `SaleCashRefundRecorder` records the physical cash payout once per source key on the paying terminal's open session. A partial refund of a cash sale needs an explicit `CashRefundAmount` (`CASH_REFUND_POLICY_UNDEFINED` otherwise). |
| Drawer expenses (D7) | `PaymentSource` (pos_drawer, petty_cash, …), `pay-from-drawer` and `reverse-drawer-payment`. Approval is not payout. Maker-checker: creator and submitter cannot approve; owner exception only with the Business setting `AllowOwnerExpenseSelfApproval`, owner status, and the exact (non-wildcard) `expenses.self_approve` permission. DB constraint `ck_expenses_maker_checker`. |
| Variance (D2) | Close creates a pending `cash_difference_approvals` row. Review requires reviewer ≠ closer. Posting is idempotent (journal key `cash-difference:{approvalId}`), uses the session date and `ClosedAt`, and records Pending/Posted/Failed with backoff; a closed accounting period gives Failed (no date substitution). |
| Handover and evidence | Handover records with denominations, confirmation by a second user, attachments upload/download with their own permissions. |
| Outbox | Deterministic-id events appended in the same transaction as the change (open, close, handover, variance, movements). Audit writes are best-effort after commit. |
| Permissions | `cash_sessions.view/open/close/handover.confirm/attachments.upload/attachments.download/variance.view/variance.approve/variance.retry_posting/expenses.pay`, `expenses.self_approve`. Registered in `PermissionCatalog`, seeder and the Angular role form. Roles with `sales.create` receive `cash_sessions.view/open/close` through the migration and seeder. |
| API | New `/api/cash-sessions` (list, open-context, detail, open, close, handover confirm, attachments, variances incl. retry and process-pending). `/api/pos/cash-sessions/*` also requires the `cash_sessions.*` policies. POS summary DTO dropped `cashIn`/`cashOut`. |
| Angular | Routes `cash-sessions`, `cash-sessions/open`, `cash-sessions/:id/close`, `cash-sessions/:id` plus menu item; `pos-screen` uses the server clock offset and the snapshotted duration and no longer sends `businessDate` or Cash In/Out. |

### 9.2 Migration
`20261103091638_PosV2P3CashSessionManagement` (hand-written SQL in the partial `PosV2P3CashSessionManagement.Custom.cs`). Converts `business_date` `timestamptz` → `date`, lower-cases status, backfills snapshots (existing open sessions get a 20 h snapshot, previously 15 h), creates the new tables, 4 triggers, constraints and the role grants. Rehearsed Up and Down on a restored copy of Development data on the Test server: 65 history rows after Up, all conversions verified, Down restored `timestamptz`. Review SQL: `backups/phase3/p21-p3-from-20261102181136.sql` (git-ignored). A Development dump taken before any P3 work exists in `backups/phase3/` (SHA-256 `C7D42FD1…FD5F`). Development was only read.

### 9.3 Verification (actual results)
| Check | Result |
|---|---|
| Unit tests | 705 passed |
| Full integration suite (Test stack) | 2,072 tests: 2,070 passed, 2 failed. Both failures were fixed and re-run individually (see below); a complete re-run of the whole suite after those two fixes was **not** repeated |
| Fix 1 | `RbacPermissionConsistencyTests` – new permissions were missing from `role-form.component.ts`; now passes |
| Fix 2 | `A_sequential_close_retry_returns_the_existing_result_without_new_facts` – close timestamp had 100 ns ticks while PostgreSQL stores µs; the handler now truncates `ClosedAt` to µs. Phase3 PG class re-run: pass |
| Phase 3 PG tests | concurrency (sale vs close, double close, double open, movement exactly-once), trigger backstops, timezone/business-date, cutoff, D1 refund; drawer-expense and maker-checker tests; HTTP security tests |
| Angular | 96 files, 1,281 tests passed; `ng build` OK (only the pre-existing `pos-screen` CSS budget warning) |
| Browser/visual (390/768/1366/1440/1920) | **Not performed** – unit specs only |

### 9.4 Not done / outstanding risks
1. **D10 asynchronous sale GL is NOT implemented.** `SaleCreationAccountingPoster.PostSaleAsync` still runs synchronously inside the sale transaction. Plan: `sale_financial_postings` intent row + outbox event + idempotent post-commit poster (key `sale:{id}`) with retry; needs a migration.
2. No scheduled background worker for variance GL posting. It is attempted after approval, via the retry endpoint and `POST variances/process-pending`. The HTTP-bound tenant context blocks a plain hosted service.
3. Not implemented: print/PDF close summary, expense-form searchable Payment Source selectors, daily-target/day-sales metrics (cards show "Not configured"/"Not available"), a separate non-immediate payout event for void/refund, line-level partial-refund cash policy.
4. Behaviour changes to be aware of: 20 h snapshot for existing open sessions; extra `cash_sessions.*` policies on POS cash-session routes; removed `cashIn`/`cashOut`.
5. Npgsql legacy timestamp mode (used by tests) can return `Local`-kind values; all cash-session instants are normalised through `UtcTime.Normalize` and EF converters.
6. Phase 2.1 and Phase 3 migrations still need explicit authorization to apply to Development.

### 9.5 Source control
Nothing staged or committed. `design-reference/` and `src/RetailPOS.API/system-settings.json` must never be staged. New/changed areas: Domain (`CashSessionEntities`, `UtcTime`, `CashSession`, `Expense`, `BusinessPosSettings`), Application (`Pos/*`, `Expenses/*`, `Sales/*`), Infrastructure (`Pos/*`, `Expenses/ExpenseDrawerStore`, `RetailPOSDbContext`, migration + partial), API (`CashSessionsController`, `ExpenseMakerCheckerPolicy`, `PermissionCatalog`, `Program.cs`, exception middleware, POS/Sales/Settings/AccountingExpenses controllers), Angular (`cash-session.service`, `pages/cash-sessions/*`, `pos.service`, `pos-screen`, `app.routes`, `menu.model`, `role-form`), tests (`UnitTests/Pos/*`, `IntegrationTests/Phase3/*`, `Phase3TestDoubles`, edited legacy cash/sale/expense tests).

### 9.6 Phase 4 readiness
Engineering-ready except the D10 decoupled sale GL (9.4-1), which should be decided before Phase 4. Owner actions: review this section, authorize applying `…P21…` and `…P3…` to Development, and decide on D10 and the background worker.

## 10. Phase 3.1 — D10 Asynchronous Accounting & Variance Worker (3 Nov 2026)

**Status: implemented and verified on the isolated Test stack; STOPPED for owner review. Phase 4 and Loyalty are not started. Nothing is staged, committed or pushed. Development was only read (still 63 migrations); P21, P3 and the new P31 migration are not applied there.**

### 10.1 Baseline (Workstream A)
| Check | Result |
|---|---|
| Working tree | Large uncommitted tree from earlier phases (≈1,100 entries). Left as found; Phase 3.1 edits are listed in §10.13 |
| Build | `dotnet build RetailPOS.slnx`: 0 errors (only the known `SixLabors.ImageSharp` advisories) |
| Full integration suite on the **pre-3.1** code (Test stack) | 2,072 tests: **2,071 passed, 1 failed** — `MobileStockTakeSubmissionPostgresHttpTests.OutboxPublicationRecoversAfterBrokerOutage_WithoutChangingJobState` (RabbitMQ broker-recovery timing under full-suite load; it passes when run alone). Both §9.3 fixes (`RbacPermissionConsistencyTests`, sequential close retry) pass in the full run |
| Migrations / snapshot | 65 migration classes (Dev 63 + P21 + P3). Scaffolding P31 produced only the intended changes, so the snapshot matched the model |
| Development (read-only, `BEGIN READ ONLY`) | 63 applied, last `20261102181136_PosV2P2TenancyTaxSettingsInvoiceNumbering`; `cash_difference_approvals` and `sale_financial_postings` absent |

Transaction boundaries found before the change: **sale** — one transaction containing the sale, stock, invoice number and a synchronous `CreateOrGetSystemJournalDraft` + `PostJournalEntry` (`SaleCreationAccountingPoster`), so an accounting error rolled the sale back; **void/refund** — the same with `SaleReversalAccountingPoster`; **variance** — posted in its own transaction after approval or by the retry/process-pending endpoints, which depended on the HTTP tenant. `ManualJournalDraftStore` joins an ambient transaction, which the new design relies on.

### 10.2 What was delivered (Workstream B)
| Area | Result |
|---|---|
| Durable intent | New `sale_financial_postings` (LLD §20.3): one row per sale (`kind = sale`) and at most one per sale reversal (`kind = reversal`, `void`/`refund`); immutable JSON snapshot + SHA-256; stable keys `sale:{id}:posting:v1` / `sale:{id}:reversal:v1`; status, attempts, last error code/text, next/last attempt, journal id, posted time |
| Sale transaction | `CreateSaleCommandHandler` no longer posts GL. Sale, items, payments, stock, stock ledger, the intent and the `pos.sale.financial-posting.requested` outbox event commit together; the worker is signalled after commit. A failure before commit leaves no intent and no event |
| Snapshot | Gross, discount, VAT, COGS, total, `TaxInclusive`, payment allocations (id, method, amount, account if already known), business date, transaction instant, outlet/terminal/session, actor. The amount rules are the pre-D10 ones, moved verbatim into `SaleFinancialIntentFactory` |
| Posting | `ProcessSaleFinancialPostingCommand`: lock row → verify Business and snapshot hash → `SaleFinancialJournalPoster` builds the lines from the snapshot and calls the **existing** `CreateOrGetSystemJournalDraftCommand` + `PostJournalEntryCommand` (no second accounting engine) → record `posted` + journal id, all in **one** transaction. The journal keeps the pre-D10 reference (`Sale`/`SaleRefund` + sale id), so a pre-D10 journal is reused, never duplicated |
| Void / refund | Handlers write a reversal intent instead of posting. The reversal waits until the sale journal is posted (it needs the payment accounts that journal records), then posts once. Return/refund quantities, cash-refund rules and the reversal arithmetic are unchanged. A sale with no lines has nothing to reverse |
| Failure handling | Any failure rolls the attempt back and is recorded in its own transaction (`failed`, attempts, code, message incl. root cause, next attempt). Missing configuration, closed/locked periods, infrastructure errors and rejected journals all stay retryable; dates are never substituted |
| Operations | `GET api/financial-postings/sales?status=` and `GET api/financial-postings/summary` (`accounting.journals.view`): counts, oldest unposted intent, variances due, worker heartbeat. `POST api/financial-postings/sales/{id}/retry` (`accounting.manual_journals.post`). Structured logs per outcome |

### 10.3 Posting state machine
```
                 commit of sale / void / refund
                              │
                              ▼
   ┌──────────────────────► pending ───────────── attempt succeeds ─────────────► posted   (final; DB trigger)
   │                          │   ▲                                                  ▲
   │       attempt fails      │   │ reversal whose sale journal is not posted yet:   │
   │  (rolled back, recorded) │   │ stays pending, code awaiting_sale_posting,       │
   │                          ▼   │ re-checked after 30 s, not counted as attempt    │
   └── due / operator retry ─ failed ──────────────── attempt succeeds ─────────────┘
         attempts+1, next = now + min(30 s × 2^(attempts−1), 3600 s)
```
Error codes (labels only — every class retries): `accounting_configuration`, `accounting_period`, `transient`, `posting_rejected`, `snapshot_mismatch`, `awaiting_sale_posting`. There is no "abandoned" state: an unresolved posting keeps retrying at most hourly and stays visible in the summary.

### 10.4 Transaction boundary changes
| Operation | Before | After |
|---|---|---|
| Sale | Sale + stock + **journal create/post** in one tx; GL error ⇒ no sale | Sale + stock + intent + outbox in one tx; GL in a later, separate tx |
| Void / refund | Reversal journal inside the void/refund tx | Reversal intent + outbox inside it; journal later |
| Posting | — | Row lock + journal create/post + `posted` + `changed` event in one tx; failure record in a second tx |
| Variance | Own tx after approval / HTTP retry | Unchanged, plus the worker (`SKIP LOCKED`) and DB backstops |
| Close | No GL | Unchanged — closing never depends on accounting |

### 10.5 Worker and retry design (Workstreams B and C)
- Two `BackgroundService`s (no new scheduler dependency): sale postings (poll 5 s, batch 25, woken early by an in-process signal after each commit) and approved variances (poll 60 s, batch 20). Settings `FinancialPosting:Sales:*` and `FinancialPosting:CashDifferences:*` in `appsettings.json`, validated at startup. Not registered in the `Testing` environment; tests drive the same batch runners directly.
- **Claim / lease:** the row lock `FOR UPDATE SKIP LOCKED` taken inside the posting transaction. Another instance skips the row; an operator retry waits for it and then sees `posted`. Because claim, journal and status are one transaction, a crash or shutdown rolls everything back and PostgreSQL releases the lock with the connection — no time-based lease and no stuck rows. A due row is listed again on the next pass, including after a restart.
- **Tenancy without an HTTP user:** discovery reads only (id, Business, outlet, actor) across Businesses. Each item runs in a new DI scope whose `IAuditContext` is `Source = Integration` with that row's Business and actor — the path `IntegrationJobBusinessScope` already honours for the Stock Count worker. The handler refuses a row whose Business differs from the scope (`UnauthorizedAccessException`, nothing recorded), and journals take the Business from the snapshot.
- **Variance:** discovery lists only `status = approved` with a reviewer and `posting_status ∈ {pending, failed}`; `PostCashDifferenceCommand` re-checks approval under the row lock. New DB constraints make posting an unapproved difference, or "posted" without a journal, impossible. The worker's journal actor is the approving reviewer. `POST variances/{id}/post` and `variances/process-pending` are preserved.
- Shutdown: cancellation propagates out of the batch, the open posting rolls back, the worker exits its loop.

### 10.6 Outbox / inbox integration
Both events use the existing `IntegrationOutbox.AppendSourceChangeAsync` (deterministic message ids; Business checked against the audit context): `pos.sale.financial-posting.requested` in the source transaction and `pos.sale.financial-posting.changed` on every transition. The existing `OutboxDispatcher` publishes them when `DurableDelivery` is enabled. Consumption of the intent is PostgreSQL-authoritative, like the Stock Count adjustment sweeper: exactly-once comes from the row lock plus the journal reference key, so a broker consumer and an inbox table are not needed for correctness, and `DurableDelivery` requires AMQPS, which has no accepted local topology yet (LOCAL_INFRASTRUCTURE §5.6). The events are available to reporting/alert consumers.

### 10.7 Migration `20261103111708_PosV2P31SaleFinancialPostings`
Additive: table `sale_financial_postings` (FKs to businesses, sales, journal_entries, all `RESTRICT`; unique `(business_id, source_key)` and `(sale_id, kind)`; CHECKs on kind, status, reversal type, attempts and "posted ⇔ journal + posted time"); trigger `trg_sale_financial_postings_guard` (no delete, immutable identity/snapshot, `posted` final — hand-written in `PosV2P31SaleFinancialPostings.Custom.cs`); CHECKs `ck_cash_difference_approvals_posting_requires_approval` and `ck_cash_difference_approvals_posted_journal`. No backfill: pre-D10 sales were posted synchronously. Rehearsed on disposable Test-server databases only: full chain from empty (`MigrationChainValidationTests`) and Up → Down to P3 → Up with schema checks (`The_P31_migration_rolls_back_to_P3_and_reapplies_cleanly`). Review SQL, generated offline and git-ignored: `backups/phase3_1/p21-p3-p31-from-20261102181136.sql` (idempotent; SHA-256 `4abad339…657a`), `p31-only-up.sql` (`5c6289c6…04`), `p31-only-down.sql` (`18123fa8…f5`).

### 10.8 Verification (actual results)
| Check | Result |
|---|---|
| Unit tests | **725 passed** (705 + 20 new in `UnitTests/Sales/SaleFinancialPostingTests`: intent amounts/keys/hash, backoff, failure labels, state machine incl. claim, tenancy, deferral, tamper; variance skip-locked and no-journal guard) |
| Phase 3.1 PostgreSQL suite (`Phase3/SaleFinancialPostingPostgresTests`, 13 tests, real DI, no HTTP user) | **13 passed** |
| Full integration suite after 3.1 (Test stack) | 2,085 tests: **2,084 passed, 1 failed** — `MobileStockTakeSubmissionPostgresHttpTests.WorkerCrashRecovery_ReclaimsExpiredLeaseAndCompletesOnce`. This class is intermittent independently of Phase 3.1 (it touches no sale/posting code; the pre-3.1 baseline failed a different test in it): rerun alone it failed a third, different test once, then passed 15/15 twice. Not claimed as a full green run; needs a separate flakiness fix |
| Angular | Not re-run: Phase 3.1 changed no frontend file and added no permission. Last result (§9.3): 1,281 passed, build OK |
| Browser/visual | Not performed |

Mandatory scenarios → tests: sale commits while accounting is down; stock deducted once; journal eventually posted; a second pass/retry does not duplicate (`Sale_commits_while_accounting_is_unavailable…`) · concurrent workers, two instances, waiting operator retry (`Concurrent_workers_cannot_both_post…`) · failed posting recoverable after a configuration fix (`Missing_accounting_configuration…`) · closed period not re-dated; posts on the original dates once reopened (`A_closed_period_fails_without_redating…`) · uncommitted sale leaves no intent, event or stock change (`An_uncommitted_sale_leaves_no_posting_intent…`, plus relational `CreateAsync_RollsBackSaleStockLedgerPostingIntentAndOutbox_WhenRequiredAuditFails`) · sale ↔ snapshot ↔ journal reconcile (`AssertSaleJournalReconcilesAsync`) · reversal waits for the sale journal and posts once (`A_void_reversal_waits…`) · DB immutability (`The_intent_snapshot_is_immutable…`) · cancellation mid-posting and restart recover all work (`Cancellation_mid_posting_and_an_instance_restart…`) · per-Business confinement (`The_worker_confines_each_posting…`) · variance cannot post before approval, incl. the DB constraint (`An_unapproved_variance_can_never_be_posted`) · two variance workers without HTTP context post once (`Two_variance_workers…`) · closing succeeds while the period is locked; the variance retries until reopened (`Closing_succeeds_while_accounting_is_closed…`).

**Cash Session regression:** all Phase 3 PostgreSQL, security and drawer-expense tests, the Sales suites and `MigrationChainValidationTests` passed in the full run; closing remains independent of accounting (also asserted with a locked period in `Closing_succeeds_while_accounting_is_closed…`).

Defects found by the PostgreSQL tests and fixed (InMemory tests did not show them): `array.Contains` inside an EF query binds to the C# 14 `ReadOnlySpan` overload and fails at runtime (the store now uses a `List`); a void of a sale with no lines threw in the intent factory (now skipped explicitly).

**Behaviour changes to be aware of:** checkout, void and refund no longer fail because of accounting configuration or periods — the posting fails visibly instead; sale/void/refund audit events carry `journalEntryId = null` and new summaries ("Sale completed", "…accounting reversal queued"); a variance journal poster returning no journal is now `failed`, never `posted`; the rounding line only balances cent-level payment-allocation rounding, on the correct side (before D10 it was added on the same side as the gap, so every imbalanced journal was rejected anyway).

### 10.9 Findings that need an owner / accounting decision (not implemented, not invented)
1. **Tax-inclusive VAT sale journals do not balance.** The pre-D10 composition credits `Sales` with the VAT-inclusive gross and credits `VAT payable` again; with inclusive pricing the journal is short by the VAT. Before D10 such a checkout failed (when accounting was wired); now the sale commits and the posting fails as `posting_rejected`. Recommended fix: credit revenue net of VAT when `TaxInclusive`; the snapshot already carries the flag, so failed postings post on retry after the fix. **Blocks enabling inclusive VAT pricing in production.** (Found by analysis of `SaleCheckoutCalculator` and the journal composition; not exercised by a test.)
2. **VAT-bearing void/refund reversals do not balance.** The pre-D10 reversal credits payments with the ex-VAT amount but debits VAT payable, so the gap equals the refunded VAT (previously rejected too, i.e. such voids/refunds failed when accounting was wired). This is Returns/Exchange SRD territory (tax reversal semantics); now the void/refund commits and the reversal stays `failed` until a rule is approved.
3. Reversal journals keep the **original sale's** posting and business dates (pre-D10 behaviour; consistent with LLD §21.3 "never silent dateshift"). If the original period is closed, the reversal waits for authorised intervention. Decide whether a void/refund should instead post on the void date.
4. Payment posting accounts are resolved when the journal posts (seconds after the sale; longer during an outage), so a mapping change in between applies. Snapshotting accounts at checkout would require accounting at checkout, which D10 removes.
5. Monitoring/retry reuse `accounting.journals.view` and `accounting.manual_journals.post`. A dedicated `sales.financial_postings.*` permission would need the catalogue, seeder, role form and a grant migration.
6. Alerts are pull-based (summary endpoint, logs, outbox events); no push notification channel was added.

### 10.10 Remaining Phase 3 items (Workstream E)
| Item | Classification |
|---|---|
| Print/PDF close summary | Suitable for Phase 4 (immutable close snapshot + hash already exist) |
| Expense Payment Source searchable selectors | Suitable for Phase 4 (the server already enforces `PaymentSource`) |
| Daily Sales card | Suitable for Phase 4 (reporting projection, FR-REPORT) |
| Daily Target card | Requires a business decision (no target model or source defined) |
| Separate event for non-immediate void/refund payout | Requires a business decision (FR-VR-01: when payout is deferred, who authorises it, which session). The GL reversal is already separate from the cash payout |
| Line-level partial-refund cash policy | Requires a business decision / Returns-Exchange SRD (`CASH_REFUND_POLICY_UNDEFINED` guard stays) |
| Browser layout verification (390–1920) | Suitable for Phase 4 as a release gate; not a financial-correctness item |
| §10.9 items 1–2 | **Required for financial correctness** before VAT-bearing sales/reversals go live, but each needs the decision above |

### 10.11 Development migration status
Development: **63 applied** (last `…P2TenancyTaxSettingsInvoiceNumbering`), unchanged by this phase. Pending, in this exact order:
1. `20261102212702_PosV2P21PricingRuleTenancy`
2. `20261103091638_PosV2P3CashSessionManagement`
3. `20261103111708_PosV2P31SaleFinancialPostings`

Apply only with explicit authorisation, after a verified backup, as one reviewed script (`backups/phase3_1/p21-p3-p31-from-20261102181136.sql`). An API built from this source expects all three; the workers start automatically unless `FinancialPosting:*:WorkerEnabled` is false.

### 10.12 Source control
Nothing staged, committed or pushed. `design-reference/`, `src/RetailPOS.API/system-settings.json`, `backups/` and build outputs must not be staged.

### 10.13 Git change manifest (Phase 3.1 only)
New: `src/RetailPOS.Domain/Entities/SaleFinancialPosting.cs`; `src/RetailPOS.Application/Sales/FinancialPostings/{SaleFinancialSnapshots,ISaleFinancialPostingServices,SaleFinancialPostingCommands}.cs`; `src/RetailPOS.Infrastructure/Sales/{SaleFinancialPostingModelConfiguration,SaleFinancialPostingStore,SaleFinancialPostingWriter}.cs`; `src/RetailPOS.Infrastructure/Data/Migrations/{20261103111708_PosV2P31SaleFinancialPostings.cs,20261103111708_PosV2P31SaleFinancialPostings.Designer.cs,PosV2P31SaleFinancialPostings.Custom.cs}`; `src/RetailPOS.API/Services/{SaleFinancialJournalPoster,FinancialPostingHostedServices}.cs`; `src/RetailPOS.API/Controllers/FinancialPostingsController.cs`; `tests/RetailPOS.UnitTests/Sales/SaleFinancialPostingTests.cs`; `tests/RetailPOS.IntegrationTests/Phase3/SaleFinancialPostingPostgresTests.cs`.

Modified: Application `Sales/Commands/{CreateSale,VoidSale,RefundSale}Command.cs`, `Sales/Abstractions/{ISaleCreationServices,ISaleVoidServices}.cs` (old poster interfaces removed), `Pos/Abstractions/ICashDifferenceStore.cs`, `Pos/Commands/CashDifferenceCommands.cs`; Infrastructure `Pos/{CashDifferenceStore,CashSessionModelConfiguration}.cs`, `Data/RetailPOSDbContext.cs`, `Data/Migrations/RetailPOSDbContextModelSnapshot.cs`; API `Services/SaleCreationAdapters.cs` (synchronous posters removed), `Program.cs`, `appsettings.json`; tests `Sales/{SaleCreationCommandTestFactory,SaleServiceAccountingJournalTests,SaleServiceVatAndExchangeAccountingTests,SaleCreationRelationalBaselineTests,SaleCreationPostgresConcurrencyTests,SaleVoidCommandTests}.cs`, `Accounting/P306FoundationTests.cs`, `MigrationChainValidationTests.cs`; this document.

### 10.14 Phase 4 readiness
Engineering-ready from the D10/worker side. Before Phase 4 the owner should: review this section; decide §10.9 items 1–3 (they decide whether VAT-bearing sales and reversals can post at all); authorise applying P21 → P3 → P31 to Development; and decide whether the workers run on every API instance (safe by design) or on a dedicated one.

## 11. Phase 3.2 — Financial Accounting Corrections & Hybrid Worker Deployment (3 Nov 2026)

**Status: implemented and verified on the isolated Test stack; STOPPED for owner review. Phase 4 and Loyalty are not started. Nothing is staged, committed or pushed. Development was only read (still 63 migrations); P21, P3, P31 and the new P32 are not applied there.**

### 11.1 Decision register
| ID | Approved decision | Where implemented |
|---|---|---|
| P3.2-D01 | VAT-correct GL: revenue net of VAT, VAT payable credited on sales; supported void/refund reverse revenue and VAT exactly; snapshots only; debits = credits; D10 preserved | `SaleFinancialIntentFactory` (v2 snapshots), `SaleFinancialJournalPoster` |
| P3.2-D02 | Reversal GL dated by the server-recorded reversal time, in the effective Outlet/Business zone; never sale date, worker or retry time; closed period stays failed | `SaleFinancialPostingWriter` (time + zone), reversal snapshot `RequestedAtUtc`/`AccountingDate`/`TimeZoneId` |
| P3.2-D03 | `sales.financial_postings.view` / `.retry`; `cash_sessions.variance.retry_posting` kept; Business-scoped, audited; worker uses trusted integration context | `PermissionCatalog`, `FinancialPostingsController`, `DbSeeder`, Role Edit, P32 grants, `RetrySaleFinancialPostingCommand` + `SaleFinancialPostingAuditWriter` |
| P3.2-D04 | Implement and verify first; prepare consolidated Development deployment; do **not** apply | §11.9 (rehearsed on a restored Development copy; nothing applied) |
| P3.2-D05 | Same-process API + workers, or a dedicated worker process; SKIP LOCKED, Business scope, idempotency, recovery, heartbeat/backlog monitoring | `FinancialPosting:HostRole`, `FinancialPostingWorkerPlan`, `financial_posting_worker_heartbeats` |

### 11.2 Baseline and characterization (Workstream A)
Where the arithmetic became unbalanced (pre-3.2 composition, now replaced):
- **Sale:** `Cr Sales = Σ(qty × unit price)` and `Cr VAT = sale.Tax`. With inclusive pricing the unit price already contains the VAT, so VAT was credited twice.
- **Reversal:** `refund = Σ(line subtotal share)` was credited to payments **ex-VAT** while VAT payable was debited, so the gap equalled the refunded VAT. The old "rounding difference" line was added on the same side as the gap, so these journals were always rejected.

Characterization tests (`Sales/SaleFinancialJournalCharacterizationTests`, real `SaleCheckoutCalculator` → intent → poster) **failed on the pre-3.2 code exactly as described**: inclusive sale of 1,000.00 incl. 10 % — debits 1,040.00 vs credits 1,130.91 (gap 90.91 = VAT); exclusive void of 2 × 100.00 + 15 % — debits 310.00 vs credits 280.00 (gap 30.00 = VAT). They pass after the fix and remain as regression tests. The D10 boundary is unchanged: no GL in the sale/void/refund transaction.

Authoritative evidence used: each `sale_items` row carries the checkout's `taxable_amount` (net of VAT after line, invoice and pricing-rule discounts) and `vat_amount`, allocated by largest remainder so that Σ(taxable + VAT) = payable exactly (`SaleCheckoutCalculator`). Nothing is recomputed from current product/tax settings.

### 11.3 Exact journal formulas
**Sale** (from the v2 snapshot; v1 snapshots post with the same totals formula):
| Line | Amount |
|---|---|
| Dr payment/clearing account (per payment, existing slot policy) | allocated amount (Σ = payable; change handed back is never revenue or drawer cash) |
| Dr Sales discount | line discounts + invoice discount (unchanged presentation) |
| Cr Sales | net revenue + discounts, where **net revenue = payable − VAT** (= Σ line `taxable_amount`, cross-checked) |
| Cr VAT payable | sale VAT (= Σ line `vat_amount`); no line when 0 (zero-rated, exempt, no VAT) |
| Dr COGS / Cr Inventory | Σ historical unit cost × qty (unchanged) |

Debits = payable + discounts + cost = credits, exactly; there is **no balancing line**. Exclusive sales post exactly what they posted before (net + discounts = gross). Preconditions, otherwise a stable failure: Σ payments = payable (`payment_allocation_mismatch`); complete line allocations reconcile to net and VAT (`allocation_evidence_missing`).

**Reversal** (void or the single refund the existing flow allows per sale):
| Line | Amount |
|---|---|
| Dr Sales return (existing `sales_return` slot) | Σ returned share of line `taxable_amount` |
| Dr VAT payable | Σ returned share of line `vat_amount` |
| Cr original payment accounts | net + VAT, split over the original payments in proportion to their allocation, in whole cents (largest remainder; parts sum exactly) |
| Dr Inventory / Cr COGS | historical unit cost × returned qty |

Returned share: the whole line returns exactly what was recorded; a partial quantity takes `round(recorded × qty / soldQty)`. Because a sale can be refunded or voided only once (unchanged policy), the reversed amounts can never exceed what was recorded. If a returned line or the sale lacks recorded net/VAT, or the line or payment allocations do not reconcile to the payable, the snapshot records `MissingEvidence` and the posting fails with `allocation_evidence_missing`. The void/refund itself still commits; nothing is guessed. The physical cash payout stays the separate `cash_session_movements` fact (D1) and is independent of the GL entitlement.

### 11.4 Worked examples (all verified on PostgreSQL)
| Case | Journal |
|---|---|
| Inclusive 10 %: 10 × 100.00, cash 1,000.00 | Dr Cash 1,000.00 · Cr Sales 909.09 · Cr VAT 90.91 · Dr COGS 700.00 / Cr Inventory 700.00 |
| Full void of the above | Dr Sales return 909.09 · Dr VAT 90.91 · Cr Cash 1,000.00 · Dr Inventory 700.00 / Cr COGS 700.00; drawer: one `cash_refund` −1,000.00 |
| Exclusive 15 %: 2 × 100.00, card 230.00, full refund | Dr Sales return 200.00 · Dr VAT 30.00 · Cr card clearing/bank 230.00 |
| Exclusive mixed 15 %/5 %, invoice discount 20.00, card 200.00 + cash (tendered with change) | Cr VAT = checkout VAT; Dr discount 20.00; Cr Sales − Dr discount = Σ net; Dr card 200.00, Dr cash = allocated cash (not tendered) |
| Partial refund: 1 of 3 units at 15 %, paid card + mobile | Dr Sales return 100.00 · Dr VAT 15.00 · Cr card + mobile 115.00 split by original allocation |
| Zero-rated + exempt, line discount 10.00 | no VAT line; Dr discount 10.00; Cr Sales 200.00; Dr Cash 190.00 |

### 11.5 Accounting-date persistence model (P3.2-D02)
- `SaleFinancialPostingWriter` takes the server time **inside the void/refund transaction** and resolves the zone with the approved D9 rule (`CashSessionTime.ResolveEffectiveTimeZoneId`: Outlet override → Business `default_time_zone_id` → `Asia/Dhaka`).
- The reversal snapshot (immutable, hashed, trigger-protected) stores `RequestedAtUtc`, `TimeZoneId` and `AccountingDate`, alongside the original sale's `TransactionAtUtc`, `BusinessDate`, sale id and invoice number. No new column was needed.
- Reversal journal: `TransactionDate = RequestedAtUtc`, `PostingDate = BusinessDate = AccountingDate`; the description names the original sale business date. The original sale and its journal are never touched.
- A closed or locked reversal period gives `accounting_period` and is retried unchanged. The date can only come from the snapshot, so retries days later post on the same date once the period is reopened.
- **Legacy v1 reversal intents** (Phase 3.1 schema) have no zone or accounting date and no VAT allocation. The time *could* be read from `RequestedAtUtc`, but the zone in force at the time cannot be proven, so they are **quarantined** (`snapshot_requires_review`) and never posted. None exist outside disposable test databases, because P31 was never applied to Development.

### 11.6 Permission changes (P3.2-D03)
- New codes in `PermissionCatalog` (policies are generated from it), Role Edit ("Sales Management" group, with labels/descriptions) and the seeder templates: **Admin** and **Accounting Manager** get view + retry. Cashier, Salesman, Outlet Manager and Manager templates get neither.
- `GET api/financial-postings/sales`, `GET …/summary` → `sales.financial_postings.view`; `POST …/sales/{id}/retry` → `sales.financial_postings.retry`. `cash_sessions.variance.retry_posting` is unchanged.
- Migration P32 grants `.view` to existing roles holding `accounting.journals.view` or `accounting.manual_journals.post`, and `.retry` to roles holding `accounting.manual_journals.post` (the Phase 3.1 equivalents). Rehearsal on the Development copy: Admin → view + retry; Accounting Manager → view only; Cashier/OutletManager/Salesman/Manager/Stock Manager/User → none; wildcard roles unchanged.
- The retry takes only the posting id: there is no way to pass amounts, dates or a snapshot. It never creates a posting, serialises on the row lock, and returns the same journal for concurrent retries. Another Business's posting is reported as not found. Each retry writes an audit event (`SaleFinancialPosting`, metadata: Business, posting id, source key, previous status, request time, outcome, new status; actor from the request) — verified in `audit_events`.
- Workers never use permissions: each item runs with an Integration-sourced audit context bound to the posting's Business (unchanged from 3.1).

### 11.7 Worker deployment architecture (P3.2-D05)
One build, two roles, selected by `FinancialPosting:HostRole`:

| Mode | Configuration | Runs |
|---|---|---|
| Same-process (Development, single instance) | `HostRole = Api` (default), `FinancialPosting:Sales:WorkerEnabled` / `CashDifferences:WorkerEnabled` | API + the enabled financial workers |
| Dedicated worker | API instances: `HostRole = Api`, both `WorkerEnabled = false`. Worker: `HostRole = Worker`, the workers to run enabled | worker process: only the two financial workers + `GET /health/financial-posting` (200/503); no controllers; every other background service of the app is removed so nothing runs twice |

A dedicated executable project was not created on purpose. The posting engine's DI graph (journal store, account resolution, tenancy adapters, outbox) lives in the API composition root, so a separate host would have to duplicate ~1,100 lines of registrations — the source divergence D05 forbids. The same binary started with a different role has none. `HostRole = Worker` with no worker enabled, or an unknown role, fails at startup.

Durable heartbeat `financial_posting_worker_heartbeats` (worker, instance, host, role, last start/completion, last batch, consecutive failures, last error *type*): upserted after every pass outside any posting transaction. `GET api/financial-postings/summary` returns backlog (pending, failed, awaiting, oldest unposted, variances due) plus each instance's liveness and a `stale` flag (no completed pass for 5 min). It omits per-batch volumes, which span Businesses. PostgreSQL stays authoritative; RabbitMQ is not needed for recovery.

Docker Compose (documentation only; no Compose file or infrastructure was changed):
```yaml
# Mode 1 – same process (current default)
api:
  image: retailpos-api
  environment:
    FinancialPosting__HostRole: Api
    FinancialPosting__Sales__WorkerEnabled: "true"
    FinancialPosting__CashDifferences__WorkerEnabled: "true"

# Mode 2 – dedicated worker (same image)
api:
  image: retailpos-api
  environment:
    FinancialPosting__HostRole: Api
    FinancialPosting__Sales__WorkerEnabled: "false"
    FinancialPosting__CashDifferences__WorkerEnabled: "false"
financial-worker:
  image: retailpos-api
  environment:
    FinancialPosting__HostRole: Worker
    FinancialPosting__Sales__WorkerEnabled: "true"
    FinancialPosting__CashDifferences__WorkerEnabled: "true"
    ConnectionStrings__DefaultConnection: ${same database as the API}
  healthcheck:
    test: ["CMD", "curl", "-fsS", "http://localhost:8080/health/financial-posting"]
  stop_grace_period: 30s   # lets an in-flight posting finish or roll back
  deploy: { replicas: 2 }  # safe: SKIP LOCKED claims
```

### 11.8 Transaction and concurrency guarantees
Unchanged from 3.1 and re-verified: sale/void/refund commit facts + intent + outbox atomically, with no GL. Each posting attempt runs claim (`FOR UPDATE SKIP LOCKED`), journal create/post and `posted` in one transaction, and failures are recorded in a second one. The journal reference key and `(sale_id, kind)` uniqueness give exactly-once. Cancellation or a crash rolls back, and the lock is released at once (verified: another worker claims the row immediately). Two dedicated worker processes, three concurrent reversal retries and worker + manual retry each produced exactly one journal.

### 11.9 Migrations and Development deployment preparation (P3.2-D04)
New: `20261103131039_PosV2P32FinancialPostingOperations` — additive table `financial_posting_worker_heartbeats` (PK worker+instance, role CHECK, index) plus the permission grants above (`.Custom.cs`, symmetric revoke in `Down`). `has-pending-model-changes`: none. No earlier migration was modified.

Pending for Development, in this order: `…P21PricingRuleTenancy` → `…P3CashSessionManagement` → `…P31SaleFinancialPostings` → `…P32FinancialPostingOperations`.

| Artifact (all in git-ignored `backups/phase3_2/`) | SHA-256 |
|---|---|
| `retailpos_db-dev-pre-p21-p32-20261103T133825Z.dump` — read-only `pg_dump -Fc` of Development, 1.1 MB, 184 table-data entries listed by `pg_restore --list` | `67d25d6f…5b94` |
| `dev-pending-p21-p3-p31-p32-idempotent.sql` — the deployment script | `33319930…4c43` |
| `dev-rollback-to-p2.sql` — rollback to the current Development head | `712ff229…c8e8` |
| `p32-only-up.sql` | `1f656b7c…e7b6` |
| `validate-pending-chain.sql` — read-only before/after validation queries | — |

**Rehearsal on a restored copy of Development data** (Test server, scratch DB `p32_rehearsal_devcopy`, `pg_restore --no-owner --no-privileges`, dropped afterwards). Forward script: exit 0, no errors; 63 → 67 migrations; rows (sales 3, sale_items 7, journal_entries 2, journal_entry_lines 4, inventories 53, stock_ledgers 12, roles 10, users 9, expenses 5) and sums (sales 1,929.91; journal debits = credits = 4,620.00; stock 3,289 units) **unchanged**; all 5 triggers and 11 constraints present; `cash_sessions.business_date` → `date`; `sale_financial_postings` empty (no evidence fabricated); grants as in §11.6. Re-running the idempotent script: exit 0, still 67. Rollback script: exit 0; back to 63 with identical rows, sums and grants, and all new objects removed. The disposable-schema Up → Down → Up test also passes.

**Impact on Development data:** none of the 3 existing sales (all pre-D10, journals already posted synchronously) gets an intent, and no backfill is done. All 7 of their lines lack `taxable_amount`/`vat_amount` and they have no `sale_payments` rows. A void after migration commits normally, and its reversal posting fails visibly with `allocation_evidence_missing` (by design; Development data is disposable).

**Deployment procedure for when it is authorised:** stop API/workers → take and verify a fresh `pg_dump -Fc` (`pg_restore --list`) → run `validate-pending-chain.sql` (save output) → apply `dev-pending-…-idempotent.sql` with `psql -v ON_ERROR_STOP=1` → re-run the validation and compare (counts/sums equal, objects present, grants as expected) → start the API (`HostRole=Api`) → check `GET api/financial-postings/summary`. Rollback: `dev-rollback-to-p2.sql`, or restore the dump.

### 11.10 Verification (actual results)
| Check | Result |
|---|---|
| Unit tests | **733 passed** (725 + factory share/accounting-date/allocation/evidence tests) |
| Characterization (pre-3.2 code) | **2 failed as expected** (inclusive sale gap 90.91; VAT void gap 30.00) → pass after the fix |
| Phase 3.1 + 3.2 PostgreSQL suite (`SaleFinancialPostingPostgresTests`, partial `Phase32FinancialAccountingPostgresTests`) | **29 passed** (13 Phase 3.1 + 16 Phase 3.2), plus 2 characterization |
| Full integration suite after 3.2 (Test stack) | **2,103 passed, 0 failed, 0 skipped** (40 min 45 s; one run, 3 Nov 2026 13:33–14:14 local; trx `phase32-integration.trx`). Phase 3.1 baseline was 2,084/2,085 with one intermittent `MobileStockTake` failure; +18 new Phase 3.2 tests (16 + 2 characterization) |
| `MobileStockTakeSubmissionPostgresHttpTests` alone after the fix | 15/15 passed, 3 consecutive runs |
| Angular | **96 files, 1,281 tests passed**; `ng build` OK (only the existing `pos-screen` CSS budget warning) |
| Migration chain / rehearsal | `MigrationChainValidationTests` (latest = P32), P31 Up/Down/Up, restored-Development forward + rollback (§11.9) |

Mandatory scenarios → tests (Phase 3.2): inclusive VAT · mixed rates + invoice discount + split payment with change · zero-rated/exempt + line discount · snapshot replay after tax settings change · full void incl. VAT with separate cash payout · full refund incl. VAT · partial refund across two payments + second refund refused · missing evidence fails explicitly · reversal on its own date with the sale period closed (midnight across Asia/Dhaka) · closed reversal period, three retries, same date after reopening · concurrent reversal retries · HTTP permissions (no permission 403, view-only cannot retry, retry-only cannot post manual journals, other Business sees nothing/404, retry audited) · dedicated worker processes ×2 post once with durable heartbeat and API-visible liveness · worker plan by role/flags · unavailable database → worker keeps running and reports 503 · cancellation releases the row lock. Regression: Phase 3 cash-session (D1/D2/D7), D10 atomicity, D13 numbering and the migration chain run in the full suite.

`Concurrent workers / restart / transient failure / retry` from Phase 3.1 still pass with the v2 snapshots.

### 11.11 Flaky test investigation (Workstream H)
**Root cause (verified):** `MobileStockTakeSubmissionPostgresHttpTests` wrote due times with `DateTime.UtcNow.AddSeconds(-1)` inside `ExecuteUpdateAsync`. EF translates that to PostgreSQL `now()` (proved with a throwaway diagnostic: the stored value equalled the transaction's start time exactly, 0.000 s, despite a 3 s client delay). The processors and the outbox dispatcher compare against the **application** clock, so whenever the Docker VM clock ran ahead of the host by more than the 1 s margin, the row was not yet due ("job not in the recoverable set", "outbox Pending instead of Published"). A second shared-state cause affected the outbox test: the class shares one database, and the test made *every* accumulated outbox row due with one identical timestamp. Past the dispatcher's batch of 20, whether its own message was attempted depended on GUID order.

**Fix (test harness only; assertions, timeouts and production code unchanged):** the five timestamps now come from a client-evaluated `ClientUtcNow()`. The outbox test defers other tests' pending messages and makes only its own due. The class passed 3/3 in isolation after the fix, and the full-suite result is in §11.10. The same `DateTime.UtcNow`-in-`ExecuteUpdate` pattern may exist in other suites; it was not changed here. The Phase 3.1 tests use a 1-minute margin with the database clock.

### 11.12 Git change manifest (Phase 3.2 only)
New: `src/RetailPOS.Domain/Entities/FinancialPostingWorkerHeartbeat.cs`; `src/RetailPOS.Infrastructure/Sales/{FinancialPostingHeartbeatStore,SaleFinancialPostingAuditWriter}.cs`; `src/RetailPOS.Infrastructure/Data/Migrations/{20261103131039_PosV2P32FinancialPostingOperations.cs,…Designer.cs,PosV2P32FinancialPostingOperations.Custom.cs}`; `tests/RetailPOS.IntegrationTests/Phase3/Phase32FinancialAccountingPostgresTests.cs`; `tests/RetailPOS.IntegrationTests/Sales/SaleFinancialJournalCharacterizationTests.cs`.

Modified: `src/RetailPOS.Domain/Entities/SaleFinancialPosting.cs` (error codes); Application `Sales/FinancialPostings/{SaleFinancialSnapshots,ISaleFinancialPostingServices,SaleFinancialPostingCommands}.cs`, `Identity/Permissions/PermissionCatalog.cs`; Infrastructure `Sales/{SaleFinancialPostingWriter,SaleFinancialPostingModelConfiguration}.cs`, `Data/RetailPOSDbContext.cs`, `Data/DbSeeder.cs`, `Data/Migrations/RetailPOSDbContextModelSnapshot.cs`; API `Services/{SaleFinancialJournalPoster,FinancialPostingHostedServices,SaleCreationAdapters}.cs`, `Controllers/FinancialPostingsController.cs`, `Program.cs`, `appsettings.json`; frontend `components/roles/role-form.component.ts`; tests `UnitTests/Sales/SaleFinancialPostingTests.cs`, `IntegrationTests/Phase3/SaleFinancialPostingPostgresTests.cs` (partial, harness hooks), `Sales/{SaleCreationCommandTestFactory,SaleServiceVatAndExchangeAccountingTests}.cs`, `Accounting/P306FoundationTests.cs`, `MigrationChainValidationTests.cs`, `Inventory/MobileStockTakeSubmissionPostgresHttpTests.cs`; this document. Nothing staged; `backups/`, `design-reference/`, `system-settings.json` and build outputs must not be staged.

### 11.13 Remaining financial correctness risks / open items
1. **Discount presentation with inclusive pricing:** the `Sales discount` line keeps the price-basis (VAT-inclusive) amount and gross Sales is net revenue + discounts. Net revenue and VAT are exact, but gross-revenue and discount *reports* include a VAT portion for inclusive sales. Splitting the discount into ex-VAT and VAT parts per rate would need an accounting-presentation decision.
2. **Cash account vs drawer on partial refunds:** the GL credits the original payment accounts proportionally, while the drawer payout of a partial cash refund is the separately entered `CashRefundAmount`. If the two differ, the cash GL account and the physical drawer differ by that amount. This is the existing "line-level partial-refund cash policy" decision (Returns/Exchange SRD).
3. Pre-D10/legacy sales without line allocations or payment rows cannot be reversed in the GL automatically (explicit failure; an authorised manual journal is the remedy).
4. Only one refund per sale exists today, so cumulative multi-refund allocation is untested by construction. It must be revisited when Returns/Exchange allows several returns.
5. Alerts remain pull-based (summary endpoint, heartbeats, logs, outbox events).

### 11.14 Phase 4 readiness
Engineering-ready: sale and reversal journals balance from immutable evidence, reversal dating follows D02, permissions are dedicated and audited, and both deployment modes are verified. Before Phase 4 the owner should: review this section; decide §11.13 items 1–2; authorise the Development deployment (§11.9, four migrations, script and backup prepared); and choose Mode 1 or Mode 2 per environment.

## 12. Phase 4 — POS UI/UX Completion & Operational Workflows (3 Nov 2026)

**Status: implemented and verified on the isolated Test stack (unit, PostgreSQL integration, full integration suite with one contract-test failure since fixed, and real-browser runs); STOPPED for owner review. Nothing is staged, committed or pushed. Development migrations P21–P4 were applied later under separate owner authorisation (§13). Financial release gates D06 and D07 are OPEN (§12.15). Customer Loyalty, desktop offline POS and a Returns/Exchange engine were not started.**

### 12.1 Baseline and gap matrix (Checkpoint 1)
Sources read: SRS v1.2, LLD v1.2, this report (§9–§11), the gap analysis, the prototype, `LOCAL_INFRASTRUCTURE.md`, the Angular routes/UI kit and the API controllers. Working tree at start: 1,111 changed/untracked paths, nothing staged.

| Requirement | Existing backend | Existing UI | Gap | Change made | Test coverage |
|---|---|---|---|---|---|
| Salesperson selector, default, outlet users first, server check (SRS L62–64, L321) | **None** (no field, list or rule) | None | Missing end to end | `sales.salesperson_user_id` + eligibility rule + `GET api/pos/salespersons` + checkout validation + receipt/sale detail | Unit (default rule, fingerprint), PG (eligibility, list order, default), browser |
| Typed quantity with +/- and validation (FR-POS-03) | Server stock gate | +/- only | No direct typing | Typed whole-unit input, refuses <1, caps at known outlet stock (server still re-checks) | Unit, browser |
| Hold / Recall (FR-POS) | `held-sales` API | Present | **Recall queue crashed** (client stored the `{heldSales,totalCount}` envelope as the list); recall silently replaced a cart; discard unconfirmed | Envelope mapped; recall/discard/clear confirmations; Hold/Recall/Clear in a cart footer | Unit (service + component), browser |
| Payments: cash/card/mobile/split, tendered, change, net cash retained (FR-PAY-01) | `SalePaymentPlanner` (tendered/change/retained) | Single cash tender only; split cash had no tendered | Split cash tender/change; net retained not shown | Per-line cash tendered, change to return, net cash retained, tender error blocks checkout | Unit, browser |
| Receipt contents (FR-RCPT-01) | Receipt document + PDF | Present | Salesperson, business date, every payment, VAT basis, line VAT missing; every print logged as "Reprinted" | Added to document, DTO, HTML and PDF; first print "Printed", later "Reprinted"; Reprint/PDF on sale detail | PG (receipt facts, no new invoice no.), browser |
| Open session: server business date, 20 h max, terminal rules (FR-CS-03) | Open-context API | Dedicated page correct; POS dialog showed the **browser** date | POS dialog | POS dialog uses server open-context (date, Business limit, 20 h max); header shows "—" when no session | Unit, browser |
| Close session (FR-CLOSE-01..05) | Complete | Dedicated page complete; POS had a weaker dialog without variance reason/handover | Duplicate, weaker path | POS "Close Cash Session" holds the cart (confirm) and opens the dedicated close page; POS close dialog removed | Browser |
| Closing summary print/PDF (FR-CS-05; §9.4-3) | None | None | Missing | `GET api/cash-sessions/{id}/closing-summary.pdf` (QuestPDF, immutable snapshot + hash; 409 while open); Print Summary + Download PDF on the closed session | PG, browser |
| Daily Sales card (§10.10) | Only a UTC-day summary | "Not available" | No business-date metric | `GET api/cash-sessions/business-day-sales` (outlet business date in the effective zone, completed sales; voided/refunded counted, not netted) | PG, browser |
| Daily Targets (FR-TARGET-01..04) | None | "Not configured" | **No approved model** (§12.7) | Not implemented; card stays "Not configured" | — |
| Expense Payment Source selectors (§9.4-3, FR-EXP-01) | `PaymentSource` + pay-from-drawer | Raw "Payment Account ID"/"Outlet ID" numbers; no payout UI | Missing selectors and payout action | Searchable source selector with drawer guidance, searchable payment account, outlet selector; list shows approval and payout separately; "Pay from drawer" with terminal choice | Browser |
| Financial-posting permissions in navigation (H) | Phase 3.2 API | None | No screen | "Sale GL Postings" (Finance menu, `sales.financial_postings.view`; retry needs `.retry`) | Browser (admin allowed, cashier denied) |
| Fullscreen with clear exit (G) | — | None | Missing | Fullscreen/Exit Fullscreen; ERP shell hidden; state kept | Browser |
| Layout at 1366×768 and responsive widths (G/J) | — | Amount column clipped; footer below the fold; Cash Sessions overflowed on phones | Layout defects | Header-token based height, tighter cart grid, clock hidden < 1600 px, grid `min-width:0`, contained `.sr-only` | Browser (5 widths) |

### 12.2 Requirements implemented
Checkout: salesperson, typed quantity, single search (the product browser no longer has its own filter box; it browses by category), product thumbnails with icon fallback, SKU/barcode/variant per line, VAT rate per line, quick add reduced to the fields the API stores (name, mobile, email), walk-in reset, F3 customer search (F6 stays Hold per SRS), Escape closes the top panel. Lifecycle: hold/recall/discard/clear with confirmations, close via the dedicated page. Payments: split cash tender, change, net cash retained. Receipts: §12.6. Cash sessions: §12.5. Expenses: §12.8. Navigation: Sale GL Postings.

### 12.3 UI routes and components changed
`pages/pos/pos-screen.component.{ts,html,css,spec.ts}`; `pages/cash-sessions/cash-session-{list,detail,open}.component.ts`, `cash-sessions.css`, `cash-sessions.spec.ts`; `pages/sales/sale-details.component.{ts,html}`; `pages/accounting/expense-form.component.{ts,html}`, `expenses-list.component.{ts,html}`, new `expense-payment-sources.ts`, new `financial-postings.component.ts` (route `financial-postings`, permission `sales.financial_postings.view`); services `pos.service.ts` (+ new `pos.service.spec.ts`), `cash-session.service.ts`, `accounting.service.ts`, new `financial-posting.service.ts`; `models/sale.model.ts`, `models/accounting.model.ts`, `models/menu.model.ts`, `app.routes.ts`, global `styles.css` (POS fullscreen shell hiding; print hides the app shell; 80 mm receipt action buttons wrap).

### 12.4 Backend contract changes
| Contract | Change |
|---|---|
| `POST api/sales` | Optional `salespersonUserId`. Eligible = active user of the sale's Business whose primary outlet is the outlet or who holds an active `user_outlet_assignments` row for it; otherwise 400 `SALE_SALESPERSON_NOT_ELIGIBLE` before any stock or invoice work. Included in the idempotency fingerprint **only when present** (a request without it keeps its pre-Phase-4 fingerprint; pinned by a unit test). The cashier is still the authenticated user. |
| `GET api/pos/salespersons?outletId&terminalId` (`sales.create`, outlet authorised) | Eligible users, primary-outlet users first, then name. Default: opener of the terminal's open session if eligible, else the caller if eligible, else none. |
| `SaleDto`, receipt document, receipt PDF | `salespersonUserId/Name`; receipt adds salesperson, business date, every payment (method, amount, tendered, change), `taxInclusive` and line VAT rate/amount. |
| `GET api/cash-sessions/business-day-sales?outletId&businessDate` (`cash_sessions.view`) | Outlet sales whose server-assigned `business_date` equals the date (default: today in the effective zone; future rejected): completed count/total/VAT/net of VAT/discount; voided and refunded counted only. |
| `GET api/cash-sessions/{id}/closing-summary.pdf` (`cash_sessions.view`) | PDF from the immutable close snapshot and hash; 409 while open; variance decision printed as "current status". |
| Migration `20261103144445_PosV2P4SaleSalesperson` | Additive: nullable `sales.salesperson_user_id`, index `i_x_sales_salesperson_user_id`, FK `f_k_sales__users_salesperson_user_id` (RESTRICT). The cashier FK/index names are pinned in the model because the second `User` navigation would otherwise rename them. `has-pending-model-changes`: none. |

No accounting algorithm, journal composition, posting worker, D13 numbering, D1/D2/D7 cash rule or void/refund policy was changed.

### 12.5 Cash Session workflow changes
Landing: Daily Sales card (server metric, refresh, loading/error states), target card "Not configured", variance "—", opener name on active sessions, terminal filter as a list. Open (POS dialog): server business date, Business limit and 20 h maximum; Open disabled without a terminal. Close: POS routes to the dedicated page (reconciliation, variance reason, denominations, handover, review step) after holding any cart. Detail: Print Summary and Download PDF for closed sessions, variance reason and snapshot hash shown. Arbitrary Cash In/Out was not reintroduced; closing remains independent of GL posting.

### 12.6 Sales, Hold/Recall, Payment and Receipt
Hold/Recall now works (it crashed before, §12.1). Known limitation: held sales do not store the salesperson (the held-sale model has no such field), so after recall the selector shows the default again and must be re-checked — verified in the browser. Payments: change = tendered − cash allocation per cash line; net cash retained = cash allocation; split with a cash line short of its amount is blocked. Receipts: reprint never issues a new invoice number (verified: one sale row, same number); the sale detail page offers Reprint (server-recorded) and Receipt PDF.

### 12.7 Daily Sales / Targets status
Daily Sales: implemented from a verified server source (§12.4). Metric shown: completed sales total (incl. VAT), excluding VAT, VAT, count; voided and refunded sales are listed as not included because a refund marks the whole sale and the sale row carries no refunded amount (partial refunds therefore cannot be netted from this source).

Daily Targets: **not implemented — no approved contract.** The SRS describes rules (weekday targets Sat–Fri, weekly/first-half/second-half/monthly auto values, manual overrides with Reset to Auto, effective dating), but the data model is a "logical proposal", the API routes are "not mandates", and these are unresolved: (1) the authoritative sales metric (gross vs net of VAT, treatment of voids/partial refunds); (2) the week boundary for "Weekly"; (3) historical target versioning conflicts (LLD §escalation); (4) the exact edit/view permission keys; (5) where targets are edited (Outlet Create/Edit per SRS) and who approves changes. The card shows "Not configured".

### 12.8 Expense Payment Source changes
Searchable Payment Source (Bank/other, Petty cash, POS cash drawer) with the consequence of each; searchable payment account (chart accounts; manual ID fallback when the list is not permitted); outlet list (required for POS drawer). List: approval and payout shown separately ("Payout after approval" / "Awaiting drawer payout" / "Paid from drawer (session n)"); "Pay from drawer" only for approved, unpaid POS-drawer expenses and `cash_sessions.expenses.pay`, choosing a terminal with an open session; duplicate submission prevented (server is idempotent). Maker-checker and the owner exception are unchanged (server). No new source types or approval rules.

### 12.9 Permission and Outlet-scope verification
Server: salesperson eligibility (Business + outlet assignment), outlet authorisation on `salespersons`, `business-day-sales` and `closing-summary.pdf` (another Business is refused — PG tests). UI: Stock Manager is redirected from `/pos` and has no POS menu entry; Cashier is redirected from `/financial-postings`; Admin sees it; manual discount inputs are disabled for the Cashier role (no `sales.discount.apply`); sale detail Void requires `sales.void` (server unchanged). Inventory destination filtering was not changed.

### 12.10 Browser test results by viewport
Environment: isolated Test stack only. Disposable database `pos_e2e_*` on the Test PostgreSQL (127.0.0.1:55432, confirmed in the API log), Test Redis (6383), API `http://localhost:5005` (Development profile, migrate + demo seed, `SystemSettings:FilePath` outside the repo), `ng serve` on 4200. Browser: installed Microsoft Edge, headless, driven by `playwright-core` 1.48.2 installed in the session scratchpad (not in the repo). Users: seeded demo accounts (Cashier, Stock Manager, Admin). Development was never used. All E2E databases were dropped afterwards (0 remaining), and the API/UI processes were stopped.

Nine runs were needed; the runs found and drove the fixes listed below. **Final clean run (run 9, fresh database): 28 steps, 27 passed, 1 failed; 0 browser console errors; 0 failed API calls.**

| Journey step | Result (final run) |
|---|---|
| Login as Cashier; POS shows Open Cash dialog with the **server** business date, the 20 h limit and the terminal | Pass |
| Open session (500); salesperson selector lists eligible users, default = session opener | Pass |
| Search by SKU → variant, barcode and outlet stock shown → add | Pass |
| Type quantity 2 (recalculates); quantity 0 refused, line keeps 2 (warning shown) | Pass |
| Quick-add customer → selected | Pass |
| Change salesperson; Hold (F6) clears; Recall (F7) restores the cart | Pass (salesperson must be re-chosen after recall — limitation §12.14-1) |
| Manual discount inputs disabled for a role without `sales.discount.apply` | Pass |
| Cash sale: tendered > payable shows change 50.02 and net cash retained; completes | Pass |
| Receipt: invoice, business date, cashier, salesperson, customer, payment tendered/change | Pass |
| Print then Reprint (history posts Printed, Reprinted); receipt PDF download | Pass |
| Split payment cash 50 (tendered 100) + card 58.45 | **Step failed in the script** after the sale completed: the final "New Sale" click was blocked by the existing auto-print "Printer Not Available" dialog. The sale itself is verified in the database: cash 50.00 / tendered 100.00 / change 50.00 / retained 50.00 + card 58.45, completed |
| Cash Sessions landing: Daily Sales (server, business date) and target "Not configured" | Pass |
| Keyboard F2 / F3 / F7 / Escape | Pass |
| Fullscreen hides the ERP shell; Exit Fullscreen restores it; cart kept | Pass |
| Close from POS: cart held (confirm), dedicated close page; counted = expected → Review → Confirm & Close | Pass |
| Closed session: Print Summary and Download PDF (38 KB PDF) | Pass |
| Stock Manager: `/pos` redirected, no POS menu; Cashier: `/financial-postings` redirected | Pass |
| Admin: Sale GL Postings (backlog, workers, retry) | Pass |
| Expense form: searchable Payment Source (3 sources with consequences), outlet selector, account selector; list shows Approval and Source/payout | Pass |

Viewports (final run; POS with a cart line and Cash Sessions):

| Width × height | POS horizontal overflow | Complete Sale visible | Qty control visible | Complete Sale without page scroll | Cash Sessions overflow |
|---|---|---|---|---|---|
| 390 × 844 | none | yes (stacked layout) | yes | n/a | none |
| 768 × 1024 | none | yes | yes | n/a | none |
| 1366 × 768 | none | yes | yes | **yes** | none |
| 1440 × 900 | none | yes | yes | n/a | none |
| 1920 × 1080 | none | yes | yes | n/a | none |

Defects found by the browser runs and fixed (each re-verified in a later run):
1. Recall queue crashed (`newCollection[Symbol.iterator] is not a function`): the client stored the API's `{heldSales,totalCount}` envelope as the list — pre-existing; Hold/Recall never worked in the browser. Fixed + unit spec.
2. A refused typed quantity left "0" in the field. Fixed.
3. Expense Payment Source / account selectors showed "No results" until typing. Fixed (options seeded).
4. Cash Sessions overflowed horizontally on phones (grid `min-width:auto`; `.sr-only` header label escaping the scroll box). Fixed.
5. At 1366×768 the cart Amount column was clipped and the footer/Complete Sale fell below the fold (height assumed a 64 px header and no content padding). Fixed.
6. App alerts (e.g. "Printer Not Available") rendered **under** POS modals, so they could not be answered and clicking them closed the receipt. Fixed (POS overlays below the shared dialog layer).
7. Receipt action buttons were clipped in the 80 mm receipt. Fixed (wrap).
8. Closed-session Print Summary / Download PDF buttons were not rendered (header content projection dropped multi-node control-flow blocks; the existing "Close session" header link had the same latent issue). Fixed + spec.

Not verified in a browser: physical receipt printers (headless Chrome/Edge has no USB/serial printer, so the existing "Printer Not Available → Save as PDF" fallback is what was exercised; `window.print` was stubbed to count calls), mobile touch devices, screen readers (only labels/roles/keyboard paths were checked), Firefox/Safari, the drawer payout end to end (the dialog and permission gating were exercised by unit/structure checks; no approved POS-drawer expense was created in the browser), and the restricted-role matrix beyond Cashier / Stock Manager / Admin.

### 12.11 Unit and integration test results
| Check | Result |
|---|---|
| Backend unit tests | **739 passed** (733 + 6 salesperson: default rule, fingerprint compatibility pinned to the pre-Phase-4 hash) |
| Phase 4 PostgreSQL/HTTP tests (`Phase4PosOperationsPostgresTests`) + migration chain | **6 passed** (checkout eligibility incl. foreign Business / inactive / unassigned users and no stock or intent on rejection; selector order and default; receipt facts and no new invoice number on reprint; Daily Sales by business date incl. Business isolation; closing-summary PDF 409 → 200 `%PDF`; chain latest = P4) |
| Full backend integration suite (Test stack, one run, 3 Nov 2026 16:15–16:56) | **2,108 tests: 2,107 passed, 1 failed, 0 skipped (40 min 56 s).** The owner's stop request arrived after the run had already finished; the result and trx (`phase4-integration.trx`) are complete. The failure was `SalesCreateTerminalHttpTests.GetSaleById_PreservesSaleDtoAndAuthorizesOutletBeforeReturningIt`, which pins the exact `SaleDto` field list; Phase 4 intentionally added `salespersonUserId`/`salespersonName`. The expected list was updated to the new contract and that test re-run alone: **passed**. A complete full-suite re-run after this change was **not** performed (not authorised), so the suite is **not** claimed fully green. |
| Angular unit tests | **97 files, 1,290 tests passed** (1,281 before Phase 4; new: POS checkout UX, held-sales envelope, closed-session header actions) |
| Angular production build | OK; only warning: `pos-screen.component.css` 11.97 kB over the 10 kB warning budget (error limit 20 kB) |
| D1–D14 / D10 / D13 regressions | Covered by the full suite (Phase 3 cash-session, Phase 3.1/3.2 posting, D13 numbering classes all passed in the run above) |

### 12.12 Migration status
New migration `20261103144445_PosV2P4SaleSalesperson` (additive). On 3 Nov 2026, under explicit owner authorisation, the full pending chain P21 → P3 → P31 → P32 → P4 was applied to **Development** after backup and rehearsal — see §13. Staging and Production were not touched.

### 12.13 Screenshots / reproducible browser evidence
Scripts and evidence are outside the repository (session scratchpad `e2e/`): `start-api.ps1` (disposable `pos_e2e_*` database on the Test server 127.0.0.1:55432, Test Redis, migrate + demo seed, settings file outside the repo), `journey.js` (Playwright-core driving the installed Edge, headless), `results-run4.json`, `screenshots/*.png` (dialogs, receipt, tender, close page, closed detail, viewport captures at 5 widths, restricted-role redirects, expense form/list, Sale GL Postings). The E2E databases were dropped afterwards. Test-only setup: outlet invoice prefixes (MAIN/DTN) were set on the disposable database because the demo seed leaves them empty (checkout would otherwise fail D13 with `SALE_OUTLET_INVOICE_PREFIX_MISSING`).

### 12.14 Outstanding issues
1. Held sales do not keep the salesperson (needs a held-sale field + migration if wanted).
2. POS product search filters the first 200 catalogue products loaded by the browser, with server barcode/SKU lookup on Enter; large catalogues need server-side name search (`/api/products/variants/search` requires `lookup.product_variants`, not held by cashiers).
3. AlertService messages are modal (e.g. "Sale held", "Cash session closed", quantity warnings) and must be acknowledged; fine for errors, heavy at the till. Auto-print shows "Printer Not Available" when no USB/serial printer is visible to the browser (existing behaviour).
11. On phones the per-line Disc % and VAT values are shown without their column labels (the header row is hidden in the stacked layout).
4. Demo seed leaves outlet invoice prefixes empty (D13 checkout fails until set in Outlet edit).
5. The Test Redis container was started with a different password than the current `docker/.env.test` (E2E used the container's own value; tests unaffected). Recreate the Test stack or align the file.
6. Sale detail Void keeps its existing client rule (same calendar day); the SRS says no age limit — owner decision, not changed.
7. "Change salesperson" has no separate permission (SRS does not define one); any eligible user can be chosen, enforced server-side.
8. POS stylesheet exceeds the 10 kB warning budget (11.8 kB, error limit 20 kB).
9. Daily Targets (§12.7).
10. Phase 3 outstanding items unchanged: deferred void/refund payout and line-level partial-refund cash policy (Returns/Exchange SRD).

### 12.15 D06 / D07 financial release gates (OPEN — not implemented)
| Gate | Decision | Current state | Customer-visible impact | Phase 4 safeguard |
|---|---|---|---|---|
| **D06** VAT-exclusive discount presentation | Revenue and Sales Discount GL presentation VAT-exclusive; gross discount snapshots kept | §11.13-1: the `Sales discount` line keeps the VAT-inclusive amount for inclusive pricing; gross revenue and discount reports include a VAT portion | None on receipts/payables; GL/report presentation of discounts for inclusive-VAT sales is overstated by the VAT share (net revenue and VAT are exact) | No accounting code changed; receipts show the VAT basis; release blocker for inclusive-VAT businesses' financial reporting |
| **D07** Refund tender-based GL | Payment-side refund GL from actual immutable refund tender evidence; drawer only for the paying session; card/mobile never reduce the drawer; unpaid ≠ settled | Reversal credits the original payment accounts proportionally (§11.13-2); drawer payout is the separately entered `CashRefundAmount` | A partial cash refund whose payout differs from the proportional split leaves the cash GL account and the drawer apart by the difference | No refund UI added (refund endpoint still has no frontend caller); no partial cash refund, deferred payout or settlement combination is exposed; void keeps the existing server workflow |

Both require a separate controlled accounting work package or an explicitly approved scope extension.

### 12.16 Git change manifest (Phase 4 only)
New: `src/RetailPOS.Application/Sales/Abstractions/ISaleSalespersonDirectory.cs`, `src/RetailPOS.Application/Sales/Queries/GetEligibleSalespersonsQuery.cs`, `src/RetailPOS.Infrastructure/Sales/SalespersonEligibility.cs`, `src/RetailPOS.Infrastructure/Pos/CashSessionCloseSummaryPdfRenderer.cs`, `src/RetailPOS.Infrastructure/Data/Migrations/20261103144445_PosV2P4SaleSalesperson{,.Designer}.cs`, `tests/RetailPOS.UnitTests/Sales/SalespersonTests.cs`, `tests/RetailPOS.IntegrationTests/Phase3/Phase4PosOperationsPostgresTests.cs`, `retailpos-frontend/src/app/pages/accounting/{expense-payment-sources.ts,financial-postings.component.ts}`, `retailpos-frontend/src/app/services/{financial-posting.service.ts,pos.service.spec.ts}`.

Modified: Domain `Entities/Sale.cs`; Application `Sales/Commands/{CreateSaleCommand,GenerateSaleReceiptDocumentCommand}.cs`, `Sales/Checkout/{SaleRequestFingerprint,SaleCheckoutExceptions}.cs`, `Sales/Abstractions/ISaleCreationStore.cs`, `Sales/ReadModels/{SaleReadModels,SaleReceiptDocumentReadModel}.cs`, `Pos/Queries/CashSessionQueries.cs`, `Pos/ReadModels/CashSessionReadModels.cs`, `Pos/Abstractions/ICashSessionQueryStore.cs`; Infrastructure `Data/RetailPOSDbContext.cs`, `Data/Migrations/RetailPOSDbContextModelSnapshot.cs`, `Sales/{SaleCreationStore,SaleReadStore,SaleReceiptPdfRenderer}.cs`, `Pos/CashSessionQueryStore.cs`; API `Controllers/{SalesController,PosController,CashSessionsController}.cs`, `DTOs/Sale/{CreateSaleDto,SaleDto,ReceiptDocumentDto}.cs`, `Program.cs`; tests `IntegrationTests/Sales/SaleCreationPostgresConcurrencyTests.cs` (store decorator), `IntegrationTests/MigrationChainValidationTests.cs` (latest = P4); frontend files in §12.3; this document. Nothing staged; `backups/`, `design-reference/`, `system-settings.json`, build outputs and test results must not be staged.

### 12.17 Readiness for the next phase
Engineering-ready for owner review. Before release: decide D06/D07 (§12.15) and the Daily Target contract (§12.7); decide whether held sales should keep the salesperson; run one complete full integration suite on the final source (the last full run had the one contract-test failure fixed afterwards); set outlet invoice prefixes in every environment. Development already carries P21–P4 (§13); Staging/Production need their own authorised, rehearsed deployment.

## 13. Development deployment of migrations P21 → P4 (3 Nov 2026, owner-authorised)

**Status: DONE on Development only. Staging and Production were not touched. Nothing staged, committed or pushed. D06 and D07 remain OPEN (§12.15). Phase 5 not started.**

### 13.1 Full integration suite at the time of the stop request
The owner asked to stop the running Phase 4 full suite. When the stop was processed the run had **already completed** (the test host had exited; the log ends with the summary and the trx was written): **2,108 tests — 2,107 passed, 1 failed, 0 skipped, 40 min 56 s.** It is therefore recorded as a completed run, not a cancelled one. The single failure (the `SaleDto` field-list contract test, §12.11) was fixed in the test and re-run alone (passed); no further full suite was started. Leftover disposable Test databases: one harness database (`retailpos_recon_match_f014…`) of unproven origin was left untouched; the Phase 4 E2E databases (`pos_e2e_*`) and the rehearsal database were dropped (verified 0 remaining).

### 13.2 Target and pre-deployment state
| Item | Value |
|---|---|
| Container / port / database | `iranimart-dev-postgres-1`, `127.0.0.1:5435`, `retailpos_db` (PostgreSQL 16.13) — the local Development stack per `LOCAL_INFRASTRUCTURE.md` |
| Migration history before | **63**, last `20261102181136_PosV2P2TenancyTaxSettingsInvoiceNumbering`; every source migration up to P2 present; no unknown history rows |
| Pending (source vs history) | `20261102212702_PosV2P21PricingRuleTenancy`, `20261103091638_PosV2P3CashSessionManagement`, `20261103111708_PosV2P31SaleFinancialPostings`, `20261103131039_PosV2P32FinancialPostingOperations`, **`20261103144445_PosV2P4SaleSalesperson` (new in Phase 4)** |
| Model vs snapshot | `has-pending-model-changes`: none |
| Writers during deployment | no Development API running (port 5005 free), 0 other connections to `retailpos_db` |

### 13.3 Safety gates
| Gate | Result |
|---|---|
| Fresh backup | `backups/phase4/retailpos_db-dev-pre-p21-p4-20261103T165832Z.dump` (`pg_dump -Fc`, 1,109,116 bytes, SHA-256 `f8c4167f3c5e74de01e44f345efb055a999a2c0f2ed981f217aacfc27ec547eb`); `pg_restore --list` OK, 184 table-data entries. Git-ignored (`/backups/`). |
| Script | `backups/phase4/dev-pending-p21-p3-p31-p32-p4-idempotent.sql` (`dotnet ef migrations script <P2> <P4> --idempotent`, 795 lines, SHA-256 `55cbc7db…f5a494908b`). Identical to the Phase 3.2 reviewed/rehearsed script except the additive P4 block (nullable `sales.salesperson_user_id`, index, FK RESTRICT, history row). |
| Statement review | No `DROP TABLE/COLUMN`, `TRUNCATE` or `DELETE`. Data-changing statements are the reviewed P21/P3/P31/P32 ones: one price-rule index replaced, `business_id` backfill + NOT NULL on price rules/overrides (0 rows in Dev), cash-session status normalisation and `business_date` → `date` (0 rows in Dev), snapshot backfill, role grants. 7 triggers / 5 functions created. |
| Rehearsal | Restored the fresh dump into `p4_rehearsal_devcopy` on the **Test** server; validation → script (`ON_ERROR_STOP`, exit 0, 0 errors) → validation → idempotent re-run (exit 0). Migrations 63 → 68; all row counts and sums identical; new tables/triggers/constraints present; 0 invalid indexes, 0 unvalidated constraints. Rehearsal database dropped afterwards. |
| Validation queries | `backups/phase4/validate-pending-chain.sql` (Phase 3.2 queries + P4 column/index/FK, invalid indexes, unvalidated constraints) |

### 13.4 Applied to Development
`psql -v ON_ERROR_STOP=1 -f dev-pending-p21-p3-p31-p32-p4-idempotent.sql` on `retailpos_db`: **exit 0, 0 errors.** Applied, in order: P21, P3, P31, P32, P4. History now **68**, last `20261103144445_PosV2P4SaleSalesperson`; source vs history: 0 pending, 0 unexpected. Development's post-deployment validation output is byte-identical to the rehearsal's. Evidence: `backups/phase4/dev-before.txt`, `dev-apply.txt`, `dev-after.txt`, `rehearsal-*.txt`. No rollback was needed or executed.

### 13.5 Post-migration verification
| Check | Result |
|---|---|
| Sales / sale items / payments | 3 / 7 / 0 — unchanged; sales total 1,929.91 unchanged; 0 orphan items or payments; existing sales have `salesperson_user_id` NULL |
| Inventory | 53 inventory rows, 3,289 units — unchanged; 0 negative stock |
| Journals | 2 entries / 4 lines, debits = credits = 4,620.00 — unchanged; 0 unbalanced, 0 orphan lines |
| Users / roles | 9 users (all active) / 10 roles retained |
| Permissions | `sales.financial_postings.view`: Admin, Accounting Manager; `.retry`: Admin; `cash_sessions.close` added to Admin, Cashier, Manager, OutletManager, Salesman (roles with `sales.create`); wildcard roles unchanged |
| Cash Session schema | `cash_difference_approvals`, `cash_session_movements`, `cash_session_close_snapshots` present; `cash_sessions.business_date` is `date`; triggers `trg_sales_require_open_cash_session`, `trg_cash_session_movements_require_open`, `trg_cash_session_movements_append_only`, `trg_cash_session_close_snapshots_immutable` present |
| Sale Financial Postings | `sale_financial_postings` (empty) with `trg_sale_financial_postings_guard` and its CHECK constraints |
| Worker heartbeats | `financial_posting_worker_heartbeats` with `ck_financial_posting_worker_heartbeats_role` |
| Indexes / constraints | 0 invalid indexes, 0 unvalidated constraints; P4 index and FK present |

### 13.6 Development API startup
Started from this source with the standard Development profile (`--launch-profile http`, user-secrets → Development; `MigrateOnStartup`/`SeedOnStartup` false; `FinancialPosting:HostRole=Api`, workers in-process). Log: both posting workers started, listening on `http://localhost:5005`, **0 fail/crit entries**. In Api mode no `/health` route is mapped (404 by design; `/health/financial-posting` exists only in Worker mode); a protected endpoint answers 401 without a token. Both workers wrote heartbeats to `retailpos_db` (`host_role=Api`, a completed pass, 0 consecutive failures); `sale_financial_postings` stayed empty and the sales data unchanged. No test sale, refund or other data was created in Development. The API was left running in this session (background task; it stops with the session or Ctrl+C).

### 13.7 Remaining tests and release gates
- One complete full integration run on the final source (the last full run had the single contract-test failure fixed afterwards).
- D06 and D07 (§12.15) — OPEN; no accounting rule was changed by this deployment.
- Daily Target contract (§12.7); held-sale salesperson (§12.14-1).
- Outlet invoice prefixes (D13): the three Development outlets already have prefixes (O001–O003); other environments seeded from the demo seed must set them before checkout.
- Existing legacy sales remain without VAT line evidence and payment rows (§11.9): a void/refund of them fails explicitly (`allocation_evidence_missing`) by design.
- Staging/Production: separate authorisation, backup and rehearsal.
