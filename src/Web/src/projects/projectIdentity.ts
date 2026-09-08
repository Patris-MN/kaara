import { workspaceMonogram } from "../workspaces/workspaceMonogram";

export const PROJECT_ACCENT_TOKENS = [
  "indigo",
  "teal",
  "violet",
  "rose",
  "amber",
  "slate",
  "emerald",
  "sky",
] as const;

export type ProjectAccentToken = (typeof PROJECT_ACCENT_TOKENS)[number];

export const DEFAULT_PROJECT_ACCENT: ProjectAccentToken = "indigo";

export function projectMonogram(name: string): string {
  return workspaceMonogram(name);
}

export function resolveProjectAccent(token?: string | null): ProjectAccentToken {
  if (token && PROJECT_ACCENT_TOKENS.includes(token as ProjectAccentToken)) {
    return token as ProjectAccentToken;
  }
  return DEFAULT_PROJECT_ACCENT;
}

export function projectAccentClass(token?: string | null): string {
  return `project-identity project-identity-${resolveProjectAccent(token)}`;
}
