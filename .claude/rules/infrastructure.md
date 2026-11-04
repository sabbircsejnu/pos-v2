---
paths:
  - "**/appsettings*.json"
  - "**/docker-compose*.yml"
  - "**/docker-compose*.yaml"
  - "docker/**"
  - "**/*.csproj"
  - "**/env.*.example"
---

# Local infrastructure and configuration

- Binding rules: `.github/instructions/local-infrastructure.instructions.md`. Recorded state: `Docs/Development/LOCAL_INFRASTRUCTURE.md`. Read the relevant section; do not rediscover what they already record.
- Active stacks: `docker/compose.dev.yml` (`iranimart-dev`) and `docker/compose.test.yml` (`iranimart-test`). The legacy `compose` / `retailpos-a2-pgtest` stacks are retired: never start, reference or delete them.
- Never run `docker compose down -v` or `docker system prune --volumes`, and never stop/remove containers, without approval. Validate compose files only with `config --quiet`.
- Config ownership: `appsettings.json` holds non-secret defaults; Development holds local endpoints; Staging/Production get endpoints and credentials from the environment (`InfrastructureConfigurationValidator` fails startup otherwise). Secrets come only from user-secrets or env vars.
- `.csproj`: no new packages or version upgrades without approval; keep versions aligned across projects.
