import type { TFunction } from "i18next";

import type { AssignableMember, TaskPriority, TaskStatus, WorkTag, WorkTaskActivity } from "../api/types";
import { isUuidLike } from "../auth/passwordPolicy";

export type ActivityPresentationContext = {
  tags: WorkTag[];
  assignable: AssignableMember[];
  t: TFunction;
};

function resolveTagName(value: string | null | undefined, context: ActivityPresentationContext): string | null {
  if (!value) {
    return null;
  }
  if (!isUuidLike(value)) {
    return value;
  }
  const tag = context.tags.find((item) => item.tagId === value);
  return tag?.name ?? context.t("tasks:activityRemovedTag");
}

function resolveAssigneeName(value: string | null | undefined, context: ActivityPresentationContext): string | null {
  if (!value) {
    return null;
  }
  if (!isUuidLike(value)) {
    return value;
  }
  const member = context.assignable.find((item) => item.membershipId === value);
  return member?.displayName ?? member?.email ?? context.t("tasks:unassigned");
}

function localizeStatus(value: string | null | undefined, context: ActivityPresentationContext): string | null {
  if (!value) {
    return null;
  }
  const key = statusTranslationKey(value as TaskStatus);
  return key ? context.t(key) : value;
}

function localizePriority(value: string | null | undefined, context: ActivityPresentationContext): string | null {
  if (!value) {
    return null;
  }
  const key = priorityTranslationKey(value as TaskPriority);
  return key ? context.t(key) : value;
}

function statusTranslationKey(status: TaskStatus): string | null {
  switch (status) {
    case "Open":
      return "tasks:status.open";
    case "InProgress":
      return "tasks:status.inProgress";
    case "Waiting":
      return "tasks:status.waiting";
    case "Resolved":
      return "tasks:status.resolved";
    case "Closed":
      return "tasks:status.closed";
    default:
      return null;
  }
}

function priorityTranslationKey(priority: TaskPriority): string | null {
  switch (priority) {
    case "Low":
      return "tasks:priority.low";
    case "Normal":
      return "tasks:priority.normal";
    case "High":
      return "tasks:priority.high";
    case "Urgent":
      return "tasks:priority.urgent";
    default:
      return null;
  }
}

export function formatActivityValue(
  eventType: WorkTaskActivity["eventType"],
  value: string | null | undefined,
  context: ActivityPresentationContext,
): string | null {
  if (!value) {
    return null;
  }

  switch (eventType) {
    case "TagAdded":
    case "TagRemoved":
      return resolveTagName(value, context);
    case "AssigneeChanged":
      return resolveAssigneeName(value, context);
    case "StatusChanged":
    case "TaskReopened":
      return localizeStatus(value, context);
    case "PriorityChanged":
      return localizePriority(value, context);
    default:
      return isUuidLike(value) ? null : value;
  }
}

export function formatActivityChange(
  item: WorkTaskActivity,
  context: ActivityPresentationContext,
): string | null {
  const oldValue = formatActivityValue(item.eventType, item.oldValue, context);
  const newValue = formatActivityValue(item.eventType, item.newValue, context);

  if (!oldValue && !newValue) {
    return null;
  }

  if (item.eventType === "TagAdded" && newValue) {
    return context.t("tasks:activityTagAdded", { tag: newValue });
  }

  if (item.eventType === "TagRemoved") {
    const tag = oldValue ?? context.t("tasks:activityRemovedTag");
    return context.t("tasks:activityTagRemoved", { tag });
  }

  const left = oldValue ?? context.t("tasks:activityEmptyValue");
  const right = newValue ?? context.t("tasks:activityEmptyValue");
  return `${left} → ${right}`;
}
