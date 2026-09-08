import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";

import {
  applyBulkAccessLevel,
  filterWorkspaceAccessDraft,
  sortWorkspaceAccessDraft,
  type AccessDraftLevel,
  type WorkspaceAccessDraftRow,
  type WorkspaceAccessFilter,
  type WorkspaceAccessSort,
} from "../members/workspaceAccessManagement";

type WorkspaceAccessEditorProps = {
  rows: WorkspaceAccessDraftRow[];
  onRowsChange: (rows: WorkspaceAccessDraftRow[]) => void;
  subjectLabel: string;
  loading?: boolean;
  emptyMessage?: string;
  showBulkActions?: boolean;
};

export function WorkspaceAccessEditor({
  rows,
  onRowsChange,
  subjectLabel,
  loading = false,
  emptyMessage,
  showBulkActions = true,
}: WorkspaceAccessEditorProps) {
  const { t } = useTranslation(["members", "common"]);
  const [search, setSearch] = useState("");
  const [accessFilter, setAccessFilter] = useState<WorkspaceAccessFilter>("all");
  const [sort, setSort] = useState<WorkspaceAccessSort>("nameAsc");

  const visibleRows = useMemo(
    () => sortWorkspaceAccessDraft(filterWorkspaceAccessDraft(rows, search, accessFilter), sort),
    [rows, search, accessFilter, sort],
  );

  function updateRow(workspaceId: string, accessLevel: AccessDraftLevel) {
    onRowsChange(
      rows.map((row) => (row.workspaceId === workspaceId ? { ...row, accessLevel } : row)),
    );
  }

  function applyBulk(accessLevel: AccessDraftLevel) {
    const visibleIds = new Set(visibleRows.map((row) => row.workspaceId));
    onRowsChange(applyBulkAccessLevel(rows, accessLevel, visibleIds));
  }

  if (loading) {
    return <p className="quiet-state">{t("common:loading")}</p>;
  }

  if (rows.length === 0) {
    return <p className="field-hint">{emptyMessage ?? t("members:noWorkspaces")}</p>;
  }

  return (
    <div className="workspace-access-editor">
      <div className="workspace-access-editor-controls">
        <label className="resource-search">
          <span className="sr-only">{t("members:workspaceAccess.searchWorkspaces")}</span>
          <input
            type="search"
            value={search}
            placeholder={t("members:workspaceAccess.searchPlaceholder")}
            aria-label={t("members:workspaceAccess.searchWorkspaces")}
            onChange={(event) => setSearch(event.target.value)}
          />
        </label>
        <label className="compact-field">
          <span className="sr-only">{t("members:workspaceAccess.filterLabel")}</span>
          <select
            aria-label={t("members:workspaceAccess.filterLabel")}
            value={accessFilter}
            onChange={(event) => setAccessFilter(event.target.value as WorkspaceAccessFilter)}
          >
            <option value="all">{t("members:workspaceAccess.filterAll")}</option>
            <option value="hasAccess">{t("members:workspaceAccess.filterHasAccess")}</option>
            <option value="noAccess">{t("members:workspaceAccess.filterNoAccess")}</option>
          </select>
        </label>
        <label className="compact-field">
          <span className="sr-only">{t("members:workspaceAccess.sortLabel")}</span>
          <select
            aria-label={t("members:workspaceAccess.sortLabel")}
            value={sort}
            onChange={(event) => setSort(event.target.value as WorkspaceAccessSort)}
          >
            <option value="nameAsc">{t("members:workspaceAccess.sortNameAsc")}</option>
            <option value="nameDesc">{t("members:workspaceAccess.sortNameDesc")}</option>
            <option value="accessAsc">{t("members:workspaceAccess.sortAccessAsc")}</option>
            <option value="accessDesc">{t("members:workspaceAccess.sortAccessDesc")}</option>
          </select>
        </label>
      </div>

      {showBulkActions ? (
        <div className="workspace-access-bulk-actions" role="group" aria-label={t("members:workspaceAccess.bulkLabel")}>
          <button type="button" className="secondary-action secondary-action-compact" onClick={() => applyBulk("View")}>
            {t("members:workspaceAccess.setAllView")}
          </button>
          <button type="button" className="secondary-action secondary-action-compact" onClick={() => applyBulk("Edit")}>
            {t("members:workspaceAccess.setAllEdit")}
          </button>
          <button type="button" className="secondary-action secondary-action-compact" onClick={() => applyBulk("None")}>
            {t("members:workspaceAccess.clearAccess")}
          </button>
        </div>
      ) : null}

      {visibleRows.length === 0 ? (
        <p className="field-hint">{t("members:workspaceAccess.noMatches")}</p>
      ) : (
        <ul className="member-access-list">
          {visibleRows.map((item) => (
            <li key={item.workspaceId} className="member-access-row">
              <span>{item.name}</span>
              <select
                aria-label={t("members:accessLabel", {
                  member: subjectLabel,
                  workspace: item.name,
                })}
                value={item.accessLevel}
                onChange={(event) => updateRow(item.workspaceId, event.target.value as AccessDraftLevel)}
              >
                <option value="None">{t("members:access.none")}</option>
                <option value="View">{t("members:access.view")}</option>
                <option value="Edit">{t("members:access.edit")}</option>
              </select>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
