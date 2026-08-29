import { type Dispatch, type FormEvent, type SetStateAction, useEffect, useRef, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";

import {
  createTask,
  createTaskComment,
  deleteTask,
  deleteTaskComment,
  getProject,
  getTask,
  getWorkspace,
  listAssignableMembers,
  listTaskActivity,
  listTaskComments,
  listTasks,
  listWorkspaceTags,
  updateTask,
  updateTaskComment,
} from "../api/client";
import { isApiError, translationKeyForApiError } from "../api/errors";
import { shouldApplyResponse } from "../api/requestIdentity";
import type {
  AssignableMember,
  Project,
  TaskCapabilities,
  TaskPriority,
  TaskStatus,
  WorkTag,
  WorkTask,
  WorkTaskActivity,
  WorkTaskComment,
  Workspace,
} from "../api/types";
import { useAuth } from "../auth/AuthProvider";
import { Dialog } from "../components/Dialog";
import { Field, StatusBanner } from "../components/Ui";
import { TaskDeadlineField } from "../tasks/TaskDeadlineField";
import { TaskPriorityBadge, TaskPriorityField } from "../tasks/TaskPriorityField";
import { formatDateTimeUtc, formatTaskDate, isTaskOverdue, normalizePriority } from "../tasks/presentation";

const STATUSES: TaskStatus[] = ["Open", "InProgress", "Waiting", "Resolved", "Closed"];

const emptyDraft = {
  title: "",
  description: "",
  status: "Open" as TaskStatus,
  priority: "Normal" as TaskPriority,
  dueDate: "",
  assigneeMembershipId: "",
  tagIds: [] as string[],
  newTag: "",
};

function resolveCapabilities(task: WorkTask, accessLevel: Workspace["accessLevel"] | undefined): TaskCapabilities {
  if (task.capabilities) {
    return task.capabilities;
  }

  const canEdit = accessLevel === "Edit";
  return {
    canEditDefinition: canEdit,
    canManageTags: canEdit,
    canReassign: canEdit,
    canComment: true,
    canDelete: canEdit,
    allowedStatuses: canEdit ? [...STATUSES] : [task.status],
  };
}

function projectPath(tenantId: string, workspaceId: string, projectId: string) {
  return `/app/tenants/${tenantId}/workspaces/${workspaceId}/projects/${projectId}`;
}

export function ProjectTasksPage() {
  const { t, i18n } = useTranslation(["tasks", "common"]);
  const { tenantId, workspaceId, projectId, "*": splat } = useParams();
  const routeTaskId = splat?.startsWith("tasks/") ? splat.slice("tasks/".length) : undefined;
  const navigate = useNavigate();
  const { token } = useAuth();
  const requestId = useRef(0);
  const activityRef = useRef<HTMLElement | null>(null);
  const [workspace, setWorkspace] = useState<Workspace | null>(null);
  const [project, setProject] = useState<Project | null>(null);
  const [assignable, setAssignable] = useState<AssignableMember[]>([]);
  const [availableTags, setAvailableTags] = useState<WorkTag[]>([]);
  const [tasks, setTasks] = useState<WorkTask[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [createDraft, setCreateDraft] = useState(emptyDraft);
  const [draft, setDraft] = useState(emptyDraft);
  const [describeOpen, setDescribeOpen] = useState(false);
  const [comments, setComments] = useState<WorkTaskComment[]>([]);
  const [activity, setActivity] = useState<WorkTaskActivity[]>([]);
  const [commentBody, setCommentBody] = useState("");
  const [editingCommentId, setEditingCommentId] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [pendingAssigneeId, setPendingAssigneeId] = useState<string | null>(null);
  const [commentsOpen, setCommentsOpen] = useState(true);
  const [activityOpen, setActivityOpen] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [forbidden, setForbidden] = useState(false);
  const [notFound, setNotFound] = useState(false);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!token || !tenantId || !workspaceId || !projectId) {
      return;
    }
    const current = requestId.current + 1;
    requestId.current = current;
    setWorkspace(null);
    setProject(null);
    setAssignable([]);
    setAvailableTags([]);
    setTasks([]);
    setSelectedId(null);
    setCreateDraft(emptyDraft);
    setDraft(emptyDraft);
    setDescribeOpen(false);
    setComments([]);
    setActivity([]);
    setCommentBody("");
    setEditingCommentId(null);
    setConfirmDelete(false);
    setPendingAssigneeId(null);
    setForbidden(false);
    setNotFound(false);
    setError(null);
    setLoading(true);
    const controller = new AbortController();

    void (async () => {
      try {
        const [nextWorkspace, nextProject, items, members, tags] = await Promise.all([
          getWorkspace(token, tenantId, workspaceId, controller.signal),
          getProject(token, tenantId, workspaceId, projectId, controller.signal),
          listTasks(token, tenantId, workspaceId, projectId, controller.signal),
          listAssignableMembers(token, tenantId, workspaceId, controller.signal),
          listWorkspaceTags(token, tenantId, workspaceId, controller.signal),
        ]);
        if (!shouldApplyResponse(current, requestId.current)) {
          return;
        }
        setWorkspace(nextWorkspace);
        setProject(nextProject);
        setTasks(items);
        setAssignable(members);
        setAvailableTags(tags);
        setLoading(false);
      } catch (cause: unknown) {
        if (!shouldApplyResponse(current, requestId.current)) {
          return;
        }
        if (isApiError(cause) && cause.status === 403) {
          setForbidden(true);
          setLoading(false);
          return;
        }
        if (
          isApiError(cause) &&
          (cause.status === 404 ||
            cause.code === "workspace_not_found" ||
            cause.code === "project_not_found" ||
            cause.code === "task_not_found")
        ) {
          setNotFound(true);
          setLoading(false);
          return;
        }
        setError(t(translationKeyForApiError(cause), { ns: "common" }));
        setLoading(false);
      }
    })();

    return () => controller.abort();
  }, [token, tenantId, workspaceId, projectId, t]);

  const canCreate = workspace?.accessLevel === "Edit";
  const selected = tasks.find((task) => task.taskId === selectedId) ?? null;
  const capabilities = selected ? resolveCapabilities(selected, workspace?.accessLevel) : null;
  const canSave =
    Boolean(capabilities?.canEditDefinition || capabilities?.canManageTags || capabilities?.canReassign) ||
    Boolean(capabilities && capabilities.allowedStatuses.some((status) => status !== selected?.status));

  useEffect(() => {
    if (!routeTaskId || loading) {
      if (!routeTaskId) {
        setSelectedId(null);
      }
      return;
    }
    const task = tasks.find((item) => item.taskId === routeTaskId);
    if (task && selectedId !== routeTaskId) {
      void loadTask(task);
    }
  }, [routeTaskId, tasks, loading, selectedId]);

  function statusLabel(status: TaskStatus) {
    return t(`tasks:status.${status === "InProgress" ? "inProgress" : status.toLowerCase()}`);
  }

  function statusOptions(task: WorkTask, caps: TaskCapabilities) {
    const values = new Set<TaskStatus>([task.status, ...caps.allowedStatuses]);
    return STATUSES.filter((status) => values.has(status));
  }

  function applyDraft(task: WorkTask) {
    setDraft({
      title: task.title,
      description: task.description ?? "",
      status: task.status,
      priority: normalizePriority(task.priority),
      dueDate: task.dueDate ?? "",
      assigneeMembershipId: task.assigneeMembershipId ?? "",
      tagIds: task.tags?.map((tag) => tag.tagId) ?? [],
      newTag: "",
    });
  }

  function listPath() {
    if (!tenantId || !workspaceId || !projectId) {
      return "/app";
    }
    return projectPath(tenantId, workspaceId, projectId);
  }

  function resetCreate() {
    setCreateDraft(emptyDraft);
    setDescribeOpen(false);
    setCreateOpen(false);
  }

  async function loadTask(task: WorkTask) {
    setSelectedId(task.taskId);
    applyDraft(task);
    setConfirmDelete(false);
    setPendingAssigneeId(null);
    setCommentBody("");
    setEditingCommentId(null);
    setCommentsOpen(true);
    setActivityOpen(task.unseenActivityCount > 0);
    if (!token || !tenantId || !workspaceId || !projectId) {
      return;
    }
    try {
      const [fresh, nextComments, nextActivity] = await Promise.all([
        getTask(token, tenantId, workspaceId, projectId, task.taskId),
        listTaskComments(token, tenantId, workspaceId, projectId, task.taskId),
        listTaskActivity(token, tenantId, workspaceId, projectId, task.taskId),
      ]);
      setTasks((current) => current.map((item) => (item.taskId === fresh.taskId ? fresh : item)));
      applyDraft(fresh);
      setComments(nextComments);
      setActivity(nextActivity);
      setActivityOpen(fresh.unseenActivityCount > 0);
    } catch (cause) {
      if (isApiError(cause) && cause.status === 404) {
        setComments([]);
        setActivity([]);
        return;
      }
      setError(t(translationKeyForApiError(cause), { ns: "common" }));
    }
  }

  function openTask(task: WorkTask) {
    if (!tenantId || !workspaceId || !projectId) {
      return;
    }
    navigate(`${projectPath(tenantId, workspaceId, projectId)}/tasks/${task.taskId}`);
  }

  function closeDetail() {
    setSelectedId(null);
    setPendingAssigneeId(null);
    setConfirmDelete(false);
    navigate(listPath());
  }

  async function onCreate(event: FormEvent) {
    event.preventDefault();
    if (!token || !tenantId || !workspaceId || !projectId || busy) {
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const created = await createTask(token, tenantId, workspaceId, projectId, {
        title: createDraft.title,
        description: createDraft.description || undefined,
        status: createDraft.status,
        priority: createDraft.priority,
        dueDate: createDraft.dueDate || null,
        assigneeMembershipId: createDraft.assigneeMembershipId || null,
        tagIds: createDraft.tagIds,
        newTags: createDraft.newTag.trim() ? [createDraft.newTag.trim()] : undefined,
      });
      setTasks((current) => [...current, created]);
      if (created.tags) {
        setAvailableTags((current) => mergeTags(current, created.tags));
      }
      resetCreate();
    } catch (cause) {
      setError(t(translationKeyForApiError(cause), { ns: "common" }));
    } finally {
      setBusy(false);
    }
  }

  async function persistTask(nextDraft: typeof emptyDraft) {
    if (!token || !tenantId || !workspaceId || !projectId || !selected || !capabilities) {
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const updated = await updateTask(token, tenantId, workspaceId, projectId, selected.taskId, {
        title: nextDraft.title,
        description: nextDraft.description || null,
        status: nextDraft.status,
        priority: nextDraft.priority,
        dueDate: nextDraft.dueDate || null,
        assigneeMembershipId: nextDraft.assigneeMembershipId || null,
        tagIds: capabilities.canManageTags ? nextDraft.tagIds : undefined,
        newTags: capabilities.canManageTags && nextDraft.newTag.trim() ? [nextDraft.newTag.trim()] : undefined,
      });
      setTasks((current) => current.map((task) => (task.taskId === updated.taskId ? updated : task)));
      applyDraft(updated);
      if (updated.tags) {
        setAvailableTags((current) => mergeTags(current, updated.tags));
      }
      const nextActivity = await listTaskActivity(token, tenantId, workspaceId, projectId, updated.taskId);
      setActivity(nextActivity);
      return updated;
    } catch (cause) {
      setError(t(translationKeyForApiError(cause), { ns: "common" }));
      if (isApiError(cause) && (cause.status === 403 || cause.status === 409)) {
        await loadTask(selected);
      }
      return null;
    } finally {
      setBusy(false);
    }
  }

  async function onSave(event: FormEvent) {
    event.preventDefault();
    if (busy) {
      return;
    }
    await persistTask(draft);
  }

  async function onConfirmHandoff() {
    if (pendingAssigneeId === null) {
      return;
    }
    const nextDraft = { ...draft, assigneeMembershipId: pendingAssigneeId };
    setDraft(nextDraft);
    setPendingAssigneeId(null);
    await persistTask(nextDraft);
  }

  async function onDelete() {
    if (!token || !tenantId || !workspaceId || !projectId || !selected || busy) {
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await deleteTask(token, tenantId, workspaceId, projectId, selected.taskId);
      setTasks((current) => current.filter((task) => task.taskId !== selected.taskId));
      closeDetail();
    } catch (cause) {
      setError(t(translationKeyForApiError(cause), { ns: "common" }));
    } finally {
      setBusy(false);
    }
  }

  async function onAddComment(event: FormEvent) {
    event.preventDefault();
    if (!token || !tenantId || !workspaceId || !projectId || !selected || busy || !commentBody.trim()) {
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const created = await createTaskComment(
        token,
        tenantId,
        workspaceId,
        projectId,
        selected.taskId,
        commentBody.trim(),
      );
      setComments((current) => [...current, created]);
      setCommentBody("");
      const nextActivity = await listTaskActivity(token, tenantId, workspaceId, projectId, selected.taskId);
      setActivity(nextActivity);
    } catch (cause) {
      setError(t(translationKeyForApiError(cause), { ns: "common" }));
    } finally {
      setBusy(false);
    }
  }

  async function onSaveComment(comment: WorkTaskComment) {
    if (!token || !tenantId || !workspaceId || !projectId || !selected || busy) {
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const updated = await updateTaskComment(
        token,
        tenantId,
        workspaceId,
        projectId,
        selected.taskId,
        comment.commentId,
        comment.body,
      );
      setComments((current) => current.map((item) => (item.commentId === updated.commentId ? updated : item)));
      setEditingCommentId(null);
      const nextActivity = await listTaskActivity(token, tenantId, workspaceId, projectId, selected.taskId);
      setActivity(nextActivity);
    } catch (cause) {
      setError(t(translationKeyForApiError(cause), { ns: "common" }));
    } finally {
      setBusy(false);
    }
  }

  async function onDeleteComment(commentId: string) {
    if (!token || !tenantId || !workspaceId || !projectId || !selected || busy) {
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await deleteTaskComment(token, tenantId, workspaceId, projectId, selected.taskId, commentId);
      setComments((current) => current.filter((item) => item.commentId !== commentId));
      const nextActivity = await listTaskActivity(token, tenantId, workspaceId, projectId, selected.taskId);
      setActivity(nextActivity);
    } catch (cause) {
      setError(t(translationKeyForApiError(cause), { ns: "common" }));
    } finally {
      setBusy(false);
    }
  }

  function mergeTags(current: WorkTag[], incoming: WorkTag[]) {
    const next = [...current];
    for (const tag of incoming) {
      if (!next.some((item) => item.tagId === tag.tagId)) {
        next.push(tag);
      }
    }
    return next;
  }

  function assigneeLabel(task: WorkTask) {
    return task.assigneeDisplayName || task.assigneeEmail || t("tasks:unassigned");
  }

  function pendingAssigneeName() {
    if (pendingAssigneeId === "") {
      return t("tasks:unassigned");
    }
    const member = assignable.find((item) => item.membershipId === pendingAssigneeId);
    return member?.displayName || t("tasks:unassigned");
  }

  function showChanges() {
    setActivityOpen(true);
    window.requestAnimationFrame(() => {
      activityRef.current?.scrollIntoView({ behavior: "smooth", block: "start" });
    });
    if (selected) {
      setTasks((current) =>
        current.map((item) => (item.taskId === selected.taskId ? { ...item, unseenActivityCount: 0 } : item)),
      );
    }
  }

  if (forbidden) {
    return <StatusBanner tone="error">{t("common:errors.forbidden")}</StatusBanner>;
  }

  if (notFound) {
    return <StatusBanner tone="error">{t("common:errors.project_not_found")}</StatusBanner>;
  }

  return (
    <section className="app-page task-list-page">
      <header className="page-heading">
        <div>
          <p className="page-eyebrow">{t("tasks:eyebrow")}</p>
          <h1>{project?.name ?? t("tasks:title")}</h1>
          <p>{t("tasks:description")}</p>
        </div>
        <div className="page-stat">
          <strong>{tasks.length}</strong>
          <span>{t("tasks:taskCount")}</span>
        </div>
      </header>
      {error && !createOpen && !selected ? <StatusBanner tone="error">{error}</StatusBanner> : null}
      {loading ? <StatusBanner tone="info">{t("common:loading")}</StatusBanner> : null}
      {workspace?.accessLevel === "View" ? (
        <StatusBanner tone="info">{t("tasks:viewOnly")}</StatusBanner>
      ) : null}

      {canCreate && !createOpen ? (
        <div className="task-page-toolbar">
          <button className="primary-action" type="button" onClick={() => setCreateOpen(true)}>
            {t("tasks:create")}
          </button>
        </div>
      ) : null}

      <div className="surface-card entity-section">
        <div className="card-heading card-heading-between">
          <div>
            <h2>{t("tasks:list")}</h2>
            <p>{t("tasks:listDescription")}</p>
          </div>
          <span className="count-badge">{tasks.length}</span>
        </div>
        {loading ? (
          <p>{t("common:loading")}</p>
        ) : tasks.length === 0 ? (
          <div className="empty-state">
            <span className="empty-state-icon" aria-hidden="true">✓</span>
            <strong>{t("tasks:emptyTitle")}</strong>
            <p>{t("tasks:empty")}</p>
          </div>
        ) : (
          <ul className="task-list">
            {tasks.map((task) => (
              <li key={task.taskId}>
                <button
                  type="button"
                  id={`task-row-${task.taskId}`}
                  className={`task-row ${selectedId === task.taskId ? "task-row-active" : ""}`}
                  onClick={() => openTask(task)}
                >
                  <span className="entity-copy">
                    <strong>{task.title}</strong>
                    <span className="task-row-meta">
                      <TaskPriorityBadge priority={task.priority} />
                      <span>{statusLabel(task.status)}</span>
                      <span>{assigneeLabel(task)}</span>
                      {task.tags?.slice(0, 3).map((tag) => (
                        <span key={tag.tagId} className="task-tag-chip task-tag-chip-static">
                          {tag.name}
                        </span>
                      ))}
                      {task.dueDate ? (
                        <time dateTime={task.dueDate}>
                          {t("tasks:due", { date: formatTaskDate(task.dueDate) })}
                        </time>
                      ) : null}
                      {isTaskOverdue(task.dueDate, task.status) ? (
                        <span className="task-overdue">{t("tasks:deadline.overdue")}</span>
                      ) : null}
                      {task.unseenActivityCount > 0 ? (
                        <span className="task-unseen">
                          {t("tasks:newChanges", { count: task.unseenActivityCount })}
                        </span>
                      ) : null}
                    </span>
                  </span>
                  <span className="entity-arrow" aria-hidden="true">→</span>
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>

      <Dialog
        open={createOpen}
        titleId="create-task-title"
        title={t("tasks:create")}
        closeLabel={t("common:close")}
        onClose={resetCreate}
      >
        {error ? <StatusBanner tone="error">{error}</StatusBanner> : null}
        <form className="form-card task-create-card" onSubmit={onCreate}>
          <p>{t("tasks:createDescription")}</p>
          <div className="form-fields">
            <Field id="task-title" label={t("tasks:fields.title")}>
              <input
                id="task-title"
                className="task-title-input"
                required
                maxLength={200}
                placeholder={t("tasks:fields.titlePlaceholder")}
                value={createDraft.title}
                onChange={(event) =>
                  setCreateDraft((current) => ({ ...current, title: event.target.value }))
                }
              />
            </Field>
            {describeOpen || createDraft.description ? (
              <Field id="task-description" label={t("tasks:fields.description")}>
                <textarea
                  id="task-description"
                  maxLength={4000}
                  placeholder={t("tasks:fields.descriptionPlaceholder")}
                  value={createDraft.description}
                  onChange={(event) =>
                    setCreateDraft((current) => ({ ...current, description: event.target.value }))
                  }
                />
              </Field>
            ) : (
              <button type="button" className="task-add-description" onClick={() => setDescribeOpen(true)}>
                {t("tasks:fields.addDescription")}
              </button>
            )}
            <div className="task-create-meta">
              <TaskPriorityField
                id="task-priority"
                value={createDraft.priority}
                onChange={(priority) => setCreateDraft((current) => ({ ...current, priority }))}
              />
              <TaskDeadlineField
                id="task-due"
                value={createDraft.dueDate}
                onChange={(dueDate) => setCreateDraft((current) => ({ ...current, dueDate }))}
              />
            </div>
            <Field id="task-assignee" label={t("tasks:assignTo")}>
              <select
                id="task-assignee"
                value={createDraft.assigneeMembershipId}
                onChange={(event) =>
                  setCreateDraft((current) => ({ ...current, assigneeMembershipId: event.target.value }))
                }
              >
                <option value="">{t("tasks:unassigned")}</option>
                {assignable.map((member) => (
                  <option key={member.membershipId} value={member.membershipId}>
                    {member.displayName} ({member.email})
                  </option>
                ))}
              </select>
            </Field>
            <TagEditor
              idPrefix="task"
              draft={createDraft}
              tags={availableTags}
              canEdit
              onChange={setCreateDraft}
            />
          </div>
          <div className="task-actions task-create-actions">
            <button className="secondary-action" type="button" disabled={busy} onClick={resetCreate}>
              {t("common:cancel")}
            </button>
            <button className="primary-action" type="submit" disabled={busy}>
              {busy ? t("common:loading") : t("tasks:create")}
            </button>
          </div>
        </form>
      </Dialog>

      <Dialog
        open={Boolean(selected && capabilities)}
        titleId="task-detail-title"
        title={t("tasks:detail")}
        closeLabel={t("common:close")}
        onClose={closeDetail}
      >
        {selected && capabilities ? (
          <form className="task-ticket" onSubmit={onSave}>
            {error ? <StatusBanner tone="error">{error}</StatusBanner> : null}
            <p className="task-ticket-meta">
              <span>
                {t("tasks:createdBy")} {selected.createdByDisplayName || selected.createdByEmail || "—"}
              </span>
              {selected.status === "Closed" ? (
                <span className="task-unseen">{statusLabel("Closed")}</span>
              ) : null}
              {selected.status === "Closed" ? <span>{t("tasks:closedReadOnly")}</span> : null}
            </p>
            {selected.unseenActivityCount > 0 ? (
              <button type="button" className="secondary-action" onClick={showChanges}>
                {t("tasks:viewChanges")} · {t("tasks:newChanges", { count: selected.unseenActivityCount })}
              </button>
            ) : null}
            <div className="form-fields">
              <Field id="edit-task-title" label={t("tasks:fields.title")}>
                <input
                  id="edit-task-title"
                  className="task-title-input"
                  required
                  maxLength={200}
                  disabled={!capabilities.canEditDefinition}
                  value={draft.title}
                  onChange={(event) => setDraft((current) => ({ ...current, title: event.target.value }))}
                />
              </Field>
              <Field id="edit-task-status" label={t("tasks:fields.status")}>
                <select
                  id="edit-task-status"
                  disabled={statusOptions(selected, capabilities).length <= 1}
                  value={draft.status}
                  onChange={(event) =>
                    setDraft((current) => ({ ...current, status: event.target.value as TaskStatus }))
                  }
                >
                  {statusOptions(selected, capabilities).map((status) => (
                    <option key={status} value={status}>
                      {statusLabel(status)}
                    </option>
                  ))}
                </select>
              </Field>
              <div className="task-create-meta">
                <TaskPriorityField
                  id="edit-task-priority"
                  value={draft.priority}
                  disabled={!capabilities.canEditDefinition}
                  onChange={(priority) => setDraft((current) => ({ ...current, priority }))}
                />
                <TaskDeadlineField
                  id="edit-task-due"
                  value={draft.dueDate}
                  disabled={!capabilities.canEditDefinition}
                  onChange={(dueDate) => setDraft((current) => ({ ...current, dueDate }))}
                />
              </div>
              <Field id="edit-task-assignee" label={t("tasks:assignee")}>
                <select
                  id="edit-task-assignee"
                  disabled={!capabilities.canReassign}
                  value={pendingAssigneeId ?? draft.assigneeMembershipId}
                  onChange={(event) => {
                    const next = event.target.value;
                    setPendingAssigneeId(next === draft.assigneeMembershipId ? null : next);
                  }}
                >
                  <option value="">{t("tasks:unassigned")}</option>
                  {assignable.map((member) => (
                    <option key={member.membershipId} value={member.membershipId}>
                      {member.displayName} ({member.email})
                    </option>
                  ))}
                </select>
              </Field>
              {pendingAssigneeId !== null ? (
                <div className="task-handoff-confirm">
                  <p>{t("tasks:reassignConfirm", { name: pendingAssigneeName() })}</p>
                  <p>{t("tasks:reassignConfirmBody")}</p>
                  <div className="task-actions">
                    <button type="button" className="secondary-action" onClick={() => setPendingAssigneeId(null)}>
                      {t("common:cancel")}
                    </button>
                    <button type="button" className="primary-action" disabled={busy} onClick={() => void onConfirmHandoff()}>
                      {t("tasks:reassign")}
                    </button>
                  </div>
                </div>
              ) : null}
              <TagEditor
                idPrefix="edit-task"
                draft={draft}
                tags={availableTags}
                canEdit={capabilities.canManageTags}
                onChange={setDraft}
              />
              <Field id="edit-task-description" label={t("tasks:originalDescription")}>
                <textarea
                  id="edit-task-description"
                  maxLength={4000}
                  disabled={!capabilities.canEditDefinition}
                  value={draft.description}
                  onChange={(event) =>
                    setDraft((current) => ({ ...current, description: event.target.value }))
                  }
                />
              </Field>
            </div>

            <section>
              <button
                type="button"
                className="task-section-toggle"
                onClick={() => setCommentsOpen((current) => !current)}
              >
                <h3 className="task-section-title">{t("tasks:comments")}</h3>
                <span>{commentsOpen ? t("tasks:hideComments") : t("tasks:showComments")}</span>
              </button>
              {commentsOpen ? (
                <>
                  <ul className="task-comment-list">
                    {comments.map((comment) => (
                      <li key={comment.commentId} className="task-comment">
                        <div className="task-comment-meta">
                          <strong>{comment.authorDisplayName || "—"}</strong>
                          <time dateTime={comment.createdAtUtc}>
                            {formatDateTimeUtc(comment.createdAtUtc, i18n.language)}
                          </time>
                        </div>
                        {editingCommentId === comment.commentId ? (
                          <textarea
                            value={comment.body}
                            maxLength={4000}
                            onChange={(event) =>
                              setComments((current) =>
                                current.map((item) =>
                                  item.commentId === comment.commentId
                                    ? { ...item, body: event.target.value }
                                    : item,
                                ),
                              )
                            }
                          />
                        ) : (
                          <p>{comment.body}</p>
                        )}
                        {comment.isOwn ? (
                          <div className="task-comment-actions">
                            {editingCommentId === comment.commentId ? (
                              <button
                                type="button"
                                className="primary-action"
                                disabled={busy}
                                onClick={() => void onSaveComment(comment)}
                              >
                                {t("tasks:saveComment")}
                              </button>
                            ) : (
                              <button
                                type="button"
                                className="secondary-action"
                                onClick={() => setEditingCommentId(comment.commentId)}
                              >
                                {t("tasks:editComment")}
                              </button>
                            )}
                            <button
                              type="button"
                              className="secondary-action"
                              disabled={busy}
                              onClick={() => void onDeleteComment(comment.commentId)}
                            >
                              {t("tasks:deleteComment")}
                            </button>
                          </div>
                        ) : null}
                      </li>
                    ))}
                  </ul>
                  {capabilities.canComment ? (
                    <div className="form-fields">
                      <Field id="task-comment" label={t("tasks:addComment")}>
                        <textarea
                          id="task-comment"
                          maxLength={4000}
                          placeholder={t("tasks:commentPlaceholder")}
                          value={commentBody}
                          onChange={(event) => setCommentBody(event.target.value)}
                        />
                      </Field>
                      <button
                        className="secondary-action"
                        type="button"
                        disabled={busy || !commentBody.trim()}
                        onClick={(event) => void onAddComment(event)}
                      >
                        {t("tasks:addComment")}
                      </button>
                    </div>
                  ) : null}
                </>
              ) : null}
            </section>

            <section ref={activityRef} id="task-activity">
              <button
                type="button"
                className="task-section-toggle"
                onClick={() => setActivityOpen((current) => !current)}
              >
                <h3 className="task-section-title">{t("tasks:activity")}</h3>
                <span>{activityOpen ? t("tasks:hideActivity") : t("tasks:showActivity")}</span>
              </button>
              {activityOpen ? (
                <ul className="task-activity-list">
                  {activity.map((item) => (
                    <li key={item.activityId} className="task-activity-item">
                      <div className="task-activity-meta">
                        <strong>{t(`tasks:activityEvent.${item.eventType}`, { defaultValue: item.eventType })}</strong>
                        <span>{item.actorDisplayName || "—"}</span>
                        <time dateTime={item.createdAtUtc}>
                          {formatDateTimeUtc(item.createdAtUtc, i18n.language)}
                        </time>
                      </div>
                      {item.oldValue || item.newValue ? (
                        <p>
                          {item.oldValue ?? "—"} → {item.newValue ?? "—"}
                        </p>
                      ) : null}
                    </li>
                  ))}
                </ul>
              ) : null}
            </section>

            {canSave || capabilities.canDelete ? (
              <div className="task-actions">
                {canSave ? (
                  <button className="primary-action" type="submit" disabled={busy}>
                    {busy ? t("common:loading") : t("tasks:save")}
                  </button>
                ) : null}
                {capabilities.canDelete ? (
                  confirmDelete ? (
                    <div className="task-delete-confirm">
                      <p>{t("tasks:deleteConfirm")}</p>
                      <div className="task-actions">
                        <button className="secondary-action" type="button" onClick={() => setConfirmDelete(false)}>
                          {t("tasks:cancelDelete")}
                        </button>
                        <button className="secondary-action" type="button" disabled={busy} onClick={() => void onDelete()}>
                          {t("tasks:delete")}
                        </button>
                      </div>
                    </div>
                  ) : (
                    <button className="secondary-action" type="button" disabled={busy} onClick={() => setConfirmDelete(true)}>
                      {t("tasks:delete")}
                    </button>
                  )
                ) : null}
              </div>
            ) : null}
          </form>
        ) : null}
      </Dialog>
    </section>
  );
}

function TagEditor({
  idPrefix,
  draft,
  tags,
  canEdit,
  onChange,
}: {
  idPrefix: string;
  draft: typeof emptyDraft;
  tags: WorkTag[];
  canEdit: boolean;
  onChange: Dispatch<SetStateAction<typeof emptyDraft>>;
}) {
  const { t } = useTranslation("tasks");
  const selected = tags.filter((tag) => draft.tagIds.includes(tag.tagId));
  const available = tags.filter((tag) => !draft.tagIds.includes(tag.tagId));

  function removeTag(tagId: string) {
    onChange((current) => ({ ...current, tagIds: current.tagIds.filter((id) => id !== tagId) }));
  }

  function addTag(tagId: string) {
    onChange((current) => ({ ...current, tagIds: [...current.tagIds, tagId] }));
  }

  return (
    <div className="task-tag-editor">
      <span className="task-tag-label">{t("tags")}</span>
      <div className="task-tag-list">
        {selected.map((tag) => (
          <span key={tag.tagId} className="task-tag-chip task-tag-chip-active">
            {tag.name}
            {canEdit ? (
              <button type="button" aria-label={t("removeTag")} onClick={() => removeTag(tag.tagId)}>
                ×
              </button>
            ) : null}
          </span>
        ))}
      </div>
      {canEdit && available.length > 0 ? (
        <div className="task-tag-list">
          {available.map((tag) => (
            <button key={tag.tagId} type="button" className="task-tag-chip" onClick={() => addTag(tag.tagId)}>
              {tag.name}
            </button>
          ))}
        </div>
      ) : null}
      {canEdit ? (
        <div className="task-tag-create">
          <input
            id={`${idPrefix}-new-tag`}
            maxLength={40}
            placeholder={t("addTag")}
            value={draft.newTag}
            onChange={(event) => onChange((current) => ({ ...current, newTag: event.target.value }))}
          />
        </div>
      ) : null}
    </div>
  );
}
