import { formatRelativeTime as formatRelativeTimeValue } from "../time/relativeTime";

export function formatWorkspaceDate(isoDate: string, locale?: string): string {
  const [year, month, day] = isoDate.split("-").map(Number);
  if (!year || !month || !day) {
    return isoDate;
  }

  return new Date(year, month - 1, day).toLocaleDateString(locale, {
    month: "short",
    day: "numeric",
    year: "numeric",
  });
}

export function formatWorkspaceUpdatedAt(iso: string, locale?: string, now = Date.now()): string {
  return formatRelativeTimeValue(iso, locale, now);
}
