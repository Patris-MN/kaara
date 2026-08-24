const TOKEN_KEY = "pts.accessToken";
const TENANT_KEY_PREFIX = "pts.selectedTenantId.";

export function readAccessToken(): string | null {
  return sessionStorage.getItem(TOKEN_KEY);
}

export function writeAccessToken(token: string): void {
  sessionStorage.setItem(TOKEN_KEY, token);
}

export function clearAccessToken(): void {
  sessionStorage.removeItem(TOKEN_KEY);
}

export function readSelectedTenantId(userId: string): string | null {
  return sessionStorage.getItem(`${TENANT_KEY_PREFIX}${userId}`);
}

export function writeSelectedTenantId(userId: string, tenantId: string): void {
  sessionStorage.setItem(`${TENANT_KEY_PREFIX}${userId}`, tenantId);
}

export function clearSelectedTenantId(userId: string): void {
  sessionStorage.removeItem(`${TENANT_KEY_PREFIX}${userId}`);
}

export function clearSession(): void {
  const keys: string[] = [];
  for (let index = 0; index < sessionStorage.length; index += 1) {
    const key = sessionStorage.key(index);
    if (key?.startsWith("pts.")) {
      keys.push(key);
    }
  }
  for (const key of keys) {
    sessionStorage.removeItem(key);
  }
}
