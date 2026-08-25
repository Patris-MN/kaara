import { type FormEvent, useEffect, useRef, useState } from "react";
import { useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";

import { createProject, listProjects } from "../api/client";
import { isApiError, translationKeyForApiError } from "../api/errors";
import { shouldApplyResponse } from "../api/requestIdentity";
import type { Project } from "../api/types";
import { useAuth } from "../auth/AuthProvider";
import { Field, StatusBanner } from "../components/Ui";

export function WorkspaceProjectsPage() {
  const { t } = useTranslation(["projects", "common"]);
  const { tenantId, workspaceId } = useParams();
  const { token } = useAuth();
  const requestId = useRef(0);
  const [projects, setProjects] = useState<Project[]>([]);
  const [name, setName] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [forbidden, setForbidden] = useState(false);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!token || !tenantId || !workspaceId) {
      return;
    }
    const current = requestId.current + 1;
    requestId.current = current;
    setProjects([]);
    setForbidden(false);
    setError(null);
    const controller = new AbortController();

    void listProjects(token, tenantId, workspaceId, controller.signal)
      .then((items) => {
        if (shouldApplyResponse(current, requestId.current)) {
          setProjects(items);
        }
      })
      .catch((cause: unknown) => {
        if (!shouldApplyResponse(current, requestId.current)) {
          return;
        }
        if (isApiError(cause) && cause.status === 403) {
          setForbidden(true);
          setProjects([]);
        } else {
          setError(t(translationKeyForApiError(cause), { ns: "common" }));
        }
      });

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

  return (
    <section className="app-page">
      <header className="page-heading">
        <div>
          <p className="page-eyebrow">{t("projects:eyebrow")}</p>
          <h1>{t("projects:title")}</h1>
          <p>{t("projects:description")}</p>
        </div>
        <div className="page-stat">
          <strong>{projects.length}</strong>
          <span>{t("projects:projectCount")}</span>
        </div>
      </header>
      {error ? <StatusBanner tone="error">{error}</StatusBanner> : null}

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
                  <span>{t("projects:readyForTasks")}</span>
                  <span aria-hidden="true">•••</span>
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>
    </section>
  );
}
