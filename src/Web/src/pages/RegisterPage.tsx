import { type FormEvent, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";

import { isApiError } from "../api/errors";
import { useAuth } from "../auth/AuthProvider";
import { AuthDivider, GoogleSignInButton } from "../auth/GoogleSignInButton";
import { isRegistrationPasswordReady, PasswordFieldGroup } from "../auth/PasswordFieldGroup";
import { LanguageSwitcher } from "../components/LanguageSwitcher";
import { Field, TextLink } from "../components/Ui";

export function RegisterPage() {
  const { t } = useTranslation(["auth", "common"]);
  const { register } = useAuth();
  const navigate = useNavigate();
  const [displayName, setDisplayName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [passwordVisible, setPasswordVisible] = useState(false);
  const [confirmVisible, setConfirmVisible] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const passwordReady = isRegistrationPasswordReady(password, confirmPassword);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (submitting || !passwordReady) {
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      await register(email, password, displayName);
      navigate("/login", { replace: true, state: { registered: true } });
    } catch (cause) {
      if (isApiError(cause) && cause.code === "email_already_registered") {
        setError(t("auth:errors.emailTakenSignIn"));
      } else {
        setError(t("auth:registerFailed"));
      }
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main className="login-page register-page">
      <section className="login-pane" aria-labelledby="register-heading">
        <div className="login-pane-inner">
          <header className="login-header">
            <a className="auth-brand" href="/" aria-label={t("auth:brandHome")}>
              <span className="auth-brand-mark" aria-hidden="true">
                <svg viewBox="0 0 32 32">
                  <path d="M7 8.5h8.5V17H7zM16.5 15H25v8.5h-8.5z" />
                  <path d="M15.5 12.75h2.25V15H15.5zM10.2 17h2.25v5.4H17v2.25h-6.8z" />
                </svg>
              </span>
              <span>{t("auth:productName")}</span>
            </a>
            <LanguageSwitcher />
          </header>

          <div className="login-content register-content">
            <div className="login-intro">
              <p className="login-eyebrow">{t("auth:registerEyebrow")}</p>
              <h1 id="register-heading">{t("auth:registerTitle")}</h1>
              <p>{t("auth:registerDescription")}</p>
            </div>

            <form className="login-form register-form" onSubmit={onSubmit} noValidate>
              {error ? (
                <div className="banner banner-error" role="alert">
                  {error}
                  {error === t("auth:errors.emailTakenSignIn") ? (
                    <>
                      {" "}
                      <TextLink to="/login">{t("auth:signIn")}</TextLink>
                    </>
                  ) : null}
                </div>
              ) : null}
              <Field id="displayName" label={t("auth:fullName")}>
                <input
                  id="displayName"
                  name="displayName"
                  type="text"
                  autoComplete="name"
                  placeholder={t("auth:fullNamePlaceholder")}
                  required
                  value={displayName}
                  disabled={submitting}
                  onChange={(event) => setDisplayName(event.target.value)}
                />
              </Field>
              <Field id="email" label={t("auth:businessEmail")}>
                <input
                  id="email"
                  name="email"
                  type="email"
                  autoComplete="email"
                  placeholder={t("auth:emailPlaceholder")}
                  required
                  value={email}
                  disabled={submitting}
                  onChange={(event) => setEmail(event.target.value)}
                />
              </Field>
              <PasswordFieldGroup
                password={password}
                confirmPassword={confirmPassword}
                onPasswordChange={setPassword}
                onConfirmPasswordChange={setConfirmPassword}
                passwordVisible={passwordVisible}
                confirmVisible={confirmVisible}
                onTogglePasswordVisible={() => setPasswordVisible((visible) => !visible)}
                onToggleConfirmVisible={() => setConfirmVisible((visible) => !visible)}
                disabled={submitting}
                showConfirm
              />
              <button className="login-submit" type="submit" disabled={submitting || !passwordReady}>
                {submitting ? t("auth:creatingAccount") : t("auth:register")}
              </button>
              <AuthDivider />
              <GoogleSignInButton label={t("auth:continueWithGoogle")} disabled={submitting} />
              <div className="register-signin">
                <span>{t("auth:alreadyRegistered")}</span>
                <TextLink to="/login">{t("auth:signIn")}</TextLink>
              </div>
            </form>
          </div>
        </div>
      </section>

      <aside className="login-branding register-branding" aria-label={t("auth:brandingLabel")}>
        <div className="branding-grid" />
        <div className="branding-orb branding-orb-one" />
        <div className="branding-orb branding-orb-two" />
        <div className="register-branding-content">
          <div className="register-structure" aria-hidden="true">
            <div className="structure-card structure-card-org">
              <span>01</span>
              <strong>{t("auth:structure.organization")}</strong>
            </div>
            <div className="structure-line" />
            <div className="structure-row">
              <div className="structure-card"><span>02</span><strong>{t("auth:structure.workspace")}</strong></div>
              <div className="structure-card"><span>03</span><strong>{t("auth:structure.team")}</strong></div>
            </div>
            <div className="structure-line structure-line-short" />
            <div className="structure-row">
              <div className="structure-card structure-card-accent"><span>04</span><strong>{t("auth:structure.project")}</strong></div>
              <div className="structure-card structure-card-accent"><span>05</span><strong>{t("auth:structure.tasks")}</strong></div>
            </div>
          </div>
          <div className="branding-copy">
            <p className="branding-kicker">{t("auth:registerBrandingKicker")}</p>
            <h2 className="register-branding-title">{t("auth:registerBrandingTitle")}</h2>
            <p>{t("auth:registerBrandingDescription")}</p>
          </div>
        </div>
      </aside>
    </main>
  );
}
