import { describe, expect, it } from "vitest";

import { resolveActiveTenantId } from "./activeTenant";

const tenants = [{ tenantId: "aaa" }, { tenantId: "bbb" }];

describe("resolveActiveTenantId", () => {
  it("prefers a valid route tenant", () => {
    expect(resolveActiveTenantId(tenants, "bbb", "aaa")).toBe("bbb");
  });

  it("uses stored tenant when the route is empty", () => {
    expect(resolveActiveTenantId(tenants, undefined, "aaa")).toBe("aaa");
  });

  it("ignores unknown ids", () => {
    expect(resolveActiveTenantId(tenants, "zzz", "missing")).toBeUndefined();
  });
});
