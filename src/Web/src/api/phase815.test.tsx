import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import "../i18n";
import App from "../App";
import { AuthProvider } from "../auth/AuthProvider";
import enMembers from "../locales/en/members.json";
import enWorkspaces from "../locales/en/workspaces.json";
import { TenantDirectoryProvider } from "../tenancy/TenantDirectoryProvider";
import { deriveAccountCapabilities } from "../test/directoryFetchHandlers";
import { clearSession, writeAccessToken } from "./session";

const authUser = {
  userId: "11111111-1111-1111-1111-111111111111",
  email: "owner@example.test",
  displayName: "Owner User",
  isPlatformAdministrator: false,
};

const tenantA = {
  tenantId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  name: "Patris",
  slug: "patris",
  role: "Owner",
  status: "Active",
};

const workspaceId = "22222222-2222-2222-2222-222222222222";
const workspaceB = "33333333-3333-3333-3333-333333333333";

const activeMember = {
  membershipId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  userId: "cccccccc-cccc-cccc-cccc-cccccccccccc",
  email: "sara@example.test",
  displayName: "Sara Member",
  role: "Member",
  status: "Active",
  joinedAtUtc: "2026-08-29T12:00:00Z",
  avatarUrl: null,
  hasImplicitWorkspaceAccess: false,
  workspaceAccessCount: 1,
  activeTaskCount: 0,
  completedTaskCount: 0,
  totalAssignedTaskCount: 0,
  completionRate: null,
};

const adminMember = {
  ...activeMember,
  membershipId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
  displayName: "Ahmed Admin",
  email: "ahmed@example.test",
  role: "Admin",
  hasImplicitWorkspaceAccess: true,
  workspaceAccessCount: null,
};

const invitedMember = {
  ...activeMember,
  membershipId: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
  displayName: "John Invited",
  email: "john@example.test",
  status: "Invited",
};

const suspendedMember = {
  ...activeMember,
  membershipId: "99999999-9999-9999-9999-999999999999",
  displayName: "Lana Suspended",
  status: "Suspended",
};

const removedMember = {
  ...activeMember,
  membershipId: "88888888-8888-8888-8888-888888888888",
  displayName: "David Removed",
  status: "Removed",
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

function baseFetch(
  overrides: Partial<Record<string, (init?: RequestInit) => Response | Promise<Response>>>,
) {
  return async (input: RequestInfo | URL, init?: RequestInit) => {
    const path = pathOf(input);
    if (path === "/auth/me") {
      return json(authUser);
    }
    if (path === "/notifications") {
      return json({ items: [], unreadCount: 0 });
    }
    if (path === "/tenants") {
      return json([{ ...tenantA, workspaceCount: 2, canManage: true }]);
    }
    if (path === "/invitations") {
      return json([]);
    }
    if (path === "/account/capabilities") {
      return json(deriveAccountCapabilities([{ role: tenantA.role }]));
    }
    const handler = overrides[path];
    if (handler) {
      return handler(init);
    }
    return json({}, 404);
  };
}

describe("phase 8.1.5 workspace access management UX", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    clearSession();
    cleanup();
  });

  it("shows member page tabs and separates invitations", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(
        baseFetch({
          [`/tenants/${tenantA.tenantId}/members`]: () => json([activeMember, invitedMember]),
          [`/tenants/${tenantA.tenantId}/invitations/pending`]: () => json([]),
          [`/tenants/${tenantA.tenantId}/workspaces`]: () => json([]),
        }),
      ),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/members`);
    await screen.findByText("Sara Member");
    expect(screen.getByRole("tab", { name: enMembers.pageTabs.members })).toBeTruthy();
    expect(screen.queryByText("Invitation pending")).toBeNull();
    await userEvent.click(screen.getByRole("tab", { name: enMembers.pageTabs.invitations }));
    await screen.findByText("Invitation pending");
    expect(screen.queryByText("Sara Member")).toBeNull();
    expect(screen.getByText("John Invited")).toBeTruthy();
  });

  it("shows full access for admin and manage access for active member", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(
        baseFetch({
          [`/tenants/${tenantA.tenantId}/members`]: () => json([activeMember, adminMember]),
          [`/tenants/${tenantA.tenantId}/invitations/pending`]: () => json([]),
          [`/tenants/${tenantA.tenantId}/workspaces`]: () => json([]),
        }),
      ),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/members`);
    await screen.findByText("Ahmed Admin");
    expect(screen.getAllByText(enMembers.fullAccess).length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: enMembers.actions.manageAccessShort })).toBeTruthy();
  });

  it("stages workspace access changes and saves with batch endpoint", async () => {
    writeAccessToken("token-a");
    let batchCalled = false;
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const path = pathOf(input);
        if (path === `/tenants/${tenantA.tenantId}/members/${activeMember.membershipId}/workspace-access` && init?.method === "PUT") {
          batchCalled = true;
          return json([]);
        }
        return baseFetch({
          [`/tenants/${tenantA.tenantId}/members`]: () => json([activeMember]),
          [`/tenants/${tenantA.tenantId}/invitations/pending`]: () => json([]),
          [`/tenants/${tenantA.tenantId}/workspaces`]: () =>
            json([
              { workspaceId, name: "Development" },
              { workspaceId: workspaceB, name: "Marketing" },
            ]),
          [`/tenants/${tenantA.tenantId}/members/${activeMember.membershipId}/workspace-access`]: () =>
            json([{ membershipId: activeMember.membershipId, workspaceId, accessLevel: "Edit" }]),
        })(input, init);
      }),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/members`);
    await screen.findByText("Sara Member");
    await userEvent.click(screen.getByRole("button", { name: enMembers.actions.manageAccessShort }));
    const dialog = await screen.findByRole("dialog");
    await userEvent.selectOptions(
      within(dialog).getByLabelText(/Marketing/i),
      "View",
    );
    await userEvent.click(within(dialog).getByRole("button", { name: enMembers.workspaceAccess.saveChanges }));
    await waitFor(() => expect(batchCalled).toBe(true));
  });

  it("shows workspace member access panel for managers", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(
        baseFetch({
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`]: () =>
            json({
              workspaceId,
              tenantId: tenantA.tenantId,
              name: "Development",
              accessLevel: "Edit",
              canManage: true,
            }),
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}/projects`]: () => json([]),
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}/member-access`]: () =>
            json([
              {
                membershipId: activeMember.membershipId,
                userId: activeMember.userId,
                displayName: activeMember.displayName,
                email: activeMember.email,
                role: "Member",
                status: "Active",
                hasImplicitWorkspaceAccess: false,
                effectiveAccess: "Edit",
              },
              {
                membershipId: authUser.userId,
                userId: authUser.userId,
                displayName: authUser.displayName,
                email: authUser.email,
                role: "Owner",
                status: "Active",
                hasImplicitWorkspaceAccess: true,
                effectiveAccess: "Full",
              },
            ]),
        }),
      ),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`);
    await screen.findByText("Development");
    await userEvent.click(screen.getByRole("tab", { name: "Members & access" }));
    await screen.findByRole("heading", { name: enWorkspaces.memberAccess.title });
    await waitFor(() => {
      expect(screen.getByText("Sara Member")).toBeTruthy();
    });
    expect(screen.getAllByText(enMembers.fullAccess).length).toBeGreaterThan(0);
  });

  it("does not show manage access for suspended or removed members", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(
        baseFetch({
          [`/tenants/${tenantA.tenantId}/members`]: () => json([suspendedMember, removedMember]),
          [`/tenants/${tenantA.tenantId}/invitations/pending`]: () => json([]),
          [`/tenants/${tenantA.tenantId}/workspaces`]: () => json([]),
        }),
      ),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/members`);
    await screen.findByText(suspendedMember.displayName);
    expect(screen.queryByRole("button", { name: enMembers.actions.manageAccessShort })).toBeNull();
  });
});

describe("phase 8.1.5 member role perspective fail closed", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    clearSession();
    cleanup();
  });

  it("hides workspace member access panel when user cannot manage", async () => {
    writeAccessToken("token-m");
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const path = pathOf(input);
        if (path === "/tenants") {
          return json([{ ...tenantA, role: "Member", workspaceCount: 1, canManage: false }]);
        }
        if (path === "/account/capabilities") {
          return json(deriveAccountCapabilities([{ role: "Member" }]));
        }
        return baseFetch({
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`]: () =>
            json({
              workspaceId,
              tenantId: tenantA.tenantId,
              name: "Development",
              accessLevel: "View",
              canManage: false,
            }),
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}/projects`]: () => json([]),
        })(input, init);
      }),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`);
    await screen.findByText("Development");
    expect(screen.queryByRole("heading", { name: enWorkspaces.memberAccess.title })).toBeNull();
  });
});
