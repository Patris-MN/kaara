import { type ReactNode, type RefObject, useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import { DialogCloseProvider } from "./DialogCloseContext";

function focusableElements(root: HTMLElement) {
  return [...root.querySelectorAll<HTMLElement>(
    'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
  )].filter((element) => !element.hasAttribute("hidden"));
}

export function Dialog({
  open,
  titleId,
  title,
  closeLabel,
  onClose,
  initialFocusRef,
  size = "default",
  closeOnBackdrop = false,
  isDirty = false,
  children,
}: {
  open: boolean;
  titleId: string;
  title: string;
  closeLabel: string;
  onClose: () => void;
  initialFocusRef?: RefObject<HTMLElement | null>;
  size?: "default" | "compact";
  closeOnBackdrop?: boolean;
  isDirty?: boolean;
  children: ReactNode;
}) {
  const { t } = useTranslation("common");
  const panelRef = useRef<HTMLDivElement>(null);
  const previousFocus = useRef<HTMLElement | null>(null);
  const onCloseRef = useRef(onClose);
  const [discardOpen, setDiscardOpen] = useState(false);

  useEffect(() => {
    onCloseRef.current = onClose;
  }, [onClose]);

  const performClose = useCallback(() => {
    setDiscardOpen(false);
    onCloseRef.current();
  }, []);

  const requestClose = useCallback(() => {
    if (isDirty) {
      setDiscardOpen(true);
      return;
    }
    performClose();
  }, [isDirty, performClose]);

  useEffect(() => {
    if (!open) {
      setDiscardOpen(false);
    }
  }, [open]);

  useEffect(() => {
    if (!open) {
      return;
    }

    previousFocus.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const panel = panelRef.current;
    const requested = initialFocusRef?.current;
    const fallback = panel ? focusableElements(panel)[0] : null;
    const target = requested && panel?.contains(requested) ? requested : fallback;
    target?.focus();

    function onKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        event.preventDefault();
        if (discardOpen) {
          setDiscardOpen(false);
          return;
        }
        requestClose();
        return;
      }

      if (event.key !== "Tab" || !panelRef.current) {
        return;
      }

      const items = focusableElements(panelRef.current);
      if (items.length === 0) {
        return;
      }

      const firstItem = items[0]!;
      const lastItem = items[items.length - 1]!;
      if (event.shiftKey && document.activeElement === firstItem) {
        event.preventDefault();
        lastItem.focus();
      } else if (!event.shiftKey && document.activeElement === lastItem) {
        event.preventDefault();
        firstItem.focus();
      }
    }

    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("keydown", onKeyDown);
      previousFocus.current?.focus();
    };
  }, [open, initialFocusRef, requestClose, discardOpen]);

  if (!open) {
    return null;
  }

  return (
    <div
      className="dialog-backdrop"
      onMouseDown={(event) => {
        if (closeOnBackdrop && event.target === event.currentTarget) {
          requestClose();
        }
      }}
    >
      <div
        ref={panelRef}
        className={size === "compact" ? "dialog-panel dialog-panel-compact" : "dialog-panel"}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        onMouseDown={(event) => event.stopPropagation()}
      >
        <div className="dialog-header">
          <h2 id={titleId}>{title}</h2>
          <button type="button" className="dialog-close" onClick={requestClose} aria-label={closeLabel}>
            ×
          </button>
        </div>
        <div className="dialog-body">
          <DialogCloseProvider requestClose={requestClose}>{children}</DialogCloseProvider>
        </div>
        {discardOpen ? (
          <div className="dialog-discard-overlay" role="alertdialog" aria-labelledby={`${titleId}-discard-title`}>
            <div className="dialog-discard-panel">
              <h3 id={`${titleId}-discard-title`}>{t("discardChangesTitle")}</h3>
              <p>{t("discardChangesBody")}</p>
              <div className="dialog-actions">
                <button type="button" className="secondary-action" onClick={() => setDiscardOpen(false)}>
                  {t("keepEditing")}
                </button>
                <button type="button" className="primary-action" onClick={performClose}>
                  {t("discardChanges")}
                </button>
              </div>
            </div>
          </div>
        ) : null}
      </div>
    </div>
  );
}
