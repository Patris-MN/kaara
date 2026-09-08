import type { PendingInvitation, TenantMember } from "../api/types";

export type MemberSort =
  | "nameAsc"
  | "nameDesc"
  | "roleAsc"
  | "statusAsc"
  | "activeTasksDesc"
  | "completedTasksDesc"
  | "completionRateDesc"
  | "joinedDesc";

export type MemberDisplayRow =
  | { key: string; kind: "member"; member: TenantMember }
  | { key: string; kind: "invitation"; invitation: PendingInvitation };

function rowLabel(row: MemberDisplayRow): string {
  if (row.kind === "member") {
    return row.member.displayName || row.member.email;
  }
  return row.invitation.invitedEmail;
}

function rowRole(row: MemberDisplayRow): string {
  return row.kind === "member" ? row.member.role : row.invitation.role;
}

function rowStatus(row: MemberDisplayRow): string {
  return row.kind === "member" ? row.member.status : "Invited";
}

function compareText(left: string, right: string): number {
  return left.localeCompare(right, "en", { sensitivity: "base", numeric: true });
}

export function sortMemberRows(rows: MemberDisplayRow[], sort: MemberSort): MemberDisplayRow[] {
  const next = [...rows];
  next.sort((left, right) => {
    switch (sort) {
      case "nameDesc":
        return compareText(rowLabel(right), rowLabel(left));
      case "roleAsc":
        return compareText(rowRole(left), rowRole(right)) || compareText(rowLabel(left), rowLabel(right));
      case "statusAsc":
        return compareText(rowStatus(left), rowStatus(right)) || compareText(rowLabel(left), rowLabel(right));
      case "activeTasksDesc":
        return (
          compareNumeric(rowActiveTasks(right), rowActiveTasks(left)) ||
          compareText(rowLabel(left), rowLabel(right))
        );
      case "completedTasksDesc":
        return (
          compareNumeric(rowCompletedTasks(right), rowCompletedTasks(left)) ||
          compareText(rowLabel(left), rowLabel(right))
        );
      case "completionRateDesc":
        return (
          compareNumeric(rowCompletionRate(right), rowCompletionRate(left)) ||
          compareText(rowLabel(left), rowLabel(right))
        );
      case "joinedDesc":
        return compareText(rowJoinedAt(right), rowJoinedAt(left)) || compareText(rowLabel(left), rowLabel(right));
      case "nameAsc":
      default:
        return compareText(rowLabel(left), rowLabel(right));
    }
  });
  return next;
}

function compareNumeric(left: number, right: number): number {
  return left - right;
}

function rowActiveTasks(row: MemberDisplayRow): number {
  return row.kind === "member" ? row.member.activeTaskCount ?? 0 : 0;
}

function rowCompletedTasks(row: MemberDisplayRow): number {
  return row.kind === "member" ? row.member.completedTaskCount ?? 0 : 0;
}

function rowCompletionRate(row: MemberDisplayRow): number {
  return row.kind === "member" ? row.member.completionRate ?? -1 : -1;
}

function rowJoinedAt(row: MemberDisplayRow): string {
  return row.kind === "member" ? row.member.joinedAtUtc : "";
}
