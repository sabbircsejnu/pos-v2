---
paths:
  - "src/**/Authorization/**"
  - "src/**/Authentication/**"
  - "src/**/Identity/**"
  - "src/**/Middleware/**"
  - "src/**/ConfidentialFiles/**"
  - "src/**/BusinessAdministration/**"
  - "src/RetailPOS.API/Controllers/{Auth,Users,Roles,Documents,Businesses}*.cs"
  - "retailpos-frontend/src/app/{guards,interceptors,directives}/**"
  - "retailpos-frontend/src/app/services/{auth,business-context,menu}*.ts"
---

# Security, authorization and tenancy

- References: `Docs/02_Security/` (AUTHORIZATION, PERMISSION_MODEL, DATA_ISOLATION, AUTHENTICATION, AUDIT_LOG_IMPLEMENTATION) and `Docs/03_User_Management/` (PERMISSION_MATRIX, ROLE_DEFINITION, OUTLET_ASSIGNMENT_RULES). Open only what the change needs.
- Default deny. Every endpoint, background job, export, file download and report authorizes on the server with an existing permission key; keep policy names and claim expansion unchanged.
- Distinguish platform SuperAdmin operations (no business required) from business-scoped ones (business required). The seeded SuperAdmin wildcard (`*`) must not become a shortcut that bypasses business or outlet scope in new code.
- Keep real-user vs acting-user / role-switch semantics and `BusinessContextMiddleware` / `AuditContextMiddleware` behavior intact.
- Prevent IDOR (load by ID **and** business/outlet), mass assignment (explicit DTOs, never bind entities), injection, XSS, SSRF and sensitive-data leakage. Uploads (logos, attachments, confidential files) are untrusted: validate type and size, and keep storage access controlled.
- Rate-limit policies (`auth-login`, `auth-register`, `auth-refresh`) and JWT/refresh semantics stay as they are unless a change is approved.
- Audit: structured, actor/business/correlation-aware, no secrets. Never log tokens, OTPs or passwords.
- 403 never forces logout; 401 follows the existing token-expiry/refresh flow.
