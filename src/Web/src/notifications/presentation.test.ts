import { describe, expect, it } from "vitest";

import type { GlobalNotification } from "../api/types";
import {
  formatNotificationBadgeCount,
  notificationHeaderLine,
  notificationMessage,
} from "./presentation";

const sample: GlobalNotification = {
  notificationId: "11111111-1111-1111-1111-111111111111",
  tenantId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  tenantName: "Patris",
  type: "TaskAssigned",
  taskId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  workspaceId: "cccccccc-cccc-cccc-cccc-cccccccccccc",
  projectId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
  taskTitle: "Hospital Dashboard",
  projectName: "Hospital Project",
  isRead: false,
  targetAvailable: true,
  createdAtUtc: "2026-08-29T12:00:00Z",
};

describe("notification presentation", () => {
  it("formats badge counts including compact overflow", () => {
    expect(formatNotificationBadgeCount(0)).toBeNull();
    expect(formatNotificationBadgeCount(3)).toBe("3");
    expect(formatNotificationBadgeCount(120)).toBe("99+");
  });

  it("builds organization and message lines from inbox data", () => {
    const t = ((key: string, options?: Record<string, unknown>) => {
      if (key === "notifications:headerLine") {
        return `${options?.organization} · ${options?.type}`;
      }
      if (key === "notifications:messages.TaskAssigned") {
        return `${options?.taskTitle} was assigned to you`;
      }
      if (key === "notifications:typeLabels.TaskAssigned") {
        return "Task assigned";
      }
      if (key === "notifications:unknownTask") {
        return "A task";
      }
      return key;
    }) as never;

    expect(notificationHeaderLine(sample, t)).toBe("Patris · Task assigned");
    expect(notificationMessage(sample, t)).toBe("Hospital Dashboard was assigned to you");
    expect(notificationMessage({ ...sample, taskTitle: "notification" }, t)).toBe(
      "A task was assigned to you",
    );
    expect(notificationMessage({ ...sample, taskTitle: null }, t)).toBe("A task was assigned to you");
  });
});
