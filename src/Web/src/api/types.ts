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
  workspaceCount?: number;
  canManage?: boolean;
};

export type AccountCapabilities = {
  canCreateOrganization: boolean;
  organizationLimit: number;
  currentOrganizationCount: number;
  activeMembershipCount: number;
  pendingInvitationCount: number;
};

export type AccountProfile = {
  email: string;
  displayName: string;
  hasLocalCredential: boolean;
};

export type AuthProviders = {
  google: {
    available: boolean;
  };
};

export type Workspace = {
  workspaceId: string;
  tenantId: string;
  name: string;
  description?: string | null;
  startDate?: string | null;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
  accessLevel: WorkspaceAccessLevel;
  canManage?: boolean;
};

export type WorkspaceAccessLevel = "View" | "Edit";

export type Project = {
  projectId: string;
  tenantId: string;
  workspaceId: string;
  name: string;
  description?: string | null;
  accentToken?: string | null;
  openTaskCount: number;
  createdAtUtc: string;
};

export type TenantMember = {
  membershipId: string;
  userId: string;
  displayName: string;
  email: string;
  role: "Owner" | "Admin" | "Member";
  status: "Invited" | "Active" | "Suspended" | "Removed";
  joinedAtUtc: string;
  avatarUrl?: string | null;
  hasImplicitWorkspaceAccess: boolean;
  workspaceAccessCount: number | null;
  activeTaskCount: number;
  completedTaskCount: number;
  totalAssignedTaskCount: number;
  completionRate: number | null;
};

export type PendingInvitation = {
  invitationId: string;
  invitedEmail: string;
  role: "Owner" | "Admin" | "Member";
  expiresAtUtc: string;
  createdAtUtc: string;
  membershipId: string | null;
};

export type InvitationPreview = {
  organizationName: string;
  invitedEmail: string;
  role: string;
  expiresAtUtc: string;
  inviterDisplayName: string | null;
  requiresRegistration: boolean;
  workspaceGrants: { workspaceName: string; accessLevel: string }[];
};

export type InvitationCreated = {
  invitationId: string;
  invitedEmail: string;
  expiresAtUtc: string;
  invitationUrl: string | null;
  emailDeliveryDeferred: boolean;
};

export type WorkspaceAccess = {
  membershipId: string;
  workspaceId: string;
  accessLevel: WorkspaceAccessLevel;
};

export type WorkspaceMemberAccess = {
  membershipId: string;
  userId: string;
  displayName: string;
  email: string;
  role: "Owner" | "Admin" | "Member";
  status: "Invited" | "Active" | "Suspended" | "Removed";
  hasImplicitWorkspaceAccess: boolean;
  effectiveAccess: "Full" | "View" | "Edit" | "None";
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
  deleteBlockedReason?: string | null;
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
  reference?: string | null;
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
  taskTitle?: string | null;
  projectName?: string | null;
  isRead: boolean;
  createdAtUtc: string;
};

export type GlobalNotification = {
  notificationId: string;
  tenantId: string;
  tenantName: string;
  type: string;
  taskId: string | null;
  workspaceId: string | null;
  projectId: string | null;
  taskTitle: string | null;
  projectName: string | null;
  isRead: boolean;
  targetAvailable: boolean;
  createdAtUtc: string;
};

export type GlobalNotificationInbox = {
  items: GlobalNotification[];
  unreadCount: number;
};

export type ApiErrorBody = {
  error?: string;
  existingName?: string;
  existingKey?: string;
};
