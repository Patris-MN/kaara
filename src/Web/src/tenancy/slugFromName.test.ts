import { describe, expect, it } from "vitest";

import { isValidOrganizationIdentifier, slugFromName } from "./slugFromName";

describe("slugFromName", () => {
  it("builds a lowercase hyphenated identifier from an English name", () => {
    expect(slugFromName("Acme Corporation")).toBe("acme-corporation");
    expect(slugFromName("  Hello---World  ")).toBe("hello-world");
    expect(isValidOrganizationIdentifier(slugFromName("Acme Corporation"))).toBe(true);
  });

  it("keeps latin letters from a mixed name and does not invent a fallback", () => {
    expect(slugFromName("Acme شركة Corp")).toBe("acme-corp");
    expect(isValidOrganizationIdentifier(slugFromName("Acme شركة Corp"))).toBe(true);
  });

  it("generates a stable fallback for Arabic names", () => {
    const first = slugFromName("شركة أكمي");
    const second = slugFromName("شركة أكمي");
    expect(first).toBe(second);
    expect(first.startsWith("organization-")).toBe(true);
    expect(isValidOrganizationIdentifier(first)).toBe(true);
    expect(slugFromName("منظمة أخرى")).not.toBe(first);
  });

  it("generates a stable fallback for Kurdish Sorani names", () => {
    const identifier = slugFromName("کۆمپانیای نموونە");
    expect(identifier.startsWith("organization-")).toBe(true);
    expect(isValidOrganizationIdentifier(identifier)).toBe(true);
    expect(slugFromName("کۆمپانیای نموونە")).toBe(identifier);
  });

  it("generates a fallback for punctuation-only names and nothing for empty input", () => {
    expect(slugFromName("")).toBe("");
    expect(slugFromName("   ")).toBe("");
    const punctuation = slugFromName("!!!");
    expect(punctuation.startsWith("organization-")).toBe(true);
    expect(isValidOrganizationIdentifier(punctuation)).toBe(true);
  });

  it("respects the maximum identifier length", () => {
    const long = slugFromName("A".repeat(160));
    expect(long.length).toBeLessThanOrEqual(100);
    expect(isValidOrganizationIdentifier(long)).toBe(true);
  });
});
