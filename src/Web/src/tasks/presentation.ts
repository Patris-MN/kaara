import type { TaskPriority, TaskStatus } from "../api/types";

export const TASK_PRIORITIES: readonly TaskPriority[] = ["Low", "Normal", "High", "Urgent"];

const PRIORITY_MARKERS: Record<TaskPriority, string> = {
  Low: "○",
  Normal: "●",
  High: "▲",
  Urgent: "!",
};

export function normalizePriority(value: string): TaskPriority {
  if (value === "Medium") {
    return "Normal";
  }
  if (TASK_PRIORITIES.includes(value as TaskPriority)) {
    return value as TaskPriority;
  }
  return "Normal";
}

export function priorityLabelKey(priority: TaskPriority): `priority.${Lowercase<TaskPriority>}` {
  return `priority.${priority.toLowerCase() as Lowercase<TaskPriority>}`;
}

export function priorityMarker(priority: TaskPriority): string {
  return PRIORITY_MARKERS[priority];
}

export function priorityToneClass(priority: TaskPriority): string {
  return `task-priority-${priority.toLowerCase()}`;
}

export function todayDateOnly(now = new Date()): string {
  const year = now.getFullYear();
  const month = String(now.getMonth() + 1).padStart(2, "0");
  const day = String(now.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

export function isTaskOverdue(
  dueDate: string | null | undefined,
  status: TaskStatus,
  today = todayDateOnly(),
): boolean {
  return Boolean(dueDate && status !== "Closed" && dueDate < today);
}

export function formatDateTimeUtc(iso: string, locale?: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return iso;
  }

  return new Intl.DateTimeFormat(locale, {
    day: "numeric",
    month: "short",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  }).format(date);
}

export function formatTaskDate(isoDate: string, locale?: string): string {
  const [year, month, day] = isoDate.split("-").map(Number);
  if (!year || !month || !day) {
    return isoDate;
  }
  return new Date(year, month - 1, day).toLocaleDateString(locale, {
    month: "short",
    day: "numeric",
    year: "numeric",
  });
}
