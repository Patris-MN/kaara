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

Architectural placeholder only. No plans, entitlement resolution, or checks are
implemented in this phase.
