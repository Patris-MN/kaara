import type { WorkspaceAccessLevel } from "../api/types";

export type WorkspaceAccessDisplay = "full" | "canEdit" | "viewOnly";

/**
 * Maps server-derived membership role + workspace access level to a status label.
 * Owner/Admin receive implicit full resource access on the backend; Members use
 * explicit WorkspaceAccess View/Edit on each workspace response.
 */
export function resolveWorkspaceAccessDisplay(
  workspace: { accessLevel: WorkspaceAccessLevel },
  membershipRole: string | undefined,
): WorkspaceAccessDisplay {
  if (membershipRole === "Owner" || membershipRole === "Admin") {
    return "full";
  }

  return workspace.accessLevel === "Edit" ? "canEdit" : "viewOnly";
}

export function workspaceAccessLabelKey(display: WorkspaceAccessDisplay): string {
  switch (display) {
    case "full":
      return "workspaces:accessFull";
    case "canEdit":
      return "workspaces:accessCanEdit";
    default:
      return "workspaces:accessViewOnly";
  }
}
