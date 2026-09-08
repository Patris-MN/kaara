import { cleanup, render, screen, within } from "@testing-library/react";
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

function baseFetch() {
  return async (input: RequestInfo | URL) => {
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
    if (path === `/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`) {
      return json({
        workspaceId,
        tenantId: tenantA.tenantId,
        name: "Main workspace",
        accessLevel: "Edit",
        canManage: true,
        createdAtUtc: "2026-09-01T12:00:00Z",
      });
    }
    if (path === `/tenants/${tenantA.tenantId}/workspaces/${workspaceId}/projects`) {
      return json([
        sampleProject,
        {
          ...sampleProject,
          projectId: "55555555-5555-5555-5555-555555555555",
          name: "Other project",
          accentToken: "teal",
        },
      ]);
    }
    return json({}, 404);
  };
}

function viewToggleButtons() {
  return {
    grid: screen.getByRole("button", { name: enProjects.viewGridAccessible }),
    list: screen.getByRole("button", { name: enProjects.viewListAccessible }),
  };
}

describe("phase 8.2C project view toggle icon polish", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    clearSession();
    cleanup();
    localStorage.removeItem(`pts.projectView.${authUser.userId}`);
  });

  it("renders grid and list icons with visible labels", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal("fetch", vi.fn(baseFetch()));

    const { container } = renderApp(`/app/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`);
    await screen.findByText(exampleProjectName);

    const { grid, list } = viewToggleButtons();
    expect(within(grid).getByText(enProjects.viewGrid)).toBeTruthy();
    expect(within(list).getByText(enProjects.viewList)).toBeTruthy();

    const icons = container.querySelectorAll(".view-toggle-icon");
    expect(icons.length).toBe(2);
    expect(grid.querySelector(".view-toggle-icon rect")).toBeTruthy();
    expect(list.querySelector(".view-toggle-icon path")).toBeTruthy();
  });

  it("reflects selected state and switches between grid and list views", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal("fetch", vi.fn(baseFetch()));

    renderApp(`/app/tenants/${tenantA.tenantId}/workspaces/${workspaceId}`);
    await screen.findByText(exampleProjectName);

    const { grid, list } = viewToggleButtons();
    expect(grid.getAttribute("aria-pressed")).toBe("true");
    expect(list.getAttribute("aria-pressed")).toBe("false");
    expect(screen.getByRole("list")).toBeTruthy();

    await userEvent.click(list);
    expect(list.getAttribute("aria-pressed")).toBe("true");
    expect(grid.getAttribute("aria-pressed")).toBe("false");
    expect(screen.getByRole("table")).toBeTruthy();

    await userEvent.click(grid);
    expect(grid.getAttribute("aria-pressed")).toBe("true");
    expect(list.getAttribute("aria-pressed")).toBe("false");
    expect(screen.getByRole("list")).toBeTruthy();
  });

  it("exposes localized accessible names for EN, AR, and KU", () => {
    expect(enProjects.viewGridAccessible).toBe("Grid view");
    expect(enProjects.viewListAccessible).toBe("List view");
    expect(arProjects.viewGridAccessible).toBeTruthy();
    expect(arProjects.viewListAccessible).toBeTruthy();
    expect(kuProjects.viewGridAccessible).toBeTruthy();
    expect(kuProjects.viewListAccessible).toBeTruthy();
  });
});
