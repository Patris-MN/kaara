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

type IconName = "organization" | "workspace" | "project" | "task" | "logout" | "chevron";

function AppIcon({ name }: { name: IconName }) {
  const paths: Record<IconName, React.ReactNode> = {
    organization: (
      <>
        <path d="M4 20h16M6 20V9h12v11M9 13h2M13 13h2M9 17h2M13 17h2M8 9V5h8v4" />
      </>
    ),
    workspace: (
      <>
        <path d="M3.5 7.5h7l2 2h8v10h-17z" />
        <path d="M3.5 7.5v-3h6l2 3" />
      </>
    ),
    project: (
      <>
        <rect x="4" y="4" width="16" height="16" rx="3" />
        <path d="M8 9h8M8 13h5M8 17h7" />
      </>
    ),
    task: (
      <>
        <path d="M9 6h11M9 12h11M9 18h11" />
        <path d="m3.5 6 1.5 1.5L7.5 4.5M3.5 12 5 13.5l2.5-3M3.5 18 5 19.5l2.5-3" />
      </>
    ),
    logout: (
      <>
        <path d="M10 5H5v14h5M14 8l4 4-4 4M8 12h10" />
      </>
    ),
    chevron: <path d="m9 6 6 6-6 6" />,
  };

  return (
    <svg className="app-icon" viewBox="0 0 24 24" aria-hidden="true">
      {paths[name]}
    </svg>
  );
}

function AppBrandMark() {
  return (
    <span className="app-brand-mark" aria-hidden="true">
      <svg viewBox="0 0 32 32">
        <path d="M7 8.5h8.5V17H7zM16.5 15H25v8.5h-8.5z" />
        <path d="M15.5 12.75h2.25V15H15.5zM10.2 17h2.25v5.4H17v2.25h-6.8z" />
      </svg>
    </span>
  );
}

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

  const currentTenant = tenants.find((tenant) => tenant.tenantId === params.tenantId);
  const userName = user?.displayName ?? user?.email ?? "";
  const userInitial = userName.trim().charAt(0).toUpperCase() || "U";

  return (
    <div className="workspace-shell">
      <aside className="workspace-sidebar">
        <NavLink to="/app" className="workspace-brand">
          <AppBrandMark />
          <span>
            <strong>PTS</strong>
            <small>{t("navigation:workspace")}</small>
          </span>
        </NavLink>

        <nav className="workspace-nav" aria-label={t("navigation:main.label")}>
          <NavLink to="/app" end>
            <AppIcon name="organization" />
            <span>{t("navigation:tenants")}</span>
          </NavLink>
          {params.tenantId ? (
            <NavLink to={`/app/tenants/${params.tenantId}`} end>
              <AppIcon name="workspace" />
              <span>{t("navigation:workspaces")}</span>
            </NavLink>
          ) : (
            <span className="workspace-nav-disabled">
              <AppIcon name="workspace" />
              <span>{t("navigation:workspaces")}</span>
            </span>
          )}
          {params.workspaceId && params.tenantId ? (
            <NavLink to={`/app/tenants/${params.tenantId}/workspaces/${params.workspaceId}`}>
              <AppIcon name="project" />
              <span>{t("navigation:projects")}</span>
            </NavLink>
          ) : (
            <span className="workspace-nav-disabled">
              <AppIcon name="project" />
              <span>{t("navigation:projects")}</span>
            </span>
          )}
          <span className="workspace-nav-disabled">
            <AppIcon name="task" />
            <span>{t("navigation:tasks")}</span>
            <small>{t("navigation:soon")}</small>
          </span>
        </nav>

        <div className="sidebar-account">
          <div className="user-avatar">{userInitial}</div>
          <div className="sidebar-user-copy">
            <strong>{userName}</strong>
            <span>
              {user?.isPlatformAdministrator
                ? t("common:platformAdministrator")
                : user?.email}
            </span>
          </div>
          <button
            type="button"
            className="sidebar-logout"
            aria-label={t("auth:signOut", { ns: "auth" })}
            onClick={() => {
              logout();
              navigate("/login");
            }}
          >
            <AppIcon name="logout" />
          </button>
        </div>
      </aside>

      <div className="workspace-main">
        <header className="workspace-topbar">
          <div className="topbar-context">
            <span>{t("tenants:selector")}</span>
            <strong>{currentTenant?.name ?? t("tenants:allOrganizations")}</strong>
          </div>
          <div className="topbar-actions">
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
            <LanguageSwitcher />
            <div className="topbar-avatar" title={userName}>
              {userInitial}
            </div>
          </div>
        </header>

        <div className="workspace-content">
          {loadError ? <StatusBanner tone="error">{loadError}</StatusBanner> : null}
          <main>
            <Outlet />
          </main>
        </div>
      </div>
    </div>
  );
}
