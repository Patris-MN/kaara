import { type FormEvent, useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";

import { acceptInvitation, createTenant, listInvitations, listTenants } from "../api/client";
import { isApiError, translationKeyForApiError } from "../api/errors";
import { writeSelectedTenantId } from "../api/session";
import type { TenantMembership } from "../api/types";
import { useAuth } from "../auth/AuthProvider";
import { Field, StatusBanner } from "../components/Ui";

export function TenantsPage() {
  const { t } = useTranslation(["tenants", "common"]);
  const { token, user } = useAuth();
  const navigate = useNavigate();
  const [tenants, setTenants] = useState<TenantMembership[]>([]);
  const [invitations, setInvitations] = useState<TenantMembership[]>([]);
  const [name, setName] = useState("");
  const [slug, setSlug] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function refresh(signal?: AbortSignal) {
    if (!token) {
      return;
    }
    const [nextTenants, nextInvitations] = await Promise.all([
      listTenants(token),
      listInvitations(token),
    ]);
    if (signal?.aborted) {
      return;
    }
    setTenants(nextTenants);
    setInvitations(nextInvitations);
  }

  useEffect(() => {
    const controller = new AbortController();
    void refresh(controller.signal).catch((cause: unknown) => {
      if (!controller.signal.aborted) {
        setError(t(translationKeyForApiError(cause), { ns: "common" }));
      }
    });
    return () => controller.abort();
  }, [token, t]);

  async function onCreate(event: FormEvent) {
    event.preventDefault();
    if (!token || !user || busy) {
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const created = await createTenant(token, name, slug);
      writeSelectedTenantId(user.userId, created.tenantId);
      await refresh();
      setName("");
      setSlug("");
      navigate(`/app/tenants/${created.tenantId}`);
    } catch (cause) {
      setError(
        isApiError(cause) && cause.code === "duplicate_slug"
          ? t("tenants:errors.duplicateSlug")
          : t(translationKeyForApiError(cause), { ns: "common" }),
      );
    } finally {
      setBusy(false);
    }
  }

  async function onAccept(tenantId: string) {
    if (!token || !user || busy) {
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await acceptInvitation(token, tenantId);
      writeSelectedTenantId(user.userId, tenantId);
      await refresh();
      navigate(`/app/tenants/${tenantId}`);
    } catch (cause) {
      setError(t(translationKeyForApiError(cause), { ns: "common" }));
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="stack">
      <h1>{t("tenants:title")}</h1>
      {error ? <StatusBanner tone="error">{error}</StatusBanner> : null}

      <form className="panel" onSubmit={onCreate}>
        <h2>{t("tenants:create")}</h2>
        <Field id="tenant-name" label={t("tenants:name")}>
          <input id="tenant-name" required value={name} onChange={(event) => setName(event.target.value)} />
        </Field>
        <Field id="tenant-slug" label={t("tenants:slug")}>
          <input id="tenant-slug" required value={slug} onChange={(event) => setSlug(event.target.value)} />
        </Field>
        <button type="submit" disabled={busy}>
          {busy ? t("common:loading") : t("tenants:create")}
        </button>
      </form>

      <div className="panel">
        <h2>{t("tenants:list")}</h2>
        {tenants.length === 0 ? (
          <p>{t("tenants:empty")}</p>
        ) : (
          <ul className="plain-list">
            {tenants.map((tenant) => (
              <li key={tenant.tenantId}>
                <Link to={`/app/tenants/${tenant.tenantId}`}>{tenant.name}</Link>
                <span className="meta">{tenant.role}</span>
              </li>
            ))}
          </ul>
        )}
      </div>

      <div className="panel">
        <h2>{t("tenants:invitations")}</h2>
        {invitations.length === 0 ? (
          <p>{t("tenants:noInvitations")}</p>
        ) : (
          <ul className="plain-list">
            {invitations.map((invitation) => (
              <li key={invitation.tenantId}>
                <span>{invitation.name}</span>
                <button type="button" disabled={busy} onClick={() => void onAccept(invitation.tenantId)}>
                  {t("tenants:accept")}
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>
    </section>
  );
}
