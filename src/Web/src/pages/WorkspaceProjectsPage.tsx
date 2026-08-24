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
    <section className="stack">
      <h1>{t("projects:title")}</h1>
      {error ? <StatusBanner tone="error">{error}</StatusBanner> : null}
      <form className="panel" onSubmit={onCreate}>
        <h2>{t("projects:create")}</h2>
        <Field id="project-name" label={t("projects:name")}>
          <input id="project-name" required value={name} onChange={(event) => setName(event.target.value)} />
        </Field>
        <button type="submit" disabled={busy}>
          {busy ? t("common:loading") : t("projects:create")}
        </button>
      </form>
      <div className="panel">
        <h2>{t("projects:list")}</h2>
        {projects.length === 0 ? (
          <p>{t("projects:empty")}</p>
        ) : (
          <ul className="plain-list">
            {projects.map((project) => (
              <li key={project.projectId}>{project.name}</li>
            ))}
          </ul>
        )}
      </div>
    </section>
  );
}
