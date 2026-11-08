# IraniMart POS v2 — Product Exchange: Browser UAT Environment

| Item | Value |
|---|---|
| Version | 1.0 (2026-11-08) |
| Purpose | A persistent, isolated, synthetic environment for browser UAT of Product Exchange: Playwright runs and manual testing. |
| Database | `iranimart_pos_v2_exchange_uat`, on the **Test** PostgreSQL server `127.0.0.1:55432` (container `iranimart-test-postgres-1`) |
| Never | Dev (`5435`/`retailpos_db`), Staging or Production. Never dropped, truncated or reset automatically. A clean reset needs the owner's explicit approval. |

## 1. Components

| Component | Where | Notes |
|---|---|---|
| Provisioning tool | `tools/RetailPOS.ExchangeUat/` (console, no new packages) | Commands: <br>• `provision` — create the database if missing, apply all migrations, seed synthetic data (idempotent); <br>• `api` — start the API; <br>• `status`; <br>• `ids`; <br>• `reconcile` (read-only checks); <br>• `stock` (read-only quantity). |
| UAT API | `http://127.0.0.1:5105` | The Release build of `RetailPOS.API`. See §3 for the overrides. |
| UAT web | `http://127.0.0.1:4200` | `e2e/exchange-uat/uat-web.mjs` (no dependencies). It serves the Angular development build from `e2e/exchange-uat/.uat/web` and points the compiled API URL at 5105. No project file is changed. |
| Browser tests | `e2e/exchange-uat/` | Playwright `1.52.0` (local dev dependency of this folder only). It uses the installed **Microsoft Edge** (`channel: msedge`), so no browser is downloaded. |
| Local artefacts (git-ignored) | `e2e/exchange-uat/.uat/` | Contents: <br>• `credentials.json` — the synthetic password; <br>• `ids.json` — synthetic ids; <br>• `auth/` — test sign-in state; <br>• `web/` — the built app; <br>• `logs/`. <br>Also git-ignored: `evidence/`, `test-results/`, `playwright-report/`, `node_modules/`. |

## 2. Database connection (no secrets here)
- The tool reads the Test-server connection from the existing IntegrationTests user-secret `RETAILPOS_TEST_POSTGRES` (user-secrets id `RetailPOS.IntegrationTests.LocalInfrastructure`). It only replaces the database name.
- The connection string is passed to the API **only as an environment variable** of the child process. It is never printed, written to a file or put on a command line.
- **Isolation guards** (the tool refuses to run otherwise):
  - the port must be `55432` and never `5435`;
  - the host must be loopback;
  - the database name is fixed;
  - an existing database must carry the comment `iranimart-exchange-uat:synthetic:persistent`.
- The test fixtures' clean-up only drops `<prefix>_<guid>` databases with test markers, so it never touches this database.
- Migrations applied: all 80 in the chain, ending with:
  - `20261107221022_PosV2ExchangeFoundation`;
  - `20261108093416_PosV2ExchangeReversalRetainedCost`;
  - `20261108094509_PosV2ExchangeApprovalFailures`.

## 3. API configuration (all non-secret overrides)

| Setting | Value | Why |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Development` | Local leniency only. The validator still rejects these values elsewhere. |
| `--urls` | `http://127.0.0.1:5105` | Never the Development port 5005, so the Dev API can run at the same time. |
| `ConnectionStrings:DefaultConnection` | UAT database (environment variable only) | — |
| `ConnectionStrings:Redis` | empty | In-memory cache; the shared Dev Redis is never used. |
| `PosCache:InstanceName` | `exchange-uat:` | — |
| `DurableDelivery:Enabled`, `Elasticsearch:Enabled` | `false` | No broker or search traffic. |
| `Database:MigrateOnStartup`, `SeedOnStartup` | `false` | The tool migrates; `DbSeeder` (fixed demo users) is never run. |

The JWT signing key is the local Development user-secret of the API. Tokens are only valid for this local API.

## 4. Synthetic data
The **Exchange UAT Business (synthetic)** is created through the real onboarding store (`BusinessOnboardingWriteStore`), so it has template roles and real accounting provisioning. Then:

| Kind | Data |
|---|---|
| Outlets / terminals | **UAT Outlet A** (prefix `UATA`, terminal `UATA-T1`); **UAT Outlet B** (prefix `UATB`, terminal `UATB-T1`) |
| POS settings | 10 % VAT on top of prices, Asia/Dhaka |
| Users (role, outlet) | <br>• `uat.owner@exchange-uat.test` — BusinessOwner, all outlets; <br>• `uat.manager.a@…` — OutletManager, A (supervisor A); <br>• `uat.sales.a@…` — SalesPerson, A; <br>• `uat.manager.b@…` — OutletManager, B (supervisor B); <br>• `uat.sales.b@…` — SalesPerson, B; <br>• `uat.accounts@…` — AccountsAdmin. <br>All are in domain `exchange-uat.test` and share one random synthetic password. |
| Password | In `e2e/exchange-uat/.uat/credentials.json` (git-ignored; generated on the first `provision`). It is never written in documents or logs. |
| Products (price / cost) | <br>• UAT Shirt 100 / 40; <br>• Jacket 120 / 50; <br>• Cap 50 / 20; <br>• Coat 300 / 120; <br>• Watch 600 / 250. <br>The barcode is `UAT-<NAME>-STD`. |
| Other | One registered customer `UAT-CUST-1` with a synthetic phone; one supplier |
| Per run (Playwright global setup) | <br>• Signs in through the real login API. <br>• Saves the exchange policy if it differs: window 7 days, re-exchange window 7 days, chain max 2, high-value threshold 500, lower-price exchange on, phone `optional`. <br>• Opens today's cash sessions (float 5000). An older-day session is closed at its expected cash first. <br>• Receives 20 units of every product at both outlets (GRN). <br>• Creates source sales through the checkout API, so they carry full price, VAT and cost evidence. |

## 5. Safe start-up (Windows, Git Bash or PowerShell; from the repository root)
1. Make sure the Test PostgreSQL container is running (`docker ps` shows `iranimart-test-postgres-1` on `127.0.0.1:55432`). Do not start or stop other stacks.
2. Build: `dotnet build src/RetailPOS.API -c Release` and `dotnet build tools/RetailPOS.ExchangeUat -c Debug`.
3. Provision (safe to repeat): `dotnet run --project tools/RetailPOS.ExchangeUat -c Debug --no-build -- provision`, then `… -- ids`.
4. Start the API (keep the window open): `dotnet run --project tools/RetailPOS.ExchangeUat -c Debug --no-build -- api`.
5. Build the web app: `cd retailpos-frontend && npx ng build --configuration development --output-path ../e2e/exchange-uat/.uat/web`.
6. Start the web app: `cd e2e/exchange-uat && npm install` (first time only; set `PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1`), then `npm run web`.
7. Manual testing: open `http://127.0.0.1:4200` and sign in as a UAT user. The password is in `.uat/credentials.json`.
8. Automated run: `cd e2e/exchange-uat && npx playwright test`.
   - Set `UAT_RELOGIN=1` after role changes.
   - Set `UAT_SKIP_LOCKOUT=1` to re-run within 15 minutes of the lockout test.
   - The HTML report is in `playwright-report/`; screenshots, downloads, console and network logs and the reconciliation are in `evidence/`.
9. Read-only checks at any time: `dotnet run --project tools/RetailPOS.ExchangeUat -c Debug --no-build -- reconcile`.

**Stopping:** close the API and web windows. Data stays in the UAT database for inspection.

## 6. Known constraints
- The login (5 per minute) and supervisor-approval (5 per minute) rate limits are per IP, and all local traffic is `127.0.0.1`. The tests pace themselves.
- The lockout test leaves the `uat.accounts` login and the Sales B cashier locked for 15 minutes (L1b), by design.
- The UAT database grows with every run: new sales, exchanges and GRNs. This is expected, and it is never cleaned automatically.
