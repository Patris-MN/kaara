import type { AccountCapabilities } from "../api/types";

export function canCreateOrganization(
  capabilities: AccountCapabilities | null | undefined,
): boolean {
  return capabilities?.canCreateOrganization === true;
}
