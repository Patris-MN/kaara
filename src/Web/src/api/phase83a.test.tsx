import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import "../i18n";
import App from "../App";
import { AuthProvider } from "../auth/AuthProvider";
import enCommon from "../locales/en/common.json";
import enTasks from "../locales/en/tasks.json";
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

const workspace = {
  workspaceId: "cccccccc-cccc-cccc-cccc-cccccccccccc",
  tenantId: tenantA.tenantId,
  name: "Leopard",
  accessLevel: "Edit" as const,
};

const project = {
  projectId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
  tenantId: tenantA.tenantId,
  workspaceId: workspace.workspaceId,
  name: "Spots",
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

const taskBase = `/tenants/${tenantA.tenantId}/workspaces/${workspace.workspaceId}/projects/${project.projectId}`;
const taskPage = `/app/tenants/${tenantA.tenantId}/workspaces/${workspace.workspaceId}/projects/${project.projectId}`;

function shell(path: string) {
  if (path.endsWith("/auth/me")) {
    return json(authUser);
  }
  if (path.endsWith("/tenants")) {
    return json([tenantA]);
  }
  if (path.endsWith("/invitations")) {
    return json([]);
  }
  if (path.endsWith("/assignable-members") || path.endsWith("/tags")) {
    return json([]);
  }
  if (path === "/notifications") {
    return json({ items: [], unreadCount: 0 });
  }
  if (path.endsWith("/notifications")) {
    return json([]);
  }
  if (path.endsWith("/account/capabilities")) {
    return json(deriveAccountCapabilities([tenantA]));
  }
  return null;
}

describe("phase 8.3A task load failure recovery", () => {
  afterEach(() => {
    cleanup();
    clearSession();
    vi.unstubAllGlobals();
  });

  it("shows loading then tasks without empty state on success", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const path = pathOf(input);
        const handled = shell(path);
        if (handled) {
          return handled;
        }
        if (path.endsWith(`/workspaces/${workspace.workspaceId}`)) {
          return json(workspace);
        }
        if (path === taskBase) {
          return json(project);
        }
        if (path.endsWith("/tasks")) {
          return json([
            {
              taskId: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
              tenantId: tenantA.tenantId,
              workspaceId: workspace.workspaceId,
              projectId: project.projectId,
              title: "Kickoff",
              status: "Open",
              priority: "Normal",
              createdAtUtc: "2026-08-01T00:00:00Z",
              updatedAtUtc: "2026-08-01T00:00:00Z",
              unseenActivityCount: 0,
              capabilities: {
                canEditDefinition: true,
                canManageTags: true,
                canReassign: true,
                canComment: true,
                canDelete: true,
                allowedStatuses: ["Open"],
              },
            },
          ]);
        }
        return json({ error: "missing" }, 404);
      }),
    );

    render(
      <MemoryRouter initialEntries={[taskPage]}>
        <AuthProvider>
          <TenantDirectoryProvider>
            <App />
          </TenantDirectoryProvider>
        </AuthProvider>
      </MemoryRouter>,
    );

    expect(await screen.findByText("Kickoff")).toBeTruthy();
    expect(screen.queryByText(enTasks.emptyTitle)).toBeNull();
    expect(screen.queryByText(enTasks.loadFailedTitle)).toBeNull();
  });

  it("shows load failure without no tasks yet and retries successfully", async () => {
    const user = userEvent.setup();
    let attempts = 0;
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const path = pathOf(input);
        const handled = shell(path);
        if (handled) {
          return handled;
        }
        if (path.endsWith(`/workspaces/${workspace.workspaceId}`)) {
          return json(workspace);
        }
        if (path === taskBase) {
          return json(project);
        }
        if (path.endsWith("/tasks")) {
          attempts += 1;
          if (attempts === 1) {
            return json({ error: "request_failed" }, 500);
          }
          return json([
            {
              taskId: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
              tenantId: tenantA.tenantId,
              workspaceId: workspace.workspaceId,
              projectId: project.projectId,
              title: "Recovered task",
              status: "Open",
              priority: "Normal",
              createdAtUtc: "2026-08-01T00:00:00Z",
              updatedAtUtc: "2026-08-01T00:00:00Z",
              unseenActivityCount: 0,
              capabilities: {
                canEditDefinition: true,
                canManageTags: true,
                canReassign: true,
                canComment: true,
                canDelete: true,
                allowedStatuses: ["Open"],
              },
            },
          ]);
        }
        return json({ error: "missing" }, 404);
      }),
    );

    render(
      <MemoryRouter initialEntries={[taskPage]}>
        <AuthProvider>
          <TenantDirectoryProvider>
            <App />
          </TenantDirectoryProvider>
        </AuthProvider>
      </MemoryRouter>,
    );

    expect(await screen.findByText(enTasks.loadFailedTitle)).toBeTruthy();
    expect(screen.getByText(enTasks.loadFailedBody)).toBeTruthy();
    expect(screen.queryByText(enTasks.emptyTitle)).toBeNull();
    expect(screen.queryByText(enCommon.errors.request_failed)).toBeNull();

    await user.click(screen.getByRole("button", { name: enTasks.retryLoad }));
    expect(await screen.findByText("Recovered task")).toBeTruthy();
    expect(screen.queryByText(enTasks.loadFailedTitle)).toBeNull();
    expect(attempts).toBe(2);
  });

  it("shows empty state only when load succeeds with zero tasks", async () => {
    writeAccessToken("token-a");
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const path = pathOf(input);
        const handled = shell(path);
        if (handled) {
          return handled;
        }
        if (path.endsWith(`/workspaces/${workspace.workspaceId}`)) {
          return json(workspace);
        }
        if (path === taskBase) {
          return json(project);
        }
        if (path.endsWith("/tasks")) {
          return json([]);
        }
        return json({ error: "missing" }, 404);
      }),
    );

    render(
      <MemoryRouter initialEntries={[taskPage]}>
        <AuthProvider>
          <TenantDirectoryProvider>
            <App />
          </TenantDirectoryProvider>
        </AuthProvider>
      </MemoryRouter>,
    );

    expect(await screen.findByText(enTasks.emptyTitle)).toBeTruthy();
    expect(screen.queryByText(enTasks.loadFailedTitle)).toBeNull();
  });
});
