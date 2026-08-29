import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";

import "./i18n";
import "./index.css";
import App from "./App.tsx";
import { AuthProvider } from "./auth/AuthProvider.tsx";
import { LanguageProvider } from "./i18n/LanguageProvider.tsx";
import { TenantDirectoryProvider } from "./tenancy/TenantDirectoryProvider.tsx";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <LanguageProvider>
      <AuthProvider>
        <TenantDirectoryProvider>
          <BrowserRouter>
            <App />
          </BrowserRouter>
        </TenantDirectoryProvider>
      </AuthProvider>
    </LanguageProvider>
  </StrictMode>,
);
