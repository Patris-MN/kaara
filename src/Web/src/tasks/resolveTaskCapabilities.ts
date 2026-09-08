import type { TaskCapabilities, WorkTask, Workspace } from "../api/types";

const STATUSES = ["Open", "InProgress", "Waiting", "Resolved", "Closed"] as const;

const conservativeCapabilities = (status: WorkTask["status"]): TaskCapabilities => ({
  canEditDefinition: false,
  canManageTags: false,
  canReassign: false,
  canComment: false,
  canDelete: false,
  allowedStatuses: [status],
});

/**
 * Server capabilities are authoritative when Workspace access allows mutation.
 * View-only workspace access denies every task mutation regardless of creator/assignee role.
 * Missing capability data must fail closed.
 */
export function resolveTaskCapabilities(
  task: WorkTask,
  accessLevel?: Workspace["accessLevel"],
): TaskCapabilities {
  if (accessLevel === "View") {
    return conservativeCapabilities(task.status);
  }
  if (task.capabilities) {
    return task.capabilities;
  }
  return conservativeCapabilities(task.status);
}

export function isTaskFullyReadOnly(capabilities: TaskCapabilities): boolean {
  return (
    !capabilities.canEditDefinition &&
    !capabilities.canManageTags &&
    !capabilities.canReassign &&
    !capabilities.canComment &&
    !capabilities.canDelete &&
    capabilities.allowedStatuses.length <= 1
  );
}

export function allTaskStatuses() {
  return [...STATUSES];
}
