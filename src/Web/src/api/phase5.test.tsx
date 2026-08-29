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
import enTenants from "../locales/en/tenants.json";
import enWorkspaces from "../locales/en/workspaces.json";
import kuMembers from "../locales/ku/members.json";
import kuProjects from "../locales/ku/projects.json";
import { TenantDirectoryProvider } from "../tenancy/TenantDirectoryProvider";
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

const leopard = {
  workspaceId: "cccccccc-cccc-cccc-cccc-cccccccccccc",
  tenantId: tenantA.tenantId,
  name: "Leopard",
  accessLevel: "View",
};

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

async function enterApp(fetchImpl: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>, path = "/app") {
  writeAccessToken("token-a");
  vi.stubGlobal("fetch", vi.fn(fetchImpl));
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
    });

    expect(await screen.findByText(invitation.name)).toBeTruthy();
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
    });

    expect(await screen.findByText(invitation.name)).toBeTruthy();
    await user.click(screen.getByRole("button", { name: enTenants.accept }));
    expect((await screen.findByRole("alert")).textContent).toContain(enCommon.errors.invitation_not_found);
    expect(screen.getByText(invitation.name)).toBeTruthy();
    expect(screen.getByRole("button", { name: enTenants.accept })).toBeTruthy();
    expect(screen.queryByRole("link", { name: new RegExp(invitation.name) })).toBeNull();
  });

  it("shows member access management for owners and hides it for members", async () => {
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
      if (path.includes("/members") && !path.includes("workspace-access")) {
        return json([
          {
            membershipId: "m-owner",
            userId: authUser.userId,
            displayName: authUser.displayName,
            email: authUser.email,
            role: "Owner",
            status: "Active",
          },
          {
            membershipId: "m-member",
            userId: "22222222-2222-2222-2222-222222222222",
            displayName: "User B",
            email: "b@example.test",
            role: "Member",
            status: "Active",
          },
        ]);
      }
      if (path.includes("/workspace-access")) {
        return json([]);
      }
      return json({ error: "missing" }, 404);
    }, `/app/tenants/${tenantA.tenantId}`);

    expect(await screen.findByText("User B")).toBeTruthy();
    expect(
      await screen.findByLabelText(
        enMembers.accessLabel.replace("{{member}}", "User B").replace("{{workspace}}", "Leopard"),
      ),
    ).toBeTruthy();

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
      if (path.endsWith("/workspaces")) {
        return json([leopard]);
      }
      if (path.endsWith("/members")) {
        return json({ error: "workspace_access_manage_forbidden" }, 403);
      }
      return json({ error: "missing" }, 404);
    }, `/app/tenants/${tenantA.tenantId}`);

    expect(await screen.findByText("Leopard")).toBeTruthy();
    expect(screen.queryByRole("heading", { name: enMembers.title })).toBeNull();
    expect(screen.queryByRole("heading", { name: enWorkspaces.create })).toBeNull();
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

    expect((await screen.findByRole("alert")).textContent).toContain(enCommon.errors.workspace_not_found);
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

    expect(await screen.findByText(enProjects.viewOnly)).toBeTruthy();
    expect(screen.queryByRole("heading", { name: enProjects.create })).toBeNull();

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

    expect(await screen.findByRole("heading", { name: enProjects.create })).toBeTruthy();
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

  it("clears stale member state when switching tenants and ignores late responses", async () => {
    let resolveTenantAMembers: ((response: Response) => void) | undefined;
    const tenantAMembers = new Promise<Response>((resolve) => {
      resolveTenantAMembers = resolve;
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
        return json([]);
      }
      if (path === `/tenants/${tenantB.tenantId}/workspaces`) {
        return json([]);
      }
      if (path === `/tenants/${tenantA.tenantId}/members`) {
        return tenantAMembers;
      }
      if (path === `/tenants/${tenantB.tenantId}/members`) {
        return json([
          {
            membershipId: "m-b",
            userId: "33333333-3333-3333-3333-333333333333",
            displayName: "Bee",
            email: "bee@example.test",
            role: "Admin",
            status: "Active",
          },
        ]);
      }
      return json({ error: "missing" }, 404);
    }, `/app/tenants/${tenantA.tenantId}`);

    expect(await screen.findByRole("heading", { name: enMembers.title })).toBeTruthy();
    expect(screen.queryByText("Bee")).toBeNull();
    expect(screen.queryByText("Aye")).toBeNull();

    await user.selectOptions(screen.getByRole("combobox", { name: enTenants.selector }), tenantB.tenantId);
    expect(await screen.findByText("Bee")).toBeTruthy();

    resolveTenantAMembers?.(
      json([
        {
          membershipId: "m-a",
          userId: authUser.userId,
          displayName: "Aye",
          email: authUser.email,
          role: "Owner",
          status: "Active",
        },
      ]),
    );

    await waitFor(() => {
      expect(screen.getByText("Bee")).toBeTruthy();
    });
    expect(screen.queryByText("Aye")).toBeNull();
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
