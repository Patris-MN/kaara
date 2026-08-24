# Work Management Module

Owns tenant-scoped workspaces and projects (tasks remain out of scope).

Entities:

- `Workspace` — `Id`, `TenantId`, `Name`, `CreatedAtUtc`
- `Project` — `Id`, `TenantId`, `WorkspaceId`, `Name`, `CreatedAtUtc`

PostgreSQL RLS (Host migration) scopes both tables to `app.current_tenant_id`.
Projects cannot reference a workspace in another tenant: composite FK
`(tenant_id, workspace_id) → workspaces (tenant_id, id)`.

This module does **not** reference Tenancy or Identity. HTTP adapters live in Host.
Entitlement / billing checks are not implemented here.
