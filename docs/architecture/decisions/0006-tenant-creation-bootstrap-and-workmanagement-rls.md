# ADR-0006: Tenant creation bootstrap and WorkManagement composite tenancy

Status: Accepted
Date: 2026-08-24

## Context

Phase 3 allowed `INSERT` on `tenants` with `WITH CHECK (true)` so a tenant row
could exist before any Membership. That was an authorization hole: any
`app_role` session could insert a tenant without a verified user.

Memberships RLS is `user_id = app.current_user_id`, which is required so
membership lookup can run before `app.current_tenant_id` exists. That policy
cannot insert a row for a different user. Invitations therefore need a second,
narrow INSERT policy that only applies when a tenant GUC is already
established (i.e. the inviter already passed Active Membership).

WorkManagement `Project` must not reference a `Workspace` owned by another
tenant even if application code is buggy.

## Decision

1. Replace `tenants` INSERT `WITH CHECK (true)` with
   `app.uuid_setting('app.current_user_id') IS NOT NULL`.
   Tenant creation still happens *before* membership exists, so it cannot
   require `current_tenant_id`. The application path SET LOCALs the
   authenticated user id, inserts the tenant, and inserts the Owner membership
   in the same transaction.

2. Add `memberships` INSERT policy
   `tenant_id = app.current_tenant_id` (tenant GUC present). Combined with
   `memberships_self`, this allows Owner/Admin (who already opened a tenant
   session) to insert Invited rows for another user. Role checks stay in the
   Tenancy application service reading database Membership — not JWT claims.

3. Workspaces and projects use the same tenant GUC RLS as
   `tenant_isolation_test_records`.
   `workspaces` has alternate key `(tenant_id, id)`.
   `projects` FKs to that alternate key as `(tenant_id, workspace_id)`.

## Alternatives considered

- SECURITY DEFINER function owned by `migrator_role` for tenant create —
  stronger at the SQL console, but needs BYPASSRLS or row_security off.
  Rejected for this phase; application atomicity is the control plane.
- Putting Owner/Admin in JWT — rejected; membership can change while a token
  is valid.

## Consequences

- Unauthenticated or unset-user GUC cannot insert tenants.
- `app_role` with a SET LOCAL user id can still insert a tenant via raw SQL;
  the public API does not expose that. Same trust model as SET LOCAL tenant id.
- Invites require an Active tenant session (tenant GUC), so Invited/Suspended
  users cannot invite.
