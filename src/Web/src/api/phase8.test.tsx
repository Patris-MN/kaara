import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import "../i18n";
import App from "../App";
import { AuthProvider } from "../auth/AuthProvider";
import { getDirectionForLocale } from "../i18n/direction";
import enCommon from "../locales/en/common.json";
import enMembers from "../locales/en/members.json";
import enTenants from "../locales/en/tenants.json";
import enWorkspaces from "../locales/en/workspaces.json";
import arWorkspaces from "../locales/ar/workspaces.json";
import kuWorkspaces from "../locales/ku/workspaces.json";
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

type WorkspacePayload = {
  workspaceId: string;
  tenantId: string;
  name: string;
  description: string | null;
  startDate: string | null;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
  accessLevel: "Edit" | "View";
  canManage: boolean;
};

function workspace(
  overrides: Partial<WorkspacePayload> & Pick<WorkspacePayload, "workspaceId" | "name">,
): WorkspacePayload {
  return {
    tenantId: tenantA.tenantId,
    description: null,
    startDate: null,
    createdAtUtc: "2026-08-01T09:00:00Z",
    updatedAtUtc: "2026-08-30T09:00:00Z",
    accessLevel: "Edit",
    canManage: true,
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

async function enterWorkspacePage(
  fetchImpl: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>,
  workspaces: WorkspacePayload[] = [],
) {
  writeAccessToken("token-a");
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const path = pathOf(input);
      if (path === "/notifications") {
        return json({ items: [], unreadCount: 0 });
      }
      return fetchImpl(input, init);
    }),
  );
  renderApp(`/app/tenants/${tenantA.tenantId}`);
  expect(await screen.findByLabelText(enTenants.selector)).toBeTruthy();
  await waitFor(() => {
    expect(screen.getByRole("heading", { name: enWorkspaces.title })).toBeTruthy();
  });
  if (workspaces.length > 0) {
    await waitFor(() => {
      expect(screen.getByText(workspaces[0]!.name)).toBeTruthy();
    });
  } else {
    await waitFor(() => {
      expect(screen.queryByText(enCommon.loading)).toBeNull();
    });
  }
}

describe("phase 8 workspace resource UX", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    clearSession();
    cleanup();
  });

  it("renders workspace resources without permanent create or invite forms", async () => {
    const items = [
      workspace({ workspaceId: "w1", name: "Alpha", description: "First team" }),
      workspace({
        workspaceId: "w2",
        name: "Beta",
        updatedAtUtc: "2026-08-29T09:00:00Z",
        createdAtUtc: "2026-08-28T09:00:00Z",
      }),
    ];

    await enterWorkspacePage(async (input) => {
      const path = pathOf(input);
      if (path.endsWith("/auth/me")) return json(authUser);
      if (path.endsWith("/tenants")) return json([tenantA]);
      if (path.endsWith("/invitations")) return json([]);
      if (path === "/account/capabilities") {
        return json(deriveAccountCapabilities([tenantA]));
      }
      if (path.endsWith("/workspaces")) return json(items);
      return json({ error: "missing" }, 404);
    }, items);

    expect(screen.getByRole("button", { name: enWorkspaces.newWorkspace })).toBeTruthy();
    const toolbar = screen.getByRole("toolbar", { name: enWorkspaces.resourceToolbar });
    expect(within(toolbar).getByRole("button", { name: enWorkspaces.newWorkspace })).toBeTruthy();
    expect(document.querySelector(".page-heading-actions")).toBeNull();
    expect(screen.getByLabelText(enWorkspaces.searchLabel)).toBeTruthy();
    expect(screen.queryByLabelText(enTenants.inviteEmail)).toBeNull();
    expect(screen.queryByText(enMembers.title)).toBeNull();
    expect(screen.getByText("Alpha")).toBeTruthy();
    expect(screen.getByText("First team")).toBeTruthy();
  });

  it("opens create modal and shows success feedback", async () => {
    const user = userEvent.setup();
    let created = false;

    await enterWorkspacePage(async (input, init) => {
      const path = pathOf(input);
      const method = (init?.method ?? "GET").toUpperCase();
      if (path.endsWith("/auth/me")) return json(authUser);
      if (path.endsWith("/tenants")) return json([tenantA]);
      if (path.endsWith("/invitations")) return json([]);
      if (path === "/account/capabilities") {
        return json(deriveAccountCapabilities([tenantA]));
      }
      if (path.endsWith("/workspaces") && method === "GET") {
        return json(created ? [workspace({ workspaceId: "new", name: "Gamma" })] : []);
      }
      if (path.endsWith("/workspaces") && method === "POST") {
        created = true;
        return json(workspace({ workspaceId: "new", name: "Gamma", description: "New area" }), 201);
      }
      return json({ error: "missing" }, 404);
    });

    const [createButton] = screen.getAllByRole("button", { name: enWorkspaces.newWorkspace });
    await user.click(createButton);
    expect(await screen.findByLabelText(enWorkspaces.name)).toBeTruthy();
    await user.type(screen.getByLabelText(enWorkspaces.name), "Gamma");
    await user.click(screen.getByRole("button", { name: enWorkspaces.createWorkspace }));
    expect(await screen.findByRole("status")).toBeTruthy();
    expect(screen.getByText(enWorkspaces.createdTitle)).toBeTruthy();
  });

  it("supports search, sort, and grid/list together", async () => {
    const user = userEvent.setup();
    const items = [
      workspace({
        workspaceId: "w1",
        name: "Zulu",
        description: "Searchable phrase",
        updatedAtUtc: "2026-08-01T09:00:00Z",
      }),
      workspace({
        workspaceId: "w2",
        name: "Alpha",
        updatedAtUtc: "2026-08-30T09:00:00Z",
      }),
    ];

    await enterWorkspacePage(async (input) => {
      const path = pathOf(input);
      if (path.endsWith("/auth/me")) return json(authUser);
      if (path.endsWith("/tenants")) return json([tenantA]);
      if (path.endsWith("/invitations")) return json([]);
      if (path === "/account/capabilities") {
        return json(deriveAccountCapabilities([tenantA]));
      }
      if (path.endsWith("/workspaces")) return json(items);
      return json({ error: "missing" }, 404);
    }, items);

    expect(screen.getByText("Alpha")).toBeTruthy();
    expect(screen.getByText("Zulu")).toBeTruthy();

    await user.type(screen.getByLabelText(enWorkspaces.searchLabel), "nomatchxyz");
    expect(screen.getByText(enWorkspaces.searchEmptyTitle)).toBeTruthy();

    await user.click(screen.getByRole("button", { name: enWorkspaces.clearSearch }));
    expect(screen.getByText("Alpha")).toBeTruthy();

    await user.selectOptions(screen.getByLabelText(enWorkspaces.sortLabel), "nameAsc");
    const names = screen.getAllByRole("link").map((node) => node.textContent ?? "");
    expect(names.some((value) => value.includes("Alpha"))).toBeTruthy();
    expect(names.some((value) => value.includes("Zulu"))).toBeTruthy();

    await user.click(screen.getByRole("button", { name: enWorkspaces.viewList }));
    expect(screen.getByRole("button", { name: enWorkspaces.viewList }).getAttribute("aria-pressed")).toBe(
      "true",
    );
  });

  it("shows empty state for organizations with zero workspaces", async () => {
    await enterWorkspacePage(async (input) => {
      const path = pathOf(input);
      if (path.endsWith("/auth/me")) return json(authUser);
      if (path.endsWith("/tenants")) return json([tenantA]);
      if (path.endsWith("/invitations")) return json([]);
      if (path === "/account/capabilities") {
        return json(deriveAccountCapabilities([tenantA]));
      }
      if (path.endsWith("/workspaces")) return json([]);
      return json({ error: "missing" }, 404);
    });

    expect(screen.getByText(enWorkspaces.emptyTitle)).toBeTruthy();
    expect(screen.getAllByRole("button", { name: enWorkspaces.newWorkspace }).length).toBeGreaterThan(0);
  });

  it("keeps workspace locales available for EN, AR, and KU with RTL", () => {
    expect(enWorkspaces.newWorkspace).toBeTruthy();
    expect(arWorkspaces.newWorkspace).toBeTruthy();
    expect(kuWorkspaces.newWorkspace).toBeTruthy();
    expect(enWorkspaces.accessFull).toBeTruthy();
    expect(arWorkspaces.accessFull).toBeTruthy();
    expect(kuWorkspaces.accessFull).toBeTruthy();
    expect(enWorkspaces.accessCanEdit).toBeTruthy();
    expect(enWorkspaces.accessViewOnly).toBeTruthy();
    expect(getDirectionForLocale("ar")).toBe("rtl");
    expect(getDirectionForLocale("ku")).toBe("rtl");
  });

  it("does not apply stale workspace data after tenant switch", async () => {
    const user = userEvent.setup();
    const tenantB = {
      tenantId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
      name: "Org B",
      slug: "org-b",
      role: "Owner",
      status: "Active",
    };

    await enterWorkspacePage(async (input) => {
      const path = pathOf(input);
      if (path.endsWith("/auth/me")) return json(authUser);
      if (path.endsWith("/tenants")) return json([tenantA, tenantB]);
      if (path.endsWith("/invitations")) return json([]);
      if (path === "/account/capabilities") {
        return json(deriveAccountCapabilities([tenantA]));
      }
      const match = path.match(/^\/tenants\/([^/]+)\/workspaces$/);
      if (match?.[1] === tenantA.tenantId) {
        return json([workspace({ workspaceId: "a1", name: "A-Workspace-1" })]);
      }
      if (match?.[1] === tenantB.tenantId) {
        return json([workspace({ workspaceId: "b1", name: "B-Workspace-1", tenantId: tenantB.tenantId })]);
      }
      return json({ error: "missing" }, 404);
    }, [workspace({ workspaceId: "a1", name: "A-Workspace-1" })]);

    expect(screen.getByText("A-Workspace-1")).toBeTruthy();
    await user.selectOptions(screen.getByLabelText(enTenants.selector), tenantB.tenantId);
    expect(await screen.findByText("B-Workspace-1")).toBeTruthy();
    expect(screen.queryByText("A-Workspace-1")).toBeNull();
  });

  it("opens edit modal when overflow is available", async () => {
    const user = userEvent.setup();
    const items = [workspace({ workspaceId: "w1", name: "Editable" })];

    await enterWorkspacePage(async (input) => {
      const path = pathOf(input);
      if (path.endsWith("/auth/me")) return json(authUser);
      if (path.endsWith("/tenants")) return json([tenantA]);
      if (path.endsWith("/invitations")) return json([]);
      if (path === "/account/capabilities") {
        return json(deriveAccountCapabilities([tenantA]));
      }
      if (path.endsWith("/workspaces")) return json(items);
      return json({ error: "missing" }, 404);
    }, items);

    await user.click(screen.getByRole("button", { name: enWorkspaces.editWorkspace }));
    expect(await screen.findByRole("heading", { name: enWorkspaces.editWorkspace })).toBeTruthy();
    fireEvent.change(screen.getByLabelText(enWorkspaces.name), { target: { value: "Edited" } });
  });

  it("renders legacy and modern workspaces together", async () => {
    const user = userEvent.setup();
    const items = [
      workspace({
        workspaceId: "legacy",
        name: "Legacy workspace",
        description: null,
        startDate: null,
        updatedAtUtc: null,
      }),
      workspace({
        workspaceId: "modern",
        name: "Modern workspace",
        description: "Description",
        startDate: "2026-01-15",
      }),
    ];

    await enterWorkspacePage(async (input) => {
      const path = pathOf(input);
      if (path.endsWith("/auth/me")) return json(authUser);
      if (path.endsWith("/tenants")) return json([tenantA]);
      if (path.endsWith("/invitations")) return json([]);
      if (path === "/account/capabilities") {
        return json(deriveAccountCapabilities([tenantA]));
      }
      if (path.endsWith("/workspaces")) return json(items);
      return json({ error: "missing" }, 404);
    }, items);

    expect(screen.getByText("Legacy workspace")).toBeTruthy();
    expect(screen.getByText("Modern workspace")).toBeTruthy();
    await user.type(screen.getByLabelText(enWorkspaces.searchLabel), "legacy");
    expect(screen.getByText("Legacy workspace")).toBeTruthy();
    expect(screen.queryByText("Modern workspace")).toBeNull();
  });

  it("shows empty state only after a successful empty response", async () => {
    await enterWorkspacePage(async (input) => {
      const path = pathOf(input);
      if (path.endsWith("/auth/me")) return json(authUser);
      if (path.endsWith("/tenants")) return json([tenantA]);
      if (path.endsWith("/invitations")) return json([]);
      if (path === "/account/capabilities") {
        return json(deriveAccountCapabilities([tenantA]));
      }
      if (path.endsWith("/workspaces")) return json([]);
      return json({ error: "missing" }, 404);
    });

    expect(await screen.findByText(enWorkspaces.emptyTitle)).toBeTruthy();
    expect(screen.queryByText(enWorkspaces.loadErrorTitle)).toBeNull();
  });

  it("shows load error instead of empty state when the list request fails", async () => {
    await enterWorkspacePage(async (input) => {
      const path = pathOf(input);
      if (path.endsWith("/auth/me")) return json(authUser);
      if (path.endsWith("/tenants")) return json([tenantA]);
      if (path.endsWith("/invitations")) return json([]);
      if (path === "/account/capabilities") {
        return json(deriveAccountCapabilities([tenantA]));
      }
      if (path.endsWith("/workspaces")) return json({ error: "request_failed" }, 500);
      return json({ error: "missing" }, 404);
    });

    expect(await screen.findByText(enWorkspaces.loadErrorTitle)).toBeTruthy();
    expect(screen.queryByText(enWorkspaces.emptyTitle)).toBeNull();
  });

  it("shows load error on network failure and retries the tenant workspace request", async () => {
    const user = userEvent.setup();
    let attempts = 0;
    const items = [workspace({ workspaceId: "w1", name: "Recovered" })];

    await enterWorkspacePage(async (input) => {
      const path = pathOf(input);
      if (path.endsWith("/auth/me")) return json(authUser);
      if (path.endsWith("/tenants")) return json([tenantA]);
      if (path.endsWith("/invitations")) return json([]);
      if (path === "/account/capabilities") {
        return json(deriveAccountCapabilities([tenantA]));
      }
      if (path.endsWith("/workspaces")) {
        attempts += 1;
        if (attempts === 1) {
          throw new TypeError("Failed to fetch");
        }
        return json(items);
      }
      return json({ error: "missing" }, 404);
    });

    expect(await screen.findByText(enWorkspaces.loadErrorTitle)).toBeTruthy();
    expect(screen.queryByText(enWorkspaces.emptyTitle)).toBeNull();
    await user.click(screen.getByRole("button", { name: enWorkspaces.retryLoad }));
    expect(await screen.findByText("Recovered")).toBeTruthy();
    expect(attempts).toBe(2);
  });

  it("shows full access for Owner/Admin and can edit or view only for Members", async () => {
    const memberTenant = { ...tenantA, role: "Member" };
    const items = [
      workspace({ workspaceId: "edit", name: "Member Edit", accessLevel: "Edit", canManage: true }),
      workspace({ workspaceId: "view", name: "Member View", accessLevel: "View", canManage: false }),
    ];

    await enterWorkspacePage(async (input) => {
      const path = pathOf(input);
      if (path.endsWith("/auth/me")) return json(authUser);
      if (path.endsWith("/tenants")) return json([memberTenant]);
      if (path.endsWith("/invitations")) return json([]);
      if (path === "/account/capabilities") {
        return json(deriveAccountCapabilities([tenantA]));
      }
      if (path.endsWith("/workspaces")) return json(items);
      return json({ error: "missing" }, 404);
    }, items);

    expect(screen.getByText(enWorkspaces.accessCanEdit)).toBeTruthy();
    expect(screen.getByText(enWorkspaces.accessViewOnly)).toBeTruthy();
    expect(screen.queryByText(enWorkspaces.accessFull)).toBeNull();
  });

  it("shows full access for Owner workspaces and keeps edit action gated by canManage", async () => {
    const items = [
      workspace({ workspaceId: "managed", name: "Managed", accessLevel: "Edit", canManage: true }),
      workspace({ workspaceId: "readonly", name: "Readonly", accessLevel: "View", canManage: false }),
    ];

    await enterWorkspacePage(async (input) => {
      const path = pathOf(input);
      if (path.endsWith("/auth/me")) return json(authUser);
      if (path.endsWith("/tenants")) return json([tenantA]);
      if (path.endsWith("/invitations")) return json([]);
      if (path === "/account/capabilities") {
        return json(deriveAccountCapabilities([tenantA]));
      }
      if (path.endsWith("/workspaces")) return json(items);
      return json({ error: "missing" }, 404);
    }, items);

    expect(screen.getAllByText(enWorkspaces.accessFull)).toHaveLength(2);
    expect(document.querySelectorAll(".access-badge")).toHaveLength(2);
    expect(document.querySelectorAll(".role-badge")).toHaveLength(0);

    const editButtons = screen.getAllByRole("button", { name: enWorkspaces.editWorkspace });
    expect(editButtons).toHaveLength(1);
  });

  it("uses the same access labels in grid and list views", async () => {
    const user = userEvent.setup();
    const memberTenant = { ...tenantA, role: "Member" };
    const items = [
      workspace({ workspaceId: "edit", name: "Member Edit", accessLevel: "Edit", canManage: true }),
      workspace({ workspaceId: "view", name: "Member View", accessLevel: "View", canManage: false }),
    ];

    await enterWorkspacePage(async (input) => {
      const path = pathOf(input);
      if (path.endsWith("/auth/me")) return json(authUser);
      if (path.endsWith("/tenants")) return json([memberTenant]);
      if (path.endsWith("/invitations")) return json([]);
      if (path === "/account/capabilities") {
        return json(deriveAccountCapabilities([tenantA]));
      }
      if (path.endsWith("/workspaces")) return json(items);
      return json({ error: "missing" }, 404);
    }, items);

    expect(screen.getByText(enWorkspaces.accessCanEdit)).toBeTruthy();
    expect(screen.getByText(enWorkspaces.accessViewOnly)).toBeTruthy();

    await user.click(screen.getByRole("button", { name: enWorkspaces.viewList }));
    expect(screen.getByText(enWorkspaces.accessCanEdit)).toBeTruthy();
    expect(screen.getByText(enWorkspaces.accessViewOnly)).toBeTruthy();
  });
});
