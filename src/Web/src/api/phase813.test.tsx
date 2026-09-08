import { cleanup, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import "../i18n";
import App from "../App";
import { AuthProvider } from "../auth/AuthProvider";
import enTenants from "../locales/en/tenants.json";
import enWorkspaces from "../locales/en/workspaces.json";
import enProjects from "../locales/en/projects.json";
import { TenantDirectoryProvider } from "../tenancy/TenantDirectoryProvider";
import { deriveAccountCapabilities } from "../test/directoryFetchHandlers";
import { clearSession, writeAccessToken, writeSelectedTenantId } from "./session";

const authUser = {
  userId: "11111111-1111-1111-1111-111111111111",
  email: "fresh@example.test",
  displayName: "Fresh User",
  isPlatformAdministrator: false,
};

const tenantA = {
  tenantId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  name: "Patris",
  slug: "patris",
  role: "Owner",
  status: "Active",
  workspaceCount: 1,
  canManage: true,
};

const memberTenant = {
  tenantId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  name: "FastPay",
  slug: "fastpay",
  role: "Member",
  status: "Active",
  workspaceCount: 2,
  canManage: false,
};

const pendingInvitation = {
  tenantId: "cccccccc-cccc-cccc-cccc-cccccccccccc",
  name: "Pending Org",
  slug: "pending-org",
  role: "Member",
  status: "Invited",
  workspaceCount: 0,
  canManage: false,
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

function renderApp(path = "/app") {
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

function directoryFetch(tenants: typeof tenantA[], invitations: typeof pendingInvitation[] = []) {
  return async (input: RequestInfo | URL, init?: RequestInit) => {
    const path = pathOf(input);
    const method = init?.method ?? "GET";
    if (path === "/auth/me") {
      return json(authUser);
    }
    if (path === "/tenants" && method === "GET") {
      return json(tenants);
    }
    if (path === "/invitations" && method === "GET") {
      return json(invitations);
    }
    if (path === "/account/capabilities") {
      return json(deriveAccountCapabilities(tenants, invitations));
    }
    if (path === "/notifications") {
      return json({ items: [], unreadCount: 0 });
    }
    return json({}, 404);
  };
}

describe("phase 8.1.3 account onboarding and authorization", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    clearSession();
    cleanup();
  });

  it("shows welcome onboarding for a fresh independent user", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal("fetch", vi.fn(directoryFetch([])));

    renderApp();
    expect(await screen.findByText(enTenants.onboarding.welcomeTitle)).toBeTruthy();
    expect(screen.getByRole("button", { name: enTenants.onboarding.createFirstOrganization })).toBeTruthy();
    expect(screen.queryByRole("button", { name: enTenants.newOrganization })).toBeNull();
  });

  it("shows pending invitation recovery before organization creation", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal("fetch", vi.fn(directoryFetch([], [pendingInvitation])));

    renderApp();
    expect(await screen.findByText(enTenants.onboarding.pendingTitle)).toBeTruthy();
    expect(screen.getByText("Pending Org")).toBeTruthy();
    expect(screen.getByRole("button", { name: enTenants.onboarding.continueInvitation })).toBeTruthy();
    expect(screen.queryByRole("button", { name: enTenants.onboarding.createFirstOrganization })).toBeNull();
  });

  it("hides New organization for member-only accounts", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal("fetch", vi.fn(directoryFetch([memberTenant])));

    renderApp();
    await screen.findByRole("combobox", { name: enTenants.selector });
    expect(screen.queryByRole("button", { name: enTenants.newOrganization })).toBeNull();
  });

  it("shows New organization for owners who may create additional organizations", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal("fetch", vi.fn(directoryFetch([tenantA])));

    renderApp();
    const toolbar = await screen.findByRole("toolbar", { name: enTenants.resourceToolbar });
    expect(within(toolbar).getByRole("button", { name: enTenants.newOrganization })).toBeTruthy();
  });

  it("accepts a pending invitation from onboarding", async () => {
    writeAccessToken("token-a");
    const fetchImpl = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const path = pathOf(input);
      const method = init?.method ?? "GET";
      if (path === `/tenants/${pendingInvitation.tenantId}/invitations/accept` && method === "POST") {
        return json({});
      }
      return directoryFetch([], [pendingInvitation])(input, init);
    });
    vi.stubGlobal("fetch", fetchImpl);

    renderApp();
    await userEvent.click(await screen.findByRole("button", { name: enTenants.onboarding.continueInvitation }));
    await screen.findByText(enTenants.feedback.invitationAccepted);
  });

  it("hides New workspace for members and shows it for owners", async () => {
    writeAccessToken("token-a");
    writeSelectedTenantId(authUser.userId, memberTenant.tenantId);
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const path = pathOf(input);
        const method = init?.method ?? "GET";
        if (path === `/tenants/${memberTenant.tenantId}/workspaces` && method === "GET") {
          return json([
            {
              workspaceId: "w1",
              tenantId: memberTenant.tenantId,
              name: "Beta",
              description: null,
              startDate: null,
              createdAtUtc: "2026-08-01T09:00:00Z",
              updatedAtUtc: "2026-08-01T09:00:00Z",
              accessLevel: "Edit",
              canManage: false,
            },
          ]);
        }
        return directoryFetch([memberTenant])(input, init);
      }),
    );

    renderApp(`/app/tenants/${memberTenant.tenantId}`);
    await screen.findByRole("option", { name: memberTenant.name });
    await screen.findByRole("heading", { name: enWorkspaces.title });
    expect(screen.queryByRole("button", { name: enWorkspaces.newWorkspace })).toBeNull();
  });

  it("shows New workspace for organization owners", async () => {
    writeAccessToken("token-a");
    writeSelectedTenantId(authUser.userId, tenantA.tenantId);
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const path = pathOf(input);
        const method = init?.method ?? "GET";
        if (path === `/tenants/${tenantA.tenantId}/workspaces` && method === "GET") {
          return json([]);
        }
        return directoryFetch([tenantA])(input, init);
      }),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}`);
    await screen.findByRole("option", { name: tenantA.name });
    expect(await screen.findByRole("button", { name: enWorkspaces.newWorkspace })).toBeTruthy();
  });

  it("hides project create for view-only workspace access", async () => {
    writeAccessToken("token-a");
    const workspaceId = "dddddddd-dddd-dddd-dddd-dddddddddddd";
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const path = pathOf(input);
        const method = init?.method ?? "GET";
        if (path === `/tenants/${memberTenant.tenantId}/workspaces/${workspaceId}` && method === "GET") {
          return json({
            workspaceId,
            tenantId: memberTenant.tenantId,
            name: "View Workspace",
            description: null,
            startDate: null,
            createdAtUtc: "2026-08-01T09:00:00Z",
            updatedAtUtc: "2026-08-01T09:00:00Z",
            accessLevel: "View",
            canManage: false,
          });
        }
        if (path === `/tenants/${memberTenant.tenantId}/workspaces/${workspaceId}/projects` && method === "GET") {
          return json([]);
        }
        return directoryFetch([memberTenant])(input, init);
      }),
    );

    renderApp(`/app/tenants/${memberTenant.tenantId}/workspaces/${workspaceId}`);
    await screen.findByText(enProjects.viewOnlyWorkspace);
    expect(screen.queryByRole("button", { name: enProjects.newProject })).toBeNull();
  });
});
