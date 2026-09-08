import { describe, expect, it, vi } from "vitest";

import type { PendingInvitation, TenantMember } from "../api/types";
import {
  assignableMemberRoles,
  buildMemberOverflowItems,
  canEditMemberAccess,
} from "./memberActions";

const activeMember: TenantMember = {
  membershipId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  userId: "cccccccc-cccc-cccc-cccc-cccccccccccc",
  email: "sara@example.test",
  displayName: "Sara Member",
  role: "Member",
  status: "Active",
  joinedAtUtc: "2026-08-29T12:00:00Z",
  avatarUrl: null,
  hasImplicitWorkspaceAccess: false,
  workspaceAccessCount: 2,
  activeTaskCount: 1,
  completedTaskCount: 2,
  totalAssignedTaskCount: 3,
  completionRate: 66.7,
};

const adminMember: TenantMember = {
  ...activeMember,
  membershipId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
  role: "Admin",
  hasImplicitWorkspaceAccess: true,
  workspaceAccessCount: null,
};

const ownerMember: TenantMember = {
  ...activeMember,
  membershipId: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
  role: "Owner",
  hasImplicitWorkspaceAccess: true,
};

const suspendedMember: TenantMember = {
  ...activeMember,
  status: "Suspended",
};

const invitedMember: TenantMember = {
  ...activeMember,
  status: "Invited",
};

const invitation: PendingInvitation = {
  invitationId: "ffffffff-ffff-ffff-ffff-ffffffffffff",
  invitedEmail: "sara@example.test",
  role: "Member",
  expiresAtUtc: "2026-09-08T00:00:00Z",
  createdAtUtc: "2026-09-01T00:00:00Z",
  membershipId: activeMember.membershipId,
};

const t = (key: string) => key;

const callbacks = {
  onOpenChangeRole: vi.fn(),
  onSuspend: vi.fn(),
  onReactivate: vi.fn(),
  onRemove: vi.fn(),
  onResend: vi.fn(),
  onRevoke: vi.fn(),
};

describe("memberActions", () => {
  it("allows manage access only for active members with Member role", () => {
    expect(canEditMemberAccess(activeMember, true)).toBe(true);
    expect(canEditMemberAccess(adminMember, true)).toBe(false);
    expect(canEditMemberAccess(suspendedMember, true)).toBe(false);
    expect(canEditMemberAccess(activeMember, false)).toBe(false);
  });

  it("builds active member overflow without manage access", () => {
    const items = buildMemberOverflowItems({
      member: activeMember,
      canManage: true,
      ...callbacks,
      t,
    });
    const labels = items.map((item) => item.label);
    expect(labels).toContain("members:actions.changeRole");
    expect(labels).toContain("members:actions.suspend");
    expect(labels).toContain("members:actions.remove");
    expect(labels).not.toContain("members:actions.manageAccess");
    expect(labels).not.toContain("members:actions.resend");
    expect(labels).not.toContain("members:actions.revoke");
  });

  it("builds invited member overflow with invitation actions only", () => {
    const items = buildMemberOverflowItems({
      member: invitedMember,
      invitation,
      canManage: true,
      ...callbacks,
      t,
    });
    const labels = items.map((item) => item.label);
    expect(labels).toEqual(["members:actions.resend", "members:actions.revoke"]);
  });

  it("builds suspended member overflow with reactivate and remove", () => {
    const items = buildMemberOverflowItems({
      member: suspendedMember,
      canManage: true,
      ...callbacks,
      t,
    });
    const labels = items.map((item) => item.label);
    expect(labels).toContain("members:actions.reactivate");
    expect(labels).not.toContain("members:actions.suspend");
    expect(labels).not.toContain("members:actions.changeRole");
  });

  it("protects owner overflow actions", () => {
    const items = buildMemberOverflowItems({
      member: ownerMember,
      canManage: true,
      ...callbacks,
      t,
    });
    expect(items).toEqual([]);
  });

  it("returns assignable roles for non-owner members", () => {
    expect(assignableMemberRoles(activeMember)).toEqual(["Admin", "Member"]);
    expect(assignableMemberRoles(ownerMember)).toEqual([]);
  });
});
