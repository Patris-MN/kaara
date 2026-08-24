import { apiRequest } from "./http";
import type { AuthUser, LoginResponse, Project, TenantMembership, Workspace } from "./types";

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
