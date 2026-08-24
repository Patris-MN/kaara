# Architecture Charter

Status: **Phase 4.5 — Frontend vertical slice wired to authenticated tenant/workspace/project APIs**
Last updated: 2026-08-24

This document is the source of truth for how this system is built. When code and
this document disagree, that is a bug in one of the two — raise it, don't silently
pick one. Persistent, machine-enforced versions of the rules below live in
`.cursor/rules/`.

## 1. Purpose

A production-grade, B2B, multi-tenant Task and Project Management SaaS. Phase 1
established the architectural skeleton. Phase 2 implemented Identity (`User`),
Tenancy (`Tenant`/`Membership`), server-side `TenantContext`, and tenant-owned
PostgreSQL RLS. Phase 3 added JWT authentication, `ICurrentUser`, and RLS on
`users` / `tenants` / `memberships` (ADR-0005). Persistence composition is in
`docs/architecture/decisions/0004-ports-and-adapters-for-persistence.md`.
Product features (projects, tasks, billing, notifications, dashboards, file
uploads) are still not implemented.

## 2. Architecture style: Modular Monolith

A single deployable ASP.NET Core application, internally organized into
independent bounded contexts ("modules"). See
`.cursor/rules/00-modular-monolith-architecture.mdc` for the enforced dependency
rules. Rationale is recorded in
[`decisions/0001-modular-monolith-over-microservices.md`](decisions/0001-modular-monolith-over-microservices.md).

### 2.1 Bounded contexts

| Module | Owns | Must never contain |
|---|---|---|
| Identity | Global `User`, credentials, auth | Tenant membership, tenant roles |
| Tenancy | `Tenant`, `Membership`, tenant-context resolution | Credentials, platform-admin permissions |
| WorkManagement | Workspaces, projects, tasks, comments | Billing/entitlement logic, storage internals |
| Entitlements | Plan/feature/limit checks | Payment provider integration |
| Billing | Subscriptions, payment provider integration | Feature-flag/limit decisions |
| Storage | Tenant-aware object storage abstraction | Business rules about what's attached |
| PlatformAdministration | Internal operator permissions & audited cross-tenant tooling | Tenant-level roles |
| Audit | Durable audit trail (tenant + platform scoped) | General app logging/metrics |

### 2.2 Composition

`src/Backend/Host/PTS.Host` is the single composition root. It references every
module; no module references another module or the Host. Cross-module contracts,
when eventually needed, live in `src/Backend/Shared/PTS.SharedKernel`, which must
never depend on a module or the Host. This is mechanically enforced by
`tests/Backend/PTS.Architecture.Tests`.

### 2.3 Deliberately excluded (for now)

Microservices, message brokers, distributed event buses, CQRS, MediatR, and a
generic repository abstraction are **not** used. Introducing any of them requires
a new ADR under `docs/architecture/decisions/` documenting the concrete,
current requirement first. See
[`decisions/0003-avoid-cqrs-mediatr-repository-abstractions.md`](decisions/0003-avoid-cqrs-mediatr-repository-abstractions.md).

## 3. Identity vs Membership vs Platform Administration

Three distinct concepts, never merged:

- **User** (Identity) — a global account, no `TenantId`.
- **Membership** (Tenancy) — `User` + `TenantId` + tenant-level role(s). A user
  may hold multiple `Membership` records (belong to many tenants).
- **Platform Administrator** (PlatformAdministration) — internal staff
  permissions, entirely separate from any `Membership` role.

Full rules: `.cursor/rules/20-identity-membership-platform-admin.mdc`.

## 4. Multi-Tenancy & Data Isolation

### 4.1 TenantId provenance

`TenantId` is **never** trusted from HTTP headers, request body, URL, query
parameters, or frontend state. The backend derives it only after authenticating
the user and verifying an active `Membership` (Tenancy module). That
server-established value is the only legitimate source for the rest of the
request pipeline.

### 4.2 Defense in depth: RLS + application filtering

Two independent layers, both required:

1. **PostgreSQL Row-Level Security (RLS)** on every tenant-owned table, using a
   session-scoped setting (`SET app.current_tenant_id = ...`) that the
   application sets after resolving tenant context, referenced by RLS policies
   via `current_setting('app.current_tenant_id')`. Policies are created by
   `migrator_role` as part of migrations.
2. **Application-level `WHERE TenantId = ...` filtering**, kept, but not treated
   as sufficient on its own — a missing filter in application code must not be
   able to leak data if RLS is configured correctly. Rationale in
   [`decisions/0002-row-level-security-for-tenant-isolation.md`](decisions/0002-row-level-security-for-tenant-isolation.md).

### 4.3 Cross-cutting tenant-awareness

- Background jobs carry `TenantId` explicitly and re-establish tenant context
  themselves; never inferred from ambient/global state.
- Cache keys include `TenantId`.
- Storage object keys/prefixes include `TenantId`.
- Audit records include tenant context where applicable.
- Cross-tenant references (foreign keys/relations between different tenants'
  data) are prohibited.

### 4.4 Enterprise isolation path (future)

Default isolation is a shared schema with RLS. Larger tenants may later move to a
dedicated schema or database. The Tenancy module is the only place that should
know which isolation strategy applies to a given tenant — other modules consume
tenant context without needing to know the underlying strategy, so this change is
possible later without rewriting consuming modules.

## 5. Database Security: Role Separation

Two PostgreSQL roles (provisioned by
`infra/docker/postgres/init/01-create-roles.sh`):

- **`migrator_role`** — schema owner; runs migrations, DDL, RLS policy creation.
  Used only by migration tooling, never by the running application.
- **`app_role`** — used by the running application. No DDL beyond explicit
  per-table `SELECT`/`INSERT`/`UPDATE`/`DELETE` grants issued by migrations, no
  `BYPASSRLS`, not the database owner, not a superuser.

The production application must never run as `migrator_role` or a PostgreSQL
superuser. Full rules: `.cursor/rules/30-database-security-roles.mdc`.

## 6. Entitlements & Billing

Billing owns subscription lifecycle and payment-provider integration.
Entitlements owns "what can this tenant do right now", derived from subscription
state but without talking to the payment provider directly. Kept as separate
modules so provider-specific concerns (webhooks, retries) never leak into
frequently-called feature-check code paths. See
`.cursor/rules/40-entitlements-and-billing.mdc`.

## 7. Audit Logging

The Audit module owns the durable audit trail. Tenant-related audit records carry
`TenantId`; platform-level actions (e.g. a platform admin's audited cross-tenant
access) get their own trail, never folded into a tenant's history.

## 8. Localization Architecture

### 8.1 Supported languages (MVP)

| Code | Language | Direction | Role |
|---|---|---|---|
| `en` | English | LTR | Fallback/default |
| `ar` | Arabic | RTL | Supported |
| `ku` | Kurdish Sorani | RTL | Supported |

### 8.2 Design

- Library: [i18next](https://www.i18next.com/) + `react-i18next` — supports
  runtime language switching without a page reload, and resource files organized
  per locale/namespace (matches the "no duplicated components per language"
  requirement).
- Resources: `src/Web/src/locales/<locale>/<namespace>.json`
  (namespaces so far: `common`, `navigation`, `auth`).
- Adding a language = add a locale folder + register the code in
  `src/Web/src/i18n/config.ts`. No component changes required.
- `src/Web/src/i18n/LanguageProvider.tsx` is the single place that syncs
  `<html lang>` / `<html dir>` with the active language, driven by
  `src/Web/src/i18n/direction.ts` (unit tested).
- Translation keys are semantic and namespaced (`navigation.projects`,
  `common.save`, `auth.signIn`), never full sentences.
- No user-facing text is hardcoded inside React components.

### 8.3 Business content is never translated

Project names, task titles, comments, descriptions, workspace names, and other
user-entered business content are stored and displayed exactly as entered,
regardless of UI locale. They never pass through translation resources or
machine translation. Only UI chrome (labels, navigation, system messages) is
localized.

### 8.4 Future: language as a user preference

The selected language is designed to later be persisted through the Identity
module as part of a signed-in user's preferences. Phase 1 only persists the
choice client-side (`localStorage`, via `i18next-browser-languagedetector`) — no
server-side preference storage exists yet, and none is faked ahead of that real
requirement.

### 8.5 Separation from authorization

Localization logic (`LanguageProvider`, `useTranslation`) has no awareness of
tenants, membership, or auth state, and vice versa — see
`.cursor/rules/60-localization-i18n.mdc`.

## 9. Repository Structure

```
PTS/
├── .cursor/rules/                     Persistent architecture rules (this charter, enforced)
├── docs/architecture/                 This charter + ADRs
├── infra/docker/                      Docker Compose: local PostgreSQL + role provisioning
├── src/
│   ├── Backend/
│   │   ├── Host/PTS.Host/             Composition root (ASP.NET Core)
│   │   ├── Modules/<Name>/PTS.Modules.<Name>/   One project per bounded context
│   │   └── Shared/PTS.SharedKernel/   Cross-module contracts only, no module deps
│   └── Web/                           React + TypeScript (Vite)
│       └── src/locales/<en|ar|ku>/    Translation resources
├── tests/Backend/PTS.Architecture.Tests/   Module-boundary enforcement tests
├── global.json                        Pinned .NET SDK version
└── PTS.slnx                            Solution file
```

## 10. Local Development

- Backend: .NET 10 LTS, `dotnet build`/`dotnet test` against `PTS.slnx`.
- Frontend: Node.js + npm, `npm run dev` / `npm run build` / `npm run test` in
  `src/Web`.
- Database: `docker compose -f infra/docker/docker-compose.yml up -d` (PostgreSQL
  17, with `migrator_role`/`app_role` provisioned on first init).
- No application containers exist yet — see `infra/docker/README.md` for why.

## 11. Non-Goals (still deferred)

OAuth/social login, cookie-based browser sessions, tenant registration product
UX, projects, tasks, billing/Stripe, subscriptions, file uploads, notifications,
dashboards, and background jobs remain out of scope. Platform administration
stays unimplemented and separate from User/Membership.
