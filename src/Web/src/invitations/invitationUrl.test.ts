import { afterEach, describe, expect, it, vi } from "vitest";

import { buildPublicInvitationUrl, normalizeInvitationLink } from "./invitationUrl";

describe("invitationUrl", () => {
  afterEach(() => {
    vi.unstubAllEnvs();
  });

  it("builds a full URL from a relative invitation path", () => {
    vi.stubEnv("VITE_APP_ORIGIN", "http://localhost:5173");
    expect(buildPublicInvitationUrl("/invite/abc123")).toBe(
      "http://localhost:5173/invite/abc123",
    );
  });

  it("builds a full URL from a raw token", () => {
    vi.stubEnv("VITE_APP_ORIGIN", "https://app.example.com");
    expect(buildPublicInvitationUrl("token-with/special")).toBe(
      "https://app.example.com/invite/token-with%2Fspecial",
    );
  });

  it("preserves absolute invitation URLs", () => {
    expect(
      normalizeInvitationLink("https://app.example.com/invite/abc"),
    ).toBe("https://app.example.com/invite/abc");
  });
});
