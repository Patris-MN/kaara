# ADR-0008: Workspace access as the WorkManagement authorization boundary

Status: Accepted
Date: 2026-08-26

## Context

Active Membership currently grants access to every Workspace and Project in a
tenant. Phase 5 requires Owner/Admin to retain broad access while Members receive
explicit View or Edit access. Projects inherit their Workspace permission.

Tenancy owns Membership role/status and WorkManagement owns Workspace/Project.
Neither module may reference the other, while PostgreSQL RLS must remain focused
on the tenant boundary rather than becoming a resource-permission engine.

## Decision

- WorkManagement owns the tenant-scoped `WorkspaceAccess` entity and the narrow
  View/Edit authorization policy.
- Owner/Admin receive implicit full WorkManagement access. Active Members require
  a `WorkspaceAccess` row. Projects inherit the parent Workspace level.
- The Host composes the freshly resolved Tenancy Membership with WorkManagement
  access rows inside the existing tenant RLS transaction.
- Cross-module `(tenant_id, membership_id)` foreign keys are wired only in the
  Host. `(tenant_id, workspace_id)` remains inside WorkManagement.
- Resource authorization is enforced by backend endpoints. PostgreSQL RLS
  continues to enforce Tenant A versus Tenant B and is not expanded into a
  per-resource policy engine.
- Inaccessible Workspace/Project identifiers return not found. A visible
  View-only Workspace returns forbidden for an attempted edit.
- Membership role/status and WorkspaceAccess are database-backed and are never
  stored in JWT claims.

## Alternatives considered

- Project-specific permissions — rejected for Phase 5; inheritance is simpler
  and meets the current requirement.
- Permission claims in JWT — rejected because role/access changes must apply
  immediately without token re-issuance.
- Resource permissions inside Workspace/Project RLS — rejected because it mixes
  tenant isolation with product authorization and would substantially complicate
  policy maintenance.
- A generic RBAC/ABAC framework — rejected because View/Edit Workspace access is
  the only current requirement.
- Direct module references — rejected by the modular-monolith boundary.

## Consequences

- `workspace_access` must carry `tenant_id`, composite tenant-safe foreign keys,
  explicit app-role grants, and ENABLE/FORCE RLS.
- Owner/Admin do not need per-Workspace rows.
- Invited/Suspended Memberships cannot establish tenant context, so any retained
  access rows are inert.
- Future Task authorization can inherit Project → Workspace access. Project
  overrides remain future work and require a separate decision if introduced.
