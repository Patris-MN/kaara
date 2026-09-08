import { NavLink, Outlet, useLocation, useNavigate, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useEffect } from "react";

import { translationKeyForApiError } from "../api/errors";
import { readSelectedTenantId, writeSelectedTenantId } from "../api/session";
import { useAuth } from "../auth/AuthProvider";
import { LanguageSwitcher } from "../components/LanguageSwitcher";
import { StatusBanner } from "../components/Ui";
import { resolveActiveTenantId } from "../tenancy/activeTenant";
import { useTenantDirectory } from "../tenancy/TenantDirectoryProvider";
import { NotificationMenu } from "./NotificationMenu";
import { UserMenu } from "../components/UserMenu";

type IconName = "organization" | "workspace" | "members" | "project" | "task" | "logout" | "chevron";

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
    members: (
      <>
        <path d="M16 11a3 3 0 1 0-6 0 3 3 0 0 0 6 0Z" />
        <path d="M8 18a6 6 0 0 1 8 0M4 20a6 6 0 0 1 7.5-5.7M20 20a6 6 0 0 0-7.5-5.7" />
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

function DisabledNavLabel({
  icon,
  label,
  hint,
}: {
  icon: IconName;
  label: string;
  hint: string;
}) {
  return (
    <span className="workspace-nav-disabled" aria-disabled="true" title={hint} aria-label={`${label}. ${hint}`}>
      <AppIcon name={icon} />
      <span>{label}</span>
    </span>
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
  const { user, logout } = useAuth();
  const { tenants, error: directoryError } = useTenantDirectory();
  const navigate = useNavigate();
  const location = useLocation();
  const params = useParams();
  const loadError = directoryError
    ? t(translationKeyForApiError(directoryError), { ns: "common" })
    : null;
  const notice = (location.state as { notice?: string } | null)?.notice;

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

  const storedTenantId = user ? readSelectedTenantId(user.userId) : null;
  const activeTenantId = resolveActiveTenantId(tenants, params.tenantId, storedTenantId);
  const currentTenant = tenants.find((tenant) => tenant.tenantId === activeTenantId);
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
          {activeTenantId ? (
            <NavLink to={`/app/tenants/${activeTenantId}`} end>
              <AppIcon name="workspace" />
              <span>{t("navigation:workspaces")}</span>
            </NavLink>
          ) : (
            <DisabledNavLabel
              icon="workspace"
              label={t("navigation:workspaces")}
              hint={t("navigation:selectOrganizationFirst")}
            />
          )}
          {activeTenantId ? (
            <NavLink to={`/app/tenants/${activeTenantId}/members`}>
              <AppIcon name="members" />
              <span>{t("navigation:members")}</span>
            </NavLink>
          ) : (
            <DisabledNavLabel
              icon="members"
              label={t("navigation:members")}
              hint={t("navigation:selectOrganizationFirst")}
            />
          )}
          {params.workspaceId && activeTenantId ? (
            <NavLink to={`/app/tenants/${activeTenantId}/workspaces/${params.workspaceId}`}>
              <AppIcon name="project" />
              <span>{t("navigation:projects")}</span>
            </NavLink>
          ) : (
            <DisabledNavLabel
              icon="project"
              label={t("navigation:projects")}
              hint={
                activeTenantId
                  ? t("navigation:selectWorkspaceFirst")
                  : t("navigation:selectOrganizationFirst")
              }
            />
          )}
          {params.projectId && params.workspaceId && activeTenantId ? (
            <NavLink
              to={`/app/tenants/${activeTenantId}/workspaces/${params.workspaceId}/projects/${params.projectId}`}
            >
              <AppIcon name="task" />
              <span>{t("navigation:tasks")}</span>
            </NavLink>
          ) : (
            <DisabledNavLabel
              icon="task"
              label={t("navigation:tasks")}
              hint={
                params.workspaceId
                  ? t("navigation:selectProjectFirst")
                  : activeTenantId
                    ? t("navigation:selectWorkspaceFirst")
                    : t("navigation:selectOrganizationFirst")
              }
            />
          )}
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
          {params.tenantId ? (
            <div className="topbar-context">
              <span>{t("tenants:selector")}</span>
              <strong>{currentTenant?.name ?? t("tenants:choose")}</strong>
            </div>
          ) : (
            <div className="topbar-context">
              <strong>{t("navigation:tenants")}</strong>
            </div>
          )}
          <div className="topbar-actions">
            <label className="tenant-switcher">
              <span>{t("tenants:selector")}</span>
              <select
                value={activeTenantId ?? ""}
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
            <NotificationMenu />
            <UserMenu
              onSignOut={() => {
                logout();
                navigate("/login");
              }}
            />
          </div>
        </header>

        <div className="workspace-content">
          {loadError ? <StatusBanner tone="error">{loadError}</StatusBanner> : null}
          {notice ? (
            <StatusBanner tone="success">{t(`tenants:feedback.${notice}`, { defaultValue: notice })}</StatusBanner>
          ) : null}
          <main>
            <Outlet />
          </main>
        </div>
      </div>
    </div>
  );
}
