import { type FormEvent, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";

import { createTenant, updateTenant } from "../api/client";
import { isApiError } from "../api/errors";
import { readSelectedTenantId, writeSelectedTenantId } from "../api/session";
import type { TenantMembership } from "../api/types";
import { useAuth } from "../auth/AuthProvider";
import { Dialog } from "../components/Dialog";
import { AccountOnboardingPanel } from "../components/AccountOnboardingPanel";
import { EmptyState } from "../components/EmptyState";
import { OrganizationLogo } from "../components/OrganizationLogo";
import { PageHeader } from "../components/PageHeader";
import { ResourceToolbar } from "../components/ResourceToolbar";
import { Field } from "../components/Ui";
import { useFeedback } from "../feedback/FeedbackProvider";
import { resolveActiveTenantId } from "../tenancy/activeTenant";
import { canCreateOrganization } from "../tenancy/accountCapabilities";
import {
  canManageOrganization,
  organizationWorkspaceCount,
} from "../tenancy/organizationMonogram";
import { type OrganizationView, readOrganizationView, writeOrganizationView } from "../tenancy/organizationView";
import { slugFromName } from "../tenancy/slugFromName";
import { useTenantDirectory } from "../tenancy/TenantDirectoryProvider";

export function TenantsPage() {
  const { t } = useTranslation(["tenants", "common"]);
  const { show } = useFeedback();
  const { token, user } = useAuth();
  const { tenants, invitations, accountCapabilities, refresh, isRefreshing, error: directoryError } = useTenantDirectory();
  const { tenantId: routeTenantId } = useParams();
  const navigate = useNavigate();
  const [view, setView] = useState<OrganizationView>(() =>
    user ? readOrganizationView(user.userId) : "grid",
  );
  const [createOpen, setCreateOpen] = useState(false);
  const [editing, setEditing] = useState<TenantMembership | null>(null);
  const [name, setName] = useState("");
  const [slug, setSlug] = useState("");
  const [slugTouched, setSlugTouched] = useState(false);
  const [customizeSlug, setCustomizeSlug] = useState(false);
  const [editName, setEditName] = useState("");
  const [nameError, setNameError] = useState<string | null>(null);
  const [slugError, setSlugError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const nameInputRef = useRef<HTMLInputElement>(null);
  const editNameRef = useRef<HTMLInputElement>(null);
  const directoryPending = isRefreshing && tenants.length === 0 && accountCapabilities === null;
  const mayCreateOrganization = canCreateOrganization(accountCapabilities);

  const activeTenantId = resolveActiveTenantId(
    tenants,
    routeTenantId,
    user ? readSelectedTenantId(user.userId) : null,
  );

  function resetCreate() {
    setName("");
    setSlug("");
    setSlugTouched(false);
    setCustomizeSlug(false);
    setNameError(null);
    setSlugError(null);
    setCreateOpen(false);
  }

  function openCreate() {
    setNameError(null);
    setSlugError(null);
    setCreateOpen(true);
  }

  function openEdit(tenant: TenantMembership) {
    setNameError(null);
    setSlugError(null);
    setEditName(tenant.name);
    setEditing(tenant);
  }

  function resetEdit() {
    setEditing(null);
    setEditName("");
    setNameError(null);
  }

  function changeView(next: OrganizationView) {
    setView(next);
    if (user) {
      writeOrganizationView(user.userId, next);
    }
  }

  async function onCreate(event: FormEvent) {
    event.preventDefault();
    if (!token || !user || busy) {
      return;
    }
    const nextSlug = slugTouched ? slug.trim().toLowerCase() : slugFromName(name);
    if (!nextSlug) {
      setCustomizeSlug(true);
      setSlugError(t("tenants:errors.identifierRequired"));
      return;
    }
    setBusy(true);
    setNameError(null);
    setSlugError(null);
    try {
      const created = await createTenant(token, name, nextSlug);
      writeSelectedTenantId(user.userId, created.tenantId);
      await refresh();
      show({
        tone: "success",
        title: t("tenants:createdTitle"),
        body: t("tenants:createdBody", { name: created.name }),
      });
      resetCreate();
      navigate(`/app/tenants/${created.tenantId}`);
    } catch (cause) {
      applyCreateFailure(cause);
    } finally {
      setBusy(false);
    }
  }

  async function onEdit(event: FormEvent) {
    event.preventDefault();
    if (!token || !editing || busy) {
      return;
    }
    setBusy(true);
    setNameError(null);
    try {
      const updated = await updateTenant(token, editing.tenantId, editName);
      await refresh();
      show({
        tone: "success",
        title: t("tenants:updatedTitle"),
        body: t("tenants:updatedBody", { name: updated.name }),
      });
      resetEdit();
    } catch (cause) {
      applyEditFailure(cause);
    } finally {
      setBusy(false);
    }
  }

  function applyCreateFailure(cause: unknown) {
    if (isApiError(cause) && cause.code === "organization_create_forbidden") {
      show({
        tone: "error",
        title: t("tenants:permissionTitle"),
        body: t("tenants:onboarding.createForbiddenBody"),
      });
      return;
    }
    if (isApiError(cause) && cause.code === "duplicate_slug") {
      setCustomizeSlug(true);
      setSlugError(t("tenants:errors.duplicateSlug"));
      return;
    }
    if (isApiError(cause) && cause.code === "organization_name_conflict") {
      setNameError(
        cause.existingName
          ? t("tenants:errors.nameConflictNamed", { existingName: cause.existingName })
          : t("tenants:errors.nameConflict"),
      );
      return;
    }
    if (isApiError(cause) && cause.code === "invalid_tenant") {
      setNameError(t("common:errors.invalid_tenant"));
      return;
    }
    if (isApiError(cause) && cause.code === "network") {
      show({
        tone: "error",
        title: t("common:feedback.connectionTitle"),
        body: t("common:feedback.connectionBody"),
      });
      return;
    }
    show({
      tone: "error",
      title: t("tenants:createFailedTitle"),
      body: t("tenants:createFailedBody"),
    });
  }

  function applyEditFailure(cause: unknown) {
    if (isApiError(cause) && (cause.code === "tenant_update_forbidden" || cause.code === "forbidden")) {
      show({
        tone: "error",
        title: t("tenants:permissionTitle"),
        body: t("tenants:permissionBody"),
      });
      return;
    }
    if (isApiError(cause) && cause.code === "organization_name_conflict") {
      setNameError(
        cause.existingName
          ? t("tenants:errors.nameConflictNamed", { existingName: cause.existingName })
          : t("tenants:errors.nameConflict"),
      );
      return;
    }
    if (isApiError(cause) && cause.code === "invalid_tenant") {
      setNameError(t("common:errors.invalid_tenant"));
      return;
    }
    if (isApiError(cause) && cause.code === "network") {
      show({
        tone: "error",
        title: t("common:feedback.connectionTitle"),
        body: t("common:feedback.connectionBody"),
      });
      return;
    }
    show({
      tone: "error",
      title: t("tenants:saveFailedTitle"),
      body: t("tenants:saveFailedBody"),
    });
  }

  function roleLabel(role: string) {
    return t(`tenants:roles.${role}`, { defaultValue: role });
  }

  const createToolbarAction = mayCreateOrganization ? (
    <button
      className="primary-action toolbar-primary-action"
      type="button"
      aria-haspopup="dialog"
      aria-expanded={createOpen}
      aria-label={t("tenants:newOrganization")}
      onClick={openCreate}
    >
      <span aria-hidden="true">+</span>
      {t("tenants:newOrganization")}
    </button>
  ) : null;

  return (
    <section className="app-page">
      <PageHeader title={t("tenants:title")} description={t("tenants:description")} />
      {directoryPending ? (
        <EmptyState compact title={t("common:loading")} />
      ) : directoryError && tenants.length === 0 ? (
        <EmptyState title={t("tenants:loadFailed")} body={t("tenants:loadFailedBody")} />
      ) : tenants.length === 0 ? (
        <AccountOnboardingPanel
          invitations={invitations}
          canCreateOrganization={mayCreateOrganization}
          onCreateClick={openCreate}
        />
      ) : (
        <div className="resource-section">
          <ResourceToolbar
            label={t("tenants:resourceToolbar")}
            summary={t("tenants:resourceSummary", { count: tenants.length })}
          >
            <div className="view-toggle" role="group" aria-label={t("tenants:viewToggle")}>
              <button
                type="button"
                className={view === "grid" ? "view-toggle-active" : undefined}
                aria-pressed={view === "grid"}
                onClick={() => changeView("grid")}
              >
                <ViewToggleIcon name="grid" />
                {t("tenants:viewGrid")}
              </button>
              <button
                type="button"
                className={view === "list" ? "view-toggle-active" : undefined}
                aria-pressed={view === "list"}
                onClick={() => changeView("list")}
              >
                <ViewToggleIcon name="list" />
                {t("tenants:viewList")}
              </button>
            </div>
            {createToolbarAction}
          </ResourceToolbar>
          {view === "grid" ? (
            <ul className="org-grid">
              {tenants.map((tenant) => (
                <li key={tenant.tenantId}>
                  <OrganizationCard
                    tenant={tenant}
                    current={tenant.tenantId === activeTenantId}
                    roleLabel={roleLabel(tenant.role)}
                    workspaceLabel={t("tenants:workspaceCount", {
                      count: organizationWorkspaceCount(tenant),
                    })}
                    currentLabel={t("tenants:current")}
                    editLabel={t("tenants:editOrganization")}
                    onEdit={canManageOrganization(tenant) ? () => openEdit(tenant) : undefined}
                  />
                </li>
              ))}
            </ul>
          ) : (
            <ul className="org-list">
              {tenants.map((tenant) => (
                <li key={tenant.tenantId}>
                  <OrganizationRow
                    tenant={tenant}
                    current={tenant.tenantId === activeTenantId}
                    roleLabel={roleLabel(tenant.role)}
                    workspaceLabel={t("tenants:workspaceCount", {
                      count: organizationWorkspaceCount(tenant),
                    })}
                    currentLabel={t("tenants:current")}
                    editLabel={t("tenants:editOrganization")}
                    onEdit={canManageOrganization(tenant) ? () => openEdit(tenant) : undefined}
                  />
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      <Dialog
        open={createOpen}
        titleId="create-org-title"
        title={t("tenants:create")}
        closeLabel={t("common:close")}
        onClose={resetCreate}
        initialFocusRef={nameInputRef}
        size="compact"
      >
        <form className="form-card form-card-compact" onSubmit={onCreate}>
          <p>{t("tenants:createDescription")}</p>
          <div className="form-fields">
            <Field id="tenant-name" label={t("tenants:name")} error={nameError ?? undefined}>
              <input
                id="tenant-name"
                ref={nameInputRef}
                required
                maxLength={200}
                placeholder={t("tenants:namePlaceholder")}
                value={name}
                onChange={(event) => {
                  const next = event.target.value;
                  setName(next);
                  setNameError(null);
                  if (!slugTouched) {
                    setSlug(slugFromName(next));
                  }
                }}
              />
            </Field>
            <button
              type="button"
              className="disclosure-button"
              aria-expanded={customizeSlug}
              aria-controls="tenant-slug-panel"
              onClick={() => {
                setCustomizeSlug((current) => {
                  const next = !current;
                  if (next && !slug) {
                    setSlug(slugFromName(name));
                  }
                  return next;
                });
              }}
            >
              <span className="disclosure-chevron" aria-hidden="true" />
              {t("tenants:customizeIdentifier")}
            </button>
            <div id="tenant-slug-panel">
              {customizeSlug ? (
                <Field id="tenant-slug" label={t("tenants:urlIdentifier")} error={slugError ?? undefined}>
                  <input
                    id="tenant-slug"
                    required
                    maxLength={100}
                    placeholder={t("tenants:slugPlaceholder")}
                    value={slug}
                    onChange={(event) => {
                      setSlugTouched(true);
                      setSlugError(null);
                      setSlug(event.target.value);
                    }}
                  />
                  <p className="field-hint">{t("tenants:urlIdentifierHelp")}</p>
                </Field>
              ) : (
                <input id="tenant-slug-hidden" type="hidden" value={slug || slugFromName(name)} readOnly />
              )}
            </div>
          </div>
          <div className="dialog-actions">
            <button className="secondary-action" type="button" onClick={resetCreate}>
              {t("common:cancel")}
            </button>
            <button className="primary-action" type="submit" disabled={busy} aria-busy={busy}>
              {busy ? <span className="button-spinner" aria-hidden="true" /> : null}
              {busy ? t("tenants:creating") : t("tenants:create")}
            </button>
          </div>
        </form>
      </Dialog>

      <Dialog
        open={Boolean(editing)}
        titleId="edit-org-title"
        title={t("tenants:editOrganization")}
        closeLabel={t("common:close")}
        onClose={resetEdit}
        initialFocusRef={editNameRef}
        size="compact"
      >
        <form className="form-card form-card-compact" onSubmit={onEdit}>
          <div className="form-fields">
            <Field id="edit-tenant-name" label={t("tenants:name")} error={nameError ?? undefined}>
              <input
                id="edit-tenant-name"
                ref={editNameRef}
                required
                maxLength={200}
                value={editName}
                onChange={(event) => {
                  setEditName(event.target.value);
                  setNameError(null);
                }}
              />
            </Field>
            {editing ? (
              <p className="field-hint">
                {t("tenants:urlIdentifier")}: {editing.slug}
              </p>
            ) : null}
          </div>
          <div className="dialog-actions">
            <button className="secondary-action" type="button" onClick={resetEdit}>
              {t("common:cancel")}
            </button>
            <button className="primary-action" type="submit" disabled={busy} aria-busy={busy}>
              {busy ? <span className="button-spinner" aria-hidden="true" /> : null}
              {busy ? t("tenants:saving") : t("tenants:saveChanges")}
            </button>
          </div>
        </form>
      </Dialog>
    </section>
  );
}

function ViewToggleIcon({ name }: { name: "grid" | "list" }) {
  return (
    <svg className="view-toggle-icon" viewBox="0 0 16 16" aria-hidden="true">
      {name === "grid" ? (
        <>
          <rect x="1.5" y="1.5" width="5.5" height="5.5" rx="1" />
          <rect x="9" y="1.5" width="5.5" height="5.5" rx="1" />
          <rect x="1.5" y="9" width="5.5" height="5.5" rx="1" />
          <rect x="9" y="9" width="5.5" height="5.5" rx="1" />
        </>
      ) : (
        <>
          <path d="M2 3.5h12M2 8h12M2 12.5h12" />
        </>
      )}
    </svg>
  );
}

function OrganizationCard({
  tenant,
  current,
  roleLabel,
  workspaceLabel,
  currentLabel,
  editLabel,
  onEdit,
}: {
  tenant: TenantMembership;
  current: boolean;
  roleLabel: string;
  workspaceLabel: string;
  currentLabel: string;
  editLabel: string;
  onEdit?: () => void;
}) {
  return (
    <div className={`org-card ${current ? "org-card-current" : ""}`}>
      <Link
        className="org-card-main"
        to={`/app/tenants/${tenant.tenantId}`}
        aria-current={current ? "page" : undefined}
      >
        <OrganizationLogo name={tenant.name} />
        <span className="org-card-copy">
          <strong>{tenant.name}</strong>
          <small>{tenant.slug}</small>
        </span>
        <span className="org-card-meta">
          <span className="role-badge">{roleLabel}</span>
          <span>{workspaceLabel}</span>
          {current ? <span className="current-org-badge">{currentLabel}</span> : null}
        </span>
      </Link>
      {onEdit ? (
        <button type="button" className="org-overflow" aria-label={editLabel} onClick={onEdit}>
          <span aria-hidden="true">⋯</span>
        </button>
      ) : null}
    </div>
  );
}

function OrganizationRow({
  tenant,
  current,
  roleLabel,
  workspaceLabel,
  currentLabel,
  editLabel,
  onEdit,
}: {
  tenant: TenantMembership;
  current: boolean;
  roleLabel: string;
  workspaceLabel: string;
  currentLabel: string;
  editLabel: string;
  onEdit?: () => void;
}) {
  return (
    <div className={`org-row ${current ? "org-row-current" : ""}`}>
      <Link
        className="org-row-main"
        to={`/app/tenants/${tenant.tenantId}`}
        aria-current={current ? "page" : undefined}
      >
        <span className="org-row-identity">
          <OrganizationLogo name={tenant.name} />
          <span className="org-row-copy">
            <strong>{tenant.name}</strong>
            <small>{tenant.slug}</small>
          </span>
        </span>
        <span className="org-row-workspaces">{workspaceLabel}</span>
        <span className="role-badge">{roleLabel}</span>
        <span className="org-row-state">
          {current ? <span className="current-org-badge">{currentLabel}</span> : null}
        </span>
      </Link>
      <span className="org-row-actions">
        {onEdit ? (
          <button type="button" className="org-overflow" aria-label={editLabel} onClick={onEdit}>
            <span aria-hidden="true">⋯</span>
          </button>
        ) : null}
      </span>
    </div>
  );
}
