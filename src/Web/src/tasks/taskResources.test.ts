import { describe, expect, it } from "vitest";

import type { WorkTask } from "../api/types";
import {
  EMPTY_TASK_FILTERS,
  applyTaskFilters,
  applyTaskQuickScope,
  countMyTasks,
  countOpenTasks,
  countOverdueTasks,
  filterTasksBySearch,
  hasActiveTaskFilters,
  sortTasks,
  taskDraftChanged,
} from "./taskResources";

const membershipId = "16161616-1616-1616-1616-161616161616";

function task(overrides: Partial<WorkTask> = {}): WorkTask {
  return {
    taskId: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
    tenantId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
    workspaceId: "cccccccc-cccc-cccc-cccc-cccccccccccc",
    projectId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
    title: "Alpha",
    description: "Notes about backend",
    status: "Open",
    priority: "Normal",
    dueDate: "2026-09-01",
    createdAtUtc: "2026-08-01T00:00:00Z",
    updatedAtUtc: "2026-08-02T00:00:00Z",
    assigneeMembershipId: membershipId,
    assigneeDisplayName: "User A",
    assigneeEmail: "a@example.test",
    tags: [{ tagId: "tag-1", name: "bug" }],
    createdByMembershipId: membershipId,
    createdByDisplayName: "User A",
    createdByEmail: "a@example.test",
    unseenActivityCount: 0,
    capabilities: null,
    ...overrides,
  };
}

describe("taskResources", () => {
  const tasks = [
    task(),
    task({
      taskId: "22222222-2222-2222-2222-222222222222",
      title: "Beta",
      status: "Closed",
      priority: "High",
      dueDate: null,
      assigneeMembershipId: null,
      description: "",
      tags: [],
      updatedAtUtc: "2026-08-03T00:00:00Z",
    }),
  ];

  it("searches title, description, and tags case-insensitively", () => {
    expect(filterTasksBySearch(tasks, "backend")).toHaveLength(1);
    expect(filterTasksBySearch(tasks, "BUG")).toHaveLength(1);
    expect(filterTasksBySearch(tasks, "missing")).toHaveLength(0);
  });

  it("applies quick scopes", () => {
    expect(applyTaskQuickScope(tasks, "all")).toHaveLength(2);
    expect(applyTaskQuickScope(tasks, "mine", membershipId)).toHaveLength(1);
    expect(applyTaskQuickScope(tasks, "active")).toHaveLength(1);
  });

  it("filters by status, priority, assignee, deadline, and tags", () => {
    expect(
      applyTaskFilters(tasks, { ...EMPTY_TASK_FILTERS, statuses: ["Closed"] }, membershipId),
    ).toHaveLength(1);
    expect(
      applyTaskFilters(tasks, { ...EMPTY_TASK_FILTERS, priorities: ["High"] }, membershipId),
    ).toHaveLength(1);
    expect(
      applyTaskFilters(tasks, { ...EMPTY_TASK_FILTERS, assignee: "unassigned" }, membershipId),
    ).toHaveLength(1);
    expect(
      applyTaskFilters(tasks, { ...EMPTY_TASK_FILTERS, assignee: "me" }, membershipId),
    ).toHaveLength(1);
    expect(
      applyTaskFilters(tasks, { ...EMPTY_TASK_FILTERS, deadline: "none" }, membershipId),
    ).toHaveLength(1);
    expect(
      applyTaskFilters(tasks, { ...EMPTY_TASK_FILTERS, tagIds: ["tag-1"] }, membershipId),
    ).toHaveLength(1);
  });

  it("sorts tasks deterministically", () => {
    expect(sortTasks(tasks, "titleAsc").map((item) => item.title)).toEqual(["Alpha", "Beta"]);
    expect(sortTasks(tasks, "priorityDesc")[0]?.priority).toBe("High");
    expect(sortTasks(tasks, "updatedDesc")[0]?.title).toBe("Beta");
  });

  it("counts summary scopes", () => {
    expect(countOpenTasks(tasks)).toBe(1);
    expect(countMyTasks(tasks, membershipId)).toBe(1);
    expect(countOverdueTasks(tasks)).toBeGreaterThanOrEqual(0);
  });

  it("detects active filters and draft changes", () => {
    expect(hasActiveTaskFilters(EMPTY_TASK_FILTERS)).toBe(false);
    expect(hasActiveTaskFilters({ ...EMPTY_TASK_FILTERS, assignee: "me" })).toBe(true);
    expect(
      taskDraftChanged(
        {
          title: "Alpha",
          description: "Notes about backend",
          status: "Open",
          priority: "Normal",
          dueDate: "2026-09-01",
          assigneeMembershipId: membershipId,
          tagIds: ["tag-1"],
        },
        tasks[0]!,
      ),
    ).toBe(false);
    expect(
      taskDraftChanged(
        {
          title: "Changed",
          description: "Notes about backend",
          status: "Open",
          priority: "Normal",
          dueDate: "2026-09-01",
          assigneeMembershipId: membershipId,
          tagIds: ["tag-1"],
        },
        tasks[0]!,
      ),
    ).toBe(true);
  });
});
