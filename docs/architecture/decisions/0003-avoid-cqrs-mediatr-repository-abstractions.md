# ADR-0003: Avoid CQRS, MediatR, and a Generic Repository Abstraction (For Now)

Status: Accepted
Date: 2026-08-23

## Context

These patterns are common in ASP.NET Core codebases and are sometimes added by
default/convention rather than in response to an actual requirement. At this
stage there are no business features implemented at all, so there is no concrete
read/write model divergence, no in-process messaging fan-out need, and no
data-access duplication across modules that a repository abstraction would
solve.

## Decision

Do not add MediatR, a CQRS split, or a generic repository abstraction in Phase 1
or by default in future phases. Use EF Core `DbContext`s directly within a
module's own code, and plain method calls / direct service composition instead of
an in-process mediator.

## Alternatives considered

- **MediatR for all "commands/queries"** — rejected as a default. It adds an
  indirection layer (locating handlers by convention) that makes code harder to
  navigate for no benefit when a direct method call would do, and this project's
  module boundaries already provide the separation MediatR is sometimes used to
  fake within a single project.
- **CQRS (separate read/write models)** — rejected as a default. Valuable when
  read and write models genuinely diverge under load or complexity; premature
  before a single business feature exists.
- **Generic repository interface (`IRepository<T>`) over EF Core** — rejected as
  a default. EF Core's `DbContext`/`DbSet` already is a reasonably good
  abstraction over the database; wrapping it in another generic abstraction
  typically only hides EF Core's capabilities (change tracking, `Include`,
  compiled queries) without adding real testability or flexibility benefit for
  this project's needs.

## Consequences

- Module code will call EF Core directly (once each module has its own
  persistence needs) rather than through a repository interface.
- If a genuine requirement emerges later — e.g. a specific read path needs a
  denormalized/cached projection that diverges meaningfully from the write model,
  or in-process fan-out to multiple handlers becomes necessary — that requirement
  must be documented in a new ADR before the pattern/dependency is introduced,
  per `.cursor/rules/00-modular-monolith-architecture.mdc`.
