import { describe, expect, it } from "vitest";

import {
  resolveWorkspaceAccessDisplay,
  workspaceAccessLabelKey,
} from "./workspaceAccess";

describe("workspaceAccess", () => {
  it("maps Owner/Admin membership to full access display", () => {
    expect(resolveWorkspaceAccessDisplay({ accessLevel: "Edit" }, "Owner")).toBe("full");
    expect(resolveWorkspaceAccessDisplay({ accessLevel: "View" }, "Admin")).toBe("full");
  });

  it("maps Member workspace access to can edit or view only", () => {
    expect(resolveWorkspaceAccessDisplay({ accessLevel: "Edit" }, "Member")).toBe("canEdit");
    expect(resolveWorkspaceAccessDisplay({ accessLevel: "View" }, "Member")).toBe("viewOnly");
  });

  it("resolves locale keys for each display state", () => {
    expect(workspaceAccessLabelKey("full")).toBe("workspaces:accessFull");
    expect(workspaceAccessLabelKey("canEdit")).toBe("workspaces:accessCanEdit");
    expect(workspaceAccessLabelKey("viewOnly")).toBe("workspaces:accessViewOnly");
  });
});
