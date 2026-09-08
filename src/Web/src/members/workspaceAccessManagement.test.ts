import { describe, expect, it } from "vitest";

import {
  applyBulkAccessLevel,
  buildWorkspaceAccessDraft,
  countExplicitWorkspaceAccess,
  filterWorkspaceAccessDraft,
  isAssignableWorkspaceMember,
  sortWorkspaceAccessDraft,
  workspaceMemberAccessLabelKey,
} from "./workspaceAccessManagement";

describe("workspaceAccessManagement", () => {
  const workspaces = [
    { workspaceId: "ws-a", name: "Development" },
    { workspaceId: "ws-b", name: "Marketing" },
    { workspaceId: "ws-c", name: "Finance" },
  ];

  it("builds draft rows from explicit grants", () => {
    const draft = buildWorkspaceAccessDraft(workspaces, [
      { membershipId: "m1", workspaceId: "ws-a", accessLevel: "Edit" },
      { membershipId: "m1", workspaceId: "ws-b", accessLevel: "View" },
    ]);
    expect(draft).toEqual([
      { workspaceId: "ws-a", name: "Development", accessLevel: "Edit" },
      { workspaceId: "ws-b", name: "Marketing", accessLevel: "View" },
      { workspaceId: "ws-c", name: "Finance", accessLevel: "None" },
    ]);
  });

  it("filters and sorts draft rows", () => {
    const draft = buildWorkspaceAccessDraft(workspaces, [
      { membershipId: "m1", workspaceId: "ws-a", accessLevel: "Edit" },
    ]);
    const filtered = filterWorkspaceAccessDraft(draft, "finance", "noAccess");
    expect(filtered).toEqual([
      { workspaceId: "ws-c", name: "Finance", accessLevel: "None" },
    ]);

    const hasAccess = filterWorkspaceAccessDraft(draft, "", "hasAccess");
    expect(hasAccess).toHaveLength(1);

    const sorted = sortWorkspaceAccessDraft(draft, "accessAsc");
    expect(sorted[0]?.accessLevel).toBe("Edit");
  });

  it("applies bulk access to visible rows only", () => {
    const draft = buildWorkspaceAccessDraft(workspaces, []);
    const next = applyBulkAccessLevel(draft, "View", new Set(["ws-a", "ws-c"]));
    expect(next).toEqual([
      { workspaceId: "ws-a", name: "Development", accessLevel: "View" },
      { workspaceId: "ws-b", name: "Marketing", accessLevel: "None" },
      { workspaceId: "ws-c", name: "Finance", accessLevel: "View" },
    ]);
  });

  it("counts explicit workspace grants", () => {
    const draft = buildWorkspaceAccessDraft(workspaces, [
      { membershipId: "m1", workspaceId: "ws-a", accessLevel: "Edit" },
      { membershipId: "m1", workspaceId: "ws-b", accessLevel: "View" },
    ]);
    expect(countExplicitWorkspaceAccess(draft)).toBe(2);
  });

  it("identifies assignable workspace members", () => {
    expect(
      isAssignableWorkspaceMember({
        role: "Member",
        status: "Active",
        hasImplicitWorkspaceAccess: false,
      }),
    ).toBe(true);
    expect(
      isAssignableWorkspaceMember({
        role: "Admin",
        status: "Active",
        hasImplicitWorkspaceAccess: true,
      }),
    ).toBe(false);
  });

  it("maps workspace member access labels", () => {
    expect(workspaceMemberAccessLabelKey(true, "Full")).toBe("members:fullAccess");
    expect(workspaceMemberAccessLabelKey(false, "View")).toBe("members:access.view");
    expect(workspaceMemberAccessLabelKey(false, "None")).toBe("members:access.none");
  });
});
