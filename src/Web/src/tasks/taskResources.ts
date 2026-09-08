import type { AssignableMember, TaskPriority, TaskStatus, WorkTask } from "../api/types";
import { isTaskOverdue, normalizePriority, todayDateOnly } from "./presentation";

export type TaskQuickScope = "all" | "mine" | "active" | "overdue";

export type TaskSort =
  | "updatedDesc"
  | "createdDesc"
  | "createdAsc"
  | "dueAsc"
  | "dueDesc"
  | "priorityDesc"
  | "priorityAsc"
  | "titleAsc"
  | "titleDesc";

export type TaskDeadlineFilter = "any" | "overdue" | "today" | "week" | "none";

export type TaskAssigneeFilter = "any" | "me" | "unassigned" | string;

export type TaskFilters = {
  statuses: TaskStatus[];
  priorities: TaskPriority[];
  assignee: TaskAssigneeFilter;
  deadline: TaskDeadlineFilter;
  tagIds: string[];
};

export const EMPTY_TASK_FILTERS: TaskFilters = {
  statuses: [],
  priorities: [],
  assignee: "any",
  deadline: "any",
  tagIds: [],
};

const PRIORITY_RANK: Record<TaskPriority, number> = {
  Urgent: 4,
  High: 3,
  Normal: 2,
  Low: 1,
};

export function filterTasksBySearch(tasks: WorkTask[], query: string): WorkTask[] {
  const normalized = query.trim().toLocaleLowerCase();
  if (!normalized) {
    return tasks;
  }

  return tasks.filter((task) => {
    const haystack = [
      task.title,
      task.description ?? "",
      ...(task.tags?.map((tag) => tag.name) ?? []),
    ]
      .join(" ")
      .toLocaleLowerCase();
    return haystack.includes(normalized);
  });
}

export function applyTaskQuickScope(
  tasks: WorkTask[],
  scope: TaskQuickScope,
  currentMembershipId?: string | null,
): WorkTask[] {
  switch (scope) {
    case "mine":
      return currentMembershipId
        ? tasks.filter((task) => task.assigneeMembershipId === currentMembershipId)
        : [];
    case "active":
      return tasks.filter((task) => task.status !== "Closed");
    case "overdue":
      return tasks.filter((task) => isTaskOverdue(task.dueDate, task.status));
    case "all":
    default:
      return tasks;
  }
}

function isWithinWeek(dateOnly: string, today = todayDateOnly()): boolean {
  const start = new Date(`${today}T00:00:00`);
  const end = new Date(start);
  end.setDate(end.getDate() + 7);
  const due = new Date(`${dateOnly}T00:00:00`);
  return due >= start && due < end;
}

export function applyTaskFilters(
  tasks: WorkTask[],
  filters: TaskFilters,
  currentMembershipId?: string | null,
): WorkTask[] {
  return tasks.filter((task) => {
    if (filters.statuses.length > 0 && !filters.statuses.includes(task.status)) {
      return false;
    }
    if (filters.priorities.length > 0 && !filters.priorities.includes(normalizePriority(task.priority))) {
      return false;
    }
    if (filters.assignee === "me") {
      if (!currentMembershipId || task.assigneeMembershipId !== currentMembershipId) {
        return false;
      }
    } else if (filters.assignee === "unassigned") {
      if (task.assigneeMembershipId) {
        return false;
      }
    } else if (filters.assignee !== "any" && task.assigneeMembershipId !== filters.assignee) {
      return false;
    }
    if (filters.deadline === "overdue" && !isTaskOverdue(task.dueDate, task.status)) {
      return false;
    }
    if (filters.deadline === "today") {
      const today = todayDateOnly();
      if (!task.dueDate || task.dueDate !== today) {
        return false;
      }
    }
    if (filters.deadline === "week" && (!task.dueDate || !isWithinWeek(task.dueDate))) {
      return false;
    }
    if (filters.deadline === "none" && task.dueDate) {
      return false;
    }
    if (filters.tagIds.length > 0) {
      const taskTagIds = new Set(task.tags?.map((tag) => tag.tagId) ?? []);
      if (!filters.tagIds.every((tagId) => taskTagIds.has(tagId))) {
        return false;
      }
    }
    return true;
  });
}

export function sortTasks(tasks: WorkTask[], sort: TaskSort): WorkTask[] {
  const next = [...tasks];
  next.sort((left, right) => {
    switch (sort) {
      case "createdAsc":
        return new Date(left.createdAtUtc).getTime() - new Date(right.createdAtUtc).getTime();
      case "dueAsc":
        return compareOptionalDate(left.dueDate, right.dueDate) || left.title.localeCompare(right.title);
      case "dueDesc":
        return compareOptionalDate(right.dueDate, left.dueDate) || left.title.localeCompare(right.title);
      case "priorityDesc":
        return (
          PRIORITY_RANK[normalizePriority(right.priority)] - PRIORITY_RANK[normalizePriority(left.priority)]
          || left.title.localeCompare(right.title)
        );
      case "priorityAsc":
        return (
          PRIORITY_RANK[normalizePriority(left.priority)] - PRIORITY_RANK[normalizePriority(right.priority)]
          || left.title.localeCompare(right.title)
        );
      case "titleDesc":
        return right.title.localeCompare(left.title);
      case "titleAsc":
        return left.title.localeCompare(right.title);
      case "createdDesc":
        return new Date(right.createdAtUtc).getTime() - new Date(left.createdAtUtc).getTime();
      case "updatedDesc":
      default:
        return (
          new Date(right.updatedAtUtc).getTime() - new Date(left.updatedAtUtc).getTime()
          || left.title.localeCompare(right.title)
        );
    }
  });
  return next;
}

function compareOptionalDate(left: string | null, right: string | null): number {
  if (!left && !right) {
    return 0;
  }
  if (!left) {
    return 1;
  }
  if (!right) {
    return -1;
  }
  return left.localeCompare(right);
}

export function countActiveTasks(tasks: WorkTask[]): number {
  return tasks.filter((task) => task.status !== "Closed").length;
}

/** @deprecated Use countActiveTasks */
export function countOpenTasks(tasks: WorkTask[]): number {
  return countActiveTasks(tasks);
}

export function countOverdueTasks(tasks: WorkTask[]): number {
  return tasks.filter((task) => isTaskOverdue(task.dueDate, task.status)).length;
}

export function countMyTasks(tasks: WorkTask[], currentMembershipId?: string | null): number {
  return currentMembershipId
    ? tasks.filter((task) => task.assigneeMembershipId === currentMembershipId).length
    : 0;
}

export function hasActiveTaskFilters(filters: TaskFilters): boolean {
  return (
    filters.statuses.length > 0
    || filters.priorities.length > 0
    || filters.assignee !== "any"
    || filters.deadline !== "any"
    || filters.tagIds.length > 0
  );
}

export function resolveCurrentMembershipId(
  assignable: AssignableMember[],
  userEmail?: string | null,
): string | null {
  if (!userEmail) {
    return null;
  }
  return assignable.find((member) => member.email === userEmail)?.membershipId ?? null;
}

export function taskDraftChanged(
  draft: {
    title: string;
    description: string;
    status: TaskStatus;
    priority: TaskPriority;
    dueDate: string;
    assigneeMembershipId: string;
    tagIds: string[];
  },
  task: WorkTask,
): boolean {
  return (
    draft.title.trim() !== task.title
    || (draft.description ?? "") !== (task.description ?? "")
    || draft.status !== task.status
    || normalizePriority(draft.priority) !== normalizePriority(task.priority)
    || (draft.dueDate || "") !== (task.dueDate ?? "")
    || (draft.assigneeMembershipId || "") !== (task.assigneeMembershipId ?? "")
    || JSON.stringify([...draft.tagIds].sort()) !== JSON.stringify((task.tags?.map((tag) => tag.tagId) ?? []).sort())
  );
}
