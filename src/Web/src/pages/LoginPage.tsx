import { type FormEvent, useId, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";

import { isApiError, translationKeyForApiError } from "../api/errors";
import { useAuth } from "../auth/AuthProvider";
import { LanguageSwitcher } from "../components/LanguageSwitcher";
import { Field, StatusBanner, TextLink } from "../components/Ui";

function ProductMark() {
  return (
    <span className="auth-brand-mark" aria-hidden="true">
      <svg viewBox="0 0 32 32" role="img">
        <path d="M7 8.5h8.5V17H7zM16.5 15H25v8.5h-8.5z" />
        <path d="M15.5 12.75h2.25V15H15.5zM10.2 17h2.25v5.4H17v2.25h-6.8z" />
      </svg>
    </span>
  );
}

function EyeIcon({ visible }: { visible: boolean }) {
  return (
    <svg viewBox="0 0 24 24" aria-hidden="true">
      {visible ? (
        <>
          <path d="M3 12s3.25-5 9-5 9 5 9 5-3.25 5-9 5-9-5-9-5Z" />
          <circle cx="12" cy="12" r="2.25" />
        </>
      ) : (
        <>
          <path d="M4.5 4.5 19.5 19.5M10.6 7.15A9.4 9.4 0 0 1 12 7c5.75 0 9 5 9 5a12.7 12.7 0 0 1-2.2 2.65M8.25 8.25C4.9 9.65 3 12 3 12s3.25 5 9 5c1.05 0 2-.17 2.85-.45M10.4 10.4a2.25 2.25 0 0 0 3.2 3.2" />
        </>
      )}
    </svg>
  );
}

function WorkflowGraphic() {
  return (
    <div className="auth-workflow" aria-hidden="true">
      <svg viewBox="0 0 620 440">
        <defs>
          <linearGradient id="workflow-line" x1="0" y1="0" x2="1" y2="1">
            <stop offset="0" stopColor="#7c8cff" />
            <stop offset="1" stopColor="#35d2c4" />
          </linearGradient>
        </defs>
        <path className="workflow-path" d="M78 220H195c30 0 30-72 60-72h75c30 0 30 72 60 72h150" />
        <path className="workflow-path workflow-path-muted" d="M195 220v116h195V220" />
        <path className="workflow-path workflow-path-muted" d="M255 148V72h135v148" />
        <g className="workflow-node">
          <rect x="38" y="180" width="80" height="80" rx="20" />
          <path d="M60 222h36M66 210h24M66 234h24" />
        </g>
        <g className="workflow-node workflow-node-accent">
          <rect x="170" y="195" width="50" height="50" rx="15" />
          <path d="m186 220 8 8 13-17" />
        </g>
        <g className="workflow-node">
          <rect x="225" y="112" width="60" height="60" rx="17" />
          <circle cx="255" cy="137" r="7" />
          <path d="M241 157c3-7 25-7 28 0" />
        </g>
        <g className="workflow-node">
          <rect x="360" y="42" width="60" height="60" rx="17" />
          <path d="M377 62h26M377 73h19M377 84h23" />
        </g>
        <g className="workflow-node workflow-node-accent">
          <rect x="360" y="190" width="60" height="60" rx="17" />
          <path d="M378 207h24v26h-24zM384 201v10M396 201v10" />
        </g>
        <g className="workflow-node">
          <rect x="510" y="180" width="80" height="80" rx="20" />
          <path d="M532 204h36v32h-36zM532 214h36M543 204v-8h14v8" />
        </g>
        <g className="workflow-node workflow-node-small">
          <rect x="170" y="311" width="50" height="50" rx="15" />
          <path d="M185 326h20M185 336h14M185 346h17" />
        </g>
        <g className="workflow-node workflow-node-small">
          <rect x="365" y="311" width="50" height="50" rx="15" />
          <path d="m380 336 7 7 12-15" />
        </g>
      </svg>
    </div>
  );
}

export function LoginPage() {
  const { t } = useTranslation(["auth", "common"]);
  const { login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const formErrorId = useId();
  const rememberedEmail = localStorage.getItem("pts.rememberedEmail") ?? "";
  const [email, setEmail] = useState(rememberedEmail);
  const [password, setPassword] = useState("");
  const [rememberMe, setRememberMe] = useState(Boolean(rememberedEmail));
  const [passwordVisible, setPasswordVisible] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [emailError, setEmailError] = useState<string | null>(null);
  const [passwordError, setPasswordError] = useState<string | null>(null);
  const [recoveryNotice, setRecoveryNotice] = useState(false);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (submitting) {
      return;
    }

    const normalizedEmail = email.trim();
    const nextEmailError = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(normalizedEmail)
      ? null
      : t("auth:errors.invalidEmail");
    const nextPasswordError = password.length > 0 ? null : t("auth:errors.passwordRequired");
    setEmailError(nextEmailError);
    setPasswordError(nextPasswordError);
    if (nextEmailError || nextPasswordError) {
      return;
    }

    setSubmitting(true);
    setError(null);
    setRecoveryNotice(false);
    try {
      await login(normalizedEmail, password);
      if (rememberMe) {
        localStorage.setItem("pts.rememberedEmail", normalizedEmail);
      } else {
        localStorage.removeItem("pts.rememberedEmail");
      }
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
    <main className="login-page">
      <section className="login-pane" aria-labelledby="login-heading">
        <div className="login-pane-inner">
          <header className="login-header">
            <a className="auth-brand" href="/" aria-label={t("auth:brandHome")}>
              <ProductMark />
              <span>{t("auth:productName")}</span>
            </a>
            <LanguageSwitcher />
          </header>

          <div className="login-content">
            <div className="login-intro">
              <p className="login-eyebrow">{t("auth:welcome")}</p>
              <h1 id="login-heading">{t("auth:productName")}</h1>
              <p>{t("auth:description")}</p>
            </div>

            <form className="login-form" onSubmit={onSubmit} noValidate>
              <div className="login-form-heading">
                <h2>{t("auth:signIn")}</h2>
                <p>{t("auth:signInHint")}</p>
              </div>

              {error ? (
                <div id={formErrorId}>
                  <StatusBanner tone="error">{error}</StatusBanner>
                </div>
              ) : null}
              {recoveryNotice ? (
                <StatusBanner tone="info">{t("auth:recoveryNotice")}</StatusBanner>
              ) : null}

              <Field id="email" label={t("auth:businessEmail")} error={emailError ?? undefined}>
                <input
                  id="email"
                  name="email"
                  type="email"
                  inputMode="email"
                  autoComplete="username"
                  placeholder={t("auth:emailPlaceholder")}
                  required
                  aria-invalid={emailError ? "true" : undefined}
                  aria-describedby={emailError ? "email-error" : error ? formErrorId : undefined}
                  value={email}
                  onChange={(event) => {
                    setEmail(event.target.value);
                    if (emailError) setEmailError(null);
                  }}
                />
              </Field>

              <Field id="password" label={t("auth:password")} error={passwordError ?? undefined}>
                <div className="password-input">
                  <input
                    id="password"
                    name="password"
                    type={passwordVisible ? "text" : "password"}
                    autoComplete="current-password"
                    placeholder={t("auth:passwordPlaceholder")}
                    required
                    aria-invalid={passwordError ? "true" : undefined}
                    aria-describedby={passwordError ? "password-error" : error ? formErrorId : undefined}
                    value={password}
                    onChange={(event) => {
                      setPassword(event.target.value);
                      if (passwordError) setPasswordError(null);
                    }}
                  />
                  <button
                    className="password-toggle"
                    type="button"
                    aria-label={passwordVisible ? t("auth:hidePassword") : t("auth:showPassword")}
                    aria-pressed={passwordVisible}
                    onClick={() => setPasswordVisible((visible) => !visible)}
                  >
                    <EyeIcon visible={passwordVisible} />
                  </button>
                </div>
              </Field>

              <div className="login-options">
                <label className="remember-option">
                  <input
                    type="checkbox"
                    checked={rememberMe}
                    onChange={(event) => setRememberMe(event.target.checked)}
                  />
                  <span>{t("auth:rememberMe")}</span>
                </label>
                <button
                  className="forgot-password"
                  type="button"
                  onClick={() => {
                    setError(null);
                    setRecoveryNotice(true);
                  }}
                >
                  {t("auth:forgotPassword")}
                </button>
              </div>

              <button className="login-submit" type="submit" disabled={submitting}>
                {submitting ? (
                  <>
                    <span className="button-spinner" aria-hidden="true" />
                    {t("auth:signingIn")}
                  </>
                ) : (
                  t("auth:signIn")
                )}
              </button>

              <div className="login-divider" aria-hidden="true">
                <span>{t("auth:newToProduct")}</span>
              </div>
              <TextLink to="/register">{t("auth:createAccount")}</TextLink>
            </form>

            <p className="login-security-note">
              <svg viewBox="0 0 24 24" aria-hidden="true">
                <path d="M6.5 10V7.5a5.5 5.5 0 0 1 11 0V10M5 10h14v10H5z" />
              </svg>
              {t("auth:securityNote")}
            </p>
          </div>
        </div>
      </section>

      <aside className="login-branding" aria-label={t("auth:brandingLabel")}>
        <div className="branding-grid" />
        <div className="branding-orb branding-orb-one" />
        <div className="branding-orb branding-orb-two" />
        <div className="branding-content">
          <WorkflowGraphic />
          <div className="branding-copy">
            <p className="branding-kicker">{t("auth:brandingKicker")}</p>
            <h2>
              <span>{t("auth:brandingMessage.plan")}</span>
              <span>{t("auth:brandingMessage.collaborate")}</span>
              <span className="branding-gradient">{t("auth:brandingMessage.deliver")}</span>
            </h2>
            <p>{t("auth:brandingDescription")}</p>
          </div>
          <div className="branding-trust">
            <span>{t("auth:trust.organization")}</span>
            <span>{t("auth:trust.workspaces")}</span>
            <span>{t("auth:trust.teams")}</span>
          </div>
        </div>
      </aside>
    </main>
  );
}
