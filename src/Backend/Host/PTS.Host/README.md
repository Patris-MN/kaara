# PTS.Host

## Purpose

The single ASP.NET Core composition root ("Host") of the modular monolith. It is the
*only* project allowed to reference every module. It wires up all bounded contexts,
owns the HTTP pipeline, and (in later phases) will own the single EF Core
`DbContext`/migration setup, authentication, and tenant-context middleware.

Modules never reference each other or the Host directly — see
`docs/architecture/architecture-charter.md` and `.cursor/rules/00-modular-monolith-architecture.mdc`.

## Phase 1 status

Exposes only a liveness endpoint (`GET /health`). No authentication, tenant
resolution, database access, or business endpoints exist yet.
