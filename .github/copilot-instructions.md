## Local Infrastructure

Authoritative reference: Docs/Development/LOCAL_INFRASTRUCTURE.md
Cutover plan/rollback: Docs/Development/DOCKER_DATA_PRESERVING_CUTOVER.md
Detailed rules: .github/instructions/local-infrastructure.instructions.md
Standard Compose stacks: docker/compose.dev.yml (six required Development services incl. Kibana and pgAdmin via the `admin` profile, enabled by `COMPOSE_PROFILES=admin` in docker/.env.dev) and docker/compose.test.yml (four Test services), configured by gitignored docker/.env.dev and docker/.env.test. They are the ACTIVE local infrastructure since the 17 Oct 2026 cutover; the old stacks (project `compose`, `retailpos-a2-pgtest`) are retired and stopped, with containers, volumes and backups retained for rollback — do not start, reference or delete them. Pending EF migrations are applied to Development only with explicit approval (API-05B/05C were applied on 17 Oct 2026; 58 migrations). Never stop/remove containers or run `docker compose down -v` without approval.

1. Reuse the standardized Docker stacks (`iranimart-dev` for Development, `iranimart-test` for tests); never create duplicate containers or reference the retired legacy containers.
2. Read LOCAL_INFRASTRUCTURE.md before touching infrastructure; do not repeat discovery it already records.
3. Keep Development and Test isolated: Development PostgreSQL (5435) is persistent; tests use the Test stack: PostgreSQL 55432, RabbitMQ 5673 (vhost `retailpos_test`), Redis 6383 (prefix `retailpos-test:`) and Elasticsearch 9201 (`retailpos-test-*` indices).
4. Use existing configuration keys, strongly typed options and DI extension methods (`appsettings.{Environment}.json`, user-secrets, environment variables); add no new configuration frameworks.
5. Never run destructive tests, flush Redis, purge queues, delete indices or apply migrations/seeding against persistent Development resources.
6. Never print or commit secrets; credentials come from user-secrets or environment variables.
7. Do not change Staging/Production infrastructure or configuration semantics without approval; they fail startup rather than fall back to Development.
8. Verify with focused, bounded checks and real test output (`dotnet test --filter "Category=LiveInfrastructure"`); a running container is not proof of connectivity.

## UI Design System (Angular frontend)

Applies to all Angular UI work in `retailpos-frontend/`. Authoritative references: Docs/UI-Standardisation/DESIGN_SYSTEM.md (visual language and contracts) and Docs/UI-Standardisation/PHASE_2A_REPORT.md (tokens, shared components, usage examples). Visual reference: the purchased Able Pro Angular full-version under `design-reference/able-pro/` (read-only; never edit, copy wholesale or import it).

1. Use the approved semantic design tokens (`--rp-*` custom properties in `retailpos-frontend/src/styles.css`, or the matching Tailwind `rp-*` colours, radii and shadows); read colour, type, spacing, radius, shadow, focus and density values from tokens, never from literals.
2. Reuse the shared UI kit in `src/app/components/shared/ui/` (import `UI_KIT` or the individual pieces): `appUiButton`, `app-ui-card`, `appUiControl` with `app-ui-form-field`, `app-ui-badge`, `app-ui-page-header`, `app-ui-state`, `app-ui-data-table` (lists; server mode keeps API calls and stale-response handling in the feature), `app-ui-dialog` / `app-ui-confirm-dialog` (modals; never hand-roll overlays or use Bootstrap JS), `app-ui-alert` (inline messages; global modal alerts stay on `AlertService`). Extend a shared component when it falls short instead of creating a variant elsewhere.
3. Follow the Able Pro visual language (blue primary, grey neutrals, flat bordered 12px cards, 8px controls, Inter with the existing system-font fallback). Do not bundle or redistribute purchased fonts or assets until the documented licence review is done.
4. Do not hard-code hex/rgb colours, Tailwind `gray-*`/`slate-*` shades, arbitrary pixel values or box-shadows in new or migrated UI; use the 4px-based `--rp-space-*` scale and `data-density="compact"` for dense screens.
5. Do not duplicate buttons, cards, alerts, tables or form controls, and do not add or edit global `.btn`, `.card`, `.table`, `.input`, `.badge` rules. Legacy classes stay until a screen is deliberately migrated; migrate a whole screen block rather than mixing both systems in it.
6. Follow the established patterns: `app-ui-page-header` at the top of a page, `app-ui-form-field` plus `ui-form-stack` for forms, `app-ui-state` for loading/empty/error areas with the feature supplying its own retry action.
7. Preserve permission-based visibility: keep existing route guards, menu permissions and per-action checks. Shared UI never decides authorization; pass actions in as projected content so the page keeps its own checks.
8. Keep accessibility and responsiveness: accessible names for icon-only controls, labels tied to controls, keyboard operation, visible focus, status never conveyed by colour alone, WCAG AA contrast, and layouts checked at 390, 768 and 1440 px.
9. Keep feature logic separate from presentation: shared UI components contain no HTTP, routing, permission or business logic, and visual behaviour needs no new service. Features own state, validation, payloads and API calls.
10. Do not add UI framework dependencies (Angular Material, Bootstrap or its JavaScript, AG Grid, other component libraries) without explicit approval.
11. Verify UI changes with focused component specs, then `npm test` and `npm run build` in `retailpos-frontend/`; do not claim visual or authenticated-browser verification that was not actually performed.
12. Treat the filled form field, company date picker, table action icon and horizontal/vertical stepper contracts in [DESIGN_SYSTEM.md](../Docs/UI-Standardisation/DESIGN_SYSTEM.md) as required shared patterns for future UI work. Reuse `components/shared/ui/`; do not mass-migrate screens or change feature-owned forms, permissions, dialogs, dates or API behavior as part of adopting them.