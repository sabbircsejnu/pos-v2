---
paths:
  - "tests/**"
  - "retailpos-frontend/**/*.spec.ts"
---

# Tests and quality gates

- Risk-based TDD: write a failing focused test for the changed rule, implement the minimum, refactor only when green. Never weaken or delete a failing test to get green unless the requirement genuinely changed (say so).
- **UnitTests** (`tests/RetailPOS.UnitTests`): Domain + Application only, ports mocked with Moq. No EF, HTTP host or infrastructure.
- **IntegrationTests** (`tests/RetailPOS.IntegrationTests`): HTTP via `Infrastructure/RetailPosApiFactory.cs` + `TestAuthHandler`; tenant context via `TestBusinessScope`.
  - For relational behavior, SQL, locking, concurrency, idempotency or migrations, use the real PostgreSQL fixture `Infrastructure/PostgresTestDatabase.cs` (template clone). Do not add new EF InMemory/SQLite tests for that behavior; the existing ones are legacy.
  - Live broker/cache tests use `[Trait("Category", "LiveInfrastructure")]` and the Test stack only (Postgres 55432, RabbitMQ 5673 vhost `retailpos_test`, Redis 6383, ES 9201). The fixture refuses Development port 5435. Rules: `.github/instructions/local-infrastructure.instructions.md` §3 and §8.
- Cover the negative and authorization paths: wrong business, wrong outlet, missing permission (403), duplicate/idempotent replay, concurrent mutation, rollback on failure.
- Frontend: Vitest via `ng test` (`@angular/build:unit-test`); test form validation, API interaction, disabled/permission states and error handling.
- Commands, narrowest first:
  - `dotnet test tests/RetailPOS.UnitTests --filter "FullyQualifiedName~<Name>"`
  - `dotnet test tests/RetailPOS.IntegrationTests --filter "FullyQualifiedName~<Name>"`
  - `npx ng test --watch=false --include <path/to.spec.ts>` (from `retailpos-frontend`)
  - Full suites (`dotnet test RetailPOS.slnx`, `npm test -- --watch=false`) only at acceptance gates or for wide changes.
- Report exact commands, pass/fail/skip counts and failures. Never claim unexecuted results.
