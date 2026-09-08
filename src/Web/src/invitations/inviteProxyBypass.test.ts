import { describe, expect, it } from "vitest";

import { shouldBypassInviteProxyToSpa } from "./inviteProxyBypass";

describe("inviteProxyBypass", () => {
  it("bypasses browser document navigation to the SPA", () => {
    expect(
      shouldBypassInviteProxyToSpa({
        method: "GET",
        headers: {
          accept: "text/html,application/xhtml+xml",
          "sec-fetch-mode": "navigate",
          "sec-fetch-dest": "document",
        },
      }),
    ).toBe(true);
  });

  it("proxies invitation preview API fetch calls to the backend", () => {
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

  it("proxies invitation accept/register POST calls", () => {
    expect(
      shouldBypassInviteProxyToSpa({
        method: "POST",
        headers: { accept: "application/json" },
      }),
    ).toBe(false);
  });
});
