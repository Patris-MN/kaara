import { type FormEvent, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";

import { isApiError, translationKeyForApiError } from "../api/errors";
import { useAuth } from "../auth/AuthProvider";
import { LanguageSwitcher } from "../components/LanguageSwitcher";
import { Field, StatusBanner, TextLink } from "../components/Ui";

export function RegisterPage() {
  const { t } = useTranslation(["auth", "common"]);
  const { register } = useAuth();
  const navigate = useNavigate();
  const [email, setEmail] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [password, setPassword] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (submitting) {
      return;
    }
    if (password.length < 8) {
      setError(t("auth:errors.passwordTooShort"));
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      await register(email, password, displayName);
      navigate("/login", { replace: true, state: { registered: true } });
    } catch (cause) {
      if (isApiError(cause) && cause.code === "email_already_registered") {
        setError(t("auth:errors.emailTaken"));
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
        <h2>{t("auth:register")}</h2>
        {error ? <StatusBanner tone="error">{error}</StatusBanner> : null}
        <Field id="displayName" label={t("auth:displayName")}>
          <input
            id="displayName"
            name="displayName"
            autoComplete="name"
            required
            value={displayName}
            onChange={(event) => setDisplayName(event.target.value)}
          />
        </Field>
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
            autoComplete="new-password"
            required
            minLength={8}
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
        </Field>
        <button type="submit" disabled={submitting}>
          {submitting ? t("common:loading") : t("auth:register")}
        </button>
        <p>
          <TextLink to="/login">{t("auth:haveAccount")}</TextLink>
        </p>
      </form>
    </div>
  );
}
