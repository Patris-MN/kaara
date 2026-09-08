# Work Management Module

Owns tenant-scoped workspaces, projects, and tasks (Phase 6 product; Phase 7 did not change this module).

Entities include:

- `Workspace` — `Id`, `TenantId`, `Name`, `CreatedAtUtc`
- `Project` — `Id`, `TenantId`, `WorkspaceId`, `Name`, `CreatedAtUtc`
- `WorkTask` plus tags, comments, activity, read-state, and in-app notifications

PostgreSQL RLS (Host migration) scopes both tables to `app.current_tenant_id`.
Projects cannot reference a workspace in another tenant: composite FK
`(tenant_id, workspace_id) → workspaces (tenant_id, id)`.

This module does **not** reference Tenancy or Identity. HTTP adapters live in Host.
Entitlement / billing checks are not implemented here.
