import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import "../i18n";
import App from "../App";
import { AuthProvider } from "../auth/AuthProvider";
import arProjects from "../locales/ar/projects.json";
import enProjects from "../locales/en/projects.json";
import kuProjects from "../locales/ku/projects.json";
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

type TenantFixture = typeof tenantA;

const workspaceId = "22222222-2222-2222-2222-222222222222";
const exampleProjectName = "Example project";
const updatedProjectName = "Renamed project";

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
  tenant: TenantFixture = tenantA,
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
      return json([tenant]);
    }
    if (path === "/invitations") {
      return json([]);
    }
    if (path === "/account/capabilities") {
      return json(deriveAccountCapabilities([{ role: tenant.role }]));
    }
    const handler = overrides[path];
    if (handler) {
      return handler(init);
    }
    return json({}, 404);
  };
}

function workspaceHandler(accessLevel: "Edit" | "View" = "Edit") {
  return () =>
    json({
      workspaceId,
      tenantId: tenantA.tenantId,
      name: "Main workspace",
      accessLevel,
      canManage: accessLevel === "Edit",
      createdAtUtc: "2026-09-01T12:00:00Z",
    });
}

describe("phase 8.2B workspace projects density and metadata editing", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    clearSession();
    cleanup();
  });

  it("integrates project count in header and uses compact toolbar without duplicate summary row", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(
        baseFetch({
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`]: workspaceHandler(),
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}/projects`]: () => json([sampleProject]),
        }),
      ),
    );

    const { container } = renderApp(`/app/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`);
    await screen.findByText(exampleProjectName);

    expect(
      screen.getByText(
        `${enProjects.workspacePageDescription} · ${enProjects.resourceSummary.replace("{{count}}", "1")}`,
      ),
    ).toBeTruthy();
    expect(container.querySelector(".resource-toolbar-summary")).toBeNull();
    expect(container.querySelector(".workspace-projects-toolbar")).toBeTruthy();
    expect(container.querySelector(".workspace-projects-page")).toBeTruthy();
    expect(screen.getByRole("toolbar", { name: enProjects.resourceToolbar })).toBeTruthy();
    expect(screen.getByLabelText(enProjects.searchLabel)).toBeTruthy();
  });

  it("shows edit project action for owner and opens prefilled modal without key or access fields", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(
        baseFetch({
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`]: workspaceHandler(),
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}/projects`]: () => json([sampleProject]),
        }),
      ),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`);
    await screen.findByText(exampleProjectName);

    const menu = screen.getByRole("button", {
      name: enProjects.projectActions.replace("{{name}}", exampleProjectName),
    });
    await userEvent.click(menu);
    await userEvent.click(screen.getByRole("menuitem", { name: enProjects.editProject }));

    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByRole("heading", { name: enProjects.editDialogTitle })).toBeTruthy();
    expect(within(dialog).getByDisplayValue(exampleProjectName)).toBeTruthy();
    expect(within(dialog).getByDisplayValue(sampleProject.description)).toBeTruthy();
    expect(within(dialog).queryByLabelText(/project key/i)).toBeNull();
    expect(within(dialog).queryByText(/member/i)).toBeNull();
    expect(within(dialog).getByText(enProjects.identityPreviewHint)).toBeTruthy();
    expect(within(dialog).getByText("EP")).toBeTruthy();
  });

  it("shows edit project action for admin tenant role", async () => {
    writeAccessToken("token-adm");
    const adminTenant: TenantFixture = { ...tenantA, role: "Admin" };
    vi.stubGlobal(
      "fetch",
      vi.fn(
        baseFetch(
          {
            [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`]: workspaceHandler(),
            [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}/projects`]: () => json([sampleProject]),
          },
          adminTenant,
        ),
      ),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`);
    await screen.findByText(exampleProjectName);
    expect(
      screen.getByRole("button", {
        name: enProjects.projectActions.replace("{{name}}", exampleProjectName),
      }),
    ).toBeTruthy();
  });

  it("hides edit project for member with workspace edit but keeps new project", async () => {
    writeAccessToken("token-mem");
    const memberTenant: TenantFixture = { ...tenantA, role: "Member" };
    vi.stubGlobal(
      "fetch",
      vi.fn(
        baseFetch(
          {
            [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`]: workspaceHandler(),
            [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}/projects`]: () => json([sampleProject]),
          },
          memberTenant,
        ),
      ),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`);
    await screen.findByText(exampleProjectName);
    expect(screen.getAllByRole("button", { name: enProjects.newProject }).length).toBeGreaterThan(0);
    expect(
      screen.queryByRole("button", {
        name: enProjects.projectActions.replace("{{name}}", exampleProjectName),
      }),
    ).toBeNull();
  });

  it("hides edit and create actions for member with workspace view access", async () => {
    writeAccessToken("token-view");
    const memberTenant: TenantFixture = { ...tenantA, role: "Member" };
    vi.stubGlobal(
      "fetch",
      vi.fn(
        baseFetch(
          {
            [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`]: workspaceHandler("View"),
            [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}/projects`]: () => json([sampleProject]),
          },
          memberTenant,
        ),
      ),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`);
    await screen.findByText(exampleProjectName);
    expect(screen.queryByRole("button", { name: enProjects.newProject })).toBeNull();
    expect(
      screen.queryByRole("button", {
        name: enProjects.projectActions.replace("{{name}}", exampleProjectName),
      }),
    ).toBeNull();
  });

  it("saves project metadata updates and refreshes card content", async () => {
    writeAccessToken("token-a");
    let patchCalls = 0;
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const path = pathOf(input);
        const method = init?.method ?? "GET";
        if (
          method === "PATCH" &&
          path === `/tenants/${tenantA.tenantId}/workspaces/${workspaceId}/projects/${sampleProject.projectId}`
        ) {
          patchCalls += 1;
          return json({
            ...sampleProject,
            name: updatedProjectName,
            description: "Updated copy",
            accentToken: "teal",
          });
        }
        return baseFetch({
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`]: workspaceHandler(),
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}/projects`]: () => json([sampleProject]),
        })(input, init);
      }),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`);
    await screen.findByText(exampleProjectName);

    const menu = screen.getByRole("button", {
      name: enProjects.projectActions.replace("{{name}}", exampleProjectName),
    });
    await userEvent.click(menu);
    await userEvent.click(screen.getByRole("menuitem", { name: enProjects.editProject }));

    const dialog = await screen.findByRole("dialog");
    const nameInput = within(dialog).getByDisplayValue(exampleProjectName);
    await userEvent.clear(nameInput);
    await userEvent.type(nameInput, updatedProjectName);
    await userEvent.click(within(dialog).getByRole("button", { name: enProjects.saveChanges }));

    await waitFor(() => {
      expect(patchCalls).toBe(1);
      expect(screen.queryByRole("dialog")).toBeNull();
      expect(screen.getByText(updatedProjectName)).toBeTruthy();
    });
    await screen.findByText(enProjects.updatedTitle);
  });

  it("shows duplicate-name error and keeps edit modal open", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const path = pathOf(input);
        const method = init?.method ?? "GET";
        if (
          method === "PATCH" &&
          path === `/tenants/${tenantA.tenantId}/workspaces/${workspaceId}/projects/${sampleProject.projectId}`
        ) {
          return json({ error: "project_name_conflict", existingName: "Beta project" }, 409);
        }
        return baseFetch({
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`]: workspaceHandler(),
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}/projects`]: () => json([sampleProject]),
        })(input, init);
      }),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`);
    await screen.findByText(exampleProjectName);
    await userEvent.click(
      screen.getByRole("button", {
        name: enProjects.projectActions.replace("{{name}}", exampleProjectName),
      }),
    );
    await userEvent.click(screen.getByRole("menuitem", { name: enProjects.editProject }));

    const dialog = await screen.findByRole("dialog");
    const nameInput = within(dialog).getByDisplayValue(exampleProjectName);
    await userEvent.clear(nameInput);
    await userEvent.type(nameInput, "Beta project");
    await userEvent.click(within(dialog).getByRole("button", { name: enProjects.saveChanges }));

    await screen.findByText(
      enProjects.errors.nameConflictNamed.replace("{{existingName}}", "Beta project"),
    );
    expect(screen.getByRole("dialog")).toBeTruthy();
  });

  it("keeps edit modal open on generic update failure", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const path = pathOf(input);
        const method = init?.method ?? "GET";
        if (
          method === "PATCH" &&
          path === `/tenants/${tenantA.tenantId}/workspaces/${workspaceId}/projects/${sampleProject.projectId}`
        ) {
          return json({ error: "server_error" }, 500);
        }
        return baseFetch({
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`]: workspaceHandler(),
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}/projects`]: () => json([sampleProject]),
        })(input, init);
      }),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`);
    await screen.findByText(exampleProjectName);
    await userEvent.click(
      screen.getByRole("button", {
        name: enProjects.projectActions.replace("{{name}}", exampleProjectName),
      }),
    );
    await userEvent.click(screen.getByRole("menuitem", { name: enProjects.editProject }));

    const dialog = await screen.findByRole("dialog");
    await userEvent.click(within(dialog).getByRole("button", { name: enProjects.saveChanges }));
    await screen.findByText(enProjects.errors.updateFailed);
    expect(screen.getByRole("dialog")).toBeTruthy();
  });

  it("does not navigate when opening the project actions menu", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(
        baseFetch({
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`]: workspaceHandler(),
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}/projects`]: () => json([sampleProject]),
        }),
      ),
    );

    renderApp(`/app/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`);
    await screen.findByText(exampleProjectName);

    await userEvent.click(
      screen.getByRole("button", {
        name: enProjects.projectActions.replace("{{name}}", exampleProjectName),
      }),
    );
    expect(screen.getByRole("menuitem", { name: enProjects.editProject })).toBeTruthy();
    expect(screen.getByText(exampleProjectName)).toBeTruthy();
  });

  it("supports list-view edit actions", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(
        baseFetch({
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`]: workspaceHandler(),
          [`/tenants/${tenantA.tenantId}/workspaces/${workspaceId}/projects`]: () => json([sampleProject]),
        }),
      ),
    );

    localStorage.setItem(`pts.projectView.${authUser.userId}`, "list");
    renderApp(`/app/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`);
    await screen.findByRole("columnheader", { name: enProjects.listColumns.actions });

    await userEvent.click(
      screen.getByRole("button", {
        name: enProjects.projectActions.replace("{{name}}", exampleProjectName),
      }),
    );
    expect(screen.getByRole("menuitem", { name: enProjects.editProject })).toBeTruthy();
    localStorage.removeItem(`pts.projectView.${authUser.userId}`);
  });

  it("exposes localized edit strings for EN, AR, and KU", () => {
    expect(enProjects.editProject).toBeTruthy();
    expect(arProjects.editProject).toBeTruthy();
    expect(kuProjects.editProject).toBeTruthy();
    expect(enProjects.saveChanges).toBeTruthy();
    expect(arProjects.saveChanges).toBeTruthy();
    expect(kuProjects.saveChanges).toBeTruthy();
    expect(enProjects.errors.editForbidden).toBeTruthy();
    expect(arProjects.errors.editForbidden).toBeTruthy();
    expect(kuProjects.errors.editForbidden).toBeTruthy();
  });
});
