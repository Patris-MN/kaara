import { describe, expect, it } from "vitest";

import type { TaskCapabilities, WorkTask } from "../api/types";
import { isTaskFullyReadOnly, resolveTaskCapabilities } from "./resolveTaskCapabilities";

const task: WorkTask = {
  taskId: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
  tenantId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  workspaceId: "cccccccc-cccc-cccc-cccc-cccccccccccc",
  projectId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
  title: "Kickoff",
  description: null,
  status: "Open",
  priority: "Normal",
  dueDate: null,
  createdAtUtc: "2026-08-01T00:00:00Z",
  updatedAtUtc: "2026-08-01T00:00:00Z",
  assigneeMembershipId: null,
  assigneeDisplayName: null,
  assigneeEmail: null,
  tags: [],
  createdByMembershipId: "16161616-1616-1616-1616-161616161616",
  createdByDisplayName: "User A",
  createdByEmail: "a@example.test",
  unseenActivityCount: 0,
  capabilities: null,
};

const creatorCaps: TaskCapabilities = {
  canEditDefinition: true,
  canManageTags: true,
  canReassign: true,
  canComment: true,
  canDelete: true,
  allowedStatuses: ["Open", "Closed"],
};

describe("resolveTaskCapabilities", () => {
  it("uses server capabilities when workspace access is Edit", () => {
    expect(resolveTaskCapabilities({ ...task, capabilities: creatorCaps }, "Edit")).toEqual(creatorCaps);
  });

  it("denies every mutation when workspace access is View even if server caps are permissive", () => {
    const resolved = resolveTaskCapabilities({ ...task, capabilities: creatorCaps }, "View");
    expect(resolved.canEditDefinition).toBe(false);
    expect(resolved.canManageTags).toBe(false);
    expect(resolved.canReassign).toBe(false);
    expect(resolved.canComment).toBe(false);
    expect(resolved.canDelete).toBe(false);
    expect(resolved.allowedStatuses).toEqual(["Open"]);
    expect(isTaskFullyReadOnly(resolved)).toBe(true);
  });

  it("does not grant creator-like controls when capabilities are missing", () => {
    const resolved = resolveTaskCapabilities(task, "Edit");
    expect(resolved.canEditDefinition).toBe(false);
    expect(resolved.canManageTags).toBe(false);
    expect(resolved.canReassign).toBe(false);
    expect(resolved.canComment).toBe(false);
    expect(resolved.canDelete).toBe(false);
    expect(resolved.allowedStatuses).toEqual(["Open"]);
    expect(isTaskFullyReadOnly(resolved)).toBe(true);
  });
});
