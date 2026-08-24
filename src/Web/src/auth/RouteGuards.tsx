import { Navigate, Outlet, useLocation } from "react-router-dom";
import { useTranslation } from "react-i18next";

import { useAuth } from "../auth/AuthProvider";

export function ProtectedRoute() {
  const { user, isLoading } = useAuth();
  const location = useLocation();
  const { t } = useTranslation("common");

  if (isLoading) {
    return <p role="status">{t("loading")}</p>;
  }

  if (!user) {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />;
  }

  return <Outlet />;
}

export function GuestRoute() {
  const { user, isLoading } = useAuth();
  const { t } = useTranslation("common");

  if (isLoading) {
    return <p role="status">{t("loading")}</p>;
  }

  if (user) {
    return <Navigate to="/app" replace />;
  }

  return <Outlet />;
}
