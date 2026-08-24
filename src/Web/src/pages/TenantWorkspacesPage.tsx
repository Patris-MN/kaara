import { type FormEvent, useEffect, useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";

import { createWorkspace, inviteMember, listTenants, listWorkspaces } from "../api/client";
import { isApiError, translationKeyForApiError } from "../api/errors";
import { shouldApplyResponse } from "../api/requestIdentity";
import type { TenantMembership, Workspace } from "../api/types";
import { useAuth } from "../auth/AuthProvider";
import { Field, StatusBanner } from "../components/Ui";

export function TenantWorkspacesPage() {
  const { t } = useTranslation(["workspaces", "tenants", "common"]);
  const { tenantId } = useParams();
  const { token } = useAuth();
  const requestId = useRef(0);
  const [membership, setMembership] = useState<TenantMembership | null>(null);
  const [workspaces, setWorkspaces] = useState<Workspace[]>([]);
  const [name, setName] = useState("");
  const [inviteEmail, setInviteEmail] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [forbidden, setForbidden] = useState(false);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!token || !tenantId) {
      return;
    }
    const current = requestId.current + 1;
    requestId.current = current;
    setWorkspaces([]);
    setMembership(null);
    setForbidden(false);
    setError(null);
    const controller = new AbortController();

    void (async () => {
      try {
        const [tenantList, workspaceList] = await Promise.all([
          listTenants(token),
          listWorkspaces(token, tenantId, controller.signal),
        ]);
        if (!shouldApplyResponse(current, requestId.current)) {
          return;
        }
        setMembership(tenantList.find((item) => item.tenantId === tenantId) ?? null);
        setWorkspaces(workspaceList);
      } catch (cause) {
        if (!shouldApplyResponse(current, requestId.current)) {
          return;
        }
        if (isApiError(cause) && cause.status === 403) {
          setForbidden(true);
          setWorkspaces([]);
        } else {
          setError(t(translationKeyForApiError(cause), { ns: "common" }));
        }
      }
    })();

    return () => controller.abort();
  }, [token, tenantId, t]);

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

  const canInvite = membership?.role === "Owner" || membership?.role === "Admin";

  if (forbidden) {
    return <StatusBanner tone="error">{t("common:errors.forbidden")}</StatusBanner>;
  }

  return (
    <section className="stack">
      <h1>{membership?.name ?? t("workspaces:title")}</h1>
      {error ? <StatusBanner tone="error">{error}</StatusBanner> : null}

      <form className="panel" onSubmit={onCreate}>
        <h2>{t("workspaces:create")}</h2>
        <Field id="workspace-name" label={t("workspaces:name")}>
          <input id="workspace-name" required value={name} onChange={(event) => setName(event.target.value)} />
        </Field>
        <button type="submit" disabled={busy}>
          {busy ? t("common:loading") : t("workspaces:create")}
        </button>
      </form>

      {canInvite ? (
        <form className="panel" onSubmit={onInvite}>
          <h2>{t("tenants:invite")}</h2>
          <Field id="invite-email" label={t("tenants:inviteEmail")}>
            <input
              id="invite-email"
              type="email"
              required
              value={inviteEmail}
              onChange={(event) => setInviteEmail(event.target.value)}
            />
          </Field>
          <button type="submit" disabled={busy}>
            {t("tenants:invite")}
          </button>
        </form>
      ) : null}

      <div className="panel">
        <h2>{t("workspaces:list")}</h2>
        {workspaces.length === 0 ? (
          <p>{t("workspaces:empty")}</p>
        ) : (
          <ul className="plain-list">
            {workspaces.map((workspace) => (
              <li key={workspace.workspaceId}>
                <Link to={`/app/tenants/${tenantId}/workspaces/${workspace.workspaceId}`}>
                  {workspace.name}
                </Link>
              </li>
            ))}
          </ul>
        )}
      </div>
    </section>
  );
}
