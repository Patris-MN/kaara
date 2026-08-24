import { NavLink, Outlet, useNavigate, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";

import { listTenants } from "../api/client";
import { translationKeyForApiError } from "../api/errors";
import { readSelectedTenantId, writeSelectedTenantId } from "../api/session";
import type { TenantMembership } from "../api/types";
import { useAuth } from "../auth/AuthProvider";
import { LanguageSwitcher } from "../components/LanguageSwitcher";
import { StatusBanner } from "../components/Ui";

export function AppLayout() {
  const { t } = useTranslation(["common", "navigation", "tenants"]);
  const { user, token, logout } = useAuth();
  const navigate = useNavigate();
  const params = useParams();
  const [tenants, setTenants] = useState<TenantMembership[]>([]);
  const [loadError, setLoadError] = useState<string | null>(null);

  useEffect(() => {
    if (!token) {
      return;
    }
    const controller = new AbortController();
    void listTenants(token)
      .then((items) => {
        if (controller.signal.aborted) {
          return;
        }
        setTenants(items);
        setLoadError(null);
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          setLoadError(t(translationKeyForApiError(error), { ns: "common" }));
        }
      });
    return () => controller.abort();
  }, [token, t]);

  useEffect(() => {
    if (!user || tenants.length === 0) {
      return;
    }
    const selected = params.tenantId ?? readSelectedTenantId(user.userId);
    if (selected && tenants.some((tenant) => tenant.tenantId === selected)) {
      if (!params.tenantId) {
        writeSelectedTenantId(user.userId, selected);
      }
    } else if (params.tenantId) {
      navigate("/app", { replace: true });
    }
  }, [user, tenants, params.tenantId, navigate]);

  function onTenantChange(tenantId: string) {
    if (!user) {
      return;
    }
    if (!tenantId) {
      navigate("/app");
      return;
    }
    writeSelectedTenantId(user.userId, tenantId);
    navigate(`/app/tenants/${tenantId}`);
  }

  return (
    <div className="app-shell">
      <header className="app-header">
        <div className="header-start">
          <NavLink to="/app" className="brand">
            {t("common:app.name")}
          </NavLink>
          <label className="tenant-switcher">
            <span>{t("tenants:selector")}</span>
            <select
              value={params.tenantId ?? ""}
              onChange={(event) => onTenantChange(event.target.value)}
              aria-label={t("tenants:selector")}
            >
              <option value="">{t("tenants:choose")}</option>
              {tenants.map((tenant) => (
                <option key={tenant.tenantId} value={tenant.tenantId}>
                  {tenant.name}
                </option>
              ))}
            </select>
          </label>
        </div>
        <div className="header-end">
          <p className="current-user">
            {t("common:signedInAs", { name: user?.displayName ?? user?.email })}
            {user?.isPlatformAdministrator ? ` · ${t("common:platformAdministrator")}` : null}
          </p>
          <LanguageSwitcher />
          <button type="button" onClick={() => { logout(); navigate("/login"); }}>
            {t("auth:signOut", { ns: "auth" })}
          </button>
        </div>
      </header>
      <nav aria-label={t("navigation:main.label")}>
        <NavLink to="/app">{t("navigation:tenants")}</NavLink>
        {params.tenantId ? (
          <NavLink to={`/app/tenants/${params.tenantId}`}>{t("navigation:workspaces")}</NavLink>
        ) : null}
        <span className="nav-muted">{t("navigation:tasks")}</span>
      </nav>
      {loadError ? <StatusBanner tone="error">{loadError}</StatusBanner> : null}
      <main>
        <Outlet />
      </main>
    </div>
  );
}
