import { describe, expect, it } from "vitest";

import {
  canCreateProject,
  canDeleteProject,
  canEditProjectMetadata,
  projectHasTasks,
} from "./projectResources";

describe("projectResources authorization helpers", () => {
  it("allows project creation only for workspace edit access", () => {
    expect(canCreateProject("Edit")).toBe(true);
    expect(canCreateProject("View")).toBe(false);
    expect(canCreateProject(undefined)).toBe(false);
  });

  it("allows metadata editing only for owner and admin tenant roles", () => {
    expect(canEditProjectMetadata("Owner")).toBe(true);
    expect(canEditProjectMetadata("Admin")).toBe(true);
    expect(canEditProjectMetadata("Member")).toBe(false);
    expect(canEditProjectMetadata(undefined)).toBe(false);
  });

  it("allows project deletion only for owner and admin tenant roles", () => {
    expect(canDeleteProject("Owner")).toBe(true);
    expect(canDeleteProject("Admin")).toBe(true);
    expect(canDeleteProject("Member")).toBe(false);
  });

  it("detects when a project still has tasks", () => {
    expect(projectHasTasks({ taskCount: 0 })).toBe(false);
    expect(projectHasTasks({ taskCount: 3 })).toBe(true);
  });
});
