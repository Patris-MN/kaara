import { type FormEvent, useEffect, useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";

import { createProject, getWorkspace, listProjects } from "../api/client";
import { isApiError, translationKeyForApiError } from "../api/errors";
import { shouldApplyResponse } from "../api/requestIdentity";
import type { Project, Workspace } from "../api/types";
import { useAuth } from "../auth/AuthProvider";
import { Field, StatusBanner } from "../components/Ui";

export function WorkspaceProjectsPage() {
  const { t } = useTranslation(["projects", "tasks", "common"]);
  const { tenantId, workspaceId } = useParams();
  const { token } = useAuth();
  const requestId = useRef(0);
  const [workspace, setWorkspace] = useState<Workspace | null>(null);
  const [projects, setProjects] = useState<Project[]>([]);
  const [name, setName] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [forbidden, setForbidden] = useState(false);
  const [notFound, setNotFound] = useState(false);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!token || !tenantId || !workspaceId) {
      return;
    }
    const current = requestId.current + 1;
    requestId.current = current;
    setWorkspace(null);
    setProjects([]);
    setForbidden(false);
    setNotFound(false);
    setError(null);
    const controller = new AbortController();

    void (async () => {
      try {
        const [nextWorkspace, items] = await Promise.all([
          getWorkspace(token, tenantId, workspaceId, controller.signal),
          listProjects(token, tenantId, workspaceId, controller.signal),
        ]);
        if (!shouldApplyResponse(current, requestId.current)) {
          return;
        }
        setWorkspace(nextWorkspace);
        setProjects(items);
      } catch (cause: unknown) {
        if (!shouldApplyResponse(current, requestId.current)) {
          return;
        }
        if (isApiError(cause) && cause.status === 403) {
          setForbidden(true);
          setProjects([]);
          return;
        }
        if (isApiError(cause) && (cause.status === 404 || cause.code === "workspace_not_found")) {
          setNotFound(true);
          setProjects([]);
          return;
        }
        setError(t(translationKeyForApiError(cause), { ns: "common" }));
      }
    })();

    return () => controller.abort();
  }, [token, tenantId, workspaceId, t]);

  async function onCreate(event: FormEvent) {
    event.preventDefault();
    if (!token || !tenantId || !workspaceId || busy) {
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const created = await createProject(token, tenantId, workspaceId, name);
      setProjects((current) => [...current, created]);
      setName("");
    } catch (cause) {
      setError(t(translationKeyForApiError(cause), { ns: "common" }));
    } finally {
      setBusy(false);
    }
  }

  if (forbidden) {
    return <StatusBanner tone="error">{t("common:errors.forbidden")}</StatusBanner>;
  }

  if (notFound) {
    return <StatusBanner tone="error">{t("common:errors.workspace_not_found")}</StatusBanner>;
  }

  const canEdit = workspace?.accessLevel === "Edit";

  return (
    <section className="app-page">
      <header className="page-heading">
        <div>
          <p className="page-eyebrow">{t("projects:eyebrow")}</p>
          <h1>{workspace?.name ?? t("projects:title")}</h1>
          <p>{t("projects:description")}</p>
        </div>
        <div className="page-stat">
          <strong>{projects.length}</strong>
          <span>{t("projects:projectCount")}</span>
        </div>
      </header>
      {error ? <StatusBanner tone="error">{error}</StatusBanner> : null}
      {workspace?.accessLevel === "View" ? (
        <StatusBanner tone="info">{t("projects:viewOnly")}</StatusBanner>
      ) : null}

      {canEdit ? (
        <form className="surface-card form-card form-card-horizontal" onSubmit={onCreate}>
          <div className="card-heading">
            <span className="card-icon card-icon-purple" aria-hidden="true">+</span>
            <div>
              <h2>{t("projects:create")}</h2>
              <p>{t("projects:createDescription")}</p>
            </div>
          </div>
          <div className="inline-create">
            <Field id="project-name" label={t("projects:name")}>
              <input
                id="project-name"
                required
                placeholder={t("projects:namePlaceholder")}
                value={name}
                onChange={(event) => setName(event.target.value)}
              />
            </Field>
            <button className="primary-action" type="submit" disabled={busy}>
              <span aria-hidden="true">+</span>
              {busy ? t("common:loading") : t("projects:create")}
            </button>
          </div>
        </form>
      ) : null}

      <div className="surface-card entity-section">
        <div className="card-heading card-heading-between">
          <div>
            <h2>{t("projects:list")}</h2>
            <p>{t("projects:listDescription")}</p>
          </div>
          <span className="count-badge">{projects.length}</span>
        </div>
        {projects.length === 0 ? (
          <div className="empty-state">
            <span className="empty-state-icon" aria-hidden="true">✓</span>
            <strong>{t("projects:emptyTitle")}</strong>
            <p>{t("projects:empty")}</p>
          </div>
        ) : (
          <ul className="project-card-grid">
            {projects.map((project, index) => (
              <li className="project-card" key={project.projectId}>
                <Link
                  className="project-card-link"
                  to={`/app/tenants/${tenantId}/workspaces/${workspaceId}/projects/${project.projectId}`}
                >
                  <div className={`project-accent project-accent-${(index % 3) + 1}`} />
                  <div className="project-card-top">
                    <span className="project-symbol" aria-hidden="true">
                      {project.name.charAt(0).toUpperCase()}
                    </span>
                    <span className="status-pill">{t("projects:active")}</span>
                  </div>
                  <strong>{project.name}</strong>
                  <p>{t("projects:projectDescription")}</p>
                  <div className="project-card-footer">
                    <span>{t("tasks:openTasks")}</span>
                    <span aria-hidden="true">→</span>
                  </div>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </div>
    </section>
  );
}
