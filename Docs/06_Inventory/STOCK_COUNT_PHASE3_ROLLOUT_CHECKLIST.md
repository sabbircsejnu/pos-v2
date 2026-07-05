# Stock Count Phase 3 Rollout Checklist

## Purpose

Use this checklist to safely enable Phase 3 actions:

- Approve
- Generate Stock Adjustment Draft

This rollout uses two gates and both must be enabled:

1. Global API flag: `FeatureFlags:StockCountPhase3Enabled`
2. Per-business feature entitlement key: `stockcount.phase3`

---

## Prerequisites

1. Deploy backend code that contains:
- Phase 3 feature gate checks in stock count controller.
- `SourceStockCountId` linkage in stock adjustments.
- Unique filtered index on `source_stock_count_id`.

2. Apply database migration:
- `20260705100458_AddStockAdjustmentSourceStockCount`

3. Deploy frontend code with:
- `stockCountPhase3Enabled` environment flag support.
- Phase 3 action buttons hidden when flag is disabled.

---

## Enable Sequence

### Step 1: Keep Global Flag OFF in All Environments Initially

Set in API config:

- `FeatureFlags:StockCountPhase3Enabled = false`

This keeps Phase 3 endpoints blocked even if users have `StockCount.Approve` permission.

### Step 2: Verify Business Feature Settings

In Business Setup feature settings, ensure `stockcount.phase3` exists for target businesses.

Recommended staged values:

- Pilot businesses: `stockcount.phase3 = true`
- All others: `stockcount.phase3 = false`

### Step 3: Pilot Enablement

For pilot environment/businesses:

1. Set API global flag to `true`.
2. Enable `stockcount.phase3` only for pilot businesses.
3. Enable frontend environment flag:
- `stockCountPhase3Enabled = true`

### Step 4: Smoke Test (Pilot)

Validate the following:

1. User with `StockCount.Approve` can approve only in allowed statuses.
2. User without `StockCount.Approve` cannot approve.
3. Generate Draft works once per approved stock count.
4. Concurrent Generate Draft calls create only one draft.
5. Repeated Generate Draft is blocked.
6. Unauthorized location/tenant access is blocked.

### Step 5: Broader Rollout

1. Keep global API flag `true`.
2. Enable `stockcount.phase3` per business in waves.
3. Monitor errors and adjustment creation rates.

---

## Rollback Plan

If any issue occurs:

1. Immediately set API global flag to `false`.
- This blocks all Phase 3 API actions.

2. Optionally set frontend flag to `false`.
- This hides Phase 3 actions in UI.

3. For business-specific rollback:
- Set `stockcount.phase3 = false` for affected businesses.

---

## Verification Checklist

- Migration applied successfully.
- Global flag value confirmed in runtime config.
- `stockcount.phase3` values verified per business.
- Approve endpoint returns `403` when phase disabled.
- Generate draft endpoint returns `403` when phase disabled.
- Only one stock adjustment draft exists per stock count source.
- Stock Count tests pass in CI, including phase-gating and concurrency checks.

---

## Notes

- `StockCount.Reject` is independent from `StockCount.Approve` and must remain mapped correctly.
- Current rollout assumes Phase 3 remains explicitly controlled by flags, not only permissions.
