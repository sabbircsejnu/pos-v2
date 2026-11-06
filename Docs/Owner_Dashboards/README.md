# IraniMart POS v2 — Owner Dashboards specification pack

Prepared 2026-11-06 from the provided Figma Make screenshots and the established project architecture/context.

- `01_OWNER_DASHBOARDS_SRS.md`: functional scope, financial and inventory metric definitions, permission and filtering rules, UI behaviour, acceptance criteria, unresolved approvals.
- `02_OWNER_DASHBOARDS_LLD.md`: proposed .NET/Angular/PostgreSQL implementation, API and DTO shapes, read models, calculations, stock-transfer suggestion algorithm, security, testing and phased development plan.

## Important

**Both documents are proposed v1.0 specifications.** The design screenshots contain demo data, not trusted financial numbers. The implementation must start with a read-only codebase/schema mapping and must verify the existing financial semantics, cost basis, authorization, stock statuses and existing transfer workflow. The LLD does not purport to know exact codebase table names.

## Suggested repository placement

`Docs/Owner_Dashboards/` (or the closest existing docs convention), retaining the two Markdown filenames.
