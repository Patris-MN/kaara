import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import { getAuthProviders } from "../api/client";

type GoogleSignInButtonProps = {
  label: string;
  disabled?: boolean;
};

export function GoogleSignInButton({ label, disabled = false }: GoogleSignInButtonProps) {
  const { t } = useTranslation("auth");
  const [available, setAvailable] = useState<boolean | null>(null);

  useEffect(() => {
    let cancelled = false;
    void getAuthProviders()
      .then((providers) => {
        if (!cancelled) {
          setAvailable(providers.google.available);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setAvailable(false);
        }
      });
    return () => {
      cancelled = true;
    };
  }, []);

  if (available === null) {
    return null;
  }

  if (!available) {
    if (import.meta.env.PROD) {
      return null;
    }
    return (
      <button
        type="button"
        className="google-auth-button google-auth-button-disabled"
        disabled
        aria-disabled="true"
        aria-label={`${label}. ${t("googleNotConfigured")}`}
      >
        <GoogleMark />
        <span>{label}</span>
        <small className="google-auth-note">{t("googleNotConfigured")}</small>
      </button>
    );
  }

  return (
    <button type="button" className="google-auth-button" disabled={disabled} aria-label={label}>
      <GoogleMark />
      <span>{label}</span>
    </button>
  );
}

function GoogleMark() {
  return (
    <svg className="google-auth-mark" viewBox="0 0 24 24" aria-hidden="true">
      <path d="M21.6 12.227c0-.709-.064-1.227-.205-1.764H12v3.205h5.618a4.64 4.64 0 0 1-2.005 3.045v2.523h3.245c1.9-1.75 2.982-4.318 2.982-7.009Z" fill="#4285F4" />
      <path d="M12 22c2.7 0 4.964-.891 6.618-2.418l-3.245-2.523c-.891.6-2.036.955-3.373.955-2.582 0-4.773-1.745-5.555-4.091H3.273v2.6A10 10 0 0 0 12 22Z" fill="#34A853" />
      <path d="M6.445 13.923A6.04 6.04 0 0 1 6.164 12c0-.664.114-1.309.282-1.923V7.477H3.273A10 10 0 0 0 3 12c0 1.636.391 3.182 1.073 4.523l3.372-2.6Z" fill="#FBBC05" />
      <path d="M12 5.773c1.473 0 2.473.636 3.045 1.164l2.227-2.227C16.955 2.891 14.7 2 12 2 7.873 2 4.236 4.473 3.273 7.477l3.172 2.6C7.227 7.518 9.418 5.773 12 5.773Z" fill="#EA4335" />
    </svg>
  );
}

export function AuthDivider() {
  const { t } = useTranslation("auth");
  return (
    <div className="auth-divider" role="separator" aria-label={t("authDivider")}>
      <span>{t("orContinueWithEmail")}</span>
    </div>
  );
}
