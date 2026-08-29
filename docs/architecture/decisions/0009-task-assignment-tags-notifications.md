# ADR-0009: Task assignment, reusable tags, and in-app notifications

Status: Accepted
Date: 2026-08-29

## Context

Phase 6 already owns Tasks under Workspace/Project authorization. This extension
adds a single optional assignee, reusable tags, and durable in-app assignment
notifications without weakening tenant RLS or introducing a message broker.

## Decision

- Assignment references `MembershipId`, never a global `UserId`. The database
  enforces `(Task.TenantId, AssignedMembershipId) → Membership(TenantId, Id)`.
- An assignee must be an Active member who can already view the Task's
  Workspace (Owner/Admin implicit access, or Member View/Edit).
- Assignment and tag mutation use the existing Task Edit permission.
- Tags are tenant-scoped reusable metadata. Uniqueness is
  `(TenantId, NormalizedName)` after trim + `ToUpperInvariant()`.
  `CreatedByMembershipId` records the definer; tags are not private.
- `TaskAssigned` notifications are written in the same EF transaction as the
  Task save. Unchanged assignees create no row. Self-assignment creates no row.
- Notification SELECT/UPDATE is recipient-only via
  `app.current_membership_id`. INSERT is tenant-scoped so an editor can notify
  another member. Members may SELECT peer memberships/users only when
  `app.current_tenant_id` is set.

## Alternatives considered

- Assign by `UserId` — rejected; User identity is global and not tenant-safe.
- Private-per-creator tags — not required; tenant-visible reuse is simpler.
- Email/SignalR delivery — out of scope; durable rows plus refetch are enough.

## Consequences

Assignable-member listing can resolve display names for Members with Edit
without granting Owner/Admin privileges. Notification RLS is stricter than
ordinary tenant isolation and must keep `current_membership_id` SET LOCAL.
