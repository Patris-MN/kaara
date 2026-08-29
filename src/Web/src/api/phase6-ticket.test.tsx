import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import "../i18n";
import App from "../App";
import { AuthProvider } from "../auth/AuthProvider";
import enCommon from "../locales/en/common.json";
import enNotifications from "../locales/en/notifications.json";
import enTasks from "../locales/en/tasks.json";
import enTenants from "../locales/en/tenants.json";
import { TenantDirectoryProvider } from "../tenancy/TenantDirectoryProvider";
import { clearSession, writeAccessToken } from "./session";
import type { TaskCapabilities, WorkTask, WorkTaskActivity, WorkTaskComment } from "./types";

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

const creatorCaps: TaskCapabilities = {
  canEditDefinition: true,
  canManageTags: true,
  canReassign: true,
  canComment: true,
  canDelete: true,
  allowedStatuses: ["Open", "InProgress", "Waiting", "Resolved", "Closed"],
};

const assigneeCaps: TaskCapabilities = {
  canEditDefinition: false,
  canManageTags: true,
  canReassign: true,
  canComment: true,
  canDelete: false,
  allowedStatuses: ["Open", "InProgress", "Waiting", "Resolved"],
};

const previousCaps: TaskCapabilities = {
  canEditDefinition: false,
  canManageTags: false,
  canReassign: false,
  canComment: true,
  canDelete: false,
  allowedStatuses: ["Open"],
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
  assigneeMembershipId: "12121212-1212-1212-1212-121212121212",
  assigneeDisplayName: "Sara",
  assigneeEmail: "sara@example.test",
  tags: [{ tagId: "13131313-1313-1313-1313-131313131313", name: "Backend" }],
  createdByMembershipId: "16161616-1616-1616-1616-161616161616",
  createdByDisplayName: "Mohammad",
  createdByEmail: "mohammad@example.test",
  unseenActivityCount: 3,
  capabilities: creatorCaps,
};

const comment: WorkTaskComment = {
  commentId: "17171717-1717-1717-1717-171717171717",
  authorMembershipId: firstTask.assigneeMembershipId!,
  authorDisplayName: "Sara",
  body: "Started the work",
  createdAtUtc: "2026-08-29T08:04:00Z",
  updatedAtUtc: null,
  isOwn: true,
};

const activity: WorkTaskActivity = {
  activityId: "18181818-1818-1818-1818-181818181818",
  eventType: "PriorityChanged",
  actorMembershipId: firstTask.createdByMembershipId!,
  actorDisplayName: "Mohammad",
  oldValue: "Normal",
  newValue: "Urgent",
  createdAtUtc: "2026-08-29T11:04:00Z",
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

function shell(path: string) {
  if (path.endsWith("/auth/me")) {
    return json(authUser);
  }
  if (path.endsWith("/tenants")) {
    return json([{ ...tenantA, role: "Owner" }]);
  }
  if (path.endsWith("/invitations") || path.endsWith("/notifications") || path.endsWith("/assignable-members") || path.endsWith("/tags")) {
    return json([]);
  }
  return null;
}

describe("phase 6 ticket collaboration UI", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    clearSession();
    cleanup();
  });

  it("shows creator controls, activity, comments, and requires delete confirmation", async () => {
    const user = userEvent.setup();
    let deleted = false;
    let postedComment: string | undefined;
    await enterApp(async (input, init) => {
      const path = pathOf(input);
      const method = (init?.method ?? "GET").toUpperCase();
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
      if (path === `${taskBase}/tasks`) {
        return json(deleted ? [] : [firstTask]);
      }
      if (path.endsWith(`/tasks/${firstTask.taskId}`) && method === "GET") {
        return json(firstTask);
      }
      if (path.endsWith("/comments") && method === "GET") {
        return json([comment]);
      }
      if (path.endsWith("/comments") && method === "POST") {
        postedComment = JSON.parse(String(init?.body)).body as string;
        return json({ ...comment, commentId: "19191919-1919-1919-1919-191919191919", body: postedComment }, 201);
      }
      if (path.endsWith("/activity")) {
        return json([activity]);
      }
      if (path.endsWith(`/tasks/${firstTask.taskId}`) && method === "DELETE") {
        deleted = true;
        return new Response(null, { status: 204 });
      }
      return json({ error: "missing" }, 404);
    });

    expect(await screen.findByText("Kickoff")).toBeTruthy();
    expect(screen.getByText(enTasks.newChanges.replace("{{count}}", "3"))).toBeTruthy();
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(screen.queryByText(enTasks.originalDescription)).toBeNull();
    expect(screen.queryByLabelText(/sort/i)).toBeNull();
    await user.click(screen.getByRole("button", { name: /Kickoff/ }));
    expect(await screen.findByRole("dialog")).toBeTruthy();
    expect(document.querySelector(".dialog-body")).toBeTruthy();
    expect(await screen.findByText(enTasks.originalDescription)).toBeTruthy();
    expect((screen.getByLabelText(enTasks.fields.title, { selector: "#edit-task-title" }) as HTMLInputElement).disabled).toBe(false);
    expect(screen.getByRole("option", { name: enTasks.status.closed })).toBeTruthy();
    expect(await screen.findByText("Started the work")).toBeTruthy();
    expect(await screen.findByText(enTasks.activityEvent.PriorityChanged)).toBeTruthy();
    expect(screen.getByText("Normal → Urgent")).toBeTruthy();
    expect(screen.getByText("Mohammad")).toBeTruthy();
    await user.type(screen.getByPlaceholderText(enTasks.commentPlaceholder), "Looks good");
    await user.click(screen.getByRole("button", { name: enTasks.addComment }));
    expect(postedComment).toBe("Looks good");
    await user.click(screen.getByRole("button", { name: enTasks.delete }));
    expect(screen.getByText(enTasks.deleteConfirm)).toBeTruthy();
    const cancelButtons = screen.getAllByRole("button", { name: enTasks.cancelDelete });
    await user.click(cancelButtons[cancelButtons.length - 1]!);
    expect(screen.queryByText(enTasks.deleteConfirm)).toBeNull();
    await user.click(screen.getByRole("button", { name: enTasks.delete }));
    await user.click(screen.getByRole("button", { name: enTasks.delete }));
    expect(deleted).toBe(true);
  });

  it("restricts assignee fields and hides delete after reassignment", async () => {
    const user = userEvent.setup();
    await enterApp(async (input) => {
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
      if (path === `${taskBase}/tasks`) {
        return json([{ ...firstTask, capabilities: assigneeCaps, unseenActivityCount: 0 }]);
      }
      if (path.endsWith(`/tasks/${firstTask.taskId}`)) {
        return json({ ...firstTask, capabilities: assigneeCaps, unseenActivityCount: 0 });
      }
      if (path.endsWith("/comments") || path.endsWith("/activity")) {
        return json([]);
      }
      return json({ error: "missing" }, 404);
    });

    expect(await screen.findByText("Kickoff")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: /Kickoff/ }));
    expect(await screen.findByRole("dialog")).toBeTruthy();
    expect((screen.getByLabelText(enTasks.fields.title, { selector: "#edit-task-title" }) as HTMLInputElement).disabled).toBe(true);
    expect((screen.getByLabelText(enTasks.originalDescription) as HTMLTextAreaElement).disabled).toBe(true);
    expect((screen.getByLabelText(enTasks.priority.label, { selector: "#edit-task-priority" }) as HTMLSelectElement).disabled).toBe(true);
    expect((screen.getByLabelText(enTasks.assignee) as HTMLSelectElement).disabled).toBe(false);
    expect(screen.queryByRole("option", { name: enTasks.status.closed })).toBeNull();
    expect(screen.queryByRole("button", { name: enTasks.delete })).toBeNull();
    expect(screen.getByPlaceholderText(enTasks.commentPlaceholder)).toBeTruthy();

    cleanup();
    clearSession();
    vi.unstubAllGlobals();

    await enterApp(async (input) => {
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
      if (path === `${taskBase}/tasks`) {
        return json([{ ...firstTask, status: "Closed", capabilities: previousCaps, unseenActivityCount: 0 }]);
      }
      if (path.endsWith(`/tasks/${firstTask.taskId}`)) {
        return json({ ...firstTask, status: "Closed", capabilities: previousCaps, unseenActivityCount: 0 });
      }
      if (path.endsWith("/comments") || path.endsWith("/activity")) {
        return json([]);
      }
      return json({ error: "missing" }, 404);
    });

    expect(await screen.findByText("Kickoff")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: /Kickoff/ }));
    expect(await screen.findByRole("dialog")).toBeTruthy();
    expect(screen.getByText(enTasks.closedReadOnly)).toBeTruthy();
    expect((screen.getByLabelText(enTasks.assignee) as HTMLSelectElement).disabled).toBe(true);
    expect(document.getElementById("edit-task-new-tag")).toBeNull();
    expect(screen.queryByRole("button", { name: enTasks.delete })).toBeNull();
  });

  it("closes the task modal back to the compact list", async () => {
    const user = userEvent.setup();
    await enterApp(async (input) => {
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
      if (path === `${taskBase}/tasks`) {
        return json([{ ...firstTask, unseenActivityCount: 0 }]);
      }
      if (path.endsWith(`/tasks/${firstTask.taskId}`)) {
        return json({ ...firstTask, unseenActivityCount: 0 });
      }
      if (path.endsWith("/comments") || path.endsWith("/activity")) {
        return json([]);
      }
      return json({ error: "missing" }, 404);
    });

    expect(await screen.findByText("Kickoff")).toBeTruthy();
    expect(screen.queryByRole("dialog")).toBeNull();
    await user.click(screen.getByRole("button", { name: /Kickoff/ }));
    expect(await screen.findByRole("dialog")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: enCommon.close }));
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(screen.queryByText(enTasks.originalDescription)).toBeNull();
    expect(screen.getByText("Kickoff")).toBeTruthy();
  });

  it("opens a deep-linked task as a modal and does not keep a stale task after switching rows", async () => {
    const user = userEvent.setup();
    const reviewTask: WorkTask = {
      ...firstTask,
      taskId: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
      title: "Review",
      description: "Later notes",
      unseenActivityCount: 0,
      capabilities: previousCaps,
    };
    await enterApp(async (input) => {
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
      if (path === `${taskBase}/tasks`) {
        return json([firstTask, reviewTask]);
      }
      if (path.endsWith(`/tasks/${reviewTask.taskId}`)) {
        return json(reviewTask);
      }
      if (path.endsWith(`/tasks/${firstTask.taskId}`)) {
        return json(firstTask);
      }
      if (path.endsWith("/comments") || path.endsWith("/activity")) {
        return json([]);
      }
      return json({ error: "missing" }, 404);
    }, `${taskPage}/tasks/${firstTask.taskId}`);

    expect(await screen.findByRole("dialog")).toBeTruthy();
    expect((screen.getByLabelText(enTasks.fields.title, { selector: "#edit-task-title" }) as HTMLInputElement).value).toBe(
      "Kickoff",
    );
    await user.click(screen.getByRole("button", { name: enCommon.close }));
    expect(screen.queryByRole("dialog")).toBeNull();
    await user.click(screen.getByRole("button", { name: /Review/ }));
    expect(await screen.findByRole("dialog")).toBeTruthy();
    expect((screen.getByLabelText(enTasks.fields.title, { selector: "#edit-task-title" }) as HTMLInputElement).value).toBe(
      "Review",
    );
    expect((screen.getByLabelText(enTasks.fields.title, { selector: "#edit-task-title" }) as HTMLInputElement).disabled).toBe(
      true,
    );
  });

  it("lets the current assignee confirm a handoff and then lose assignee controls", async () => {
    const user = userEvent.setup();
    const ali = {
      membershipId: "22222222-2222-2222-2222-222222222222",
      displayName: "Ali",
      email: "ali@example.test",
    };
    let current: WorkTask = {
      ...firstTask,
      capabilities: assigneeCaps,
      unseenActivityCount: 0,
    };
    let lastAssignee: string | null | undefined;

    await enterApp(async (input, init) => {
      const path = pathOf(input);
      const method = (init?.method ?? "GET").toUpperCase();
      if (path.endsWith("/assignable-members")) {
        return json([
          {
            membershipId: firstTask.assigneeMembershipId,
            displayName: "Sara",
            email: firstTask.assigneeEmail,
          },
          ali,
        ]);
      }
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
      if (path === `${taskBase}/tasks`) {
        return json([current]);
      }
      if (path.endsWith("/comments") || path.endsWith("/activity")) {
        return json(
          path.endsWith("/activity") && lastAssignee
            ? [
                {
                  activityId: "19191919-1919-1919-1919-191919191919",
                  eventType: "AssigneeChanged",
                  actorMembershipId: firstTask.assigneeMembershipId,
                  actorDisplayName: "Sara",
                  oldValue: firstTask.assigneeMembershipId,
                  newValue: lastAssignee,
                  createdAtUtc: "2026-08-29T13:42:00Z",
                },
              ]
            : [],
        );
      }
      if (path.endsWith(`/tasks/${firstTask.taskId}`) && method === "PUT") {
        const body = JSON.parse(String(init?.body)) as { assigneeMembershipId?: string | null };
        lastAssignee = body.assigneeMembershipId;
        current = {
          ...current,
          assigneeMembershipId: body.assigneeMembershipId ?? null,
          assigneeDisplayName: body.assigneeMembershipId === ali.membershipId ? ali.displayName : null,
          assigneeEmail: body.assigneeMembershipId === ali.membershipId ? ali.email : null,
          capabilities: previousCaps,
        };
        return json(current);
      }
      if (path.endsWith(`/tasks/${firstTask.taskId}`)) {
        return json(current);
      }
      return json({ error: "missing" }, 404);
    });

    expect(await screen.findByText("Kickoff")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: /Kickoff/ }));
    expect(await screen.findByRole("dialog")).toBeTruthy();
    expect((screen.getByLabelText(enTasks.assignee) as HTMLSelectElement).disabled).toBe(false);
    await user.selectOptions(screen.getByLabelText(enTasks.assignee), ali.membershipId);
    expect(screen.getByText(enTasks.reassignConfirm.replace("{{name}}", "Ali"))).toBeTruthy();
    expect(screen.getByText(enTasks.reassignConfirmBody)).toBeTruthy();
    await user.click(screen.getByRole("button", { name: enTasks.reassign }));
    await waitFor(() => {
      expect(lastAssignee).toBe(ali.membershipId);
    });
    await user.click(screen.getByText(enTasks.showActivity));
    expect(await screen.findByText(enTasks.activityEvent.AssigneeChanged)).toBeTruthy();
    expect((screen.getByLabelText(enTasks.assignee) as HTMLSelectElement).disabled).toBe(true);
    expect(screen.queryByRole("button", { name: enTasks.delete })).toBeNull();
    expect(document.getElementById("edit-task-new-tag")).toBeNull();
  });

  it("refetches the task when a stale assignee handoff is rejected", async () => {
    const user = userEvent.setup();
    const ali = {
      membershipId: "22222222-2222-2222-2222-222222222222",
      displayName: "Ali",
      email: "ali@example.test",
    };
    const stale: WorkTask = { ...firstTask, capabilities: assigneeCaps, unseenActivityCount: 0 };
    const fresh: WorkTask = {
      ...firstTask,
      assigneeMembershipId: ali.membershipId,
      assigneeDisplayName: ali.displayName,
      assigneeEmail: ali.email,
      capabilities: previousCaps,
      unseenActivityCount: 0,
    };
    let current = stale;

    await enterApp(async (input, init) => {
      const path = pathOf(input);
      const method = (init?.method ?? "GET").toUpperCase();
      if (path.endsWith("/assignable-members")) {
        return json([
          {
            membershipId: firstTask.assigneeMembershipId,
            displayName: "Sara",
            email: firstTask.assigneeEmail,
          },
          ali,
        ]);
      }
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
      if (path === `${taskBase}/tasks`) {
        return json([current]);
      }
      if (path.endsWith("/comments") || path.endsWith("/activity")) {
        return json([]);
      }
      if (path.endsWith(`/tasks/${firstTask.taskId}`) && method === "PUT") {
        current = fresh;
        return json({ error: "task_field_forbidden" }, 403);
      }
      if (path.endsWith(`/tasks/${firstTask.taskId}`)) {
        return json(current);
      }
      return json({ error: "missing" }, 404);
    });

    expect(await screen.findByText("Kickoff")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: /Kickoff/ }));
    expect(await screen.findByRole("dialog")).toBeTruthy();
    await user.selectOptions(screen.getByLabelText(enTasks.assignee), ali.membershipId);
    await user.click(screen.getByRole("button", { name: enTasks.reassign }));
    expect(await screen.findByText(enCommon.errors.task_field_forbidden)).toBeTruthy();
    await waitFor(() => {
      expect((screen.getByLabelText(enTasks.assignee) as HTMLSelectElement).disabled).toBe(true);
    });
    expect((screen.getByLabelText(enTasks.assignee) as HTMLSelectElement).value).toBe(ali.membershipId);
  });

  it("localizes notification types without storing English-only copy", () => {
    expect(enNotifications.types.TaskReassigned).toBeTruthy();
    expect(enNotifications.types.TaskCommentAdded).toBeTruthy();
    expect(enNotifications.types.TaskClosed).toBeTruthy();
    expect(enTasks.activityEvent.TaskReopened).toBeTruthy();
  });
});
