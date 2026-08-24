import { describe, expect, it } from "vitest";

import { shouldApplyResponse } from "./requestIdentity";

describe("shouldApplyResponse", () => {
  it("accepts the current request and rejects a stale one", () => {
    expect(shouldApplyResponse(2, 2)).toBe(true);
    expect(shouldApplyResponse(1, 2)).toBe(false);
  });
});
