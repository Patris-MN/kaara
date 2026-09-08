# PTS — Task and Project Management SaaS

A production-grade, B2B, **multi-tenant** Task and Project Management platform,
built as a **Modular Monolith**.

> **Current phase: Phase 8.3P-A — Signup, Profile Recovery & Account UX
> (implementation complete; restart PTS.Host and run browser QA).**
> Phase 8.3 remaining task-discovery work is **paused** until 8.3P is reviewed.
> Phase 8.4 has **not** started.

## Current Development Baseline

**Phase 7 — Application Shell, Organization UX, and Product Design Foundation**
is implemented as a frontend/product UX slice. **Phase 7.1** hardened dialog
focus, identifiers, and lint. **Phase 7.2** delivered Organization Grid/List,
workspace counts, invitation relocation, and editing. **Phase 7.3** hardens
application density, the shared content rail, and a reusable resource toolbar.
**Phase 7.4** adds a reusable action-feedback/toast primitive and integrates
Organization Create/Edit with polished loading, success, and failure feedback.
**Phase 7.4.1** improves toast contrast and readability.
**Phase 8.0** redesigns the Workspace page as a list-first resource browser
with Create/Edit modals, Description/StartDate metadata, Grid/List, search,
sort (default Recently updated), and removes inline member administration.
**Phase 8.0.1** fixes a post-8.0 regression where pre-existing workspaces failed
to load (null `updated_at_utc` under FORCE RLS) and separates Workspace list
**error** vs **empty** UI states with Retry.
**Phase 8.0.2** polishes Workspace empty-state CTA spacing and replaces
action-like **Edit access** / **View access** pills with non-interactive status
badges (**Full access**, **Can edit**, **View only**) mapped from server
membership role + workspace `accessLevel`.
**Phase 8.0.3** delivers a **shell-global notification inbox**: the bell works
without a selected organization, aggregates unread across active memberships,
shows organization/type/resource/timestamp context in the dropdown, marks read
on open, and navigates cross-tenant with tenant-switch isolation. Realtime push
(SignalR/WebSockets/SSE) remains deferred.
Phase 6 Task Management remains in place and was not rebuilt.

**Phase 8.1.3C** fixes auth/invitation form input contrast (`--auth-text`, `color-scheme:
light` on auth pages). **Phase 8.1.5** delivers mature **WorkspaceAccess** administration UX from both
**Member → Workspaces** (Manage access dialog) and **Workspace → Members & access**
(reused `WorkspaceMemberAccessPanel` on its own tab). **Phase 8.2** separates
workspace **Projects** from **Members & access**, replaces the permanent create
form with **+ New project** modal, compact create UX (name, optional description,
optional accent color, identity preview), Grid/List project directory with search,
and aggregated open-task counts. **Phase 8.2A** removes user-facing **Project Key**
and KEY-n task references; projects use UUID identity plus display initials/color only.
**Phase 8.2B** compacts the Projects page layout (integrated project count, single
toolbar row) and adds **Owner/Admin-only** project metadata editing (name,
description, accent color) via card/list action menus and a shared edit modal.
**Phase 8.2C** adds icon + label Grid/List toggles on the Projects toolbar.
**Phase 8.3** delivers compact **Task list discovery** (search, filters, sort,
quick scopes), scan-friendly task rows, compact **Create task** modal, redesigned
**Task details** (property grid, comments/activity disclosure, save feedback),
and **creator-only delete before external engagement** with backend enforcement.

**Phase 8.3P** delivers registration success feedback, live password requirements +
confirm password (aligned with backend **8-character minimum**), Google sign-in UI
foundation (disabled when unconfigured), global **Profile** (`/app/profile`) with
display-name edit and secure password change, modal backdrop no-close + dirty-form
discard confirmation, human-readable task activity (tag names not UUIDs), delete-blocked
informational callout, and quick scope **Active** (renamed from ambiguous **Open**).

This is the real repository state after Phase 8.3P (2026-09-07):

| Layer | What exists |
|---|---|
| Identity | Global `User`, register/login, JWT identity-only tokens; **`GET/PATCH /account/profile`**, **`POST /account/change-password`**; **`GET /auth/providers`** (Google availability); email change **deferred** (no verification infra); avatar upload **deferred** |
| Tenancy | Members admin API, secure invitation tokens, role change dialog, suspend/reactivate/remove, pending invitation list, **batch workspace-access replace**, **workspace-scoped member-access list/grant** |
| Global account authz | **`CanCreateOrganization`** via `IOrganizationCreationEntitlementProvider` + `GET /account/capabilities`; **not** inferred from tenant Role |
| Resource authz | **Organization role ≠ Workspace access** — Owner/Admin implicit all-workspace access; Member explicit `WorkspaceAccess` View/Edit; **projects inherit workspace access — NO Project ACL** |
| Project identity | UUID (internal) + mutable name; optional description; optional curated **accent token** (UX-only); display initials derived from name |
| Task identity | UUID (internal) only; human-readable task references **deferred** |
| Task authz | **Workspace Edit is prerequisite for every task mutation**; View-only members read tasks; **creator may delete only before another member views/interacts** |
| Task discovery | Client-side search/filter/sort/quick scopes on project task list; compact toolbar; filter chips |
| WorkManagement | Project create/list/update metadata; **task external-engagement flag** for delete eligibility; `POST …/tasks/{id}/seen` view receipt |
| Frontend | Compact project tasks page; task detail property grid; comments/activity disclosure; context-menu delete + confirmation; EN/AR/KU + RTL |
| Database | Migrations through **`20260906100000_RemoveProjectKeyAddAccentToken`** + **`20260907100000_TaskExternalEngagement`** (`tasks.has_external_engagement`, applied locally) |

**Authorization model (Phase 8.2B baseline):**

- **Global account entitlement** — `CanCreateOrganization` (pre-Billing development policy in Host).
- **Organization role** — Owner / Admin / Member system roles inside a Membership; defines organization-level authority (invite, role change, workspace-access management for Owner/Admin).
- **Resource access** — Workspace No access / View / Edit for **Active Members**; one Member may hold **many** `WorkspaceAccess` grants. Owner/Admin implicit all-workspace access (no explicit rows required for UI).
- **Resource access level** — View or Edit within a workspace; **projects inherit workspace access** (no project-specific membership or ACL).
- **Project metadata edit** — **Owner/Admin only** (`CanManageProjectMetadata`); Member + Workspace Edit may **create** projects but **cannot** edit project metadata.
- **Task mutation** — `CanMutateTaskAction = HasEffectiveWorkspaceEdit AND TaskSpecificCapabilityForAction`; View-only members are read-only on tasks.
- **Task delete (Phase 8.3)** — only **creator** may delete; blocked once any **non-creator** has **viewed** (`POST /seen`) or **interacted** (comment, status/tag/assignment change, etc.); `tasks.has_external_engagement` denormalized flag; `409 task_already_seen_cannot_delete`.
- **Account-level People Directory** — **deferred**.
- Full **Roles & Permissions** management UI remains **deferred**.

**Creation matrix (system roles):**

| Action | Owner | Admin | Member |
|---|---|---|---|
| Create Organization | Global entitlement | Global entitlement | Global entitlement |
| Create Workspace | Yes | Yes | No |
| Create Project | Yes | Yes | Workspace Edit only |
| Edit project metadata (name/description/color) | Yes | Yes | No |
| Manage workspace member access | Yes | Yes | No |

**Verification (this session — 7 Sep 2026):**

| Suite | Result |
|---|---|
| `dotnet build PTS.slnx` | **Pass** (0 warnings on Host build) |
| `dotnet test` — Architecture | **27/27** pass |
| `dotnet test PTS.slnx` — Integration | **136/136** pass after applying `20260907100000_TaskExternalEngagement` (stop running `PTS.Host` before rebuild if file-locked) |
| `npm run test` (Web) | **243/243** pass (**45** test files; 9 flaky under full parallel load — phase83a recovery tests pass in isolation) |
| `npm run lint` | Pass (7 oxlint warnings incl. set-state-in-effect) |
| `npm run build` | **Pass** (chunk-size notice) |
| Live browser QA (Phase 8.3 scenarios 1–16) | **Not run in this session** |

### Phase 8.3 — Task list, discovery & task detail UX

- **Task page density:** Removed oversized grid/min-height gaps; compact header + quick scopes (All / My tasks / Open / Overdue) + toolbar (Search | Filters | Sort | + New task).
- **Discovery:** Client-side search (title/description/tags), filter dialog (status, priority, assignee, deadline, tags), sort dropdown, active filter chips, distinct empty / no-match states.
- **Task rows:** Whole-row click; priority badge + status + assignee + due date + tags + unread indicator.
- **Create modal:** Compact layout; optional description disclosure; clear creating/success/failure feedback.
- **Task details:** Title-first header; property grid; read-only static rendering for View-only; comments/activity disclosure with icons; save disabled when unchanged; delete via ⋯ menu + confirmation.
- **Delete policy:** Creator-only; allowed only before external view/interaction; backend `canDelete` + `deleteBlockedReason`; race-safe delete vs view.
- **View tracking:** Idempotent `POST /tenants/…/tasks/{taskId}/seen`; reuses `WorkTaskReadState`; engagement via `HasExternalEngagement`.
- **Migration:** `20260907100000_TaskExternalEngagement` — **`has_external_engagement`** on `tasks` (FORCE RLS unchanged on existing tables).
- **Tests:** `TaskDeleteEligibilityHttpTests`, `phase83.test.tsx`, `taskResources.test.ts`; updated phase 6 ticket/collaboration tests.
- **Deferred:** Kanban, subtasks, dependencies, attachments, saved views, global search, Phase 8.4.

### Phase 8.3A — Task load failure recovery

**Incident:** After Phase 8.3 added `WorkTask.HasExternalEngagement`, the Tasks page showed “The request could not be completed.” alongside a false “No tasks yet” empty state.

**Root cause:** Migration `20260907100000_TaskExternalEngagement` existed in code but was not registered/applied (missing EF `[Migration]` attribute → PostgreSQL error `42703: column "has_external_engagement" of relation "tasks" does not exist` on `GET …/tasks`). Frontend treated load failure like an empty task list.

**Repair:**
- Fixed migration registration; applied to local `pts` database.
- Task list GET remains read-only (`markSelectedSeen: false` on all list/detail GET paths); view receipts only via `POST …/tasks/{id}/seen`.
- Task GET detail no longer calls `SaveChangesAsync` without a mutation.
- Frontend: mutually exclusive load error vs empty states; **Tasks couldn't load** + **Try again** retry.
- Tests: `TaskLoadRecoveryHttpTests`, `phase83a.test.tsx`.

### Phase 8.2C — Grid/List view switch icon polish

- **Projects toolbar:** Grid/List segmented control now shows inline SVG icons + short labels (same `ViewToggleIcon` pattern as Organizations/Workspaces).
- **Accessibility:** Visible labels remain `Grid` / `List`; `aria-label` uses `Grid view` / `List view` (EN/AR/KU).
- **Unchanged:** Toolbar layout, search/sort/create, grid/list behavior, authorization, backend.

### Phase 8.2B — Project page density & metadata editing

- **Page density:** Removed duplicate project-count row and `ResourceToolbar` two-column grid; count lives in page header description (`… · N projects`); compact toolbar (`Search | Sort | Grid/List | + New project`) sits directly under tabs.
- **Edit project:** Owner/Admin ⋯ menu on grid cards and list rows opens shared edit modal (name, description, accent color, initials preview); Member + Workspace Edit retains create but **no** edit action.
- **API:** `PATCH /tenants/{tenantId}/workspaces/{workspaceId}/projects/{projectId}` with explicit `UpdateProjectRequest` DTO; `project_metadata_edit_forbidden` (403) for non-Owner/Admin.
- **Tests:** `ProjectMetadataUpdateHttpTests`, `phase82b.test.tsx`, `projectResources.test.ts`.
- **Deferred:** Project ACL, Phase 8.3.

### Phase 8.2A — Project creation & empty-state UX refinement

- **Product change:** User-facing **Project Key removed**; creation requires **Project name** only (+ optional description, optional accent color).
- **Create modal:** Compact dialog — name, short description, curated color swatches, initials preview, workspace-access info (no member picker).
- **Empty states:** Creator onboarding, view-only explanation, search-no-results with **Clear search** (distinct copy).
- **Grid/List:** Identity badge (initials + accent); no Key column; search by name/description only.
- **Schema:** Forward migration drops `key`/`sequence_number` columns; adds nullable `accent_token` (curated tokens validated server-side).
- **Deferred:** KEY-n / `#184`-style human task IDs, project image upload, Project ACL, Phase 8.3.

### Phase 8.2 — Project resource UX foundation (superseded by 8.2A for project key/references)

- **Information architecture:** Workspace page tabs — **Projects** (directory) and **Members & access** (Phase 8.1.5 panel). No inline members list under project cards.
- **Projects tab:** Search, sort, Grid/List (`localStorage` key `pts.projectView.{userId}`), aggregated open-task counts, **+ New project** modal.
- **Project status filter:** Deferred — domain has no archive/status field; UI shows **Active** only.
- **Explicitly not in 8.2:** Project ACL, Kanban, Gantt, templates, Global Search, Phase 8.3.

See [ADR 0012](docs/architecture/decisions/0012-global-account-authorization.md) and [ADR 0011](docs/architecture/decisions/0011-secure-invitation-link-onboarding.md).

### Phase 8.1.3C — Auth / invitation form input contrast fix

- **Root cause:** Global `input { color: inherit; background: var(--bg) }` combined with `prefers-color-scheme: dark` in `index.css` made typed text dark-on-dark when auth inputs missed the narrow `.login-form input[type=…]` selector (Display name had no explicit `type`).
- **Fix:** Auth pages set `color-scheme: light` and `--auth-text`; `.login-form .field input` covers all field types including implicit text inputs; read-only, disabled, selection, caret, and `-webkit-autofill` styling added.
- **Tests:** `phase813c.test.tsx` (registration + invitation display name typing, read-only invited email, EN/AR/KU).

### Phase 8.1.3B — Task authorization view-access regression

- **Root cause (both layers):** Backend `TaskAuthorizationService` and task capability DTOs treated workspace view as sufficient for collaboration mutations; frontend `resolveTaskCapabilities` ignored workspace `accessLevel` and rendered editable controls from permissive server caps.
- **Backend:** `HasWorkspaceEdit` gate on every task mutation; `TaskCollaboration.DescribeSubject` resolves view+edit from workspace access; all task endpoints use it; View-only direct API mutations return **403**.
- **Frontend:** View workspace → conservative deny-all capabilities; task modal switches to static read-only `<dl>` fields; comment edit/delete hidden when `!canComment`; missing capabilities fail closed.
- **Tests:** `TaskViewAccessHttpTests`, updated `TaskAuthorizationServiceTests` / `TicketCollaborationHttpTests`, `phase813b.test.tsx`, `resolveTaskCapabilities.test.ts`.
- **Security:** RLS / tenant isolation unchanged; suspended/removed memberships cannot mutate tasks.

### Phase 8.1.3A — Public invitation route & onboarding repair

- **Root cause:** Vite dev proxy forwarded browser navigations on `/invite/*` to the backend preview API, returning raw JSON instead of loading the SPA.
- **Fix:** Proxy `bypass` serves `index.html` for document navigations; XHR/fetch preview/accept/register calls still proxy to `PTS.Host`.
- **Frontend:** `InviteAcceptPage` shows organization/inviter/email/role/workspace grants/expiration; locked invited email on registration; wrong-account message; login redirect preserves `/invite/:token`.
- **Development UX:** Full invitation URLs built via `VITE_APP_ORIGIN` / `window.location.origin`; **Copy invitation link** in Members invite dialog when email delivery is deferred.
- **Security:** No backend invitation security changes (token hash, expiry, revoke, email binding, RLS unchanged).

### Phase 8.1.3 — Global account onboarding & resource creation authorization

- **`IOrganizationCreationEntitlementProvider`** (SharedKernel) + Host development policy.
- **`GET /account/capabilities`** — frontend consumes same capability snapshot as backend enforcement.
- **`POST /tenants`** returns **403** `organization_create_forbidden` when disallowed.
- **Fresh independent signup** — valid zero-membership state with welcome onboarding + create first organization.
- **Interrupted invite recovery** — pending invitations surfaced; **Continue invitation** uses secure accept endpoint (no token hash exposure).
- **No `InvitedUser` global account type** — one User, many Memberships/Invitations.
- **CTA gating** — New organization / workspace / project visibility matches effective capabilities; unknown capability fails closed.

### Phase 8.1.2 — Member action UX & access separation

- **Manage access** is a dedicated secondary button beside **⋯** for Active Members only; not in overflow menu.
- **Grid layout:** **⋯** lives in the card header (logical top-end); compact **Manage access** utility button in the card footer (32px, start-aligned, not full-width).
- **List layout:** dedicated Actions column with `[Manage access] [⋯]` aligned per row.
- **Context menu trigger** uses in-flow positioning (not `org-overflow` absolute) so menus anchor to their card/row.
- **Overflow menu** (`ContextMenu`) is compact, keyboard-navigable, viewport-aware, RTL-safe — lifecycle/role actions only.
- **Change role** replaces Make admin/Make member; dedicated dialog with role selector and Admin/Member descriptions.
- **Admin → Member** demotion opens workspace access dialog when member has zero explicit grants.
- **Active members** no longer show invitation actions even when a pending invitation row exists in API data.
- **Invited / Suspended / Owner** menus follow status-specific action sets; Owner overflow is empty (protected).
- **Suspend** requires confirmation; **Remove** retains Phase 8.1.1 `Removed` lifecycle with expanded confirmation copy.

### Phase 8.1.1 — Members UX, lifecycle & notification hardening

- **Member action menu fix:** PostgreSQL RLS now allows Owner/Admin membership UPDATE/DELETE
  (`20260901130000_MembershipManagerMutationRls`); manager session GUCs set before writes.
  Role/suspend/resend/revoke no longer return HTTP 500.
- **Grid/List:** persisted per-user preference (`pts.memberView.{userId}`); **List is default**.
- **Task aggregates:** assignee-scoped counts on member list — active (Open/InProgress/Waiting),
  completed (Resolved/Closed), total assigned, completion rate %.
- **Sorting:** name, role, status, active/completed tasks, completion rate, recently joined.
- **Remove from organization:** `Removed` membership status; revokes workspace access;
  preserves task/comment history; self-removal forbidden; final owner protected.
- **Notifications:** empty/malformed `task_title` values sanitized at source and in renderer;
  legacy repair migration clears `"notification"`-style placeholders.
- **Avatar:** global User identity; `avatarUrl` DTO reserved; monogram fallback only —
  **upload deferred** until Storage module ships.

### Phase 8.1 — Members, invitations & access management

- **Members page** (`/app/tenants/:tenantId/members`): list-first admin surface with
  search, status/role filters, member identity (name/email/monogram), status badges,
  role display, workspace access summary, and capability-gated overflow actions.
- **Invite member:** email + role (+ optional workspace grants for Member).
  Creates hashed token invitation; Development returns `/invite/{token}` URL.
- **Pending invitations:** integrated rows + resend/revoke for Owner/Admin.
- **Role management:** Member ↔ Admin with owner-protection rules (backend authoritative).
- **Workspace access:** manage dialog for Members; Owner/Admin show implicit full access.
- **Suspension:** Active ↔ Suspended supported.
- **Removal:** Active/Suspended → **Removed** (Phase 8.1.1); not a User delete.
- **Invitation onboarding:** preview → sign in or register (display name + password) → accept;
  email binding enforced; explicit invalid/expired/revoked/used/mismatch states.
- **Token security:** SHA-256 hash storage, 7-day expiration, single-use, revocable/resendable.
- **RLS:** `tenant_invitations` + `invitation_workspace_grants` tenant-isolated;
  narrow token-hash GUC policies for preview SELECT and accept UPDATE.

**Email delivery adapter — DEFERRED.** Do not claim emails are sent in production.

### Phase 7 product UX

- **Application shell:** Organizations remain available with no tenant selected.
  Unavailable Workspaces / Projects / Tasks use a quieter disabled state plus
  an accessible title/aria-label (the same sentence is not printed three times).
  Deeper routes still show Organization context in the top bar. The root
  Organizations page no longer repeats “ORGANIZATION / Select an organization”.
  Switching still uses the existing server-derived `TenantContext` flow; the
  client does not authorize by `TenantId`.
- **Organizations page:** Resource management / switching. `PageHeader` is
  Organizations + description + **+ New organization**. No pending-invitation
  block. Zero organizations after load may promote **Create your first
  organization**.
- **Grid / List:** One data model, two presentations, local view preference
  (`pts.organizationView.{userId}` — UX only). Cards/rows show monogram, name,
  identifier, role, workspace count, Current (not color-only), open/select,
  and an overflow Edit only when `canManage` is true from the server.
- **Empty / loading / error:** Initial directory load is a compact loading
  state. Load failure uses a distinct empty state.
- **Invitations:** Pending invitations live in the top-bar attention surface
  (separate from work notifications). Accept remains the only action.
  **Invitation Decline is DEFERRED** — no backend Decline endpoint.
- **Shared primitives:** `PageHeader`, `ResourceToolbar`, `EmptyState`,
  `Dialog` / `Field`, `FeedbackProvider` / `ActionFeedback`, `OrganizationLogo`
  (monogram now; logo URL slot unused).
- **Governance:** [`.cursor/rules/90-product-ux.mdc`](.cursor/rules/90-product-ux.mdc)
  records list-first, progressive disclosure, conservative capabilities,
  comfortable B2B density, action feedback, a11y, i18n, RTL, and reuse. It
  does not encode pixel/color tokens.

### Phase 7.1 hardening

- **Dialog initial focus:** callers may pass `initialFocusRef`. Create
  Organization focuses the name field. Task dialogs keep the default fallback
  (first focusable control, typically Close). Trap, Escape, restore, and
  `aria-modal` are unchanged.
- **Non-Latin identifiers:** Latin-compatible names still become hyphenated
  ASCII identifiers. Arabic, Kurdish Sorani, and other names that produce no
  Latin identifier get a stable `organization-<8-hex>` fallback (FNV-1a of the
  normalized name). No public URL product. Server uniqueness remains
  authoritative. Users are not asked to invent an ASCII identifier unless they
  open Customize identifier.
- **Lint:** `npm run lint` completes with **0** application-code warnings.
  Remaining fetch-on-mount / colocated-hook cases use targeted, commented
  suppressions. Auth session loading and notification empty-list handling were
  adjusted idiomatically.
- **Slow Phase 6 test:** the create/edit Task test was ~5s because
  `userEvent.type` synthesized a full keystroke sequence for long titles inside
  a large App render. Title values are now set with `fireEvent.change` (same
  pattern already used for dates). Assertions and API payload checks are
  unchanged. Focused run: **~1.2–1.6s**. The 15s timeout was removed.
- **Capabilities:** missing `task.capabilities` still fail conservatively.

### Phase 7.2 Organization resource UX

- **Grid / List:** Accessible toggle (`aria-pressed`). Grid is the default
  browsing mode. List is the compact management mode. Primary navigation is a
  `Link`; Edit is a sibling button (no nested interactive controls).
- **WorkspaceCount:** `GET /tenants` returns a membership-gated aggregate.
  The frontend does not fetch workspaces per organization. Counts use
  localized pluralization (EN / AR / KU).
- **Organization editing:** `PUT /tenants/{tenantId}` updates **name only**.
  Owner/Admin Active membership is required on the server. Members receive
  `403 tenant_update_forbidden`. The identifier (slug) is not mutable —
  `TenantId` remains the identity; slug uniqueness and future URL use make
  rename unsafe to expose here. UI uses `canManage` from the directory
  response; missing capability data does not show Edit.
- **Create modal:** Compact `Dialog` size (task dialogs stay at the default
  width). **Customize identifier** is a disclosure button (`aria-expanded`,
  keyboard, RTL-safe). Footer actions stay Cancel / Create at the logical end.
  Name still receives initial focus.
- **Action feedback (Phase 7.2 baseline):** invitation accept still uses route
  `StatusBanner` via navigation state. Organization Create/Edit success moved
  to Phase 7.4 toasts (see below).
- **Invitations:** Removed from the Organizations page, including the empty
  state. Discoverable from the attention trigger (badge when pending). Work
  notifications stay a separate section and a separate backend model.
- **Logo / media:** **ACTUAL ORGANIZATION LOGO UPLOAD DEFERRED TO RESOURCE
  MEDIA / STORAGE PHASE.** Storage is still scaffolding (`IObjectStorage` does
  not exist). No base64-in-tenant, no filesystem paths, no fake upload
  control. Cards/rows use a monogram fallback and an `OrganizationLogo` slot.
- **Shell:** Topbar stays in document flow (not a sticky overlay), so resource
  headings cannot render beneath it. Content uses one shared max-width
  container (`72rem`) for PageHeader, toolbar, and Grid/List.
- **Accessibility:** Grid/List toggle, disclosure, Edit, invitation attention,
  `aria-current`, Current is not color-only. Dialog trap / Escape / restore /
  initial focus are unchanged.
- **Security:** No JWT permission claims, no client-authoritative `TenantId`,
  no RLS bypass. Workspace counts are gated by Active membership of the
  current user. Tenant UPDATE RLS is now Owner/Admin (was any Active member).

### Phase 7.3 layout and density hardening

This slice does not add product features. It corrects the Organization page
so PageHeader, ResourceToolbar, and Grid/List share one content rail.

- **Root cause:** the Organization grid used `auto-fill`, which reserved empty
  columns and left a fourth-column dead zone beside three cards. The toolbar
  was end-aligned with no collection summary, so Grid/List floated at the
  far edge. Page, header, and canvas gaps stacked into a large vertical void.
- **ResourceToolbar:** reusable start/end primitive (summary + future actions
  slot). Organizations show a localized collection count at logical-start and
  the Grid/List toggle at logical-end, directly above the resources.
- **Grid:** `auto-fit` with a sensible min track and a single-card max so one
  organization stays readable, two/three fill the rail, and four or more wrap.
  Cards stay compact; no invented metrics.
- **List:** same rail and toolbar; compact columns for identity, workspaces,
  role, current state, and actions.
- **Density:** tighter page/canvas spacing, header action aligned to the title
  row, selected toggle state is not color-only. Content remains centered at
  `72rem` so ultrawide viewports do not stretch cards into banners.
- **Backend / database:** none.

### Phase 7.4 action feedback foundation

This slice does not add product features. It establishes reusable mutation
outcome feedback before Workspace work begins.

- **Architecture:** application-level `FeedbackProvider` + `useFeedback()` +
  `ActionFeedback` toast list. Lives in `App.tsx` so feedback survives modal
  close and navigation. No third-party toast library.
- **Organization Create:** submit shows **Creating...** with spinner;
  duplicate submit blocked via `disabled`/`aria-busy`. Server success closes
  the modal, navigates, and shows **Organization created** /
  **{{name}} is ready to use.** Failures keep the dialog and entered values.
  Duplicate identifier stays a field error; network/permission/generic failures
  use friendly toasts.
- **Organization Edit:** **Saving...** loading state; success shows **Changes
  saved** / **{{name}} was updated successfully.** Failures keep the dialog
  and edited name. Permission failures use a dedicated toast copy.
- **Visual/motion:** compact logical-end desktop placement (mirrored in RTL);
  mobile inset above bottom nav. Subtle enter transition and one-shot success
  icon animation. `prefers-reduced-motion: reduce` removes motion.
- **Accessibility:** polite `status` for success, assertive `alert` for errors,
  manual dismiss, no focus steal, icon + text (not color-only).
- **Localization:** EN / AR / KU for all new feedback strings.
- **Cross-tenant workspace isolation (security fix):** For every organization,
  `GET /tenants/{tenantId}/workspaces` returns only workspaces whose persisted
  `TenantId` matches the server-established tenant session. RLS policy
  `workspaces_select_active_member` is limited to directory-count queries (no
  tenant GUC). Workspace list queries also filter by `TenantId` in
  `WorkManagementEndpoints`. The Workspaces route remounts on tenant change
  (`key={tenantId}`) so stale UI cannot flash another organization's list.
  Migration `20260830160000_WorkspaceDirectoryCountPolicyTenantScope`.
  Integration test uses dynamic Tenant A/B/C (not named production orgs).

### Phase 8.0 Workspace Resource UX

- **Page IA:** `PageHeader` (Workspaces + description + **+ New workspace**),
  `ResourceToolbar` (search, sort, Grid/List, collection count), resource
  grid/list — no permanent Create/Invite forms, no Organization Members block.
- **Create/Edit:** shared `Dialog` with Name (required), Description (optional,
  max 500), Start date (optional `DateOnly`). Server returns `canManage` for
  edit overflow. `FeedbackProvider` success/error toasts with loading states.
- **Search/sort:** client-side on the bounded tenant workspace collection;
  case-insensitive name/description search; sort Recently updated (default),
  Recently created, Name A–Z / Z–A with deterministic `workspaceId` tie-break.
- **View preference:** `pts.workspaceView.{userId}` (UX only).
- **Logo/media:** `WorkspaceLogo` monogram fallback only — **actual Workspace
  logo upload DEFERRED** (Storage module still scaffolding).
- **Member admin removal:** Invite Member form, member list, and per-member
  workspace access UI removed from the Workspace page. Backend membership/access
  APIs unchanged. **Phase 8.1** will add organization-level **Members** nav/page.
  No dead Members nav item added yet.
- **Tenant isolation:** preserved (`key={tenantId}`, request identity guards,
  RLS + endpoint scoping, A/B/C integration regression).
- **Migration:** `20260830170000_WorkspaceMetadata` — `description`,
  `start_date`, `updated_at_utc` on `workspaces`.
- **API:** `PUT /tenants/{tenantId}/workspaces/{workspaceId}`; expanded
  `WorkspaceResponse` with metadata + `CanManage`.

### Phase 8.0.1 Legacy workspace compatibility & load-state fix

**Root cause (reproduced):** `GET /tenants/{tenantId}/workspaces` returned
**HTTP 500** for organizations with pre-Phase-8 workspaces. Legacy rows had
**NULL** `updated_at_utc` (734/777 rows in the dev database). EF Core could
not materialize NULL into a non-nullable `DateTimeOffset`, throwing during
list queries. The Phase 8.0 metadata migration backfill did not apply under
**FORCE RLS** without temporarily disabling row security on `workspaces`.

**Frontend regression:** failed loads still showed **"No workspaces yet"**
alongside the generic error banner because empty state was derived from
`workspaces.length === 0` without checking load success.

**Fixes:**
- Corrective migration `20260830171000_WorkspaceUpdatedAtUtcBackfill` —
  backfills `updated_at_utc` from `created_at_utc` (with RLS disabled during
  migration), then sets NOT NULL.
- API mapping uses `UpdatedAtUtc ?? CreatedAtUtc`; sort uses the same
  coalesce. `AppDbContext` stamps `UpdatedAtUtc` on workspace saves when absent.
- Workspace page load states: **loading** / **ready** / **error** / **forbidden**.
  Error shows dedicated copy + **Retry** (no empty state). Create remains
  available; successful create refetches when list was in error.
- Phase 7 org-modal tests stabilized with directory-ready waits (**102/102**
  frontend tests passing).

- **Migrations:** `20260830170000_WorkspaceMetadata`,
  `20260830171000_WorkspaceUpdatedAtUtcBackfill`.
- **Data verified:** legacy workspaces (e.g. `e-razha`, `KARAMA`, `leopard`,
  `Sherko` under Patris `test1`) **still exist** in PostgreSQL — not deleted.

### Phase 8.0.2 Workspace empty-state spacing & access-badge clarity

Small UI polish from browser screenshots — **no backend or database changes.**

- **Empty state:** CTA wrapped in `.empty-state-action` with `16px` block-start
  margin so description and **+ New workspace** have clear hierarchy without
  excess height.
- **Access labels:** replaced action-like **Edit access** / **View access**
  (`role-badge` green pill) with non-interactive `.access-badge` status text:
  - Owner/Admin membership → **Full access** (implicit full resource access)
  - Member + `accessLevel: Edit` → **Can edit**
  - Member + `accessLevel: View` → **View only**
- **Edit workspace** overflow action unchanged; still gated by server
  `canManage` only — permission status and management action stay separate.
- **i18n:** `accessFull`, `accessCanEdit`, `accessViewOnly` in EN/AR/KU.
- **Tests:** empty-state action wrapper, access label mapping, badge
  non-interactivity, grid/list label consistency.

### Phase 8.0.3 Global user notification inbox foundation

Fixes the shell bell depending on selected organization and improves dropdown
information hierarchy.

- **Global inbox:** `GET /notifications` returns `{ items, unreadCount }` for
  the authenticated user across **Active** memberships. `POST
  /notifications/{notificationId}/read` marks read by notification id. Tenant
  endpoints (`/tenants/{tenantId}/notifications`) remain for tenant-scoped use.
- **Security:** User-level RLS SELECT/UPDATE policies on `notifications` keyed
  through recipient membership + `app.current_user_id` (Active membership only).
  `IUserRlsSessionFactory` opens transactions with SET LOCAL user context only.
  Tenant ownership and existing tenant-scoped RLS policies are unchanged. See
  [ADR 0010](docs/architecture/decisions/0010-global-user-notification-inbox.md).
- **Display snapshots:** `task_title` and `project_name` stored on notification
  rows at creation (backfilled for existing rows) so the inbox projection avoids
  cross-tenant joins.
- **Inactive membership:** Suspended/non-Active memberships omitted from global
  inbox (not an authorization bypass).
- **Shell UX:** Bell badge = global unread (0 hidden, 1–99 numeric, 99+).
  Dropdown rows show organization · type, resource message/title, optional
  project · relative time, restrained unread dot. Mark read on row open; no
  per-row Mark read clutter. Empty / error / loading states distinct. Cross-
  tenant click syncs organization selector and navigates to resource; stale
  targets show unavailable messaging.
- **Invitations:** Still separate via `GET /invitations` (not notification
  rows). Global inbox architecture is compatible with future invitation items.
- **Realtime:** Explicitly **not** implemented (refresh/fetch only).
- **i18n:** Notification type labels, relative time, empty/error copy in EN/AR/KU.
- **Governance:** `.cursor/rules/90-product-ux.mdc` — global user attention,
  notification information hierarchy, restrained read/unread.
- **Tests:** `GlobalNotificationInboxHttpTests` (aggregation, cross-user,
  suspended membership, read isolation), `phase803.test.tsx`, presentation unit
  tests; Phase 6 deep-link tests preserved.

### Phase 8.0.3C Resource name integrity (case-insensitive uniqueness)

- **Normalization:** `trim` + invariant lowercase (`ResourceNameNormalizer`),
  aligned with PostgreSQL `lower(btrim(name))` backfill/index columns.
- **Organization display names:** unique per **authenticated user** among
  **Active** memberships (not global cross-tenant). Slug/identifier uniqueness
  unchanged. Different users may each own an organization named “Patris”.
- **Workspace names:** unique per **Tenant** (`tenant_id + name_normalized`).
- **Project names:** unique per **Workspace** (`tenant_id + workspace_id +
  name_normalized`).
- **Database:** `name_normalized` columns + unique indexes on workspaces/projects;
  organization conflicts enforced in `EfTenantLifecycleStore` (user membership
  query, no cross-tenant leak).
- **API:** `409 Conflict` with `organization_name_conflict`,
  `workspace_name_conflict`, `project_name_conflict` (+ optional `existingName`).
- **Frontend:** field-level duplicate errors on Create/Edit modals (EN/AR/KU);
  modal stays open with entered values preserved.
- **Tests:** `ResourceNameUniquenessHttpTests` (including concurrent workspace
  create), `phase803c.test.tsx`.

### Phase 8.0.3C-2 Workspace GET 500 repair (migration + legacy duplicates)

- **Root cause:** `20260831153000_ResourceNameUniqueness` was not applied while
  the running app expected `name_normalized` → PostgreSQL **42703** (missing
  column) on `GET /tenants/{id}/workspaces`.
- **Migration fix:** backfill SQL now **disables RLS** during
  `name_normalized` population (same pattern as
  `WorkspaceUpdatedAtUtcBackfill`) because `migrator_role` does not bypass RLS.
- **Legacy duplicate data (local dev):** case-equivalent rows renamed in place
  (IDs/relationships preserved) — oldest row keeps the original display name;
  later rows suffixed `duplicate 2`, `duplicate 3`, etc.
- **Migration applied** on PostgreSQL **127.0.0.1:5433**; unique indexes
  `ux_workspaces_tenant_name_normalized` and
  `ux_projects_tenant_workspace_name_normalized` verified.
- **SaveChanges:** `AppDbContext` stamps `NameNormalized` from `Name` on
  workspace/project writes (tests and direct EF inserts stay consistent).

`docs/architecture/architecture-charter.md` records architectural principles.
This README is the current implemented product baseline.

## Project purpose

A SaaS product where organizations ("tenants") manage projects, tasks, comments,
and collaboration, with strict data isolation between tenants, organization-level
subscriptions/billing (not yet implemented), and a multilingual UI from day one.

## Architecture

Single deployable ASP.NET Core application (`src/Backend/Host/PTS.Host`),
internally organized into independent bounded contexts. It is not, and must not
become, microservices.

Full architectural intent lives in
[`docs/architecture/architecture-charter.md`](docs/architecture/architecture-charter.md)
(charter text may lag the code). Individual decisions are under
[`docs/architecture/decisions/`](docs/architecture/decisions/).
Persistent, agent-enforced rules live in [`.cursor/rules/`](.cursor/rules/).

### Module boundaries

| Module | Responsibility | Status |
|---|---|---|
| **Identity** | Global user identity/authentication. No tenant concept. | Implemented |
| **Tenancy** | Tenants and Membership (`User` + `TenantId` + role) | Implemented |
| **WorkManagement** | Workspaces, projects, tasks, comments, tags, activity, notifications | Implemented through Phase 6 |
| **Entitlements** | Plan/feature/limit checks derived from subscription state | Scaffolding only |
| **Billing** | Subscriptions and payment-provider integration | Scaffolding only |
| **Storage** | Tenant-aware file/object storage abstraction | Scaffolding only |
| **PlatformAdministration** | Internal operator permissions, separate from tenant roles | Dev bootstrap + platform-admin records |
| **Audit** | Durable, tenant-aware audit trail | Scaffolding only |

A global **User** (Identity) is always separate from tenant **Membership**
(Tenancy). A user may belong to multiple tenants. Platform-administrator
permissions are separate from any tenant-level role.

### Dependency rules

Enforced by `tests/Backend/PTS.Architecture.Tests`:

- A module may reference `PTS.SharedKernel` only.
- Modules must not reference each other.
- `PTS.Host` is the composition root and must stay free of business logic.

## Technology stack

| Layer | Technology |
|---|---|
| Backend | ASP.NET Core (.NET 10 LTS) |
| Frontend | React + TypeScript (Vite) |
| Database | PostgreSQL 17, with Row-Level Security |
| ORM | Entity Framework Core |
| Localization | i18next / react-i18next |
| Local infrastructure | Docker / Docker Compose |

## Tenancy, authentication, and authorization

### Authentication

JWT access tokens identify the **User** only (`sub` / name identifier, email, `jti`).
Tokens do **not** carry tenant role, Workspace access, or Task permissions.
Role and access changes apply on the next request without issuing a new JWT.

### Membership and tenant roles

| Concept | Owner |
|---|---|
| User | Identity module — credentials and profile; no `TenantId` |
| Membership | Tenancy module — User + Tenant + role + status (`Active` / `Invited` / `Suspended`) |
| Tenant roles | `Owner` / `Admin` / `Member` |
| Platform administrator | PlatformAdministration — not a tenant role |

Invited and Suspended memberships cannot establish tenant context.

### Workspace View/Edit

Inside one tenant, WorkManagement resource access is:

| Actor | Access |
|---|---|
| Owner / Admin | Implicit full Workspace and Project access |
| Member + none | No Workspace/Project/Task access |
| Member + View | Read Workspace, Projects, and Tasks; may comment |
| Member + Edit | Create/update Workspace-scoped work as the endpoints allow |

Projects inherit the parent Workspace level. There are no project-level
permission overrides and no Task-specific permission records.

Authorization chain:

```text
Authentication
→ Membership (Active)
→ Tenant Role
→ Workspace Resource Authorization (View / Edit)
→ PostgreSQL RLS (Tenant A vs Tenant B)
```

`TenantId` is never trusted from headers, the request body, the URL, query
parameters, or frontend state. The backend establishes tenant context only after
authenticating the user and verifying an Active Membership.

## Multi-tenancy and PostgreSQL RLS

- Every tenant-owned row belongs to exactly one `TenantId`. Cross-tenant
  references are prohibited.
- **PostgreSQL Row-Level Security is mandatory** on tenant-owned tables
  (ENABLE + FORCE). Application `WHERE TenantId = ...` is still required but is
  not sufficient alone.
- Session GUCs such as `app.current_tenant_id` (and membership-scoped settings
  where needed) back RLS policies.
- Two PostgreSQL roles: `migrator_role` (DDL / migrations / RLS policy creation)
  and `app_role` (runtime — no DDL, no `BYPASSRLS`, not the database owner).

## Work hierarchy

```text
Tenant
→ Workspace
→ Project
→ Task
```

Task parent ids (`TenantId`, `WorkspaceId`, `ProjectId`) are immutable. Tasks
are not moved between projects.

## Tasks (Phase 6)

### Fields

- Title (required, max 200)
- Description (optional, max 4000)
- Status
- Priority (`Low` / `Normal` / `High` / `Urgent`; API still accepts legacy `Medium` as `Normal`)
- Deadline (optional calendar date)
- Immutable **Creator** Membership
- Optional current **Assignee** Membership (never a global `UserId`)
- Reusable tenant-scoped **Tags**

### Statuses

`Open` | `InProgress` | `Waiting` | `Resolved` | `Closed`

`Closed` is the only closed state. There is no `IsClosed` flag.
Legacy API strings `Todo` and `Done` map to `Open` and `Closed`.

### Creator vs current assignee

Workspace View/Edit is the access gate. Field rights are then:

| Action | Creator | Current assignee | Previous assignee / View |
|---|---|---|---|
| Title, description, priority, deadline | Yes (unless Closed) | No | No |
| Tags | Yes | Yes | No |
| Reassign | Yes | Yes | No |
| Open / InProgress / Waiting / Resolved | Yes | Yes | No |
| Closed / Reopen | Yes | No | No |
| Comment | Yes | Yes | Yes if Workspace View |
| Own comment edit/delete | Yes if author | Yes if author | Yes if author |
| View activity | Yes | Yes | Yes |
| Delete task | **Creator only** | No | No |

Creator identity never changes. After a handoff, the previous assignee
immediately loses assignee mutation rights. The backend checks the persisted
assignee; stale UI state is not trusted. Responses include `capabilities`;
the UI must not invent rights the API omitted.

Assignable members must be Active, same tenant, and able to view the Task
Workspace (Owner/Admin implicit, or Member View/Edit). Invited, Suspended,
no-access, and foreign-tenant memberships are rejected.

### Comments, activity, tags, notifications

- **Comments** live on the Task (shown in the Task modal). View-authorized
  users can comment. Authors can edit/delete their own comments.
- **Activity** is append-only structured history (old → new, actor, UTC).
  There is no activity mutation API.
- **Tags** are tenant-scoped and reusable. Uniqueness is
  `(TenantId, NormalizedName)`.
- **Notifications** are durable in-app rows. Typical participants are the
  Creator and current Assignee. The actor is not notified of their own action.
  The previous assignee is not an implicit watcher. Unchanged assignee and
  self-assignment do not create assignment notifications.

### Task UI

The Project Task page is list-first:

- Compact rows (title, status, priority, assignee, due date, a few tags)
- **Create Task** opens a modal
- Clicking a row (or opening
  `/app/tenants/{tenantId}/workspaces/{workspaceId}/projects/{projectId}/tasks/{taskId}`)
  opens the Task detail modal
- Closing the modal returns to the project task list
- Assignee handoff in the modal asks for confirmation before persist
- 403/409 on update refetches the Task

### Task HTTP surface

Under `/tenants/{tenantId}/workspaces/{workspaceId}/projects/{projectId}`:

- `GET/POST /tasks`, `GET/PUT/DELETE /tasks/{taskId}`
- `GET/POST /tasks/{taskId}/comments`
- `PUT/DELETE /tasks/{taskId}/comments/{commentId}`
- `GET /tasks/{taskId}/activity`
- `POST /tasks/{taskId}/seen`

Also:

- `GET /tenants/{tenantId}/workspaces/{workspaceId}/assignable-members`
- `GET/POST /tenants/{tenantId}/workspaces/{workspaceId}/tags`
- `GET /tenants/{tenantId}/notifications`
- `POST /tenants/{tenantId}/notifications/{notificationId}/read`

## Localization and RTL

| Code | Language | Direction |
|---|---|---|
| `en` | English | LTR (fallback/default) |
| `ar` | Arabic | RTL |
| `ku` | Kurdish Sorani | RTL |

The UI switches language at runtime (no page reload). `<html lang>` and
`<html dir>` update automatically. Resources live under
`src/Web/src/locales/<code>/` by namespace. User-entered business content is
never machine-translated.

See [`src/Web/README.md`](src/Web/README.md) and
[`.cursor/rules/60-localization-i18n.mdc`](.cursor/rules/60-localization-i18n.mdc).

## Phase 6 migrations

Applied as `migrator_role`:

| Migration | What it added |
|---|---|
| `20260829062146_TaskManagementCore` | `tasks` table, tenant-safe project FK, ENABLE + FORCE RLS |
| `20260829065620_TaskPriorityDeadline` | `Medium` → `Normal`, `Urgent`, priority check constraint |
| `20260829074104_TaskAssignmentTagsNotifications` | assignee FK, tags, notifications, peer SELECT policies |
| `20260829083242_TaskTicketCollaboration` | creator, comments, activity, read-state, refined statuses |

## Phase 7.2 migration

| Migration | Why it exists |
|---|---|
| `20260830140000_OrganizationDirectoryCounts` | Owner/Admin `tenants_update`. (Count function later replaced — FORCE RLS blocked SECURITY DEFINER.) **No Tenant logo column.** |
| `20260830152000_OrganizationDirectoryCountsByUser` | Intermediate count-function signature change; superseded by the policy below. |
| `20260830153000_WorkspaceDirectoryCountPolicy` | `workspaces_select_active_member` — membership-gated SELECT so `GET /tenants` can aggregate `WorkspaceCount` in one query without `current_tenant_id`, N+1, or `BYPASSRLS`. |
| `20260830160000_WorkspaceDirectoryCountPolicyTenantScope` | **Security fix:** scopes `workspaces_select_active_member` to sessions with **no** `app.current_tenant_id` only. |
| `20260830170000_WorkspaceMetadata` | Phase 8.0 — optional `description`, `start_date`, `updated_at_utc` on `workspaces`. |
| `20260830171000_WorkspaceUpdatedAtUtcBackfill` | Phase 8.0.1 — backfill legacy NULL `updated_at_utc` under RLS-safe migration; NOT NULL. |
| `20260831100000_GlobalNotificationInbox` | Phase 8.0.3 — `task_title`/`project_name` on `notifications`, user-level RLS inbox policies, recipient+created index. |
| `20260831153000_ResourceNameUniqueness` | Phase 8.0.3C — `name_normalized` + unique indexes on workspaces/projects. |

Earlier tenancy/workspace migrations remain in the same EF history
(Identity/Tenancy RLS, Workspace/Project, Workspace access).

## Intentionally unsupported (not Phase 7, not started)

- Phase 8 and later product work
- Kanban, drag/drop, subtasks, dependencies
- Multi-assignee, watchers
- Attachments, email delivery, SignalR / WebSockets
- Time tracking, reporting, dashboards
- Billing, Stripe, entitlement enforcement
- OAuth / social login, cookie/session UI auth
- Task-specific permission records
- Project-level permission overrides
- Major Workspace / Project / Task redesign
- Sorting redesign / complex filters
- Public organization URL product (no `pts.app/...` feature was invented)
- Invitation Decline (**DEFERRED** — backend has no Decline endpoint)
- Actual Organization logo upload (**DEFERRED TO RESOURCE MEDIA / STORAGE PHASE**)
- Member / project / task aggregate counts on Organization cards
- Dashboard, My Work, Global Search, notification-center redesign
- Workspace / Project / Task discovery redesign
- Custom RBAC / arbitrary permission matrices

## Test and verification status

Run in this workspace on **31 Aug 2026** after Phase 8.0.3C-2 migration repair.

| Check | Result |
|---|---|
| `dotnet build PTS.slnx` | **Succeeded** |
| Architecture tests | **27 passed** |
| Integration tests | **89 passed** (includes 7 resource-name uniqueness tests) |
| `npm run test` (`src/Web`) | **25 files / 121 passed**, 0 failed |
| `npm run lint` | Succeeded, **0 warnings** |
| `npm run build` | Succeeded |
| EF migration `20260831153000_ResourceNameUniqueness` | **Applied** on local PostgreSQL 5433 |
| Live API smoke (GET workspaces, duplicate 409, cross-tenant allow) | **Verified** |
| Interactive browser QA (Patris/FastPay UI walkthrough) | **Not run** |

Automated coverage includes global notification inbox (multi-tenant aggregation,
cross-user isolation, suspended membership omission, read-by-id), notification
dropdown presentation (organization, type, title, timestamp, unread), cross-tenant
navigation, Workspace access-badge labels, empty-state/load-error states, legacy
+ modern workspace list compatibility, Organization UX (Phase 7), and Phase 6 task
flows. These are **not** a substitute for live browser QA.

## Local development

### Prerequisites

- .NET 10 SDK (pinned via [`global.json`](global.json))
- Node.js 20+ and npm
- Docker / Docker Compose (for local PostgreSQL), or an equivalent native
  PostgreSQL 17+ instance

### Database

Via Docker (preferred):

```bash
cd infra/docker
cp .env.example .env   # then set real local passwords
docker compose up -d
```

See [`infra/docker/README.md`](infra/docker/README.md).

Without Docker, provision an equivalent native PostgreSQL 17+ instance, then
run
[`infra/docker/postgres/init/templates/roles.template.sql`](infra/docker/postgres/init/templates/roles.template.sql)
as a superuser.

The application and migration tooling read credentials only from environment
variables — never from source or `appsettings.json`:

```bash
export PTS_APP_PASSWORD=...        # app_role — required to run the app or PTS.IntegrationTests
export PTS_MIGRATOR_PASSWORD=...   # migrator_role — required for dotnet ef
# optional, default localhost:5432/pts:
export POSTGRES_HOST=localhost
export POSTGRES_HOST_PORT=5432
export POSTGRES_DB=pts
```

On Windows PowerShell, load `infra/docker/.env` with:

```powershell
. .\scripts\Load-DevEnv.ps1
```

### Backend

```bash
dotnet build PTS.slnx
dotnet test PTS.slnx          # architecture tests always run; PTS.IntegrationTests
                               # skips (does not fail) if PostgreSQL/PTS_APP_PASSWORD
                               # is unavailable
dotnet ef database update --project src/Backend/Host/PTS.Host
dotnet run --project src/Backend/Host/PTS.Host   # GET /health
```

### Frontend

```bash
cd src/Web
npm install
npm run dev
npm run test
npm run lint
npm run build
```
