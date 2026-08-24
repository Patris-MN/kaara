# PTS — Task and Project Management SaaS

A production-grade, B2B, **multi-tenant** Task and Project Management platform,
built as a **Modular Monolith**.

> **Current phase: Phase 4 — Tenant lifecycle & first WorkManagement surfaces, proven against real PostgreSQL.**
> Authenticated tenant create/invite/accept, Workspace and Project persistence with RLS.

## Project purpose

A SaaS product where organizations ("tenants") manage projects, tasks, comments,
and collaboration, with strict data isolation between tenants, organization-level
subscriptions/billing, and a multilingual UI from day one.

## Architecture

Single deployable ASP.NET Core application, internally organized into
independent bounded contexts ("modules") with enforced dependency rules — not
microservices. Full detail lives in
[`docs/architecture/architecture-charter.md`](docs/architecture/architecture-charter.md),
with individual decisions recorded under
[`docs/architecture/decisions/`](docs/architecture/decisions/).

Persistent, agent-enforced rules derived from the charter live in
[`.cursor/rules/`](.cursor/rules/).

### Module boundaries

| Module | Responsibility |
|---|---|
| **Identity** | Global user identity/authentication |
| **Tenancy** | Tenants (organizations) & Membership (user ↔ tenant + tenant role) |
| **WorkManagement** | Workspaces, projects, tasks, comments (core business domain) |
| **Entitlements** | Plan/feature/limit checks derived from subscription state |
| **Billing** | Subscriptions & payment-provider integration |
| **Storage** | Tenant-aware file/object storage abstraction |
| **PlatformAdministration** | Internal operator permissions, separate from tenant roles |
| **Audit** | Durable, tenant-aware audit trail |

A global `User` (Identity) is always separate from tenant `Membership` (Tenancy);
a user may belong to multiple tenants. Platform-administrator permissions
(PlatformAdministration) are separate from any tenant-level role.

## Technology stack

| Layer | Technology |
|---|---|
| Backend | ASP.NET Core (.NET 10 LTS) |
| Frontend | React + TypeScript (Vite) |
| Database | PostgreSQL 17, with Row-Level Security |
| ORM | Entity Framework Core |
| Localization | i18next / react-i18next |
| Local infrastructure | Docker / Docker Compose |

## Multi-tenancy & security posture

- Every tenant-owned table belongs to exactly one `TenantId`; cross-tenant
  references are prohibited.
- `TenantId` is **never** trusted from client input (headers, body, URL, query
  params, frontend state) — it is established server-side after verifying
  `Membership`.
- **PostgreSQL Row-Level Security is mandatory** on tenant-owned tables, as a
  second, independent layer beneath application-level filtering.
- Two PostgreSQL roles: `migrator_role` (schema owner, runs migrations/DDL/RLS
  policy creation) and `app_role` (used by the running application — no DDL, no
  `BYPASSRLS`, not the database owner). The application never runs as
  `migrator_role` or a superuser.
- Background jobs, cache keys, and storage keys are all tenant-aware by design.

Full detail: [`docs/architecture/architecture-charter.md`](docs/architecture/architecture-charter.md).

## Local development approach

### Prerequisites

- .NET 10 SDK (pinned via [`global.json`](global.json))
- Node.js 20+ and npm
- Docker / Docker Compose (for local PostgreSQL)

### Database

Via Docker (preferred):

```bash
cd infra/docker
cp .env.example .env   # then set real local passwords
docker compose up -d
```

See [`infra/docker/README.md`](infra/docker/README.md) for what this provisions.

Without Docker, provision an equivalent native PostgreSQL 17+ instance yourself:
create a database, then run
[`infra/docker/postgres/init/templates/roles.template.sql`](infra/docker/postgres/init/templates/roles.template.sql)
against it as a superuser (see that script and
[`.cursor/rules/30-database-security-roles.mdc`](.cursor/rules/30-database-security-roles.mdc)
for exactly what it provisions and why).

Either way, the running application and migration tooling read credentials only
from environment variables — never from source or `appsettings.json`:

```bash
export PTS_APP_PASSWORD=...        # app_role password — required to run the app or PTS.IntegrationTests
export PTS_MIGRATOR_PASSWORD=...   # migrator_role password — required for `dotnet ef` commands
# optional, default to localhost:5432/pts:
export POSTGRES_HOST=localhost
export POSTGRES_HOST_PORT=5432
export POSTGRES_DB=pts
```

### Backend

```bash
dotnet build PTS.slnx
dotnet test PTS.slnx          # architecture tests always run; PTS.IntegrationTests
                               # skips its tests (not fails) if PostgreSQL/PTS_APP_PASSWORD
                               # isn't available
dotnet ef database update --project src/Backend/Host/PTS.Host   # apply migrations as migrator_role
dotnet run --project src/Backend/Host/PTS.Host   # GET /health
```

### Frontend

```bash
cd src/Web
npm install
npm run dev
npm run test
npm run build
```

## Supported languages

| Code | Language | Direction |
|---|---|---|
| `en` | English | LTR (fallback/default) |
| `ar` | Arabic | RTL |
| `ku` | Kurdish Sorani | RTL |

The UI switches language at runtime (no page reload), and `<html lang>`/`<html dir>`
update automatically. Translation resources live under `src/Web/src/locales/<code>/`,
organized by namespace with semantic keys (e.g. `navigation.projects`) — never
full sentences as keys, and never duplicated components per language. User-entered
business content (project names, task titles, comments, descriptions, workspace
names) is stored and shown exactly as entered and is never machine-translated.
See [`src/Web/README.md`](src/Web/README.md) and
[`.cursor/rules/60-localization-i18n.mdc`](.cursor/rules/60-localization-i18n.mdc).

## Current implementation phase

**Phase 4 — Tenant lifecycle & first WorkManagement surfaces, proven against real PostgreSQL.**
Phase 3 authentication and identity RLS remain in force. Phase 4 adds authenticated
tenant create (atomic Owner membership), Owner/Admin invite + accept, and
Workspace/Project tables with RLS plus a composite FK so a project cannot
reference another tenant's workspace (ADR-0006).

Not implemented yet (by design): Tasks, OAuth/social login, cookie/session UI auth,
email delivery, billing/Stripe, entitlements enforcement, file uploads,
notifications, dashboards, background jobs.

What exists: JWT auth; `POST /tenants`, invitations; workspace/project HTTP APIs;
dual transaction-local GUCs; RLS on users, tenants, memberships, workspaces,
projects, and the isolation test table. The React app shell and `en`/`ar`/`ku` +
RTL localization pipeline are unchanged.
