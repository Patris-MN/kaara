export type MemberView = "grid" | "list";

export type MemberSort =
  | "nameAsc"
  | "nameDesc"
  | "roleAsc"
  | "statusAsc"
  | "activeTasksDesc"
  | "completedTasksDesc"
  | "completionRateDesc"
  | "joinedDesc";

const VIEW_KEY_PREFIX = "pts.memberView.";

export function readMemberView(userId: string): MemberView {
  try {
    const stored = localStorage.getItem(`${VIEW_KEY_PREFIX}${userId}`);
    return stored === "grid" ? "grid" : "list";
  } catch {
    return "list";
  }
}

export function writeMemberView(userId: string, view: MemberView): void {
  try {
    localStorage.setItem(`${VIEW_KEY_PREFIX}${userId}`, view);
  } catch {
    // UX-only preference.
  }
}

export function memberAccessSummaryKey(member: {
  hasImplicitWorkspaceAccess: boolean;
  status: string;
  workspaceAccessCount: number | null;
}): string {
  if (member.hasImplicitWorkspaceAccess) {
    return "members:fullAccess";
  }
  if (member.status === "Suspended") {
    return "members:inactiveAccess";
  }
  if (member.status === "Invited") {
    return "members:invitationPending";
  }
  if (member.status === "Removed") {
    return "members:removedAccess";
  }
  return "members:workspaceCount";
}

export function formatMemberTaskSummary(
  member: {
    completedTaskCount?: number;
    totalAssignedTaskCount?: number;
  },
  t: (key: string, options?: Record<string, unknown>) => string,
): string {
  const completed = member.completedTaskCount ?? 0;
  const total = member.totalAssignedTaskCount ?? 0;
  if (total === 0) {
    return t("members:tasks.none");
  }
  return t("members:tasks.summary", { completed, total });
}
