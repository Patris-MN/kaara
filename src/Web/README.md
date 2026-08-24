# PTS Web (React + TypeScript)

## Phase 1 status

Minimal app shell demonstrating the localization architecture only. No
authentication, tenant, project, or task UI exists yet — see
`docs/architecture/architecture-charter.md`.

## Localization

- Library: [i18next](https://www.i18next.com/) + `react-i18next`, chosen because it
  supports runtime language switching without a page reload and locale resources
  organized as plain JSON per locale/namespace.
- Supported locales: `en` (default/fallback), `ar` (RTL), `ku` — Kurdish Sorani (RTL).
- Resources live in `src/locales/<locale>/<namespace>.json`. Add a new language by
  adding a folder here and registering the locale code in `src/i18n/config.ts` —
  no component code should need to change.
- `src/i18n/LanguageProvider.tsx` is the single place that syncs `<html lang>` and
  `<html dir>` with the active language. Components must never read/write those
  attributes themselves.
- `src/i18n/direction.ts` maps a locale to `ltr`/`rtl` and is unit-tested
  (`npm run test`).

## What is intentionally NOT here yet

- No persistence of the language choice beyond the browser (`localStorage`, via
  `i18next-browser-languagedetector`). Saving it as part of a signed-in user's
  preference is future work, once the Identity module exists.
- No business UI (projects, tasks, dashboards, etc.).

## Scripts

```bash
npm install
npm run dev      # local dev server
npm run build    # tsc -b && vite build
npm run test     # vitest run
npm run lint      # oxlint
```

## Translation quality note

The Arabic and Kurdish Sorani strings in `src/locales/ar` and `src/locales/ku` were
written to establish the resource structure and are reasonable best-effort
translations, but have **not** been reviewed by a native speaker/professional
translator. Treat them as placeholders to validate before shipping to real users.
