# Audit & Logging Module

## Bounded Context Responsibility

Owns the durable audit trail: who did what, when, and in which tenant context.

In scope (future phases):

- Audit event capture for security-sensitive and business-sensitive actions.
- Every tenant-related audit record carries `TenantId`; platform-level actions
  (e.g. a platform admin's cross-tenant support access) are recorded with their own
  distinct audit trail, not folded into a tenant's own history.
- Read APIs for audit history, scoped the same way all tenant data is scoped.

Explicitly **out of scope**:

- Application logging/observability infrastructure (metrics, traces) — this module
  is specifically about the durable, queryable *audit* record, not general logs.

## Allowed dependencies

- `PTS.SharedKernel` only.

## Phase 1 status

Architectural placeholder only. No audit schema, storage, or capture pipeline is
implemented in this phase.
