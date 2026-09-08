import type { Project } from "../api/types";

export type ProjectView = "grid" | "list";

export type ProjectSort = "nameAsc" | "nameDesc" | "openTasksDesc" | "createdDesc";

const VIEW_KEY_PREFIX = "pts.projectView.";

export function readProjectView(userId: string): ProjectView {
  try {
    const stored = localStorage.getItem(`${VIEW_KEY_PREFIX}${userId}`);
    return stored === "list" ? "list" : "grid";
  } catch {
    return "grid";
  }
}

export function writeProjectView(userId: string, view: ProjectView): void {
  try {
    localStorage.setItem(`${VIEW_KEY_PREFIX}${userId}`, view);
  } catch {
    // UX-only preference.
  }
}

export function filterProjects(projects: Project[], query: string): Project[] {
  const normalized = query.trim().toLocaleLowerCase();
  if (!normalized) {
    return projects;
  }

  return projects.filter((project) => {
    const haystack = [project.name, project.description ?? ""]
      .join(" ")
      .toLocaleLowerCase();
    return haystack.includes(normalized);
  });
}

export function sortProjects(projects: Project[], sort: ProjectSort): Project[] {
  const next = [...projects];
  next.sort((left, right) => {
    switch (sort) {
      case "nameDesc":
        return right.name.localeCompare(left.name);
      case "openTasksDesc":
        return (right.openTaskCount ?? 0) - (left.openTaskCount ?? 0)
          || left.name.localeCompare(right.name);
      case "createdDesc":
        return new Date(right.createdAtUtc).getTime() - new Date(left.createdAtUtc).getTime();
      case "nameAsc":
      default:
        return left.name.localeCompare(right.name);
    }
  });
  return next;
}

export function canCreateProject(accessLevel: string | undefined): boolean {
  return accessLevel === "Edit";
}

export function canEditProjectMetadata(role: string | undefined): boolean {
  return role === "Owner" || role === "Admin";
}
