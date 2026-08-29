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
  accessLevel: WorkspaceAccessLevel;
};

export type WorkspaceAccessLevel = "View" | "Edit";

export type Project = {
  projectId: string;
  tenantId: string;
  workspaceId: string;
  name: string;
};

export type TenantMember = {
  membershipId: string;
  userId: string;
  displayName: string;
  email: string;
  role: "Owner" | "Admin" | "Member";
  status: "Invited" | "Active" | "Suspended";
};

export type WorkspaceAccess = {
  membershipId: string;
  workspaceId: string;
  accessLevel: WorkspaceAccessLevel;
};

export type TaskStatus = "Open" | "InProgress" | "Waiting" | "Resolved" | "Closed";

export type TaskPriority = "Low" | "Normal" | "High" | "Urgent";

export type WorkTaskTag = {
  tagId: string;
  name: string;
};

export type TaskCapabilities = {
  canEditDefinition: boolean;
  canManageTags: boolean;
  canReassign: boolean;
  canComment: boolean;
  canDelete: boolean;
  allowedStatuses: TaskStatus[];
};

export type WorkTask = {
  taskId: string;
  tenantId: string;
  workspaceId: string;
  projectId: string;
  title: string;
  description: string | null;
  status: TaskStatus;
  priority: TaskPriority;
  dueDate: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  assigneeMembershipId: string | null;
  assigneeDisplayName: string | null;
  assigneeEmail: string | null;
  tags: WorkTaskTag[];
  createdByMembershipId: string | null;
  createdByDisplayName: string | null;
  createdByEmail: string | null;
  unseenActivityCount: number;
  capabilities: TaskCapabilities | null;
};

export type WorkTaskComment = {
  commentId: string;
  authorMembershipId: string;
  authorDisplayName: string | null;
  body: string;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  isOwn: boolean;
};

export type WorkTaskActivity = {
  activityId: string;
  eventType: string;
  actorMembershipId: string;
  actorDisplayName: string | null;
  oldValue: string | null;
  newValue: string | null;
  createdAtUtc: string;
};

export type AssignableMember = {
  membershipId: string;
  displayName: string;
  email: string;
};

export type WorkTag = {
  tagId: string;
  name: string;
};

export type WorkNotification = {
  notificationId: string;
  type: string;
  taskId: string | null;
  workspaceId: string | null;
  projectId: string | null;
  isRead: boolean;
  createdAtUtc: string;
};

export type ApiErrorBody = {
  error?: string;
};
