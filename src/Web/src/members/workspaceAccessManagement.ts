import {
  removeWorkspaceAccess,
  replaceWorkspaceAccess,
  setWorkspaceAccess,
} from "../api/client";
import type { WorkspaceAccess, WorkspaceAccessLevel } from "../api/types";

export type AccessDraftLevel = "View" | "Edit" | "None";

export type WorkspaceAccessDraftRow = {
  workspaceId: string;
  name: string;
  accessLevel: AccessDraftLevel;
};

export type WorkspaceAccessFilter = "all" | "hasAccess" | "noAccess";
export type WorkspaceAccessSort = "nameAsc" | "nameDesc" | "accessAsc" | "accessDesc";

const ACCESS_ORDER: Record<AccessDraftLevel, number> = {
  Edit: 0,
  View: 1,
  None: 2,
};

export function buildWorkspaceAccessDraft(
  workspaces: ReadonlyArray<{ workspaceId: string; name: string }>,
  grants: ReadonlyArray<WorkspaceAccess>,
): WorkspaceAccessDraftRow[] {
  return workspaces.map((workspace) => {
    const grant = grants.find((item) => item.workspaceId === workspace.workspaceId);
    return {
      workspaceId: workspace.workspaceId,
      name: workspace.name,
      accessLevel: grant?.accessLevel ?? "None",
    };
  });
}

export function filterWorkspaceAccessDraft(
  rows: ReadonlyArray<WorkspaceAccessDraftRow>,
  query: string,
  accessFilter: WorkspaceAccessFilter,
): WorkspaceAccessDraftRow[] {
  const normalized = query.trim().toLowerCase();
  return rows.filter((row) => {
    if (accessFilter === "hasAccess" && row.accessLevel === "None") {
      return false;
    }
    if (accessFilter === "noAccess" && row.accessLevel !== "None") {
      return false;
    }
    if (normalized && !row.name.toLowerCase().includes(normalized)) {
      return false;
    }
    return true;
  });
}

export function sortWorkspaceAccessDraft(
  rows: ReadonlyArray<WorkspaceAccessDraftRow>,
  sort: WorkspaceAccessSort,
): WorkspaceAccessDraftRow[] {
  const next = [...rows];
  next.sort((left, right) => {
    switch (sort) {
      case "nameDesc":
        return right.name.localeCompare(left.name);
      case "accessAsc":
        return ACCESS_ORDER[left.accessLevel] - ACCESS_ORDER[right.accessLevel]
          || left.name.localeCompare(right.name);
      case "accessDesc":
        return ACCESS_ORDER[right.accessLevel] - ACCESS_ORDER[left.accessLevel]
          || left.name.localeCompare(right.name);
      case "nameAsc":
      default:
        return left.name.localeCompare(right.name);
    }
  });
  return next;
}

export function applyBulkAccessLevel(
  rows: ReadonlyArray<WorkspaceAccessDraftRow>,
  accessLevel: AccessDraftLevel,
  workspaceIds?: ReadonlySet<string>,
): WorkspaceAccessDraftRow[] {
  return rows.map((row) =>
    workspaceIds && !workspaceIds.has(row.workspaceId)
      ? row
      : { ...row, accessLevel },
  );
}

export function countExplicitWorkspaceAccess(
  rows: ReadonlyArray<WorkspaceAccessDraftRow>,
): number {
  return rows.filter((row) => row.accessLevel !== "None").length;
}

export async function saveWorkspaceAccessChanges(
  token: string,
  tenantId: string,
  membershipId: string,
  existing: ReadonlyArray<WorkspaceAccess>,
  draft: ReadonlyArray<WorkspaceAccessDraftRow>,
  options?: { useBatch?: boolean },
): Promise<void> {
  if (options?.useBatch !== false) {
    const grants = draft.map((row) => ({
      workspaceId: row.workspaceId,
      accessLevel: row.accessLevel === "None" ? null : row.accessLevel,
    }));
    const changed = draft.some((row) => {
      const current = existing.find((item) => item.workspaceId === row.workspaceId);
      if (row.accessLevel === "None") {
        return Boolean(current);
      }
      return !current || current.accessLevel !== row.accessLevel;
    });
    if (changed) {
      await replaceWorkspaceAccess(token, tenantId, membershipId, grants);
    }
    return;
  }

  for (const row of draft) {
    const current = existing.find((item) => item.workspaceId === row.workspaceId);
    if (row.accessLevel === "None") {
      if (current) {
        await removeWorkspaceAccess(token, tenantId, membershipId, row.workspaceId);
      }
      continue;
    }
    if (!current || current.accessLevel !== row.accessLevel) {
      await setWorkspaceAccess(
        token,
        tenantId,
        membershipId,
        row.workspaceId,
        row.accessLevel as WorkspaceAccessLevel,
      );
    }
  }
}

export function workspaceMemberAccessLabelKey(
  hasImplicitWorkspaceAccess: boolean,
  effectiveAccess: string,
): string {
  if (hasImplicitWorkspaceAccess) {
    return "members:fullAccess";
  }
  if (effectiveAccess === "View") {
    return "members:access.view";
  }
  if (effectiveAccess === "Edit") {
    return "members:access.edit";
  }
  return "members:access.none";
}

export function isAssignableWorkspaceMember(member: {
  role: string;
  status: string;
  hasImplicitWorkspaceAccess: boolean;
}): boolean {
  return member.status === "Active" && member.role === "Member" && !member.hasImplicitWorkspaceAccess;
}
