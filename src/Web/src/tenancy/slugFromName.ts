const SLUG_MAX = 100;
const FALLBACK_PREFIX = "organization-";

export function slugFromName(name: string): string {
  const latin = latinIdentifier(name);
  if (latin) {
    return latin;
  }

  const trimmed = name.trim();
  if (!trimmed) {
    return "";
  }

  return `${FALLBACK_PREFIX}${stableSuffix(trimmed)}`.slice(0, SLUG_MAX);
}

export function isValidOrganizationIdentifier(value: string): boolean {
  return value.length > 0 && value.length <= SLUG_MAX && /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(value);
}

function latinIdentifier(name: string): string {
  return name
    .trim()
    .toLowerCase()
    .normalize("NFKD")
    .replace(/[\u0300-\u036f]/g, "")
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, SLUG_MAX)
    .replace(/-+$/g, "");
}

function stableSuffix(name: string): string {
  const normalized = name.normalize("NFC").trim().toLowerCase();
  let hash = 0x811c9dc5;
  for (let index = 0; index < normalized.length; index += 1) {
    hash ^= normalized.charCodeAt(index);
    hash = Math.imul(hash, 0x01000193);
  }
  return (hash >>> 0).toString(16).padStart(8, "0");
}
