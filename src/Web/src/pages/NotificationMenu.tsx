import { useCallback, useEffect, useId, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";

import {
  acceptInvitation,
  listGlobalNotifications,
  markGlobalNotificationRead,
} from "../api/client";
import { isApiError, translationKeyForApiError } from "../api/errors";
import { shouldApplyResponse } from "../api/requestIdentity";
import { writeSelectedTenantId } from "../api/session";
import type { GlobalNotification } from "../api/types";
import { useAuth } from "../auth/AuthProvider";
import {
  formatNotificationBadgeCount,
  notificationHeaderLine,
  notificationMessage,
  notificationMetaLine,
} from "../notifications/presentation";
import { useTenantDirectory } from "../tenancy/TenantDirectoryProvider";
import { formatAbsoluteTime, formatRelativeTime } from "../time/relativeTime";

type InboxLoadState = "loading" | "ready" | "error";

export function NotificationMenu() {
  const { t, i18n } = useTranslation(["notifications", "tenants", "common"]);
  const { token, user } = useAuth();
  const { invitations, markInvitationAccepted, refresh } = useTenantDirectory();
  const navigate = useNavigate();
  const requestId = useRef(0);
  const rootRef = useRef<HTMLDivElement>(null);
  const panelId = useId();
  const [open, setOpen] = useState(false);
  const [items, setItems] = useState<GlobalNotification[]>([]);
  const [unreadCount, setUnreadCount] = useState(0);
  const [loadState, setLoadState] = useState<InboxLoadState>("loading");
  const [busy, setBusy] = useState(false);
  const [acceptError, setAcceptError] = useState<string | null>(null);

  const loadInbox = useCallback(async () => {
    if (!token) {
      setItems([]);
      setUnreadCount(0);
      setLoadState("ready");
      return;
    }

    const current = requestId.current + 1;
    requestId.current = current;
    setLoadState((state) => (state === "ready" ? state : "loading"));

    try {
      const inbox = await listGlobalNotifications(token);
      if (!shouldApplyResponse(current, requestId.current)) {
        return;
      }
      setItems(inbox.items ?? []);
      setUnreadCount(inbox.unreadCount ?? 0);
      setLoadState("ready");
    } catch (cause) {
      if (isApiError(cause) && cause.status === 401) {
        return;
      }
      if (!shouldApplyResponse(current, requestId.current)) {
        return;
      }
      setLoadState("error");
    }
  }, [token]);

  useEffect(() => {
    if (!token) {
      requestId.current += 1;
      queueMicrotask(() => {
        setItems([]);
        setUnreadCount(0);
        setLoadState("ready");
      });
      return;
    }

    const activeToken = token;
    let active = true;

    async function refreshInbox() {
      const current = requestId.current + 1;
      requestId.current = current;
      try {
        const inbox = await listGlobalNotifications(activeToken);
        if (!active || !shouldApplyResponse(current, requestId.current)) {
          return;
        }
        setItems(inbox.items ?? []);
        setUnreadCount(inbox.unreadCount ?? 0);
        setLoadState("ready");
      } catch (cause) {
        if (!active) {
          return;
        }
        if (isApiError(cause) && cause.status === 401) {
          return;
        }
        if (!shouldApplyResponse(current, requestId.current)) {
          return;
        }
        setLoadState("error");
      }
    }

    void refreshInbox();
    const timer = window.setInterval(() => void refreshInbox(), 30000);
    return () => {
      active = false;
      window.clearInterval(timer);
    };
  }, [token]);

  useEffect(() => {
    if (!open) {
      return;
    }

    function onPointerDown(event: MouseEvent) {
      if (rootRef.current && !rootRef.current.contains(event.target as Node)) {
        setOpen(false);
      }
    }

    function onKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        setOpen(false);
      }
    }

    document.addEventListener("mousedown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("mousedown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

  const badgeCount = unreadCount + invitations.length;
  const badgeLabel = formatNotificationBadgeCount(badgeCount);
  const triggerLabel =
    invitations.length > 0
      ? t("notifications:attentionWithInvitations", { count: invitations.length })
      : unreadCount > 0
        ? t("notifications:attentionWithUnread", { count: unreadCount })
        : t("notifications:title");

  async function onOpenNotification(notification: GlobalNotification) {
    if (!token || !user) {
      return;
    }

    if (!notification.isRead) {
      try {
        await markGlobalNotificationRead(token, notification.notificationId);
        setItems((current) =>
          current.map((item) =>
            item.notificationId === notification.notificationId ? { ...item, isRead: true } : item,
          ),
        );
        setUnreadCount((current) => Math.max(0, current - 1));
      } catch {
        // navigation still proceeds if mark-read fails
      }
    }

    setOpen(false);
    writeSelectedTenantId(user.userId, notification.tenantId);

    if (!notification.targetAvailable || !notification.workspaceId || !notification.projectId) {
      navigate(`/app/tenants/${notification.tenantId}`, {
        state: { notice: "notificationTargetUnavailable" },
      });
      return;
    }

    const projectUrl = `/app/tenants/${notification.tenantId}/workspaces/${notification.workspaceId}/projects/${notification.projectId}`;
    navigate(notification.taskId ? `${projectUrl}/tasks/${notification.taskId}` : projectUrl);
  }

  async function onAccept(invitationTenantId: string) {
    if (!token || !user || busy) {
      return;
    }
    setBusy(true);
    setAcceptError(null);
    try {
      await acceptInvitation(token, invitationTenantId);
      writeSelectedTenantId(user.userId, invitationTenantId);
      markInvitationAccepted(invitationTenantId);
      await refresh();
      setOpen(false);
      navigate(`/app/tenants/${invitationTenantId}`, { state: { notice: "invitationAccepted" } });
    } catch (cause) {
      setAcceptError(t(translationKeyForApiError(cause), { ns: "common" }));
    } finally {
      setBusy(false);
    }
  }

  function roleLabel(role: string) {
    return t(`tenants:roles.${role}`, { defaultValue: role });
  }

  return (
    <div className="notification-menu" ref={rootRef}>
      <button
        type="button"
        className="notification-trigger"
        aria-expanded={open}
        aria-haspopup="true"
        aria-controls={open ? panelId : undefined}
        aria-label={triggerLabel}
        onClick={() => setOpen((current) => !current)}
      >
        <svg className="app-icon" viewBox="0 0 24 24" aria-hidden="true">
          <path d="M6 17h12l-1.2-1.6V11a4.8 4.8 0 0 0-9.6 0v4.4Zm6 4a2 2 0 0 0 2-2H10a2 2 0 0 0 2 2Z" />
        </svg>
        {badgeLabel ? (
          <span className="notification-count" aria-hidden="true">
            {badgeLabel}
          </span>
        ) : null}
      </button>
      {open ? (
        <div
          className="notification-panel"
          id={panelId}
          role="region"
          aria-label={t("notifications:title")}
        >
          {invitations.length > 0 ? (
            <section className="attention-section">
              <strong>{t("notifications:pendingInvitations")}</strong>
              {acceptError ? <p role="alert">{acceptError}</p> : null}
              <ul>
                {invitations.map((invitation) => (
                  <li className="attention-invitation" key={invitation.tenantId}>
                    <span>
                      <strong>{invitation.name}</strong>
                      <small>{t("tenants:invitedRole", { role: roleLabel(invitation.role) })}</small>
                    </span>
                    <button
                      className="secondary-action"
                      type="button"
                      disabled={busy}
                      onClick={() => void onAccept(invitation.tenantId)}
                    >
                      {t("tenants:accept")}
                    </button>
                  </li>
                ))}
              </ul>
            </section>
          ) : null}
          <section className="attention-section">
            <div className="notification-panel-header">
              <strong>{t("notifications:title")}</strong>
              {unreadCount > 0 ? (
                <span className="notification-unread-summary">
                  {t("notifications:unreadSummary", { count: unreadCount })}
                </span>
              ) : null}
            </div>
            {loadState === "loading" ? (
              <p className="notification-panel-status">{t("common:loading")}</p>
            ) : null}
            {loadState === "error" ? (
              <div className="notification-panel-status notification-panel-error">
                <p>{t("notifications:loadErrorTitle")}</p>
                <button className="secondary-action" type="button" onClick={() => void loadInbox()}>
                  {t("notifications:retryLoad")}
                </button>
              </div>
            ) : null}
            {loadState === "ready" && items.length === 0 ? (
              <div className="notification-empty">
                <p className="notification-empty-title">{t("notifications:emptyTitle")}</p>
                <p>{t("notifications:emptyBody")}</p>
              </div>
            ) : null}
            {loadState === "ready" && items.length > 0 ? (
              <ul className="notification-list">
                {items.map((item) => {
                  const timeLabel = formatRelativeTime(item.createdAtUtc, i18n.language);
                  const absoluteTime = formatAbsoluteTime(item.createdAtUtc, i18n.language);
                  return (
                    <li key={item.notificationId}>
                      <button
                        type="button"
                        className={`notification-item${item.isRead ? " notification-item-read" : " notification-item-unread"}`}
                        onClick={() => void onOpenNotification(item)}
                      >
                        {!item.isRead ? (
                          <span className="notification-unread-dot" aria-hidden="true" />
                        ) : null}
                        <span className="notification-item-body">
                          <span className="notification-item-header">
                            {notificationHeaderLine(item, t)}
                          </span>
                          <span className="notification-item-message">
                            {notificationMessage(item, t)}
                          </span>
                          <span className="notification-item-meta">
                            <time dateTime={item.createdAtUtc} title={absoluteTime}>
                              {notificationMetaLine(item, timeLabel, t)}
                            </time>
                          </span>
                        </span>
                      </button>
                    </li>
                  );
                })}
              </ul>
            ) : null}
          </section>
        </div>
      ) : null}
    </div>
  );
}
