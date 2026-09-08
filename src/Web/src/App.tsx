import { useEffect } from "react";
import { Navigate, Route, Routes, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";

import { GuestRoute, ProtectedRoute } from "./auth/RouteGuards";
import { FeedbackProvider } from "./feedback/FeedbackProvider";
import { AppLayout } from "./pages/AppLayout";
import { LoginPage } from "./pages/LoginPage";
import { NotFoundPage } from "./pages/NotFoundPage";
import { RegisterPage } from "./pages/RegisterPage";
import { TenantWorkspacesPage } from "./pages/TenantWorkspacesPage";
import { TenantMembersPage } from "./pages/TenantMembersPage";
import { InviteAcceptPage } from "./pages/InviteAcceptPage";
import { TenantsPage } from "./pages/TenantsPage";
import { ProfilePage } from "./pages/ProfilePage";
import { ProjectTasksPage } from "./pages/ProjectTasksPage";
import { WorkspaceProjectsPage } from "./pages/WorkspaceProjectsPage";
import "./App.css";

function TenantWorkspacesRoute() {
  const { tenantId } = useParams();
  return <TenantWorkspacesPage key={tenantId} />;
}

function TenantMembersRoute() {
  const { tenantId } = useParams();
  return <TenantMembersPage key={tenantId} />;
}

function App() {
  const { t } = useTranslation("common");

  useEffect(() => {
    document.title = t("app.name");
  }, [t]);

  return (
    <FeedbackProvider>
    <Routes>
      <Route element={<GuestRoute />}>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/register" element={<RegisterPage />} />
      </Route>
      <Route element={<ProtectedRoute />}>
        <Route path="/app" element={<AppLayout />}>
          <Route index element={<TenantsPage />} />
          <Route path="tenants/:tenantId" element={<TenantWorkspacesRoute />} />
          <Route path="tenants/:tenantId/members" element={<TenantMembersRoute />} />
          <Route
            path="tenants/:tenantId/workspaces/:workspaceId"
            element={<WorkspaceProjectsPage />}
          />
          <Route path="tenants/:tenantId/workspaces/:workspaceId/projects/:projectId/*" element={<ProjectTasksPage />} />
          <Route path="profile" element={<ProfilePage />} />
        </Route>
      </Route>
      <Route path="/invite/:token" element={<InviteAcceptPage />} />
      <Route path="/" element={<Navigate to="/app" replace />} />
      <Route path="*" element={<NotFoundPage />} />
    </Routes>
    </FeedbackProvider>
  );
}

export default App;
