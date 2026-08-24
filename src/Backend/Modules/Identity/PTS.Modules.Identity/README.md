# Identity Module

## Bounded Context Responsibility

Owns the **global user identity** — a person's account across the entire platform,
independent of any tenant.

In scope (future phases):

- Global `User` record: credentials, authentication, MFA, email, profile.
- Login/session/token issuance.
- User-level preferences intended to be reusable across tenants (e.g. preferred UI
  language — see `docs/architecture/architecture-charter.md`).

Explicitly **out of scope** (belongs to the Tenancy module instead):

- Tenant membership.
- Tenant-level roles/permissions.
- Any concept of "which organization is this user acting in right now".

## Why the split from Tenancy matters

A `User` can belong to zero, one, or many tenants. Mixing "who is this person" with
"what can they do in tenant X" makes it impossible to reason about cross-tenant
security. This module must never expose a `TenantId` on its core entities.

## Allowed dependencies

- `PTS.SharedKernel` only.
- Must **not** reference `PTS.Modules.Tenancy`, `PTS.Modules.PlatformAdministration`,
  or any other module directly. Cross-module interaction, when needed, goes through
  contracts in `PTS.SharedKernel` or composition in `PTS.Host`.

## Phase 1 status

Architectural placeholder only. No authentication, registration, or storage is
implemented in this phase.
