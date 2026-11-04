---
paths:
  - "src/**/Pos/**"
  - "src/**/Sales/**"
  - "src/**/SalesReports/**"
  - "src/**/Inventory/**"
  - "src/**/InventoryReports/**"
  - "src/**/Grns/**"
  - "src/**/Pricing/**"
  - "src/**/Customers/**"
  - "src/RetailPOS.API/Controllers/{Pos,Sales,HeldSales,CashSession,Inventory,Stock,Mobile}*.cs"
  - "retailpos-frontend/src/app/pages/{pos,cash-sessions,sales,stock-adjustments,stock-counts,stock-transfers,grn,customers}/**"
---

# POS, sales and inventory safety

- Authoritative specs: `Docs/08_Sales_And_POS/IRANIMART_POS_MASTER_SRS_V1_2.md` and `IRANIMART_POS_MASTER_LLD_V1_2.md` (search by FR/D ID; do not bulk-read). Progress record: `POS_V2_IMPLEMENTATION_PROGRESS.md`. UX reference: `iranimart_pos_prototype.html`. Inventory: `Docs/06_Inventory/`.
- Business, outlet and terminal scope stay explicit in sale, held sale, stock movement, cash session and report flows. Use the existing authorizer ports.
- Sale totals (discount, VAT, payment, change, rounding) are computed by the backend per approved rules. Final stock/price checks read the live DB under `xmin`/row lock, never cache or search projections.
- No negative stock in online checkout unless an approved rule says otherwise. Offline conflict policy must be designed and approved, not assumed.
- Cash sessions, receipt numbering, sale status transitions, held sales, voids, returns/exchanges and backdated sales require permission checks, idempotency where established, and audit evidence in the same transaction.
- Loyalty belongs to `Docs/Customer_Loyalty_Module/` and is out of scope unless explicitly requested. Never assume offline loyalty redemption (it requires online validation).
- Sale/GRN/stock flows that post to accounting go through the Journal Engine inside the owning transaction; see `accounting-safety.md` concerns and `Docs/10_Accounts_And_Finance/P2-0x` checklists.
