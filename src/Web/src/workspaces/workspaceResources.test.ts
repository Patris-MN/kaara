import { describe, expect, it } from "vitest";

import type { Workspace } from "../api/types";
import { filterWorkspaces, sortWorkspaces } from "./workspaceResources";

const base = (overrides: Partial<Workspace>): Workspace => ({
  workspaceId: "00000000-0000-0000-0000-000000000001",
  tenantId: "11111111-1111-1111-1111-111111111111",
  name: "Alpha",
  description: null,
  startDate: null,
  createdAtUtc: "2026-08-01T10:00:00Z",
  updatedAtUtc: "2026-08-10T10:00:00Z",
  accessLevel: "Edit",
  canManage: true,
  ...overrides,
});

describe("workspaceResources", () => {
  it("filters by name and description case-insensitively", () => {
    const workspaces = [
      base({ workspaceId: "a", name: "A-Workspace-1", description: "Team Alpha" }),
      base({ workspaceId: "b", name: "B-Workspace-1", description: "Other" }),
    ];
    expect(filterWorkspaces(workspaces, "alpha")).toHaveLength(1);
    expect(filterWorkspaces(workspaces, "b-workspace")).toHaveLength(1);
    expect(filterWorkspaces(workspaces, "missing")).toHaveLength(0);
  });

  it("sorts by updated, created, and name with deterministic tie-breakers", () => {
    const workspaces = [
      base({
        workspaceId: "b",
        name: "Bravo",
        createdAtUtc: "2026-08-01T10:00:00Z",
        updatedAtUtc: "2026-08-03T10:00:00Z",
      }),
      base({
        workspaceId: "a",
        name: "Alpha",
        createdAtUtc: "2026-08-02T10:00:00Z",
        updatedAtUtc: "2026-08-05T10:00:00Z",
      }),
    ];

    expect(sortWorkspaces(workspaces, "updatedDesc").map((item) => item.workspaceId)).toEqual([
      "a",
      "b",
    ]);
    expect(sortWorkspaces(workspaces, "createdDesc").map((item) => item.workspaceId)).toEqual([
      "a",
      "b",
    ]);
    expect(sortWorkspaces(workspaces, "nameAsc").map((item) => item.name)).toEqual([
      "Alpha",
      "Bravo",
    ]);
    expect(sortWorkspaces(workspaces, "nameDesc").map((item) => item.name)).toEqual([
      "Bravo",
      "Alpha",
    ]);
  });

  it("sorts legacy workspaces without updatedAtUtc using createdAtUtc", () => {
    const workspaces = [
      base({
        workspaceId: "legacy",
        name: "Legacy",
        createdAtUtc: "2026-08-01T10:00:00Z",
        updatedAtUtc: null,
      }),
      base({
        workspaceId: "modern",
        name: "Modern",
        createdAtUtc: "2026-08-01T10:00:00Z",
        updatedAtUtc: "2026-08-20T10:00:00Z",
      }),
    ];

    expect(sortWorkspaces(workspaces, "updatedDesc").map((item) => item.workspaceId)).toEqual([
      "modern",
      "legacy",
    ]);
  });
});
