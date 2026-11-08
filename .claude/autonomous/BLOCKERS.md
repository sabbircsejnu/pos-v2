# Owner Dashboards — blockers and decisions awaiting sign-off

None of these stop the build. Each has a safe fallback in place until decided.

| ID | Decision needed | Owner | Current fallback | Status |
|---|---|---|---|---|
| B-01 | Sales targets are not implemented (`OutletSalesTargetVersion`). Build them? This needs a migration. | Business owner, tech lead | Target, Shortfall and Achievement show N/A | open |
| B-02 | Confirm P2-06 weighted-average ledger replay as the owner-facing stock valuation. It can differ from the operational Stock Valuation Report (`CostPrice + CostAdjustment`). | Finance | P2-06 replay; unvalued stock flagged as partial | open |
| B-03 | Exchange netting policy. Policy approved in `Docs/08_Sales_And_POS/Exchange/` (EX-01, EX-20, D-12). Close only after Exchange reporting is implemented and reconciled (Exchange Plan S5-05). | Business owner, finance | Counted as posted, with a warning | open |
| B-04 | Approve slow-moving and transfer thresholds (LLD-D05, D06, D07). | Business owner | LLD proposed defaults | open |
| B-05 | Reversals reduce the period of their accounting date, not the original sale's period. | Finance | Matches P3.2-D02 | open |
| B-06 | Figma screenshots are not in the repo. Able Pro licence review is pending. | Business owner | Layout from SRS text; no Able Pro assets copied | open |
| B-07 | Should Purchase and Expense KPIs need `reports.financial`? | Business owner | Decided: two dashboard permissions only (SRS §2.1) | resolved |

# UOM v1 — decisions awaiting sign-off

None of these stop the build. Details: `Docs/Unit_Of_Measurement/IMPLEMENTATION_REPORT.md` §11.

| ID | Decision needed | Owner | Current fallback | Status |
|---|---|---|---|---|
| U-01 | Approve the six new permission keys. | Business owner | Approved (I01), verified by a permission matrix test | resolved |
| U-02 | Allow more than one refund per sale (RET-002)? | Business owner, finance | Approved (I02) and implemented | resolved |
| U-03 | Should pricing rules apply to pack and custom prices? | Business owner | Approved per option (I03) and implemented | resolved |
| U-04 | Show the refund button for pcs-only sales? | Business owner | Approved (I04): shown for every eligible sale | resolved |
| U-05 | Sign off repack Pattern P1. | Finance | Approved (I05); all twelve proofs pass | resolved |
| U-06 | Apply the migration to Development. | Tech lead | Approved for Development; gates passed and rehearsed, but this session was not permitted to run it | blocked |
| U-07 | Update Flutter stock take for fractional quantities before wiring it to the API. | Tech lead | Decimal counts are now refused with a message; decimal mobile counting not built | mitigated |
| U-08 | Confirm that opening a prepared pack may return a reusable bottle to packaging stock at its capitalized value (SRS PACK-006/REP-008). | Finance | Approved as I06 Option A (reusable bottle recovery) | resolved |
| U-09 | VAT report omits SalesReturnPosting refund VAT (existed before UOM). | Finance, tech lead | Fixed under I07: posted refunds and voids are reported | resolved |
| U-10 | Remove the rehearsal DB uom_restore_check_20261107094240 (test server) and container /tmp files; the session safety check blocked the deletion. | Tech lead | Disposable, harmless | open |
| U-11 | Independent review of commit dcfcc48f; the review sub-agent was refused by the session permission control. | Tech lead | Done in round 3; Medium findings fixed | resolved |
| U-12 | Apply the three UOM migrations to Development; MigrateOnStartup refused twice in session. Script, backup and rehearsal are ready (UOM_RELEASE_READINESS.md section 9). | Tech lead | Applied by the owner; verified (17 totals identical) | resolved |
| U-13 | Should inventory reports (`reports.inventory`) require `products.view_cost` for cost and value columns? About 15 reports and exports show them today (existed before UOM). | Business owner | Option A approved and implemented (9cd6da61, 0e71c1d5) | resolved |
| U-14 | Does `dashboard.inventory_intelligence.view` imply stock value, or should owner dashboard inventory value also need `products.view_cost`? | Business owner | Unchanged | open |

# Product Exchange — release gates (2026-11-08)

Code complete and test verified on isolated databases. These gates block UAT and production, not the build.

| ID | Gate | Owner | Status |
|---|---|---|---|
| EXB-01 | VI-20: default `vat_payable` points at 2000 Supplier Payables. Decide the correction (proposal in Exchange Plan §12.5) and handle existing businesses. | Finance | open |
| EXB-02 | VI-03: confirm that pre-activation card/mobile → Bank is policy (C-06). | Finance | open |
| EXB-03 | TB-02: historical data audit Q1–Q11 needs an authorized non-production copy or read replica. | Owner, Finance | open |
| EXB-04 | TB-01/TB-06: approve applying the 3 Exchange migrations to a UAT/shared database, and setting up exchange roles for existing businesses. | Owner | open |
| EXB-05 | Browser UAT: automated run executed 2026-11-08 (31 of 43 IDs passed in the browser, 0 failed; checklist §10). Human sign-off (§9), physical 80 mm print and the 12 non-automated IDs remain. | Operations, Finance | partially done |
| EXB-06 | DN-01: `required` phone verification unavailable (G-45). This is a release limitation. | Owner | accepted limitation |
| EXB-07 | LiveInfrastructure tests not run: the Test-stack RabbitMQ, Redis and Elasticsearch containers are not running. | Tech lead | open |
| EXB-08 | DEF-01: decide whether to add `cash_sessions.view/open/close` to the existing `OutletManager`/`SalesPerson` roles in shared environments (the rule from migration PosV2P3CashSessionManagement; the templates are fixed in code). This is a role data change, so it needs approval. | Owner | open |
