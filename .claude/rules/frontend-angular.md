---
paths:
  - "retailpos-frontend/**"
---

# Angular frontend (`retailpos-frontend/`)

- Angular 21, standalone components (no NgModules), signals for local state, RxJS 7.8 for HTTP/streams, TypeScript 5.9 strict typing (avoid `any`), Tailwind 3 + `--rp-*` design tokens. Confirm versions in `package.json` before choosing APIs.
- **Before any UI work, read the "UI Design System" section of `.github/copilot-instructions.md`.** It is binding (tokens, shared UI kit in `src/app/components/shared/ui/`, no hard-coded colours, no new UI libraries, accessibility, 390/768/1440 checks). Do not duplicate its content here.
- Structure: `pages/<feature>/` for screens, `services/` for API clients/stores, `guards/`, `interceptors/` (auth, session, error), `directives/has-permission.directive.ts`, `models/`. Follow the closest existing feature; do not add new state libraries.
- Forms: reactive forms with explicit validation; prevent double submit; show loading/empty/error states via `app-ui-state`; preserve user input on recoverable errors; refresh affected lists/stores after mutations; guard against stale responses.
- Permissions: keep route guards, menu permissions and per-action checks (`hasPermission`). The UI never decides authorization alone; the server is authoritative.
- 401 is handled by the existing auth/session interceptors; **403 shows access denied and never logs out**.
- Money: display the backend-computed amounts with the existing currency/rounding helpers (`currency.service`, `company-date.service` for dates). Never treat client arithmetic as final.
- POS/cashier screens: keyboard-first, high-throughput; reference `Docs/08_Sales_And_POS/iranimart_pos_prototype.html` for UX.
- Subscriptions: `async` pipe, `takeUntilDestroyed` or signals. No leaked subscriptions.
- Verify with focused specs (`npx ng test --watch=false --include <spec>`), then `npm test -- --watch=false` and `npm run build`. There is no lint script; do not claim lint ran.
