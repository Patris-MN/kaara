import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { useTranslation } from "react-i18next";

import { useAuth } from "../auth/AuthProvider";
import { UserAvatar } from "../components/UserAvatar";

type UserMenuProps = {
  onSignOut: () => void;
};

export function UserMenu({ onSignOut }: UserMenuProps) {
  const { t } = useTranslation(["profile", "auth"]);
  const { user } = useAuth();
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);

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

  const displayName = user?.displayName ?? user?.email ?? "";

  return (
    <div className="user-menu" ref={rootRef}>
      <button
        type="button"
        className="user-menu-trigger"
        aria-expanded={open}
        aria-haspopup="menu"
        onClick={() => setOpen((current) => !current)}
      >
        <UserAvatar displayName={displayName} email={user?.email ?? ""} className="user-menu-avatar" />
      </button>
      {open ? (
        <div className="user-menu-panel" role="menu">
          <div className="user-menu-header">
            <strong>{displayName}</strong>
            <span>{user?.email}</span>
          </div>
          <Link className="user-menu-item" role="menuitem" to="/app/profile" onClick={() => setOpen(false)}>
            {t("profile:myProfile")}
          </Link>
          <button type="button" className="user-menu-item user-menu-signout" role="menuitem" onClick={onSignOut}>
            {t("auth:signOut")}
          </button>
        </div>
      ) : null}
    </div>
  );
}
