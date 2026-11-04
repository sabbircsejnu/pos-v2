---
paths:
  - "src/RetailPOS.Infrastructure/**"
  - "**/Migrations/**"
  - "database/**"
  - "**/*.sql"
---

# PostgreSQL / EF Core data integrity

- PostgreSQL is the transactional source of truth. Data access is **EF Core 9 + Npgsql** (`Infrastructure/Data/RetailPOSDbContext.cs`); there is no Dapper. Do not add another data-access library.
- Inspect the actual entity configuration, keys, constraints and existing migrations before proposing schema or SQL changes.
- Raw SQL (`FromSqlInterpolated`, `ExecuteSqlInterpolated`, `SqlQuery`): parameters only. Whitelist dynamic sort/filter identifiers; never concatenate input.
- Every business-scoped query filters by business ID, and by outlet/warehouse where the use case is location-scoped. No cross-business joins or caches.
- Money/quantity: `decimal` ↔ `numeric` with the existing precision. Unique business keys, stock invariants and referential integrity belong in DB constraints as well as code.
- Concurrency: reuse the established `xmin` tokens and `FOR UPDATE`/`FOR SHARE`/`SKIP LOCKED` lock reads in store implementations. Keep transactions short; no external calls inside them.
- Migrations live in `src/RetailPOS.Infrastructure/Data/Migrations`. Generate one only when the approved scope needs a schema change; review its SQL for backward compatibility, locking and rollback. **Never apply** (`dotnet ef database update`) or remove migrations without approval. Development PostgreSQL (5435, `retailpos_db`) is persistent, not disposable.
- Never delete or rewrite real data without a preview, impact analysis and explicit confirmation.
- Indexes: justify them with the query shape; avoid `SELECT *`-style projections and unbounded scans.
