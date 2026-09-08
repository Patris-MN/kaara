import { describe, expect, it } from "vitest";

import {
  canManageOrganization,
  organizationMonogram,
  organizationWorkspaceCount,
} from "./organizationMonogram";

describe("organizationMonogram", () => {
  it("uses the first letter of the first two words", () => {
    expect(organizationMonogram("Soft Vision")).toBe("SV");
    expect(organizationMonogram("Acme Corporation")).toBe("AC");
  });

  it("uses the first two characters of a single word", () => {
    expect(organizationMonogram("SoftVision")).toBe("SO");
  });

  it("falls back for a single grapheme or empty name", () => {
    expect(organizationMonogram("S")).toBe("S");
    expect(organizationMonogram("   ")).toBe("?");
  });

  it("uses the first letters of a non-Latin name", () => {
    expect(organizationMonogram("شركة أكمي")).toBe("شأ");
  });
});

describe("organization directory capabilities", () => {
  it("treats missing workspace counts as zero", () => {
    expect(organizationWorkspaceCount({})).toBe(0);
    expect(organizationWorkspaceCount({ workspaceCount: 4 })).toBe(4);
  });

  it("does not grant management from missing capability data", () => {
    expect(canManageOrganization({})).toBe(false);
    expect(canManageOrganization({ canManage: false })).toBe(false);
    expect(canManageOrganization({ canManage: true })).toBe(true);
  });
});
