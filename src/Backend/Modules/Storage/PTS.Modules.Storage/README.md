# Storage Module

## Bounded Context Responsibility

Owns file/blob storage as a provider-agnostic abstraction used by other modules.

In scope (future phases):

- A storage interface (e.g. `IObjectStorage`) independent of the underlying
  provider (local disk in dev, S3/Azure Blob/etc. in production).
- **Tenant-aware object keys/prefixes are mandatory.** Every object key must be
  namespaced by `TenantId` (e.g. `tenants/{tenantId}/...`) so that a bug in calling
  code cannot accidentally read or overwrite another tenant's file by guessable key
  alone.

Explicitly **out of scope**:

- Business rules about *what* gets uploaded/attached to (Work Management module).
- Any file upload endpoint (explicitly excluded from Phase 1 scope entirely).

## Allowed dependencies

- `PTS.SharedKernel` only.

## Phase 1 status

Architectural placeholder only. No storage provider or upload capability is
implemented in this phase.
