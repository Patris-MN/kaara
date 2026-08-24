# ADR-0004: Ports-and-Adapters for Persistence, Composed Only in the Host

Status: Accepted
Date: 2026-08-23

## Context

Phase 2 introduces real persistence (EF Core + PostgreSQL) for the Identity and
Tenancy modules. The modular-monolith rule (`00-modular-monolith-architecture.mdc`)
already says a module may only reference `PTS.SharedKernel`, never another
module directly, and that `PTS.Host` is the only project allowed to reference
every module. Persistence raises three concrete questions that rule doesn't
answer by itself:

1. Where does the `DbContext` live, given it must aggregate entities from
   multiple modules (Identity's `User`, Tenancy's `Tenant`/`Membership`) into
   one PostgreSQL connection/transaction?
2. Where do cross-module foreign keys get declared (e.g. `Membership.UserId`
   → `users.id`), given `PTS.Modules.Tenancy` must never reference
   `PTS.Modules.Identity`?
3. How does `PTS.Modules.Tenancy`'s `TenantContextResolver` check an active
   `Membership` without the Tenancy module taking a hard dependency on EF Core
   *and* a concrete database provider?

## Decision

- **The single composed `DbContext` (`AppDbContext`) lives in `PTS.Host`**, not
  in any module. It references every module's entity types and applies every
  module's `IEntityTypeConfiguration<T>`, but modules never reference it back.
- **Modules own their own entity shape and configuration** — `User`,
  `Tenant`, `Membership`, and their `IEntityTypeConfiguration<T>` classes live
  in their respective modules. Modules may reference the provider-agnostic
  `Microsoft.EntityFrameworkCore` / `Microsoft.EntityFrameworkCore.Relational`
  packages for this (mapping, constraints, indexes) — these are ORM
  abstractions, not a concrete database. **Modules must never reference a
  concrete database provider** (`Npgsql`, `Npgsql.EntityFrameworkCore.PostgreSQL`,
  etc.) — only `PTS.Host` may. This is enforced mechanically by
  `PTS.Architecture.Tests.ModuleBoundaryTests.Module_must_not_reference_a_concrete_database_provider`.
- **Cross-module foreign keys are wired in `AppDbContext.OnModelCreating`
  (Host), not in either module.** E.g. `Membership.UserId → users.id` is
  declared once, in the Host, where both `User` and `Membership` are already
  in scope. Neither module's configuration class references the other
  module's entity type.
- **A module that needs to ask a persistence question defines a narrow port
  (interface) in the module itself; the Host provides the adapter.** Tenancy
  defines `IMembershipLookup` (one method:
  `FindActiveMembershipAsync(userId, tenantId)`); the Host's `EfMembershipLookup`
  implements it using `AppDbContext`. `TenantContextResolver` (Tenancy) depends
  only on `IMembershipLookup`, never on EF Core or `AppDbContext` — Tenancy's
  own project reference to `Microsoft.EntityFrameworkCore` exists solely for
  `Membership`'s own `IEntityTypeConfiguration<Membership>`, not for querying.
- **The bridge from application `TenantContext` to PostgreSQL RLS
  (`TenantRlsSessionFactory`/`TenantRlsSession`) also lives in the Host**, since
  it needs both a concrete `AppDbContext`/connection and the Tenancy module's
  `ITenantContextResolver`/`ITenantContextEstablisher` — it is composition, not
  business logic belonging to any one module.

## Alternatives considered

- **A generic repository abstraction in `PTS.SharedKernel`** (e.g.
  `IRepository<T>`) — rejected per the existing rule against adding a generic
  repository layer speculatively (`00-modular-monolith-architecture.mdc`).
  `IMembershipLookup` is deliberately a narrow, use-case-specific port, not a
  generic data-access abstraction.
- **Each module owns its own `DbContext`/connection** — rejected. This is a
  single deployable application backed by one PostgreSQL database; per-module
  `DbContext`s would either need to share one connection/transaction anyway
  (defeating the point of separating them) or risk multiple uncoordinated
  transactions against the same tables, especially dangerous for `SET LOCAL`
  tenant-context correctness.
- **Modules reference `Npgsql` directly for provider-specific features** —
  rejected. It would make every module hard-depend on "we run on PostgreSQL",
  which the Tenancy rule's "enterprise isolation is a Tenancy-module
  implementation detail" goal (`10-multi-tenancy-and-rls.mdc`) explicitly
  wants to avoid leaking into other modules.

## Consequences

- Adding a new tenant-owned entity in a future module means: the module
  defines the entity + `IEntityTypeConfiguration<T>` (provider-agnostic), and
  `PTS.Host`'s `AppDbContext` registers it, wires any cross-module FK, and
  (via a migration) grants `app_role` and creates its RLS policy. Two-sided by
  design — this is deliberately not "automatic," so every new RLS-protected
  table is a reviewable, explicit diff.
- A module that needs to *read* something owned by another module's tables
  (beyond what `PTS.SharedKernel` contracts already expose) must define its
  own narrow port and get an adapter from the Host — never a direct EF Core
  query against another module's entity type from inside a module.
- `PTS.Architecture.Tests` mechanically enforces both the "no other module"
  rule and the new "no concrete database provider" rule for every module
  assembly, so a violation fails the build, not just code review.
