import { type FormEvent, type ReactNode, type RefObject, useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";

import { createWorkspace, listWorkspaces, updateWorkspace } from "../api/client";
import { isApiError } from "../api/errors";
import { shouldApplyResponse } from "../api/requestIdentity";
import type { Workspace } from "../api/types";
import { useAuth } from "../auth/AuthProvider";
import { Dialog } from "../components/Dialog";
import { EmptyState } from "../components/EmptyState";
import { PageHeader } from "../components/PageHeader";
import { ResourceToolbar } from "../components/ResourceToolbar";
import { Field, StatusBanner } from "../components/Ui";
import { WorkspaceAccessBadge } from "../components/WorkspaceAccessBadge";
import { WorkspaceLogo } from "../components/WorkspaceLogo";
import { useFeedback } from "../feedback/FeedbackProvider";
import { useTenantDirectory } from "../tenancy/TenantDirectoryProvider";
import {
  canCreateWorkspace,
  canManageWorkspace,
} from "../workspaces/workspaceMonogram";
import {
  resolveWorkspaceAccessDisplay,
  workspaceAccessLabelKey,
} from "../workspaces/workspaceAccess";
import {
  formatWorkspaceDate,
  formatWorkspaceUpdatedAt,
} from "../workspaces/presentation";
import {
  filterWorkspaces,
  readWorkspaceView,
  sortWorkspaces,
  writeWorkspaceView,
  type WorkspaceSort,
  type WorkspaceView,
} from "../workspaces/workspaceResources";

type WorkspaceFormState = {
  name: string;
  description: string;
  startDate: string;
};

type LoadState = "loading" | "ready" | "error" | "forbidden";

const EMPTY_FORM: WorkspaceFormState = {
  name: "",
  description: "",
  startDate: "",
};

export function TenantWorkspacesPage() {
  const { t, i18n } = useTranslation(["workspaces", "common"]);
  const { tenantId } = useParams();
  const { token, user } = useAuth();
  const { show } = useFeedback();
  const { tenants } = useTenantDirectory();
  const requestId = useRef(0);
  const nameInputRef = useRef<HTMLInputElement>(null);
  const editNameRef = useRef<HTMLInputElement>(null);

  const [workspaces, setWorkspaces] = useState<Workspace[]>([]);
  const [loadState, setLoadState] = useState<LoadState>("loading");
  const [retryNonce, setRetryNonce] = useState(0);
  const [search, setSearch] = useState("");
  const [sort, setSort] = useState<WorkspaceSort>("updatedDesc");
  const [view, setView] = useState<WorkspaceView>(() =>
    user ? readWorkspaceView(user.userId) : "grid",
  );
  const [createOpen, setCreateOpen] = useState(false);
  const [editing, setEditing] = useState<Workspace | null>(null);
  const [form, setForm] = useState<WorkspaceFormState>(EMPTY_FORM);
  const [nameError, setNameError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const membership = tenants.find((item) => item.tenantId === tenantId) ?? null;
  const mayCreate = canCreateWorkspace(membership);

  function accessLabelFor(workspace: Workspace): string {
    const display = resolveWorkspaceAccessDisplay(workspace, membership?.role);
    return t(workspaceAccessLabelKey(display));
  }

  const reloadWorkspaces = useCallback(() => {
    setRetryNonce((current) => current + 1);
  }, []);

  useEffect(() => {
    if (!token || !tenantId) {
      return;
    }
    const current = requestId.current + 1;
    requestId.current = current;
    // oxlint-disable-next-line react/set-state-in-effect
    setWorkspaces([]);
    // oxlint-disable-next-line react/set-state-in-effect
    setLoadState("loading");
    const controller = new AbortController();

    void (async () => {
      try {
        const workspaceList = await listWorkspaces(token, tenantId, controller.signal);
        if (!shouldApplyResponse(current, requestId.current)) {
          return;
        }
        setWorkspaces(workspaceList);
        setLoadState("ready");
      } catch (cause) {
        if (!shouldApplyResponse(current, requestId.current)) {
          return;
        }
        if (isApiError(cause) && cause.status === 403) {
          setLoadState("forbidden");
          setWorkspaces([]);
        } else if (!controller.signal.aborted) {
          setLoadState("error");
          setWorkspaces([]);
        }
      }
    })();

    return () => controller.abort();
  }, [token, tenantId, retryNonce]);

  const filtered = useMemo(
    () => sortWorkspaces(filterWorkspaces(workspaces, search), sort),
    [workspaces, search, sort],
  );

  function changeView(next: WorkspaceView) {
    setView(next);
    if (user) {
      writeWorkspaceView(user.userId, next);
    }
  }

  function resetCreate() {
    setForm(EMPTY_FORM);
    setNameError(null);
    setCreateOpen(false);
  }

  function openCreate() {
    setNameError(null);
    setForm(EMPTY_FORM);
    setCreateOpen(true);
  }

  function openEdit(workspace: Workspace) {
    setNameError(null);
    setEditing(workspace);
    setForm({
      name: workspace.name,
      description: workspace.description ?? "",
      startDate: workspace.startDate ?? "",
    });
  }

  function resetEdit() {
    setEditing(null);
    setForm(EMPTY_FORM);
    setNameError(null);
  }

  function applyMutationFailure(cause: unknown, mode: "create" | "edit") {
    if (isApiError(cause) && cause.status === 403) {
      show({
        tone: "error",
        title: t("common:feedback.errorTitle"),
        body: t("workspaces:errors.permissionDenied"),
      });
      return;
    }
    if (isApiError(cause) && cause.code === "invalid_workspace") {
      setNameError(t("common:errors.invalidInput"));
      return;
    }
    if (isApiError(cause) && cause.code === "workspace_name_conflict") {
      setNameError(
        cause.existingName
          ? t("workspaces:errors.nameConflictNamed", { existingName: cause.existingName })
          : t("workspaces:errors.nameConflict"),
      );
      return;
    }
    show({
      tone: "error",
      title: t("common:feedback.errorTitle"),
      body:
        mode === "create"
          ? t("workspaces:errors.createFailed")
          : t("workspaces:errors.updateFailed"),
    });
  }

  async function onCreate(event: FormEvent) {
    event.preventDefault();
    if (!token || !tenantId || busy) {
      return;
    }
    setBusy(true);
    setNameError(null);
    try {
      const created = await createWorkspace(token, tenantId, {
        name: form.name,
        description: form.description.trim() || null,
        startDate: form.startDate || null,
      });
      if (loadState === "ready") {
        setWorkspaces((current) => [...current, created]);
      } else {
        reloadWorkspaces();
      }
      show({
        tone: "success",
        title: t("workspaces:createdTitle"),
        body: t("workspaces:createdBody", { name: created.name }),
      });
      resetCreate();
    } catch (cause) {
      applyMutationFailure(cause, "create");
    } finally {
      setBusy(false);
    }
  }

  async function onEdit(event: FormEvent) {
    event.preventDefault();
    if (!token || !tenantId || !editing || busy) {
      return;
    }
    setBusy(true);
    setNameError(null);
    try {
      const updated = await updateWorkspace(token, tenantId, editing.workspaceId, {
        name: form.name,
        description: form.description.trim() || null,
        startDate: form.startDate || null,
      });
      setWorkspaces((current) =>
        current.map((item) => (item.workspaceId === updated.workspaceId ? updated : item)),
      );
      show({
        tone: "success",
        title: t("workspaces:updatedTitle"),
        body: t("workspaces:updatedBody", { name: updated.name }),
      });
      resetEdit();
    } catch (cause) {
      applyMutationFailure(cause, "edit");
    } finally {
      setBusy(false);
    }
  }

  const createToolbarAction = mayCreate ? (
    <button
      type="button"
      className="primary-action toolbar-primary-action"
      aria-haspopup="dialog"
      aria-expanded={createOpen}
      aria-label={t("workspaces:newWorkspace")}
      onClick={openCreate}
    >
      <span aria-hidden="true">+</span>
      {t("workspaces:newWorkspace")}
    </button>
  ) : undefined;

  const createEmptyAction = mayCreate ? (
    <button
      type="button"
      className="primary-action"
      aria-haspopup="dialog"
      aria-expanded={createOpen}
      aria-label={t("workspaces:newWorkspace")}
      onClick={openCreate}
    >
      <span aria-hidden="true">+</span>
      {t("workspaces:newWorkspace")}
    </button>
  ) : undefined;

  if (loadState === "forbidden") {
    return <StatusBanner tone="error">{t("common:errors.forbidden")}</StatusBanner>;
  }

  let mainContent: ReactNode;
  if (loadState === "loading") {
    mainContent = <p className="quiet-state">{t("common:loading")}</p>;
  } else if (loadState === "error") {
    mainContent = (
      <EmptyState
        title={t("workspaces:loadErrorTitle")}
        body={t("workspaces:loadErrorBody")}
        action={
          <button type="button" className="primary-action" onClick={reloadWorkspaces}>
            {t("workspaces:retryLoad")}
          </button>
        }
      />
    );
  } else if (workspaces.length === 0) {
    mainContent = (
      <EmptyState
        title={mayCreate ? t("workspaces:emptyTitle") : t("workspaces:emptyAssignedTitle")}
        body={mayCreate ? t("workspaces:emptyBody") : t("workspaces:emptyAssignedBody")}
        action={createEmptyAction}
      />
    );
  } else {
    mainContent = (
      <div className="resource-section">
        <label className="resource-search resource-search-row">
          <span className="sr-only">{t("workspaces:searchLabel")}</span>
          <input
            type="search"
            value={search}
            placeholder={t("workspaces:searchPlaceholder")}
            aria-label={t("workspaces:searchLabel")}
            onChange={(event) => setSearch(event.target.value)}
          />
        </label>
        <ResourceToolbar
          label={t("workspaces:resourceToolbar")}
          summary={t("workspaces:resourceSummary", { count: filtered.length })}
        >
          <label className="resource-sort">
            <span className="sr-only">{t("workspaces:sortLabel")}</span>
            <select
              aria-label={t("workspaces:sortLabel")}
              value={sort}
              onChange={(event) => setSort(event.target.value as WorkspaceSort)}
            >
              <option value="updatedDesc">{t("workspaces:sortUpdated")}</option>
              <option value="createdDesc">{t("workspaces:sortCreated")}</option>
              <option value="nameAsc">{t("workspaces:sortNameAsc")}</option>
              <option value="nameDesc">{t("workspaces:sortNameDesc")}</option>
            </select>
          </label>
          <div className="view-toggle" role="group" aria-label={t("workspaces:viewToggle")}>
            <button
              type="button"
              className={view === "grid" ? "view-toggle-active" : undefined}
              aria-pressed={view === "grid"}
              onClick={() => changeView("grid")}
            >
              <ViewToggleIcon name="grid" />
              {t("workspaces:viewGrid")}
            </button>
            <button
              type="button"
              className={view === "list" ? "view-toggle-active" : undefined}
              aria-pressed={view === "list"}
              onClick={() => changeView("list")}
            >
              <ViewToggleIcon name="list" />
              {t("workspaces:viewList")}
            </button>
          </div>
          {createToolbarAction}
        </ResourceToolbar>

        {filtered.length === 0 ? (
          <EmptyState
            title={t("workspaces:searchEmptyTitle")}
            body={t("workspaces:searchEmptyBody")}
            action={
              <button type="button" className="secondary-action" onClick={() => setSearch("")}>
                {t("workspaces:clearSearch")}
              </button>
            }
          />
        ) : view === "grid" ? (
          <ul className="workspace-grid">
            {filtered.map((workspace) => (
              <li key={workspace.workspaceId}>
                <WorkspaceCard
                  workspace={workspace}
                  tenantId={tenantId!}
                    updatedLabel={t("workspaces:updated", {
                      when: formatWorkspaceUpdatedAt(
                        workspace.updatedAtUtc ?? workspace.createdAtUtc,
                        i18n.language,
                      ),
                    })}
                    accessLabel={accessLabelFor(workspace)}
                    editLabel={t("workspaces:editWorkspace")}
                  onEdit={canManageWorkspace(workspace) ? () => openEdit(workspace) : undefined}
                />
              </li>
            ))}
          </ul>
        ) : (
          <ul className="workspace-list">
            {filtered.map((workspace) => (
              <li key={workspace.workspaceId}>
                <WorkspaceRow
                  workspace={workspace}
                  tenantId={tenantId!}
                  updatedLabel={t("workspaces:updated", {
                    when: formatWorkspaceUpdatedAt(
                      workspace.updatedAtUtc ?? workspace.createdAtUtc,
                      i18n.language,
                    ),
                  })}
                  createdLabel={t("workspaces:created", {
                    when: formatWorkspaceDate(
                      workspace.createdAtUtc.slice(0, 10),
                      i18n.language,
                    ),
                  })}
                  accessLabel={accessLabelFor(workspace)}
                  editLabel={t("workspaces:editWorkspace")}
                  onEdit={canManageWorkspace(workspace) ? () => openEdit(workspace) : undefined}
                />
              </li>
            ))}
          </ul>
        )}
      </div>
    );
  }

  return (
    <section className="app-page">
      <PageHeader title={t("workspaces:title")} description={t("workspaces:description")} />

      {mainContent}

      <Dialog
        open={createOpen}
        titleId="create-workspace-title"
        title={t("workspaces:createWorkspace")}
        closeLabel={t("common:close")}
        onClose={resetCreate}
        initialFocusRef={nameInputRef}
        size="compact"
      >
        <WorkspaceForm
          form={form}
          setForm={setForm}
          nameError={nameError}
          setNameError={setNameError}
          nameInputRef={nameInputRef}
          busy={busy}
          submitLabel={busy ? t("workspaces:creating") : t("workspaces:createWorkspace")}
          onSubmit={onCreate}
          onCancel={resetCreate}
          t={t}
        />
      </Dialog>

      <Dialog
        open={Boolean(editing)}
        titleId="edit-workspace-title"
        title={t("workspaces:editWorkspace")}
        closeLabel={t("common:close")}
        onClose={resetEdit}
        initialFocusRef={editNameRef}
        size="compact"
      >
        <WorkspaceForm
          form={form}
          setForm={setForm}
          nameError={nameError}
          setNameError={setNameError}
          nameInputRef={editNameRef}
          busy={busy}
          submitLabel={busy ? t("workspaces:saving") : t("workspaces:saveChanges")}
          onSubmit={onEdit}
          onCancel={resetEdit}
          t={t}
        />
      </Dialog>
    </section>
  );
}

function WorkspaceForm({
  form,
  setForm,
  nameError,
  setNameError,
  nameInputRef,
  busy,
  submitLabel,
  onSubmit,
  onCancel,
  t,
}: {
  form: WorkspaceFormState;
  setForm: (next: WorkspaceFormState) => void;
  nameError: string | null;
  setNameError: (next: string | null) => void;
  nameInputRef: RefObject<HTMLInputElement | null>;
  busy: boolean;
  submitLabel: string;
  onSubmit: (event: FormEvent) => void;
  onCancel: () => void;
  t: (key: string) => string;
}) {
  return (
    <form className="form-card form-card-compact" onSubmit={onSubmit}>
      <p>{t("workspaces:createDescription")}</p>
      <div className="form-fields">
        <Field id="workspace-name" label={t("workspaces:name")} error={nameError ?? undefined}>
          <input
            id="workspace-name"
            ref={nameInputRef}
            required
            maxLength={200}
            placeholder={t("workspaces:namePlaceholder")}
            value={form.name}
            onChange={(event) => {
              setForm({ ...form, name: event.target.value });
              setNameError(null);
            }}
          />
        </Field>
        <Field id="workspace-description" label={t("workspaces:descriptionLabel")}>
          <textarea
            id="workspace-description"
            maxLength={500}
            rows={3}
            placeholder={t("workspaces:descriptionPlaceholder")}
            value={form.description}
            onChange={(event) => setForm({ ...form, description: event.target.value })}
          />
        </Field>
        <Field id="workspace-start-date" label={t("workspaces:startDate")}>
          <input
            id="workspace-start-date"
            type="date"
            value={form.startDate}
            onChange={(event) => setForm({ ...form, startDate: event.target.value })}
          />
        </Field>
      </div>
      <div className="dialog-actions">
        <button className="secondary-action" type="button" onClick={onCancel}>
          {t("common:cancel")}
        </button>
        <button className="primary-action" type="submit" disabled={busy} aria-busy={busy}>
          {busy ? <span className="button-spinner" aria-hidden="true" /> : null}
          {submitLabel}
        </button>
      </div>
    </form>
  );
}

function WorkspaceCard({
  workspace,
  tenantId,
  updatedLabel,
  accessLabel,
  editLabel,
  onEdit,
}: {
  workspace: Workspace;
  tenantId: string;
  updatedLabel: string;
  accessLabel: string;
  editLabel: string;
  onEdit?: () => void;
}) {
  return (
    <div className="workspace-card-shell">
      <Link
        className="workspace-card-main"
        to={`/app/tenants/${tenantId}/workspaces/${workspace.workspaceId}`}
      >
        <WorkspaceLogo name={workspace.name} />
        <span className="workspace-card-copy">
          <strong>{workspace.name}</strong>
          {workspace.description ? <p>{workspace.description}</p> : null}
        </span>
        <span className="workspace-card-meta">
          <WorkspaceAccessBadge label={accessLabel} />
          <span>{updatedLabel}</span>
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

function WorkspaceRow({
  workspace,
  tenantId,
  updatedLabel,
  createdLabel,
  accessLabel,
  editLabel,
  onEdit,
}: {
  workspace: Workspace;
  tenantId: string;
  updatedLabel: string;
  createdLabel: string;
  accessLabel: string;
  editLabel: string;
  onEdit?: () => void;
}) {
  return (
    <div className="workspace-row">
      <Link
        className="workspace-row-main"
        to={`/app/tenants/${tenantId}/workspaces/${workspace.workspaceId}`}
      >
        <WorkspaceLogo name={workspace.name} className="workspace-row-logo" />
        <span className="workspace-row-copy">
          <strong>{workspace.name}</strong>
          {workspace.description ? <small>{workspace.description}</small> : null}
        </span>
      </Link>
      <span className="workspace-row-updated">{updatedLabel}</span>
      <span className="workspace-row-created">{createdLabel}</span>
      <span className="workspace-row-access">
        <WorkspaceAccessBadge label={accessLabel} />
      </span>
      {onEdit ? (
        <button type="button" className="org-overflow" aria-label={editLabel} onClick={onEdit}>
          <span aria-hidden="true">⋯</span>
        </button>
      ) : (
        <span className="workspace-row-spacer" aria-hidden="true" />
      )}
    </div>
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
        <path d="M2 3.5h12M2 8h12M2 12.5h12" />
      )}
    </svg>
  );
}
