import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import "../i18n";
import App from "../App";
import { AuthProvider } from "../auth/AuthProvider";
import enTenants from "../locales/en/tenants.json";
import enWorkspaces from "../locales/en/workspaces.json";
import { TenantDirectoryProvider } from "../tenancy/TenantDirectoryProvider";
import { deriveAccountCapabilities } from "../test/directoryFetchHandlers";
import { clearSession, writeAccessToken } from "./session";

const authUser = {
  userId: "11111111-1111-1111-1111-111111111111",
  email: "a@example.test",
  displayName: "User A",
  isPlatformAdministrator: false,
};

const tenantA = {
  tenantId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  name: "Org A",
  slug: "org-a",
  role: "Owner",
  status: "Active",
};

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

describe("phase 8.0.3c resource name uniqueness UX", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    clearSession();
    cleanup();
  });

  it("keeps workspace create modal open with a duplicate-name field error", async () => {
    const user = userEvent.setup();
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const path = pathOf(input);
        const method = (init?.method ?? "GET").toUpperCase();
        if (path === "/notifications") return json({ items: [], unreadCount: 0 });
        if (path.endsWith("/auth/me")) return json(authUser);
        if (path.endsWith("/tenants")) return json([tenantA]);
        if (path.endsWith("/invitations")) return json([]);
        if (path === "/account/capabilities") {
          return json(deriveAccountCapabilities([tenantA]));
        }
        if (path.endsWith("/workspaces") && method === "GET") {
          return json([
            {
              workspaceId: "w1",
              tenantId: tenantA.tenantId,
              name: "Development",
              description: null,
              startDate: null,
              createdAtUtc: "2026-08-01T09:00:00Z",
              updatedAtUtc: "2026-08-01T09:00:00Z",
              accessLevel: "Edit",
              canManage: true,
            },
          ]);
        }
        if (path.endsWith("/workspaces") && method === "POST") {
          return json({ error: "workspace_name_conflict", existingName: "Development" }, 409);
        }
        return json({ error: "missing" }, 404);
      }),
    );

    render(
      <MemoryRouter initialEntries={[`/app/tenants/${tenantA.tenantId}`]}>
        <AuthProvider>
          <TenantDirectoryProvider>
            <App />
          </TenantDirectoryProvider>
        </AuthProvider>
      </MemoryRouter>,
    );

    await screen.findByRole("heading", { name: enWorkspaces.title });
    await user.click(await screen.findByRole("button", { name: enWorkspaces.newWorkspace }));
    await user.type(screen.getByLabelText(enWorkspaces.name), "development");
    await user.click(screen.getByRole("button", { name: enWorkspaces.createWorkspace }));

    expect(await screen.findByRole("dialog")).toBeTruthy();
    expect((screen.getByLabelText(enWorkspaces.name) as HTMLInputElement).value).toBe("development");
    expect(
      screen.getByText(
        enWorkspaces.errors.nameConflictNamed.replace("{{existingName}}", "Development"),
      ),
    ).toBeTruthy();
  });

  it("keeps organization create modal open with a duplicate-name field error", async () => {
    const user = userEvent.setup();
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const path = pathOf(input);
        const method = (init?.method ?? "GET").toUpperCase();
        if (path === "/notifications") return json({ items: [], unreadCount: 0 });
        if (path.endsWith("/auth/me")) return json(authUser);
        if (path.endsWith("/tenants") && method === "GET") return json([tenantA]);
        if (path.endsWith("/tenants") && method === "POST") {
          return json({ error: "organization_name_conflict", existingName: "Patris" }, 409);
        }
        if (path.endsWith("/invitations")) return json([]);
        if (path === "/account/capabilities") {
          return json(deriveAccountCapabilities([tenantA]));
        }
        return json({ error: "missing" }, 404);
      }),
    );

    render(
      <MemoryRouter initialEntries={["/app"]}>
        <AuthProvider>
          <TenantDirectoryProvider>
            <App />
          </TenantDirectoryProvider>
        </AuthProvider>
      </MemoryRouter>,
    );

    await screen.findByRole("link", { name: /Org A/ });
    await user.click(screen.getByRole("button", { name: enTenants.newOrganization }));
    await user.type(screen.getByLabelText(enTenants.name), "patris");
    await user.click(screen.getByRole("button", { name: enTenants.create }));

    expect(await screen.findByRole("dialog")).toBeTruthy();
    expect((screen.getByLabelText(enTenants.name) as HTMLInputElement).value).toBe("patris");
    await waitFor(() => {
      expect(
        screen.getByText(
          enTenants.errors.nameConflictNamed.replace("{{existingName}}", "Patris"),
        ),
      ).toBeTruthy();
    });
  });
});
