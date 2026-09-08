import { cleanup, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import i18n from "../i18n";
import App from "../App";
import { AuthProvider } from "../auth/AuthProvider";
import enCommon from "../locales/en/common.json";
import arTasks from "../locales/ar/tasks.json";
import enTasks from "../locales/en/tasks.json";
import kuTasks from "../locales/ku/tasks.json";
import { TenantDirectoryProvider } from "../tenancy/TenantDirectoryProvider";
import { deriveAccountCapabilities } from "../test/directoryFetchHandlers";
import { clearSession, writeAccessToken } from "./session";
import type { TaskCapabilities, WorkTask, WorkTaskComment } from "./types";

const authUser = {
  userId: "11111111-1111-1111-1111-111111111111",
  email: "viewer@example.test",
  displayName: "Viewer",
  isPlatformAdministrator: false,
};

const tenantA = {
  tenantId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  name: "Org A",
  slug: "org-a",
  role: "Member" as const,
  status: "Active" as const,
  workspaceCount: 1,
};

const memberMembershipId = "12121212-1212-1212-1212-121212121212";

const workspaceView = {
  workspaceId: "cccccccc-cccc-cccc-cccc-cccccccccccc",
  tenantId: tenantA.tenantId,
  name: "Leopard",
  accessLevel: "View" as const,
};

const workspaceEdit = { ...workspaceView, accessLevel: "Edit" as const };

const project = {
  projectId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
  tenantId: tenantA.tenantId,
  workspaceId: workspaceView.workspaceId,
  name: "Spots",
};

const permissiveCaps: TaskCapabilities = {
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

const readOnlyCaps: TaskCapabilities = {
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
    workspaceId: workspaceView.workspaceId,
    projectId: project.projectId,
    title: "Kickoff",
    description: "Prepare notes",
    status: "Open",
    priority: "Normal",
    dueDate: "2026-09-01",
    createdAtUtc: "2026-08-01T00:00:00Z",
    updatedAtUtc: "2026-08-01T00:00:00Z",
    assigneeMembershipId: memberMembershipId,
    assigneeDisplayName: "Viewer",
    assigneeEmail: authUser.email,
    tags: [{ tagId: "13131313-1313-1313-1313-131313131313", name: "Backend" }],
    createdByMembershipId: memberMembershipId,
    createdByDisplayName: "Viewer",
    createdByEmail: authUser.email,
    unseenActivityCount: 0,
    capabilities: permissiveCaps,
    ...overrides,
  };
}

const sampleComment: WorkTaskComment = {
  commentId: "17171717-1717-1717-1717-171717171717",
  authorMembershipId: memberMembershipId,
  authorDisplayName: "Viewer",
  body: "Existing note",
  createdAtUtc: "2026-08-29T08:04:00Z",
  updatedAtUtc: null,
  isOwn: true,
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

const taskBase = `/tenants/${tenantA.tenantId}/workspaces/${workspaceView.workspaceId}/projects/${project.projectId}`;
const taskPage = `/app/tenants/${tenantA.tenantId}/workspaces/${workspaceView.workspaceId}/projects/${project.projectId}`;

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

function taskHandlers(
  route: string,
  method: string,
  workspace: typeof workspaceView | typeof workspaceEdit,
  task: WorkTask,
  comments: WorkTaskComment[],
) {
  if (route.endsWith("/auth/me")) {
    return json(authUser);
  }
  if (route === "/tenants") {
    return json([tenantA]);
  }
  if (route === "/account/capabilities") {
    return json(deriveAccountCapabilities([{ role: tenantA.role }]));
  }
  if (route.endsWith("/invitations") || route.endsWith("/assignable-members") || route.endsWith("/tags")) {
    return json([]);
  }
  if (route === "/notifications") {
    return json({ items: [], unreadCount: 0 });
  }
  if (route.endsWith(`/workspaces/${workspace.workspaceId}`) && !route.includes("/projects/")) {
    return json(workspace);
  }
  if (route.endsWith("/comments") && method === "GET") {
    return json(comments);
  }
  if (route.endsWith("/activity")) {
    return json([]);
  }
  if (route.endsWith(`/tasks/${task.taskId}`)) {
    return json(task);
  }
  if (route.endsWith("/tasks")) {
    return json([task]);
  }
  if (route === taskBase || route.includes("/projects/")) {
    return json(project);
  }
  return null;
}

async function enterTaskPage(options: {
  workspace?: typeof workspaceView | typeof workspaceEdit;
  task?: WorkTask;
  comments?: WorkTaskComment[];
  locale?: string;
}) {
  const workspace = options.workspace ?? workspaceView;
  const task = options.task ?? buildTask();
  const comments = options.comments ?? [sampleComment];
  if (options.locale) {
    await i18n.changeLanguage(options.locale);
  }
  writeAccessToken("token-view");
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const route = pathOf(input);
      const method = (init?.method ?? "GET").toUpperCase();
      const handled = taskHandlers(route, method, workspace, task, comments);
      return handled ?? json({ error: "missing" }, 404);
    }),
  );
  renderApp(taskPage);
  expect(await screen.findByText("Kickoff")).toBeTruthy();
  return task;
}

async function openTaskModal(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole("button", { name: /Kickoff/ }));
  return screen.findByRole("dialog");
}

describe("phase 8.1.3B task view-access regression", () => {
  afterEach(async () => {
    vi.unstubAllGlobals();
    clearSession();
    cleanup();
    await i18n.changeLanguage("en");
    document.documentElement.lang = "en";
    document.documentElement.dir = "ltr";
  });

  it("lets a View member open the task modal and see read-only content", async () => {
    const user = userEvent.setup();
    await enterTaskPage({ task: buildTask({ capabilities: readOnlyCaps }) });

    expect(screen.getByText(enTasks.viewOnly)).toBeTruthy();
    const dialog = await openTaskModal(user);
    expect(within(dialog).getByText("Prepare notes")).toBeTruthy();
    expect(within(dialog).getAllByText("Backend").length).toBeGreaterThan(0);
    expect(within(dialog).getByText("Existing note")).toBeTruthy();
  });

  it("hides every mutation control for View workspace access", async () => {
    const user = userEvent.setup();
    await enterTaskPage({ task: buildTask({ capabilities: permissiveCaps }) });
    const dialog = await openTaskModal(user);

    expect(within(dialog).queryByLabelText(enTasks.fields.title)).toBeNull();
    expect(within(dialog).queryByLabelText(enTasks.fields.status)).toBeNull();
    expect(within(dialog).queryByLabelText(enTasks.fields.priority)).toBeNull();
    expect(within(dialog).queryByLabelText(enTasks.assignee)).toBeNull();
    expect(within(dialog).queryByPlaceholderText(enTasks.addTag)).toBeNull();
    expect(within(dialog).queryByPlaceholderText(enTasks.commentPlaceholder)).toBeNull();
    expect(within(dialog).queryByRole("button", { name: enTasks.save })).toBeNull();
    expect(within(dialog).queryByRole("button", { name: enTasks.delete })).toBeNull();
    expect(within(dialog).queryByRole("button", { name: enTasks.editComment })).toBeNull();
    expect(within(dialog).queryByRole("button", { name: enTasks.addComment })).toBeNull();
  });

  it("keeps View-only task modal read-only for creator and current assignee", async () => {
    const user = userEvent.setup();
    await enterTaskPage({
      task: buildTask({
        createdByMembershipId: memberMembershipId,
        assigneeMembershipId: memberMembershipId,
        capabilities: permissiveCaps,
      }),
    });
    const dialog = await openTaskModal(user);
    expect(within(dialog).queryByRole("button", { name: enTasks.save })).toBeNull();
    expect(within(dialog).queryByLabelText(enTasks.fields.title)).toBeNull();
  });

  it("fails closed when server capabilities are missing under Edit access", async () => {
    const user = userEvent.setup();
    await enterTaskPage({
      workspace: workspaceEdit,
      task: buildTask({ capabilities: null }),
    });
    const dialog = await openTaskModal(user);
    expect(within(dialog).queryByRole("button", { name: enTasks.save })).toBeNull();
    expect(within(dialog).queryByLabelText(enTasks.fields.title)).toBeNull();
  });

  it("shows creator capabilities when workspace access is Edit", async () => {
    const user = userEvent.setup();
    await enterTaskPage({
      workspace: workspaceEdit,
      task: buildTask({ capabilities: permissiveCaps }),
    });
    const dialog = await openTaskModal(user);
    expect(within(dialog).getByLabelText(enTasks.fields.title)).toBeTruthy();
    expect(within(dialog).getByRole("button", { name: enTasks.save })).toBeTruthy();
    await user.click(within(dialog).getByRole("button", { name: enTasks.detail }));
    expect(within(dialog).getByRole("menuitem", { name: enTasks.delete })).toBeTruthy();
  });

  it("shows assignee collaboration controls without creator definition rights", async () => {
    const user = userEvent.setup();
    await enterTaskPage({
      workspace: workspaceEdit,
      task: buildTask({
        createdByMembershipId: "16161616-1616-1616-1616-161616161616",
        capabilities: assigneeCaps,
      }),
    });
    const dialog = await openTaskModal(user);
    expect((within(dialog).getByLabelText(enTasks.fields.title) as HTMLInputElement).disabled).toBe(true);
    expect(within(dialog).getByPlaceholderText(enTasks.commentPlaceholder)).toBeTruthy();
    expect(within(dialog).queryByRole("button", { name: enTasks.delete })).toBeNull();
  });

  it("removes mutation controls after workspace access changes from Edit to View on refresh", async () => {
    const user = userEvent.setup();
    await enterTaskPage({
      workspace: workspaceEdit,
      task: buildTask({ capabilities: permissiveCaps }),
    });
    const editDialog = await openTaskModal(user);
    expect(within(editDialog).getByRole("button", { name: enTasks.save })).toBeTruthy();
    await user.click(within(editDialog).getByRole("button", { name: enCommon.close }));

    cleanup();
    clearSession();
    vi.unstubAllGlobals();

    await enterTaskPage({
      workspace: workspaceView,
      task: buildTask({ capabilities: permissiveCaps }),
    });
    const viewDialog = await openTaskModal(user);
    expect(within(viewDialog).queryByRole("button", { name: enTasks.save })).toBeNull();
    expect(within(viewDialog).queryByLabelText(enTasks.fields.title)).toBeNull();
  });

  it.each([
    ["en", enTasks.viewOnly],
    ["ar", arTasks.viewOnly],
    ["ku", kuTasks.viewOnly],
  ])("shows localized view-only messaging for %s", async (locale, message) => {
    const user = userEvent.setup();
    await enterTaskPage({ task: buildTask({ capabilities: readOnlyCaps }), locale });
    expect(screen.getByText(message)).toBeTruthy();
    const dialog = await openTaskModal(user);
    expect(within(dialog).queryByLabelText(i18n.t("tasks:fields.title"))).toBeNull();
  });

  it("keeps the task modal usable at a narrow viewport width", async () => {
    const user = userEvent.setup();
    Object.defineProperty(window, "innerWidth", { configurable: true, value: 390, writable: true });
    await enterTaskPage({ task: buildTask({ capabilities: readOnlyCaps }) });
    const dialog = await openTaskModal(user);
    expect(dialog).toBeTruthy();
    expect(within(dialog).getAllByText("Kickoff").length).toBeGreaterThan(0);
  });
});
