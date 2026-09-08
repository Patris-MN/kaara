import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import "../i18n";
import App from "../App";
import { AuthProvider } from "../auth/AuthProvider";
import enProjects from "../locales/en/projects.json";
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
  name: "Example Org",
  slug: "example-org",
  role: "Owner",
  status: "Active",
};

const workspaceId = "22222222-2222-2222-2222-222222222222";
const exampleProjectName = "Example project";

const sampleProject = {
  projectId: "44444444-4444-4444-4444-444444444444",
  tenantId: tenantA.tenantId,
  workspaceId,
  name: exampleProjectName,
  accentToken: "indigo",
  description: "Structured project workspace",
  openTaskCount: 8,
  createdAtUtc: "2026-09-01T12:00:00Z",
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
      return json([tenantA]);
    }
    if (path === "/invitations") {
      return json([]);
    }
    if (path === "/account/capabilities") {
      return json(deriveAccountCapabilities([{ role: "Owner" }]));
    }
    const handler = overrides[path];
    if (handler) {
      return handler(init);
    }
    return json({}, 404);
  };
}

describe("phase 8.2A workspace projects UX", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    clearSession();
    cleanup();
  });

  it("shows Projects tab with identity initials instead of project key", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(
        baseFetch({
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`]: () =>
            json({
              workspaceId,
              tenantId: tenantA.tenantId,
              name: "Main workspace",
              accessLevel: "Edit",
              canManage: true,
              createdAtUtc: "2026-09-01T12:00:00Z",
            }),
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}/projects`]: () => json([sampleProject]),
        }),
      ),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`);
    await screen.findByRole("tab", { name: enProjects.workspaceTabs.projects });
    expect(screen.getByRole("tab", { name: enProjects.workspaceTabs.membersAccess })).toBeTruthy();
    expect(screen.queryByRole("heading", { name: enWorkspaces.memberAccess.title })).toBeNull();
    await waitFor(() => {
      expect(screen.getByText(exampleProjectName)).toBeTruthy();
      expect(screen.getByText("EP")).toBeTruthy();
    });
    expect(screen.queryByText("Key")).toBeNull();
    expect(screen.getAllByRole("button", { name: enProjects.newProject }).length).toBeGreaterThan(0);
  });

  it("opens compact create modal without project key and shows workspace access note", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(
        baseFetch({
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`]: () =>
            json({
              workspaceId,
              tenantId: tenantA.tenantId,
              name: "Main workspace",
              accessLevel: "Edit",
              canManage: true,
              createdAtUtc: "2026-09-01T12:00:00Z",
            }),
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}/projects`]: () => json([]),
        }),
      ),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`);
    const createButtons = await screen.findAllByRole("button", { name: enProjects.newProject });
    await userEvent.click(createButtons[0]!);
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByPlaceholderText(enProjects.namePlaceholder)).toBeTruthy();
    expect(within(dialog).getByPlaceholderText(enProjects.descriptionPlaceholder)).toBeTruthy();
    expect(within(dialog).getByText(enProjects.workspaceAccessNote)).toBeTruthy();
    expect(within(dialog).queryByLabelText(/project key/i)).toBeNull();
    expect(within(dialog).getByRole("radiogroup", { name: enProjects.colorLabel })).toBeTruthy();
    expect(within(dialog).queryByText(/member/i)).toBeNull();
    expect(within(dialog).getByText(enProjects.identityPreviewHint)).toBeTruthy();
    expect(within(dialog).getByText("PR")).toBeTruthy();
    expect(within(dialog).queryByText(enProjects.namePlaceholder)).toBeNull();
  });

  it("validates duplicate project name in create modal", async () => {
    writeAccessToken("token-a");
    let createCalls = 0;
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const path = pathOf(input);
        const method = init?.method ?? "GET";
        if (method === "POST" && path === `/tenants/${tenantA.tenantId}/workspaces/${workspaceId}/projects`) {
          createCalls += 1;
          return json({ error: "project_name_conflict", existingName: exampleProjectName }, 409);
        }
        return baseFetch({
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`]: () =>
            json({
              workspaceId,
              tenantId: tenantA.tenantId,
              name: "Main workspace",
              accessLevel: "Edit",
              canManage: true,
              createdAtUtc: "2026-09-01T12:00:00Z",
            }),
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}/projects`]: () => json([]),
        })(input, init);
      }),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`);
    const createButtons = await screen.findAllByRole("button", { name: enProjects.newProject });
    await userEvent.click(createButtons[0]!);
    const dialog = await screen.findByRole("dialog");
    await userEvent.type(within(dialog).getByPlaceholderText(enProjects.namePlaceholder), exampleProjectName);
    await userEvent.click(within(dialog).getByRole("button", { name: enProjects.createSubmit }));
    await screen.findByText(enProjects.errors.nameConflictNamed.replace("{{existingName}}", exampleProjectName));
    expect(createCalls).toBe(1);
  });

  it("hides New project for view-only workspace access and shows view-only empty state", async () => {
    writeAccessToken("token-v");
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const path = pathOf(input);
        if (path === "/tenants") {
          return json([{ ...tenantA, role: "Member" }]);
        }
        if (path === "/account/capabilities") {
          return json(deriveAccountCapabilities([{ role: "Member" }]));
        }
        return baseFetch({
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`]: () =>
            json({
              workspaceId,
              tenantId: tenantA.tenantId,
              name: "Main workspace",
              accessLevel: "View",
              canManage: false,
            }),
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}/projects`]: () => json([]),
        })(input, init);
      }),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`);
    await screen.findByText(enProjects.viewOnlyWorkspace);
    await screen.findByText(enProjects.emptyBodyViewOnly);
    expect(screen.queryByRole("button", { name: enProjects.newProject })).toBeNull();
  });

  it("shows creator empty state when workspace has zero projects", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(
        baseFetch({
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`]: () =>
            json({
              workspaceId,
              tenantId: tenantA.tenantId,
              name: "Main workspace",
              accessLevel: "Edit",
              canManage: true,
              createdAtUtc: "2026-09-01T12:00:00Z",
            }),
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}/projects`]: () => json([]),
        }),
      ),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`);
    await screen.findByText(enProjects.emptyTitle);
    await screen.findByText(enProjects.emptyBodyCreate);
    expect(screen.getAllByRole("button", { name: enProjects.newProject }).length).toBeGreaterThan(0);
  });

  it("filters projects by name only and distinguishes search-empty state", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(
        baseFetch({
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`]: () =>
            json({
              workspaceId,
              tenantId: tenantA.tenantId,
              name: "Main workspace",
              accessLevel: "Edit",
              canManage: true,
              createdAtUtc: "2026-09-01T12:00:00Z",
            }),
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}/projects`]: () =>
            json([
              sampleProject,
              {
                ...sampleProject,
                projectId: "55555555-5555-5555-5555-555555555555",
                name: "Other project",
                accentToken: "teal",
              },
            ]),
        }),
      ),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`);
    await screen.findByText(exampleProjectName);
    await userEvent.type(screen.getByLabelText(enProjects.searchLabel), "nonsense");
    await screen.findByText(enProjects.searchEmptyTitle);
    await screen.findByRole("button", { name: enProjects.clearSearch });
    expect(screen.queryByText(enProjects.emptyBodyCreate)).toBeNull();
  });
});
