import { describe, expect, it } from "vitest";

import { sortMemberRows, type MemberDisplayRow } from "./memberListPresentation";

const member = (
  name: string,
  role: "Owner" | "Admin" | "Member" = "Member",
  status: "Invited" | "Active" | "Suspended" = "Active",
): MemberDisplayRow => ({
  key: name,
  kind: "member",
  member: {
    membershipId: `m-${name}`,
    userId: `u-${name}`,
    email: `${name}@example.test`,
    displayName: name,
    role,
    status,
    joinedAtUtc: "2026-08-29T12:00:00Z",
    avatarUrl: null,
    hasImplicitWorkspaceAccess: false,
    workspaceAccessCount: 0,
    activeTaskCount: 0,
    completedTaskCount: 0,
    totalAssignedTaskCount: 0,
    completionRate: null,
  },
});

describe("sortMemberRows", () => {
  it("sorts by name ascending by default", () => {
    const rows = [member("Zara"), member("Ahmed"), member("Sara")];
    const sorted = sortMemberRows(rows, "nameAsc");
    expect(sorted.map((row) => (row.kind === "member" ? row.member.displayName : ""))).toEqual([
      "Ahmed",
      "Sara",
      "Zara",
    ]);
  });

  it("sorts by role then name", () => {
    const rows = [member("Bob", "Member"), member("Amy", "Admin"), member("Cal", "Owner")];
    const sorted = sortMemberRows(rows, "roleAsc");
    expect(sorted.map((row) => (row.kind === "member" ? row.member.role : ""))).toEqual([
      "Admin",
      "Member",
      "Owner",
    ]);
  });
});
