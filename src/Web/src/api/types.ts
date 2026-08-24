export type AuthUser = {
  userId: string;
  email: string;
  displayName: string;
  isPlatformAdministrator: boolean;
};

export type LoginResponse = AuthUser & {
  accessToken: string;
  expiresAtUtc: string;
};

export type TenantMembership = {
  tenantId: string;
  name: string;
  slug: string;
  role: string;
  status: string;
};

export type Workspace = {
  workspaceId: string;
  tenantId: string;
  name: string;
};

export type Project = {
  projectId: string;
  tenantId: string;
  workspaceId: string;
  name: string;
};

export type ApiErrorBody = {
  error?: string;
};
