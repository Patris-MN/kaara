import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import "../i18n";
import App from "../App";
import { AuthProvider } from "../auth/AuthProvider";
import enAuth from "../locales/en/auth.json";
import enCommon from "../locales/en/common.json";
import enNotifications from "../locales/en/notifications.json";
import enTasks from "../locales/en/tasks.json";
import enTenants from "../locales/en/tenants.json";
import { TenantDirectoryProvider } from "../tenancy/TenantDirectoryProvider";
import { clearSession, writeAccessToken } from "./session";
import type { WorkNotification, WorkTask } from "./types";

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

const memberMohammad = {
  membershipId: "12121212-1212-1212-1212-121212121212",
  displayName: "Mohammad",
  email: "mohammad@example.test",
};

const backendTag = { tagId: "13131313-1313-1313-1313-131313131313", name: "Backend" };
const reviewTag = { tagId: "14141414-1414-1414-1414-141414141414", name: "Review" };

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
  assigneeMembershipId: memberMohammad.membershipId,
  assigneeDisplayName: memberMohammad.displayName,
  assigneeEmail: memberMohammad.email,
  tags: [backendTag],
  createdByMembershipId: "16161616-1616-1616-1616-161616161616",
  createdByDisplayName: "User A",
  createdByEmail: authUser.email,
  unseenActivityCount: 0,
  capabilities: null,
};

const assignmentNotification: WorkNotification = {
  notificationId: "15151515-1515-1515-1515-151515151515",
  type: "TaskAssigned",
  taskId: firstTask.taskId,
  workspaceId: workspace.workspaceId,
  projectId: project.projectId,
  isRead: false,
  createdAtUtc: "2026-08-29T00:00:00Z",
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
  path = taskPage,
) {
  writeAccessToken("token-a");
  vi.stubGlobal("fetch", vi.fn(fetchImpl));
  renderApp(path);
  expect(await screen.findByLabelText(enTenants.selector)).toBeTruthy();
}

function shellHandlers(path: string, notifications: WorkNotification[] = []) {
  if (path.endsWith("/auth/me")) {
    return json(authUser);
  }
  if (path.endsWith("/tenants")) {
    return json([{ ...tenantA, role: "Owner" }]);
  }
  if (path.endsWith("/invitations")) {
    return json([]);
  }
  if (path.endsWith("/notifications")) {
    return json(notifications);
  }
  return null;
}

describe("phase 6 assignment tags and notifications UI", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    clearSession();
    cleanup();
  });

  it("lists only assignable members, sends assignment, and reloads the assignee", async () => {
    const user = userEvent.setup();
    let lastCreate: { assigneeMembershipId?: string | null; tagIds?: string[]; newTags?: string[] } | undefined;
    const tasks: WorkTask[] = [];

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
      if (path === taskBase) {
        return json(project);
      }
      if (path.endsWith("/assignable-members")) {
        return json([memberMohammad]);
      }
      if (path.endsWith(`/${workspace.workspaceId}/tags`)) {
        return json([backendTag, reviewTag]);
      }
      if (path === `${taskBase}/tasks` && method === "GET") {
        return json(tasks);
      }
      if (path === `${taskBase}/tasks` && method === "POST") {
        lastCreate = JSON.parse(String(init?.body));
        const created: WorkTask = {
          ...firstTask,
          taskId: "ffffffff-ffff-ffff-ffff-ffffffffffff",
          title: "Assigned task",
          assigneeMembershipId: lastCreate?.assigneeMembershipId ?? null,
          assigneeDisplayName: memberMohammad.displayName,
          assigneeEmail: memberMohammad.email,
          tags: [backendTag],
        };
        tasks.push(created);
        return json(created, 201);
      }
      return json({ error: "missing" }, 404);
    });

    await user.click(await screen.findByRole("button", { name: enTasks.create }));
    const assignee = screen.getByLabelText(enTasks.assignTo);
    expect(assignee).toHaveProperty("value", "");
    expect(screen.getByRole("option", { name: enTasks.unassigned })).toBeTruthy();
    expect(screen.getByRole("option", { name: /Mohammad/ })).toBeTruthy();
    expect(screen.queryByRole("option", { name: /Invited/ })).toBeNull();
    expect(screen.queryByRole("option", { name: /Suspended/ })).toBeNull();

    await user.type(screen.getByLabelText(enTasks.fields.title), "Assigned task");
    await user.selectOptions(assignee, memberMohammad.membershipId);
    expect(assignee).toHaveProperty("value", memberMohammad.membershipId);
    await user.click(screen.getByRole("button", { name: "Backend" }));
    await user.click(screen.getByRole("button", { name: enTasks.create }));

    expect(await screen.findByText("Assigned task")).toBeTruthy();
    expect(screen.getByText("Mohammad")).toBeTruthy();
    expect(lastCreate).toMatchObject({
      assigneeMembershipId: memberMohammad.membershipId,
      tagIds: [backendTag.tagId],
    });
  });

  it("can unassign, create a tag, remove a selected tag, and keep View read-only", async () => {
    const user = userEvent.setup();
    let lastUpdate: { assigneeMembershipId?: string | null; tagIds?: string[]; newTags?: string[] } | undefined;
    const tasks = [firstTask];

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
      if (path === taskBase) {
        return json(project);
      }
      if (path.endsWith("/assignable-members")) {
        return json([memberMohammad]);
      }
      if (path.endsWith(`/${workspace.workspaceId}/tags`)) {
        return json([backendTag, reviewTag]);
      }
      if (path === `${taskBase}/tasks` && method === "GET") {
        return json(tasks);
      }
      if (path.endsWith("/comments") || path.endsWith("/activity")) {
        return json([]);
      }
      if (path.endsWith(`/tasks/${firstTask.taskId}`) && method === "GET") {
        return json(tasks[0] ?? firstTask);
      }
      if (path.endsWith(`/tasks/${firstTask.taskId}`) && method === "PUT") {
        lastUpdate = JSON.parse(String(init?.body));
        const updated: WorkTask = {
          ...firstTask,
          assigneeMembershipId: lastUpdate?.assigneeMembershipId ?? null,
          assigneeDisplayName: lastUpdate?.assigneeMembershipId ? memberMohammad.displayName : null,
          assigneeEmail: lastUpdate?.assigneeMembershipId ? memberMohammad.email : null,
          tags: (lastUpdate?.tagIds ?? [])
            .map((id) => [backendTag, reviewTag].find((tag) => tag.tagId === id))
            .filter((tag): tag is typeof backendTag => tag != null),
        };
        tasks[0] = updated;
        return json(updated);
      }
      return json({ error: "missing" }, 404);
    });

    expect(await screen.findByText("Mohammad")).toBeTruthy();
    expect(screen.getAllByText("Backend").length).toBeGreaterThan(0);
    await user.click(screen.getByRole("button", { name: /Kickoff/ }));
    expect(await screen.findByRole("dialog")).toBeTruthy();

    const editAssignee = screen.getByLabelText(enTasks.assignee);
    expect(editAssignee).toHaveProperty("value", memberMohammad.membershipId);
    await user.selectOptions(editAssignee, "");
    expect(screen.getByText(enTasks.reassignConfirm.replace("{{name}}", enTasks.unassigned))).toBeTruthy();
    await user.click(screen.getByRole("button", { name: enTasks.reassign }));
    await user.click(screen.getByRole("button", { name: enTasks.removeTag }));
    const reviewButtons = screen.getAllByRole("button", { name: "Review" });
    await user.click(reviewButtons[reviewButtons.length - 1]!);
    await user.type(document.getElementById("edit-task-new-tag") as HTMLInputElement, "Customer issue");
    await user.click(screen.getByRole("button", { name: enTasks.save }));

    expect(lastUpdate).toMatchObject({
      assigneeMembershipId: null,
      tagIds: [reviewTag.tagId],
      newTags: ["Customer issue"],
    });
    expect((await screen.findAllByText(enTasks.unassigned)).length).toBeGreaterThan(0);

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
      if (path.endsWith("/invitations") || path.endsWith("/notifications") || path.endsWith("/assignable-members")) {
        return json([]);
      }
      if (path.endsWith("/tags")) {
        return json([backendTag]);
      }
      if (path.endsWith(`/workspaces/${workspace.workspaceId}`)) {
        return json({ ...workspace, accessLevel: "View" });
      }
      if (path === taskBase) {
        return json(project);
      }
      if (path.endsWith("/comments") || path.endsWith("/activity")) {
        return json([]);
      }
      if (path.endsWith(`/tasks/${firstTask.taskId}`)) {
        return json(firstTask);
      }
      if (path.endsWith("/tasks")) {
        return json([firstTask]);
      }
      return json({ error: "missing" }, 404);
    });

    expect(await screen.findByText("Kickoff")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: /Kickoff/ }));
    expect(await screen.findByRole("dialog")).toBeTruthy();
    expect((screen.getByLabelText(enTasks.assignee) as HTMLSelectElement).disabled).toBe(true);
    expect(screen.queryByPlaceholderText(enTasks.addTag)).toBeNull();
    expect(screen.queryByRole("button", { name: enTasks.removeTag })).toBeNull();
  });

  it("lists assignment notifications, marks them read, and navigates", async () => {
    const user = userEvent.setup();
    let markedRead = false;
    const notifications = [assignmentNotification];

    await enterApp(async (input, init) => {
      const path = pathOf(input);
      const method = (init?.method ?? "GET").toUpperCase();
      const shell = shellHandlers(path, markedRead ? [{ ...assignmentNotification, isRead: true }] : notifications);
      if (shell) {
        return shell;
      }
      if (path.endsWith(`/workspaces/${workspace.workspaceId}`)) {
        return json(workspace);
      }
      if (path.endsWith("/comments") || path.endsWith("/activity")) {
        return json([]);
      }
      if (path.endsWith(`/tasks/${firstTask.taskId}`)) {
        return json(firstTask);
      }
      if (path === taskBase || path.includes("/projects/")) {
        return path.endsWith("/tasks") ? json([firstTask]) : json(project);
      }
      if (path.endsWith("/assignable-members") || path.endsWith("/tags")) {
        return json([]);
      }
      if (path.endsWith(`/notifications/${assignmentNotification.notificationId}/read`) && method === "POST") {
        markedRead = true;
        return new Response(null, { status: 204 });
      }
      return json({ error: "missing" }, 404);
    });

    expect(await screen.findByText("Kickoff")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: enNotifications.title }));
    expect(screen.getByText(enNotifications.taskAssigned)).toBeTruthy();
    expect(screen.getByText(enNotifications.markRead)).toBeTruthy();
    await user.click(screen.getByText(enNotifications.taskAssigned));
    await waitFor(() => {
      expect(markedRead).toBe(true);
    });
  });

  it("logs out when notification listing returns 401 and stays signed in on 403", async () => {
    await enterApp(async (input) => {
      const path = pathOf(input);
      if (path.endsWith("/auth/me")) {
        return json(authUser);
      }
      if (path.endsWith("/tenants")) {
        return json([{ ...tenantA, role: "Owner" }]);
      }
      if (path.endsWith("/notifications")) {
        return json({ error: "unauthenticated" }, 401);
      }
      if (path.endsWith("/invitations") || path.endsWith("/assignable-members") || path.endsWith("/tags")) {
        return json([]);
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
    });

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
        return json([{ ...tenantA, role: "Owner" }]);
      }
      if (path.endsWith("/notifications")) {
        return json({ error: "forbidden" }, 403);
      }
      if (path.endsWith("/invitations") || path.endsWith("/assignable-members") || path.endsWith("/tags")) {
        return json([]);
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
    });

    expect(await screen.findByLabelText(enTenants.selector)).toBeTruthy();
    expect(screen.queryByRole("heading", { name: enAuth.signIn })).toBeNull();
    expect(screen.queryByText(enCommon.errors.forbidden)).toBeNull();
  });
});
