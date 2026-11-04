---
name: pos-code-review
description: Project-specific review of a diff for IraniMart POS v2: correctness, business/outlet isolation, stock/sale/accounting invariants, transactions/concurrency, API compatibility and test gaps. Findings first, ranked by severity. Read-only.
when_to_use: When the user asks to review changes, or before reporting a high-risk change (money, stock, authorization, migrations, messaging) as done. Not for routine low-risk edits.
---

# POS code review

1. Scope: `git diff` (and `git diff --staged`) plus directly related code only. Do not review the whole repo. Load the path-scoped rules for the touched areas and the relevant spec section only.
2. Check, in priority order:
   - Business scope via `IBusinessScope.RequireBusinessId()`; outlet scope via the feature authorizer; IDOR (load by ID **and** business/outlet); permission policy keys unchanged.
   - Money/stock/journal invariants; backend-authoritative totals; no new rounding behavior.
   - Handler-owned transactions, `xmin`/row-lock usage, idempotency replay, audit in the same transaction, rollback paths.
   - Layering: no DbContext in controllers, no EF/HTTP types in Application, no generic repositories, no MediatR notifications for mandatory financial effects.
   - API contract: route, verb, payload, status code, the `ApiResponse` envelope, the Angular caller.
   - Query efficiency (N+1, unbounded queries), cancellation, logging without secrets/PII.
   - Messaging: existing outbox/inbox used correctly (same transaction, idempotent consumer, business scope validated); no MassTransit or parallel messaging infrastructure (see `messaging-cache.md`).
   - Frontend: design-system compliance, 403 ≠ logout, double submit, stale responses.
   - Tests: negative, tenancy, concurrency and idempotency coverage; real PostgreSQL where relational behavior matters.
3. Output **findings first**, ranked Critical / High / Medium / Low. Each finding: `file:line`, a concrete failure scenario, impact, the smallest fix. Mark each one confirmed or hypothesis. If nothing is found, say so and list the unverified risk.
4. Do not modify code unless asked.
