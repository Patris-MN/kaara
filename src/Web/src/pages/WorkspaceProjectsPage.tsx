import { type MouseEvent, type ReactNode, useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";

import { createProject, deleteProject, getWorkspace, listProjects, updateProject } from "../api/client";
import { isApiError } from "../api/errors";
import { shouldApplyResponse } from "../api/requestIdentity";
import type { Project, Workspace } from "../api/types";
import { useAuth } from "../auth/AuthProvider";
import { ContextMenu } from "../components/ContextMenu";
import { CreateProjectDialog } from "../components/CreateProjectDialog";
import { Dialog } from "../components/Dialog";
import { EditProjectDialog } from "../components/EditProjectDialog";
import { EmptyState } from "../components/EmptyState";
import { PageHeader } from "../components/PageHeader";
import { ProjectIdentityBadge } from "../components/ProjectIdentityBadge";
import { StatusBanner } from "../components/Ui";
import { ViewToggleIcon } from "../components/ViewToggleIcon";
import { WorkspaceMemberAccessPanel } from "../components/WorkspaceMemberAccessPanel";
import { useFeedback } from "../feedback/FeedbackProvider";
import type { MemberMenuItem } from "../members/memberActions";
import {
  canCreateProject,
  canDeleteProject,
  canEditProjectMetadata,
  filterProjects,
  projectHasTasks,
  readProjectView,
  sortProjects,
  writeProjectView,
  type ProjectSort,
  type ProjectView,
} from "../projects/projectResources";
import { useTenantDirectory } from "../tenancy/TenantDirectoryProvider";

type WorkspaceTab = "projects" | "members";
type LoadState = "loading" | "ready" | "error" | "forbidden";

function stopCardNavigation(event: MouseEvent) {
  event.preventDefault();
  event.stopPropagation();
}

function ProjectActionsMenu({
  label,
  onEdit,
  onDelete,
  showDelete,
}: {
  label: string;
  onEdit: () => void;
  onDelete?: () => void;
  showDelete?: boolean;
}) {
  const { t } = useTranslation(["projects"]);
  const items: MemberMenuItem[] = [
    {
      id: "edit-project",
      label: t("projects:editProject"),
      onClick: onEdit,
    },
  ];
  if (showDelete && onDelete) {
    items.push({
      id: "delete-project",
      label: t("projects:deleteProject"),
      onClick: onDelete,
      destructive: true,
    });
  }

  return (
    <div className="project-card-menu" onClick={stopCardNavigation} onMouseDown={stopCardNavigation}>
      <ContextMenu label={label} items={items} />
    </div>
  );
}

export function WorkspaceProjectsPage() {
  const { t } = useTranslation(["projects", "tasks", "workspaces", "common"]);
  const { tenantId, workspaceId } = useParams();
  const navigate = useNavigate();
  const { token, user } = useAuth();
  const { show } = useFeedback();
  const { tenants } = useTenantDirectory();
  const requestId = useRef(0);

  const [workspace, setWorkspace] = useState<Workspace | null>(null);
  const [projects, setProjects] = useState<Project[]>([]);
  const [loadState, setLoadState] = useState<LoadState>("loading");
  const [retryNonce, setRetryNonce] = useState(0);
  const [tab, setTab] = useState<WorkspaceTab>("projects");
  const [search, setSearch] = useState("");
  const [sort, setSort] = useState<ProjectSort>("nameAsc");
  const [view, setView] = useState<ProjectView>(() => (user ? readProjectView(user.userId) : "grid"));
  const [createOpen, setCreateOpen] = useState(false);
  const [editOpen, setEditOpen] = useState(false);
  const [editingProject, setEditingProject] = useState<Project | null>(null);
  const [nameError, setNameError] = useState<string | null>(null);
  const [editNameError, setEditNameError] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [editFormError, setEditFormError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [editBusy, setEditBusy] = useState(false);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [deleteBlockedOpen, setDeleteBlockedOpen] = useState(false);
  const [deletingProject, setDeletingProject] = useState<Project | null>(null);
  const [deleteBusy, setDeleteBusy] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  const membership = tenants.find((item) => item.tenantId === tenantId) ?? null;
  const mayCreate = canCreateProject(workspace?.accessLevel);
  const mayEditMetadata = canEditProjectMetadata(membership?.role);
  const mayDeleteProject = canDeleteProject(membership?.role);
  const canManageWorkspaceAccess = Boolean(
    workspace?.canManage ?? (membership?.role === "Owner" || membership?.role === "Admin"),
  );

  const reload = () => setRetryNonce((current) => current + 1);

  function changeView(next: ProjectView) {
    setView(next);
    if (user) {
      writeProjectView(user.userId, next);
    }
  }

  useEffect(() => {
    if (!token || !tenantId || !workspaceId) {
      return;
    }
    const current = requestId.current + 1;
    requestId.current = current;
    setWorkspace(null);
    setProjects([]);
    setLoadState("loading");
    setError(null);
    const controller = new AbortController();

    void (async () => {
      try {
        const [nextWorkspace, items] = await Promise.all([
          getWorkspace(token, tenantId, workspaceId, controller.signal),
          listProjects(token, tenantId, workspaceId, controller.signal),
        ]);
        if (!shouldApplyResponse(current, requestId.current)) {
          return;
        }
        setWorkspace(nextWorkspace);
        setProjects(items);
        setLoadState("ready");
      } catch (cause: unknown) {
        if (!shouldApplyResponse(current, requestId.current)) {
          return;
        }
        if (isApiError(cause) && cause.status === 403) {
          setLoadState("forbidden");
          return;
        }
        if (isApiError(cause) && (cause.status === 404 || cause.code === "workspace_not_found")) {
          setLoadState("error");
          setError(t("common:errors.workspace_not_found"));
          return;
        }
        setLoadState("error");
        setError(t("projects:loadErrorBody"));
      }
    })();

    return () => controller.abort();
  }, [token, tenantId, workspaceId, retryNonce, t]);

  const filtered = useMemo(
    () => sortProjects(filterProjects(projects, search), sort),
    [projects, search, sort],
  );

  const hasSearch = search.trim().length > 0;
  const isEmptyWorkspace = projects.length === 0;
  const showProjectToolbar = !isEmptyWorkspace && loadState === "ready";

  function openCreateDialog() {
    setNameError(null);
    setFormError(null);
    setCreateOpen(true);
  }

  function openEditDialog(project: Project) {
    setEditingProject(project);
    setEditNameError(null);
    setEditFormError(null);
    setEditOpen(true);
  }

  function openDeleteDialog(project: Project) {
    setDeletingProject(project);
    setDeleteError(null);
    if (projectHasTasks(project)) {
      setDeleteBlockedOpen(true);
      return;
    }
    setDeleteOpen(true);
  }

  async function onDeleteProject() {
    if (!token || !tenantId || !workspaceId || !deletingProject || deleteBusy || !mayDeleteProject) {
      return;
    }
    setDeleteError(null);
    setDeleteBusy(true);
    try {
      await deleteProject(token, tenantId, workspaceId, deletingProject.projectId);
      setProjects((current) =>
        current.filter((project) => project.projectId !== deletingProject.projectId),
      );
      setDeleteOpen(false);
      setDeletingProject(null);
      show({
        tone: "success",
        title: t("projects:deletedTitle"),
        body: t("projects:deletedBody", { name: deletingProject.name }),
      });
    } catch (cause) {
      if (isApiError(cause) && cause.code === "project_has_tasks") {
        setDeleteOpen(false);
        setDeleteBlockedOpen(true);
        return;
      }
      if (
        isApiError(cause) &&
        (cause.code === "project_delete_forbidden" || cause.status === 403)
      ) {
        setDeleteError(t("projects:errors.deleteForbidden"));
        return;
      }
      if (isApiError(cause) && cause.code === "tenant_access_denied") {
        setDeleteError(t("projects:errors.permissionsChanged"));
        return;
      }
      setDeleteError(t("projects:errors.deleteFailed"));
    } finally {
      setDeleteBusy(false);
    }
  }

  async function onCreateProject(payload: {
    name: string;
    description: string;
    accentToken: string;
  }) {
    if (!token || !tenantId || !workspaceId || busy || !mayCreate) {
      return;
    }
    setNameError(null);
    setFormError(null);

    if (!payload.name.trim()) {
      setNameError(t("projects:errors.nameRequired"));
      return;
    }

    setBusy(true);
    try {
      const created = await createProject(token, tenantId, workspaceId, {
        name: payload.name,
        description: payload.description || null,
        accentToken: payload.accentToken,
      });
      setProjects((current) => [...current, created]);
      setCreateOpen(false);
      show({
        tone: "success",
        title: t("projects:createdTitle"),
        body: t("projects:createdBody", { name: created.name }),
      });
      navigate(`/app/tenants/${tenantId}/workspaces/${workspaceId}/projects/${created.projectId}`);
    } catch (cause) {
      if (isApiError(cause) && cause.code === "project_name_conflict") {
        setNameError(
          cause.existingName
            ? t("projects:errors.nameConflictNamed", { existingName: cause.existingName })
            : t("projects:errors.nameConflict"),
        );
        return;
      }
      setFormError(t("projects:errors.createFailed"));
    } finally {
      setBusy(false);
    }
  }

  async function onUpdateProject(payload: {
    name: string;
    description: string;
    accentToken: string;
  }) {
    if (!token || !tenantId || !workspaceId || !editingProject || editBusy || !mayEditMetadata) {
      return;
    }
    setEditNameError(null);
    setEditFormError(null);

    if (!payload.name.trim()) {
      setEditNameError(t("projects:errors.nameRequired"));
      return;
    }

    setEditBusy(true);
    try {
      const updated = await updateProject(
        token,
        tenantId,
        workspaceId,
        editingProject.projectId,
        {
          name: payload.name,
          description: payload.description || null,
          accentToken: payload.accentToken,
        },
      );
      setProjects((current) =>
        current.map((project) =>
          project.projectId === updated.projectId ? updated : project,
        ),
      );
      setEditOpen(false);
      setEditingProject(null);
      show({
        tone: "success",
        title: t("projects:updatedTitle"),
        body: t("projects:updatedBody", { name: updated.name }),
      });
    } catch (cause) {
      if (isApiError(cause) && cause.code === "project_name_conflict") {
        setEditNameError(
          cause.existingName
            ? t("projects:errors.nameConflictNamed", { existingName: cause.existingName })
            : t("projects:errors.nameConflict"),
        );
        return;
      }
      if (
        isApiError(cause) &&
        (cause.code === "project_metadata_edit_forbidden" || cause.status === 403)
      ) {
        setEditFormError(t("projects:errors.editForbidden"));
        return;
      }
      if (isApiError(cause) && cause.code === "tenant_access_denied") {
        setEditFormError(t("projects:errors.permissionsChanged"));
        return;
      }
      setEditFormError(t("projects:errors.updateFailed"));
    } finally {
      setEditBusy(false);
    }
  }

  const createAction = mayCreate ? (
    <button
      type="button"
      className="primary-action toolbar-primary-action"
      aria-haspopup="dialog"
      aria-expanded={createOpen}
      onClick={openCreateDialog}
    >
      <span aria-hidden="true">+</span>
      {t("projects:newProject")}
    </button>
  ) : null;

  const projectToolbar = showProjectToolbar ? (
    <div className="workspace-projects-toolbar" role="toolbar" aria-label={t("projects:resourceToolbar")}>
      <label className="resource-search">
        <span className="sr-only">{t("projects:searchLabel")}</span>
        <input
          type="search"
          value={search}
          placeholder={t("projects:searchPlaceholder")}
          aria-label={t("projects:searchLabel")}
          onChange={(event) => setSearch(event.target.value)}
        />
      </label>
      <div className="workspace-projects-toolbar-controls">
        <label className="resource-sort">
          <span className="sr-only">{t("projects:sortLabel")}</span>
          <select
            aria-label={t("projects:sortLabel")}
            value={sort}
            onChange={(event) => setSort(event.target.value as ProjectSort)}
          >
            <option value="nameAsc">{t("projects:sortNameAsc")}</option>
            <option value="nameDesc">{t("projects:sortNameDesc")}</option>
            <option value="openTasksDesc">{t("projects:sortOpenTasks")}</option>
            <option value="createdDesc">{t("projects:sortCreated")}</option>
          </select>
        </label>
        <div className="view-toggle" role="group" aria-label={t("projects:viewToggle")}>
          <button
            type="button"
            className={view === "grid" ? "view-toggle-active" : undefined}
            aria-pressed={view === "grid"}
            aria-label={t("projects:viewGridAccessible")}
            onClick={() => changeView("grid")}
          >
            <ViewToggleIcon name="grid" />
            {t("projects:viewGrid")}
          </button>
          <button
            type="button"
            className={view === "list" ? "view-toggle-active" : undefined}
            aria-pressed={view === "list"}
            aria-label={t("projects:viewListAccessible")}
            onClick={() => changeView("list")}
          >
            <ViewToggleIcon name="list" />
            {t("projects:viewList")}
          </button>
        </div>
        {createAction}
      </div>
    </div>
  ) : null;

  if (loadState === "forbidden") {
    return <StatusBanner tone="error">{t("common:errors.forbidden")}</StatusBanner>;
  }

  if (loadState === "error" && !workspace) {
    return (
      <EmptyState
        title={t("projects:loadErrorTitle")}
        body={error ?? t("projects:loadErrorBody")}
        action={
          <button type="button" className="primary-action" onClick={reload}>
            {t("projects:retryLoad")}
          </button>
        }
      />
    );
  }

  let tabContent: ReactNode;
  if (tab === "members") {
    tabContent =
      token && tenantId && workspaceId ? (
        <WorkspaceMemberAccessPanel
          token={token}
          tenantId={tenantId}
          workspaceId={workspaceId}
          canManage={canManageWorkspaceAccess}
        />
      ) : null;
  } else if (loadState === "loading") {
    tabContent = <p className="quiet-state">{t("common:loading")}</p>;
  } else {
    tabContent = (
      <div className="resource-section workspace-projects-content">
        {workspace?.accessLevel === "View" ? (
          <StatusBanner tone="info">{t("projects:viewOnlyWorkspace")}</StatusBanner>
        ) : null}
        {projectToolbar}

        {filtered.length === 0 ? (
          <EmptyState
            compact
            icon="project"
            title={hasSearch ? t("projects:searchEmptyTitle") : t("projects:emptyTitle")}
            body={
              hasSearch
                ? t("projects:searchEmptyBody")
                : mayCreate
                  ? t("projects:emptyBodyCreate")
                  : t("projects:emptyBodyViewOnly")
            }
            secondary={!hasSearch && mayCreate ? t("projects:emptySecondaryCreate") : undefined}
            action={
              hasSearch ? (
                <button type="button" className="secondary-action" onClick={() => setSearch("")}>
                  {t("projects:clearSearch")}
                </button>
              ) : mayCreate ? (
                createAction
              ) : undefined
            }
          />
        ) : view === "list" ? (
          <div className="workspace-project-table-wrap">
            <table className="resource-table workspace-project-table">
              <thead>
                <tr>
                  <th scope="col">{t("projects:listColumns.project")}</th>
                  <th scope="col">{t("projects:listColumns.status")}</th>
                  <th scope="col">{t("projects:listColumns.openTasks")}</th>
                  {mayEditMetadata ? (
                    <th scope="col" className="project-table-actions-col">
                      <span className="sr-only">{t("projects:listColumns.actions")}</span>
                    </th>
                  ) : null}
                </tr>
              </thead>
              <tbody>
                {filtered.map((project) => (
                  <tr key={project.projectId}>
                    <td>
                      <Link
                        className="project-row-link"
                        to={`/app/tenants/${tenantId}/workspaces/${workspaceId}/projects/${project.projectId}`}
                      >
                        <ProjectIdentityBadge name={project.name} accentToken={project.accentToken} />
                        <span className="project-row-copy">
                          <strong>{project.name}</strong>
                          {project.description ? (
                            <span className="table-secondary">{project.description}</span>
                          ) : null}
                        </span>
                      </Link>
                    </td>
                    <td>{t("projects:active")}</td>
                    <td>{project.openTaskCount}</td>
                    {mayEditMetadata ? (
                      <td className="project-table-actions-col">
                        <ProjectActionsMenu
                          label={t("projects:projectActions", { name: project.name })}
                          onEdit={() => openEditDialog(project)}
                          showDelete={mayDeleteProject}
                          onDelete={() => openDeleteDialog(project)}
                        />
                      </td>
                    ) : null}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <ul className="project-card-grid">
            {filtered.map((project) => (
              <li className="project-card" key={project.projectId}>
                <div className="project-card-header-meta">
                  <span className="status-pill">{t("projects:active")}</span>
                  {mayEditMetadata ? (
                    <ProjectActionsMenu
                      label={t("projects:projectActions", { name: project.name })}
                      onEdit={() => openEditDialog(project)}
                      showDelete={mayDeleteProject}
                      onDelete={() => openDeleteDialog(project)}
                    />
                  ) : null}
                </div>
                <Link
                  className="project-card-link"
                  to={`/app/tenants/${tenantId}/workspaces/${workspaceId}/projects/${project.projectId}`}
                >
                  <div className="project-card-top">
                    <ProjectIdentityBadge name={project.name} accentToken={project.accentToken} />
                  </div>
                  <strong>{project.name}</strong>
                  <p>{project.description || t("projects:projectDescription")}</p>
                  <div className="project-card-footer">
                    <span>{t("projects:openTasksCount", { count: project.openTaskCount })}</span>
                    <span aria-hidden="true">→</span>
                  </div>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </div>
    );
  }

  const breadcrumb = membership
    ? `${membership.name} / ${t("workspaces:title")} / ${workspace?.name ?? ""}`
    : workspace?.name ?? "";

  return (
    <section className="app-page workspace-projects-page">
      <PageHeader
        eyebrow={breadcrumb}
        title={workspace?.name ?? t("projects:title")}
        description={
          loadState === "ready"
            ? `${t("projects:workspacePageDescription")} · ${t("projects:resourceSummary", { count: projects.length })}`
            : t("projects:workspacePageDescription")
        }
        action={isEmptyWorkspace && loadState === "ready" && mayCreate ? createAction : undefined}
      />

      <div className="member-page-tabs workspace-page-tabs" role="tablist" aria-label={t("projects:workspaceTabsLabel")}>
        <button
          type="button"
          role="tab"
          id="workspace-tab-projects"
          aria-selected={tab === "projects"}
          aria-controls="workspace-panel"
          className={tab === "projects" ? "member-page-tab-active" : undefined}
          onClick={() => setTab("projects")}
        >
          {t("projects:workspaceTabs.projects")}
        </button>
        <button
          type="button"
          role="tab"
          id="workspace-tab-members"
          aria-selected={tab === "members"}
          aria-controls="workspace-panel"
          className={tab === "members" ? "member-page-tab-active" : undefined}
          onClick={() => setTab("members")}
        >
          {t("projects:workspaceTabs.membersAccess")}
        </button>
      </div>

      <div
        id="workspace-panel"
        role="tabpanel"
        aria-labelledby={tab === "projects" ? "workspace-tab-projects" : "workspace-tab-members"}
      >
        {tabContent}
      </div>

      <CreateProjectDialog
        open={createOpen}
        busy={busy}
        nameError={nameError}
        formError={formError}
        onClose={() => setCreateOpen(false)}
        onSubmit={onCreateProject}
      />

      <EditProjectDialog
        open={editOpen}
        busy={editBusy}
        project={editingProject}
        nameError={editNameError}
        formError={editFormError}
        onClose={() => {
          setEditOpen(false);
          setEditingProject(null);
        }}
        onSubmit={onUpdateProject}
      />

      <Dialog
        open={deleteOpen}
        size="compact"
        titleId="delete-project-title"
        title={t("projects:deleteConfirmTitle")}
        closeLabel={t("common:close")}
        onClose={() => {
          setDeleteOpen(false);
          setDeletingProject(null);
          setDeleteError(null);
        }}
      >
        <p>
          {deletingProject
            ? t("projects:deleteConfirmNamed", { name: deletingProject.name })
            : t("projects:deleteConfirmBody")}
        </p>
        {deleteError ? <StatusBanner tone="error">{deleteError}</StatusBanner> : null}
        <div className="task-actions">
          <button
            type="button"
            className="secondary-action"
            disabled={deleteBusy}
            onClick={() => {
              setDeleteOpen(false);
              setDeletingProject(null);
              setDeleteError(null);
            }}
          >
            {t("projects:cancelDelete")}
          </button>
          <button
            type="button"
            className="secondary-action context-menu-item-destructive-action"
            disabled={deleteBusy}
            onClick={() => void onDeleteProject()}
          >
            {deleteBusy ? t("projects:deleting") : t("projects:deleteProject")}
          </button>
        </div>
      </Dialog>

      <Dialog
        open={deleteBlockedOpen}
        size="compact"
        titleId="delete-project-blocked-title"
        title={t("projects:deleteBlockedTitle")}
        closeLabel={t("common:close")}
        onClose={() => {
          setDeleteBlockedOpen(false);
          setDeletingProject(null);
        }}
      >
        <p>
          {deletingProject
            ? t("projects:deleteBlockedBody", {
                name: deletingProject.name,
                count: deletingProject.taskCount ?? 0,
              })
            : t("projects:deleteBlockedBodyGeneric")}
        </p>
        <div className="task-actions">
          <button
            type="button"
            className="secondary-action"
            onClick={() => {
              setDeleteBlockedOpen(false);
              setDeletingProject(null);
            }}
          >
            {t("common:close")}
          </button>
          {deletingProject && tenantId && workspaceId ? (
            <Link
              className="primary-action"
              to={`/app/tenants/${tenantId}/workspaces/${workspaceId}/projects/${deletingProject.projectId}`}
              onClick={() => {
                setDeleteBlockedOpen(false);
                setDeletingProject(null);
              }}
            >
              {t("projects:deleteBlockedGoToTasks")}
            </Link>
          ) : null}
        </div>
      </Dialog>
    </section>
  );
}
