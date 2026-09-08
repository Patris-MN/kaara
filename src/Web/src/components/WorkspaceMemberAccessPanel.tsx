import { type FormEvent, useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import {
  grantWorkspaceMemberAccess,
  listWorkspaceMemberAccess,
  removeWorkspaceAccess,
  setWorkspaceAccess,
} from "../api/client";
import { isApiError } from "../api/errors";
import { shouldApplyResponse } from "../api/requestIdentity";
import type { WorkspaceAccessLevel, WorkspaceMemberAccess } from "../api/types";
import { Dialog } from "./Dialog";
import { EmptyState } from "./EmptyState";
import { Field } from "./Ui";
import {
  isAssignableWorkspaceMember,
  workspaceMemberAccessLabelKey,
} from "../members/workspaceAccessManagement";

type LoadState = "loading" | "ready" | "error" | "forbidden";

type WorkspaceMemberAccessPanelProps = {
  token: string;
  tenantId: string;
  workspaceId: string;
  canManage: boolean;
  onAccessChanged?: () => void;
};

export function WorkspaceMemberAccessPanel({
  token,
  tenantId,
  workspaceId,
  canManage,
  onAccessChanged,
}: WorkspaceMemberAccessPanelProps) {
  const { t } = useTranslation(["members", "workspaces", "common"]);
  const requestId = useRef(0);
  const [members, setMembers] = useState<WorkspaceMemberAccess[]>([]);
  const [loadState, setLoadState] = useState<LoadState>("loading");
  const [search, setSearch] = useState("");
  const [accessFilter, setAccessFilter] = useState<"all" | "view" | "edit" | "full">("all");
  const [addOpen, setAddOpen] = useState(false);
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [addAccessLevel, setAddAccessLevel] = useState<WorkspaceAccessLevel>("View");
  const [busy, setBusy] = useState(false);
  const [mutationError, setMutationError] = useState<string | null>(null);

  const reload = useCallback(() => {
    requestId.current += 1;
    const current = requestId.current;
    setLoadState("loading");
    setMutationError(null);

    void (async () => {
      try {
        const rows = await listWorkspaceMemberAccess(token, tenantId, workspaceId);
        if (!shouldApplyResponse(current, requestId.current)) {
          return;
        }
        setMembers(rows);
        setLoadState("ready");
      } catch (cause) {
        if (!shouldApplyResponse(current, requestId.current)) {
          return;
        }
        if (isApiError(cause) && cause.status === 403) {
          setLoadState("forbidden");
          setMembers([]);
          return;
        }
        setLoadState("error");
        setMembers([]);
      }
    })();
  }, [token, tenantId, workspaceId]);

  useEffect(() => {
    if (!canManage) {
      return;
    }
    // oxlint-disable-next-line react/set-state-in-effect
    reload();
  }, [canManage, reload]);

  const withAccess = useMemo(
    () =>
      members.filter(
        (member) =>
          member.hasImplicitWorkspaceAccess
          || member.effectiveAccess === "View"
          || member.effectiveAccess === "Edit",
      ),
    [members],
  );

  const assignableWithoutAccess = useMemo(
    () => members.filter((member) => isAssignableWorkspaceMember(member) && member.effectiveAccess === "None"),
    [members],
  );

  const filteredWithAccess = useMemo(() => {
    const query = search.trim().toLowerCase();
    return withAccess.filter((member) => {
      const label = `${member.displayName} ${member.email}`.toLowerCase();
      if (query && !label.includes(query)) {
        return false;
      }
      if (accessFilter === "view" && member.effectiveAccess !== "View") {
        return false;
      }
      if (accessFilter === "edit" && member.effectiveAccess !== "Edit") {
        return false;
      }
      if (accessFilter === "full" && !member.hasImplicitWorkspaceAccess) {
        return false;
      }
      return true;
    });
  }, [withAccess, search, accessFilter]);

  function openAddDialog() {
    setSelectedIds(new Set());
    setAddAccessLevel("View");
    setMutationError(null);
    setAddOpen(true);
  }

  function closeAddDialog() {
    setAddOpen(false);
    setSelectedIds(new Set());
    setMutationError(null);
  }

  async function onAddMembers(event: FormEvent) {
    event.preventDefault();
    if (busy || selectedIds.size === 0) {
      return;
    }
    setBusy(true);
    setMutationError(null);
    try {
      await grantWorkspaceMemberAccess(token, tenantId, workspaceId, [...selectedIds], addAccessLevel);
      closeAddDialog();
      reload();
      onAccessChanged?.();
    } catch (cause) {
      if (isApiError(cause) && cause.status === 403) {
        setMutationError(t("members:errors.permissionDenied"));
      } else {
        setMutationError(t("members:errors.accessSaveFailed"));
      }
    } finally {
      setBusy(false);
    }
  }

  async function onMemberAccessChange(member: WorkspaceMemberAccess, next: WorkspaceAccessLevel | "None") {
    if (busy || !isAssignableWorkspaceMember(member)) {
      return;
    }
    setBusy(true);
    setMutationError(null);
    try {
      if (next === "None") {
        await removeWorkspaceAccess(token, tenantId, member.membershipId, workspaceId);
      } else {
        await setWorkspaceAccess(token, tenantId, member.membershipId, workspaceId, next);
      }
      reload();
      onAccessChanged?.();
    } catch (cause) {
      if (isApiError(cause) && cause.status === 403) {
        setMutationError(t("members:errors.permissionDenied"));
      } else {
        setMutationError(t("members:errors.accessSaveFailed"));
      }
    } finally {
      setBusy(false);
    }
  }

  if (!canManage) {
    return null;
  }

  return (
    <div className="surface-card entity-section workspace-member-access-panel">
      <div className="card-heading card-heading-between">
        <div>
          <h2>{t("workspaces:memberAccess.title")}</h2>
          <p>{t("workspaces:memberAccess.description")}</p>
        </div>
        <button type="button" className="secondary-action" onClick={openAddDialog} disabled={assignableWithoutAccess.length === 0}>
          {t("workspaces:memberAccess.addMembers")}
        </button>
      </div>

      {mutationError ? <p className="field-error">{mutationError}</p> : null}

      {loadState === "loading" ? (
        <p className="quiet-state">{t("common:loading")}</p>
      ) : loadState === "error" ? (
        <EmptyState
          title={t("workspaces:memberAccess.loadErrorTitle")}
          body={t("workspaces:memberAccess.loadErrorBody")}
          action={
            <button type="button" className="primary-action" onClick={reload}>
              {t("workspaces:retryLoad")}
            </button>
          }
        />
      ) : loadState === "forbidden" ? (
        <p className="field-hint">{t("members:errors.permissionDenied")}</p>
      ) : (
        <>
          <div className="workspace-access-editor-controls">
            <label className="resource-search">
              <span className="sr-only">{t("members:searchLabel")}</span>
              <input
                type="search"
                value={search}
                placeholder={t("members:searchPlaceholder")}
                aria-label={t("members:searchLabel")}
                onChange={(event) => setSearch(event.target.value)}
              />
            </label>
            <label className="compact-field">
              <span className="sr-only">{t("workspaces:memberAccess.filterLabel")}</span>
              <select
                aria-label={t("workspaces:memberAccess.filterLabel")}
                value={accessFilter}
                onChange={(event) => setAccessFilter(event.target.value as typeof accessFilter)}
              >
                <option value="all">{t("workspaces:memberAccess.filterAll")}</option>
                <option value="full">{t("workspaces:memberAccess.filterFull")}</option>
                <option value="edit">{t("members:access.edit")}</option>
                <option value="view">{t("members:access.view")}</option>
              </select>
            </label>
          </div>

          {filteredWithAccess.length === 0 ? (
            <EmptyState
              title={t("workspaces:memberAccess.emptyTitle")}
              body={t("workspaces:memberAccess.emptyBody")}
              action={
                assignableWithoutAccess.length > 0 ? (
                  <button type="button" className="primary-action" onClick={openAddDialog}>
                    {t("workspaces:memberAccess.addMembers")}
                  </button>
                ) : undefined
              }
            />
          ) : (
            <div className="workspace-member-access-table-wrap">
              <table className="resource-table workspace-member-access-table">
                <thead>
                  <tr>
                    <th scope="col">{t("workspaces:memberAccess.columns.member")}</th>
                    <th scope="col">{t("workspaces:memberAccess.columns.role")}</th>
                    <th scope="col">{t("workspaces:memberAccess.columns.access")}</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredWithAccess.map((member) => (
                    <tr key={member.membershipId}>
                      <td>
                        <strong>{member.displayName}</strong>
                        <span className="table-secondary">{member.email}</span>
                      </td>
                      <td>{t(`members:roles.${member.role.toLowerCase()}`)}</td>
                      <td>
                        {isAssignableWorkspaceMember(member) ? (
                          <select
                            aria-label={t("members:accessLabel", {
                              member: member.displayName,
                              workspace: t("workspaces:memberAccess.thisWorkspace"),
                            })}
                            value={member.effectiveAccess === "None" ? "None" : member.effectiveAccess}
                            disabled={busy}
                            onChange={(event) => {
                              const value = event.target.value as WorkspaceAccessLevel | "None";
                              void onMemberAccessChange(member, value);
                            }}
                          >
                            <option value="None">{t("members:access.none")}</option>
                            <option value="View">{t("members:access.view")}</option>
                            <option value="Edit">{t("members:access.edit")}</option>
                          </select>
                        ) : (
                          <span className="field-static">
                            {member.hasImplicitWorkspaceAccess
                              ? t("members:fullAccess")
                              : t(workspaceMemberAccessLabelKey(member.hasImplicitWorkspaceAccess, member.effectiveAccess))}
                          </span>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}

      <Dialog
        open={addOpen}
        titleId="workspace-add-members-title"
        title={t("workspaces:memberAccess.addDialogTitle")}
        closeLabel={t("common:close")}
        onClose={closeAddDialog}
        size="default"
      >
        <form className="form-card form-card-compact" onSubmit={onAddMembers}>
          <p>{t("workspaces:memberAccess.addDialogDescription")}</p>
          {assignableWithoutAccess.length === 0 ? (
            <p className="field-hint">{t("workspaces:memberAccess.noAssignableMembers")}</p>
          ) : (
            <ul className="workspace-add-members-list">
              {assignableWithoutAccess.map((member) => (
                <li key={member.membershipId}>
                  <label className="checkbox-row">
                    <input
                      type="checkbox"
                      checked={selectedIds.has(member.membershipId)}
                      onChange={(event) => {
                        setSelectedIds((current) => {
                          const next = new Set(current);
                          if (event.target.checked) {
                            next.add(member.membershipId);
                          } else {
                            next.delete(member.membershipId);
                          }
                          return next;
                        });
                      }}
                    />
                    <span>
                      <strong>{member.displayName}</strong>
                      <span className="table-secondary">{member.email}</span>
                    </span>
                  </label>
                </li>
              ))}
            </ul>
          )}
          <Field id="workspace-add-access-level" label={t("workspaces:memberAccess.accessLevelLabel")}>
            <div className="radio-row" role="radiogroup" aria-labelledby="workspace-add-access-level">
              <label className="radio-option">
                <input
                  type="radio"
                  name="workspace-add-access-level"
                  value="View"
                  checked={addAccessLevel === "View"}
                  onChange={() => setAddAccessLevel("View")}
                />
                {t("members:access.view")}
              </label>
              <label className="radio-option">
                <input
                  type="radio"
                  name="workspace-add-access-level"
                  value="Edit"
                  checked={addAccessLevel === "Edit"}
                  onChange={() => setAddAccessLevel("Edit")}
                />
                {t("members:access.edit")}
              </label>
            </div>
          </Field>
          {mutationError ? <p className="field-error">{mutationError}</p> : null}
          <div className="dialog-actions">
            <button type="button" className="secondary-action" onClick={closeAddDialog}>
              {t("common:cancel")}
            </button>
            <button
              type="submit"
              className="primary-action"
              disabled={busy || selectedIds.size === 0}
              aria-busy={busy}
            >
              {busy ? t("workspaces:memberAccess.adding") : t("workspaces:memberAccess.addSubmit")}
            </button>
          </div>
        </form>
      </Dialog>
    </div>
  );
}
