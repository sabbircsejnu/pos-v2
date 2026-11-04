---
name: tdd-implement
description: Implement an IraniMart POS v2 change test-first using the existing CQRS, validation, authorization and data conventions and the established xUnit/Moq, PostgreSQL and Vitest fixtures; focused tests first.
when_to_use: When implementing or fixing a behavior rule (backend or frontend), especially money, stock, authorization, concurrency or idempotency logic.
---

# Test-first implementation

1. Find the existing tests and fixtures for the feature (`tests/RetailPOS.UnitTests/<Feature>`, `tests/RetailPOS.IntegrationTests/<Feature>`, `*.spec.ts` beside the component/service). Read the spec rule and the code before changing anything.
2. Write a meaningful failing test for the smallest behavior:
   - handler/validator/domain rule → UnitTests with Moq-faked ports;
   - HTTP contract, permission (403), tenancy → IntegrationTests via `RetailPosApiFactory` + `TestAuthHandler` / `TestBusinessScope`;
   - SQL, locking, concurrency, idempotency, rollback → `PostgresTestDatabase` (Test stack 55432 only);
   - Angular form/permission/error behavior → Vitest spec.
   Run it and confirm it fails for the right reason. If the infrastructure is unavailable, say so; never fabricate red/green.
3. Implement the smallest safe change following the nearest feature folder (Command/Query + validator + handler + port + store, controller sends via MediatR, existing error codes and `ApiResponse`).
4. Add negative cases as risk warrants: wrong business, wrong outlet, missing permission, duplicate/replay, concurrent mutation, failure rollback.
5. Run the focused tests, then the impacted project tests and builds (`verify-build`).
6. Report the changed files, behavior, exact commands with results, and remaining risks.
