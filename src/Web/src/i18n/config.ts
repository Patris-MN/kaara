/**
 * Single source of truth for which locales this application supports.
 * Adding a new language later means adding a folder under `src/locales/<code>/`
 * and appending it here — no application code should need to change.
 */
export const SUPPORTED_LOCALES = ["en", "ar", "ku"] as const;

export type SupportedLocale = (typeof SUPPORTED_LOCALES)[number];

/** English is always the fallback language (see architecture charter). */
export const DEFAULT_LOCALE: SupportedLocale = "en";

/** Locales that must render with a right-to-left document direction. */
export const RTL_LOCALES: ReadonlySet<SupportedLocale> = new Set(["ar", "ku"]);

/**
 * Translation resource namespaces. Keep namespaces small and topic-scoped
 * (common, navigation, auth, ...) rather than one giant catalogue per locale.
 */
export const NAMESPACES = [
  "common",
  "navigation",
  "auth",
  "tenants",
  "workspaces",
  "projects",
  "members",
  "tasks",
  "notifications",
] as const;

export type Namespace = (typeof NAMESPACES)[number];

export function isSupportedLocale(value: string): value is SupportedLocale {
  return (SUPPORTED_LOCALES as readonly string[]).includes(value);
}
