# Billing Module

## Bounded Context Responsibility

Owns organization-level subscriptions and all payment-provider integration.

In scope (future phases):

- Subscription lifecycle (trial, active, past-due, cancelled).
- Payment provider integration (e.g. Stripe) — webhooks, invoices, payment methods.
- Emitting subscription-state changes that the Entitlements module reacts to.

Explicitly **out of scope**:

- Feature-flag/limit evaluation (Entitlements module). Billing knows *what plan a
  tenant is on*; it does not decide *what that unlocks* — that's a separate concern
  so payment-provider details never leak into feature-check code paths.

## Allowed dependencies

- `PTS.SharedKernel` only.
- Must **not** reference `PTS.Modules.Entitlements` or any other module directly.

## Phase 1 status

Architectural placeholder only. No subscriptions, payment provider integration, or
Stripe code is implemented in this phase (explicitly excluded from Phase 1 scope).
