import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import "../i18n";
import App from "../App";
import { AuthProvider } from "../auth/AuthProvider";
import enInvitations from "../locales/en/invitations.json";
import enMembers from "../locales/en/members.json";
import enNavigation from "../locales/en/navigation.json";
import enTenants from "../locales/en/tenants.json";
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

const memberRow = {
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

describe("phase 8.1 members and invitations", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    clearSession();
    cleanup();
  });

  it("shows Members navigation and renders member rows for an owner", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(
        baseFetch({
          [`/tenants/${tenantA.tenantId}/members`]: () => json([memberRow]),
          [`/tenants/${tenantA.tenantId}/invitations/pending`]: () => json([]),
          [`/tenants/${tenantA.tenantId}/workspaces`]: () => json([]),
        }),
      ),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/members`);
    expect(await screen.findByRole("heading", { name: enMembers.title })).toBeTruthy();
    expect(await screen.findByRole("link", { name: enNavigation.members })).toBeTruthy();
    expect(await screen.findByText("Sara Member")).toBeTruthy();
    expect(screen.getByText("sara@example.test")).toBeTruthy();
    expect(screen.getAllByText(enMembers.status.active).length).toBeGreaterThan(0);
    expect(screen.getAllByText(enMembers.roles.member).length).toBeGreaterThan(0);
  });

  it("filters members by search query", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(
        baseFetch({
          [`/tenants/${tenantA.tenantId}/members`]: () =>
            json([
              memberRow,
              {
                ...memberRow,
                membershipId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
                displayName: "Ahmed Admin",
                email: "ahmed@example.test",
                role: "Admin",
              },
            ]),
          [`/tenants/${tenantA.tenantId}/invitations/pending`]: () => json([]),
          [`/tenants/${tenantA.tenantId}/workspaces`]: () => json([]),
        }),
      ),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/members`);
    await screen.findByText("Sara Member");
    const search = screen.getByLabelText(enMembers.searchLabel);
    await userEvent.type(search, "ahmed");
    await waitFor(() => {
      expect(screen.queryByText("Sara Member")).toBeNull();
      expect(screen.getByText("Ahmed Admin")).toBeTruthy();
    });
  });

  it("opens invite member dialog with email and role fields", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(
        baseFetch({
          [`/tenants/${tenantA.tenantId}/members`]: () => json([]),
          [`/tenants/${tenantA.tenantId}/invitations/pending`]: () => json([]),
          [`/tenants/${tenantA.tenantId}/workspaces`]: () => json([]),
        }),
      ),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/members`);
    await screen.findByRole("heading", { name: enMembers.title });
    const inviteButton = await screen.findByRole("button", { name: enTenants.invite });
    await userEvent.click(inviteButton);
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByLabelText(enTenants.inviteEmail)).toBeTruthy();
    expect(within(dialog).getByLabelText(enMembers.inviteRole)).toBeTruthy();
  });

  it("renders invitation preview for a valid token", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const path = pathOf(input);
        if (path === "/invite/abc123") {
          return json({
            organizationName: "Patris",
            invitedEmail: "new@example.test",
            role: "Member",
            expiresAtUtc: "2026-09-08T00:00:00Z",
            inviterDisplayName: "Owner User",
            requiresRegistration: true,
            workspaceGrants: [{ workspaceName: "Development", accessLevel: "Edit" }],
          });
        }
        return json({}, 404);
      }),
    );

    renderApp("/invite/abc123");
    expect(await screen.findByRole("heading", { name: enInvitations.title })).toBeTruthy();
    expect(screen.getByText(/Patris/)).toBeTruthy();
    expect(screen.getByText("new@example.test")).toBeTruthy();
    expect(screen.getByRole("button", { name: enInvitations.createAccount })).toBeTruthy();
  });

  it("shows invalid invitation state", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const path = pathOf(input);
        if (path === "/invite/bad-token") {
          return json({ error: "invitation_invalid" }, 404);
        }
        return json({}, 404);
      }),
    );

    renderApp("/invite/bad-token");
    expect(await screen.findByText(enInvitations.errors.invalid)).toBeTruthy();
  });
});
