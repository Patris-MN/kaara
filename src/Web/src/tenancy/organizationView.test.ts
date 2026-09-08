import { afterEach, describe, expect, it } from "vitest";

import { readOrganizationView, writeOrganizationView } from "./organizationView";

describe("organizationView preference", () => {
  afterEach(() => {
    localStorage.clear();
  });

  it("defaults to grid and persists list locally", () => {
    expect(readOrganizationView("user-1")).toBe("grid");
    writeOrganizationView("user-1", "list");
    expect(readOrganizationView("user-1")).toBe("list");
    expect(readOrganizationView("user-2")).toBe("grid");
  });
});
