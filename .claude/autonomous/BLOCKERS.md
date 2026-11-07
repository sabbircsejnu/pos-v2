# Owner Dashboards — blockers and decisions awaiting sign-off

None of these stop the build. Each has a safe fallback in place until decided.

| ID | Decision needed | Owner | Current fallback | Status |
|---|---|---|---|---|
| B-01 | Sales targets are not implemented (`OutletSalesTargetVersion`). Build them? This needs a migration. | Business owner, tech lead | Target, Shortfall and Achievement show N/A | open |
| B-02 | Confirm P2-06 weighted-average ledger replay as the owner-facing stock valuation. It can differ from the operational Stock Valuation Report (`CostPrice + CostAdjustment`). | Finance | P2-06 replay; unvalued stock flagged as partial | open |
| B-03 | Exchange netting policy. No Returns/Exchange SRD exists. | Business owner, finance | Counted as posted, with a warning | open |
| B-04 | Approve slow-moving and transfer thresholds (LLD-D05, D06, D07). | Business owner | LLD proposed defaults | open |
| B-05 | Reversals reduce the period of their accounting date, not the original sale's period. | Finance | Matches P3.2-D02 | open |
| B-06 | Figma screenshots are not in the repo. Able Pro licence review is pending. | Business owner | Layout from SRS text; no Able Pro assets copied | open |
| B-07 | Should Purchase and Expense KPIs need `reports.financial`? | Business owner | Decided: two dashboard permissions only (SRS §2.1) | resolved |

# UOM v1 — decisions awaiting sign-off

None of these stop the build. Details: `Docs/Unit_Of_Measurement/IMPLEMENTATION_REPORT.md` §11.

| ID | Decision needed | Owner | Current fallback | Status |
|---|---|---|---|---|
| U-01 | Approve the six new permission keys. | Business owner | Keys live; BusinessOwner by wildcard; other roles via Role Edit | open |
| U-02 | Allow more than one refund per sale (RET-002)? | Business owner, finance | One refund per sale | open |
| U-03 | Should pricing rules apply to pack and custom prices? | Business owner | They do not | open |
| U-04 | Show the refund button for pcs-only sales? | Business owner | Hidden | open |
| U-05 | Sign off repack Pattern P1. | Finance | Implemented and proven by test | open |
| U-06 | Apply the migration to Development. | Tech lead | Created, not applied | open |
| U-07 | Update Flutter stock take for fractional quantities before wiring it to the API. | Tech lead | Not connected, so nothing fails today | open |
