export function emptyGlobalNotificationInbox() {
  return { items: [] as unknown[], unreadCount: 0 };
}

export function globalNotificationInboxResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

export function notificationFetchResponse(path: string) {
  if (path === "/notifications") {
    return globalNotificationInboxResponse(emptyGlobalNotificationInbox());
  }
  if (path.includes("/tenants/") && path.endsWith("/notifications")) {
    return globalNotificationInboxResponse([]);
  }
  return null;
}
