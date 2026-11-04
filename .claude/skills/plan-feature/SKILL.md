---
name: plan-feature
description: Produce a compact implementation plan for a multi-file, multi-module or behavior-changing IraniMart POS v2 feature (vertical slice across Angular, API, Application, Infrastructure, tests) before coding.
when_to_use: For non-trivial, risky or cross-module work, schema or messaging changes, or when a business decision may be unresolved. Skip for trivial fixes.
---

# Plan a feature

1. Read the request, then only the relevant spec sections (use `.claude/context/PROJECT_CONTEXT.md` to find the doc; the highest version wins) and the nearest existing feature folder as the pattern source.
2. Identify:
   - the invariants (money, stock, journal, audit), the business/outlet authorization and the permission keys;
   - layers and files: Angular page/service → controller → Command/Query + validator + handler → ports → Infrastructure store → migrations (if any);
   - transaction ownership, concurrency (`xmin`/row lock), idempotency, integration events (existing custom outbox/inbox per ADR-ARCH-002);
   - API contract compatibility and the affected Angular callers;
   - tests: unit (handler/validator with Moq), integration (`RetailPosApiFactory`, `PostgresTestDatabase`), frontend specs.
3. Output a **compact** plan: objective · files/layers · 3–8 steps · acceptance criteria · risks/unknowns · tests and commands.
4. Stop for approval when the user asked for review first, when a business/money/authorization decision is unresolved, or when the plan needs a migration, new package or new infrastructure. Otherwise proceed within scope.
