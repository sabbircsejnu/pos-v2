---
paths:
  - "src/**/*.cs"
  - "tests/**/*.cs"
---

# Backend (.NET 10, Clean Architecture modular monolith)

Authoritative plan: `Docs/ARCHITECTURE/CLEAN_ARCHITECTURE_MEDIATR_CQRS_MIGRATION_PLAN.md` (§3 project rules, §5 use-case conventions, §7 MediatR, §8 validation, §9 authorization, §10 transactions). Read only the relevant section.

## Layering (compile-time direction)
- Domain: BCL only. Application: Domain + MediatR/FluentValidation abstractions; no EF, DbContext, HttpContext or API DTOs. Infrastructure: EF/provider/adapters implementing Application ports. API: HTTP host, controllers, middleware, policies, composition.
- Controllers contain no business logic and never use `RetailPOSDbContext`. API references Infrastructure only for registration and startup.
- `src/RetailPOS.API/Services` is transitional legacy. Migrate a service only when its slice is in scope; no permanent one-line handler forwarders.

## Use cases (MediatR + CQRS, in-process)
- Follow the nearest feature folder (e.g. `Application/Pos/{Commands,Queries,Abstractions,ReadModels}`). Commands end in `Command`, queries in `Query`; use `ICommand<T>`/`IQuery<T>` (`Application/Abstractions/Messaging`) or `IRequest<T>` as the folder already does.
- Controllers send via MediatR with `HttpContext.RequestAborted`. Pass the `CancellationToken` through every async call.
- Queries never mutate state and return purpose-built read models, not tracked entities.
- Prefer narrow ports (`I…Store`, `I…Authorizer`, `I…AuditWriter`) over generic repositories or a broad unit of work.
- Do not use MediatR notifications for mandatory effects inside financial transactions; orchestrate the required effects directly.

## Validation, errors, authorization
- One owner per rule: transport shape → API binding; request shape → FluentValidation validator (run by `Behaviors/ValidationBehavior.cs`); business invariants → handler/domain; races → DB constraints and concurrency.
- Errors map through `GlobalExceptionHandlerMiddleware` to the existing `ApiResponse` envelope (not ProblemDetails). Use the feature's error codes/exceptions (e.g. `CashSessionException` + `CashSessionErrorCodes`). Never return success-shaped fallbacks.
- HTTP entry: `[Authorize(Policy = "<permission.key>")]` with existing permission keys (registered in `Program.cs`). In the handler, enforce business ownership plus outlet scope via the feature authorizer (`ICashSessionWriteOutletAuthorizer`, `ISaleMutationOutletAuthorizer`, `IUserOutletAccessService`, …).

## Transactions, concurrency, idempotency
- No transaction pipeline behavior. The command handler that owns the operation opens the transaction (`store.BeginTransactionAsync`); join an ambient one rather than nesting.
- For stock, cash sessions, sales and journals, use the established `xmin` optimistic concurrency or `FOR UPDATE`/`FOR SHARE`/`SKIP LOCKED` row locks deliberately, and cover race behavior with tests.
- Mutation endpoints that already accept idempotency keys must keep replay semantics (see `OpenCashSessionCommand`).
- Avoid sync-over-async, N+1, DB calls in loops, unbounded `Include` graphs, unbounded `Task.WhenAll` and silent catch blocks. Paginate and bound report/list queries.
- Logging: structured, no secrets or sensitive customer data. Configuration: typed options + existing `appsettings.{Environment}.json`/user-secrets; no new configuration frameworks.
