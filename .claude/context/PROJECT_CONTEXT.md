# Project context — navigation map (read on demand; not auto-loaded)

Navigation only. Code and approved `Docs/` are the source of truth. Verified 2026-11-04.

## Architecture — current implementation
- `RetailPOS.slnx`: `src/RetailPOS.Domain` (entities, money, audit contracts) ← `src/RetailPOS.Application` (feature folders: Commands/Queries/Abstractions/ReadModels, `Behaviors/ValidationBehavior.cs`, `Abstractions/IBusinessScope.cs`, `Abstractions/Messaging/ICommand|IQuery`) ← `src/RetailPOS.Infrastructure` (`Data/RetailPOSDbContext.cs`, `Data/Migrations`, feature stores, `Integration/` outbox/inbox/RabbitMQ) ← `src/RetailPOS.API` (Controllers, `Middleware/` GlobalExceptionHandler/BusinessContext/AuditContext, `Authorization/`, `Services/` legacy transitional, `Program.cs` policies/composition).
- .NET 10 · EF Core 9 + Npgsql · MediatR 12 · FluentValidation 12 · JWT · Redis `IDistributedCache` · `RabbitMQ.Client` 7.2 + custom outbox/inbox · QuestPDF/ClosedXML exports · Elasticsearch config only.
- Frontend `retailpos-frontend/`: Angular 21 standalone + signals, Vitest, Tailwind + `--rp-*`, shared UI kit `src/app/components/shared/ui/`.
- Mobile `retailpos-mobile/`: Flutter.
- Tests: `tests/RetailPOS.UnitTests` (Domain/Application, Moq), `tests/RetailPOS.IntegrationTests` (`Infrastructure/RetailPosApiFactory.cs`, `PostgresTestDatabase.cs`, `TestAuthHandler.cs`, `TestBusinessScope.cs`; legacy InMemory/SQLite tests remain).

## Approved decisions
- **Messaging:** `Docs/ARCHITECTURE/ADR-ARCH-002_RABBITMQ_CLIENT_CUSTOM_OUTBOX_INBOX.md` (accepted 2026-11-04). Keep `RabbitMQ.Client` and the custom PostgreSQL outbox/inbox behind application-owned ports. MassTransit is deferred indefinitely. ADR-ARCH-001 is superseded.
- **Clean Architecture migration (not yet complete):** continue per `Docs/ARCHITECTURE/CLEAN_ARCHITECTURE_MEDIATR_CQRS_MIGRATION_PLAN.md` (move `API/Services` workflows into Application handlers slice by slice).

## Docs index (highest version is authoritative)
| Area | Location |
|---|---|
| Architecture / migration plan | `Docs/ARCHITECTURE/`, `Docs/01_Architecture/` (frontend architecture, list/filter standards) |
| Security, permissions, isolation | `Docs/02_Security/`, `Docs/03_User_Management/` (PERMISSION_MATRIX, ROLE_DEFINITION, OUTLET_ASSIGNMENT_RULES), `Docs/POS_v2_Role_Definition.md` |
| Business setup | `Docs/04_Business_Management/` |
| Inventory / stock | `Docs/06_Inventory/` |
| POS / sales | `Docs/08_Sales_And_POS/` (MASTER SRS/LLD v1.2, prototype HTML, implementation progress) |
| Accounting | `Docs/10_Accounts_And_Finance/` (module spec, phase plans, P2/P3/RM acceptance checklists) |
| HR / payroll | `Docs/20_HR_And_Payroll/` (Requirements v1.6, LLD v1.3) |
| Loyalty (separate scope) | `Docs/Customer_Loyalty_Module/` SRS/LLD |
| Mobile | `Docs/Mobile/` SRS/LLD v1.4 |
| UI design system | `Docs/UI-Standardisation/DESIGN_SYSTEM.md`, `PHASE_2A_REPORT.md` + `.github/copilot-instructions.md` |
| Local infra | `Docs/Development/LOCAL_INFRASTRUCTURE.md` + `.github/instructions/local-infrastructure.instructions.md` |
| DB schema / seeding | `Docs/DATABASE_SCHEMA.md`, `Docs/DATABASE_SEEDER_GUIDE.md` |
