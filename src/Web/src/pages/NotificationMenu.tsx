import { useEffect, useRef, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";

import { listNotifications, markNotificationRead } from "../api/client";
import { isApiError } from "../api/errors";
import { shouldApplyResponse } from "../api/requestIdentity";
import type { WorkNotification } from "../api/types";
import { useAuth } from "../auth/AuthProvider";

export function NotificationMenu() {
  const { t } = useTranslation("notifications");
  const { token } = useAuth();
  const { tenantId } = useParams();
  const navigate = useNavigate();
  const requestId = useRef(0);
  const [open, setOpen] = useState(false);
  const [items, setItems] = useState<WorkNotification[]>([]);

  useEffect(() => {
    if (!token || !tenantId) {
      setItems([]);
      return;
    }

    let cancelled = false;
    async function load() {
      const current = requestId.current + 1;
      requestId.current = current;
      try {
        const next = await listNotifications(token!, tenantId!);
        if (!shouldApplyResponse(current, requestId.current) || cancelled) {
          return;
        }
        setItems(next);
      } catch (cause) {
        if (isApiError(cause) && cause.status === 401) {
          return;
        }
      }
    }

    void load();
    const timer = window.setInterval(() => void load(), 30000);
    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, [token, tenantId]);

  const unread = items.filter((item) => !item.isRead).length;

  async function onOpen(notification: WorkNotification) {
    if (!token || !tenantId) {
      return;
    }
    if (!notification.isRead) {
      try {
        await markNotificationRead(token, tenantId, notification.notificationId);
        setItems((current) =>
          current.map((item) =>
            item.notificationId === notification.notificationId ? { ...item, isRead: true } : item,
          ),
        );
      } catch {
        // listing still works if mark-read fails
      }
    }
    setOpen(false);
    if (notification.workspaceId && notification.projectId) {
      const projectUrl = `/app/tenants/${tenantId}/workspaces/${notification.workspaceId}/projects/${notification.projectId}`;
      navigate(notification.taskId ? `${projectUrl}/tasks/${notification.taskId}` : projectUrl);
    }
  }

  if (!tenantId) {
    return null;
  }

  return (
    <div className="notification-menu">
      <button
        type="button"
        className="notification-trigger"
        aria-expanded={open}
        aria-label={t("title")}
        onClick={() => setOpen((current) => !current)}
      >
        <svg className="app-icon" viewBox="0 0 24 24" aria-hidden="true">
          <path d="M6 17h12l-1.2-1.6V11a4.8 4.8 0 0 0-9.6 0v4.4Zm6 4a2 2 0 0 0 2-2H10a2 2 0 0 0 2 2Z" />
        </svg>
        {unread > 0 ? <span className="notification-count">{unread}</span> : null}
      </button>
      {open ? (
        <div className="notification-panel" role="menu">
          <strong>{t("title")}</strong>
          {items.length === 0 ? (
            <p>{t("empty")}</p>
          ) : (
            <ul>
              {items.map((item) => (
                <li key={item.notificationId}>
                  <button type="button" className={item.isRead ? "" : "notification-unread"} onClick={() => void onOpen(item)}>
                    <span>{t(`types.${item.type}`, { defaultValue: item.type })}</span>
                    {!item.isRead ? <small>{t("markRead")}</small> : null}
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      ) : null}
    </div>
  );
}
