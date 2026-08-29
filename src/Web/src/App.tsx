import { useEffect } from "react";
import { Navigate, Route, Routes } from "react-router-dom";
import { useTranslation } from "react-i18next";

import { GuestRoute, ProtectedRoute } from "./auth/RouteGuards";
import { AppLayout } from "./pages/AppLayout";
import { LoginPage } from "./pages/LoginPage";
import { NotFoundPage } from "./pages/NotFoundPage";
import { RegisterPage } from "./pages/RegisterPage";
import { TenantWorkspacesPage } from "./pages/TenantWorkspacesPage";
import { TenantsPage } from "./pages/TenantsPage";
import { ProjectTasksPage } from "./pages/ProjectTasksPage";
import { WorkspaceProjectsPage } from "./pages/WorkspaceProjectsPage";
import "./App.css";

function App() {
  const { t } = useTranslation("common");

  useEffect(() => {
    document.title = t("app.name");
  }, [t]);

  return (
    <Routes>
      <Route element={<GuestRoute />}>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/register" element={<RegisterPage />} />
      </Route>
      <Route element={<ProtectedRoute />}>
        <Route path="/app" element={<AppLayout />}>
          <Route index element={<TenantsPage />} />
          <Route path="tenants/:tenantId" element={<TenantWorkspacesPage />} />
          <Route
            path="tenants/:tenantId/workspaces/:workspaceId"
            element={<WorkspaceProjectsPage />}
          />
          <Route
            path="tenants/:tenantId/workspaces/:workspaceId/projects/:projectId/*"
            element={<ProjectTasksPage />}
          />
        </Route>
      </Route>
      <Route path="/" element={<Navigate to="/app" replace />} />
      <Route path="*" element={<NotFoundPage />} />
    </Routes>
  );
}

export default App;
