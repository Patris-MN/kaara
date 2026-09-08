import { describe, expect, it } from "vitest";

import { formatActivityChange } from "./activityPresentation";

const t = ((key: string, options?: Record<string, string>) => {
  if (key === "tasks:activityTagAdded") {
    return `"${options?.tag ?? ""}"`;
  }
  if (key === "tasks:activityTagRemoved") {
    return `"${options?.tag ?? ""}"`;
  }
  if (key === "tasks:activityRemovedTag") {
    return "Removed tag";
  }
  if (key === "tasks:unassigned") {
    return "Unassigned";
  }
  if (key === "tasks:status.open") {
    return "Open";
  }
  if (key === "tasks:status.closed") {
    return "Closed";
  }
  if (key === "tasks:activityEmptyValue") {
    return "—";
  }
  return key;
}) as never;

describe("activityPresentation", () => {
  it("shows tag names instead of uuids", () => {
    const text = formatActivityChange(
      {
        activityId: "1",
        actorMembershipId: "m1",
        eventType: "TagAdded",
        actorDisplayName: "Mohammad",
        oldValue: null,
        newValue: "8d1a1dca-8bcb-4e53-9a44-0b2b6a2e6f5d",
        createdAtUtc: "2026-09-07T10:00:00Z",
      },
      {
        tags: [{ tagId: "8d1a1dca-8bcb-4e53-9a44-0b2b6a2e6f5d", name: "bug" }],
        assignable: [],
        t,
      },
    );
    expect(text).toBe('"bug"');
    expect(text).not.toContain("8d1a1dca");
  });

  it("falls back when tag is missing", () => {
    const text = formatActivityChange(
      {
        activityId: "2",
        actorMembershipId: "m1",
        eventType: "TagRemoved",
        actorDisplayName: "Sara",
        oldValue: "8d1a1dca-8bcb-4e53-9a44-0b2b6a2e6f5d",
        newValue: null,
        createdAtUtc: "2026-09-07T10:00:00Z",
      },
      { tags: [], assignable: [], t },
    );
    expect(text).toBe('"Removed tag"');
  });
});
