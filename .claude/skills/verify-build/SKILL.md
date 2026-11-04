---
name: verify-build
description: Run and report risk-proportionate verification for IraniMart POS v2 (focused dotnet/Vitest tests, then builds, then wider suites) with exact commands and real results.
when_to_use: After implementing a change, before claiming it works, or when the user asks to build or test.
---

# Verify build and tests

Run from the repo root unless noted. Start narrow; widen only as the change risk warrants.

| Step | Command |
|---|---|
| Focused backend unit | `dotnet test tests/RetailPOS.UnitTests --filter "FullyQualifiedName~<Name>"` |
| Focused backend integration | `dotnet test tests/RetailPOS.IntegrationTests --filter "FullyQualifiedName~<Name>"` (Test stack; never Dev 5435) |
| Backend build | `dotnet build RetailPOS.slnx` |
| Live infra (only when relevant) | `dotnet test tests/RetailPOS.IntegrationTests --filter "Category=LiveInfrastructure"` |
| Focused frontend | in `retailpos-frontend`: `npx ng test --watch=false --include <path/to.spec.ts>` |
| Frontend full + build | in `retailpos-frontend`: `npm test -- --watch=false`, `npm run build` |
| Mobile (if touched) | in `retailpos-mobile`: `flutter analyze`, `flutter test <file>` |
| Acceptance gate (wide or high-risk change) | `dotnet test RetailPOS.slnx` |

- There is no frontend lint script; the build is the type check. Do not claim lint ran.
- Never start or recreate Docker stacks, apply migrations or run E2E without approval. If a required service is down, report it as a blocker; do not "fix" infrastructure.
- Do not change code just to silence failures unrelated to the change; report them as pre-existing only with evidence (the failure is in code the change does not touch). Never stash or reset to prove it.
- Report: each command, pass/fail/skip counts, failures (first relevant lines), and anything left unverified.
