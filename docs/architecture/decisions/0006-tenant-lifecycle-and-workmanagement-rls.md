# ADR-0006: Tenant creation bootstrap and WorkManagement composite tenancy

Status: Accepted
Date: 2026-08-24

## Context

Phase 3 left `tenants INSERT WITH CHECK (true)` so a tenant row could be
created before any Membership existed. That was a bootstrap hole: `app_role`
could insert a tenant without an owner.

Memberships RLS (`user_id = current_user_id`) cannot see another user's row,
so inviting a member cannot use a tenant-scoped memberships policy that
depends on `current_tenant_id` for SELECT of the invitee's row. Inserting an
Invited row for someone else also fails `memberships_self` WITH CHECK.

WorkManagement projects must not reference a workspace from another tenant.

## Decision

1. **Tenant INSERT** requires `app.current_user_id` to be set (authenticated
   identity GUC). The application creates Tenant + Owner Membership in a
   **single transaction**. `current_tenant_id` is still unset during that
   insert (no circular Membership bootstrap). Unauthenticated connections
   cannot insert tenants.

2. **Invited membership INSERT** uses an additional policy:
   `tenant_id = current_tenant_id` AND `current_tenant_id` is set. The Host
   only sets the tenant GUC after Active Membership is proven (existing
   session factory). Application code additionally requires Owner/Admin.
   Invitee SELECT/UPDATE of their own Invited row still uses `memberships_self`.

3. **Workspaces / projects** use the same `app.uuid_setting('app.current_tenant_id')`
   RLS as other tenant-owned tables. Projects reference workspaces via
   composite FK `(tenant_id, workspace_id) → (tenant_id, id)`.

## Alternatives

- SECURITY DEFINER create_tenant() — stronger DB least privilege, more
  operational surface; deferred.
- Tenant roles in JWT — rejected; membership is database-backed.

## Consequences

- Raw `app_role` SQL with a spoofed `current_tenant_id` can still INSERT
  memberships for that tenant (same class of GUC trust as other tenant-owned
  writes). The HTTP/application path never sets the tenant GUC without Active
  Membership, and never trusts client UserId/TenantId as identity.
