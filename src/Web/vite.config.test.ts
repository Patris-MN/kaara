import { describe, expect, it } from "vitest";

import { shouldBypassInviteProxyToSpa } from "./src/invitations/inviteProxyBypass";
import viteConfig from "./vite.config";

describe("vite dev server proxy", () => {
  function proxyConfig() {
    const config = typeof viteConfig === "function"
      ? viteConfig({ mode: "development", command: "serve" })
      : viteConfig;
    return config.server?.proxy as Record<
      string,
      { target: string; bypass?: (req: { method?: string; headers?: Record<string, string> }) => string | undefined }
    > | undefined;
  }

  it("forwards account profile API routes to the backend", () => {
    const proxy = proxyConfig();
    expect(proxy?.["/account"]).toBeDefined();
    expect(proxy?.["/account"]?.target).toBe("http://localhost:5227");
  });

  it("forwards global notification API routes to the backend", () => {
    const proxy = proxyConfig();
    expect(proxy?.["/notifications"]).toBeDefined();
    expect(proxy?.["/notifications"]?.target).toBe("http://localhost:5227");
  });

  it("keeps invitation API routes on the backend while bypassing browser navigation to the SPA", () => {
    const proxy = proxyConfig();
    const inviteProxy = proxy?.["/invite"];
    expect(inviteProxy?.target).toBe("http://localhost:5227");
    expect(typeof inviteProxy?.bypass).toBe("function");

    const bypass = inviteProxy?.bypass;
    expect(
      bypass?.({
        method: "GET",
        headers: {
          accept: "text/html",
          "sec-fetch-mode": "navigate",
          "sec-fetch-dest": "document",
        },
      }),
    ).toBe("/index.html");
    expect(
      bypass?.({
        method: "GET",
        headers: {
          accept: "*/*",
          "sec-fetch-mode": "cors",
          "sec-fetch-dest": "empty",
        },
      }),
    ).toBeUndefined();
  });

  it("matches invite proxy bypass rules used by the dev server", () => {
    expect(
      shouldBypassInviteProxyToSpa({
        method: "GET",
        headers: {
          accept: "text/html",
          "sec-fetch-mode": "navigate",
          "sec-fetch-dest": "document",
        },
      }),
    ).toBe(true);
    expect(
      shouldBypassInviteProxyToSpa({
        method: "GET",
        headers: {
          accept: "*/*",
          "sec-fetch-mode": "cors",
          "sec-fetch-dest": "empty",
        },
      }),
    ).toBe(false);
  });
});
