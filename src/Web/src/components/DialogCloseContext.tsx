import { createContext, useContext } from "react";

type DialogCloseContextValue = {
  requestClose: () => void;
};

const DialogCloseContext = createContext<DialogCloseContextValue | null>(null);

export function DialogCloseProvider({
  requestClose,
  children,
}: {
  requestClose: () => void;
  children: React.ReactNode;
}) {
  return (
    <DialogCloseContext.Provider value={{ requestClose }}>{children}</DialogCloseContext.Provider>
  );
}

// oxlint-disable-next-line react/only-export-components
export function useDialogClose(): DialogCloseContextValue {
  const value = useContext(DialogCloseContext);
  if (!value) {
    throw new Error("useDialogClose must be used within Dialog");
  }
  return value;
}

export function useOptionalDialogClose(): DialogCloseContextValue | null {
  return useContext(DialogCloseContext);
}
