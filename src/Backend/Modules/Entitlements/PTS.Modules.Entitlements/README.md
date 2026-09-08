# Entitlements Module

## Bounded Context Responsibility

Answers "what is this tenant allowed to do/use right now?" — plan limits, feature
flags, and seat counts derived from subscription state.

In scope (future phases):

- Resolving a tenant's active entitlements (features, quotas, limits).
- Fast, frequently-called checks consumed by Work Management and other modules
  before allowing an action (e.g. "can this tenant create another project?").

Explicitly **out of scope**:

- Payment processing, invoicing, or any direct integration with a payment provider
  (Stripe, etc.) — that belongs to the Billing module. Entitlements *reacts to*
  subscription state; it does not manage billing itself.

## Allowed dependencies

- `PTS.SharedKernel` only.
- Must **not** reference `PTS.Modules.Billing` directly; the two communicate
  through contracts/composition in later phases, not direct coupling.

## Phase 1 status

Architectural placeholder only. No plans, entitlement resolution, or tenant-scoped
checks are implemented in this module yet.

**Phase 8.1.3 — organization creation seam (global account scope):**

- Contract lives in `PTS.SharedKernel`: `IOrganizationCreationEntitlementProvider`
  and `OrganizationCreationEntitlement`.
- **Pre-Billing development policy** is composed in `PTS.Host` via
  `DevelopmentOrganizationCreationEntitlementProvider` (not in this module).
- Future Billing/Entitlements work will supply the production provider without
  direct `PTS.Modules.Billing` ↔ `PTS.Modules.Entitlements` references.

See [ADR 0012](../../../docs/architecture/decisions/0012-global-account-authorization.md).
