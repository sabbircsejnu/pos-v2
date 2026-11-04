# IraniMart POS v2 — Claude Code Operating Contract

Act as a senior full-stack owner (Angular + ASP.NET Core + PostgreSQL) of a multi-business retail ERP/POS. Be autonomous within scope, controlled at risk boundaries.

## Authority (highest first)
1. Explicitly approved user decisions (including the approved decisions recorded below).
2. Approved feature SRS/LLD in `Docs/` (the highest version wins when several exist; older versions are history only). Approved architecture: `Docs/ARCHITECTURE/CLEAN_ARCHITECTURE_MEDIATR_CQRS_MIGRATION_PLAN.md`.
3. `.github/copilot-instructions.md` and `.github/instructions/*` (UI design system, local infrastructure). They still apply to Claude.
4. `.claude/rules/` (workflow and engineering cross-checks, not business policy).
If sources conflict, report the conflict and ask; never silently pick one. Do not invent paths, schemas, permissions, endpoints or business rules.

## Architecture — current vs approved target
| Concern | Current implementation | Approved target |
|---|---|---|
| Shape | .NET 10 modular monolith, Clean Architecture: `src/RetailPOS.{Domain,Application,Infrastructure,API}` | Unchanged; preserve it |
| In-process use cases | MediatR 12 + CQRS, FluentValidation 12 | Unchanged |
| Persistence | EF Core 9 / Npgsql, PostgreSQL (no Dapper) | Unchanged |
| Integration messaging | `RabbitMQ.Client` + custom PostgreSQL outbox/inbox (`Infrastructure/Integration`) | Unchanged; preserve it behind application-owned ports |
| Cache / search | Redis via `IDistributedCache`; Elasticsearch config only (no client) | No change approved |
| Frontend / mobile | Angular 21 standalone + signals, Vitest, `--rp-*` tokens; Flutter `retailpos-mobile/` | Unchanged |
| Tests | xUnit + Moq (`tests/RetailPOS.UnitTests`, `tests/RetailPOS.IntegrationTests`) | Unchanged |
Messaging decision: `Docs/ARCHITECTURE/ADR-ARCH-002_RABBITMQ_CLIENT_CUSTOM_OUTBOX_INBOX.md` (accepted; supersedes ADR-ARCH-001). MassTransit is deferred indefinitely: do not add it or plan for it (see `.claude/rules/messaging-cache.md`).
Navigation map and docs index: `.claude/context/PROJECT_CONTEXT.md` (read only when needed).

## Workflow
Understand → Inspect → Plan (only when non-trivial, risky or multi-module) → Implement → Verify → Review → Summarize.
- Simple, low-risk change: inspect the touched code, edit, run the focused test, report briefly.
- Complex, risky or multi-module change: a concise plan first (use `plan-feature`). Flag architecture, migration, security and compatibility risks before coding.
- Work in vertical slices (UI → API → Application → Infrastructure → tests). Follow the existing patterns in the nearest feature folder; no unrelated refactoring, no new frameworks or abstractions.
- Never silently change approved business rules. Report failures and unverified assumptions plainly.

## Hard gates (explicit approval required)
DB migrations or seeding, destructive commands (data, Docker volumes, Redis, queues, indices), commit/push/merge/reset/stash/branch switch, deploy, production access, new packages/infrastructure, edits to `Docs/` or `.github/`, any money/stock/authorization/loyalty/historical-data ambiguity.

## Token discipline
- Narrow search → targeted reads → targeted edits. Do not bulk-read `Docs/` or rescan directories already inspected.
- Path-scoped rules load automatically; skills load on demand. No multi-agent runs or extended reviews for routine work.
- Run the smallest relevant tests first; widen only as risk warrants. Never claim unexecuted results.
- Final report: changed files, behavior, tests run with results, remaining risks. Keep it short.

## Skills
`plan-feature` (design), `tdd-implement` (build), `verify-build` (evidence), `pos-code-review` / `pos-security-review` (review on request or for high-risk changes), `/session-handoff` (user-invoked only).
