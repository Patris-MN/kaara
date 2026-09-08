# PTS.Host

## Purpose

The single ASP.NET Core composition root ("Host") of the modular monolith. It is the
*only* project allowed to reference every module. It wires up all bounded contexts,
owns the HTTP pipeline, the single EF Core `DbContext`/migration setup,
authentication, and tenant-context middleware.

Modules never reference each other or the Host directly — see
`docs/architecture/architecture-charter.md` and `.cursor/rules/00-modular-monolith-architecture.mdc`.

## Current status (Phase 7.2)

Composition root for Identity, Tenancy, WorkManagement (Workspace / Project /
Task), JWT auth, tenant context, and PostgreSQL migrations/RLS. Phase 7.2 added
`PUT /tenants/{tenantId}` (Owner/Admin name update) and a membership-gated
workspace-count function used by `GET /tenants`. `GET /health` remains
available. Product baseline: repository root `README.md`.
