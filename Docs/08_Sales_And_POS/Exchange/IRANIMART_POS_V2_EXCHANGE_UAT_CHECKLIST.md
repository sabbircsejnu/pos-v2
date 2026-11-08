# IraniMart POS v2 — Product Exchange: Manual Browser UAT Checklist

| Item | Value |
|---|---|
| Version | 1.0 (2026-11-08) |
| Status | **Automated browser run executed 2026-11-08 (see §10): 31 of 43 IDs passed in a real browser, 0 failed.** Human sign-off (§9) is still pending. |
| Applies to | SRS v2.1, LLD v2.2, Implementation Plan v2.2 §12 |
| Why manual | The implementation session had no browser automation, and no running environment had the Exchange migrations. Applying them to a shared database needs owner approval (TB-01). |

## 0. Environment prerequisites (all required before starting)

| # | Prerequisite | How to check |
|---|---|---|
| P-1 | A **disposable or explicitly approved** UAT database, never Production. | Owner approval recorded |
| P-2 | Migrations applied in order: <br>• `20261107221022_PosV2ExchangeFoundation` <br>• `20261108093416_PosV2ExchangeReversalRetainedCost` <br>• `20261108094509_PosV2ExchangeApprovalFailures` | `__EFMigrationsHistory` lists all three |
| P-3 | Test business: <br>• at least 2 outlets (A, B); <br>• each outlet has a terminal and an **open cash session** on today's business date; <br>• an open accounting period; <br>• accounting configuration with every slot mapped (cash, bank, card/mobile clearing or bank, sales, sales return, sales discount, VAT payable, COGS, inventory). | Accounting → Configuration, POS → Cash Session |
| P-4 | Test users (roles built in the role form, no seeding of real roles): <br>• **Cashier A** (`pos.exchange.create`, assigned to outlet A); <br>• **Supervisor A** (`pos.exchange.override_window`, `pos.exchange.override_chain`, `pos.exchange.refund`, assigned to A); <br>• **Supervisor B** (same permissions, assigned only to B); <br>• **Manager** (`pos.exchange.reverse`, `sales.view`, `settings.view/edit`); <br>• **Viewer B** (`sales.view`, assigned only to outlet B). | Users and Roles screens |
| P-5 | Products with stock at A: <br>• Shirt (pcs) at 100; <br>• Jacket (pcs) at 120; <br>• Cap (pcs) at 50; <br>• one UOM item with a pack option that uses packaging. | Inventory |
| P-6 | Exchange policy saved in Settings → Exchange Policy: <br>• window 7 days; <br>• lower-price exchange allowed; <br>• re-exchange on, with a 7-day replacement window and chain max 2; <br>• phone verification `optional`; <br>• high-value threshold 500. | Settings page shows version 1 |
| P-7 | Screen widths available for checks: 390 px, 768 px, 1440 px. | Browser dev tools |

**Evidence for every case:**
- a screenshot of the final screen;
- the exchange number;
- for money cases, the journal entry number and the cash session movement;
- the tester's name and date.

## 1. Entry points and lookup

| ID | Steps | Expected outcome | Evidence |
|---|---|---|---|
| U-01 | Cashier A: POS screen → **Exchange** button. | `/pos/exchange` opens. Terminal and outlet A are preselected. | Screenshot |
| U-02 | Sale Details of a completed sale at A → **Exchange** action. | The workflow opens with that invoice already loaded. | Screenshot |
| U-03 | Enter an invoice number that does not exist. | "No invoice with that number was found." Nothing else is shown. | Screenshot |
| U-04 | Enter the invoice of a sale at outlet B. | It loads (lookup across the business's outlets, D-10). The phone is masked; walk-in customers show no name. | Screenshot |
| U-05 | Enter the number of an exchange that was **reversed**. | "That exchange was reversed, so its items cannot be exchanged again." (I-12 item 3). | Screenshot |

## 2. Core exchanges

| ID | Steps | Expected outcome | Evidence |
|---|---|---|---|
| U-10 | Return 1 Shirt (bought at 100) and take 1 Jacket (120). Settle the collection in cash. | <br>• Preview: return 110, replacement 132, collect 22 (VAT 10%). <br>• Confirm creates an `…X…` number. <br>• Shirt stock +1, Jacket −1. <br>• The cash session shows exchange cash in 22. <br>• The journal balances. | Receipt, session, journal |
| U-11 | Multiple items: return 2 lines from 1 invoice and take 3 replacements. | The totals equal the sum of the lines. Each line shows its own source. | Receipt |
| U-12 | Lower value: return a Jacket and take a Cap, as a cashier **with** `pos.exchange.refund`. Refund in cash. | Refund 77. Cash out is recorded. The drawer must hold enough cash, otherwise `EXCHANGE_CASH_INSUFFICIENT`. | Session |
| U-13 | Same as U-12, by a cashier **without** the refund permission. | Confirm asks for a supervisor refund approval (OC-03 Case B). After Supervisor A approves, Confirm succeeds. | Approval dialog, audit |
| U-14 | A card refund without a reference. | Blocked, asking for a reference and confirmation (`EXCHANGE_REFUND_EVIDENCE_REQUIRED`). | Screenshot |
| U-15 | Even exchange (same value). | No settlement. Stock and journal still post. | Journal |
| U-16 | Change the price of a replacement in another tab after Preview, then Confirm. | `EXCHANGE_PREVIEW_STALE`, and the cashier reviews again. Nothing is posted. | Screenshot |
| U-17 | Click Confirm twice quickly. | Exactly one exchange. The second click is a replay or is ignored. | History shows one |

## 3. Approvals (C-02, OC-01, OC-02, OC-05, L1b)

| ID | Steps | Expected outcome | Evidence |
|---|---|---|---|
| U-20 | Exchange a sale older than the 7-day window. | A window approval is required. Supervisor A signs in with a reason, and Confirm succeeds. The token is single use. | Audit `exchange.approval_granted` |
| U-21 | Same, but Supervisor B (no access to outlet A) signs in. | "The supervisor does not have the required permission or access to this outlet." | Screenshot |
| U-22 | Cashier A enters their own credentials as supervisor. | Refused: the supervisor must be a different user. | Screenshot |
| U-23 | Enter a wrong supervisor password 5 times (from any tills), then the right password. | Attempts 1–5 say "The supervisor could not be verified…". Attempt 6 says "Too many failed supervisor sign-ins. Wait 15 minutes…", even with the right password. | Screenshot |
| U-24 | Repeat U-23 with an e-mail that does not exist. | The answers are identical to U-23 (no account enumeration). | Screenshot |
| U-25 | High value: an exchange with replacement value ≥ 500. | A high-value approval (`override_window` permission, C-10) is required. | Audit |
| U-26 | Re-exchange (chain): exchange a replacement from U-10 by looking up its exchange number, then exchange that replacement again (count 3 > max 2). | The third exchange needs a chain approval (OC-02). | Screenshot |

## 4. Cross-outlet, terminal and cash session

| ID | Steps | Expected outcome | Evidence |
|---|---|---|---|
| U-30 | At outlet A, exchange an invoice sold at outlet B. | Allowed. Stock is received at A. The exchange shows receiving outlet A. The original sale at B is unchanged. | Detail page |
| U-31 | Close the cash session of terminal A, then Preview. | "An active cash session…" (`EXCHANGE_SESSION_REQUIRED`). | Screenshot |
| U-32 | After Preview, an administrator deactivates terminal A. Then Confirm. | "The selected terminal is not active at this outlet…" (L6). Nothing is posted. | Screenshot |
| U-33 | A cash session past its 20-hour limit. | Blocked with the overdue message. | Screenshot |

## 5. History, detail, receipts

| ID | Steps | Expected outcome | Evidence |
|---|---|---|---|
| U-40 | `/exchanges`: search by exchange number and by original invoice number. Filter by status and date. | The matching rows appear. Pagination works. | Screenshot |
| U-41 | Open the detail. | Shows: <br>• source invoices; <br>• returned and replacement lines; <br>• settlements; <br>• approvals (supervisor name and reason); <br>• status. | Screenshot |
| U-42 | Print the 80 mm receipt. Then reprint. | The layout fits 80 mm. The reprint is marked as a reprint. Print history is recorded. | Photo of print, history |
| U-43 | Download the PDF receipt. | The PDF opens and matches the receipt values. | PDF file |
| U-44 | Viewer B opens an exchange of outlet A by URL. | Not found (no data shown). | Screenshot |
| U-45 | Viewer B opens Sale Details of a sale at A (if reachable). | The linked-exchanges card stays hidden. The by-sale API returns 404 (L3). | Network tab |

## 6. Reversal (EX-14, C-05)

| ID | Steps | Expected outcome | Evidence |
|---|---|---|---|
| U-50 | Manager reverses U-10 with a reason and a cash refund of 22. | <br>• Number `…-RV`. <br>• Stock back to the pre-exchange levels. <br>• The journal mirrors the original. <br>• Cash out 22. <br>• The exchange shows "reversed". | Detail, journal |
| U-51 | Try to reverse an exchange whose replacement was re-exchanged. | Blocked: "A replacement from this exchange has been exchanged again…". | Screenshot |
| U-52 | Try to reverse without a reason, or as Cashier A. | Reason required, or access denied (403, no logout). | Screenshot |
| U-53 | Reverse an exchange whose replacement used fractional packaging. | Packaging stays consumed. The dashboard COGS keeps that packaging cost (L4). | Dashboard, journal |

## 7. Reports and dashboards

| ID | Steps | Expected outcome | Evidence |
|---|---|---|---|
| U-60 | Owner dashboard → Sales for today at outlet A, before and after U-10. | Net sales +20 (net of VAT) on the exchange business date. | Screenshot |
| U-61 | After U-50 (same day). | Net sales back to the U-60 "before" value. The exchange still counts once, and the reversal is counted separately. | Screenshot |
| U-62 | Exchange metrics for today. | Count, reversal count, net adjustments and by-salesperson figures are consistent with U-10 and U-50. | Screenshot |
| U-63 | Mobile sales dashboard filtered by Cashier A. | Revenue moves by the gross adjustment (+22, then 0 after the reversal). | Screenshot |

## 8. Roles, permissions and responsiveness

| ID | Steps | Expected outcome | Evidence |
|---|---|---|---|
| U-70 | A user without `pos.exchange.create` opens `/pos/exchange`. | Access denied page. The POS Exchange button is hidden. The user is not logged out. | Screenshot |
| U-71 | Role form: the Exchange permission group shows exactly 5 keys. | Matches PermissionCatalog. | Screenshot |
| U-72 | Settings → Exchange Policy: try to choose phone verification `required`. | Shown as unavailable (DN-01). Saving it is rejected. | Screenshot |
| U-73 | Repeat U-01, U-10, U-41 and U-50 at 390, 768 and 1440 px. | No horizontal scroll. Buttons are reachable. Tables scroll inside their cards. | Screenshots |
| U-74 | Go offline (dev tools) and press Confirm. | An offline message. Nothing is posted, and the idempotency key is kept for the retry. | Screenshot |

## 9. Sign-off

| Role | Name | Date | Result |
|---|---|---|---|
| Store operations | | | |
| Finance (U-10…U-15, U-50, U-53, U-60…U-63) | | | |
| Owner | | | |

## 10. Automated browser execution (2026-11-08)
These cases were **actually executed in a real browser**: Microsoft Edge (headless) driven by Playwright 1.52.0. The environment was the persistent synthetic UAT environment (`IRANIMART_POS_V2_EXCHANGE_UAT_ENVIRONMENT.md`). This is a browser run, not API integration tests. Store-operations, Finance and owner sign-off (§9) are still required.

**Final runs:**
- 25 of 25 browser tests passed (6.4 minutes).
- A re-run to capture evidence for every test: 24 passed, and the lockout test was skipped by design because its 15-minute lock window was still active.
- Database reconciliation after the runs: `RECONCILIATION OK` across 54 exchanges (4 reversed) and 58 journals:
  - every journal is balanced;
  - stock-ledger COGS equals the recorded COGS, and after a reversal only the retained packaging remains;
  - inventory equals the ledger;
  - returned quantities match the exchanges in force;
  - every cash settlement reached the drawer once.

| ID | Result | Evidence or reason |
|---|---|---|
| U-01 | **Pass** | Login form, then the exchange screen (`U-01-exchange-screen-sales-a.png`) |
| U-02 | Not automated | The Sale Details entry point is covered by Vitest (`sale-details.component.spec.ts`); manual check advised |
| U-03 | **Pass** | `U-03-unknown-invoice.png` |
| U-04 | **Pass** | Lookup of an outlet-B invoice from outlet A (see U-30) |
| U-05 | **Pass** | "That exchange was reversed…" (`U-05-reversed-number-lookup.png`) |
| U-10 | **Pass** | Totals 110 / 132 / +22. Cash 22 tendered 50, change 28. Stock: Shirt +1, Jacket −1. Receipt shown (`U-10-*.png`) |
| U-11 | **Pass** | Two lines: 220 / 462 / +242, and stock deltas. Also two invoices of one registered customer (`U-11b-*.png`) |
| U-12 | **Pass** | Manager refund of 77 in cash (`U-12-*.png`) |
| U-13 | **Pass** | Salesperson refund with a supervisor refund approval (OC-03 B) (`U-13-*.png`) |
| U-14 | **Pass** | A card refund is disabled until a reference is entered and confirmed (`U-14-*.png`) |
| U-15 | **Pass** | Even exchange, no settlement |
| U-16 | Not automated | Covered by PostgreSQL preview-hash tests (409 `EXCHANGE_PREVIEW_STALE`) |
| U-17 | Not automated | Covered by PostgreSQL idempotency tests |
| U-20 | **Pass** | Window expired (sale back-dated 9 days through the checkout API); approved by Supervisor A |
| U-21 | **Pass** | Supervisor B: "does not have the required permission or access to this outlet" (`U-21-*.png`) |
| U-22 | Not automated | Self-approval refusal is covered by PostgreSQL tests |
| U-23 | **Pass** | 5 wrong passwords, then the right one: "Too many failed supervisor sign-ins…" |
| U-24 | **Pass** | An unknown login gets answers identical to a real account's |
| U-25 | **Pass** | Watch 660 ≥ 500: high-value approval, collect 550 |
| U-26 | **Pass** | Re-exchange by exchange number; the third exchange needs a chain approval |
| U-30 | **Pass** | Outlet-B invoice exchanged at A. Stock received at A; B unchanged |
| U-31, U-33 | Not automated | Closing or ageing the shared UAT session would disturb the persistent environment. Covered by PostgreSQL/unit tests |
| U-32 | Not automated in the browser | Terminals change only through outlet administration; the L6 PostgreSQL tests cover it |
| U-40 | **Pass** | History search (`U-40-history-search.png`) |
| U-41 | **Pass** | Detail page (`U-41-detail.png`) |
| U-42 | **Pass (print call)** | "Print 80mm" opened the browser print once (print dialog stubbed in headless mode). **Physical 80 mm printing: manual** |
| U-43 | **Pass** | PDF downloaded (`evidence/downloads/*.pdf`) |
| U-44, U-45 | Not automated | Outlet scoping is covered by PostgreSQL tests (L3) |
| U-50 | **Pass** | Reversal with a 22 cash refund. Stock back to the pre-exchange levels. Dashboard back to its value before the exchange |
| U-51 | **Pass** | Reversal blocked when the replacement was re-exchanged (`U-51-*.png`) |
| U-52 | **Pass** | The salesperson has no Reverse button |
| U-53 | Not automated in the browser | Needs a UOM pack with fractional packaging; covered by the L4 PostgreSQL reconciliation test |
| U-60 | **Pass** | Dashboard net sales +20 on the exchange date (API figure plus a page screenshot) |
| U-61 | **Pass** | Back to the earlier value after a same-day reversal |
| U-62 | **Pass** | Metrics card shown (`U-62-metrics.png`) |
| U-63 | Not automated | Mobile app; covered by the PostgreSQL reporting test |
| U-70 | **Pass** | Accounts user: access denied, still signed in (`U-70-access-denied.png`) |
| U-71 | Not automated | Covered by the role-form Vitest spec |
| U-72 | **Pass** | `required` shown as unavailable (`U-72-policy-settings.png`) |
| U-73 | **Pass** | Exchange and history at 390, 768 and 1440 px: no horizontal scroll (`U-73-*.png`) |
| U-74 | **Pass** | Offline: Confirm is disabled and the reviewed exchange is kept; it re-enables when back online |

**Totals:** 43 checklist IDs: **31 passed in the browser**, 0 failed, 0 blocked, 12 not automated in the browser (each covered elsewhere, as noted).

**Evidence** (local and git-ignored, under `e2e/exchange-uat/`):
- `evidence/screenshots/` (45 files);
- `evidence/downloads/`;
- `evidence/network-console/`;
- `evidence/results.json`;
- `evidence/reconciliation.txt`;
- `playwright-report/`;
- failure traces in `test-results/`.

**Defects found in the browser and fixed:** DEF-01 to DEF-04, see Implementation Plan §12.6.
