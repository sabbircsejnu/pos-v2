# P2-06 Valuation Prerequisite Acceptance Checklist

Updated: 2026-09-05
Scope: P2-06 COGS and inventory valuation reporting implementation and prerequisite readiness review
Source of truth: [Docs/10_Accounts_And_Finance/PHASE_2_ACCOUNTING_DEVELOPMENT_PLAN.md](Docs/10_Accounts_And_Finance/PHASE_2_ACCOUNTING_DEVELOPMENT_PLAN.md)

Status legend:
- Passed
- Partially Passed
- Failed
- Not Applicable

---

## 1. Approval and scope gate

- This review covers the valuation prerequisites and the implemented P2-06 COGS and inventory valuation reports.
- P2-07 remains out of scope for this acceptance decision.
- COGS financial totals are sourced from posted journal lines on the configured COGS account; inventory totals are reconstructed from valuation-aware stock-ledger movements.

---

## 2. Requirement status matrix

| Requirement | Status | Evidence | Notes |
|---|---|---|---|
| Weighted-average cost consistency per Variant + Location | Passed | [src/RetailPOS.API/Services/StockLedgerService.cs](src/RetailPOS.API/Services/StockLedgerService.cs), [src/RetailPOS.Core/Entities/StockLedger.cs](src/RetailPOS.Core/Entities/StockLedger.cs), [tests/RetailPOS.Tests/Inventory/StockLedgerValuationTests.cs](tests/RetailPOS.Tests/Inventory/StockLedgerValuationTests.cs) | The ledger now stores durable valuation metadata and uses deterministic movement sequencing for replay. |
| GRN receipt cost persistence | Passed | [src/RetailPOS.API/Services/GrnService.cs](src/RetailPOS.API/Services/GrnService.cs) | Receipt movements persist the actual received unit cost into the ledger history. |
| Sale / issue movement cost persistence | Passed | [src/RetailPOS.API/Services/SaleService.cs](src/RetailPOS.API/Services/SaleService.cs) | Sale issue movements preserve the movement-time cost basis rather than relying on mutable current cost. |
| Return / reversal cost preservation | Passed | [src/RetailPOS.API/Services/SaleService.cs](src/RetailPOS.API/Services/SaleService.cs) | Reversals and refunds preserve the original movement cost basis for replay fidelity. |
| Stock adjustment snapshot usage | Passed | [src/RetailPOS.API/Services/StockAdjustmentService.cs](src/RetailPOS.API/Services/StockAdjustmentService.cs), [tests/RetailPOS.Tests/Inventory/StockAdjustmentWorkflowHttpTests.cs](tests/RetailPOS.Tests/Inventory/StockAdjustmentWorkflowHttpTests.cs) | Approved adjustments persist the approved cost snapshot into ledger rows and reversals preserve that basis. |
| Transfer valuation integrity | Passed | [src/RetailPOS.API/Services/StockTransferService.cs](src/RetailPOS.API/Services/StockTransferService.cs), [tests/RetailPOS.Tests/Inventory/StockTransferSubmitAndDispatchHttpTests.cs](tests/RetailPOS.Tests/Inventory/StockTransferSubmitAndDispatchHttpTests.cs) | Dispatch and receive flows preserve transfer valuation metadata without creating artificial gain/loss. |
| Immutability of movement metadata | Passed | [src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs](src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs), [src/RetailPOS.Core/Entities/StockLedger.cs](src/RetailPOS.Core/Entities/StockLedger.cs) | Ledger rows are treated as append-only and valuation fields are persisted as durable metadata. |
| Deterministic movement ordering | Passed | [src/RetailPOS.API/Services/StockLedgerService.cs](src/RetailPOS.API/Services/StockLedgerService.cs), [tests/RetailPOS.Tests/Inventory/StockLedgerValuationTests.cs](tests/RetailPOS.Tests/Inventory/StockLedgerValuationTests.cs) | Movement sequencing is resolved deterministically for historical replay. |
| Explicit tagging of legacy / unvalued movements | Passed | [src/RetailPOS.API/Services/StockLedgerService.cs](src/RetailPOS.API/Services/StockLedgerService.cs) | Movements can be flagged as unvalued or unreliable rather than silently using current cost as a fallback. |
| Migration for valuation metadata is present | Passed | [src/RetailPOS.Infrastructure/Data/Migrations/20260810000000_AddStockLedgerValuationMetadata.cs](src/RetailPOS.Infrastructure/Data/Migrations/20260810000000_AddStockLedgerValuationMetadata.cs) | The schema change for valuation columns and supporting index is included in the migration set. |
| COGS report uses posted configured-account journal lines | Passed | [src/RetailPOS.API/Services/AccountingReportService.cs](src/RetailPOS.API/Services/AccountingReportService.cs), [src/RetailPOS.API/Controllers/AccountingReportsController.cs](src/RetailPOS.API/Controllers/AccountingReportsController.cs) | Posted COGS-account debits and credits provide the report total and source-document drill-down; no product cost or selling price is used. |
| Inventory valuation supports deterministic historical as-of replay | Passed | [src/RetailPOS.API/Services/AccountingReportService.cs](src/RetailPOS.API/Services/AccountingReportService.cs), [tests/RetailPOS.Tests/Accounting/P206ValuationReportServiceTests.cs](tests/RetailPOS.Tests/Accounting/P206ValuationReportServiceTests.cs) | Reliable movements are replayed by business date and deterministic movement sequence into variant/location weighted-average pools. |
| Reconciliation differences and unvalued records are surfaced | Passed | [src/RetailPOS.API/Services/AccountingReportService.cs](src/RetailPOS.API/Services/AccountingReportService.cs), [retailpos-frontend/src/app/pages/accounting/valuation-reports.component.html](retailpos-frontend/src/app/pages/accounting/valuation-reports.component.html) | The API returns GL balances and reconciliation differences, while legacy/unreliable movements are excluded from precise totals and returned separately. |
| Report APIs, UI, routes, and permissions are available | Passed | [src/RetailPOS.API/Controllers/AccountingReportsController.cs](src/RetailPOS.API/Controllers/AccountingReportsController.cs), [retailpos-frontend/src/app/app.routes.ts](retailpos-frontend/src/app/app.routes.ts), [retailpos-frontend/src/app/models/menu.model.ts](retailpos-frontend/src/app/models/menu.model.ts) | Dedicated COGS and inventory valuation permissions protect the APIs and pages. |

---

## 3. Automated evidence

### Scenario-to-test mapping

| Required scenario | Automated evidence |
|---|---|
| Normal sale COGS | `P206ValuationReportServiceTests.Cogs_UsesOnlyPostedConfiguredAccountLines_AndReversalsReduceTotal` verifies a posted sale debit on the configured COGS account. |
| Full and partial return COGS | `SaleServiceHistoricalCostSnapshotTests.RefundAsync_UsesHistoricalUnitCost_ForFullRefundAndIgnoresCurrentCostChanges` and `RefundAsync_UsesHistoricalUnitCost_ForPartialRefundAndIgnoresCurrentCostChanges`; the P2-06 report test verifies the corresponding `SalesReturn` COGS credit is reported as negative COGS. |
| Exchange / reversal COGS | `P206ValuationReportServiceTests.Cogs_UsesOnlyPostedConfiguredAccountLines_AndReversalsReduceTotal` verifies `SaleExchange` COGS credits reduce the report total; `SaleServiceHistoricalCostSnapshotTests.VoidAsync_UsesHistoricalUnitCost_ForFullReversalAndIgnoresCurrentCostChanges` protects the source cost basis. |
| GRN weighted-average valuation | `PurchaseAndGrnAccountingTests.GrnCompletion_PostsPurchaseReceiptJournal_UsingGrnUnitCost` verifies receipt source cost; `P206ValuationReportServiceTests.InventoryValuation_ReplaysWeightedAverage_AsOfBusinessDate_AndExcludesUnvalued` replays receipt values into the pool. |
| Multiple receipts at different costs | `P206ValuationReportServiceTests.InventoryValuation_ReplaysWeightedAverage_AsOfBusinessDate_AndExcludesUnvalued` uses receipts at 10 and 20, producing a 300 value for 20 units. |
| Sale / issue consumption from weighted-average pool | `P206ValuationReportServiceTests.InventoryValuation_ReplaysWeightedAverage_AsOfBusinessDate_AndExcludesUnvalued` verifies that a five-unit issue consumes the 15 weighted-average cost, leaving 225. |
| Stock gain and shortage | `P206ValuationReportServiceTests.InventoryValuation_UsesStockGainAndShortageMovementsWithoutCurrentCost` verifies gain and shortage movement replay and zero reconciliation difference. |
| Transfer preserves business-level inventory value | `P206ValuationReportServiceTests.InventoryValuation_TransferPreservesBusinessValue_AndLocationFilterIsolated` verifies a transfer leaves combined value at 100. |
| Historical as-of valuation | `P206ValuationReportServiceTests.InventoryValuation_ReplaysWeightedAverage_AsOfBusinessDate_AndExcludesUnvalued` verifies as-of receipt and post-issue states using `BusinessDate`. |
| Multi-location isolation | `P206ValuationReportServiceTests.InventoryValuation_TransferPreservesBusinessValue_AndLocationFilterIsolated` and `CogsAndValuation_ApplyProductVariantAndLocationFilters_AndExcludeRoundingAccount`. |
| Deterministic replay | `P206ValuationReportServiceTests.InventoryValuation_ReplaysSameDayMovementsBySequenceDeterministically` uses deliberately non-chronological IDs and verifies `MovementSequence` drives the result. |
| COGS to GL reconciliation | `P206ValuationReportServiceTests.Cogs_UsesOnlyPostedConfiguredAccountLines_AndReversalsReduceTotal` verifies `reconciliationDifference = 0` for the clean posted journal dataset. |
| Inventory valuation to Inventory GL reconciliation | `P206ValuationReportServiceTests.InventoryValuation_ReplaysWeightedAverage_AsOfBusinessDate_AndExcludesUnvalued` verifies `reconciliationDifference = 0` for the fully-valued as-of dataset. |
| Legacy / unvalued movement handling | `P206ValuationReportServiceTests.InventoryValuation_ReplaysWeightedAverage_AsOfBusinessDate_AndExcludesUnvalued` verifies the movement is excluded, returned in `UnvaluedMovements`, and leaves a surfaced `reconciliationDifference = -15`. |
| Rounding-account exclusion | `P206ValuationReportServiceTests.CogsAndValuation_ApplyProductVariantAndLocationFilters_AndExcludeRoundingAccount` adds a rounding-account line and verifies it does not change COGS. |
| Business/outlet/warehouse/location/product/variant filters | `P206ValuationReportServiceTests.CogsAndValuation_ApplyProductVariantAndLocationFilters_AndExcludeRoundingAccount` verifies tenant-business isolation plus outlet, warehouse, location, product, and variant scopes. |
| Report permissions | `FinancialReportsHttpTests.P206Reports_RequireTheirDedicatedPermissions` verifies both endpoints reject a caller lacking `accounting.reports.cogs` and `accounting.reports.inventory_valuation`. |

### Targeted valuation prerequisite suite
- Command: `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj --filter "FullyQualifiedName~StockLedgerValuationTests|FullyQualifiedName~SaleServiceHistoricalCostSnapshotTests|FullyQualifiedName~PurchaseAndGrnAccountingTests|FullyQualifiedName~StockTransferSubmitAndDispatchHttpTests|FullyQualifiedName~StockAdjustmentWorkflowHttpTests" --logger "console;verbosity=minimal"`
- Observed result: 33 passed, 0 failed, 0 skipped

### P2-06 report suite
- Command: `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj -c Release --filter "FullyQualifiedName~P206ValuationReportServiceTests|FullyQualifiedName~P206Reports_RequireTheirDedicatedPermissions" --logger "console;verbosity=minimal"`
- Observed result: 7 passed, 0 failed, 0 skipped

### Build validation
- Backend: `dotnet build RetailPOS.slnx -c Release --no-restore`
- Observed result: succeeded; the test project reports the existing `SQLitePCLRaw.lib.e_sqlite3` NU1903 dependency advisory.
- Frontend: `npm run -s build` from `retailpos-frontend`
- Observed result: production bundle generated successfully.

### Accounting and inventory regression suite
- Command: `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj -c Release --filter "FullyQualifiedName~Accounting|FullyQualifiedName~Inventory|FullyQualifiedName~StockLedger|FullyQualifiedName~SaleServiceHistoricalCostSnapshotTests|FullyQualifiedName~PurchaseAndGrnAccountingTests" --logger "console;verbosity=minimal"`
- Observed result: 195 passed, 0 failed, 0 skipped

### Full backend suite
- Command: `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj -c Release --logger "console;verbosity=minimal"`
- Observed result: 334 passed, 0 failed, 0 skipped

---

## 4. Final decision

Decision: P2-06 Accepted

Rationale:
- The valuation data model and movement write paths preserve historical cost information for deterministic replay.
- The COGS report uses posted configured-account journal lines, including credits that reverse prior COGS effects.
- The inventory report uses historical cost metadata and deterministic movement ordering, preserves transfer value, and explicitly excludes unreliable legacy movements from precise valuation.
- The targeted P2-06 calculation and authorization suite passed without failures.
- The final acceptance verification independently calculates COGS GL balance rather than assigning it from the report total, and applies tenant business scope when replaying stock-ledger locations.

Follow-up items:
- P2-07 remains deferred and must not be started without approval.
- Historical rows explicitly marked unreliable remain excluded until an approved backfill or estimation policy is adopted.
