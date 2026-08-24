import { Link } from "react-router-dom";
import { useTranslation } from "react-i18next";

export function NotFoundPage() {
  const { t } = useTranslation("common");
  return (
    <section className="auth-page">
      <h1>{t("notFound.title")}</h1>
      <p>{t("notFound.body")}</p>
      <p>
        <Link to="/app">{t("notFound.home")}</Link>
      </p>
    </section>
  );
}
