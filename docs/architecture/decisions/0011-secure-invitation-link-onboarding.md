# ADR 0011: Secure Invitation-Link Onboarding Across Global Identity and Tenant Membership

## Status

Accepted (Phase 8.1)

## Context

PTS separates **User** (global identity) from **Membership** (tenant-scoped role).
Organization administrators must invite people by email without creating another
person's global account. Invitees may already have a PTS User or may need to
register first.

Prior to Phase 8.1, invitation was modeled only as an `Invited` Membership and
required the recipient to already exist as a User. There was no opaque
invitation token, no link-based onboarding, and no secure unauthenticated
preview.

Requirements:

- Cryptographically random, expiring, single-use invitation tokens
- Store **hash(token)** only; raw token available at creation/delivery time
- Token proves possession of an invite, **not** general authentication
- Acceptance must bind to the invited email address
- RLS must remain ENABLE + FORCE on tenant-owned tables
- No email delivery infrastructure exists yet in the repository

## Decision

### 1. Separate `tenant_invitations` from Membership lifecycle

Create `tenant_invitations` (tenant-owned, RLS-protected) holding:

- invited email, role, token hash, expiration, used/revoked timestamps
- optional link to pre-created `Invited` Membership when invitee User already exists
- denormalized preview snapshots (`organization_name`, `inviter_display_name`,
  grant workspace names) to avoid cross-table RLS recursion on unauthenticated preview

Pending workspace grants live in `invitation_workspace_grants` and are applied on
acceptance for **Member** role only. Owner/Admin retain implicit full workspace
access per existing Phase 5 rules.

### 2. Token security

- Generate opaque tokens via `InvitationTokenGenerator` (random + SHA-256 hash)
- Default lifetime: **7 days** (`EfTenantInvitationStore.InvitationLifetimeDays`)
- Resend revokes the prior pending invitation and issues a new token
- Never log raw tokens; Development returns `/invite/{token}` URL to authorized inviters only

### 3. HTTP surface

| Endpoint | Auth | Purpose |
|---|---|---|
| `POST /tenants/{tenantId}/invitations` | Owner/Admin | Create invitation |
| `GET /invite/{token}` | None | Safe preview |
| `POST /invite/{token}/accept` | User JWT | Existing user acceptance |
| `POST /invite/{token}/register` | None | New user profile + acceptance |
| `GET /tenants/{tenantId}/invitations/pending` | Owner/Admin | Pending list |
| `POST .../resend`, `POST .../revoke` | Owner/Admin | Lifecycle management |
| `PATCH /tenants/{tenantId}/members/{id}/role` | Owner/Admin | Role changes |
| `POST .../suspend`, `POST .../reactivate` | Owner/Admin | Status management |

Legacy `POST /tenants/{tenantId}/invitations/accept` remains for existing flows
where an `Invited` Membership already exists and the user is authenticated.

### 4. RLS for token paths

Narrow policies scoped to session GUC `app.invitation_token_hash`:

- **SELECT** on `tenant_invitations` and `invitation_workspace_grants` for preview
- **UPDATE** on `tenant_invitations` to mark `used_at_utc` on acceptance

Token lookup does **not** disable RLS globally and does not grant arbitrary tenant
data access. Preview fields are denormalized snapshots populated at invite time.

Cross-table preview RLS policies were attempted and **rejected** because they
caused policy recursion on unrelated login queries.

### 5. Identity binding

Acceptance compares authenticated User email (or registration email) to
`tenant_invitations.invited_email`. Mismatch returns `403 invitation_email_mismatch`.

### 6. Email delivery — deferred

No transactional email provider is integrated. `EmailDeliveryDeferred: true` is
returned on invite/resend; Development exposes the invitation URL to authorized
inviters. Production must not depend on manual copy forever — adapter deferred.

## Consequences

### Positive

- Production-grade invitation-link onboarding without JWT role claims
- Clear separation: User ≠ Membership; token ≠ authentication
- Members admin surface can list, filter, invite, manage roles/access, and handle pending invitations

### Negative / follow-ups

- Email delivery adapter still required for production
- Denormalized preview snapshots can become stale if organization/workspace names change before acceptance (acceptable for invite UX)
- Permanent member removal deferred; suspend is supported

## References

- `.cursor/rules/10-multi-tenancy-and-rls.mdc`
- `.cursor/rules/20-identity-membership-platform-admin.mdc`
- Migration chain through `20260901120000_InvitationTokenAcceptRls`
