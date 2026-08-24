# ADR-0002: PostgreSQL Row-Level Security for Tenant Isolation

Status: Accepted
Date: 2026-08-23

## Context

This is a strict multi-tenant SaaS. A single bug — a missing `WHERE TenantId = ...`
clause, a mis-scoped EF Core query, a raw SQL statement written without the
filter — must not be able to leak one tenant's data to another. Application-level
filtering alone puts that guarantee entirely in the hands of every future line of
query-writing code, forever.

## Decision

Every tenant-owned PostgreSQL table has Row-Level Security enabled and forced
(`ENABLE ROW LEVEL SECURITY` + `FORCE ROW LEVEL SECURITY`), with a policy scoping
rows to a session-scoped setting (`current_setting('app.current_tenant_id')`) that
the application sets after resolving tenant context from an authenticated
`Membership`. Application-level `TenantId` filtering is still required, but is
explicitly treated as a second, non-load-bearing-alone layer of defense — not the
only line of defense.

## Alternatives considered

- **Application-level filtering only** — rejected. Relies on 100% of current and
  future code paths (including ad hoc scripts, raw SQL, admin tooling) getting the
  filter right, forever. A single miss is a cross-tenant data leak.
- **Separate database per tenant from day one** — rejected for the MVP. Adds
  significant operational overhead (migrations, connection management, backups
  multiplied per tenant) with no current requirement for it. The architecture
  keeps this option open per-tenant for a future enterprise isolation tier (see
  `docs/architecture/architecture-charter.md`, "Enterprise isolation path"),
  without paying that cost for every tenant now.
- **Separate schema per tenant from day one** — rejected for the same reason;
  same future option is kept open, not built now.

## Consequences

- Every migration that creates a tenant-owned table must also create its RLS
  policy — this is a mandatory step, documented in
  `.cursor/rules/30-database-security-roles.mdc`.
- `app_role` must never have `BYPASSRLS`; this is checked by a sanity assertion in
  `infra/docker/postgres/init/templates/roles.template.sql`.
- Requires the application to reliably set `app.current_tenant_id` on every
  connection/transaction that touches tenant-owned data before that data is
  queried — this becomes a core piece of request-pipeline middleware in a future
  phase, not yet implemented.
- Enables a future move to per-tenant schema/database for specific enterprise
  tenants without changing how consuming modules read "the current tenant".
