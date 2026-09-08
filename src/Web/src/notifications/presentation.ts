import type { TFunction } from "i18next";

import type { GlobalNotification } from "../api/types";

const GENERIC_TASK_TITLES = new Set([
  "notification",
  "task",
  "unknown",
  "a task",
  "taskassigned",
  "taskreassigned",
  "taskcommentadded",
  "taskprioritychanged",
  "taskdeadlinechanged",
  "taskstatuschanged",
  "tasktagchanged",
  "taskupdated",
  "taskclosed",
  "taskreopened",
]);

export function resolveNotificationTaskTitle(
  rawTitle: string | null | undefined,
  t: TFunction,
): string {
  const trimmed = rawTitle?.trim();
  if (!trimmed) {
    return t("notifications:unknownTask");
  }

  if (GENERIC_TASK_TITLES.has(trimmed.toLocaleLowerCase("en"))) {
    return t("notifications:unknownTask");
  }

  return trimmed;
}

export function formatNotificationBadgeCount(count: number): string | null {
  if (count <= 0) {
    return null;
  }
  if (count > 99) {
    return "99+";
  }
  return String(count);
}

export function notificationTypeLabel(type: string, t: TFunction): string {
  return t(`notifications:typeLabels.${type}`, { defaultValue: type });
}

export function notificationMessage(notification: GlobalNotification, t: TFunction): string {
  const taskTitle = resolveNotificationTaskTitle(notification.taskTitle, t);
  const key = `notifications:messages.${notification.type}`;
  const translated = t(key, { taskTitle, defaultValue: "" });
  if (translated) {
    return translated;
  }
  return t("notifications:messages.TaskUpdated", { taskTitle });
}

export function notificationMetaLine(
  notification: GlobalNotification,
  timeLabel: string,
  t: TFunction,
): string {
  if (notification.projectName?.trim()) {
    return t("notifications:metaWithProject", {
      project: notification.projectName,
      time: timeLabel,
    });
  }
  return timeLabel;
}

export function notificationHeaderLine(notification: GlobalNotification, t: TFunction): string {
  return t("notifications:headerLine", {
    organization: notification.tenantName,
    type: notificationTypeLabel(notification.type, t),
  });
}
