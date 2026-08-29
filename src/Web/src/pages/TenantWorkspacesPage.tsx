import { type FormEvent, useEffect, useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";

import {
  createWorkspace,
  inviteMember,
  listMembers,
  listWorkspaceAccess,
  listWorkspaces,
  removeWorkspaceAccess,
  setWorkspaceAccess,
} from "../api/client";
import { isApiError, translationKeyForApiError } from "../api/errors";
import { shouldApplyResponse } from "../api/requestIdentity";
import type { TenantMember, Workspace, WorkspaceAccessLevel } from "../api/types";
import { useAuth } from "../auth/AuthProvider";
import { Field, StatusBanner } from "../components/Ui";
import { useTenantDirectory } from "../tenancy/TenantDirectoryProvider";

type AccessChoice = "None" | WorkspaceAccessLevel;
type AccessByMember = Record<string, Record<string, WorkspaceAccessLevel>>;

export function TenantWorkspacesPage() {
  const { t } = useTranslation(["workspaces", "tenants", "members", "common"]);
  const { tenantId } = useParams();
  const { token } = useAuth();
  const { tenants } = useTenantDirectory();
  const requestId = useRef(0);
  const [workspaces, setWorkspaces] = useState<Workspace[]>([]);
  const [members, setMembers] = useState<TenantMember[]>([]);
  const [accessByMember, setAccessByMember] = useState<AccessByMember>({});
  const [name, setName] = useState("");
  const [inviteEmail, setInviteEmail] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [forbidden, setForbidden] = useState(false);
  const [busy, setBusy] = useState(false);

  const membership = tenants.find((item) => item.tenantId === tenantId) ?? null;
  const canManage = membership?.role === "Owner" || membership?.role === "Admin";

  useEffect(() => {
    if (!token || !tenantId) {
      return;
    }
    const current = requestId.current + 1;
    requestId.current = current;
    setWorkspaces([]);
    setMembers([]);
    setAccessByMember({});
    setForbidden(false);
    setError(null);
    const controller = new AbortController();

    void (async () => {
      try {
        const workspaceList = await listWorkspaces(token, tenantId, controller.signal);
        if (!shouldApplyResponse(current, requestId.current)) {
          return;
        }
        setWorkspaces(workspaceList);

        if (!canManage) {
          return;
        }

        const memberList = await listMembers(token, tenantId, controller.signal);
        if (!shouldApplyResponse(current, requestId.current)) {
          return;
        }
        setMembers(memberList);

        const activeMembers = memberList.filter(
          (member) => member.role === "Member" && member.status === "Active",
        );
        const accessEntries = await Promise.all(
          activeMembers.map(async (member) => {
            const access = await listWorkspaceAccess(
              token,
              tenantId,
              member.membershipId,
              controller.signal,
            );
            return [
              member.membershipId,
              Object.fromEntries(access.map((item) => [item.workspaceId, item.accessLevel])),
            ] as const;
          }),
        );
        if (!shouldApplyResponse(current, requestId.current)) {
          return;
        }
        setAccessByMember(Object.fromEntries(accessEntries));
      } catch (cause) {
        if (!shouldApplyResponse(current, requestId.current)) {
          return;
        }
        if (isApiError(cause) && cause.status === 403) {
          setForbidden(true);
          setWorkspaces([]);
          setMembers([]);
        } else {
          setError(t(translationKeyForApiError(cause), { ns: "common" }));
        }
      }
    })();

    return () => controller.abort();
  }, [token, tenantId, canManage, t]);

  async function onCreate(event: FormEvent) {
    event.preventDefault();
    if (!token || !tenantId || busy) {
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const created = await createWorkspace(token, tenantId, name);
      setWorkspaces((current) => [...current, created]);
      setName("");
    } catch (cause) {
      setError(t(translationKeyForApiError(cause), { ns: "common" }));
    } finally {
      setBusy(false);
    }
  }

  async function onInvite(event: FormEvent) {
    event.preventDefault();
    if (!token || !tenantId || busy) {
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await inviteMember(token, tenantId, inviteEmail);
      setInviteEmail("");
      if (canManage) {
        const memberList = await listMembers(token, tenantId);
        setMembers(memberList);
      }
    } catch (cause) {
      setError(
        isApiError(cause) && cause.code === "user_not_found"
          ? t("tenants:errors.userNotFound")
          : t(translationKeyForApiError(cause), { ns: "common" }),
      );
    } finally {
      setBusy(false);
    }
  }

  async function onAccessChange(
    membershipId: string,
    workspaceId: string,
    next: AccessChoice,
  ) {
    if (!token || !tenantId || busy) {
      return;
    }
    const previous = accessByMember[membershipId]?.[workspaceId];
    setAccessByMember((current) => {
      const memberAccess = { ...(current[membershipId] ?? {}) };
      if (next === "None") {
        delete memberAccess[workspaceId];
      } else {
        memberAccess[workspaceId] = next;
      }
      return { ...current, [membershipId]: memberAccess };
    });
    setBusy(true);
    setError(null);
    try {
      if (next === "None") {
        await removeWorkspaceAccess(token, tenantId, membershipId, workspaceId);
      } else {
        await setWorkspaceAccess(token, tenantId, membershipId, workspaceId, next);
      }
    } catch (cause) {
      setAccessByMember((current) => {
        const memberAccess = { ...(current[membershipId] ?? {}) };
        if (previous) {
          memberAccess[workspaceId] = previous;
        } else {
          delete memberAccess[workspaceId];
        }
        return { ...current, [membershipId]: memberAccess };
      });
      setError(t(translationKeyForApiError(cause), { ns: "common" }));
    } finally {
      setBusy(false);
    }
  }

  if (forbidden) {
    return <StatusBanner tone="error">{t("common:errors.forbidden")}</StatusBanner>;
  }

  return (
    <section className="app-page">
      <header className="page-heading">
        <div>
          <p className="page-eyebrow">{t("workspaces:eyebrow")}</p>
          <h1>{membership?.name ?? t("workspaces:title")}</h1>
          <p>{t("workspaces:description")}</p>
        </div>
        <div className="page-stat">
          <strong>{workspaces.length}</strong>
          <span>{t("workspaces:workspaceCount")}</span>
        </div>
      </header>
      {error ? <StatusBanner tone="error">{error}</StatusBanner> : null}

      {canManage ? (
        <div className="dashboard-grid dashboard-grid-two">
          <form className="surface-card form-card" onSubmit={onCreate}>
            <div className="card-heading">
              <span className="card-icon card-icon-purple" aria-hidden="true">+</span>
              <div>
                <h2>{t("workspaces:create")}</h2>
                <p>{t("workspaces:createDescription")}</p>
              </div>
            </div>
            <Field id="workspace-name" label={t("workspaces:name")}>
              <input
                id="workspace-name"
                required
                placeholder={t("workspaces:namePlaceholder")}
                value={name}
                onChange={(event) => setName(event.target.value)}
              />
            </Field>
            <button className="primary-action" type="submit" disabled={busy}>
              <span aria-hidden="true">+</span>
              {busy ? t("common:loading") : t("workspaces:create")}
            </button>
          </form>

          <form className="surface-card form-card" onSubmit={onInvite}>
            <div className="card-heading">
              <span className="card-icon card-icon-teal" aria-hidden="true">↗</span>
              <div>
                <h2>{t("tenants:invite")}</h2>
                <p>{t("tenants:inviteDescription")}</p>
              </div>
            </div>
            <Field id="invite-email" label={t("tenants:inviteEmail")}>
              <input
                id="invite-email"
                type="email"
                required
                placeholder={t("tenants:invitePlaceholder")}
                value={inviteEmail}
                onChange={(event) => setInviteEmail(event.target.value)}
              />
            </Field>
            <button className="secondary-action" type="submit" disabled={busy}>
              {t("tenants:invite")}
            </button>
          </form>
        </div>
      ) : null}

      <div className="surface-card entity-section">
        <div className="card-heading card-heading-between">
          <div>
            <h2>{t("workspaces:list")}</h2>
            <p>{t("workspaces:listDescription")}</p>
          </div>
          <span className="count-badge">{workspaces.length}</span>
        </div>
        {workspaces.length === 0 ? (
          <div className="empty-state">
            <span className="empty-state-icon" aria-hidden="true">□</span>
            <strong>
              {canManage ? t("workspaces:emptyTitle") : t("workspaces:emptyAssignedTitle")}
            </strong>
            <p>{canManage ? t("workspaces:empty") : t("workspaces:emptyAssigned")}</p>
          </div>
        ) : (
          <ul className="entity-card-grid">
            {workspaces.map((workspace) => (
              <li key={workspace.workspaceId}>
                <Link
                  className="workspace-card"
                  to={`/app/tenants/${tenantId}/workspaces/${workspace.workspaceId}`}
                >
                  <span className="workspace-card-icon" aria-hidden="true">
                    <span />
                  </span>
                  <span className="entity-copy">
                    <strong>{workspace.name}</strong>
                    <small>{t("workspaces:openProjects")}</small>
                  </span>
                  <span className="entity-arrow" aria-hidden="true">→</span>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </div>

      {canManage ? (
        <div className="surface-card entity-section">
          <div className="card-heading card-heading-between">
            <div>
              <h2>{t("members:title")}</h2>
              <p>{t("members:description")}</p>
            </div>
            <span className="count-badge">{members.length}</span>
          </div>
          {members.length === 0 ? (
            <p className="quiet-state">{t("members:empty")}</p>
          ) : (
            <ul className="member-list">
              {members.map((member) => (
                <li className="member-card" key={member.membershipId}>
                  <div className="member-card-heading">
                    <span className="entity-copy">
                      <strong>{member.displayName}</strong>
                      <small>{member.email}</small>
                    </span>
                    <span className="member-badges">
                      <span className="role-badge">
                        {t(`members:roles.${member.role.toLowerCase()}`)}
                      </span>
                      <span className="status-pill">
                        {t(`members:status.${member.status.toLowerCase()}`)}
                      </span>
                    </span>
                  </div>
                  {member.role === "Owner" || member.role === "Admin" ? (
                    <p className="quiet-state">{t("members:implicitAccess")}</p>
                  ) : member.status !== "Active" ? (
                    <p className="quiet-state">{t("members:inactiveAccess")}</p>
                  ) : workspaces.length === 0 ? (
                    <p className="quiet-state">{t("members:noWorkspaces")}</p>
                  ) : (
                    <ul className="member-access-list">
                      {workspaces.map((workspace) => {
                        const value = accessByMember[member.membershipId]?.[workspace.workspaceId] ?? "None";
                        const selectId = `access-${member.membershipId}-${workspace.workspaceId}`;
                        return (
                          <li key={workspace.workspaceId}>
                            <label className="member-access-row" htmlFor={selectId}>
                              <span>{workspace.name}</span>
                              <select
                                id={selectId}
                                value={value}
                                disabled={busy}
                                aria-label={t("members:accessLabel", {
                                  member: member.displayName,
                                  workspace: workspace.name,
                                })}
                                onChange={(event) =>
                                  void onAccessChange(
                                    member.membershipId,
                                    workspace.workspaceId,
                                    event.target.value as AccessChoice,
                                  )
                                }
                              >
                                <option value="None">{t("members:access.none")}</option>
                                <option value="View">{t("members:access.view")}</option>
                                <option value="Edit">{t("members:access.edit")}</option>
                              </select>
                            </label>
                          </li>
                        );
                      })}
                    </ul>
                  )}
                </li>
              ))}
            </ul>
          )}
        </div>
      ) : null}
    </section>
  );
}
