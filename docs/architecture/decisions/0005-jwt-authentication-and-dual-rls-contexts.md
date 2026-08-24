# ADR-0005: JWT authentication and dual RLS contexts

Status: Accepted
Date: 2026-08-24

## Context

Phase 2 proved tenant isolation via PostgreSQL RLS using a transaction-local
`app.current_tenant_id`, but two gaps remained:

1. Production code accepted a raw `userId` argument as if it were identity.
2. `users`, `tenants`, and `memberships` had no RLS, so any `app_role` query
   could read every identity and membership row.

Membership lookup is required *before* a tenant context exists, so tenant-scoped
RLS cannot protect `memberships`.

## Decision

- Authenticate with ASP.NET Core JWT Bearer (HMAC-SHA256). Signature, issuer,
  audience, and lifetime are validated. Identity is the `NameIdentifier` /
  `sub` claim (the global `User.Id`). Tenant roles are **not** placed in the
  token; membership is always re-read from the database.
- Store password hashes with `PasswordHasher<User>` (PBKDF2). Never store
  plaintext. Login looks up `user_credentials` (email + hash) **without** RLS
  so authentication can run before `app.current_user_id` exists. Profile rows
  in `users` remain RLS-protected.
- Introduce `ICurrentUser` in SharedKernel. Host maps `ClaimsPrincipal` →
  `UserId`. `ITenantRlsSessionFactory.OpenAsync` takes only the requested
  tenant id.
- Two transaction-local GUCs, both `SET LOCAL` (`is_local = true`):
  - `app.current_user_id` — set first (bootstrap)
  - `app.current_tenant_id` — set only after an active membership is proven
- RLS:
  - `memberships`: `user_id = current_user_id` (no tenant GUC)
  - `users`: `id = current_user_id`
  - `tenants`: visible iff an **Active** membership exists for current_user_id
  - existing tenant-owned table policy unchanged

## Alternatives considered

- Cookie/session auth — rejected for this API-first phase; JWT in the
  Authorization header avoids CSRF on a bearer API. Cookie auth can be added
  later for a browser UI.
- OAuth/social login — not required yet; would add infrastructure without a
  product need.
- Memberships RLS on `tenant_id = current_tenant_id` — rejected; circular with
  membership bootstrap.
- Putting tenant ids in JWT claims — rejected; memberships change independently
  of token lifetime.

## Consequences

- Host must configure a 256-bit signing key (`Authentication:Jwt:SigningKey` or
  `PTS_JWT_SIGNING_KEY`).
- Login can read credential rows for any email (needed to authenticate); hashes
  are not reversible. User profiles remain non-enumerable via `users` RLS.
- `user_credentials` must stay off tenant RLS; do not add a tenant id to it.
