import { describe, expect, it } from "vitest";

import { evaluatePassword, isUuidLike, passwordsMatch } from "./passwordPolicy";

describe("passwordPolicy", () => {
  it("requires at least eight characters", () => {
    expect(evaluatePassword("short", true).valid).toBe(false);
    expect(evaluatePassword("long-enough", true).valid).toBe(true);
  });

  it("does not mark requirements satisfied before typing", () => {
    expect(evaluatePassword("", false).requirements[0]?.satisfied).toBe(false);
  });

  it("reports password confirmation match", () => {
    expect(passwordsMatch("abcdefgh", "abcdefgh", true)).toBe(true);
    expect(passwordsMatch("abcdefgh", "abcdefghi", true)).toBe(false);
    expect(passwordsMatch("abcdefgh", "", false)).toBeNull();
  });

  it("detects uuid-like values", () => {
    expect(isUuidLike("8d1a1dca-8bcb-4e53-9a44-0b2b6a2e6f5d")).toBe(true);
    expect(isUuidLike("bug")).toBe(false);
  });
});
