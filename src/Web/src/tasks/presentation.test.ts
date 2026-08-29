import { describe, expect, it } from "vitest";

import {
  formatDateTimeUtc,
  formatTaskDate,
  isTaskOverdue,
  normalizePriority,
  priorityLabelKey,
  priorityMarker,
  todayDateOnly,
} from "./presentation";

describe("task presentation", () => {
  it("maps Medium to Normal and keeps valid priorities", () => {
    expect(normalizePriority("Medium")).toBe("Normal");
    expect(normalizePriority("Urgent")).toBe("Urgent");
    expect(normalizePriority("unknown")).toBe("Normal");
  });

  it("exposes a marker and label key for each priority", () => {
    expect(priorityMarker("Low")).toBe("○");
    expect(priorityMarker("Normal")).toBe("●");
    expect(priorityMarker("High")).toBe("▲");
    expect(priorityMarker("Urgent")).toBe("!");
    expect(priorityLabelKey("Normal")).toBe("priority.normal");
  });

  it("marks incomplete past-due tasks overdue without treating Closed as overdue", () => {
    expect(isTaskOverdue("2020-01-01", "Open", "2026-08-29")).toBe(true);
    expect(isTaskOverdue("2020-01-01", "Resolved", "2026-08-29")).toBe(true);
    expect(isTaskOverdue("2020-01-01", "Closed", "2026-08-29")).toBe(false);
    expect(isTaskOverdue("2026-09-01", "Open", "2026-08-29")).toBe(false);
    expect(isTaskOverdue(null, "Open", "2026-08-29")).toBe(false);
  });

  it("formats stored UTC timestamps into a localized date and time", () => {
    const formatted = formatDateTimeUtc("2026-08-29T11:13:00Z", "en-US");
    expect(formatted).toMatch(/29/);
    expect(formatted).toMatch(/2026/);
  });

  it("formats date-only values without a UTC day shift", () => {
    expect(formatTaskDate("2026-09-15", "en-US")).toContain("15");
    expect(todayDateOnly(new Date(2026, 7, 29))).toBe("2026-08-29");
  });
});
