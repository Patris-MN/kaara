import { useTranslation } from "react-i18next";

import { SUPPORTED_LOCALES, type SupportedLocale } from "../i18n/config";

const LANGUAGE_DISPLAY_NAMES: Record<SupportedLocale, string> = {
  en: "English",
  ar: "العربية",
  ku: "کوردیی ناوەندی",
};

function activeLocale(language: string): SupportedLocale {
  const match = SUPPORTED_LOCALES.find(
    (locale) => language === locale || language.startsWith(`${locale}-`),
  );
  return match ?? "en";
}

export function LanguageSwitcher() {
  const { i18n, t } = useTranslation("common");
  const current = activeLocale(i18n.resolvedLanguage ?? i18n.language);

  return (
    <label className="language-switcher">
      <span>{t("language.label")}</span>
      <select
        value={current}
        onChange={(event) => {
          void i18n.changeLanguage(event.target.value);
        }}
        aria-label={t("language.label")}
      >
        {SUPPORTED_LOCALES.map((locale) => (
          <option key={locale} value={locale}>
            {LANGUAGE_DISPLAY_NAMES[locale]}
          </option>
        ))}
      </select>
    </label>
  );
}
