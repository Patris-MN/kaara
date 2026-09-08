/**
 * Builds a browser-usable invitation URL from a backend path or raw token.
 * Uses VITE_APP_ORIGIN when configured; otherwise falls back to window.location.origin.
 */
export function buildPublicInvitationUrl(pathOrToken: string): string {
  const configuredOrigin = import.meta.env.VITE_APP_ORIGIN?.trim();
  const origin =
    configuredOrigin ||
    (typeof window !== "undefined" ? window.location.origin : "");

  const path = pathOrToken.startsWith("/invite/")
    ? pathOrToken
    : `/invite/${encodeURIComponent(pathOrToken)}`;

  return `${origin.replace(/\/$/, "")}${path}`;
}

export function normalizeInvitationLink(url: string | null | undefined): string | null {
  if (!url) {
    return null;
  }
  if (url.startsWith("http://") || url.startsWith("https://")) {
    return url;
  }
  return buildPublicInvitationUrl(url);
}

export async function copyInvitationLink(url: string): Promise<boolean> {
  if (typeof navigator === "undefined" || !navigator.clipboard?.writeText) {
    return false;
  }
  try {
    await navigator.clipboard.writeText(url);
    return true;
  } catch {
    return false;
  }
}
