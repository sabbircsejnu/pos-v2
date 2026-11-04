---
name: pos-security-review
description: Project-specific security review of changed IraniMart POS v2 surfaces: authn/authz policies, business/outlet tenancy, IDOR, injection, XSS, uploads, secrets, audit and message-consumer trust. Evidence-based, read-only, no active scanning.
when_to_use: When the user asks for a security review, or when a change touches auth, permissions, roles, tenancy, uploads/files, exports, raw SQL or integration consumers.
---

# POS security review

1. Scope to the impacted surfaces (diff + callers). Load `security-auth.md` and the relevant `Docs/02_Security/` or `Docs/03_User_Management/` section only. No active scans, external requests or production access.
2. Inspect:
   - `[Authorize(Policy = ...)]` on every new or changed endpoint; server-side checks in the handler; SuperAdmin vs business-scoped handling; wildcard `*` not bypassing scope.
   - Business/outlet filtering on reads, writes, exports, reports, file downloads and background jobs; IDOR via client-supplied IDs.
   - Mass assignment (entities bound from requests), raw SQL parameterization and identifier whitelisting, XSS in Angular bindings (`innerHTML`, bypassSecurityTrust*), SSRF, upload type/size/storage.
   - Secrets in code, config, logs or test fixtures; tokens/OTPs/passwords in logs; PII in messages.
   - Message consumers: trust boundary, idempotency, tenant ID validation.
   - 401/403 handling (403 never logs out); rate-limit policies intact.
3. For each finding: evidence (`file:line`), attack preconditions, impact, severity, smallest remediation. Actionable issues only, no generic checklist. Never print credentials or sensitive payloads.
