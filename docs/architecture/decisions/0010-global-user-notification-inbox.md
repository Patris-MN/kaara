# ADR 0010: Global user notification inbox

## Status

Accepted (Phase 8.0.3)

## Context

In-app notifications (`notifications` table) are tenant-owned rows addressed to a
specific `RecipientMembershipId`. Existing list/mark-read APIs were scoped to
`/tenants/{tenantId}/notifications`, which required an active tenant context.
The application shell bell must represent the signed-in user's unread state
across all organizations, including when no organization is selected.

PostgreSQL RLS on `notifications` previously allowed SELECT/UPDATE only when
both `app.current_tenant_id` and `app.current_membership_id` matched the row.

## Decision

1. Add **user-level RLS policies** on `notifications` that permit SELECT/UPDATE
   when the row's recipient membership belongs to an **Active** membership for
   `app.current_user_id`. Existing tenant-scoped policies remain unchanged.

2. Introduce `IUserRlsSessionFactory` / `UserRlsSession` in `PTS.Host` to open
   transactions with only `app.current_user_id` set (SET LOCAL), mirroring the
   bootstrap pattern used by `EfMembershipLookup` and `GET /invitations`.

3. Expose shell inbox APIs:
   - `GET /notifications` — recent items + global `unreadCount`
   - `POST /notifications/{notificationId}/read`

   User identity is derived from JWT; the client does not supply inbox scope.

4. Store lightweight display snapshots on notification rows (`task_title`,
   `project_name`) at creation time so the inbox projection does not require
   cross-tenant joins to tenant-scoped task/project tables.

5. **Suspended** memberships: notifications addressed to inactive memberships are
   omitted from the global inbox (RLS requires `status = 'Active'`). Historical
   rows remain in the database but are not an authorization bypass.

6. **Invitations** remain separate (`Membership` with `Status = Invited` via
   `GET /invitations`). The global inbox architecture is compatible with future
   invitation rows but does not merge invitation lifecycle into `notifications`.

## Consequences

- One backend query powers the shell bell; no N+1 tenant notification fetches.
- Tenant isolation is preserved: users see only notifications for their own
  active memberships; cross-user leakage is blocked by RLS.
- Mark-read mutations remain recipient-scoped; marking one notification does
  not affect another membership's rows.
- Realtime push (SignalR/SSE/WebSockets) remains out of scope.
