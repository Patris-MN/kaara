import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import "../i18n";
import App from "../App";
import { AuthProvider } from "../auth/AuthProvider";
import enMembers from "../locales/en/members.json";
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
  workspaceAccessCount: 2,
  activeTaskCount: 3,
  completedTaskCount: 5,
  totalAssignedTaskCount: 8,
  completionRate: 62.5,
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
  status: "Invited",
};

const invitation = {
  invitationId: "ffffffff-ffff-ffff-ffff-ffffffffffff",
  invitedEmail: "sara@example.test",
  role: "Member",
  expiresAtUtc: "2026-09-08T00:00:00Z",
  membershipId: invitedMember.membershipId,
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
      return json([{ ...tenantA, workspaceCount: 1, canManage: true }]);
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

describe("phase 8.1.2 member action UX", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    clearSession();
    cleanup();
  });

  it("shows Manage access separately for active members", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(
        baseFetch({
          [`/tenants/${tenantA.tenantId}/members`]: () => json([activeMember]),
          [`/tenants/${tenantA.tenantId}/invitations/pending`]: () => json([]),
          [`/tenants/${tenantA.tenantId}/workspaces`]: () =>
            json([{ workspaceId: "11111111-1111-1111-1111-111111111112", name: "Development" }]),
        }),
      ),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/members`);
    await screen.findByText("Sara Member");
    expect(screen.getByRole("button", { name: enMembers.actions.manageAccessShort })).toBeTruthy();
  });

  it("does not show Manage access for admins", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(
        baseFetch({
          [`/tenants/${tenantA.tenantId}/members`]: () => json([adminMember]),
          [`/tenants/${tenantA.tenantId}/invitations/pending`]: () => json([]),
          [`/tenants/${tenantA.tenantId}/workspaces`]: () => json([]),
        }),
      ),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/members`);
    await screen.findByText("Ahmed Admin");
    expect(screen.queryByRole("button", { name: enMembers.actions.manageAccessShort })).toBeNull();
  });

  it("opens workspace access dialog from Manage access button", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(
        baseFetch({
          [`/tenants/${tenantA.tenantId}/members`]: () => json([activeMember]),
          [`/tenants/${tenantA.tenantId}/invitations/pending`]: () => json([]),
          [`/tenants/${tenantA.tenantId}/workspaces`]: () =>
            json([{ workspaceId: "11111111-1111-1111-1111-111111111112", name: "Development" }]),
          [`/tenants/${tenantA.tenantId}/members/${activeMember.membershipId}/workspace-access`]: () =>
            json([]),
        }),
      ),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/members`);
    await screen.findByText("Sara Member");
    await userEvent.click(screen.getByRole("button", { name: enMembers.actions.manageAccessShort }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByRole("heading", { name: enMembers.workspaceAccess.title })).toBeTruthy();
    expect(within(dialog).getByText("Development")).toBeTruthy();
  });

  it("keeps invitation actions out of active member overflow menu", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(
        baseFetch({
          [`/tenants/${tenantA.tenantId}/members`]: () => json([activeMember]),
          [`/tenants/${tenantA.tenantId}/invitations/pending`]: () => json([invitation]),
          [`/tenants/${tenantA.tenantId}/workspaces`]: () => json([]),
        }),
      ),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/members`);
    await screen.findByText("Sara Member");
    await userEvent.click(screen.getByRole("button", { name: enMembers.actions.menu }));
    const menu = await screen.findByRole("menu");
    expect(within(menu).getByRole("menuitem", { name: enMembers.actions.changeRole })).toBeTruthy();
    expect(within(menu).queryByRole("menuitem", { name: enMembers.actions.resend })).toBeNull();
    expect(within(menu).queryByRole("menuitem", { name: enMembers.actions.manageAccess })).toBeNull();
  });

  it("opens change role dialog from overflow menu", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(
        baseFetch({
          [`/tenants/${tenantA.tenantId}/members`]: () => json([activeMember]),
          [`/tenants/${tenantA.tenantId}/invitations/pending`]: () => json([]),
          [`/tenants/${tenantA.tenantId}/workspaces`]: () => json([]),
        }),
      ),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/members`);
    await screen.findByText("Sara Member");
    await userEvent.click(screen.getByRole("button", { name: enMembers.actions.menu }));
    await userEvent.click(await screen.findByRole("menuitem", { name: enMembers.actions.changeRole }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByRole("heading", { name: enMembers.changeRole.title })).toBeTruthy();
    expect(within(dialog).getByLabelText(enMembers.changeRole.newRole)).toBeTruthy();
  });

  it("shows invitation-only actions for invited members", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(
        baseFetch({
          [`/tenants/${tenantA.tenantId}/members`]: () => json([invitedMember]),
          [`/tenants/${tenantA.tenantId}/invitations/pending`]: () => json([invitation]),
          [`/tenants/${tenantA.tenantId}/workspaces`]: () => json([]),
        }),
      ),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/members`);
    await screen.findByRole("tab", { name: enMembers.pageTabs.members });
    await userEvent.click(screen.getByRole("tab", { name: enMembers.pageTabs.invitations }));
    await screen.findByText("Sara Member");
    expect(screen.queryByRole("button", { name: enMembers.actions.manageAccessShort })).toBeNull();
    await userEvent.click(screen.getByRole("button", { name: enMembers.actions.menu }));
    const menu = await screen.findByRole("menu");
    expect(within(menu).getByRole("menuitem", { name: enMembers.actions.resend })).toBeTruthy();
    expect(within(menu).queryByRole("menuitem", { name: enMembers.actions.changeRole })).toBeNull();
  });

  it("styles remove action as destructive in overflow menu", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(
        baseFetch({
          [`/tenants/${tenantA.tenantId}/members`]: () => json([activeMember]),
          [`/tenants/${tenantA.tenantId}/invitations/pending`]: () => json([]),
          [`/tenants/${tenantA.tenantId}/workspaces`]: () => json([]),
        }),
      ),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/members`);
    await screen.findByText("Sara Member");
    await userEvent.click(screen.getByRole("button", { name: enMembers.actions.menu }));
    const removeItem = await screen.findByRole("menuitem", { name: enMembers.actions.remove });
    expect(removeItem.className).toContain("destructive");
  });

  it("closes overflow menu on Escape", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(
        baseFetch({
          [`/tenants/${tenantA.tenantId}/members`]: () => json([activeMember]),
          [`/tenants/${tenantA.tenantId}/invitations/pending`]: () => json([]),
          [`/tenants/${tenantA.tenantId}/workspaces`]: () => json([]),
        }),
      ),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/members`);
    await screen.findByText("Sara Member");
    await userEvent.click(screen.getByRole("button", { name: enMembers.actions.menu }));
    expect(await screen.findByRole("menu")).toBeTruthy();
    await userEvent.keyboard("{Escape}");
    await waitFor(() => {
      expect(screen.queryByRole("menu")).toBeNull();
    });
  });
});
