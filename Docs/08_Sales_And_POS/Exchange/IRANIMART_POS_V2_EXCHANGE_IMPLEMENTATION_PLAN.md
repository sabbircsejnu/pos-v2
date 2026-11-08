# IraniMart POS v2 — Product Exchange: Implementation Plan

**Document ID:** IM-POS-EX-PLAN-001
**Version:** 2.2 (Stage 0 outcome incorporated)
**Date:** 2026-11-07
**Status:**
- Approved decisions: EX-01–EX-31, D-01–D-19, C-01–C-10 and OC-01–OC-07 (67). No business clarification is open.
- **Implementation not started.** Stage 0 (read-only) may begin. Each later stage starts only when its entry gates are met.

**Companions:**
- SRS v2.0 (FR-EX-nnn, AC-nn, OC-nn)
- LLD v2.0 (§n)
- Gap Analysis v2.0 (G-nn, TF-nn, VI-nn, TB-nn)

> This plan adds no scope and changes no approved decision. Each of the following still needs its own explicit approval under `CLAUDE.md`: code changes, migrations (TB-01), seeding (TB-06), database reads for the data audit (TB-02), commits and deployment.

### Change log
| Version | Change |
|---|---|
| 1.0 | Stages S1–S6, decision register D-01–D-19 (proposals), D-05 approved. |
| 2.0 | All decisions approved and the D-register replaced by the traceability matrix (Appendix A). <br>• Restructured into Stages 0–5. <br>• Superseded: `pos.exchange.approve_high_value` (C-10); the cashier's own-permission override (D-04 B, C-02); deferred Exchange posting (C-04); proof-type declaration (D-13). |
| 2.1 | OC-01–OC-07 approved and incorporated into tasks, tests (AC-46–AC-52), exit gates and Appendix A (67 records). TB-05 resolved; TB-07, VI-16 and VI-17 added. |
| 2.2 | Stage 0 static audit complete, verdict **CONDITIONAL PASS** (`IRANIMART_POS_V2_EXCHANGE_STAGE_0_TECHNICAL_AUDIT.md`). <br>• Added DB tests T-01…T-05 as Stage 3 exit gates. <br>• Added TB-08 (test infrastructure), DN-01 (phone `required` mode) and VI-18…VI-20. <br>• Added migration scope for the cash CHECK constraints. <br>• Readiness per stage in §2.1. |

---

## 1. Delivery scope
| Area | Status |
|---|---|
| EX-01–EX-12, EX-14–EX-31 | Required now, production-ready |
| EX-13 | Business requirement approved; **runtime integration deferred** to EXL-01 (§8). It is not reported as implemented until EXL-01 passes. |
| D-06 individual salesperson target management | Deferred; facts and outlet target integration are required now |
| Receipt QR, payment-provider integration, drawer guard for existing refunds and expenses | Deferred / out of scope |

## 2. Stage 0 — Read-only compatibility audit
| Item | Content |
|---|---|
| **Objectives** | Close the verification items and quantify data compatibility before any schema or code change |
| **Dependencies** | None (read-only). The data audit needs DB read approval (TB-02). |
| **Scope** | Sales, Refund, Void, Stock/Ledger, Cash Session, Accounting (journal engine, period guard, slots), Roles/Auth, UOM, Reporting, Outbox, Loyalty boundary |
| **Components** | Read-only: `src/**`, `tests/**`, `retailpos-frontend/src/**`; approved read-only SQL against an approved database |
| **Tasks** | S0-01: VI-01 slot identifiers and journal error types; prove a synchronous post plus rollback on the **Test** DB (throw-away spike test, not committed code). <br>S0-02: VI-02 posting-date/period day mapping. <br>S0-03: VI-03 finance confirmation of the slot rule. <br>S0-04: VI-04 supervisor lookup model, incl. supervisor `UserOutletAccessContext` construction (OC-01). <br>S0-05: VI-05 data audit (counts of sales and lines lacking `TaxableAmount`/`VatAmount`/`HistoricalUnitCost`; ledger cost evidence match rate per business). <br>S0-06: VI-06 walk-in rule. <br>S0-07: VI-07 movement CHECKs and close-snapshot formula. <br>S0-08: VI-08 PDF renderer reuse. <br>S0-09: VI-10 inventory-row creation path. <br>S0-10: VI-11 audit atomic scope for outbox and journal. <br>S0-11: VI-12 lock-order analysis document. <br>S0-12: VI-13 dashboard bases. <br>S0-13: VI-15 ledger CHECKs. <br>S0-14: ~~obtain owner answers OC-01…OC-07~~ **done 2026-11-07** (SRS §26.3). <br>S0-15: VI-16 phone-verification capability (OC-06). <br>S0-16: VI-17 purchase-instant source and `DateTime.Kind` handling (OC-07). <br>S0-17: read-only analysis that reversal + existing refund (the OC-04 path) reconciles stock, cash, GL and `returned_quantity`. |
| **Tests** | Spike tests only (not merged); evidence captured in a Stage 0 report |
| **Acceptance** | Every VI has a recorded result (resolved, or an accepted blocker with mitigation) |
| **Exit gates** | Stage 0 report appended to the Gap Analysis (§4 updated); TB-03 and TB-07 cleared or explicitly escalated |
| **Known blockers** | TB-02 (DB read approval) |
| **Outcome (2026-11-07)** | **Static audit complete: CONDITIONAL PASS.** <br>Report: `IRANIMART_POS_V2_EXCHANGE_STAGE_0_TECHNICAL_AUDIT.md`. Gap Analysis §4.1/§4.2/§8 and LLD v2.2 updated. <br>Escalated, not cleared: <br>• TB-03 needs T-01; <br>• TB-07 needs DN-01 and Q8; <br>• the read-only data audit (§18 of the report) awaits approval (TB-02); <br>• DB tests T-01…T-05 await TB-08. |

### 2.1 Stage readiness after Stage 0
| Stage | Readiness | Conditions |
|---|---|---|
| 1 | **Ready** | — |
| 2 | **Conditionally ready** | Schema work needs TB-01. Pure-code tasks (permission keys, policy and date and high-value calculators, void-guard logic with unit tests) can proceed. The phone `required` + registered path waits for DN-01. |
| 3 | **Conditionally ready** | Exit requires T-01 (TB-03), T-02 (VI-18), T-03 (TB-04), T-04 (VI-07), T-05 (VI-10), all under TB-08 approval |
| 4 | **Conditionally ready** | After the Stage 3 schema and flows |
| 5 | **Blocked** until Stages 3–4. Release also needs TB-02 (data audit), TB-06 (seeding), VI-03/VI-20 (finance) | |

## 3. Stage 1 — Contract and documentation alignment
| Item | Content |
|---|---|
| **Objectives** | Freeze the API, DB and error contracts using the Stage 0 results |
| **Dependencies** | Stage 0 exit |
| **Scope** | SRS/LLD/Gap/Plan updates only; the migration script **design** (not applied) |
| **Components** | `Docs/08_Sales_And_POS/Exchange/*` |
| **Tasks** | S1-01: Move LLD contracts from Proposed to Frozen where verified. <br>S1-02: ~~Record the OC answers~~ done (SRS v2.1); freeze the OC-derived contracts (LLD §7.1, §10.1, §10.1.1, §11, §22.1). <br>S1-03: Set the technical configuration values (approval lifetime, page-size cap, rate-limit window). <br>S1-04: Write the migration design review (DDL listing, constraints, rollback). <br>S1-05: Decide the barcode ambiguity UX (VI-09). |
| **Tests** | Document review (cross-reference check, §10) |
| **Acceptance** | No open "Proposed" item needed by Stage 2; consistency review passes |
| **Exit gates** | Owner sign-off on the frozen contracts; migration approval requested (TB-01) |
| **Known blockers** | TB-07 if the VI-16/VI-17 results need contract changes |

## 4. Stage 2 — Backend foundations
| Item | Content |
|---|---|
| **Objectives** | Schema, policy, permissions, approvals, source lookup, quantity and chain model, idempotency scaffolding, legacy-path changes |
| **Dependencies** | Stage 1 exit; migration approval (TB-01) |
| **Scope** | Domain, Infrastructure, Application, API; FE: policy settings, role-form group, connectivity service |
| **Components** | `PermissionCatalog`; new `Exchanges` slice; `PosV2ExchangeFoundation` migration; `VoidSaleCommand`; `CreateSaleCommand`/`SalesController` (D-01); extracted shared helpers (VI-14) |
| **Tasks** | S2-01: Permissions ×5 + role-form "Product Exchange" group (EX-16, C-01, C-10). <br>S2-02: Entities, EF configuration, `PosV2ExchangeFoundation` migration on the Test DB (G-01). <br>S2-03: Policy versions + GET/PUT + "disabled until configured" (D-16, D-07; G-02). <br>S2-04: Exchange number allocator (D-19; G-22). <br>S2-05: Exact source-invoice lookup (D-10; G-10) + eligibility (status, evidence C-09, window, chain, remaining quantity C-03). <br>S2-06: Same-customer / walk-in validator (D-03; G-11). <br>S2-07: Supervisor approval endpoint + `exchange_approvals` + rate-limit policy (D-04, C-02, C-10; G-04). Approval types `window`/`chain`/`high_value`/`refund`; supervisor receiving-outlet check via `IUserOutletAccess` on the supervisor's own context (OC-01); several listed types in one authentication, recorded separately (OC-02); refund approval bound to the refund amount and method (OC-03). <br>S2-08: Void guard `SALE_HAS_RETURNS` (D-14; G-26). <br>S2-09: Legacy link rejection `SALE_EXCHANGE_LINK_DEPRECATED` (D-01; G-27) + updated legacy tests. <br>S2-10: Extract CreateSale helpers without behaviour change (VI-14). <br>S2-11: FE `ConnectivityService` (EX-28), policy settings UI (threshold > 0 when set, OC-05). <br>S2-12: Time-zone-aware eligibility date evaluator (OC-07; G-47): purchase local Day 0, `eligibility_local_date`, replacement anchor, no UTC-calendar shortcuts. <br>S2-13: Phone verification classification (OC-06; G-39, G-45): `not_required` / `skipped_walkin` / `matched_customer_record`, never the `WALKIN` phone, never a loyalty phone. |
| **Tests** | Unit: policy validator, approval rules (all AC-31 cases), number rendering, evidence rule, the date matrix (AC-52 a–i), the phone matrix (AC-51). PG: allocator concurrency; approval single-use race; supervisor outlet access incl. revoked assignment (AC-46); chain approval cases (AC-47); void guard; legacy rejection; tenancy of the lookup (AC-26, AC-41). Full `SaleCreation*`/`SaleVoid*`/Refund regression before and after S2-10. |
| **Acceptance** | AC-26, AC-29, AC-30, AC-31, AC-32, AC-33, AC-34, AC-36, AC-41, AC-46, AC-47, AC-51, AC-52 |
| **Exit gates** | Backend build; migration validation (`MigrationChainValidationTests`); targeted unit and PG suites green; regression green; `pos-security-review` of the approval endpoint (credential handling, OC-01 outlet check, no session switch) |
| **Known blockers** | TB-01 (incl. the `cash_session_movements` CHECK extension, G-48); DN-01 (phone `required` + registered only); Q8 before window activation |

## 5. Stage 3 — Atomic Exchange processing
| Item | Content |
|---|---|
| **Objectives** | Preview and Confirm with stock, pricing, settlement, cash, journal, attribution facts, receipt snapshot and outbox, in one transaction |
| **Dependencies** | Stage 2 exit; TB-03 cleared (VI-01/02/11); VI-12 analysis |
| **Scope** | `PreviewExchangeQuery`, `ConfirmExchangeCommand`, settlement planner, drawer guard, journal poster port, receipt document |
| **Components** | Application `Exchanges`; `IExchangeJournalPoster` implementation beside `SaleFinancialJournalPoster`; `CashSessionMovementRecorder` (drawer lock); `CashSessionExpectedCashCalculator`; `StockLedgerWriter`; `IPackStateLedger`; `IIntegrationOutbox` |
| **Tasks** | S3-01: Return credit and cost (EX-09, D-08, C-09; G-07). <br>S3-02: Replacement pricing (EX-10, EX-27; G-08). <br>S3-03: Settlement planner (EX-07, EX-08, EX-02, D-09; G-14, G-15), incl. refund authorization Case A/B (OC-03) without bypassing D-02, D-09, C-06 or other exceptions. <br>S3-04: Drawer lock + expected cash + new movement types (EX-31, D-02; G-16, G-17). <br>S3-05: Receiving-outlet stock-in/out + UOM + pack state (EX-05, EX-06; G-09, G-42). <br>S3-06: Journal composition + synchronous post + period and account errors (C-04, C-06, D-15; G-19, G-20, G-21). <br>S3-07: Salesperson fact (D-06, D-07; G-30 data part). <br>S3-08: Receipt snapshot + PDF (EX-12; G-23). <br>S3-09: Outbox `pos.exchange.completed` + `loyalty_status = module_inactive` (D-05; G-41). <br>S3-10: Confirm orchestration (lock order, preview hash, idempotency, approval consumption) (EX-29, C-03, C-04; G-37, G-43). <br>S3-11: HTTP endpoints + error mapping. <br>S3-12: High-value evaluation `max(return_gross, replacement_gross) ≥ threshold`, stored on the header, bound in `previewHash`, recalculated at Confirm (OC-05; G-39). <br>S3-13: Confirm re-checks approvals against current supervisor permission and outlet assignment (OC-01) and the recomputed eligibility date (OC-07). |
| **Tests** | Unit: journal balance matrix; settlement; expected cash. PG: AC-01…AC-09, AC-12, AC-15, AC-16, AC-18, AC-19, AC-24, AC-25, AC-28, AC-37, AC-38, AC-39, AC-40, AC-43, AC-44; fault injection per write step; idempotent replay; concurrency matrix (refund vs exchange, exchange vs exchange, cash-out vs refund payout vs expense, exchange vs checkout last unit). |
| **Acceptance** | All listed ACs plus AC-48 (refund paths) and AC-50 (high value) green on the isolated Test DB |
| **Exit gates** | Journal balance and reconciliation proof; return-quantity consistency; stock conservation; cash sufficiency; idempotency; `pos-code-review` + `pos-security-review` |
| **Known blockers** | TB-03, TB-04, TB-08. Stage 0 additions to the scope: <br>• period row FOR SHARE + typed pre-validation (G-49, VI-18); <br>• `AppendSourceChangeAsync`; <br>• rollback with `ChangeTracker.Clear()`; <br>• inventory locked in one `ORDER BY id` statement; <br>• drawer advisory lock in `CashSessionMovementRecorder`; <br>• mobile-report netting (G-50, Stage 5). <br>Exit DB tests: T-01…T-05 (Stage 0 report §17). |

## 6. Stage 4 — Reversal and UI
| Item | Content |
|---|---|
| **Objectives** | Strict reversal; Exchange History; receipts; POS and Sales entry; supervisor approval UI; re-exchange UX |
| **Dependencies** | Stage 3 exit; reversal migration approval (TB-01) |
| **Scope** | `ReverseExchangeCommand`, `PosV2ExchangeReversals`; FE `pages/exchange/*` |
| **Components** | Backend reversal (LLD §22); history queries (LLD §21); Angular: `exchange-shell` (stepper), `exchange-settlement` (pay-panel pattern), `exchange-approval-dialog`, `exchange-receipt` (80mm, reusing `receipt-print-page.ts`), `exchange-history-list/detail`, POS `pos-actions` button, Sale Details action + linked-exchanges card |
| **Tasks** | S4-01: Reversal command + eligibility endpoint (EX-14, C-05; G-32). <br>S4-02: History, detail and by-sale queries (EX-30, D-11; G-31). <br>S4-03: Re-exchange UX of replacement lines (EX-04; G-06). <br>S4-04: Exchange workflow UI incl. scan/manual (EX-26), reasons (EX-17), salesperson (EX-11), settlement (EX-07/08, D-09), review/confirm (EX-29). <br>S4-05: Supervisor approval dialog (C-02: password field cleared after the request; no session switch). Lists every required type, each with its own reason (OC-02). Refund approval only in Case B (OC-03). <br>S4-06: Receipt print/PDF/reprint (EX-12). <br>S4-07: Entry points (EX-19) and offline disable (EX-28). <br>S4-08: D-18 guard test (replacement not refundable via Sales Refund). <br>S4-09: OC-04 boundary: UI message on a replacement line pointing to escalation (no refund action); PG test that reversal then the existing refund reconciles, and that a blocked reversal produces no side effects (G-46). No legal-remedy subsystem. |
| **Tests** | PG: AC-20, AC-21, AC-11, AC-35, AC-42. Vitest: entry points, offline (AC-17), stepper validation, approval dialog, settlement, receipt, history scope. Angular production build. |
| **Acceptance** | AC-11, AC-13, AC-14, AC-17, AC-20, AC-21, AC-27, AC-35, AC-42, AC-49 |
| **Exit gates** | Angular production build + lint; Vitest green; existing POS checkout specs green (`pos-screen*.spec`, `sale-details.component.spec`, `thermal-receipt.component.spec`, `role-form.component.spec`) |
| **Known blockers** | TB-01 (reversal migration) |

## 7. Stage 5 — Reporting and verification
| Item | Content |
|---|---|
| **Objectives** | Dashboards, targets, reports; full verification; release readiness |
| **Dependencies** | Stage 4 exit |
| **Scope** | `SalesFinanceReadStore`, `SalesReportReadStore`, VAT report, metrics query, outlet target, exchange report UI; full regression; UAT |
| **Tasks** | S5-01: Owner dashboard exchange facts + outlet target (EX-01, EX-20, D-06; G-29, G-30). <br>S5-02: Mobile dashboard facts (VI-13). <br>S5-03: VAT report legs. <br>S5-04: Metrics query and report UI. <br>S5-05: B-03 reconciliation (D-12; G-28), closing only on evidence. <br>S5-06: Authorization suite (C-08; G-40). <br>S5-07: Full regression and quality gates (§9). <br>S5-08: Role template seeding after approval (D-17; TB-06). <br>S5-09: UAT script from the AC list. |
| **Tests** | Reconciliation: dashboard Net Sales = Σ regular + Σ exchange net adjustments for ≥3 dates × 2 outlets, including a reversal on a later date and a cross-outlet Exchange; VAT tie-out; AC-26, AC-41, AC-42, AC-45 |
| **Acceptance** | All AC-01…AC-52 except AC-22/AC-23 (EXL-01) |
| **Exit gates** | §9 quality gates all evidenced; owner UAT sign-off; B-03 decision recorded |
| **Known blockers** | TB-06 |

## 8. Deferred task EXL-01 — Exchange ↔ Loyalty runtime integration (D-05)
**Required now (the boundary):**
- Immutable facts (LLD §3.3–§3.6, §3.13), including `completed_at_utc`.
- `exchanges.loyalty_status = module_inactive`.
- No points shown on receipts or screens.
- Outbox events (LLD §20).
- No fake or no-op loyalty service and no runtime dependency.

**Activation prerequisites:**
1. Loyalty P0 gates passed and the business programme is **Active** with a go-live timestamp (Loyalty FR-09.1, D23).
2. The loyalty ledger is live with an idempotent unique `(BusinessId, EventKey, MovementType, SourceLineKey)`.
3. Checkout earning and redemption are live.
4. Historical boundary: Exchanges with `completed_at_utc` before go-live, or `module_inactive`, never receive automated point effects (Loyalty D18, FR-09.2, AT20; FR-EX-193).
5. Agreed keys `loyalty:exchange:{id}:v1` and `loyalty:exchange-reversal:{id}:v1`.
6. Loyalty liability accounting approved.
7. Loyalty OTP enforced when redemption applies (EX-18).

**Acceptance tests:**
- AC-22 / AT19;
- AC-23 / AT08;
- AT09, AT14, AT15;
- EXL-01-A: pre-go-live Exchange gives zero effect;
- EXL-01-B: duplicate event gives one effect;
- EXL-01-C: reversal compensates once;
- EXL-01-D: loyalty unavailable gives a durable `pending_reconciliation`, never fabricated success;
- EXL-01-E: family/third-party account reversal hits the original beneficiary.

## 9. Implementation verification gates (must be evidenced; none are claimed as passed by this document)
| Gate | Evidence |
|---|---|
| Backend build | `dotnet build` (Release while the API runs) |
| Angular production build + lint | `ng build --configuration production`, lint |
| Migration validation | `MigrationChainValidationTests`; Test-DB Up/Down rehearsal |
| PostgreSQL integration | New `Exchange` suite on the isolated Test DB (`RETAILPOS_TEST_POSTGRES`) |
| Journal balance and reconciliation | Every Exchange and reversal journal balanced; GL vs exchange totals reconcile |
| Return-quantity consistency | Σ refunds + Σ exchanges − Σ reversals = `returned_quantity` per line |
| Stock conservation, no negative stock | Ledger Σ = inventory per variant and location; CHECK and race tests |
| Cross-outlet valuation | Receiving-outlet movement value = original cost share (D-08) |
| Cash session and refund sufficiency | AC-04; payout races; close snapshot includes Exchange movements |
| Supervisor approval security | AC-31, AC-46, AC-47, AC-48 matrices; log capture shows no credentials; no POS session switch |
| High-value and eligibility-date correctness | AC-50 and AC-52 matrices incl. threshold equality and crossing local midnight |
| Phone verification honesty | AC-51; no "verified identity" label without evidence (VI-16) |
| Business and location authorization | C-08 suite |
| Concurrent Exchange and Refund | Race matrix (Stage 3) |
| Idempotent Confirm and Reversal | Replay and mismatch tests |
| Outbox replay and duplicate events | One envelope per Exchange; replay is idempotent |
| Exchange receipt generation | Snapshot, PDF and reprint tests |
| Net Sales and dashboard reconciliation | Stage 5 reconciliation |
| Regression | Existing Sale, Refund, Void, Inventory, Cash Session, UOM, dashboard and frontend suites green |

## 10. Documentation consistency rules
- Permissions: exactly the five `pos.exchange.*` keys plus existing `sales.view` and `settings.view`/`settings.edit`.
- Settlement statuses: none pending. Exchange status is `completed` or `reversed`.
- All amounts are server-calculated; return credit = original allocated net + VAT; replacement = current price.
- Dates: exchange and reversal business date from the cash session; never the original sale date.

## 11. Tracking
- Task IDs S0-xx…S5-xx are mirrored into `.claude/autonomous/TASKS.md` when implementation starts. EXL-01 is listed as deferred.
- Status flow per task: `planned` → `in-progress` → `implemented (tests passing)` → `reviewed` → `accepted`. EX-13 stays "approved / runtime deferred (EXL-01)".

---

## Appendix A — Decision traceability matrix (67 approved records: 31 EX + 19 D + 10 C + 7 OC)
| Decision | SRS | LLD | Gap | Stage | Tests / AC |
|---|---|---|---|---|---|
| EX-01 | §18 FR-EX-170–174; §19 FR-EX-180–181; FR-EX-202 | §3.11, §17, §23 | G-29, G-30 | 3, 5 | AC-01, AC-03, AC-43 |
| EX-02 | FR-EX-003, FR-EX-111, FR-EX-116 | §10.1, §16 | G-02, G-15 | 2, 3 | AC-02, AC-03, AC-48 |
| EX-03 | FR-EX-033, FR-EX-036, FR-EX-091–092, §3.2 | §6, §7, §7.1, §11 | G-05, G-04, G-47 | 2, 3 | AC-09, AC-10, AC-52 |
| EX-04 | §11 FR-EX-100–102 | §3.6, §3.8, §7, §7.1 | G-06, G-47 | 2, 3, 4 | AC-11, AC-47 |
| EX-05 | FR-EX-060–061, FR-EX-064 | §3.5, §13 | G-13, G-09 | 3 | AC-12 |
| EX-06 | §6 FR-EX-050–053 | §12, §13 | G-09, G-10, G-42 | 2, 3 | AC-08, AC-41 |
| EX-07 | FR-EX-110 | §16 | G-14 | 3 | AC-01, AC-06 |
| EX-08 | FR-EX-112 | §16 | G-15 | 3 | AC-03 |
| EX-09 | FR-EX-070–071 | §14 | G-07 | 3 | AC-05 |
| EX-10 | FR-EX-072 | §14 | G-08 | 3 | AC-24 |
| EX-11 | FR-EX-136, FR-EX-170, FR-EX-173 | §3.3, §3.11 | G-30 | 3, 5 | AC-01 |
| EX-12 | §15 FR-EX-140–143 | §3.12, §8 | G-22, G-23 | 3, 4 | AC-27, AC-36 |
| EX-13 | §20 FR-EX-190–194 | §3.3, §20 | G-34 | EXL-01 | AC-22, AC-23, AC-44 |
| EX-14 | §17 FR-EX-160–163 | §22 | G-32 | 4 | AC-20, AC-21 |
| EX-15 | FR-EX-120, FR-EX-123 | §15 | G-18 | 3 | AC-15, AC-16 |
| EX-16 | §3.1 FR-EX-010–013 | §12 | G-03 | 2 | AC-31 |
| EX-17 | FR-EX-135 | §3.3 | G-38 | 3, 4 | AC-25 |
| EX-18 | FR-EX-029, FR-EX-035, FR-EX-037–038, §3.2 | §10.1, §10.1.1, §11 | G-39, G-04, G-45 | 2, 3 | AC-31, AC-32, AC-50, AC-51 |
| EX-19 | FR-EX-152 | §9 (by-sale) | G-36 | 4 | Vitest entry tests |
| EX-20 | §19 FR-EX-180–183 | §23 | G-29 | 5 | AC-01, AC-03 |
| EX-21 | FR-EX-090 | §2, §3 | G-01 | 2, 3 | AC-06 |
| EX-22 | FR-EX-090, FR-EX-093 | §3.4 | G-01 | 3 | AC-06 |
| EX-23 | §5 FR-EX-041–043 | §10.1 | G-11 | 2, 3 | AC-07, AC-30 |
| EX-24 | FR-EX-030–031 | §9 | G-12 | 3, 4 | AC-13, AC-14 |
| EX-25 | FR-EX-030–031 | §9 | G-12 | 3, 4 | AC-13 |
| EX-26 | FR-EX-094 | §10.1 | G-05, G-12 | 3, 4 | AC-09 |
| EX-27 | §9 FR-EX-080–081 | §14 | G-08 | 3 | AC-24 |
| EX-28 | FR-EX-137 | §1 | G-35 | 2, 4 | AC-17 |
| EX-29 | §14 FR-EX-130–134 | §10 | G-37 | 3 | AC-18 |
| EX-30 | §16 FR-EX-150–153 | §21 | G-31 | 4 | AC-42 |
| EX-31 | FR-EX-121 | §15 | G-16 | 3 | AC-04 |
| D-01 | FR-EX-184, FR-EX-213 | §4, §24 | G-27 | 2 | AC-29 |
| D-02 | FR-EX-121–122 | §15 | G-16, G-17 | 3 | AC-04 |
| D-03 | §5 FR-EX-040–042 | §10.1 | G-11 | 2, 3 | AC-30 |
| D-04 | §3.2 FR-EX-020–021 | §11 | G-04 | 2, 4 | AC-31 |
| D-05 | §20 | §3.3, §20 | G-34 | EXL-01 (boundary in 3) | AC-44 |
| D-06 | §18 FR-EX-170, FR-EX-174 | §3.11, §23 | G-30 | 3, 5 | AC-43 |
| D-07 | FR-EX-003, FR-EX-172 | §3.1, §3.11 | G-30 | 2, 5 | AC-43 |
| D-08 | FR-EX-062 | §13 | G-09 | 3 | AC-08 |
| D-09 | FR-EX-113 | §3.9, §16 | G-15 | 3 | AC-28 |
| D-10 | FR-EX-051 | §9, §12 | G-10 | 2 | AC-41 |
| D-11 | FR-EX-011, FR-EX-153 | §12, §21 | G-31 | 4 | AC-42 |
| D-12 | FR-EX-182 | §23 | G-28 | 5 | Reconciliation |
| D-13 | FR-EX-031 | §9; v2.0 change table | G-12 | 3, 4 | AC-13, AC-14 |
| D-14 | FR-EX-095 | §24 (`SALE_HAS_RETURNS`) | G-26 | 2 | AC-33 |
| D-15 | §21 FR-EX-200–201 | §17 | G-21, G-25 | 0, 3 | AC-38 |
| D-16 | §2 FR-EX-001–005 | §3.1, §5 | G-02 | 2 | AC-34 |
| D-17 | FR-EX-014 | §25 | G-03 | 2 (code), 5 (seed) | Role template test |
| D-18 | FR-EX-103, FR-EX-164–165 | §22, §22.1 | G-33, G-46 | 4 | AC-35, AC-49 |
| D-19 | FR-EX-140–141 | §8 | G-22 | 2 | AC-36 |
| C-01 | FR-EX-010 | §12 | G-03 | 2 | AC-32 |
| C-02 | §3.2 FR-EX-020–025 | §11 | G-04 | 2, 4 | AC-31 |
| C-03 | FR-EX-091, FR-EX-102 | §6, §18 | G-05, G-06, G-43 | 2, 3 | AC-09, AC-19 |
| C-04 | FR-EX-133–134, FR-EX-183 | §10.2, §17–§20 | G-19, G-37, G-41 | 3 | AC-18, AC-40 |
| C-05 | FR-EX-161 | §22 | G-32 | 4 | AC-20 |
| C-06 | FR-EX-115 | §16, §17 | G-20 | 3 | AC-37 |
| C-07 | FR-EX-200–201 | §17 | G-21, G-25 | 0, 3 | AC-38 |
| C-08 | §23 FR-EX-220–224 | §12, §26 | G-40 | 2, 5 | AC-26, AC-41, AC-42, AC-45 |
| C-09 | FR-EX-034, §22 FR-EX-210–213 | §13 | G-24 | 0, 3 | AC-39 |
| C-10 | FR-EX-021, FR-EX-026 | §11 | G-04 | 2 | AC-32 |
| OC-01 (A) | FR-EX-021, FR-EX-027 | §1 (outlet access row), §3.10, §10.2 step 6, §11 step 6, §12 | G-04; VI-04 | 0, 2, 3 | AC-31(i), AC-46 |
| OC-02 (A) | FR-EX-020, FR-EX-028, FR-EX-101 | §3.10, §7, §11 | G-04, G-06 | 2, 3, 4 | AC-47 |
| OC-03 (B) | FR-EX-020–021, FR-EX-111, FR-EX-116 | §3.10, §10.1 step 10, §10.2 step 7, §11, §12, §16 | G-04, G-15 | 2, 3, 4 | AC-48 |
| OC-04 (A) | FR-EX-164–165 | §22.1 | G-46 | 0, 4 | AC-49 |
| OC-05 (C) | FR-EX-029 | §3.1, §3.3, §10.1 step 9, §11 | G-39 | 2, 3 | AC-50 |
| OC-06 (A) | FR-EX-035, FR-EX-037–038 | §3.3, §10.1.1 | G-39, G-45; VI-06, VI-16 | 0, 2 | AC-51 |
| OC-07 (A) | FR-EX-033, FR-EX-036, FR-EX-100 | §3.3, §7, §7.1, §10.1 | G-47; VI-17 | 0, 2, 3 | AC-52 |

---

## 12. Implementation status (autonomous implementation, 2026-11-07, updated 2026-11-08)
Status of the working tree. Nothing is committed. Nothing is applied to any shared database.

| Stage | Status | Evidence |
|---|---|---|
| 0 | Done | Stage 0 audit; database tests now executed on the isolated A2 server (below) |
| 1 | Done | Contracts implemented as documented in LLD v2.2, with the deviations listed below |
| 2 | Done | Permissions (5), templates (D-17, new-business templates only), D-14 void guard, D-01 rejection, entities, EF configuration, migration `20261107221022_PosV2ExchangeFoundation` (incl. cash CHECK extension), policy API (DN-01), source lookup |
| 3 | Done (core) | Preview, supervisor approvals, atomic confirm (stock, cash, journal, attribution, receipt snapshot, outbox) |
| 4 | Done | Reversal (`pos.exchange.reverse`), history, detail, by-sale, receipt document/PDF/reprint; Angular workflow, approval dialog, receipt, history, settings, POS and Sale Details entry points; re-exchange of replacement lines via exchange-number lookup (EX-04) |
| 5 | Done (code); UAT pending | Owner dashboard and mobile report facts by business date, exchange metrics, ledger labels; independent code and security review (§12.2) |

**Tests executed (local, isolated):**
- Unit tests: 1,094 passed (baseline 1,054).
- Exchange PostgreSQL HTTP tests: 5 passed. They cover:
  - T-01 commit;
  - T-01 rollback by fault injection after the stock, cash and journal writes;
  - C-03 and T-03 refund-vs-exchange race;
  - rules (D-01, DN-01, EX-02, EX-05, OC-03);
  - supervisor approval (C-02, OC-01, single use).
- Integration regression subset (Sales, UOM, CashSession, Drawer, Phase3, Outlets, P308, MigrationChain): 520 passed.
- Full integration suite before the review fixes: 2,744 total, 2,740 passed, 4 failed. Three are `LiveInfrastructure` tests whose RabbitMQ, Elasticsearch and Redis test services were not running. The fourth, `DurableDeliveryFoundationTests.InboxDuplicates…`, then passed 3 out of 3 times on its own and inside the 511-test regression run below, so it only fails under full-suite load (Exchange does not use the inbox).
- After the review fixes (2026-11-08): unit 1,094 passed. Sale concurrency and void tests: 15 passed. Regression (UOM incl. Exchange PG, Sales, DurableDelivery, OwnerDashboard, SalesReport, CashSession, Drawer, MigrationChain): 511 passed, 0 failed.
- One test changed: the concurrent-void test's barrier moved from the sale read to the sale lock, the same as the existing concurrent-refund test. The requirement is unchanged: exactly one void wins and the other gets "Sale is already voided".
- Frontend: 2,039 Vitest tests passed; production build succeeded.
- Not executed: browser/UAT walk-through, the historical data audit Q1–Q11 (TB-02), and migration on any shared database.

### 12.1 Implementation decisions (technical; no business rule changed)
| # | Decision | Reason |
|---|---|---|
| I-01 | Replacement lines ignore any client unit price. The **preview hash** binds what the cashier reviewed; Confirm recomputes and returns `409 EXCHANGE_PREVIEW_STALE` on any change. | Server-authoritative pricing (EX-10) without a second price-assertion path |
| I-02 | The exchange id is reserved from its identity sequence. The header and lines are inserted **once**, with final values, after stock, cash and journal work. | Lets the database immutability triggers stay strict (EX-14) |
| I-03 | The exchange number is allocated late, from its own counter table. | Short lock hold; no cross-workflow lock edge |
| I-04 | Exchange cash is reported inside the existing close-summary totals: collections as cash received, refunds as cash refunds. The calculator now **fails** on unknown movement types. | No close-snapshot schema change; nothing can silently vanish from the drawer |
| I-05 | `CashSessionMovementRecorder` takes a drawer advisory lock for every outflow (locking only). | D-02 race safety against refund payouts and drawer expenses |
| I-06 | A refund uses one method (split refunds are not supported). | LLD §16 v1 scope |
| I-07 | A re-exchanged replacement's packaging is not restored; the item returns as loose stock. | Conservative UOM handling; cost stays expensed |
| I-08 | Approval tokens: 256-bit random, stored hashed, 5-minute lifetime. Rate limit `exchange-approval`: 5 per minute **per remote IP**. *Corrected 2026-11-08:* the limiter runs before authentication, so it never saw the cashier, despite the earlier wording. Per-login and per-cashier lockout was added in the use case (§12.3, L1b). | C-02 "short-lived"; brute-force protection |
| I-09 | `SaleUomReversal.RestoreAsync` gained optional location and reference parameters; the defaults keep refund and void identical. | Reuse UOM D08 restoration at the receiving outlet (EX-06) |
| I-10 | Exchange CHECK constraints use `trim()` (not `btrim`). | Portable to the legacy SQLite-based test suites |
| I-11 | Legacy checkout exchange-link tests were updated to assert `SALE_EXCHANGE_LINK_DEPRECATED`. | D-01 |
| I-12 | Three reporting defaults. They were resolved on 2026-11-08 against approved rules; see §12.4. | §12.4 |

### 12.2 Independent review (2026-11-08)
Fixed in code and re-verified:

| # | Finding | Fix |
|---|---|---|
| M1 | An idempotent replay could race a concurrent first request. | Confirm and Reverse re-check the idempotency key after taking the sale/exchange row lock. |
| M2 | Void could take locks in a different order from Exchange (deadlock risk). | Void locks the sale row first (`ISaleVoidStore.LockSaleAsync`). |
| L1 | Supervisor approval answered faster for unknown e-mails (user enumeration by timing). | An unknown e-mail still runs a password verification against a dummy hash. |
| L2 | The replacement-line lock SQL did not filter by business. | It joins `exchanges` and filters `business_id`. |
| L5 | Confirm required accounts for every slot, even unused ones. | Only the slots used by the composed journal are required. |

The remaining findings (L1b, L3, L4, L6) were first accepted, then fixed in the hardening pass (§12.3).

### 12.3 Post-implementation hardening (2026-11-08)
| # | Finding | Root cause | Fix | Evidence (PostgreSQL, isolated A2 clones) |
|---|---|---|---|---|
| L4 | After a reversal, dashboard COGS was lower than the ledger and journals by the value of packaging issued in a fraction. Reproduced: dashboard 40.00, ledger and journals 45.00. | The reversal journal keeps that packaging in COGS (I-07). The dashboard reversed the full `ReplacementCost`, because the retained value was never stored. | New column `exchange_reversals.retained_packaging_cost` (CHECK ≥ 0), written by Reverse. The dashboard reverses `ReplacementCost − RetainedPackagingCost − ReturnCost`. History is untouched: existing reversals default to 0, which equals the old behaviour. | `ExchangeReversal_KeepsFractionalPackagingInDashboardCogs_ReconcilingWithLedgerAndJournals`: dashboard = stock ledger = journals = 5.00 retained |
| L6 | The terminal was resolved again at Confirm, but three gaps remained. | <br>• An inactive or foreign terminal raised a raw exception (400 with an internal message). <br>• The resolved terminal was not part of the preview hash. <br>• The row was not locked. | <br>• `ExchangeTerminals.ResolveAsync` returns `EXCHANGE_SESSION_REQUIRED` (422) with an actionable message. <br>• The terminal is bound into the preview hash. <br>• Confirm and Reverse lock the terminal `FOR SHARE` (active, of the receiving outlet and business) before the cash session. | `ExchangeConfirm_RevalidatesTheTerminal_…` (deactivated, other outlet, changed since preview → 409 stale, two active terminals without a choice); `ExchangeReverse_RefusesWhenTheOutletHasNoActiveTerminal_…` |
| L1b | No per-account protection, and the "per IP and cashier" limiter was effectively per IP (I-08). | The rate limiter runs before authentication. | Failed sign-ins are recorded in the new table `exchange_approval_failures`. Only SHA-256 keys are stored: the login as typed (normalised) and the cashier, both within the business. <br>• 5 failures per login, or 10 per cashier, in 15 minutes → `429 EXCHANGE_APPROVAL_LOCKED`. The check runs before any credential check. <br>• A success clears the failures. <br>• An unknown login locks exactly like a real one. <br>• The approval stays one-time. <br>• The limiter key now honestly reads per IP. | `ExchangeApproval_LocksTheSupervisorLoginAcrossCashiers_UnknownLoginsAlike_…`; 3 unit tests on the keys |
| L3 | `by-sale/{saleId}` answered 404 for another business's sale but 200 with an empty list for a same-business sale at an unseen outlet, which revealed that the sale exists. | The visibility check covered the business only. | A sale outside the user's readable outlets is answered exactly like a missing sale (404 `EXCHANGE_SOURCE_NOT_FOUND`), the same as detail and receipts. | `ExchangeHistory_DetailBySaleAndReceipts_FollowBusinessAndOutletScope` (updated assertion) |

**Migrations added** (generated; tested only on disposable databases; not applied to any shared database):
- `20261108093416_PosV2ExchangeReversalRetainedCost`: additive column with default 0, plus a CHECK.
- `20261108094509_PosV2ExchangeApprovalFailures`: new table, index and CHECK.

**Frontend:**
- `EXCHANGE_SESSION_REQUIRED` and `EXCHANGE_APPROVAL_INVALID` now show the server's specific message.
- `EXCHANGE_APPROVAL_LOCKED` and a body-less 429 have their own messages.
- New spec: `exchange-errors.spec.ts`.

**Test-only infrastructure:** `TestRemoteIpStartupFilter` (Postgres API factory) lets a test act as several network sources through `X-Test-RemoteIp`. Requests without the header are unchanged.

**Final regression after hardening (2026-11-08, exact results):**

| Suite | Result |
|---|---|
| Backend unit | 1,097 passed, 0 failed, 0 skipped |
| Backend build (Release, solution) | Succeeded, 0 errors |
| Integration, complete PostgreSQL suite with `Category!=LiveInfrastructure` | 2,733 passed, 0 failed, 0 skipped. This includes T-01…T-05 and all Exchange, Reversal, Refund, Void, Sales, Accounting and migration-chain tests. |
| LiveInfrastructure (15 tests) | **Not run.** The Test-stack RabbitMQ 5673, Redis 6383 and Elasticsearch 9201 are not running; only Dev services and Test Postgres are up. |
| Frontend unit (Vitest) | 2,043 passed (147 files) |
| Frontend production build | Succeeded. The `pos-screen.component.css` budget warning was already there. |
| Browser UAT | **Not executed** (checklist only) |

**Load-only flake:** `DurableDeliveryFoundationTests.InboxDuplicatesRepeatSuccess_…` failed once, in the 2026-11-07 full run. It has not recurred in 5 runs since: 3 alone, 1 in a 511-test subset and 1 in this full run. It is not disabled and not related to Exchange (Exchange does not use the inbox). It remains under observation.

### 12.4 Reporting defaults I-12, resolved against approved rules
| # | Exact issue | Constraining decisions | Resolution |
|---|---|---|---|
| 1 | The exchange metrics gross and net figures are shown **net of reversals**. An exchange still counts once, on its own business date, whatever its status. A reversal is a separate, offsetting fact on the reversal's business date. | FR-EX-163 (compensating reporting facts on the reversal business date); FR-EX-181 (reversals apply on the reversal date; original-date figures never rewritten); FR-EX-180 | **Determined by approved rules. Kept, no change.** Excluding reversed exchanges from their original date would rewrite history and break FR-EX-181. |
| 2 | In the mobile sales report, a reversal is netted under the **original exchange's cashier**, not the user who reversed it. | FR-EX-173 (original attribution never changes); FR-EX-170 (the salesperson-attribution fact of the reversal keeps its own actor, unaffected); the existing refund rule in `SalesReportReadStore` (refunds are netted under the original sale's cashier) | **Kept, following the existing refund precedent** (no new policy). Attributing reversals to the reversing user would be a new rule that refunds do not follow; that is owner review only. Money totals are identical either way; only the per-cashier split differs. |
| 3 | A reversed exchange number in the source lookup answered "not found". | C-05 and FR-EX-162 (the customer hands back the replacements; no partial reversal); Preview and Confirm already refuse the lines with `EXCHANGE_SOURCE_INELIGIBLE` | **Determined, and aligned:** the lookup now returns 422 `EXCHANGE_SOURCE_INELIGIBLE` ("That exchange was reversed…"), the same answer as Preview. A reversed exchange is still never a re-exchange source. Evidence: `ExchangeReversal_IsBlockedWhileTheReplacementIsReExchanged_…` |

### 12.5 Finance validation (read-only code analysis, 2026-11-08)
**VI-20 is confirmed and pre-existing; it is a release blocker.**
- `AccountingDefaults.ChartOfAccounts` has no output-VAT account.
- `AccountingConfigurationProvisioner` sets `VatPayableAccountId` to **2000 Supplier Payables** for every newly provisioned business.

Effects on such a business:
- Output VAT from sales, refunds and exchanges is credited to Supplier Payables, so trade payables are overstated.
- The VAT reconciliation report takes "GL output VAT" from the net credit on account 2000, which also carries GRN and supplier-payment postings. It cannot reconcile.

Smallest backward-compatible correction (**proposal; not implemented; needs Finance approval**):
1. Add a default chart account, for example `2200 Output VAT Payable` (Liability, credit).
2. Map `vat_payable` to it **only when provisioning new businesses**.
3. Existing businesses are not changed automatically. Finance decides, per business, whether to remap through the existing Accounting Configuration screen, from a chosen date, and whether a reclassification journal is needed. Posted journals are never rewritten.

Read-only query for Finance to size the impact:

```sql
SELECT ac.business_id FROM accounting_configurations ac
WHERE ac.vat_payable_account_id = ac.supplier_payable_account_id;
```

**VI-03 is documented. Finance confirmation stays a release gate.** `PaymentPostingPolicy.ResolveSlot` behaves as follows:

| Method | Before activation (or no activation date) | From the activation date |
|---|---|---|
| `card` | Bank slot | Card Clearing slot |
| `mobile` / `mfs` | Bank slot | Mobile/MFS Clearing slot |
| any other method | Bank slot | Bank slot |

- The activation date is `BankClearingActivationDate`, compared with the business date.
- Sales use the sale's business date; exchanges and reversals use the cash-session business date. That is consistent.
- An unmapped slot throws (`EXCHANGE_PAYMENT_ACCOUNT_MISSING`); there is no silent fallback.
- Exchanges and new sale checkouts (`SalePaymentPlanner`) both accept only cash, card and mobile, so new documents never reach the "any other method" fallback. It could only apply to historical rows posted with another method label.
- No approved accounting document outside the Exchange set defines the pre-activation rule. Whether "card/mobile → Bank before activation" is policy, as C-06 requires, needs Finance confirmation.


### 12.6 Browser UAT and end-to-end verification (2026-11-08)
**Environment:** see `IRANIMART_POS_V2_EXCHANGE_UAT_ENVIRONMENT.md`.
- Persistent synthetic database `iranimart_pos_v2_exchange_uat` on the Test PostgreSQL server, with all 80 migrations including the three Exchange migrations.
- Provisioning tool `tools/RetailPOS.ExchangeUat`.
- UAT API on `127.0.0.1:5105`; UAT web on `127.0.0.1:4200`.
- Playwright 1.52.0 driving the installed Microsoft Edge.
- No shared database touched; no real users or roles seeded.

**Result:**
- 25 of 25 browser tests passed; 31 of 43 checklist IDs passed in a real browser. The other 12 are not automated in the browser and are covered by PostgreSQL or Vitest tests.
- `reconcile`: OK over 54 exchanges and 4 reversals.
- Details: UAT checklist §10.

| # | Defect found in the browser | Severity | Root cause | Fix and regression test |
|---|---|---|---|---|
| DEF-01 | A SalesPerson or OutletManager of a newly onboarded business could not check, open or close the cash session. The POS and Exchange screens got 403 on `GET /api/pos/cash-sessions/active`, so the exchange was blocked ("session could not be checked"). | High (pre-existing; affects POS too) | Migration `PosV2P3CashSessionManagement` gives every role with `sales.create` the keys `cash_sessions.view/open/close`. The onboarding templates in `BusinessRolePermissions` (OutletManager, SalesPerson) grant `sales.create` without them, so roles created by onboarding after that migration lack them. | Templates aligned with that rule (only those three keys). New test `DefaultRoleTemplates_ThatCreateSales_CanViewOpenAndCloseTheCashSession`. **Existing roles in shared environments are not changed**: updating them is an owner decision (see release gate EXB-08). |
| DEF-03 | A supervisor-approval 403 (for example a supervisor without access to the outlet) also raised the global "You do not have permission to perform this action" modal over the dialog's specific message. | Medium (misleading; blocks the dialog) | The global error interceptor alerts on every 403. | Approval, confirm and reverse calls use the existing `SUPPRESS_ERROR_NOTICE` token; their screens show the specific error. 403 still never signs out. `exchange.service.spec.ts` |
| DEF-04 | A reversed exchange number showed the generic "voided, fully refunded…" text instead of "That exchange was reversed…". | Low (undermined I-12 item 3 in the UI) | The fixed message overrode the server's specific reason. | `EXCHANGE_SOURCE_INELIGIBLE` now shows the server message. `exchange-errors.spec.ts` |
| DEF-02 | The session business date was shown as a raw timestamp `2026-11-08T00:00:00`. | Low | No date formatting | `appDate:'dd/MM/yyyy'`, as on the POS screen. `exchange-workflow.component.spec.ts` |

**Observation (not changed):** history rows of a re-exchange (a source that is itself an exchange) show an empty "Original invoices" column. This is a UX choice for the owner.

**Regression after the fixes:**
- Backend unit: 1,097 passed.
- Focused integration (Exchange PostgreSQL, RBAC/Security, onboarding, migration chain): 167 passed.
- Frontend: 2,046 passed; production build OK.
- Browser: 25 of 25 passed.

### 12.7 Exchange UI/UX and replacement product search (2026-11-08, owner UAT feedback)
No business rule, calculation, authorization or API contract was changed. All prices and stock come from the server, and the server preview stays authoritative.

| # | Problem | Root cause | Fix |
|---|---|---|---|
| UX-1 | Step 1: the invoice input and Find button were misaligned | `.ex-row` aligned items to the bottom of the form field, and that field also holds the hint and error, so the button lined up with the hint. | A dedicated lookup layout: label, then input and button in one row (same control height), then the error and guidance below. The button is disabled until text is entered. |
| UX-2 | Replacement search found exact barcode/SKU only, with no result list | The screen called only `GET /api/pos/lookup` (exact barcode, then exact SKU). | New `ExchangeProductSearchComponent`. It reuses the existing server search `GET /api/pos/catalogue?q=` (business-scoped; partial match on product name, variant name, SKU, barcode and main product code; stock at the outlet; selling price; no cost) through `PosService.searchCatalogue`. The exact `/api/pos/lookup` is the scanner fallback. |
| UX-3 | The wizard looked unfinished | Inconsistent layout, raw scanner field, Next always enabled | Changes: <br>• A product result list with image or fallback, name, main code, attributes, SKU, barcode, selling price and outlet stock. <br>• Out-of-stock items are shown but cannot be added. <br>• Selected lines show code, attributes, SKU, unit, price and stock. <br>• Next is disabled until the step is complete, with the reason shown. <br>• Next stays at the trailing edge on every step. |

**Reused:**
- the POS catalogue search endpoint and DTO;
- `PosService`;
- `ProductImageService.resolveUrl`;
- the POS variant-attribute formatter, moved to `pages/pos/pos-variant-attributes.ts` and used by both screens (POS behaviour unchanged);
- the existing UOM picker and quantity model (picking a variant already in the list adds one).

**Search behaviour:**
- A 250 ms debounce and a 2-character minimum.
- Exact barcode or SKU first, then exact main code, then prefix matches.
- Enter adds a single exact barcode/SKU match at once (scanner path).
- Typing drops the highlight from the previous results. The browser run found that a scan typed over open results could otherwise add the stale highlighted item.
- Only the latest request can update the list.
- Arrow keys, Enter and Escape work; it is an ARIA combobox/listbox.
- Loading, no-results, and error-with-retry states.

**Shared UI kit:** `app-ui-stepper` gained an optional `nextDisabled` input, and Next now keeps the trailing edge on the first step as well. This applies to every stepper.

**Tests:**
- New Vitest specs: `exchange-product-search.component.spec.ts` (13 tests), workflow layout, Next and replacement tests, and a stepper `nextDisabled` test.
- Full frontend suite: 2,064 passed; production build OK.
- Backend: `CatalogueSearch_ByMainProductCode_ReturnsEveryVariantOfOwnBusinessOnly`. It covers partial and case-insensitive matching, every variant, no cost, and the same code in another business staying hidden. Catalogue suite: 8 passed.
- Browser (Playwright/Edge, persistent UAT database): 33 of 33 passed, including the new `05-replacement-search.spec.ts`. That spec covers:
  - Step 1 measured alignment at 1440, 768 and 390 px;
  - `ABA852P` returning 4 variants;
  - keyboard selection, barcode scan and repeat;
  - out-of-stock refusal;
  - partial name and SKU;
  - the responsive layout;
  - a completed high-value exchange chosen by product code.
- Reconciliation OK: 73 exchanges, 78 journals.

**Evidence** (in `e2e/exchange-uat/evidence/`):
- `before/step1-before-*.png` and `screenshots/S1-after-*.png`;
- `S3-search-ABA852P-results.png`, `S3-selected-replacement-details.png`, `S3-barcode-scan-and-repeat.png`;
- `S3-search-390.png`, `S3-search-768.png`, `S3-selected-*.png`;
- `S3-exchange-completed-with-searched-product.png`.

**Limitations:**
- The server search does not match inside variant attribute values (for example "Navy"); variant names do match.
- The exact `/api/pos/lookup` returns no main code or attributes, so a code found only through that fallback shows its variant name instead.
- The catalogue `listPrice` shown in the list is the list selling price; any pricing rules apply in the server Review.
- The synthetic `ABA852P` product exists only in the UAT database. The Dev database was not used.
