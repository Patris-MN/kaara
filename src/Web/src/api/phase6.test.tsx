import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import "../i18n";
import App from "../App";
import { AuthProvider } from "../auth/AuthProvider";
import { getDirectionForLocale } from "../i18n/direction";
import enAuth from "../locales/en/auth.json";
import enCommon from "../locales/en/common.json";
import enTasks from "../locales/en/tasks.json";
import enTenants from "../locales/en/tenants.json";
import arTasks from "../locales/ar/tasks.json";
import kuTasks from "../locales/ku/tasks.json";
import { formatTaskDate } from "../tasks/presentation";
import { TenantDirectoryProvider } from "../tenancy/TenantDirectoryProvider";
import { clearSession, writeAccessToken } from "./session";
import type { WorkTask } from "./types";

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
  role: "Member",
  status: "Active",
};

const tenantB = {
  tenantId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  name: "Org B",
  slug: "org-b",
  role: "Member",
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

const firstTask: WorkTask = {
  taskId: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
  tenantId: tenantA.tenantId,
  workspaceId: workspace.workspaceId,
  projectId: project.projectId,
  title: "Kickoff",
  description: "Prepare notes",
  status: "Open",
  priority: "Normal",
  dueDate: "2026-09-01",
  createdAtUtc: "2026-08-01T00:00:00Z",
  updatedAtUtc: "2026-08-01T00:00:00Z",
  assigneeMembershipId: null,
  assigneeDisplayName: null,
  assigneeEmail: null,
  tags: [],
  createdByMembershipId: "16161616-1616-1616-1616-161616161616",
  createdByDisplayName: "User A",
  createdByEmail: authUser.email,
  unseenActivityCount: 0,
  capabilities: null,
};

const overdueTask: WorkTask = {
  ...firstTask,
  taskId: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
  title: "Late brief",
  dueDate: "2020-01-15",
  priority: "Urgent",
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

const taskBase = `/tenants/${tenantA.tenantId}/workspaces/${workspace.workspaceId}/projects/${project.projectId}`;
const taskPage = `/app/tenants/${tenantA.tenantId}/workspaces/${workspace.workspaceId}/projects/${project.projectId}`;

async function enterApp(
  fetchImpl: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>,
  path: string,
) {
  writeAccessToken("token-a");
  vi.stubGlobal("fetch", vi.fn(fetchImpl));
  renderApp(path);
  expect(await screen.findByLabelText(enTenants.selector)).toBeTruthy();
}

function shellHandlers(path: string) {
  if (path.endsWith("/auth/me")) {
    return json(authUser);
  }
  if (path.endsWith("/tenants")) {
    return json([{ ...tenantA, role: "Owner" }]);
  }
  if (path.endsWith("/invitations")) {
    return json([]);
  }
  if (path.endsWith("/assignable-members")) {
    return json([]);
  }
  if (path.endsWith(`/${workspace.workspaceId}/tags`)) {
    return json([]);
  }
  if (path.endsWith("/notifications")) {
    return json([]);
  }
  return null;
}

describe("phase 6 task management UI", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    clearSession();
    cleanup();
  });

  it("creates a task with priority and deadline, then edits and resets the form", async () => {
    const tasks = [firstTask];
    const user = userEvent.setup();
    let lastCreate: { title?: string; priority?: string; dueDate?: string | null } | undefined;
    let lastUpdate: { title?: string; priority?: string; dueDate?: string | null; status?: string } | undefined;

    await enterApp(async (input, init) => {
      const path = pathOf(input);
      const method = (init?.method ?? "GET").toUpperCase();
      const shell = shellHandlers(path);
      if (shell) {
        return shell;
      }
      if (path.endsWith(`/workspaces/${workspace.workspaceId}`)) {
        return json(workspace);
      }
      if (path === `${taskBase}` && method === "GET") {
        return json(project);
      }
      if (path === `${taskBase}/tasks` && method === "GET") {
        return json(tasks);
      }
      if (path === `${taskBase}/tasks` && method === "POST") {
        lastCreate = JSON.parse(String(init?.body)) as typeof lastCreate;
        if (!lastCreate?.title) {
          return json({ error: "invalid_task" }, 400);
        }
        const created: WorkTask = {
          ...firstTask,
          taskId: "ffffffff-ffff-ffff-ffff-ffffffffffff",
          title: lastCreate.title ?? "New task",
          status: "Open",
          priority: (lastCreate.priority as WorkTask["priority"]) ?? "Normal",
          description: null,
          dueDate: lastCreate.dueDate ?? null,
        };
        tasks.push(created);
        return json(created, 201);
      }
      if (path.endsWith("/comments") || path.endsWith("/activity")) {
        return json([]);
      }
      if (path.endsWith(`/tasks/${firstTask.taskId}`) && method === "GET") {
        return json(tasks[0] ?? firstTask);
      }
      if (path.endsWith(`/tasks/${firstTask.taskId}`) && method === "PUT") {
        lastUpdate = JSON.parse(String(init?.body)) as typeof lastUpdate;
        const updated: WorkTask = {
          ...firstTask,
          title: lastUpdate?.title ?? firstTask.title,
          status: (lastUpdate?.status as WorkTask["status"]) ?? firstTask.status,
          priority: (lastUpdate?.priority as WorkTask["priority"]) ?? firstTask.priority,
          dueDate: lastUpdate?.dueDate ?? null,
        };
        tasks[0] = updated;
        return json(updated);
      }
      return json({ error: "missing" }, 404);
    }, taskPage);

    expect(await screen.findByText("Kickoff")).toBeTruthy();
    expect(screen.getByText(enTasks.priority.normal)).toBeTruthy();
    expect(screen.getByText(new RegExp(formatTaskDate("2026-09-01")))).toBeTruthy();
    expect(screen.queryByRole("dialog")).toBeNull();

    await user.click(screen.getByRole("button", { name: enTasks.create }));
    const prioritySelect = screen.getByLabelText(enTasks.priority.label);
    expect(prioritySelect).toHaveProperty("value", "Normal");
    expect(screen.getByRole("option", { name: new RegExp(enTasks.priority.low) })).toBeTruthy();
    expect(screen.getByRole("option", { name: new RegExp(enTasks.priority.normal) })).toBeTruthy();
    expect(screen.getByRole("option", { name: new RegExp(enTasks.priority.high) })).toBeTruthy();
    expect(screen.getByRole("option", { name: new RegExp(enTasks.priority.urgent) })).toBeTruthy();

    await user.type(screen.getByLabelText(enTasks.fields.title), "New task");
    await user.selectOptions(prioritySelect, "Urgent");
    expect(prioritySelect).toHaveProperty("value", "Urgent");
    fireEvent.change(screen.getByLabelText(enTasks.deadline.label), { target: { value: "2026-09-15" } });
    expect(screen.getByText(formatTaskDate("2026-09-15"))).toBeTruthy();
    await user.click(screen.getByRole("button", { name: enTasks.create }));

    expect(await screen.findByText("New task")).toBeTruthy();
    expect(lastCreate).toMatchObject({
      title: "New task",
      priority: "Urgent",
      dueDate: "2026-09-15",
    });
    expect(screen.queryByRole("dialog")).toBeNull();
    await user.click(screen.getByRole("button", { name: enTasks.create }));
    expect((screen.getByLabelText(enTasks.fields.title) as HTMLInputElement).value).toBe("");
    expect((screen.getByLabelText(enTasks.priority.label) as HTMLSelectElement).value).toBe("Normal");
    expect(screen.getByText(enTasks.deadline.none)).toBeTruthy();
    await user.click(screen.getByRole("button", { name: enCommon.cancel }));

    await user.click(screen.getByRole("button", { name: /Kickoff/ }));
    const titleField = await screen.findByLabelText(enTasks.fields.title, { selector: "#edit-task-title" });
    await user.clear(titleField);
    await user.type(titleField, "Kickoff done");
    await user.selectOptions(
      screen.getByLabelText(enTasks.fields.status, { selector: "#edit-task-status" }),
      "Closed",
    );
    await user.selectOptions(
      screen.getByLabelText(enTasks.priority.label, { selector: "#edit-task-priority" }),
      "High",
    );
    fireEvent.change(screen.getByLabelText(enTasks.deadline.label, { selector: "#edit-task-due" }), {
      target: { value: "2026-10-01" },
    });
    await user.click(screen.getByRole("button", { name: enTasks.save }));
    expect(await screen.findByText("Kickoff done")).toBeTruthy();
    expect(lastUpdate).toMatchObject({
      title: "Kickoff done",
      status: "Closed",
      priority: "High",
      dueDate: "2026-10-01",
    });
    expect(screen.getByText(enTasks.priority.high)).toBeTruthy();
  });

  it("can remove a deadline and keeps input after a failed create", async () => {
    const user = userEvent.setup();
    let created = false;
    await enterApp(async (input, init) => {
      const path = pathOf(input);
      const method = (init?.method ?? "GET").toUpperCase();
      const shell = shellHandlers(path);
      if (shell) {
        return shell;
      }
      if (path.endsWith(`/workspaces/${workspace.workspaceId}`)) {
        return json(workspace);
      }
      if (path === taskBase || path.endsWith("/tasks") && method === "GET") {
        return path === taskBase ? json(project) : json([]);
      }
      if (path.endsWith("/tasks") && method === "POST") {
        if (!created) {
          return json({ error: "invalid_task" }, 400);
        }
        return json({ ...firstTask, title: "Kept title" }, 201);
      }
      return json({ error: "missing" }, 404);
    }, taskPage);

    expect(await screen.findByText(enTasks.emptyTitle)).toBeTruthy();
    await user.click(screen.getByRole("button", { name: enTasks.create }));
    const title = screen.getByLabelText(enTasks.fields.title);
    await user.type(title, "Kept title");
    fireEvent.change(screen.getByLabelText(enTasks.deadline.label), { target: { value: "2026-09-15" } });
    await user.click(screen.getByRole("button", { name: enTasks.deadline.remove }));
    expect(screen.getByText(enTasks.deadline.none)).toBeTruthy();
    fireEvent.change(screen.getByLabelText(enTasks.deadline.label), { target: { value: "2026-09-15" } });
    await user.click(screen.getByRole("button", { name: enTasks.create }));
    expect(await screen.findByText(enCommon.errors.invalid_task)).toBeTruthy();
    expect((screen.getByLabelText(enTasks.fields.title) as HTMLInputElement).value).toBe("Kept title");
    expect(screen.queryByRole("button", { name: /Kept title/ })).toBeNull();
  });

  it("shows overdue text for incomplete past-due tasks", async () => {
    await enterApp(async (input) => {
      const path = pathOf(input);
      const shell = shellHandlers(path);
      if (shell) {
        return shell;
      }
      if (path.endsWith(`/workspaces/${workspace.workspaceId}`)) {
        return json(workspace);
      }
      if (path === taskBase) {
        return json(project);
      }
      if (path.endsWith("/tasks")) {
        return json([overdueTask, { ...overdueTask, taskId: "done-task", title: "Finished", status: "Closed" }]);
      }
      return json({ error: "missing" }, 404);
    }, taskPage);

    expect(await screen.findByText("Late brief")).toBeTruthy();
    expect(screen.getAllByText(enTasks.deadline.overdue)).toHaveLength(1);
    expect(screen.getAllByText(enTasks.priority.urgent).length).toBeGreaterThan(0);
  });

  it("hides mutation controls for View and the whole page for no access", async () => {
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
      if (path.endsWith("/assignable-members") || path.endsWith("/notifications") || path.endsWith("/tags")) {
        return json([]);
      }
      if (path.endsWith(`/workspaces/${workspace.workspaceId}`)) {
        return json({ ...workspace, accessLevel: "View" });
      }
      if (path === taskBase) {
        return json(project);
      }
      if (path.endsWith("/tasks")) {
        return json([firstTask]);
      }
      return json({ error: "missing" }, 404);
    }, taskPage);

    expect(await screen.findByText("Kickoff")).toBeTruthy();
    expect(screen.getByText(enTasks.viewOnly)).toBeTruthy();
    expect(screen.queryByRole("heading", { name: enTasks.create })).toBeNull();
    expect(screen.queryByRole("button", { name: enTasks.create })).toBeNull();
    expect(screen.queryByRole("button", { name: enTasks.save })).toBeNull();
    expect(screen.queryByRole("button", { name: enTasks.deadline.remove })).toBeNull();

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
      if (path.endsWith("/notifications")) {
        return json([]);
      }
      return json({ error: "workspace_not_found" }, 404);
    }, taskPage);

    expect((await screen.findByRole("alert")).textContent).toContain(enCommon.errors.project_not_found);
    expect(screen.queryByRole("heading", { name: enTasks.create })).toBeNull();
  });

  it("logs out on 401 and stays signed in on 403", async () => {
    await enterApp(async (input) => {
      const path = pathOf(input);
      const shell = shellHandlers(path);
      if (shell) {
        return shell;
      }
      if (path.includes("/tasks")) {
        return json({ error: "unauthenticated" }, 401);
      }
      if (path.endsWith(`/workspaces/${workspace.workspaceId}`)) {
        return json(workspace);
      }
      if (path === taskBase) {
        return json(project);
      }
      return json({ error: "missing" }, 404);
    }, taskPage);

    expect((await screen.findAllByRole("heading", { name: enAuth.signIn })).length).toBeGreaterThan(0);

    cleanup();
    clearSession();
    vi.unstubAllGlobals();

    await enterApp(async (input) => {
      const path = pathOf(input);
      const shell = shellHandlers(path);
      if (shell) {
        return shell;
      }
      if (path.includes("/workspaces") || path.includes("/projects")) {
        return json({ error: "tenant_access_denied" }, 403);
      }
      return json({ error: "missing" }, 404);
    }, taskPage);

    expect(await screen.findByLabelText(enTenants.selector)).toBeTruthy();
    expect((await screen.findByRole("alert")).textContent).toContain(enCommon.errors.forbidden);
    expect(screen.queryByRole("heading", { name: enAuth.signIn })).toBeNull();
  });

  it("clears stale task state when the project route changes", async () => {
    let resolveFirst: ((response: Response) => void) | undefined;
    const firstTasks = new Promise<Response>((resolve) => {
      resolveFirst = resolve;
    });
    const otherProjectId = "99999999-9999-9999-9999-999999999999";

    await enterApp(async (input) => {
      const path = pathOf(input);
      if (path.endsWith("/auth/me")) {
        return json(authUser);
      }
      if (path.endsWith("/tenants")) {
        return json([
          { ...tenantA, role: "Owner" },
          { ...tenantB, role: "Owner" },
        ]);
      }
      if (path.endsWith("/invitations")) {
        return json([]);
      }
      if (path.endsWith("/assignable-members") || path.endsWith("/notifications") || path.endsWith("/tags")) {
        return json([]);
      }
      if (path.endsWith(`/workspaces/${workspace.workspaceId}`)) {
        return json(workspace);
      }
      if (path === taskBase) {
        return json(project);
      }
      if (path === `${taskBase}/tasks`) {
        return firstTasks;
      }
      return json({ error: "missing" }, 404);
    }, taskPage);

    cleanup();
    clearSession();
    vi.unstubAllGlobals();

    await enterApp(async (input) => {
      const path = pathOf(input);
      const shell = shellHandlers(path);
      if (shell) {
        return shell;
      }
      if (path.endsWith(`/workspaces/${workspace.workspaceId}`)) {
        return json(workspace);
      }
      if (path.includes(otherProjectId) && path.endsWith("/tasks")) {
        return json([]);
      }
      if (path.includes(otherProjectId)) {
        return json({ ...project, projectId: otherProjectId, name: "Other" });
      }
      return json({ error: "missing" }, 404);
    }, `/app/tenants/${tenantA.tenantId}/workspaces/${workspace.workspaceId}/projects/${otherProjectId}`);

    expect(await screen.findByText(enTasks.emptyTitle)).toBeTruthy();
    expect(screen.queryByText("Kickoff")).toBeNull();
    resolveFirst?.(json([firstTask]));
    await waitFor(() => {
      expect(screen.queryByText("Kickoff")).toBeNull();
    });
  });

  it("resolves task strings in English, Arabic, and Kurdish and keeps RTL", () => {
    expect(enTasks.priority.normal).toBeTruthy();
    expect(enTasks.priority.urgent).toBeTruthy();
    expect(enTasks.deadline.remove).toBeTruthy();
    expect(enTasks.deadline.overdue).toBeTruthy();
    expect(arTasks.priority.urgent).toBeTruthy();
    expect(arTasks.deadline.overdue).toBeTruthy();
    expect(kuTasks.priority.normal).toBeTruthy();
    expect(kuTasks.deadline.none).toBeTruthy();
    expect(enTasks.assignee).toBeTruthy();
    expect(enTasks.unassigned).toBeTruthy();
    expect(enTasks.tags).toBeTruthy();
    expect(enTasks.status.open).toBeTruthy();
    expect(enTasks.status.closed).toBeTruthy();
    expect(enTasks.viewChanges).toBeTruthy();
    expect(enTasks.deleteConfirm).toBeTruthy();
    expect(arTasks.assignee).toBeTruthy();
    expect(arTasks.status.resolved).toBeTruthy();
    expect(kuTasks.tags).toBeTruthy();
    expect(kuTasks.activity).toBeTruthy();
    expect(getDirectionForLocale("en")).toBe("ltr");
    expect(getDirectionForLocale("ar")).toBe("rtl");
    expect(getDirectionForLocale("ku")).toBe("rtl");
  });
});
