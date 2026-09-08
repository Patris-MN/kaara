import {
  type FormEvent,
  type ReactNode,
  type RefObject,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import { useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";

import {
  inviteMember,
  listMembers,
  listPendingInvitations,
  listWorkspaceAccess,
  listWorkspaces,
  reactivateMember,
  removeMember,
  resendInvitation,
  revokeInvitation,
  suspendMember,
  updateMemberRole,
} from "../api/client";
import { isApiError } from "../api/errors";
import { shouldApplyResponse } from "../api/requestIdentity";
import type {
  PendingInvitation,
  TenantMember,
  Workspace,
  WorkspaceAccess,
  WorkspaceAccessLevel,
} from "../api/types";
import { useAuth } from "../auth/AuthProvider";
import { Dialog } from "../components/Dialog";
import { ContextMenu } from "../components/ContextMenu";
import { EmptyState } from "../components/EmptyState";
import { PageHeader } from "../components/PageHeader";
import { ResourceToolbar } from "../components/ResourceToolbar";
import { Field, StatusBanner } from "../components/Ui";
import { UserAvatar } from "../components/UserAvatar";
import { WorkspaceAccessEditor } from "../components/WorkspaceAccessEditor";
import { useFeedback } from "../feedback/FeedbackProvider";
import {
  assignableMemberRoles,
  buildInvitationOverflowItems,
  buildMemberOverflowItems,
  canEditMemberAccess,
  type AssignableMemberRole,
} from "../members/memberActions";
import {
  formatMemberTaskSummary,
  memberAccessSummaryKey,
  readMemberView,
  writeMemberView,
  type MemberView,
} from "../members/memberResources";
import { sortMemberRows, type MemberSort } from "../members/memberListPresentation";
import {
  buildWorkspaceAccessDraft,
  saveWorkspaceAccessChanges,
  type WorkspaceAccessDraftRow,
} from "../members/workspaceAccessManagement";
import { canManageOrganization } from "../tenancy/organizationMonogram";
import { useTenantDirectory } from "../tenancy/TenantDirectoryProvider";
import {
  copyInvitationLink,
  normalizeInvitationLink,
} from "../invitations/invitationUrl";

type LoadState = "loading" | "ready" | "error" | "forbidden";
type PageTab = "members" | "invitations";
type StatusFilter = "all" | "active" | "invited" | "suspended" | "removed";
type RoleFilter = "all" | "owner" | "admin" | "member";
type InviteRole = "Admin" | "Member";

type DisplayRow =
  | { key: string; kind: "member"; member: TenantMember }
  | { key: string; kind: "invitation"; invitation: PendingInvitation };

const EMPTY_INVITE_GRANTS: WorkspaceAccessDraftRow[] = [];

export function TenantMembersPage() {
  const { t } = useTranslation(["members", "tenants", "common"]);
  const { tenantId } = useParams();
  const { token, user } = useAuth();
  const { show } = useFeedback();
  const { tenants } = useTenantDirectory();
  const requestId = useRef(0);
  const emailInputRef = useRef<HTMLInputElement>(null);

  const [members, setMembers] = useState<TenantMember[]>([]);
  const [invitations, setInvitations] = useState<PendingInvitation[]>([]);
  const [workspaces, setWorkspaces] = useState<Workspace[]>([]);
  const [loadState, setLoadState] = useState<LoadState>("loading");
  const [retryNonce, setRetryNonce] = useState(0);
  const [search, setSearch] = useState("");
  const [pageTab, setPageTab] = useState<PageTab>("members");
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("all");
  const [roleFilter, setRoleFilter] = useState<RoleFilter>("all");
  const [sort, setSort] = useState<MemberSort>("nameAsc");
  const [view, setView] = useState<MemberView>(() => (user ? readMemberView(user.userId) : "list"));
  const [inviteOpen, setInviteOpen] = useState(false);
  const [inviteEmail, setInviteEmail] = useState("");
  const [inviteRole, setInviteRole] = useState<InviteRole>("Member");
  const [inviteGrants, setInviteGrants] = useState<WorkspaceAccessDraftRow[]>(EMPTY_INVITE_GRANTS);
  const [emailError, setEmailError] = useState<string | null>(null);
  const [accessTarget, setAccessTarget] = useState<TenantMember | null>(null);
  const [accessDraft, setAccessDraft] = useState<WorkspaceAccessDraftRow[]>([]);
  const [accessExisting, setAccessExisting] = useState<WorkspaceAccess[]>([]);
  const [accessLoading, setAccessLoading] = useState(false);
  const [removeTarget, setRemoveTarget] = useState<TenantMember | null>(null);
  const [roleChangeTarget, setRoleChangeTarget] = useState<TenantMember | null>(null);
  const [roleDraft, setRoleDraft] = useState<AssignableMemberRole>("Member");
  const [suspendTarget, setSuspendTarget] = useState<TenantMember | null>(null);
  const [busy, setBusy] = useState(false);
  const [inviteDevLink, setInviteDevLink] = useState<string | null>(null);

  const membership = tenants.find((item) => item.tenantId === tenantId) ?? null;
  const canManage = membership !== null && canManageOrganization(membership);

  const reload = useCallback(() => {
    setRetryNonce((current) => current + 1);
  }, []);

  function changeView(next: MemberView) {
    setView(next);
    if (user) {
      writeMemberView(user.userId, next);
    }
  }

  useEffect(() => {
    if (!token || !tenantId) {
      return;
    }
    const current = requestId.current + 1;
    requestId.current = current;
    setMembers([]);
    setInvitations([]);
    setWorkspaces([]);
    setLoadState("loading");
    const controller = new AbortController();

    void (async () => {
      try {
        const [memberList, pendingList, workspaceList] = await Promise.all([
          listMembers(token, tenantId, controller.signal),
          listPendingInvitations(token, tenantId, controller.signal),
          listWorkspaces(token, tenantId, controller.signal),
        ]);
        if (!shouldApplyResponse(current, requestId.current)) {
          return;
        }
        setMembers(memberList);
        setInvitations(pendingList);
        setWorkspaces(workspaceList);
        setLoadState("ready");
      } catch (cause) {
        if (!shouldApplyResponse(current, requestId.current)) {
          return;
        }
        if (isApiError(cause) && cause.status === 403) {
          setLoadState("forbidden");
          setMembers([]);
          setInvitations([]);
          setWorkspaces([]);
        } else if (!controller.signal.aborted) {
          setLoadState("error");
          setMembers([]);
          setInvitations([]);
          setWorkspaces([]);
        }
      }
    })();

    return () => controller.abort();
  }, [token, tenantId, retryNonce]);

  const rows = useMemo(() => {
    const display: DisplayRow[] = members.map((member) => ({
      key: `member-${member.membershipId}`,
      kind: "member",
      member,
    }));
    for (const invitation of invitations) {
      if (invitation.membershipId) {
        continue;
      }
      display.push({
        key: `invitation-${invitation.invitationId}`,
        kind: "invitation",
        invitation,
      });
    }
    return display;
  }, [members, invitations]);

  const filtered = useMemo(() => {
    const query = search.trim().toLowerCase();
    const matches = rows.filter((row) => {
      if (pageTab === "members") {
        if (row.kind === "invitation") {
          return false;
        }
        if (row.member.status === "Invited") {
          return false;
        }
      } else if (row.kind === "member" && row.member.status !== "Invited") {
        return false;
      }

      const status =
        row.kind === "member" ? row.member.status.toLowerCase() : "invited";
      const role =
        row.kind === "member"
          ? row.member.role.toLowerCase()
          : row.invitation.role.toLowerCase();
      const label =
        row.kind === "member"
          ? `${row.member.displayName} ${row.member.email}`.toLowerCase()
          : row.invitation.invitedEmail.toLowerCase();

      if (statusFilter !== "all" && status !== statusFilter) {
        return false;
      }
      if (roleFilter !== "all" && role !== roleFilter) {
        return false;
      }
      if (query && !label.includes(query)) {
        return false;
      }
      return true;
    });
    return sortMemberRows(matches, sort);
  }, [rows, search, statusFilter, roleFilter, sort, pageTab]);

  function invitationForMember(member: TenantMember): PendingInvitation | undefined {
    return invitations.find((item) => item.membershipId === member.membershipId);
  }

  function resetInvite() {
    setInviteEmail("");
    setInviteRole("Member");
    setInviteGrants(EMPTY_INVITE_GRANTS);
    setEmailError(null);
    setInviteDevLink(null);
    setInviteOpen(false);
  }

  function openInvite() {
    setEmailError(null);
    setInviteEmail("");
    setInviteRole("Member");
    setInviteGrants(buildWorkspaceAccessDraft(workspaces, []));
    setInviteOpen(true);
  }

  async function openAccessDialog(member: TenantMember) {
    if (!token || !tenantId || member.status !== "Active" || member.role !== "Member") {
      return;
    }
    setAccessTarget(member);
    setAccessLoading(true);
    setAccessExisting([]);
    setAccessDraft(buildWorkspaceAccessDraft(workspaces, []));
    try {
      const grants = await listWorkspaceAccess(token, tenantId, member.membershipId);
      setAccessExisting(grants);
      setAccessDraft(buildWorkspaceAccessDraft(workspaces, grants));
    } catch (cause) {
      setAccessTarget(null);
      showMutationError(cause, "accessLoad");
    } finally {
      setAccessLoading(false);
    }
  }

  function closeAccessDialog() {
    setAccessTarget(null);
    setAccessDraft([]);
    setAccessExisting([]);
    setAccessLoading(false);
  }

  function showMutationError(cause: unknown, mode: string) {
    if (isApiError(cause) && cause.status === 403) {
      show({
        tone: "error",
        title: t("common:feedback.errorTitle"),
        body: t("members:errors.permissionDenied"),
      });
      return;
    }
    if (isApiError(cause) && cause.code === "owner_protection") {
      show({
        tone: "error",
        title: t("common:feedback.errorTitle"),
        body: t("members:errors.ownerProtected"),
      });
      return;
    }
    if (isApiError(cause) && cause.code === "already_tenant_member") {
      setEmailError(t("members:errors.alreadyMember"));
      return;
    }
    if (isApiError(cause) && cause.code === "user_not_found") {
      setEmailError(t("tenants:errors.userNotFound"));
      return;
    }
    if (isApiError(cause) && cause.code === "invalid_email") {
      setEmailError(t("common:errors.invalid_email"));
      return;
    }
    const key = `members:errors.${mode}` as const;
    show({
      tone: "error",
      title: t("common:feedback.errorTitle"),
      body: t(key, { defaultValue: t("members:errors.generic") }),
    });
  }

  async function onInvite(event: FormEvent) {
    event.preventDefault();
    if (!token || !tenantId || busy) {
      return;
    }
    setBusy(true);
    setEmailError(null);
    try {
      const workspaceAccess =
        inviteRole === "Member"
          ? inviteGrants
              .filter((item) => item.accessLevel !== "None")
              .map((item) => ({
                workspaceId: item.workspaceId,
                accessLevel: item.accessLevel as WorkspaceAccessLevel,
              }))
          : undefined;
      const result = await inviteMember(token, tenantId, {
        email: inviteEmail.trim(),
        role: inviteRole,
        workspaceAccess,
      });
      reload();
      const invitedEmail = inviteEmail.trim();
      const publicLink = normalizeInvitationLink(result.invitationUrl);
      if (result.emailDeliveryDeferred && publicLink) {
        setInviteDevLink(publicLink);
        show({
          tone: "success",
          title: t("members:inviteCreatedTitle"),
          body: t("members:inviteDevLinkBody", { email: invitedEmail }),
        });
      } else {
        show({
          tone: "success",
          title: result.emailDeliveryDeferred
            ? t("members:inviteCreatedTitle")
            : t("members:inviteSentTitle"),
          body: result.emailDeliveryDeferred
            ? t("members:inviteCreatedBody", { email: invitedEmail })
            : t("members:inviteSentBody", { email: invitedEmail }),
        });
        resetInvite();
      }
    } catch (cause) {
      showMutationError(cause, "inviteFailed");
    } finally {
      setBusy(false);
    }
  }

  async function onSaveAccess(event: FormEvent) {
    event.preventDefault();
    if (!token || !tenantId || !accessTarget || busy) {
      return;
    }
    setBusy(true);
    try {
      await saveWorkspaceAccessChanges(
        token,
        tenantId,
        accessTarget.membershipId,
        accessExisting,
        accessDraft,
      );
      reload();
      show({
        tone: "success",
        title: t("members:accessSavedTitle"),
        body: t("members:accessSavedBody", { name: accessTarget.displayName }),
      });
      closeAccessDialog();
    } catch (cause) {
      showMutationError(cause, "accessSaveFailed");
    } finally {
      setBusy(false);
    }
  }

  function openRoleDialog(member: TenantMember) {
    const options = assignableMemberRoles(member);
    if (options.length === 0) {
      return;
    }
    const nextRole = options.find((role) => role !== member.role) ?? options[0];
    setRoleDraft(nextRole);
    setRoleChangeTarget(member);
  }

  function closeRoleDialog() {
    setRoleChangeTarget(null);
  }

  async function confirmRoleChange(event: FormEvent) {
    event.preventDefault();
    if (!token || !tenantId || !roleChangeTarget || busy) {
      return;
    }
    if (roleDraft === roleChangeTarget.role) {
      closeRoleDialog();
      return;
    }
    const demotingToMember = roleChangeTarget.role === "Admin" && roleDraft === "Member";
    setBusy(true);
    try {
      await updateMemberRole(token, tenantId, roleChangeTarget.membershipId, roleDraft);
      const memberName = roleChangeTarget.displayName;
      closeRoleDialog();
      reload();
      show({
        tone: "success",
        title: t("members:roleChangedTitle"),
        body: t("members:roleChangedBody", {
          name: memberName,
          role: t(`members:roles.${roleDraft.toLowerCase()}`),
        }),
      });
      if (demotingToMember && (roleChangeTarget.workspaceAccessCount ?? 0) === 0) {
        const updatedMember: TenantMember = {
          ...roleChangeTarget,
          role: "Member",
          hasImplicitWorkspaceAccess: false,
        };
        void openAccessDialog(updatedMember);
      }
    } catch (cause) {
      showMutationError(cause, "roleChangeFailed");
    } finally {
      setBusy(false);
    }
  }

  async function confirmSuspendMember() {
    if (!token || !tenantId || !suspendTarget || busy) {
      return;
    }
    setBusy(true);
    try {
      await suspendMember(token, tenantId, suspendTarget.membershipId);
      const memberName = suspendTarget.displayName;
      setSuspendTarget(null);
      reload();
      show({
        tone: "success",
        title: t("members:suspendedTitle"),
        body: t("members:suspendedBody", { name: memberName }),
      });
    } catch (cause) {
      showMutationError(cause, "statusChangeFailed");
    } finally {
      setBusy(false);
    }
  }

  async function reactivateMemberRow(member: TenantMember) {
    if (!token || !tenantId || busy) {
      return;
    }
    setBusy(true);
    try {
      await reactivateMember(token, tenantId, member.membershipId);
      reload();
      show({
        tone: "success",
        title: t("members:reactivatedTitle"),
        body: t("members:reactivatedBody", { name: member.displayName }),
      });
    } catch (cause) {
      showMutationError(cause, "statusChangeFailed");
    } finally {
      setBusy(false);
    }
  }

  async function confirmRemoveMember() {
    if (!token || !tenantId || !removeTarget || busy) {
      return;
    }
    setBusy(true);
    try {
      await removeMember(token, tenantId, removeTarget.membershipId);
      setRemoveTarget(null);
      reload();
      show({
        tone: "success",
        title: t("members:removedTitle"),
        body: t("members:removedBody", { name: removeTarget.displayName }),
      });
    } catch (cause) {
      showMutationError(cause, "removeFailed");
    } finally {
      setBusy(false);
    }
  }

  async function resendPending(invitation: PendingInvitation) {
    if (!token || !tenantId || busy) {
      return;
    }
    setBusy(true);
    try {
      const result = await resendInvitation(token, tenantId, invitation.invitationId);
      reload();
      const publicLink = normalizeInvitationLink(result.invitationUrl);
      show({
        tone: "success",
        title: result.emailDeliveryDeferred
          ? t("members:invitationRegeneratedTitle")
          : t("members:invitationResentTitle"),
        body: result.emailDeliveryDeferred
          ? publicLink
            ? t("members:invitationRegeneratedBody", { email: invitation.invitedEmail })
            : t("members:invitationRegeneratedBody", { email: invitation.invitedEmail })
          : t("members:invitationResentBody", { email: invitation.invitedEmail }),
      });
      if (result.emailDeliveryDeferred && publicLink) {
        setInviteDevLink(publicLink);
        setInviteOpen(true);
      }
    } catch (cause) {
      showMutationError(cause, "resendFailed");
    } finally {
      setBusy(false);
    }
  }

  async function revokePending(invitation: PendingInvitation) {
    if (!token || !tenantId || busy) {
      return;
    }
    setBusy(true);
    try {
      await revokeInvitation(token, tenantId, invitation.invitationId);
      reload();
      show({
        tone: "success",
        title: t("members:invitationRevokedTitle"),
        body: t("members:invitationRevokedBody", { email: invitation.invitedEmail }),
      });
    } catch (cause) {
      showMutationError(cause, "revokeFailed");
    } finally {
      setBusy(false);
    }
  }

  const inviteToolbarAction = canManage ? (
    <button
      type="button"
      className="primary-action toolbar-primary-action"
      aria-haspopup="dialog"
      aria-expanded={inviteOpen}
      aria-label={t("tenants:invite")}
      onClick={openInvite}
    >
      <span aria-hidden="true">+</span>
      {t("tenants:invite")}
    </button>
  ) : undefined;

  if (loadState === "forbidden") {
    return <StatusBanner tone="error">{t("common:errors.forbidden")}</StatusBanner>;
  }

  let mainContent: ReactNode;
  if (loadState === "loading") {
    mainContent = <p className="quiet-state">{t("common:loading")}</p>;
  } else if (loadState === "error") {
    mainContent = (
      <EmptyState
        title={t("members:loadErrorTitle")}
        body={t("members:loadErrorBody")}
        action={
          <button type="button" className="primary-action" onClick={reload}>
            {t("members:retryLoad")}
          </button>
        }
      />
    );
  } else if (rows.length === 0) {
    mainContent = (
      <EmptyState
        title={t("members:emptyTitle")}
        body={canManage ? t("members:emptyBodyManage") : t("members:emptyBody")}
        action={canManage ? inviteToolbarAction : undefined}
      />
    );
  } else {
    mainContent = (
      <div className="resource-section">
        <label className="resource-search resource-search-row">
          <span className="sr-only">{t("members:searchLabel")}</span>
          <input
            type="search"
            value={search}
            placeholder={t("members:searchPlaceholder")}
            aria-label={t("members:searchLabel")}
            onChange={(event) => setSearch(event.target.value)}
          />
        </label>
        <div className="member-page-tabs" role="tablist" aria-label={t("members:pageTabsLabel")}>
          <button
            type="button"
            role="tab"
            id="members-tab-members"
            aria-selected={pageTab === "members"}
            aria-controls="members-panel"
            className={pageTab === "members" ? "member-page-tab-active" : undefined}
            onClick={() => setPageTab("members")}
          >
            {t("members:pageTabs.members")}
          </button>
          <button
            type="button"
            role="tab"
            id="members-tab-invitations"
            aria-selected={pageTab === "invitations"}
            aria-controls="members-panel"
            className={pageTab === "invitations" ? "member-page-tab-active" : undefined}
            onClick={() => setPageTab("invitations")}
          >
            {t("members:pageTabs.invitations")}
          </button>
        </div>
        <ResourceToolbar
          label={t("members:resourceToolbar")}
          summary={t("members:resourceSummary", { count: filtered.length })}
        >
          <label className="resource-sort">
            <span className="sr-only">{t("members:statusFilter")}</span>
            <select
              aria-label={t("members:statusFilter")}
              value={statusFilter}
              onChange={(event) => setStatusFilter(event.target.value as StatusFilter)}
            >
              <option value="all">{t("members:filters.all")}</option>
              <option value="active">{t("members:filters.active")}</option>
              <option value="invited">{t("members:filters.invited")}</option>
              <option value="suspended">{t("members:filters.suspended")}</option>
              <option value="removed">{t("members:filters.removed")}</option>
            </select>
          </label>
          <label className="resource-sort">
            <span className="sr-only">{t("members:roleFilter")}</span>
            <select
              aria-label={t("members:roleFilter")}
              value={roleFilter}
              onChange={(event) => setRoleFilter(event.target.value as RoleFilter)}
            >
              <option value="all">{t("members:filters.roleAll")}</option>
              <option value="owner">{t("members:roles.owner")}</option>
              <option value="admin">{t("members:roles.admin")}</option>
              <option value="member">{t("members:roles.member")}</option>
            </select>
          </label>
          <label className="resource-sort">
            <span className="sr-only">{t("members:sortLabel")}</span>
            <select
              aria-label={t("members:sortLabel")}
              value={sort}
              onChange={(event) => setSort(event.target.value as MemberSort)}
            >
              <option value="nameAsc">{t("members:sortNameAsc")}</option>
              <option value="nameDesc">{t("members:sortNameDesc")}</option>
              <option value="roleAsc">{t("members:sortRole")}</option>
              <option value="statusAsc">{t("members:sortStatus")}</option>
              <option value="activeTasksDesc">{t("members:sortActiveTasks")}</option>
              <option value="completedTasksDesc">{t("members:sortCompletedTasks")}</option>
              <option value="completionRateDesc">{t("members:sortCompletionRate")}</option>
              <option value="joinedDesc">{t("members:sortJoinedDesc")}</option>
            </select>
          </label>
          <div className="view-toggle" role="group" aria-label={t("members:viewToggle")}>
            <button
              type="button"
              className={view === "grid" ? "view-toggle-active" : undefined}
              aria-pressed={view === "grid"}
              aria-label={t("members:viewGrid")}
              onClick={() => changeView("grid")}
            >
              <MemberViewIcon name="grid" />
            </button>
            <button
              type="button"
              className={view === "list" ? "view-toggle-active" : undefined}
              aria-pressed={view === "list"}
              aria-label={t("members:viewList")}
              onClick={() => changeView("list")}
            >
              <MemberViewIcon name="list" />
            </button>
          </div>
          {inviteToolbarAction}
        </ResourceToolbar>

        {filtered.length === 0 ? (
          <EmptyState
            title={t("members:searchEmptyTitle")}
            body={t("members:searchEmptyBody")}
            action={
              <button type="button" className="secondary-action" onClick={() => setSearch("")}>
                {t("members:clearSearch")}
              </button>
            }
          />
        ) : view === "grid" ? (
          <ul className="member-grid">
            {filtered.map((row) =>
              row.kind === "member" ? (
                <MemberGridCard
                  key={row.key}
                  member={row.member}
                  invitation={invitationForMember(row.member)}
                  canManage={canManage}
                  accessSummary={accessSummaryFor(row.member, t)}
                  taskSummary={formatMemberTaskSummary(row.member, t)}
                  statusLabel={t(`members:status.${row.member.status.toLowerCase()}`)}
                  roleLabel={t(`members:roles.${row.member.role.toLowerCase()}`)}
                  onOpenChangeRole={() => openRoleDialog(row.member)}
                  onSuspend={() => setSuspendTarget(row.member)}
                  onReactivate={() => void reactivateMemberRow(row.member)}
                  onManageAccess={() => void openAccessDialog(row.member)}
                  onRemove={() => setRemoveTarget(row.member)}
                  onResend={(invitation) => void resendPending(invitation)}
                  onRevoke={(invitation) => void revokePending(invitation)}
                  t={t}
                />
              ) : (
                <InvitationGridCard
                  key={row.key}
                  invitation={row.invitation}
                  canManage={canManage}
                  statusLabel={t("members:status.invited")}
                  roleLabel={t(`members:roles.${row.invitation.role.toLowerCase()}`)}
                  accessSummary={t("members:invitationPending")}
                  onResend={() => void resendPending(row.invitation)}
                  onRevoke={() => void revokePending(row.invitation)}
                  t={t}
                />
              ),
            )}
          </ul>
        ) : (
          <ul className="member-list">
            <li className="member-list-header" aria-hidden="true">
              <span>{t("members:listColumns.member")}</span>
              <span>{t("members:listColumns.status")}</span>
              <span>{t("members:listColumns.role")}</span>
              <span>{t("members:listColumns.access")}</span>
              <span>{t("members:listColumns.tasks")}</span>
              <span>{t("members:listColumns.actions")}</span>
            </li>
            {filtered.map((row) =>
              row.kind === "member" ? (
                <MemberListRow
                  key={row.key}
                  member={row.member}
                  invitation={invitationForMember(row.member)}
                  canManage={canManage}
                  accessSummary={accessSummaryFor(row.member, t)}
                  taskSummary={formatMemberTaskSummary(row.member, t)}
                  statusLabel={t(`members:status.${row.member.status.toLowerCase()}`)}
                  roleLabel={t(`members:roles.${row.member.role.toLowerCase()}`)}
                  onOpenChangeRole={() => openRoleDialog(row.member)}
                  onSuspend={() => setSuspendTarget(row.member)}
                  onReactivate={() => void reactivateMemberRow(row.member)}
                  onManageAccess={() => void openAccessDialog(row.member)}
                  onRemove={() => setRemoveTarget(row.member)}
                  onResend={(invitation) => void resendPending(invitation)}
                  onRevoke={(invitation) => void revokePending(invitation)}
                  t={t}
                />
              ) : (
                <InvitationListRow
                  key={row.key}
                  invitation={row.invitation}
                  canManage={canManage}
                  statusLabel={t("members:status.invited")}
                  roleLabel={t(`members:roles.${row.invitation.role.toLowerCase()}`)}
                  accessSummary={t("members:invitationPending")}
                  onResend={() => void resendPending(row.invitation)}
                  onRevoke={() => void revokePending(row.invitation)}
                  t={t}
                />
              ),
            )}
          </ul>
        )}
      </div>
    );
  }

  return (
    <section className="app-page">
      <PageHeader title={t("members:title")} description={t("members:pageDescription")} />
      {mainContent}

      <Dialog
        open={inviteOpen}
        titleId="invite-member-title"
        title={t("tenants:invite")}
        closeLabel={t("common:close")}
        onClose={resetInvite}
        initialFocusRef={emailInputRef}
        size="compact"
      >
        {inviteDevLink ? (
          <InvitationDevLinkPanel
            link={inviteDevLink}
            busy={busy}
            onCopy={async () => {
              const copied = await copyInvitationLink(inviteDevLink);
              if (copied) {
                show({
                  tone: "success",
                  title: t("members:invitationLinkCopied"),
                });
              }
            }}
            onClose={resetInvite}
            t={t}
          />
        ) : (
          <InviteForm
            email={inviteEmail}
            setEmail={setInviteEmail}
            role={inviteRole}
            setRole={setInviteRole}
            grants={inviteGrants}
            setGrants={setInviteGrants}
            workspaces={workspaces}
            emailError={emailError}
            setEmailError={setEmailError}
            emailInputRef={emailInputRef}
            busy={busy}
            onSubmit={onInvite}
            onCancel={resetInvite}
            t={t}
          />
        )}
      </Dialog>

      <Dialog
        open={Boolean(accessTarget)}
        titleId="member-access-title"
        title={t("members:workspaceAccess.title")}
        closeLabel={t("common:close")}
        onClose={closeAccessDialog}
        size="default"
      >
        {accessTarget ? (
          <form className="form-card form-card-compact" onSubmit={onAccessSubmit}>
            <p className="member-dialog-subtitle">
              {t("members:workspaceAccess.memberLabel", { name: accessTarget.displayName })}
            </p>
            <p>{t("members:workspaceAccess.description", { name: accessTarget.displayName })}</p>
            <WorkspaceAccessEditor
              rows={accessDraft}
              onRowsChange={setAccessDraft}
              subjectLabel={accessTarget.displayName}
              loading={accessLoading}
              emptyMessage={t("members:noWorkspaces")}
            />
            <div className="dialog-actions">
              <button className="secondary-action" type="button" onClick={closeAccessDialog}>
                {t("common:cancel")}
              </button>
              <button
                className="primary-action"
                type="submit"
                disabled={busy || accessLoading || workspaces.length === 0}
                aria-busy={busy}
              >
                {busy ? <span className="button-spinner" aria-hidden="true" /> : null}
                {busy ? t("members:workspaceAccess.saving") : t("members:workspaceAccess.saveChanges")}
              </button>
            </div>
          </form>
        ) : null}
      </Dialog>

      <Dialog
        open={Boolean(roleChangeTarget)}
        titleId="change-role-title"
        title={t("members:changeRole.title")}
        closeLabel={t("common:close")}
        onClose={closeRoleDialog}
        size="compact"
      >
        {roleChangeTarget ? (
          <form className="form-card form-card-compact" onSubmit={confirmRoleChange}>
            <p className="member-dialog-subtitle">
              {t("members:changeRole.memberLabel", { name: roleChangeTarget.displayName })}
            </p>
            <div className="form-fields">
              <Field id="current-role" label={t("members:changeRole.currentRole")}>
                <output id="current-role" className="field-static">
                  {t(`members:roles.${roleChangeTarget.role.toLowerCase()}`)}
                </output>
              </Field>
              <Field id="new-role" label={t("members:changeRole.newRole")}>
                <select
                  id="new-role"
                  value={roleDraft}
                  onChange={(event) => setRoleDraft(event.target.value as AssignableMemberRole)}
                >
                  {assignableMemberRoles(roleChangeTarget).map((role) => (
                    <option key={role} value={role}>
                      {t(`members:roles.${role.toLowerCase()}`)}
                    </option>
                  ))}
                </select>
              </Field>
            </div>
            <p className="field-hint">
              {roleDraft === "Admin"
                ? t("members:changeRole.adminDescription")
                : t("members:changeRole.memberDescription")}
            </p>
            {roleChangeTarget.role === "Admin" && roleDraft === "Member" ? (
              <p className="field-hint">{t("members:changeRole.demoteWarning")}</p>
            ) : null}
            <div className="dialog-actions">
              <button className="secondary-action" type="button" onClick={closeRoleDialog}>
                {t("common:cancel")}
              </button>
              <button
                className="primary-action"
                type="submit"
                disabled={busy || roleDraft === roleChangeTarget.role}
                aria-busy={busy}
              >
                {busy ? <span className="button-spinner" aria-hidden="true" /> : null}
                {busy ? t("members:changeRole.submitting") : t("members:changeRole.submit")}
              </button>
            </div>
          </form>
        ) : null}
      </Dialog>

      <Dialog
        open={Boolean(suspendTarget)}
        titleId="suspend-member-title"
        title={t("members:suspendConfirmTitle")}
        closeLabel={t("common:close")}
        onClose={() => setSuspendTarget(null)}
        size="compact"
      >
        {suspendTarget ? (
          <form
            className="form-card form-card-compact"
            onSubmit={(event) => {
              event.preventDefault();
              void confirmSuspendMember();
            }}
          >
            <p>{t("members:suspendConfirmBody", { name: suspendTarget.displayName })}</p>
            <p className="field-hint">{t("members:suspendConfirmHint")}</p>
            <div className="dialog-actions">
              <button className="secondary-action" type="button" onClick={() => setSuspendTarget(null)}>
                {t("common:cancel")}
              </button>
              <button className="primary-action" type="submit" disabled={busy} aria-busy={busy}>
                {busy ? <span className="button-spinner" aria-hidden="true" /> : null}
                {t("members:suspendConfirmAction")}
              </button>
            </div>
          </form>
        ) : null}
      </Dialog>

      <Dialog
        open={Boolean(removeTarget)}
        titleId="remove-member-title"
        title={t("members:removeConfirmTitle", { name: removeTarget?.displayName ?? "" })}
        closeLabel={t("common:close")}
        onClose={() => setRemoveTarget(null)}
        size="compact"
      >
        {removeTarget ? (
          <form
            className="form-card form-card-compact"
            onSubmit={(event) => {
              event.preventDefault();
              void confirmRemoveMember();
            }}
          >
            <p>{t("members:removeConfirmBody", { name: removeTarget.displayName, org: membership?.name ?? t("members:removeConfirmOrgFallback") })}</p>
            <p className="field-hint">{t("members:removeConfirmHistory")}</p>
            <p className="field-hint">{t("members:removeConfirmAccount")}</p>
            <div className="dialog-actions">
              <button className="secondary-action" type="button" onClick={() => setRemoveTarget(null)}>
                {t("common:cancel")}
              </button>
              <button className="primary-action context-menu-item-destructive-action" type="submit" disabled={busy} aria-busy={busy}>
                {busy ? <span className="button-spinner" aria-hidden="true" /> : null}
                {t("members:removeConfirmAction")}
              </button>
            </div>
          </form>
        ) : null}
      </Dialog>
    </section>
  );

  function onAccessSubmit(event: FormEvent) {
    void onSaveAccess(event);
  }
}

function accessSummaryFor(
  member: TenantMember,
  t: (key: string, options?: Record<string, unknown>) => string,
): string {
  const key = memberAccessSummaryKey(member);
  if (key === "members:workspaceCount") {
    return t(key, { count: member.workspaceAccessCount ?? 0 });
  }
  return t(key);
}

function MemberActionBar({
  member,
  canManage,
  onManageAccess,
  menuItems,
  menuLabel,
  placement,
  t,
}: {
  member: TenantMember;
  canManage: boolean;
  onManageAccess: () => void;
  menuItems: ReturnType<typeof buildMemberOverflowItems>;
  menuLabel: string;
  placement: "grid-header" | "grid-footer" | "list";
  t: (key: string) => string;
}) {
  const showManageAccess = canEditMemberAccess(member, canManage);
  const menu =
    menuItems.length > 0 ? <ContextMenu label={menuLabel} items={menuItems} /> : null;

  if (placement === "grid-header") {
    return menu ? <div className="member-card-header-actions">{menu}</div> : null;
  }

  if (placement === "grid-footer") {
    if (!showManageAccess) {
      return null;
    }
    return (
      <div className="member-card-footer">
        <button type="button" className="secondary-action member-access-action" onClick={onManageAccess}>
          {t("members:actions.manageAccessShort")}
        </button>
      </div>
    );
  }

  if (!showManageAccess && !menu) {
    return null;
  }

  return (
    <div className="member-row-action-group">
      {showManageAccess ? (
        <button type="button" className="secondary-action member-access-action" onClick={onManageAccess}>
          {t("members:actions.manageAccessShort")}
        </button>
      ) : null}
      {menu}
    </div>
  );
}

function InvitationDevLinkPanel({
  link,
  busy,
  onCopy,
  onClose,
  t,
}: {
  link: string;
  busy: boolean;
  onCopy: () => void | Promise<void>;
  onClose: () => void;
  t: (key: string, options?: Record<string, unknown>) => string;
}) {
  return (
    <div className="form-card form-card-compact">
      <p>{t("members:invitationLinkHint")}</p>
      <Field id="invitation-dev-link" label={t("members:invitationLinkLabel")}>
        <input id="invitation-dev-link" type="url" readOnly value={link} />
      </Field>
      <div className="dialog-actions">
        <button className="primary-action" type="button" disabled={busy} onClick={() => void onCopy()}>
          {t("members:copyInvitationLink")}
        </button>
        <button className="secondary-action" type="button" onClick={onClose}>
          {t("common:close")}
        </button>
      </div>
    </div>
  );
}

function InviteForm({
  email,
  setEmail,
  role,
  setRole,
  grants,
  setGrants,
  workspaces,
  emailError,
  setEmailError,
  emailInputRef,
  busy,
  onSubmit,
  onCancel,
  t,
}: {
  email: string;
  setEmail: (value: string) => void;
  role: InviteRole;
  setRole: (value: InviteRole) => void;
  grants: WorkspaceAccessDraftRow[];
  setGrants: (value: WorkspaceAccessDraftRow[]) => void;
  workspaces: Workspace[];
  emailError: string | null;
  setEmailError: (value: string | null) => void;
  emailInputRef: RefObject<HTMLInputElement | null>;
  busy: boolean;
  onSubmit: (event: FormEvent) => void;
  onCancel: () => void;
  t: (key: string, options?: Record<string, unknown>) => string;
}) {
  return (
    <form className="form-card form-card-compact" onSubmit={onSubmit}>
      <p>{t("tenants:inviteDescription")}</p>
      <div className="form-fields">
        <Field id="invite-email" label={t("tenants:inviteEmail")} error={emailError ?? undefined}>
          <input
            id="invite-email"
            ref={emailInputRef}
            type="email"
            required
            autoComplete="email"
            placeholder={t("tenants:invitePlaceholder")}
            value={email}
            onChange={(event) => {
              setEmail(event.target.value);
              setEmailError(null);
            }}
          />
        </Field>
        <Field id="invite-role" label={t("members:inviteRole")}>
          <select
            id="invite-role"
            value={role}
            onChange={(event) => setRole(event.target.value as InviteRole)}
          >
            <option value="Member">{t("members:roles.member")}</option>
            <option value="Admin">{t("members:roles.admin")}</option>
          </select>
        </Field>
        {role === "Member" && workspaces.length > 0 ? (
          <div>
            <p className="field-label">{t("members:inviteWorkspaceAccess")}</p>
            <WorkspaceAccessEditor
              rows={grants}
              onRowsChange={setGrants}
              subjectLabel={email || t("members:roles.member")}
              showBulkActions={false}
            />
          </div>
        ) : null}
      </div>
      <div className="dialog-actions">
        <button className="secondary-action" type="button" onClick={onCancel}>
          {t("common:cancel")}
        </button>
        <button className="primary-action" type="submit" disabled={busy} aria-busy={busy}>
          {busy ? <span className="button-spinner" aria-hidden="true" /> : null}
          {busy ? t("members:inviting") : t("tenants:invite")}
        </button>
      </div>
    </form>
  );
}

function MemberGridCard({
  member,
  invitation,
  canManage,
  accessSummary,
  taskSummary,
  statusLabel,
  roleLabel,
  onOpenChangeRole,
  onSuspend,
  onReactivate,
  onManageAccess,
  onRemove,
  onResend,
  onRevoke,
  t,
}: {
  member: TenantMember;
  invitation?: PendingInvitation;
  canManage: boolean;
  accessSummary: string;
  taskSummary: string;
  statusLabel: string;
  roleLabel: string;
  onOpenChangeRole: () => void;
  onSuspend: () => void;
  onReactivate: () => void;
  onManageAccess: () => void;
  onRemove: () => void;
  onResend: (invitation: PendingInvitation) => void;
  onRevoke: (invitation: PendingInvitation) => void;
  t: (key: string) => string;
}) {
  const menuItems = buildMemberOverflowItems({
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
  });

  return (
    <li>
      <article className="member-card member-card-compact">
        <div className="member-card-heading">
          <div className="member-identity">
            <UserAvatar
              displayName={member.displayName}
              email={member.email}
              imageUrl={member.avatarUrl}
            />
            <span className="entity-copy">
              <strong>{member.displayName || member.email}</strong>
              {member.displayName ? <small>{member.email}</small> : null}
            </span>
          </div>
          <MemberActionBar
            member={member}
            canManage={canManage}
            onManageAccess={onManageAccess}
            menuItems={menuItems}
            menuLabel={t("members:actions.menu")}
            placement="grid-header"
            t={t}
          />
        </div>
        <div className="member-card-meta">
          <span className="status-pill">{statusLabel}</span>
          <span className="role-badge">{roleLabel}</span>
        </div>
        <div className="member-card-details">
          <span className="member-detail-line" title={member.hasImplicitWorkspaceAccess ? t("members:fullAccessHint") : undefined}>
            {accessSummary}
          </span>
          <span className="member-detail-line member-detail-tasks">
            <span className="member-detail-label">{t("members:listColumns.tasks")}</span>
            {taskSummary}
          </span>
        </div>
        <MemberActionBar
          member={member}
          canManage={canManage}
          onManageAccess={onManageAccess}
          menuItems={menuItems}
          menuLabel={t("members:actions.menu")}
          placement="grid-footer"
          t={t}
        />
      </article>
    </li>
  );
}

function MemberListRow({
  member,
  invitation,
  canManage,
  accessSummary,
  taskSummary,
  statusLabel,
  roleLabel,
  onOpenChangeRole,
  onSuspend,
  onReactivate,
  onManageAccess,
  onRemove,
  onResend,
  onRevoke,
  t,
}: {
  member: TenantMember;
  invitation?: PendingInvitation;
  canManage: boolean;
  accessSummary: string;
  taskSummary: string;
  statusLabel: string;
  roleLabel: string;
  onOpenChangeRole: () => void;
  onSuspend: () => void;
  onReactivate: () => void;
  onManageAccess: () => void;
  onRemove: () => void;
  onResend: (invitation: PendingInvitation) => void;
  onRevoke: (invitation: PendingInvitation) => void;
  t: (key: string) => string;
}) {
  const menuItems = buildMemberOverflowItems({
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
  });

  return (
    <li>
      <article className="member-row">
        <div className="member-row-main">
          <UserAvatar displayName={member.displayName} email={member.email} imageUrl={member.avatarUrl} />
          <span className="member-row-copy">
            <strong>{member.displayName || member.email}</strong>
            {member.displayName ? <small>{member.email}</small> : null}
          </span>
        </div>
        <span className="member-row-cell">
          <span className="status-pill">{statusLabel}</span>
        </span>
        <span className="member-row-cell">{roleLabel}</span>
        <span className="member-row-cell" title={member.hasImplicitWorkspaceAccess ? t("members:fullAccessHint") : undefined}>
          {accessSummary}
        </span>
        <span className="member-row-cell">{taskSummary}</span>
        <span className="member-row-actions">
          <MemberActionBar
            member={member}
            canManage={canManage}
            onManageAccess={onManageAccess}
            menuItems={menuItems}
            menuLabel={t("members:actions.menu")}
            placement="list"
            t={t}
          />
        </span>
      </article>
    </li>
  );
}

function InvitationGridCard({
  invitation,
  canManage,
  accessSummary,
  statusLabel,
  roleLabel,
  onResend,
  onRevoke,
  t,
}: {
  invitation: PendingInvitation;
  canManage: boolean;
  accessSummary: string;
  statusLabel: string;
  roleLabel: string;
  onResend: () => void;
  onRevoke: () => void;
  t: (key: string) => string;
}) {
  return (
    <li>
      <article className="member-card member-card-compact">
        <div className="member-card-heading">
          <div className="member-identity">
            <UserAvatar displayName="" email={invitation.invitedEmail} className="entity-monogram-teal" />
            <span className="entity-copy">
              <strong>{invitation.invitedEmail}</strong>
            </span>
          </div>
          {canManage ? (
            <div className="member-card-header-actions">
              <ContextMenu
                label={t("members:actions.menu")}
                items={buildInvitationOverflowItems({
                  canManage,
                  onResend,
                  onRevoke,
                  t,
                })}
              />
            </div>
          ) : null}
        </div>
        <div className="member-card-meta">
          <span className="status-pill">{statusLabel}</span>
          <span className="role-badge">{roleLabel}</span>
        </div>
        <p className="member-detail-line">{accessSummary}</p>
      </article>
    </li>
  );
}

function InvitationListRow({
  invitation,
  canManage,
  accessSummary,
  statusLabel,
  roleLabel,
  onResend,
  onRevoke,
  t,
}: {
  invitation: PendingInvitation;
  canManage: boolean;
  accessSummary: string;
  statusLabel: string;
  roleLabel: string;
  onResend: () => void;
  onRevoke: () => void;
  t: (key: string) => string;
}) {
  return (
    <li>
      <article className="member-row">
        <div className="member-row-main">
          <UserAvatar displayName="" email={invitation.invitedEmail} className="entity-monogram-teal" />
          <span className="member-row-copy">
            <strong>{invitation.invitedEmail}</strong>
          </span>
        </div>
        <span className="member-row-cell">
          <span className="status-pill">{statusLabel}</span>
        </span>
        <span className="member-row-cell">{roleLabel}</span>
        <span className="member-row-cell">{accessSummary}</span>
        <span className="member-row-cell">{t("members:tasks.none")}</span>
        <span className="member-row-actions">
          {canManage ? (
            <div className="member-row-action-group">
              <ContextMenu
                label={t("members:actions.menu")}
                items={buildInvitationOverflowItems({
                  canManage,
                  onResend,
                  onRevoke,
                  t,
                })}
              />
            </div>
          ) : null}
        </span>
      </article>
    </li>
  );
}

function MemberViewIcon({ name }: { name: "grid" | "list" }) {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden="true">
      {name === "grid" ? (
        <>
          <rect x="2" y="2" width="5" height="5" rx="1" />
          <rect x="9" y="2" width="5" height="5" rx="1" />
          <rect x="2" y="9" width="5" height="5" rx="1" />
          <rect x="9" y="9" width="5" height="5" rx="1" />
        </>
      ) : (
        <>
          <rect x="2" y="3" width="12" height="2" rx="1" />
          <rect x="2" y="7" width="12" height="2" rx="1" />
          <rect x="2" y="11" width="12" height="2" rx="1" />
        </>
      )}
    </svg>
  );
}
