import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from "react";

import { ActionFeedbackList } from "../components/ActionFeedback";

export type FeedbackTone = "success" | "error";

export type FeedbackItem = {
  id: string;
  tone: FeedbackTone;
  title: string;
  body?: string;
};

type ShowFeedbackInput = {
  tone: FeedbackTone;
  title: string;
  body?: string;
};

type FeedbackApi = {
  show: (input: ShowFeedbackInput) => string;
  dismiss: (id: string) => void;
};

const FeedbackContext = createContext<FeedbackApi | null>(null);

export function FeedbackProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<FeedbackItem[]>([]);

  const dismiss = useCallback((id: string) => {
    setItems((current) => current.filter((item) => item.id !== id));
  }, []);

  const show = useCallback((input: ShowFeedbackInput) => {
    const id =
      typeof crypto !== "undefined" && "randomUUID" in crypto
        ? crypto.randomUUID()
        : `feedback-${Date.now()}-${Math.random().toString(16).slice(2)}`;
    setItems((current) => [{ id, ...input }, ...current].slice(0, 3));
    return id;
  }, []);

  const value = useMemo(() => ({ show, dismiss }), [show, dismiss]);

  return (
    <FeedbackContext.Provider value={value}>
      {children}
      <ActionFeedbackList items={items} onDismiss={dismiss} />
    </FeedbackContext.Provider>
  );
}

// oxlint-disable-next-line react/only-export-components
export function useFeedback(): FeedbackApi {
  const value = useContext(FeedbackContext);
  if (!value) {
    throw new Error("useFeedback must be used within FeedbackProvider");
  }
  return value;
}
