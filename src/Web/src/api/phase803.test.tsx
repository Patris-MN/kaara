import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import "../i18n";
import App from "../App";
import { AuthProvider } from "../auth/AuthProvider";
import enNotifications from "../locales/en/notifications.json";
import enTenants from "../locales/en/tenants.json";
import { TenantDirectoryProvider } from "../tenancy/TenantDirectoryProvider";
import { deriveAccountCapabilities } from "../test/directoryFetchHandlers";
import { clearSession, writeAccessToken } from "./session";
import type { GlobalNotification } from "./types";

const authUser = {
  userId: "11111111-1111-1111-1111-111111111111",
  email: "a@example.test",
  displayName: "User A",
  isPlatformAdministrator: false,
};

const tenantA = {
  tenantId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  name: "Patris",
  slug: "patris",
  role: "Owner",
  status: "Active",
};

const tenantB = {
  tenantId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  name: "FastPay",
  slug: "fastpay",
  role: "Member",
  status: "Active",
};

function notification(
  overrides: Partial<GlobalNotification> & Pick<GlobalNotification, "notificationId" | "tenantId" | "tenantName">,
): GlobalNotification {
  return {
    type: "TaskAssigned",
    taskId: "cccccccc-cccc-cccc-cccc-cccccccccccc",
    workspaceId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
    projectId: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
    taskTitle: "Hospital Dashboard",
    projectName: "Hospital Project",
    isRead: false,
    targetAvailable: true,
    createdAtUtc: "2026-08-31T09:00:00Z",
    ...overrides,
  };
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function pathOf(input: RequestInfo | URL) {
  const raw = String(input);
  return raw.startsWith("http") ? new URL(raw).pathname : raw;
}

function inbox(items: GlobalNotification[]) {
  return json({
    items,
    unreadCount: items.filter((item) => !item.isRead).length,
  });
}

function renderApp(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <AuthProvider>
        <TenantDirectoryProvider>
          <App />
        </TenantDirectoryProvider>
      </AuthProvider>
    </MemoryRouter>,
  );
}

describe("phase 8.0.3 global notification inbox", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    clearSession();
    cleanup();
  });

  it("loads global inbox without a selected organization and shows unread badge", async () => {
    const items = [
      notification({
        notificationId: "n1",
        tenantId: tenantA.tenantId,
        tenantName: tenantA.name,
      }),
      notification({
        notificationId: "n2",
        tenantId: tenantB.tenantId,
        tenantName: tenantB.name,
        taskTitle: "Payment API",
        isRead: true,
      }),
    ];

    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const path = pathOf(input);
        if (path.endsWith("/auth/me")) return json(authUser);
        if (path === "/tenants") return json([tenantA, tenantB]);
        if (path.endsWith("/invitations")) return json([]);
        if (path === "/account/capabilities") {
          return json(deriveAccountCapabilities([tenantA, tenantB]));
        }
        if (path === "/notifications") return inbox(items);
        return json({ error: "missing" }, 404);
      }),
    );

    renderApp("/app");
    expect(await screen.findByLabelText(enTenants.selector)).toBeTruthy();
    await waitFor(() => {
      expect(screen.getByRole("button", { name: /Notifications, 1 unread/i })).toBeTruthy();
    });
  });

  it("shows organization context, message, timestamp, and unread styling", async () => {
    const user = userEvent.setup();
    const items = [
      notification({
        notificationId: "n1",
        tenantId: tenantA.tenantId,
        tenantName: tenantA.name,
      }),
    ];

    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const path = pathOf(input);
        if (path.endsWith("/auth/me")) return json(authUser);
        if (path === "/tenants") return json([tenantA]);
        if (path.endsWith("/invitations")) return json([]);
        if (path === "/account/capabilities") {
          return json(deriveAccountCapabilities([tenantA]));
        }
        if (path === "/notifications") return inbox(items);
        return json({ error: "missing" }, 404);
      }),
    );

    renderApp("/app");
    await user.click(await screen.findByRole("button", { name: /Notifications, 1 unread/i }));
    expect(screen.getByText("Patris · Task assigned")).toBeTruthy();
    expect(screen.getByText("Hospital Dashboard was assigned to you")).toBeTruthy();
    expect(document.querySelector(".notification-unread-dot")).toBeTruthy();
    expect(screen.queryByText(enNotifications.markRead)).toBeNull();
  });

  it("shows error state instead of empty when inbox fetch fails", async () => {
    const user = userEvent.setup();

    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const path = pathOf(input);
        if (path.endsWith("/auth/me")) return json(authUser);
        if (path === "/tenants") return json([tenantA]);
        if (path.endsWith("/invitations")) return json([]);
        if (path === "/account/capabilities") {
          return json(deriveAccountCapabilities([tenantA]));
        }
        if (path === "/notifications") return json({ error: "request_failed" }, 500);
        return json({ error: "missing" }, 404);
      }),
    );

    renderApp("/app");
    await user.click(await screen.findByRole("button", { name: enNotifications.title }));
    expect(await screen.findByText(enNotifications.loadErrorTitle)).toBeTruthy();
    expect(screen.queryByText(enNotifications.emptyTitle)).toBeNull();
  });

  it("marks read via global endpoint and navigates across tenants", async () => {
    const user = userEvent.setup();
    let marked = false;
    const items = [
      notification({
        notificationId: "n1",
        tenantId: tenantB.tenantId,
        tenantName: tenantB.name,
        taskTitle: "Payment API",
      }),
    ];

    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const path = pathOf(input);
        const method = (init?.method ?? "GET").toUpperCase();
        if (path.endsWith("/auth/me")) return json(authUser);
        if (path === "/tenants") return json([tenantA, tenantB]);
        if (path.endsWith("/invitations")) return json([]);
        if (path === "/account/capabilities") {
          return json(deriveAccountCapabilities([tenantA, tenantB]));
        }
        if (path === "/notifications") {
          return inbox(marked ? [{ ...items[0]!, isRead: true }] : items);
        }
        if (path.endsWith("/notifications/n1/read") && method === "POST") {
          marked = true;
          return new Response(null, { status: 204 });
        }
        if (path.endsWith("/workspaces")) return json([]);
        if (path.match(/\/tenants\/[^/]+\/workspaces$/)) return json([]);
        return json({ error: "missing" }, 404);
      }),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}`);
    await waitFor(() => {
      expect(screen.getByRole("button", { name: /Notifications, 1 unread/i })).toBeTruthy();
    });
    await user.click(screen.getByRole("button", { name: /Notifications, 1 unread/i }));
    await user.click(screen.getByText("Payment API was assigned to you"));
    await waitFor(() => {
      expect(marked).toBe(true);
    });
    await waitFor(() => {
      const select = screen.getByLabelText(enTenants.selector) as HTMLSelectElement;
      expect(select.value).toBe(tenantB.tenantId);
    });
  });
});
