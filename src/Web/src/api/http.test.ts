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

  it("requests global notifications on the API path used by the shared client", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ items: [], unreadCount: 0 }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      }),
    );
    vi.stubGlobal("fetch", fetchMock);

    const body = await apiRequest<{ items: unknown[]; unreadCount: number }>("/notifications", {
      token: "test-token",
    });

    expect(body).toEqual({ items: [], unreadCount: 0 });
    expect(fetchMock).toHaveBeenCalledOnce();
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("/notifications");
    expect(new Headers(init.headers).get("Authorization")).toBe("Bearer test-token");
  });

  it("marks global notifications read on the shared API path", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetchMock);

    await apiRequest<void>("/notifications/abc/read", { method: "POST", token: "test-token" });

    expect(fetchMock).toHaveBeenCalledOnce();
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("/notifications/abc/read");
    expect(init.method).toBe("POST");
  });

  it("preserves existingName from conflict responses", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ error: "workspace_name_conflict", existingName: "Development" }), {
          status: 409,
          headers: { "Content-Type": "application/json" },
        }),
      ),
    );

    await expect(apiRequest("/tenants/a/workspaces", { method: "POST", token: "t" })).rejects.toMatchObject({
      status: 409,
      code: "workspace_name_conflict",
      existingName: "Development",
    });
  });
});
