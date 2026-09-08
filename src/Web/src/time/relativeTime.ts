/**
 * Shared relative-time formatting for resource and notification surfaces.
 */
export function formatRelativeTime(iso: string, locale?: string, now = Date.now()): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return iso;
  }

  const diffMs = now - date.getTime();
  const minute = 60_000;
  const hour = 60 * minute;
  const day = 24 * hour;

  if (diffMs < minute) {
    return new Intl.RelativeTimeFormat(locale, { numeric: "auto" }).format(0, "minute");
  }
  if (diffMs < hour) {
    const minutes = Math.round(-diffMs / minute);
    return new Intl.RelativeTimeFormat(locale, { numeric: "auto" }).format(minutes, "minute");
  }
  if (diffMs < day) {
    const hours = Math.round(-diffMs / hour);
    return new Intl.RelativeTimeFormat(locale, { numeric: "auto" }).format(hours, "hour");
  }
  if (diffMs < 7 * day) {
    const days = Math.round(-diffMs / day);
    return new Intl.RelativeTimeFormat(locale, { numeric: "auto" }).format(days, "day");
  }

  return new Intl.DateTimeFormat(locale, {
    day: "numeric",
    month: "short",
    year: "numeric",
  }).format(date);
}

export function formatAbsoluteTime(iso: string, locale?: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return iso;
  }

  return new Intl.DateTimeFormat(locale, {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(date);
}
