# Platform Administration Module

## Bounded Context Responsibility

Owns permissions and tooling for **internal platform operators/staff** — people who
manage the SaaS itself, not a specific tenant.

A platform administrator is recorded in `platform_administrators` (keyed by
global `UserId`). It is **not** a column on `User` and **not** a tenant
`Membership` role.

In scope:

- Granting/looking up the platform-administrator flag for a global user.
- Future controlled, audited cross-tenant visibility for support/ops.

Explicitly **out of scope**:

- Tenant-level roles/permissions (Tenancy module).

## Allowed dependencies

- `PTS.SharedKernel` only (plus provider-agnostic EF Core mapping packages).

## Local bootstrap

In Development, the Host can create and grant a platform administrator from
environment variables (see `infra/docker/.env.example`). Those values must
never be committed.
