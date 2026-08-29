import { apiRequest } from "./http";
import type {
  AssignableMember,
  AuthUser,
  LoginResponse,
  Project,
  TaskPriority,
  TaskStatus,
  TenantMember,
  TenantMembership,
  WorkNotification,
  WorkTag,
  WorkTask,
  WorkTaskActivity,
  WorkTaskComment,
  Workspace,
  WorkspaceAccess,
  WorkspaceAccessLevel,
} from "./types";

export function registerAccount(email: string, password: string, displayName: string) {
  return apiRequest<AuthUser>("/auth/register", {
    method: "POST",
    body: JSON.stringify({ email, password, displayName }),
    skipUnauthorizedHandler: true,
  });
}

export function login(email: string, password: string) {
  return apiRequest<LoginResponse>("/auth/login", {
    method: "POST",
    body: JSON.stringify({ email, password }),
    skipUnauthorizedHandler: true,
  });
}

export function loadCurrentUser(token: string) {
  return apiRequest<AuthUser>("/auth/me", { token, skipUnauthorizedHandler: true });
}

export function listTenants(token: string) {
  return apiRequest<TenantMembership[]>("/tenants", { token });
}

export function listInvitations(token: string) {
  return apiRequest<TenantMembership[]>("/invitations", { token });
}

export function createTenant(token: string, name: string, slug: string) {
  return apiRequest<TenantMembership | { tenantId: string; name: string; slug: string }>(
    "/tenants",
    { method: "POST", token, body: JSON.stringify({ name, slug }) },
  );
}

export function inviteMember(token: string, tenantId: string, email: string) {
  return apiRequest(`/tenants/${tenantId}/invitations`, {
    method: "POST",
    token,
    body: JSON.stringify({ email }),
  });
}

export function acceptInvitation(token: string, tenantId: string) {
  return apiRequest(`/tenants/${tenantId}/invitations/accept`, {
    method: "POST",
    token,
  });
}

export function listWorkspaces(token: string, tenantId: string, signal?: AbortSignal) {
  return apiRequest<Workspace[]>(`/tenants/${tenantId}/workspaces`, { token, signal });
}

export function getWorkspace(
  token: string,
  tenantId: string,
  workspaceId: string,
  signal?: AbortSignal,
) {
  return apiRequest<Workspace>(`/tenants/${tenantId}/workspaces/${workspaceId}`, {
    token,
    signal,
  });
}

export function createWorkspace(token: string, tenantId: string, name: string) {
  return apiRequest<Workspace>(`/tenants/${tenantId}/workspaces`, {
    method: "POST",
    token,
    body: JSON.stringify({ name }),
  });
}

export function listProjects(token: string, tenantId: string, workspaceId: string, signal?: AbortSignal) {
  return apiRequest<Project[]>(`/tenants/${tenantId}/workspaces/${workspaceId}/projects`, {
    token,
    signal,
  });
}

export function createProject(token: string, tenantId: string, workspaceId: string, name: string) {
  return apiRequest<Project>(`/tenants/${tenantId}/workspaces/${workspaceId}/projects`, {
    method: "POST",
    token,
    body: JSON.stringify({ name }),
  });
}

export function getProject(
  token: string,
  tenantId: string,
  workspaceId: string,
  projectId: string,
  signal?: AbortSignal,
) {
  return apiRequest<Project>(
    `/tenants/${tenantId}/workspaces/${workspaceId}/projects/${projectId}`,
    { token, signal },
  );
}

export function listTasks(
  token: string,
  tenantId: string,
  workspaceId: string,
  projectId: string,
  signal?: AbortSignal,
) {
  return apiRequest<WorkTask[]>(
    `/tenants/${tenantId}/workspaces/${workspaceId}/projects/${projectId}/tasks`,
    { token, signal },
  );
}

export function getTask(
  token: string,
  tenantId: string,
  workspaceId: string,
  projectId: string,
  taskId: string,
  signal?: AbortSignal,
) {
  return apiRequest<WorkTask>(
    `/tenants/${tenantId}/workspaces/${workspaceId}/projects/${projectId}/tasks/${taskId}`,
    { token, signal },
  );
}

export function createTask(
  token: string,
  tenantId: string,
  workspaceId: string,
  projectId: string,
  input: {
    title: string;
    description?: string;
    status?: TaskStatus;
    priority?: TaskPriority;
    dueDate?: string | null;
    assigneeMembershipId?: string | null;
    tagIds?: string[];
    newTags?: string[];
  },
) {
  return apiRequest<WorkTask>(
    `/tenants/${tenantId}/workspaces/${workspaceId}/projects/${projectId}/tasks`,
    { method: "POST", token, body: JSON.stringify(input) },
  );
}

export function updateTask(
  token: string,
  tenantId: string,
  workspaceId: string,
  projectId: string,
  taskId: string,
  input: {
    title: string;
    description?: string | null;
    status: TaskStatus;
    priority: TaskPriority;
    dueDate?: string | null;
    assigneeMembershipId?: string | null;
    tagIds?: string[];
    newTags?: string[];
  },
) {
  return apiRequest<WorkTask>(
    `/tenants/${tenantId}/workspaces/${workspaceId}/projects/${projectId}/tasks/${taskId}`,
    { method: "PUT", token, body: JSON.stringify(input) },
  );
}

export function deleteTask(
  token: string,
  tenantId: string,
  workspaceId: string,
  projectId: string,
  taskId: string,
) {
  return apiRequest<void>(
    `/tenants/${tenantId}/workspaces/${workspaceId}/projects/${projectId}/tasks/${taskId}`,
    { method: "DELETE", token },
  );
}

export function listTaskComments(
  token: string,
  tenantId: string,
  workspaceId: string,
  projectId: string,
  taskId: string,
) {
  return apiRequest<WorkTaskComment[]>(
    `/tenants/${tenantId}/workspaces/${workspaceId}/projects/${projectId}/tasks/${taskId}/comments`,
    { token },
  );
}

export function updateTaskComment(
  token: string,
  tenantId: string,
  workspaceId: string,
  projectId: string,
  taskId: string,
  commentId: string,
  body: string,
) {
  return apiRequest<WorkTaskComment>(
    `/tenants/${tenantId}/workspaces/${workspaceId}/projects/${projectId}/tasks/${taskId}/comments/${commentId}`,
    { method: "PUT", token, body: JSON.stringify({ body }) },
  );
}

export function deleteTaskComment(
  token: string,
  tenantId: string,
  workspaceId: string,
  projectId: string,
  taskId: string,
  commentId: string,
) {
  return apiRequest<void>(
    `/tenants/${tenantId}/workspaces/${workspaceId}/projects/${projectId}/tasks/${taskId}/comments/${commentId}`,
    { method: "DELETE", token },
  );
}

export function createTaskComment(
  token: string,
  tenantId: string,
  workspaceId: string,
  projectId: string,
  taskId: string,
  body: string,
) {
  return apiRequest<WorkTaskComment>(
    `/tenants/${tenantId}/workspaces/${workspaceId}/projects/${projectId}/tasks/${taskId}/comments`,
    { method: "POST", token, body: JSON.stringify({ body }) },
  );
}

export function listTaskActivity(
  token: string,
  tenantId: string,
  workspaceId: string,
  projectId: string,
  taskId: string,
) {
  return apiRequest<WorkTaskActivity[]>(
    `/tenants/${tenantId}/workspaces/${workspaceId}/projects/${projectId}/tasks/${taskId}/activity`,
    { token },
  );
}

export function markTaskSeen(
  token: string,
  tenantId: string,
  workspaceId: string,
  projectId: string,
  taskId: string,
) {
  return apiRequest<void>(
    `/tenants/${tenantId}/workspaces/${workspaceId}/projects/${projectId}/tasks/${taskId}/seen`,
    { method: "POST", token },
  );
}

export function listAssignableMembers(
  token: string,
  tenantId: string,
  workspaceId: string,
  signal?: AbortSignal,
) {
  return apiRequest<AssignableMember[]>(
    `/tenants/${tenantId}/workspaces/${workspaceId}/assignable-members`,
    { token, signal },
  );
}

export function listWorkspaceTags(
  token: string,
  tenantId: string,
  workspaceId: string,
  signal?: AbortSignal,
) {
  return apiRequest<WorkTag[]>(`/tenants/${tenantId}/workspaces/${workspaceId}/tags`, {
    token,
    signal,
  });
}

export function createWorkspaceTag(
  token: string,
  tenantId: string,
  workspaceId: string,
  name: string,
) {
  return apiRequest<WorkTag>(`/tenants/${tenantId}/workspaces/${workspaceId}/tags`, {
    method: "POST",
    token,
    body: JSON.stringify({ name }),
  });
}

export function listNotifications(token: string, tenantId: string, signal?: AbortSignal) {
  return apiRequest<WorkNotification[]>(`/tenants/${tenantId}/notifications`, { token, signal });
}

export function markNotificationRead(token: string, tenantId: string, notificationId: string) {
  return apiRequest<void>(`/tenants/${tenantId}/notifications/${notificationId}/read`, {
    method: "POST",
    token,
  });
}

export function listMembers(token: string, tenantId: string, signal?: AbortSignal) {
  return apiRequest<TenantMember[]>(`/tenants/${tenantId}/members`, { token, signal });
}

export function listWorkspaceAccess(
  token: string,
  tenantId: string,
  membershipId: string,
  signal?: AbortSignal,
) {
  return apiRequest<WorkspaceAccess[]>(
    `/tenants/${tenantId}/members/${membershipId}/workspace-access`,
    { token, signal },
  );
}

export function setWorkspaceAccess(
  token: string,
  tenantId: string,
  membershipId: string,
  workspaceId: string,
  accessLevel: WorkspaceAccessLevel,
) {
  return apiRequest<WorkspaceAccess>(
    `/tenants/${tenantId}/members/${membershipId}/workspace-access/${workspaceId}`,
    {
      method: "PUT",
      token,
      body: JSON.stringify({ accessLevel }),
    },
  );
}

export function removeWorkspaceAccess(
  token: string,
  tenantId: string,
  membershipId: string,
  workspaceId: string,
) {
  return apiRequest<void>(
    `/tenants/${tenantId}/members/${membershipId}/workspace-access/${workspaceId}`,
    { method: "DELETE", token },
  );
}
