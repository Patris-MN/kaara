import {
  type Dispatch,
  type FormEvent,
  type SetStateAction,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";
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
  markTaskSeen,
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
import { ContextMenu } from "../components/ContextMenu";
import { Dialog } from "../components/Dialog";
import { EmptyState } from "../components/EmptyState";
import { InfoCallout } from "../components/InfoCallout";
import { PageHeader } from "../components/PageHeader";
import { TaskSectionDisclosure } from "../components/TaskSectionDisclosure";
import { Field, StatusBanner } from "../components/Ui";
import { useFeedback } from "../feedback/FeedbackProvider";
import type { MemberMenuItem } from "../members/memberActions";
import { TaskDeadlineField } from "../tasks/TaskDeadlineField";
import { TaskPriorityBadge, TaskPriorityField } from "../tasks/TaskPriorityField";
import { formatDateTimeUtc, formatTaskDate, isTaskOverdue, normalizePriority } from "../tasks/presentation";
import { isTaskFullyReadOnly, resolveTaskCapabilities } from "../tasks/resolveTaskCapabilities";
import { formatActivityChange } from "../tasks/activityPresentation";
import {
  applyTaskFilters,
  applyTaskQuickScope,
  countActiveTasks,
  countMyTasks,
  countOverdueTasks,
  EMPTY_TASK_FILTERS,
  filterTasksBySearch,
  hasActiveTaskFilters,
  resolveCurrentMembershipId,
  sortTasks,
  taskDraftChanged,
  type TaskFilters,
  type TaskQuickScope,
  type TaskSort,
} from "../tasks/taskResources";

const STATUSES: TaskStatus[] = ["Open", "InProgress", "Waiting", "Resolved", "Closed"];
const PRIORITIES: TaskPriority[] = ["Urgent", "High", "Normal", "Low"];

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
  return resolveTaskCapabilities(task, accessLevel);
}

function projectPath(tenantId: string, workspaceId: string, projectId: string) {
  return `/app/tenants/${tenantId}/workspaces/${workspaceId}/projects/${projectId}`;
}

export function ProjectTasksPage() {
  const { t, i18n } = useTranslation(["tasks", "common"]);
  const { tenantId, workspaceId, projectId, "*": splat } = useParams();
  const routeTaskId = splat?.startsWith("tasks/") ? splat.slice("tasks/".length) : undefined;
  const navigate = useNavigate();
  const { token, user } = useAuth();
  const { show } = useFeedback();
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
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [pendingAssigneeId, setPendingAssigneeId] = useState<string | null>(null);
  const [commentsOpen, setCommentsOpen] = useState(true);
  const [activityOpen, setActivityOpen] = useState(false);
  const [search, setSearch] = useState("");
  const [quickScope, setQuickScope] = useState<TaskQuickScope>("all");
  const [filters, setFilters] = useState<TaskFilters>(EMPTY_TASK_FILTERS);
  const [filterDraft, setFilterDraft] = useState<TaskFilters>(EMPTY_TASK_FILTERS);
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [sort, setSort] = useState<TaskSort>("updatedDesc");
  const [error, setError] = useState<string | null>(null);
  const [loadError, setLoadError] = useState(false);
  const [reloadToken, setReloadToken] = useState(0);
  const [forbidden, setForbidden] = useState(false);
  const [notFound, setNotFound] = useState(false);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [creating, setCreating] = useState(false);

  useEffect(() => {
    if (!token || !tenantId || !workspaceId || !projectId) {
      return;
    }
    const current = requestId.current + 1;
    requestId.current = current;
    // Reset task-page state when the project route changes, then fetch.
    // oxlint-disable-next-line react/set-state-in-effect
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
    setDeleteOpen(false);
    setPendingAssigneeId(null);
    setSearch("");
    setQuickScope("all");
    setFilters(EMPTY_TASK_FILTERS);
    setFilterDraft(EMPTY_TASK_FILTERS);
    setSort("updatedDesc");
    setForbidden(false);
    setNotFound(false);
    setError(null);
    setLoadError(false);
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
        setLoadError(true);
        setLoading(false);
      }
    })();

    return () => controller.abort();
  }, [token, tenantId, workspaceId, projectId, t, reloadToken]);

  function retryLoad() {
    setLoadError(false);
    setError(null);
    setReloadToken((current) => current + 1);
  }

  const canCreate = workspace?.accessLevel === "Edit";
  const currentMembershipId = useMemo(
    () => resolveCurrentMembershipId(assignable, user?.email),
    [assignable, user?.email],
  );

  // Client-side search, quick scope, filters, and sort on the loaded task list.
  const filteredTasks = useMemo(() => {
    let result = tasks;
    result = filterTasksBySearch(result, search);
    result = applyTaskQuickScope(result, quickScope, currentMembershipId);
    result = applyTaskFilters(result, filters, currentMembershipId);
    return sortTasks(result, sort);
  }, [tasks, search, quickScope, filters, sort, currentMembershipId]);

  const selected = tasks.find((task) => task.taskId === selectedId) ?? null;
  const capabilities = selected ? resolveCapabilities(selected, workspace?.accessLevel) : null;
  const taskReadOnly = capabilities ? isTaskFullyReadOnly(capabilities) : false;
  const draftChanged = selected ? taskDraftChanged(draft, selected) : false;
  const canSave =
    Boolean(capabilities?.canEditDefinition || capabilities?.canManageTags || capabilities?.canReassign) ||
    Boolean(capabilities && capabilities.allowedStatuses.some((status) => status !== selected?.status));
  const canReopen =
    selected?.status === "Closed" && Boolean(capabilities?.allowedStatuses.includes("Open"));
  const showTaskToolbar = !loading && tasks.length > 0;

  const applyDraft = useCallback((task: WorkTask) => {
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
  }, []);

  const loadTask = useCallback(async (task: WorkTask) => {
    setSelectedId(task.taskId);
    applyDraft(task);
    setDeleteOpen(false);
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
      void markTaskSeen(token, tenantId, workspaceId, projectId, fresh.taskId).catch(() => undefined);
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
  }, [applyDraft, projectId, t, tenantId, token, workspaceId]);

  useEffect(() => {
    if (!routeTaskId || loading) {
      if (!routeTaskId) {
        // Keep list selection aligned with the URL after the detail route closes.
        // oxlint-disable-next-line react/set-state-in-effect
        setSelectedId(null);
      }
      return;
    }
    const task = tasks.find((item) => item.taskId === routeTaskId);
    if (task && selectedId !== routeTaskId) {
      void loadTask(task);
    }
  }, [loadTask, loading, routeTaskId, selectedId, tasks]);

  function statusLabel(status: TaskStatus) {
    return t(`tasks:status.${status === "InProgress" ? "inProgress" : status.toLowerCase()}`);
  }

  function priorityLabel(priority: TaskPriority) {
    return t(`tasks:priority.${normalizePriority(priority).toLowerCase()}`, { defaultValue: priority });
  }

  function statusOptions(task: WorkTask, caps: TaskCapabilities) {
    const values = new Set<TaskStatus>([task.status, ...caps.allowedStatuses]);
    return STATUSES.filter((status) => values.has(status));
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

  function openCreateDialog() {
    setError(null);
    setCreateOpen(true);
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
    setDeleteOpen(false);
    navigate(listPath());
  }

  function clearDiscovery() {
    setSearch("");
    setQuickScope("all");
    setFilters(EMPTY_TASK_FILTERS);
  }

  function openFiltersDialog() {
    setFilterDraft(filters);
    setFiltersOpen(true);
  }

  function applyFilters() {
    setFilters(filterDraft);
    setFiltersOpen(false);
  }

  async function onCreate(event: FormEvent) {
    event.preventDefault();
    if (!token || !tenantId || !workspaceId || !projectId || creating) {
      return;
    }
    setCreating(true);
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
      show({
        tone: "success",
        title: t("tasks:createdTitle"),
        body: t("tasks:createdBody", { title: created.title }),
      });
    } catch (cause) {
      show({
        tone: "error",
        title: t("tasks:createFailed"),
      });
      setError(t(translationKeyForApiError(cause), { ns: "common" }));
    } finally {
      setCreating(false);
    }
  }

  async function persistTask(nextDraft: typeof emptyDraft) {
    if (!token || !tenantId || !workspaceId || !projectId || !selected || !capabilities) {
      return null;
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
      show({
        tone: "success",
        title: t("tasks:savedTitle"),
        body: t("tasks:savedBody"),
      });
      return updated;
    } catch (cause) {
      show({
        tone: "error",
        title: t("tasks:saveFailed"),
      });
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
    if (busy || !draftChanged) {
      return;
    }
    await persistTask(draft);
  }

  async function onReopen() {
    if (!selected || busy) {
      return;
    }
    const nextDraft = { ...draft, status: "Open" as TaskStatus };
    setDraft(nextDraft);
    await persistTask(nextDraft);
  }

  async function onCloseTaskFromCallout() {
    if (!selected || busy) {
      return;
    }
    const nextDraft = { ...draft, status: "Closed" as TaskStatus };
    setDraft(nextDraft);
    await persistTask(nextDraft);
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
      setDeleteOpen(false);
      closeDetail();
    } catch (cause) {
      show({
        tone: "error",
        title: t("tasks:deleteFailed"),
      });
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

  function toggleFilterStatus(status: TaskStatus) {
    setFilterDraft((current) => ({
      ...current,
      statuses: current.statuses.includes(status)
        ? current.statuses.filter((item) => item !== status)
        : [...current.statuses, status],
    }));
  }

  function toggleFilterPriority(priority: TaskPriority) {
    setFilterDraft((current) => ({
      ...current,
      priorities: current.priorities.includes(priority)
        ? current.priorities.filter((item) => item !== priority)
        : [...current.priorities, priority],
    }));
  }

  function deadlineFilterLabel(deadline: TaskFilters["deadline"]) {
    switch (deadline) {
      case "overdue":
        return t("tasks:filterDeadlineOverdue");
      case "today":
        return t("tasks:filterDeadlineToday");
      case "week":
        return t("tasks:filterDeadlineWeek");
      case "none":
        return t("tasks:filterDeadlineNone");
      default:
        return t("tasks:filterDeadlineAny");
    }
  }

  function toggleFilterTag(tagId: string) {
    setFilterDraft((current) => ({
      ...current,
      tagIds: current.tagIds.includes(tagId)
        ? current.tagIds.filter((item) => item !== tagId)
        : [...current.tagIds, tagId],
    }));
  }

  const newTaskAction = canCreate ? (
    <button className="primary-action toolbar-primary-action" type="button" onClick={openCreateDialog}>
      {t("tasks:newTask")}
    </button>
  ) : null;

  const taskActionItems: MemberMenuItem[] =
    capabilities?.canDelete && selected
      ? [
          {
            id: "delete-task",
            label: t("tasks:delete"),
            destructive: true,
            onClick: () => setDeleteOpen(true),
          },
        ]
      : [];

  const pageDescription =
    loading || !project
      ? t("tasks:pageDescription")
      : `${project.description?.trim() || t("tasks:pageDescription")} · ${t("tasks:resourceSummary", { count: tasks.length })}`;

  if (forbidden) {
    return <StatusBanner tone="error">{t("common:errors.forbidden")}</StatusBanner>;
  }

  if (notFound) {
    return <StatusBanner tone="error">{t("common:errors.project_not_found")}</StatusBanner>;
  }

  return (
    <section className="app-page task-list-page project-tasks-page">
      <PageHeader eyebrow={t("tasks:eyebrow")} title={project?.name ?? t("tasks:title")} description={pageDescription} />

      {error && !loadError && !createOpen && !selected ? <StatusBanner tone="error">{error}</StatusBanner> : null}
      {loading ? <StatusBanner tone="info">{t("common:loading")}</StatusBanner> : null}
      {workspace?.accessLevel === "View" ? (
        <StatusBanner tone="info">{t("tasks:viewOnly")}</StatusBanner>
      ) : null}

      {showTaskToolbar ? (
        <>
          <div className="task-quick-scopes" role="group" aria-label={t("tasks:resourceToolbar")}>
            {(
              [
                ["all", t("tasks:quickAll"), tasks.length],
                ["mine", t("tasks:quickMine"), countMyTasks(tasks, currentMembershipId)],
                ["active", t("tasks:quickActive"), countActiveTasks(tasks)],
                ["overdue", t("tasks:quickOverdue"), countOverdueTasks(tasks)],
              ] as const
            ).map(([scope, label, count]) => (
              <button
                key={scope}
                type="button"
                className={quickScope === scope ? "task-quick-scope task-quick-scope-active" : "task-quick-scope"}
                aria-pressed={quickScope === scope}
                onClick={() => setQuickScope(scope)}
              >
                {label}
                <span className="task-quick-scope-count">{count}</span>
              </button>
            ))}
          </div>

          <div className="project-tasks-toolbar" role="toolbar" aria-label={t("tasks:resourceToolbar")}>
            <label className="resource-search">
              <span className="sr-only">{t("tasks:searchLabel")}</span>
              <input
                type="search"
                value={search}
                placeholder={t("tasks:searchPlaceholder")}
                aria-label={t("tasks:searchLabel")}
                onChange={(event) => setSearch(event.target.value)}
              />
            </label>
            <div className="project-tasks-toolbar-controls">
              <button
                type="button"
                className={hasActiveTaskFilters(filters) ? "secondary-action filter-action-active" : "secondary-action"}
                onClick={openFiltersDialog}
              >
                {t("tasks:filterLabel")}
              </button>
              <label className="resource-sort">
                <span className="sr-only">{t("tasks:sortLabel")}</span>
                <select
                  aria-label={t("tasks:sortLabel")}
                  value={sort}
                  onChange={(event) => setSort(event.target.value as TaskSort)}
                >
                  <option value="updatedDesc">{t("tasks:sortUpdatedDesc")}</option>
                  <option value="createdDesc">{t("tasks:sortCreatedDesc")}</option>
                  <option value="createdAsc">{t("tasks:sortCreatedAsc")}</option>
                  <option value="dueAsc">{t("tasks:sortDueAsc")}</option>
                  <option value="dueDesc">{t("tasks:sortDueDesc")}</option>
                  <option value="priorityDesc">{t("tasks:sortPriorityDesc")}</option>
                  <option value="priorityAsc">{t("tasks:sortPriorityAsc")}</option>
                  <option value="titleAsc">{t("tasks:sortTitleAsc")}</option>
                  <option value="titleDesc">{t("tasks:sortTitleDesc")}</option>
                </select>
              </label>
              {newTaskAction}
            </div>
          </div>

          {hasActiveTaskFilters(filters) ? (
            <div className="task-filter-chips">
              <span className="task-filter-chips-label">{t("tasks:filterLabel")}</span>
              {filters.statuses.map((status) => (
                <span key={status} className="task-filter-chip">
                  {statusLabel(status)}
                </span>
              ))}
              {filters.priorities.map((priority) => (
                <span key={priority} className="task-filter-chip">
                  {priorityLabel(priority)}
                </span>
              ))}
              {filters.assignee !== "any" ? (
                <span className="task-filter-chip">
                  {filters.assignee === "me"
                    ? t("tasks:filterAssigneeMe")
                    : filters.assignee === "unassigned"
                      ? t("tasks:filterAssigneeUnassigned")
                      : assignable.find((member) => member.membershipId === filters.assignee)?.displayName ??
                        t("tasks:assignee")}
                </span>
              ) : null}
              {filters.deadline !== "any" ? (
                <span className="task-filter-chip">{deadlineFilterLabel(filters.deadline)}</span>
              ) : null}
              {filters.tagIds.map((tagId) => {
                const tag = availableTags.find((item) => item.tagId === tagId);
                return tag ? (
                  <span key={tagId} className="task-filter-chip">
                    {tag.name}
                  </span>
                ) : null;
              })}
              <button type="button" className="secondary-action task-filter-clear" onClick={() => setFilters(EMPTY_TASK_FILTERS)}>
                {t("tasks:clearFilters")}
              </button>
            </div>
          ) : null}
        </>
      ) : null}

      {loading ? (
        <p className="quiet-state">{t("common:loading")}</p>
      ) : loadError ? (
        <EmptyState
          compact
          title={t("tasks:loadFailedTitle")}
          body={t("tasks:loadFailedBody")}
          action={
            <button type="button" className="secondary-action" onClick={retryLoad}>
              {t("tasks:retryLoad")}
            </button>
          }
        />
      ) : tasks.length === 0 ? (
        <EmptyState
          title={t("tasks:emptyTitle")}
          body={canCreate ? t("tasks:emptyBodyCreate") : t("tasks:emptyBodyViewOnly")}
          action={
            canCreate ? (
              <button type="button" className="primary-action" onClick={openCreateDialog}>
                {t("tasks:newTask")}
              </button>
            ) : undefined
          }
        />
      ) : filteredTasks.length === 0 ? (
        <EmptyState
          compact
          title={t("tasks:searchEmptyTitle")}
          body={t("tasks:searchEmptyBody")}
          action={
            <button type="button" className="secondary-action" onClick={clearDiscovery}>
              {hasActiveTaskFilters(filters) ? t("tasks:clearFilters") : t("tasks:clearSearch")}
            </button>
          }
        />
      ) : (
        <ul className="task-list">
          {filteredTasks.map((task) => (
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
                        {isTaskOverdue(task.dueDate, task.status)
                          ? t("tasks:dueOverdue", { date: formatTaskDate(task.dueDate) })
                          : t("tasks:due", { date: formatTaskDate(task.dueDate) })}
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
              </button>
            </li>
          ))}
        </ul>
      )}

      <Dialog
        open={createOpen}
        size="compact"
        titleId="create-task-title"
        title={t("tasks:newTask")}
        closeLabel={t("common:close")}
        onClose={resetCreate}
      >
        {error ? <StatusBanner tone="error">{error}</StatusBanner> : null}
        <form className="form-card task-create-card" onSubmit={onCreate}>
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
            <button className="secondary-action" type="button" disabled={creating} onClick={resetCreate}>
              {t("common:cancel")}
            </button>
            <button className="primary-action" type="submit" disabled={creating}>
              {creating ? t("tasks:creating") : t("tasks:newTask")}
            </button>
          </div>
        </form>
      </Dialog>

      <Dialog
        open={filtersOpen}
        size="compact"
        titleId="task-filter-title"
        title={t("tasks:filterDialogTitle")}
        closeLabel={t("common:close")}
        onClose={() => setFiltersOpen(false)}
      >
        <div className="form-fields task-filter-form">
          <fieldset className="task-filter-group">
            <legend>{t("tasks:filterStatus")}</legend>
            <div className="task-filter-options">
              {STATUSES.map((status) => (
                <label key={status} className="task-filter-option">
                  <input
                    type="checkbox"
                    checked={filterDraft.statuses.includes(status)}
                    onChange={() => toggleFilterStatus(status)}
                  />
                  {statusLabel(status)}
                </label>
              ))}
            </div>
          </fieldset>
          <fieldset className="task-filter-group">
            <legend>{t("tasks:filterPriority")}</legend>
            <div className="task-filter-options">
              {PRIORITIES.map((priority) => (
                <label key={priority} className="task-filter-option">
                  <input
                    type="checkbox"
                    checked={filterDraft.priorities.includes(priority)}
                    onChange={() => toggleFilterPriority(priority)}
                  />
                  {priorityLabel(priority)}
                </label>
              ))}
            </div>
          </fieldset>
          <Field id="filter-assignee" label={t("tasks:filterAssignee")}>
            <select
              id="filter-assignee"
              value={filterDraft.assignee}
              onChange={(event) =>
                setFilterDraft((current) => ({
                  ...current,
                  assignee: event.target.value as TaskFilters["assignee"],
                }))
              }
            >
              <option value="any">{t("tasks:filterAssigneeAny")}</option>
              <option value="me">{t("tasks:filterAssigneeMe")}</option>
              <option value="unassigned">{t("tasks:filterAssigneeUnassigned")}</option>
              {assignable.map((member) => (
                <option key={member.membershipId} value={member.membershipId}>
                  {member.displayName}
                </option>
              ))}
            </select>
          </Field>
          <Field id="filter-deadline" label={t("tasks:filterDeadline")}>
            <select
              id="filter-deadline"
              value={filterDraft.deadline}
              onChange={(event) =>
                setFilterDraft((current) => ({
                  ...current,
                  deadline: event.target.value as TaskFilters["deadline"],
                }))
              }
            >
              <option value="any">{t("tasks:filterDeadlineAny")}</option>
              <option value="overdue">{t("tasks:filterDeadlineOverdue")}</option>
              <option value="today">{t("tasks:filterDeadlineToday")}</option>
              <option value="week">{t("tasks:filterDeadlineWeek")}</option>
              <option value="none">{t("tasks:filterDeadlineNone")}</option>
            </select>
          </Field>
          {availableTags.length > 0 ? (
            <fieldset className="task-filter-group">
              <legend>{t("tasks:filterTags")}</legend>
              <div className="task-filter-options">
                {availableTags.map((tag) => (
                  <label key={tag.tagId} className="task-filter-option">
                    <input
                      type="checkbox"
                      checked={filterDraft.tagIds.includes(tag.tagId)}
                      onChange={() => toggleFilterTag(tag.tagId)}
                    />
                    {tag.name}
                  </label>
                ))}
              </div>
            </fieldset>
          ) : null}
        </div>
        <div className="task-actions">
          <button type="button" className="secondary-action" onClick={() => setFiltersOpen(false)}>
            {t("common:cancel")}
          </button>
          <button type="button" className="primary-action" onClick={applyFilters}>
            {t("tasks:applyFilters")}
          </button>
        </div>
      </Dialog>

      <Dialog
        open={Boolean(selected && capabilities)}
        titleId="task-detail-title"
        title={selected?.title ?? t("tasks:detail")}
        closeLabel={t("common:close")}
        onClose={closeDetail}
      >
        {selected && capabilities ? (
          <form className="task-ticket" onSubmit={onSave}>
            {error ? <StatusBanner tone="error">{error}</StatusBanner> : null}

            <div className="task-detail-header">
              {taskReadOnly ? (
                <h2 id="task-detail-title-visible">{selected.title}</h2>
              ) : (
                <Field id="edit-task-title-visible" label={t("tasks:fields.title")}>
                  <input
                    id="edit-task-title-visible"
                    className="task-title-input task-detail-title-input"
                    required
                    maxLength={200}
                    disabled={!capabilities.canEditDefinition}
                    value={draft.title}
                    onChange={(event) => setDraft((current) => ({ ...current, title: event.target.value }))}
                  />
                </Field>
              )}
              <p className="task-ticket-meta">
                <span>
                  {t("tasks:createdBy")} {selected.createdByDisplayName || selected.createdByEmail || "—"}
                </span>
                {selected.createdAtUtc ? (
                  <time dateTime={selected.createdAtUtc}>
                    {t("tasks:createdOn", {
                      date: formatDateTimeUtc(selected.createdAtUtc, i18n.language),
                    })}
                  </time>
                ) : null}
              </p>
            </div>

            {selected.status === "Closed" ? (
              <div className="task-closed-banner">
                <span className="task-unseen">{t("tasks:closedBanner")}</span>
                <span>
                  {canReopen ? t("tasks:closedReadOnly") : t("tasks:closedReadOnlyNoReopen")}
                </span>
                {canReopen ? (
                  <button type="button" className="secondary-action" disabled={busy} onClick={() => void onReopen()}>
                    {t("tasks:reopenTask")}
                  </button>
                ) : null}
              </div>
            ) : null}

            {selected.unseenActivityCount > 0 ? (
              <button type="button" className="secondary-action" onClick={showChanges}>
                {t("tasks:viewChanges")} · {t("tasks:newChanges", { count: selected.unseenActivityCount })}
              </button>
            ) : null}

            <div className="form-fields">
              {taskReadOnly ? (
                <dl className="task-detail-grid task-readonly-fields">
                  <div>
                    <dt>{t("tasks:fields.status")}</dt>
                    <dd>{statusLabel(selected.status)}</dd>
                  </div>
                  <div>
                    <dt>{t("tasks:fields.priority")}</dt>
                    <dd>{priorityLabel(normalizePriority(selected.priority))}</dd>
                  </div>
                  <div>
                    <dt>{t("tasks:assignee")}</dt>
                    <dd>{assigneeLabel(selected)}</dd>
                  </div>
                  <div>
                    <dt>{t("tasks:fields.deadline")}</dt>
                    <dd>{selected.dueDate ? formatTaskDate(selected.dueDate) : "—"}</dd>
                  </div>
                  {selected.tags && selected.tags.length > 0 ? (
                    <div className="task-detail-grid-span">
                      <dt>{t("tasks:fields.tags")}</dt>
                      <dd className="task-readonly-tags">
                        {selected.tags.map((tag) => (
                          <span key={tag.tagId} className="task-tag-chip task-tag-chip-static">
                            {tag.name}
                          </span>
                        ))}
                      </dd>
                    </div>
                  ) : null}
                  <div className="task-detail-grid-span">
                    <dt>{t("tasks:descriptionLabel")}</dt>
                    <dd>{selected.description?.trim() ? selected.description : t("tasks:noDescription")}</dd>
                  </div>
                </dl>
              ) : (
                <>
                  <div className="task-detail-grid">
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
                    <TaskPriorityField
                      id="edit-task-priority"
                      value={draft.priority}
                      disabled={!capabilities.canEditDefinition}
                      onChange={(priority) => setDraft((current) => ({ ...current, priority }))}
                    />
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
                    <TaskDeadlineField
                      id="edit-task-due"
                      value={draft.dueDate}
                      disabled={!capabilities.canEditDefinition}
                      onChange={(dueDate) => setDraft((current) => ({ ...current, dueDate }))}
                    />
                  </div>
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
                  <Field id="edit-task-description" label={t("tasks:descriptionLabel")}>
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
                </>
              )}
            </div>

            <section>
              <TaskSectionDisclosure
                label={t("tasks:comments")}
                count={comments.length}
                expanded={commentsOpen}
                onToggle={() => setCommentsOpen((current) => !current)}
                icon="comments"
                showLabel={t("tasks:showComments")}
                hideLabel={t("tasks:hideComments")}
              />
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
                        {comment.isOwn && capabilities.canComment ? (
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
              <TaskSectionDisclosure
                label={t("tasks:activity")}
                expanded={activityOpen}
                onToggle={() => setActivityOpen((current) => !current)}
                icon="activity"
                showLabel={t("tasks:showActivity")}
                hideLabel={t("tasks:hideActivity")}
              />
              {activityOpen ? (
                <ul className="task-activity-list">
                  {activity.map((item) => {
                    const changeText = formatActivityChange(item, {
                      tags: availableTags,
                      assignable,
                      t,
                    });
                    return (
                    <li key={item.activityId} className="task-activity-item">
                      <div className="task-activity-meta">
                        <strong>{t(`tasks:activityEvent.${item.eventType}`, { defaultValue: item.eventType })}</strong>
                        <span>{item.actorDisplayName || "—"}</span>
                        <time dateTime={item.createdAtUtc}>
                          {formatDateTimeUtc(item.createdAtUtc, i18n.language)}
                        </time>
                      </div>
                      {changeText ? <p>{changeText}</p> : null}
                    </li>
                    );
                  })}
                </ul>
              ) : null}
            </section>

            {!capabilities.canDelete && capabilities.deleteBlockedReason ? (
              <InfoCallout
                icon={
                  <svg viewBox="0 0 24 24" aria-hidden="true">
                    <path d="M2 12s3.5-6 10-6 10 6 10 6-3.5 6-10 6-10-6-10-6Z" />
                    <circle cx="12" cy="12" r="2.75" />
                  </svg>
                }
                title={t("tasks:deleteBlockedTitle")}
                action={
                  capabilities.allowedStatuses.includes("Closed") && selected?.status !== "Closed" ? (
                    <button
                      type="button"
                      className="secondary-action"
                      disabled={busy}
                      onClick={() => void onCloseTaskFromCallout()}
                    >
                      {t("tasks:closeTaskAction")}
                    </button>
                  ) : null
                }
              >
                <p>{t("tasks:deleteBlockedBody")}</p>
              </InfoCallout>
            ) : null}

            {canSave || taskActionItems.length > 0 ? (
              <div className="task-actions task-detail-actions">
                {canSave ? (
                  <button className="primary-action" type="submit" disabled={busy || !draftChanged}>
                    {busy ? t("tasks:saving") : t("tasks:save")}
                  </button>
                ) : null}
                {taskActionItems.length > 0 ? (
                  <ContextMenu label={t("tasks:detail")} items={taskActionItems} />
                ) : null}
              </div>
            ) : null}
          </form>
        ) : null}
      </Dialog>

      <Dialog
        open={deleteOpen}
        size="compact"
        titleId="delete-task-title"
        title={t("tasks:deleteConfirmTitle")}
        closeLabel={t("common:close")}
        onClose={() => setDeleteOpen(false)}
      >
        <p>
          {selected
            ? t("tasks:deleteConfirmNamed", { title: selected.title })
            : t("tasks:deleteConfirmBody")}
        </p>
        <div className="task-actions">
          <button type="button" className="secondary-action" disabled={busy} onClick={() => setDeleteOpen(false)}>
            {t("tasks:cancelDelete")}
          </button>
          <button type="button" className="secondary-action" disabled={busy} onClick={() => void onDelete()}>
            {t("tasks:delete")}
          </button>
        </div>
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
