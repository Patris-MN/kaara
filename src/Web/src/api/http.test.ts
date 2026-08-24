import { afterEach, describe, expect, it, vi } from "vitest";

import { ApiError } from "./errors";
import { apiRequest, setUnauthorizedHandler } from "./http";

describe("apiRequest", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    setUnauthorizedHandler(null);
  });

  it("notifies the auth layer on 401 unless skipped", async () => {
    const unauthorized = vi.fn();
    setUnauthorizedHandler(unauthorized);
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ error: "unauthenticated" }), { status: 401 }),
      ),
    );

    await expect(apiRequest("/tenants")).rejects.toMatchObject({
      status: 401,
      code: "unauthenticated",
    });
    expect(unauthorized).toHaveBeenCalledOnce();
  });

  it("does not treat 403 as a logout", async () => {
    const unauthorized = vi.fn();
    setUnauthorizedHandler(unauthorized);
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ error: "tenant_access_denied" }), { status: 403 }),
      ),
    );

    await expect(apiRequest("/tenants/1/workspaces")).rejects.toBeInstanceOf(ApiError);
    expect(unauthorized).not.toHaveBeenCalled();
  });

  it("maps a network failure without treating it as 401", async () => {
    const unauthorized = vi.fn();
    setUnauthorizedHandler(unauthorized);
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("Failed to fetch")));

    await expect(apiRequest("/tenants")).rejects.toMatchObject({ status: 0, code: "network" });
    expect(unauthorized).not.toHaveBeenCalled();
  });
});
