# ADR-0001: Modular Monolith over Microservices

Status: Accepted
Date: 2026-08-23

## Context

The system is a B2B multi-tenant SaaS at the very beginning of its life, with a
small initial set of bounded contexts (Identity, Tenancy, WorkManagement,
Entitlements, Billing, Storage, PlatformAdministration, Audit). There is no
existing production traffic, no team-scaling pressure requiring independent
deployability, and no bounded context with meaningfully different scaling
characteristics from the others yet.

## Decision

Build a single deployable ASP.NET Core application (a modular monolith), with
bounded contexts enforced as internally-separated modules with a strict
one-directional dependency rule (module → SharedKernel; Host → every module).
Microservices are not introduced.

## Alternatives considered

- **Microservices per bounded context** — rejected for Phase 1. Would add
  network boundaries, distributed transaction/consistency concerns, deployment
  and observability overhead, and independent-versioning complexity with no
  current requirement that justifies it (no independent scaling need, no
  independent team ownership yet). This can be revisited per-module later
  (e.g. Storage or Billing peeling off) if a concrete requirement — such as an
  independent scaling need or a genuine team boundary — emerges; that would be
  its own future ADR.
- **Unstructured single project ("big ball of mud")** — rejected because it
  provides no enforced boundary between bounded contexts, making the eventual
  extraction of a module (if ever needed) far more expensive, and making it easy
  to accidentally couple tenant, billing, and business-domain concerns.

## Consequences

- Simpler deployment, debugging, and local development (one process, one
  solution, one `dotnet run`).
- Module boundaries are enforced at compile/test time
  (`tests/Backend/PTS.Architecture.Tests`) rather than by network isolation, so
  discipline is required to keep them meaningful.
- If a module later needs independent scaling or deployment, its clean internal
  boundary (no cross-module project references) makes extraction tractable —
  but that extraction is deliberately not pre-built now.
