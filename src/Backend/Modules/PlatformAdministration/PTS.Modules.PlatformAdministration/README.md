# Platform Administration Module

## Bounded Context Responsibility

Owns permissions and tooling for **internal platform operators/staff** — people who
manage the SaaS itself, not a specific tenant.

In scope (future phases):

- Platform-administrator identity/permission model, held completely separate from
  tenant `Membership` roles (a platform admin is not automatically a member of any
  tenant, and a tenant admin is not automatically a platform admin).
- Any controlled, audited cross-tenant visibility needed for support/ops. Cross-tenant
  reads for support purposes must go through explicit, audited code paths in this
  module — never through ordinary tenant-scoped queries or by disabling RLS ad hoc.

Explicitly **out of scope**:

- Tenant-level roles/permissions (Tenancy module).

## Allowed dependencies

- `PTS.SharedKernel` only.

## Phase 1 status

Architectural placeholder only. No platform-admin identity, permissions, or tooling
is implemented in this phase.
