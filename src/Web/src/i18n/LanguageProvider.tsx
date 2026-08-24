import { useEffect, type ReactNode } from "react";
import { useTranslation } from "react-i18next";

import { getDirectionForLocale } from "./direction";

interface LanguageProviderProps {
  children: ReactNode;
}

/**
 * Keeps the document's `lang` and `dir` attributes in sync with the active
 * i18next language, including on runtime language switches (no page reload).
 *
 * This is intentionally the *only* place localization logic touches the DOM
 * directly, and it has no awareness of tenants, auth, or authorization —
 * localization and tenant/authorization logic must stay separate
 * (see .cursor/rules/60-localization-i18n.mdc).
 */
export function LanguageProvider({ children }: LanguageProviderProps) {
  const { i18n } = useTranslation();

  useEffect(() => {
    const applyDocumentLanguage = (locale: string) => {
      document.documentElement.lang = locale;
      document.documentElement.dir = getDirectionForLocale(locale);
    };

    applyDocumentLanguage(i18n.language);
    i18n.on("languageChanged", applyDocumentLanguage);

    return () => {
      i18n.off("languageChanged", applyDocumentLanguage);
    };
  }, [i18n]);

  return children;
}
