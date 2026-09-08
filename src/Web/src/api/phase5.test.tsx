import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import "../i18n";
import App from "../App";
import { AuthProvider } from "../auth/AuthProvider";
import { getDirectionForLocale } from "../i18n/direction";
import arMembers from "../locales/ar/members.json";
import arProjects from "../locales/ar/projects.json";
import enAuth from "../locales/en/auth.json";
import enCommon from "../locales/en/common.json";
import enMembers from "../locales/en/members.json";
import enProjects from "../locales/en/projects.json";
import enNotifications from "../locales/en/notifications.json";
import enTenants from "../locales/en/tenants.json";
import enWorkspaces from "../locales/en/workspaces.json";
import kuMembers from "../locales/ku/members.json";
import kuProjects from "../locales/ku/projects.json";
import { TenantDirectoryProvider } from "../tenancy/TenantDirectoryProvider";
import { deriveAccountCapabilities } from "../test/directoryFetchHandlers";
import { clearSession, writeAccessToken } from "./session";

const authUser = {
  userId: "11111111-1111-1111-1111-111111111111",
  email: "a@example.test",
  displayName: "User A",
  isPlatformAdministrator: false,
};

const invitation = {
  tenantId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  name: "Invited Org",
  slug: "invited-org",
  role: "Member",
  status: "Invited",
};

const tenantA = {
  tenantId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  name: "Org A",
  slug: "org-a",
  role: "Owner",
  status: "Active",
};

const tenantB = {
  tenantId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  name: "Org B",
  slug: "org-b",
  role: "Owner",
  status: "Active",
};

type WorkspaceStub = {
  workspaceId: string;
  tenantId: string;
  name: string;
  description: string | null;
  startDate: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  accessLevel: "View" | "Edit";
  canManage: boolean;
};

function workspaceStub(
  overrides: Partial<WorkspaceStub> & Pick<WorkspaceStub, "workspaceId" | "name">,
): WorkspaceStub {
  return {
    description: null,
    startDate: null,
    createdAtUtc: "2026-08-29T10:00:00Z",
    updatedAtUtc: "2026-08-30T12:00:00Z",
    canManage: true,
    accessLevel: "Edit" as const,
    tenantId: tenantA.tenantId,
    ...overrides,
  };
}

const leopard = workspaceStub({
  workspaceId: "cccccccc-cccc-cccc-cccc-cccccccccccc",
  tenantId: tenantA.tenantId,
  name: "Leopard",
  description: "Big cat workspace",
  startDate: "2026-01-15",
  createdAtUtc: "2026-08-29T10:00:00Z",
  updatedAtUtc: "2026-08-30T12:00:00Z",
  accessLevel: "View",
  canManage: false,
});

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
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

function pathOf(input: RequestInfo | URL) {
  const raw = String(input);
  return raw.startsWith("http") ? new URL(raw).pathname : raw;
}

async function enterApp(
  fetchImpl: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>,
  path = "/app",
  directory: {
    getTenants?: () => { role: string }[];
    getInvitations?: () => unknown[];
  } = {},
) {
  const getTenants = directory.getTenants ?? (() => [tenantA]);
  const getInvitations = directory.getInvitations ?? (() => []);
  writeAccessToken("token-a");
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input, init) => {
      const route = pathOf(input);
      if (route === "/notifications") {
        return json({ items: [], unreadCount: 0 });
      }
      if (route === "/account/capabilities") {
        return json(deriveAccountCapabilities(getTenants(), getInvitations()));
      }
      return fetchImpl(input, init);
    }),
  );
  renderApp(path);
  expect(await screen.findByLabelText(enTenants.selector)).toBeTruthy();
}

describe("phase 5 membership and resource authorization UI", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    clearSession();
    cleanup();
  });

  it("refreshes invitations and tenants after accept without a page reload", async () => {
    const tenants: typeof tenantA[] = [];
    const invitations = [{ ...invitation }];

    const user = userEvent.setup();
    await enterApp(async (input, init) => {
      const path = pathOf(input);
      const method = (init?.method ?? "GET").toUpperCase();
      if (path.endsWith("/auth/me")) {
        return json(authUser);
      }
      if (path.endsWith("/tenants") && method === "GET") {
        return json(tenants);
      }
      if (path.endsWith("/invitations") && method === "GET") {
        return json(invitations);
      }
      if (path.includes("/invitations/accept") && method === "POST") {
        const accepted = invitations.shift();
        if (accepted) {
          tenants.push({
            tenantId: accepted.tenantId,
            name: accepted.name,
            slug: accepted.slug,
            role: accepted.role,
            status: "Active",
          });
        }
        return new Response(null, { status: 204 });
      }
      if (path.includes("/workspaces")) {
        return json([]);
      }
      return json({ error: "missing" }, 404);
    }, "/app", { getTenants: () => tenants, getInvitations: () => invitations });

    await user.click(
      await screen.findByRole("button", {
        name: enNotifications.attentionWithInvitations.replace("{{count}}", "1"),
      }),
    );
    expect((await screen.findAllByText(invitation.name)).length).toBeGreaterThan(0);
    await user.click(screen.getByRole("button", { name: enTenants.accept }));

    await waitFor(() => {
      expect(screen.getByRole("combobox", { name: enTenants.selector }).textContent).toContain(
        invitation.name,
      );
    });
    expect(screen.queryByRole("button", { name: enTenants.accept })).toBeNull();
  });

  it("keeps invitation state when acceptance fails", async () => {
    const user = userEvent.setup();
    await enterApp(async (input, init) => {
      const path = pathOf(input);
      const method = init?.method ?? "GET";
      if (path.endsWith("/auth/me")) {
        return json(authUser);
      }
      if (path.endsWith("/tenants") && method === "GET") {
        return json([]);
      }
      if (path.endsWith("/invitations") && method === "GET") {
        return json([invitation]);
      }
      if (path.endsWith("/invitations/accept") && method === "POST") {
        return json({ error: "invitation_not_found" }, 400);
      }
      return json({ error: "missing" }, 404);
    }, "/app", { getTenants: () => [], getInvitations: () => [invitation] });

    await user.click(
      await screen.findByRole("button", {
        name: enNotifications.attentionWithInvitations.replace("{{count}}", "1"),
      }),
    );
    expect((await screen.findAllByText(invitation.name)).length).toBeGreaterThan(0);
    await user.click(screen.getByRole("button", { name: enTenants.accept }));
    expect((await screen.findByRole("alert")).textContent).toContain(enCommon.errors.invitation_not_found);
    expect(screen.getByRole("button", { name: enTenants.accept })).toBeTruthy();
    expect(screen.queryByRole("link", { name: new RegExp(invitation.name) })).toBeNull();
  });

  it("does not show organization member administration on the workspace page", async () => {
    await enterApp(async (input) => {
      const path = pathOf(input);
      if (path.endsWith("/auth/me")) {
        return json(authUser);
      }
      if (path.endsWith("/tenants")) {
        return json([tenantA]);
      }
      if (path.endsWith("/invitations")) {
        return json([]);
      }
      if (path.endsWith("/workspaces")) {
        return json([leopard]);
      }
      if (path.includes("/members")) {
        return json([]);
      }
      return json({ error: "missing" }, 404);
    }, `/app/tenants/${tenantA.tenantId}`);

    expect(await screen.findByRole("heading", { name: enWorkspaces.title })).toBeTruthy();
    expect(await screen.findByText("Leopard")).toBeTruthy();
    expect(screen.queryByText(enMembers.title)).toBeNull();
    expect(screen.queryByLabelText(enTenants.inviteEmail)).toBeNull();
    expect(screen.queryByRole("heading", { name: enWorkspaces.createWorkspace })).toBeNull();
  });

  it("hides a workspace with no access and treats unknown ids as not found", async () => {
    await enterApp(async (input) => {
      const path = pathOf(input);
      if (path.endsWith("/auth/me")) {
        return json(authUser);
      }
      if (path.endsWith("/tenants")) {
        return json([{ ...tenantA, role: "Member" }]);
      }
      if (path.endsWith("/invitations")) {
        return json([]);
      }
      if (path.endsWith("/workspaces") && !path.includes(leopard.workspaceId)) {
        return json([]);
      }
      if (path.includes(leopard.workspaceId)) {
        return json({ error: "workspace_not_found" }, 404);
      }
      return json({ error: "missing" }, 404);
    }, `/app/tenants/${tenantA.tenantId}`);

    expect(await screen.findByText(enWorkspaces.emptyAssignedTitle)).toBeTruthy();
    expect(screen.queryByText("Leopard")).toBeNull();

    cleanup();
    clearSession();
    vi.unstubAllGlobals();

    await enterApp(async (input) => {
      const path = pathOf(input);
      if (path.endsWith("/auth/me")) {
        return json(authUser);
      }
      if (path.endsWith("/tenants")) {
        return json([{ ...tenantA, role: "Member" }]);
      }
      if (path.endsWith("/invitations")) {
        return json([]);
      }
      return json({ error: "workspace_not_found" }, 404);
    }, `/app/tenants/${tenantA.tenantId}/workspaces/${leopard.workspaceId}`);

    expect(await screen.findByText(enCommon.errors.workspace_not_found)).toBeTruthy();
    expect(screen.queryByRole("heading", { name: enProjects.create })).toBeNull();
  });

  it("hides project create for View and shows it for Edit", async () => {
    await enterApp(async (input) => {
      const path = pathOf(input);
      if (path.endsWith("/auth/me")) {
        return json(authUser);
      }
      if (path.endsWith("/tenants")) {
        return json([{ ...tenantA, role: "Member" }]);
      }
      if (path.endsWith("/invitations")) {
        return json([]);
      }
      if (path.endsWith(`/workspaces/${leopard.workspaceId}`)) {
        return json(leopard);
      }
      if (path.endsWith("/projects")) {
        return json([]);
      }
      return json({ error: "missing" }, 404);
    }, `/app/tenants/${tenantA.tenantId}/workspaces/${leopard.workspaceId}`);

    expect(await screen.findByText(enProjects.viewOnlyWorkspace)).toBeTruthy();
    expect(screen.queryByRole("button", { name: enProjects.newProject })).toBeNull();

    cleanup();
    clearSession();
    vi.unstubAllGlobals();

    await enterApp(async (input) => {
      const path = pathOf(input);
      if (path.endsWith("/auth/me")) {
        return json(authUser);
      }
      if (path.endsWith("/tenants")) {
        return json([{ ...tenantA, role: "Member" }]);
      }
      if (path.endsWith("/invitations")) {
        return json([]);
      }
      if (path.endsWith(`/workspaces/${leopard.workspaceId}`)) {
        return json({ ...leopard, accessLevel: "Edit" });
      }
      if (path.endsWith("/projects")) {
        return json([]);
      }
      return json({ error: "missing" }, 404);
    }, `/app/tenants/${tenantA.tenantId}/workspaces/${leopard.workspaceId}`);

    await screen.findByText(enProjects.emptyTitle);
    expect(screen.getAllByRole("button", { name: enProjects.newProject }).length).toBeGreaterThan(0);
  });

  it("logs out on 401 and stays signed in on 403", async () => {
    const user = userEvent.setup();
    await enterApp(async (input) => {
      const path = pathOf(input);
      if (path.endsWith("/auth/me")) {
        return json(authUser);
      }
      if (path.endsWith("/tenants")) {
        return json([tenantA]);
      }
      if (path.endsWith("/invitations")) {
        return json([]);
      }
      if (path.includes("/workspaces")) {
        return json({ error: "unauthenticated" }, 401);
      }
      return json({ error: "missing" }, 404);
    }, "/app");

    await user.click(await screen.findByRole("link", { name: /Org A/ }));
    expect((await screen.findAllByRole("heading", { name: enAuth.signIn })).length).toBeGreaterThan(0);

    cleanup();
    clearSession();
    vi.unstubAllGlobals();

    await enterApp(async (input) => {
      const path = pathOf(input);
      if (path.endsWith("/auth/me")) {
        return json(authUser);
      }
      if (path.endsWith("/tenants")) {
        return json([tenantA]);
      }
      if (path.endsWith("/invitations")) {
        return json([]);
      }
      if (path.includes("/workspaces")) {
        return json({ error: "tenant_access_denied" }, 403);
      }
      return json({ error: "missing" }, 404);
    }, `/app/tenants/${tenantA.tenantId}`);

    expect(await screen.findByLabelText(enTenants.selector)).toBeTruthy();
    expect((await screen.findByRole("alert")).textContent).toContain(enCommon.errors.forbidden);
    expect(screen.queryByRole("heading", { name: enAuth.signIn })).toBeNull();
  });

  it("clears stale workspace state when switching tenants and ignores late responses", async () => {
    let resolveTenantAWorkspaces: ((response: Response) => void) | undefined;
    const tenantAWorkspaces = new Promise<Response>((resolve) => {
      resolveTenantAWorkspaces = resolve;
    });
    const user = userEvent.setup();

    await enterApp(async (input) => {
      const path = pathOf(input);
      if (path.endsWith("/auth/me")) {
        return json(authUser);
      }
      if (path.endsWith("/tenants")) {
        return json([tenantA, tenantB]);
      }
      if (path.endsWith("/invitations")) {
        return json([]);
      }
      if (path === `/tenants/${tenantA.tenantId}/workspaces`) {
        return tenantAWorkspaces;
      }
      if (path === `/tenants/${tenantB.tenantId}/workspaces`) {
        return json([workspaceStub({ workspaceId: "wb", name: "Bee", tenantId: tenantB.tenantId })]);
      }
      return json({ error: "missing" }, 404);
    }, `/app/tenants/${tenantA.tenantId}`);

    expect(await screen.findByRole("heading", { name: enWorkspaces.title })).toBeTruthy();
    expect(screen.queryByText("Bee")).toBeNull();
    expect(screen.queryByText("Aye")).toBeNull();

    await user.selectOptions(screen.getByRole("combobox", { name: enTenants.selector }), tenantB.tenantId);
    expect(await screen.findByText("Bee")).toBeTruthy();

    resolveTenantAWorkspaces?.(
      json([workspaceStub({ workspaceId: "wa", name: "Aye", tenantId: tenantA.tenantId })]),
    );

    await waitFor(() => {
      expect(screen.getByText("Bee")).toBeTruthy();
    });
    expect(screen.queryByText("Aye")).toBeNull();
  });

  it("shows only workspaces for the active tenant when switching organizations", async () => {
    const user = userEvent.setup();
    const workspacesByTenant: Record<string, ReturnType<typeof workspaceStub>[]> = {
      [tenantA.tenantId]: [
        workspaceStub({ workspaceId: "wa-1", name: "A-Workspace-1", tenantId: tenantA.tenantId }),
        workspaceStub({ workspaceId: "wa-2", name: "A-Workspace-2", tenantId: tenantA.tenantId }),
      ],
      [tenantB.tenantId]: [
        workspaceStub({ workspaceId: "wb-1", name: "B-Workspace-1", tenantId: tenantB.tenantId }),
      ],
    };

    await enterApp(async (input) => {
      const path = pathOf(input);
      if (path.endsWith("/auth/me")) {
        return json(authUser);
      }
      if (path.endsWith("/tenants")) {
        return json([tenantA, tenantB]);
      }
      if (path.endsWith("/invitations")) {
        return json([]);
      }
      if (path.endsWith("/members")) {
        return json([]);
      }
      const match = path.match(/^\/tenants\/([^/]+)\/workspaces$/);
      if (match) {
        return json(workspacesByTenant[match[1]] ?? []);
      }
      return json({ error: "missing" }, 404);
    }, `/app/tenants/${tenantA.tenantId}`);

    expect(await screen.findByText("A-Workspace-1")).toBeTruthy();
    expect(screen.getByText("A-Workspace-2")).toBeTruthy();
    expect(screen.queryByText("B-Workspace-1")).toBeNull();

    await user.selectOptions(screen.getByLabelText(enTenants.selector), tenantB.tenantId);
    expect(await screen.findByText("B-Workspace-1")).toBeTruthy();
    expect(screen.queryByText("A-Workspace-1")).toBeNull();
    expect(screen.queryByText("A-Workspace-2")).toBeNull();

    await user.selectOptions(screen.getByLabelText(enTenants.selector), tenantA.tenantId);
    expect(await screen.findByText("A-Workspace-1")).toBeTruthy();
    expect(screen.getByText("A-Workspace-2")).toBeTruthy();
    expect(screen.queryByText("B-Workspace-1")).toBeNull();
  });

  it("resolves member and project strings in English, Arabic, and Kurdish and keeps RTL", () => {
    expect(enMembers.title).toBeTruthy();
    expect(arMembers.title).toBeTruthy();
    expect(kuMembers.title).toBeTruthy();
    expect(enMembers.access.view).toBeTruthy();
    expect(arMembers.access.edit).toBeTruthy();
    expect(kuMembers.access.none).toBeTruthy();
    expect(enProjects.viewOnly).toBeTruthy();
    expect(arProjects.viewOnly).toBeTruthy();
    expect(kuProjects.viewOnly).toBeTruthy();
    expect(enCommon.errors.workspace_edit_forbidden).toBeTruthy();
    expect(getDirectionForLocale("en")).toBe("ltr");
    expect(getDirectionForLocale("ar")).toBe("rtl");
    expect(getDirectionForLocale("ku")).toBe("rtl");
  });
});
