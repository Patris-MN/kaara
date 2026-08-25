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
    <section className="app-page">
      <header className="page-heading">
        <div>
          <p className="page-eyebrow">{t("tenants:eyebrow")}</p>
          <h1>{t("tenants:title")}</h1>
          <p>{t("tenants:description")}</p>
        </div>
        <div className="page-stat">
          <strong>{tenants.length}</strong>
          <span>{t("tenants:organizationCount")}</span>
        </div>
      </header>
      {error ? <StatusBanner tone="error">{error}</StatusBanner> : null}

      <div className="dashboard-grid dashboard-grid-organizations">
        <form className="surface-card form-card" onSubmit={onCreate}>
          <div className="card-heading">
            <span className="card-icon card-icon-purple" aria-hidden="true">+</span>
            <div>
              <h2>{t("tenants:create")}</h2>
              <p>{t("tenants:createDescription")}</p>
            </div>
          </div>
          <div className="form-fields">
            <Field id="tenant-name" label={t("tenants:name")}>
              <input
                id="tenant-name"
                required
                placeholder={t("tenants:namePlaceholder")}
                value={name}
                onChange={(event) => setName(event.target.value)}
              />
            </Field>
            <Field id="tenant-slug" label={t("tenants:slug")}>
              <input
                id="tenant-slug"
                required
                placeholder={t("tenants:slugPlaceholder")}
                value={slug}
                onChange={(event) => setSlug(event.target.value)}
              />
            </Field>
          </div>
          <button className="primary-action" type="submit" disabled={busy}>
            <span aria-hidden="true">+</span>
            {busy ? t("common:loading") : t("tenants:create")}
          </button>
        </form>

        <div className="surface-card entity-section">
          <div className="card-heading card-heading-between">
            <div>
              <h2>{t("tenants:list")}</h2>
              <p>{t("tenants:listDescription")}</p>
            </div>
            <span className="count-badge">{tenants.length}</span>
          </div>
          {tenants.length === 0 ? (
            <div className="empty-state">
              <span className="empty-state-icon" aria-hidden="true">◇</span>
              <strong>{t("tenants:emptyTitle")}</strong>
              <p>{t("tenants:empty")}</p>
            </div>
          ) : (
            <ul className="entity-list">
              {tenants.map((tenant) => (
                <li key={tenant.tenantId}>
                  <Link className="entity-row" to={`/app/tenants/${tenant.tenantId}`}>
                    <span className="entity-monogram">{tenant.name.charAt(0).toUpperCase()}</span>
                    <span className="entity-copy">
                      <strong>{tenant.name}</strong>
                      <small>/{tenant.slug}</small>
                    </span>
                    <span className="role-badge">{tenant.role}</span>
                    <span className="entity-arrow" aria-hidden="true">→</span>
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>

      <div className="surface-card invitation-section">
        <div className="card-heading card-heading-between">
          <div>
            <h2>{t("tenants:invitations")}</h2>
            <p>{t("tenants:invitationDescription")}</p>
          </div>
          <span className="count-badge count-badge-teal">{invitations.length}</span>
        </div>
        {invitations.length === 0 ? (
          <p className="quiet-state">{t("tenants:noInvitations")}</p>
        ) : (
          <ul className="entity-list">
            {invitations.map((invitation) => (
              <li className="invitation-row" key={invitation.tenantId}>
                <span className="entity-monogram entity-monogram-teal">
                  {invitation.name.charAt(0).toUpperCase()}
                </span>
                <span className="entity-copy">
                  <strong>{invitation.name}</strong>
                  <small>{t("tenants:invitedRole", { role: invitation.role })}</small>
                </span>
                <button
                  className="secondary-action"
                  type="button"
                  disabled={busy}
                  onClick={() => void onAccept(invitation.tenantId)}
                >
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
