export type OrganizationView = "grid" | "list";

const KEY_PREFIX = "pts.organizationView.";

export function readOrganizationView(userId: string): OrganizationView {
  try {
    const stored = localStorage.getItem(`${KEY_PREFIX}${userId}`);
    return stored === "list" ? "list" : "grid";
  } catch {
    return "grid";
  }
}

export function writeOrganizationView(userId: string, view: OrganizationView): void {
  try {
    localStorage.setItem(`${KEY_PREFIX}${userId}`, view);
  } catch {
    // Preference is UX-only; authorization never depends on it.
  }
}
