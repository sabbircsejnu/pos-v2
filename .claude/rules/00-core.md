# Core invariants (always loaded)

## Business safety
- Business isolation, outlet/warehouse scope, authorization, stock integrity, sale/payment/accounting correctness and auditability are invariants. Enforce them on the server, in the use case. Route guards and hidden UI are usability only.
- Every business-scoped read or write resolves the business through `IBusinessScope.RequireBusinessId()` (or the established equivalent), never from a client-supplied business ID. SuperAdmin has no implicit business; a missing business is never implicit permission.
- Outlet/warehouse scope is operation-specific. Assigned locations, default inventory location and inventory location access are different concepts (`Docs/03_User_Management/OUTLET_ASSIGNMENT_RULES.md`). Use the feature's existing authorizer port; never invent one global "all locations" rule.
- Money and quantities use `decimal`/`numeric`, never floating point. Backend computes final totals. Never introduce new rounding behavior.
- Persist timestamps in UTC; derive business dates from the configured business/outlet time zone (see the `CashSessionTime` pattern).
- 401 means re-authentication per the current auth design; **403 is access denied and must never force logout**.
- For any ambiguity affecting money, stock, authorization, loyalty or historical data: stop and present the precise decision.

## Change safety
- Check `git status` before editing. The tree often holds unrelated user work: never overwrite, revert, stage or format it.
- No commit, push, merge, reset, clean, stash, branch switch, file deletion, migration, seeding or deploy without explicit approval.
- Secrets (user-secrets, `docker/.env.*`, `Docs/Cred`, connection strings, tokens, OTPs, passwords): never print, copy into files or commit them; redact them in output, logs, summaries and docs. `docker/.env.dev`, `docker/.env.test` and `Docs/Cred` are denied by default in `.claude/settings.json`; if a task truly needs one, ask for explicit approval first.
- Fail safely: actionable user messages, no exception details or stack traces to clients.
- Keep public API routes, verbs, payloads, status codes and permission keys backward-compatible unless a change is explicitly approved.
- Confirm framework/package versions from project files; no unrequested upgrades or new dependencies.
