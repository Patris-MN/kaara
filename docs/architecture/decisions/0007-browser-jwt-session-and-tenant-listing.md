# ADR-0007: Browser JWT session storage and tenant listing

Status: Accepted
Date: 2026-08-24

## Context

Phase 4.5 connects the React UI to JWT Bearer APIs. The API does not issue
HttpOnly cookies. The UI needs a way to keep the access token across a page
refresh, list tenants the signed-in user can actually use, and show pending
invitations (including tenant name) without treating the browser as a
security boundary.

## Decision

- Store the access token in `sessionStorage` for this development phase.
  Refresh restores the session; closing the tab ends it. The token is never
  logged, never rendered, and never written to `localStorage`.
- On startup, call `GET /auth/me`. A stored token is not treated as valid
  until the backend accepts it.
- Add `GET /tenants` (Active memberships only) and `GET /invitations`
  (Invited memberships only). Both derive UserId from `ICurrentUser` and
  run under memberships/tenants RLS.
- Add a tenants SELECT policy for Invited memberships so invitees can read
  tenant name/slug. Workspace and project RLS still require an Active
  membership plus `app.current_tenant_id`.

## Alternatives considered

- In-memory token only — rejected for this phase; refresh would always log
  the user out and block the required restore flow.
- `localStorage` — rejected; the token would survive the browser session
  with a larger XSS window and no extra product benefit yet.
- HttpOnly cookie session — deferred. ADR-0005 chose Bearer tokens; switching
  the API to cookies is a later auth-hardening change, not required to prove
  the vertical slice.
- Extending `tenants_select` to every membership status — rejected.
  Suspended users must not see tenant rows. Invited visibility is a separate
  SELECT policy.

## Consequences

- XSS in the SPA can still read `sessionStorage`. Production should move to
  HttpOnly cookies (or BFF) before treating this as a hardened browser
  session. That remains technical debt.
- Authorization stays on the server. Persisted tenant IDs are UI hints only.
- Invited users can read tenant metadata; they still cannot open a tenant
  RLS session or mutate WorkManagement data until they accept.
