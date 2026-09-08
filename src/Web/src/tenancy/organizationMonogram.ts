/** Visual fallback when an organization has no persisted logo. */
export function organizationMonogram(name: string): string {
  const words = name
    .trim()
    .split(/\s+/)
    .filter(Boolean)
    .map((word) => Array.from(word)[0])
    .filter((letter): letter is string => Boolean(letter));

  if (words.length >= 2) {
    return `${words[0]}${words[1]}`.toLocaleUpperCase();
  }

  const letters = Array.from(name.trim());
  if (letters.length >= 2) {
    return `${letters[0]}${letters[1]}`.toLocaleUpperCase();
  }

  return (letters[0] ?? "?").toLocaleUpperCase();
}

export function organizationWorkspaceCount(tenant: { workspaceCount?: number }): number {
  return typeof tenant.workspaceCount === "number" && tenant.workspaceCount >= 0
    ? tenant.workspaceCount
    : 0;
}

/** Server-provided only. Missing data must not grant management controls. */
export function canManageOrganization(tenant: { canManage?: boolean }): boolean {
  return tenant.canManage === true;
}
