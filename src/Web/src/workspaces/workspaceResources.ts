import type { Workspace } from "../api/types";

export type WorkspaceView = "grid" | "list";

export type WorkspaceSort =
  | "updatedDesc"
  | "createdDesc"
  | "nameAsc"
  | "nameDesc";

const VIEW_KEY_PREFIX = "pts.workspaceView.";

export function readWorkspaceView(userId: string): WorkspaceView {
  try {
    const stored = localStorage.getItem(`${VIEW_KEY_PREFIX}${userId}`);
    return stored === "list" ? "list" : "grid";
  } catch {
    return "grid";
  }
}

export function writeWorkspaceView(userId: string, view: WorkspaceView): void {
  try {
    localStorage.setItem(`${VIEW_KEY_PREFIX}${userId}`, view);
  } catch {
    // UX-only preference.
  }
}

export function filterWorkspaces(workspaces: Workspace[], query: string): Workspace[] {
  const normalized = query.trim().toLocaleLowerCase();
  if (!normalized) {
    return workspaces;
  }

  return workspaces.filter((workspace) => {
    const haystack = [workspace.name, workspace.description ?? ""]
      .join(" ")
      .toLocaleLowerCase();
    return haystack.includes(normalized);
  });
}

function compareText(left: string, right: string): number {
  return left.localeCompare(right, "en", { sensitivity: "base", numeric: true });
}

export function sortWorkspaces(workspaces: Workspace[], sort: WorkspaceSort): Workspace[] {
  const next = [...workspaces];
  next.sort((left, right) => {
    let result = 0;
    switch (sort) {
      case "updatedDesc":
        result =
          new Date(right.updatedAtUtc ?? right.createdAtUtc).getTime() -
          new Date(left.updatedAtUtc ?? left.createdAtUtc).getTime();
        break;
      case "createdDesc":
        result =
          new Date(right.createdAtUtc).getTime() - new Date(left.createdAtUtc).getTime();
        break;
      case "nameAsc":
        result = compareText(left.name, right.name);
        break;
      case "nameDesc":
        result = compareText(right.name, left.name);
        break;
      default:
        break;
    }

    if (result !== 0) {
      return result;
    }

    return compareText(left.workspaceId, right.workspaceId);
  });
  return next;
}
