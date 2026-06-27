# Frontend Architecture

Updated: 2026-06-13

## Overview

This document describes the architectural conventions, standards, and patterns for the `retailpos-frontend` Angular application. It is the authoritative reference for frontend developers working on this project.

---

## Technology Stack

| Technology      | Version   | Role                                  |
|-----------------|-----------|---------------------------------------|
| Angular         | 21.1.x    | Application framework                 |
| TypeScript      | 5.9.x     | Primary language                      |
| Tailwind CSS    | 3.4.x     | Utility-first styling                 |
| Vitest          | 4.x       | Unit and component testing            |

---

## Project Structure

```text
retailpos-frontend/
├── angular.json
├── package.json
├── tailwind.config.js
├── tsconfig.json
├── tsconfig.app.json
├── tsconfig.spec.json
├── public/
└── src/
    ├── index.html
    ├── main.ts
    ├── styles.css
    ├── environments/
    └── app/
        ├── app.ts
        ├── app.config.ts
        ├── app.routes.ts
        ├── app.html
        ├── app.css
        ├── components/       # Shared/reusable UI components
        ├── directives/       # Custom Angular directives
        ├── guards/           # Route guards
        ├── interceptors/     # HTTP interceptors
        ├── models/           # Shared TypeScript interfaces/types
        ├── pages/            # Feature pages (routed views)
        ├── pipes/            # Custom Angular pipes
        └── services/         # Shared Angular services
```

### Pages (Feature Modules)

Each subdirectory under `pages/` corresponds to a functional domain:

| Directory         | Domain                       |
|-------------------|------------------------------|
| `accounting/`     | Accounts and finance         |
| `audit/`          | Audit log viewer             |
| `businesses/`     | Super-admin business list    |
| `customers/`      | Customer management          |
| `grn/`            | Goods receipt notes          |
| `pos/`            | Point-of-sale terminal       |
| `purchase-orders/`| Purchase order management    |
| `reports/`        | Reports and dashboards        |
| `sales/`          | Sales management             |
| `settings/`       | System settings (users, roles, outlets, warehouses, etc.) |
| `stock-adjustments/` | Stock adjustment management |
| `stock-transfers/`| Stock transfer management    |
| `suppliers/`      | Supplier management          |
| `variations/`     | Product variation management |

---

## Routing Conventions

- Use Angular standalone components with the file-based `app.routes.ts` root router.
- Lazy-load feature pages using `loadComponent` or `loadChildren` for route-level code splitting.
- Use `ActivatedRoute.queryParams` as the primary mechanism for passing and reading list state (see [List State Preservation](#list-state-preservation)).
- Route guards in `guards/` enforce authentication and permission-based access control (see [AUTHORIZATION.md](../02_Security/AUTHORIZATION.md)).

---

## HTTP and API Communication

- All API calls go through Angular's `HttpClient`.
- A global HTTP interceptor (`interceptors/`) handles:
  - JWT Bearer token attachment.
  - Refresh token flow on 401 responses.
  - Centralised error notification for non-handled errors.
- API base URLs are configured per environment in `environments/`.
- Do not hardcode API base URLs inside services.

---

## State Management

The project does not use a global state management library (e.g., NgRx, Akita). State is managed at the component and service level using:

- `signal`-based reactive state (Angular Signals, preferred for local component state).
- Service-level `BehaviorSubject` or `signal` stores for shared cross-component state.
- URL query parameters for list page state (see [List State Preservation](#list-state-preservation)).

---

## List State Preservation

**This is a mandatory standard for all list pages.**

All list/index pages in the application must preserve their filter, search, pagination, sorting, tab, and view-mode state across navigation. This ensures users are not forced to re-enter filters after navigating to a child route and back.

### Standard

The preferred implementation is **URL query parameters**, which additionally supports:

- Browser refresh (state survives).
- Deep linking (shareable URLs with state encoded).
- Browser back/forward button navigation.

**Preserved state properties:**

| Property         | URL Parameter | Default  |
|------------------|---------------|----------|
| Search text      | `search`      | `''`     |
| Status           | `status`      | `'all'`  |
| Page             | `page`        | `1`      |
| Page size        | `pageSize`    | `20`     |
| Sort column      | `sortBy`      | varies   |
| Sort direction   | `sortDir`     | `'desc'` |
| Active tab       | `tab`         | `'all'`  |
| View mode        | `view`        | `'table'`|

**Example URL:**

```
/customers?search=Ahmad&status=active&page=2&pageSize=20&sortBy=name&sortDir=asc
```

**Scope** — The following list pages must implement this standard:
Users, Roles, Products, Categories, Inventory, Warehouses, Outlets, Suppliers, Customers, Sales, Purchases, Expenses, GRN, Stock Adjustments, Stock Transfers, Businesses — and all future list pages.

**Full specification:** [LIST_STATE_PRESERVATION.md](LIST_STATE_PRESERVATION.md)

Filter visibility, advanced filter drawer behaviour, responsive filter panel behaviour, and active advanced-filter indicator rules are defined in [LIST_PAGE_FILTER_STANDARDS.md](LIST_PAGE_FILTER_STANDARDS.md) and are mandatory for all list/grid/report pages.

---

## Styling Conventions

- Use Tailwind CSS utility classes as the primary styling approach.
- Avoid writing custom CSS in component `.css` files except for cases Tailwind cannot handle.
- Global styles (resets, base typography, custom CSS variables) live in `src/styles.css`.
- Do not use inline `style` attributes in templates.

---

## Forms

- Use Angular Reactive Forms (`FormBuilder`, `FormGroup`, `FormControl`) for all data-entry forms.
- Apply validation at the form-control level and surface error messages via shared error-display components.
- Disable submit buttons while the form is invalid or a request is in flight.

---

## Authentication and Authorisation

- JWT-based authentication. Tokens stored in secure storage (not `localStorage`).
- `guards/` contains route guards that check authentication status and role/permission requirements.
- Permission checks in templates use a shared directive or pipe from `directives/` or `pipes/`.
- See [AUTHENTICATION.md](../02_Security/AUTHENTICATION.md) and [AUTHORIZATION.md](../02_Security/AUTHORIZATION.md) for full details.

---

## Testing

- Test framework: **Vitest**.
- Test files follow the `*.spec.ts` naming convention co-located with the source file.
- Unit test scope: services, pipes, directives, utility functions.
- Component test scope: critical interaction flows and state changes.
- See `tests/` at the root for backend integration tests (separate from frontend).

---

## Environment Configuration

Environment-specific values (API URL, feature flags) are defined in:

```text
src/environments/
├── environment.ts          # Development
└── environment.prod.ts     # Production
```

Use Angular's `environment` token injection or direct import — never hardcode environment-specific values outside these files.

---

## Related Documents

- [LIST_STATE_PRESERVATION.md](LIST_STATE_PRESERVATION.md) — mandatory standard for list page state
- [LIST_PAGE_FILTER_STANDARDS.md](LIST_PAGE_FILTER_STANDARDS.md) — reusable standard for list/report filter UX and advanced drawer behaviour
- [PROJECT_STRUCTURE.md](PROJECT_STRUCTURE.md) — full project structure overview
- [AUTHENTICATION.md](../02_Security/AUTHENTICATION.md) — authentication implementation
- [AUTHORIZATION.md](../02_Security/AUTHORIZATION.md) — permission-based access control
- [PERMISSION_MODEL.md](../02_Security/PERMISSION_MODEL.md) — permission model reference

---

*This document describes frontend architecture conventions. For backend architecture, refer to [PROJECT_STRUCTURE.md](PROJECT_STRUCTURE.md).*
