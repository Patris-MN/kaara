# PTS.SharedKernel

## Purpose

Holds only the small set of abstractions/contracts that every module (and the Host)
are allowed to depend on, so modules can be composed without depending on each
other. This is the *only* project any module may reference besides its own code.

## What belongs here (future phases)

- Cross-module contracts (interfaces), e.g. a future `ITenantContext` abstraction
  that Tenancy implements and other modules consume to read the current tenant
  without depending on the Tenancy module's internals.
- Small, framework-light value objects/marker interfaces shared by convention across
  modules (e.g. a future `ITenantOwned` marker interface expressing "this entity
  must carry exactly one `TenantId`").

## What does NOT belong here

- Business logic of any kind.
- EF Core `DbContext`s, entities with behavior, or persistence concerns.
- Anything specific to one module's internal implementation.

## Dependency rule

`PTS.SharedKernel` must never reference any module project (`PTS.Modules.*`) or the
`PTS.Host` project. This is enforced by `PTS.Architecture.Tests`.

## Phase 1 status

Intentionally empty besides an assembly marker. No shared contracts are defined yet
because no module has an implemented need for one — adding abstractions ahead of a
real consumer is avoided per the project's architectural rules (see
`docs/architecture/architecture-charter.md`).
