import { type FormEvent, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";

import { isApiError, translationKeyForApiError } from "../api/errors";
import { useAuth } from "../auth/AuthProvider";
import { LanguageSwitcher } from "../components/LanguageSwitcher";
import { Field, StatusBanner, TextLink } from "../components/Ui";

export function LoginPage() {
  const { t } = useTranslation(["auth", "common"]);
  const { login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (submitting) {
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      await login(email, password);
      const from = (location.state as { from?: string } | null)?.from;
      navigate(from && from.startsWith("/app") ? from : "/app", { replace: true });
    } catch (cause) {
      if (isApiError(cause) && (cause.code === "invalid_credentials" || cause.status === 401)) {
        setError(t("auth:errors.invalidCredentials"));
      } else {
        setError(t(translationKeyForApiError(cause), { ns: "common" }));
      }
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="auth-page">
      <header className="auth-header">
        <h1>{t("common:app.name")}</h1>
        <LanguageSwitcher />
      </header>
      <form className="panel" onSubmit={onSubmit} noValidate>
        <h2>{t("auth:signIn")}</h2>
        {error ? <StatusBanner tone="error">{error}</StatusBanner> : null}
        <Field id="email" label={t("auth:email")}>
          <input
            id="email"
            name="email"
            type="email"
            autoComplete="username"
            required
            value={email}
            onChange={(event) => setEmail(event.target.value)}
          />
        </Field>
        <Field id="password" label={t("auth:password")}>
          <input
            id="password"
            name="password"
            type="password"
            autoComplete="current-password"
            required
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
        </Field>
        <button type="submit" disabled={submitting}>
          {submitting ? t("common:loading") : t("auth:signIn")}
        </button>
        <p>
          <TextLink to="/register">{t("auth:needAccount")}</TextLink>
        </p>
      </form>
    </div>
  );
}
