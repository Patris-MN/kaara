import type { PendingInvitation, TenantMember } from "../api/types";

export type AssignableMemberRole = "Admin" | "Member";

export type MemberMenuItem = {
  id: string;
  label: string;
  onClick: () => void;
  destructive?: boolean;
  separatorBefore?: boolean;
};

export function canEditMemberAccess(member: TenantMember, canManage: boolean): boolean {
  return canManage && member.status === "Active" && member.role === "Member";
}

export function assignableMemberRoles(member: TenantMember): AssignableMemberRole[] {
  if (member.role === "Owner") {
    return [];
  }
  return ["Admin", "Member"];
}

export function buildMemberOverflowItems({
  member,
  invitation,
  canManage,
  onOpenChangeRole,
  onSuspend,
  onReactivate,
  onRemove,
  onResend,
  onRevoke,
  t,
}: {
  member: TenantMember;
  invitation?: PendingInvitation;
  canManage: boolean;
  onOpenChangeRole: () => void;
  onSuspend: () => void;
  onReactivate: () => void;
  onRemove: () => void;
  onResend: (invitation: PendingInvitation) => void;
  onRevoke: (invitation: PendingInvitation) => void;
  t: (key: string) => string;
}): MemberMenuItem[] {
  if (!canManage) {
    return [];
  }

  if (member.status === "Removed") {
    return [];
  }

  if (member.status === "Invited") {
    if (!invitation) {
      return [];
    }
    return [
      {
        id: "resend",
        label: t("members:actions.resend"),
        onClick: () => onResend(invitation),
      },
      {
        id: "revoke",
        label: t("members:actions.revoke"),
        onClick: () => onRevoke(invitation),
        destructive: true,
        separatorBefore: true,
      },
    ];
  }

  if (member.role === "Owner") {
    return [];
  }

  const items: MemberMenuItem[] = [];

  if (member.status === "Active") {
    items.push({
      id: "changeRole",
      label: t("members:actions.changeRole"),
      onClick: onOpenChangeRole,
    });
    items.push({
      id: "suspend",
      label: t("members:actions.suspend"),
      onClick: onSuspend,
    });
  } else if (member.status === "Suspended") {
    items.push({
      id: "reactivate",
      label: t("members:actions.reactivate"),
      onClick: onReactivate,
    });
  }

  if (member.status === "Active" || member.status === "Suspended") {
    items.push({
      id: "remove",
      label: t("members:actions.remove"),
      onClick: onRemove,
      destructive: true,
      separatorBefore: true,
    });
  }

  return items;
}

export function buildInvitationOverflowItems({
  canManage,
  onResend,
  onRevoke,
  t,
}: {
  canManage: boolean;
  onResend: () => void;
  onRevoke: () => void;
  t: (key: string) => string;
}): MemberMenuItem[] {
  if (!canManage) {
    return [];
  }
  return [
    {
      id: "resend",
      label: t("members:actions.resend"),
      onClick: onResend,
    },
    {
      id: "revoke",
      label: t("members:actions.revoke"),
      onClick: onRevoke,
      destructive: true,
      separatorBefore: true,
    },
  ];
}
