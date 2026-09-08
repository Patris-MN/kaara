import { describe, expect, it } from "vitest";

import {
  DEFAULT_PROJECT_ACCENT,
  projectAccentClass,
  projectMonogram,
  resolveProjectAccent,
} from "./projectIdentity";

describe("projectIdentity", () => {
  it("derives display initials from project name", () => {
    expect(projectMonogram("Example project")).toBe("EP");
    expect(projectMonogram("Alpha Beta Gamma")).toBe("AB");
  });

  it("resolves curated accent tokens with a default", () => {
    expect(resolveProjectAccent(null)).toBe(DEFAULT_PROJECT_ACCENT);
    expect(resolveProjectAccent("teal")).toBe("teal");
    expect(resolveProjectAccent("unknown")).toBe(DEFAULT_PROJECT_ACCENT);
  });

  it("maps accent tokens to css classes", () => {
    expect(projectAccentClass("teal")).toBe("project-identity project-identity-teal");
  });
});
