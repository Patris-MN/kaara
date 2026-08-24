import { describe, expect, it } from "vitest";

import { getDirectionForLocale } from "./direction";

describe("getDirectionForLocale", () => {
  it("returns ltr for English", () => {
    expect(getDirectionForLocale("en")).toBe("ltr");
  });

  it("returns rtl for Arabic", () => {
    expect(getDirectionForLocale("ar")).toBe("rtl");
  });

  it("returns rtl for Kurdish Sorani", () => {
    expect(getDirectionForLocale("ku")).toBe("rtl");
  });

  it("falls back to ltr for an unsupported locale", () => {
    expect(getDirectionForLocale("fr")).toBe("ltr");
  });
});
