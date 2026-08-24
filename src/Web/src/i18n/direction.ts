import { RTL_LOCALES, type SupportedLocale } from "./config";

export type Direction = "ltr" | "rtl";

/**
 * Resolves the document direction for a given locale code. Unknown/unsupported
 * locale strings fall back to "ltr" rather than throwing, since this is called
 * from DOM-syncing code that must never crash the app over a locale mismatch.
 */
export function getDirectionForLocale(locale: string): Direction {
  const normalized = locale.toLowerCase().split("-")[0] ?? locale;
  return RTL_LOCALES.has(normalized as SupportedLocale) ? "rtl" : "ltr";
}
