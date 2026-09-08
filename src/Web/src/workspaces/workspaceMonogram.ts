/** Visual fallback when a workspace has no persisted logo. */
export function workspaceMonogram(name: string): string {
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

export function canManageWorkspace(workspace: { canManage?: boolean }): boolean {
  return workspace.canManage === true;
}

export function canCreateWorkspace(membership: { role?: string } | null): boolean {
  return membership?.role === "Owner" || membership?.role === "Admin";
}
