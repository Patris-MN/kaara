import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import "../i18n";
import i18n from "../i18n";
import App from "../App";
import { AuthProvider } from "../auth/AuthProvider";
import { getDirectionForLocale } from "../i18n/direction";
import arTasks from "../locales/ar/tasks.json";
import enTasks from "../locales/en/tasks.json";
import kuTasks from "../locales/ku/tasks.json";
import enTenants from "../locales/en/tenants.json";
import { TenantDirectoryProvider } from "../tenancy/TenantDirectoryProvider";
import { deriveAccountCapabilities } from "../test/directoryFetchHandlers";
import { clearSession, writeAccessToken } from "./session";
import type { TaskCapabilities, WorkTask } from "./types";

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
  description: "Track work",
};

const creatorCaps: TaskCapabilities = {
  canEditDefinition: true,
  canManageTags: true,
  canReassign: true,
  canComment: true,
  canDelete: true,
  allowedStatuses: ["Open", "InProgress", "Waiting", "Resolved", "Closed"],
};

const viewCaps: TaskCapabilities = {
  canEditDefinition: false,
  canManageTags: false,
  canReassign: false,
  canComment: false,
  canDelete: false,
  allowedStatuses: ["Open"],
};

function buildTask(overrides: Partial<WorkTask> = {}): WorkTask {
  return {
    taskId: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
    tenantId: tenantA.tenantId,
    workspaceId: workspace.workspaceId,
    projectId: project.projectId,
    title: "Kickoff",
    description: "Prepare notes",
    status: "Open",
    priority: "High",
    dueDate: "2026-09-12",
    createdAtUtc: "2026-09-07T00:00:00Z",
    updatedAtUtc: "2026-09-07T00:00:00Z",
    assigneeMembershipId: "16161616-1616-1616-1616-161616161616",
    assigneeDisplayName: "Mohammad",
    assigneeEmail: "m@example.test",
    tags: [{ tagId: "tag-1", name: "bug" }],
    createdByMembershipId: "16161616-1616-1616-1616-161616161616",
    createdByDisplayName: "Mohammad",
    createdByEmail: "m@example.test",
    unseenActivityCount: 0,
    capabilities: creatorCaps,
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

const taskBase = `/tenants/${tenantA.tenantId}/workspaces/${workspace.workspaceId}/projects/${project.projectId}`;
const taskPage = `/app/tenants/${tenantA.tenantId}/workspaces/${workspace.workspaceId}/projects/${project.projectId}`;

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

async function enterTasks(
  handler: (path: string, method: string, init?: RequestInit) => Response | Promise<Response>,
  path = taskPage,
) {
  writeAccessToken("token-a");
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const currentPath = pathOf(input);
      const method = (init?.method ?? "GET").toUpperCase();
      const handled = shell(currentPath);
      if (handled) {
        return handled;
      }
      return handler(currentPath, method, init);
    }),
  );
  renderApp(path);
  expect(await screen.findByLabelText(enTenants.selector)).toBeTruthy();
}

describe("phase 8.3 task list and discovery UX", () => {
  afterEach(() => {
    cleanup();
    clearSession();
    vi.unstubAllGlobals();
  });

  it("shows compact toolbar with search, filters, sort, and new task", async () => {
    await enterTasks((path, method) => {
      if (path.endsWith(`/workspaces/${workspace.workspaceId}`)) {
        return json(workspace);
      }
      if (path === taskBase) {
        return json(project);
      }
      if (path.endsWith("/tasks") && method === "GET") {
        return json([buildTask()]);
      }
      return json({ error: "missing" }, 404);
    });

    expect(await screen.findByText("Kickoff")).toBeTruthy();
    expect(screen.getByLabelText(enTasks.searchLabel)).toBeTruthy();
    expect(screen.getByRole("button", { name: enTasks.filterLabel })).toBeTruthy();
    expect(screen.getByLabelText(enTasks.sortLabel)).toBeTruthy();
    expect(screen.getAllByRole("button", { name: enTasks.newTask }).length).toBeGreaterThanOrEqual(1);
    expect(document.querySelector(".project-tasks-page")).toBeTruthy();
  });

  it("searches tasks and shows no matching state", async () => {
    const user = userEvent.setup();
    await enterTasks((path, method) => {
      if (path.endsWith(`/workspaces/${workspace.workspaceId}`)) {
        return json(workspace);
      }
      if (path === taskBase) {
        return json(project);
      }
      if (path.endsWith("/tasks") && method === "GET") {
        return json([buildTask(), buildTask({ taskId: "22222222-2222-2222-2222-222222222222", title: "Review" })]);
      }
      return json({ error: "missing" }, 404);
    });

    await user.type(await screen.findByLabelText(enTasks.searchLabel), "missing query");
    expect(await screen.findByText(enTasks.searchEmptyTitle)).toBeTruthy();
    await user.click(screen.getByRole("button", { name: enTasks.clearSearch }));
    expect(screen.getByText("Kickoff")).toBeTruthy();
  });

  it("opens whole task row and never shows raw fields.deadline", async () => {
    const user = userEvent.setup();
    await enterTasks((path, method) => {
      if (path.endsWith(`/workspaces/${workspace.workspaceId}`)) {
        return json(workspace);
      }
      if (path === taskBase) {
        return json(project);
      }
      if (path.endsWith("/tasks") && method === "GET") {
        return json([buildTask()]);
      }
      if (path.endsWith(`/tasks/${buildTask().taskId}`)) {
        return json(buildTask());
      }
      if (path.endsWith("/seen") && method === "POST") {
        return new Response(null, { status: 204 });
      }
      if (path.endsWith("/comments") || path.endsWith("/activity")) {
        return json([]);
      }
      return json({ error: "missing" }, 404);
    });

    await user.click(await screen.findByRole("button", { name: /Kickoff/ }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(enTasks.fields.deadline)).toBeTruthy();
    expect(screen.queryByText("fields.deadline")).toBeNull();
  });

  it("uses comments and activity disclosure controls", async () => {
    const user = userEvent.setup();
    await enterTasks((path, method) => {
      if (path.endsWith(`/workspaces/${workspace.workspaceId}`)) {
        return json(workspace);
      }
      if (path === taskBase) {
        return json(project);
      }
      if (path.endsWith("/tasks") && method === "GET") {
        return json([buildTask()]);
      }
      if (path.endsWith(`/tasks/${buildTask().taskId}`)) {
        return json(buildTask());
      }
      if (path.endsWith("/seen") && method === "POST") {
        return new Response(null, { status: 204 });
      }
      if (path.endsWith("/comments")) {
        return json([]);
      }
      if (path.endsWith("/activity")) {
        return json([]);
      }
      return json({ error: "missing" }, 404);
    });

    await user.click(await screen.findByRole("button", { name: /Kickoff/ }));
    const toggle = await screen.findByRole("button", { name: new RegExp(enTasks.comments) });
    expect(toggle.getAttribute("aria-expanded")).toBe("true");
    await user.click(toggle);
    expect(toggle.getAttribute("aria-expanded")).toBe("false");
    const activityToggle = screen.getByRole("button", { name: new RegExp(enTasks.activity) });
    expect(activityToggle.getAttribute("aria-expanded")).toBe("false");
    await user.click(activityToggle);
    expect(activityToggle.getAttribute("aria-expanded")).toBe("true");
  });

  it("disables save until draft changes and keeps modal open after save", async () => {
    const user = userEvent.setup();
    let saved = false;
    await enterTasks((path, method, _init) => {
      if (path.endsWith(`/workspaces/${workspace.workspaceId}`)) {
        return json(workspace);
      }
      if (path === taskBase) {
        return json(project);
      }
      if (path.endsWith("/tasks") && method === "GET") {
        return json([buildTask()]);
      }
      if (path.endsWith(`/tasks/${buildTask().taskId}`) && method === "GET") {
        return json(buildTask());
      }
      if (path.endsWith(`/tasks/${buildTask().taskId}`) && method === "PUT") {
        saved = true;
        return json({ ...buildTask(), title: "Updated title" });
      }
      if (path.endsWith("/seen") && method === "POST") {
        return new Response(null, { status: 204 });
      }
      if (path.endsWith("/comments") || path.endsWith("/activity")) {
        return json([]);
      }
      return json({ error: "missing" }, 404);
    });

    await user.click(await screen.findByRole("button", { name: /Kickoff/ }));
    const saveButton = await screen.findByRole("button", { name: enTasks.save });
    expect((saveButton as HTMLButtonElement).disabled).toBe(true);
    fireEvent.change(screen.getByLabelText(enTasks.fields.title, { selector: "#edit-task-title-visible" }), {
      target: { value: "Updated title" },
    });
    expect((saveButton as HTMLButtonElement).disabled).toBe(false);
    await user.click(saveButton);
    expect(saved).toBe(true);
    expect(screen.getByRole("dialog")).toBeTruthy();
  });

  it("shows delete through actions menu with confirmation for deletable tasks", async () => {
    const user = userEvent.setup();
    let deleted = false;
    await enterTasks((path, method) => {
      if (path.endsWith(`/workspaces/${workspace.workspaceId}`)) {
        return json(workspace);
      }
      if (path === taskBase) {
        return json(project);
      }
      if (path.endsWith("/tasks") && method === "GET") {
        return json([buildTask({ capabilities: { ...creatorCaps, canDelete: true } })]);
      }
      if (path.endsWith(`/tasks/${buildTask().taskId}`) && method === "GET") {
        return json(buildTask({ capabilities: { ...creatorCaps, canDelete: true } }));
      }
      if (path.endsWith(`/tasks/${buildTask().taskId}`) && method === "DELETE") {
        deleted = true;
        return new Response(null, { status: 204 });
      }
      if (path.endsWith("/seen") && method === "POST") {
        return new Response(null, { status: 204 });
      }
      if (path.endsWith("/comments") || path.endsWith("/activity")) {
        return json([]);
      }
      return json({ error: "missing" }, 404);
    });

    await user.click(await screen.findByRole("button", { name: /Kickoff/ }));
    await user.click(await screen.findByRole("button", { name: enTasks.detail }));
    await user.click(screen.getByRole("menuitem", { name: enTasks.delete }));
    expect(screen.getByText(enTasks.deleteConfirmTitle)).toBeTruthy();
    await user.click(screen.getAllByRole("button", { name: enTasks.delete }).at(-1)!);
    expect(deleted).toBe(true);
  });

  it("hides delete when server marks task as seen by another member", async () => {
    const user = userEvent.setup();
    await enterTasks((path, method) => {
      if (path.endsWith(`/workspaces/${workspace.workspaceId}`)) {
        return json(workspace);
      }
      if (path === taskBase) {
        return json(project);
      }
      if (path.endsWith("/tasks") && method === "GET") {
        return json([
          buildTask({
            capabilities: {
              ...creatorCaps,
              canDelete: false,
              deleteBlockedReason: "task_seen_by_another_member",
            },
          }),
        ]);
      }
      if (path.endsWith(`/tasks/${buildTask().taskId}`)) {
        return json(
          buildTask({
            capabilities: {
              ...creatorCaps,
              canDelete: false,
              deleteBlockedReason: "task_seen_by_another_member",
            },
          }),
        );
      }
      if (path.endsWith("/seen") && method === "POST") {
        return new Response(null, { status: 204 });
      }
      if (path.endsWith("/comments") || path.endsWith("/activity")) {
        return json([]);
      }
      return json({ error: "missing" }, 404);
    });

    await user.click(await screen.findByRole("button", { name: /Kickoff/ }));
    expect(screen.queryByRole("menuitem", { name: enTasks.delete })).toBeNull();
    expect(screen.getByText(enTasks.deleteBlockedTitle)).toBeTruthy();
    expect(screen.getByText(enTasks.deleteBlockedBody)).toBeTruthy();
  });

  it("renders view-only task details without mutation controls", async () => {
    const user = userEvent.setup();
    await enterTasks((path, method) => {
      if (path.endsWith(`/workspaces/${workspace.workspaceId}`)) {
        return json({ ...workspace, accessLevel: "View" });
      }
      if (path === taskBase) {
        return json(project);
      }
      if (path.endsWith("/tasks") && method === "GET") {
        return json([buildTask({ capabilities: viewCaps })]);
      }
      if (path.endsWith(`/tasks/${buildTask().taskId}`)) {
        return json(buildTask({ capabilities: viewCaps }));
      }
      if (path.endsWith("/seen") && method === "POST") {
        return new Response(null, { status: 204 });
      }
      if (path.endsWith("/comments") || path.endsWith("/activity")) {
        return json([]);
      }
      return json({ error: "missing" }, 404);
    });

    expect(screen.queryByRole("button", { name: enTasks.newTask })).toBeNull();
    await user.click(await screen.findByRole("button", { name: /Kickoff/ }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).queryByRole("button", { name: enTasks.save })).toBeNull();
    expect(within(dialog).queryByPlaceholderText(enTasks.commentPlaceholder)).toBeNull();
    expect(within(dialog).getByText(enTasks.descriptionLabel)).toBeTruthy();
  });

  it("opens compact create modal with optional description disclosure", async () => {
    const user = userEvent.setup();
    await enterTasks((path, method) => {
      if (path.endsWith(`/workspaces/${workspace.workspaceId}`)) {
        return json(workspace);
      }
      if (path === taskBase) {
        return json(project);
      }
      if (path.endsWith("/tasks") && method === "GET") {
        return json([]);
      }
      return json({ error: "missing" }, 404);
    });

    expect(await screen.findByText(enTasks.emptyTitle)).toBeTruthy();
    await user.click(await screen.findByRole("button", { name: enTasks.newTask }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByLabelText(enTasks.fields.title)).toBeTruthy();
    expect(within(dialog).queryByLabelText(enTasks.descriptionLabel)).toBeNull();
    await user.click(within(dialog).getByRole("button", { name: enTasks.fields.addDescription }));
    expect(within(dialog).getByLabelText(enTasks.descriptionLabel)).toBeTruthy();
  });

  it("localizes task strings in AR and KU without raw keys", async () => {
    await i18n.changeLanguage("ar");
    expect(arTasks.fields.deadline).toBeTruthy();
    expect(getDirectionForLocale("ar")).toBe("rtl");
    await i18n.changeLanguage("ku");
    expect(kuTasks.fields.deadline).toBeTruthy();
    expect(getDirectionForLocale("ku")).toBe("rtl");
    await i18n.changeLanguage("en");
  });
});
