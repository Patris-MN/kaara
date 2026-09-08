import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import "../i18n";
import App from "../App";
import { AuthProvider } from "../auth/AuthProvider";
import { FeedbackProvider } from "../feedback/FeedbackProvider";
import { RegisterPage } from "../pages/RegisterPage";
import enAuth from "../locales/en/auth.json";
import enInvitations from "../locales/en/invitations.json";
import arAuth from "../locales/ar/auth.json";
import kuAuth from "../locales/ku/auth.json";
import i18n from "../i18n";
import { TenantDirectoryProvider } from "../tenancy/TenantDirectoryProvider";

const preview = {
  organizationName: "Patris",
  invitedEmail: "ali@company.com",
  role: "Member",
  expiresAtUtc: "2026-09-08T00:00:00Z",
  inviterDisplayName: "Owner User",
  requiresRegistration: true,
  workspaceGrants: [{ workspaceName: "Desk", accessLevel: "Edit" }],
};

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function pathOf(input: RequestInfo | URL) {
  const raw = String(input);
  return raw.startsWith("http") ? new URL(raw).pathname : raw;
}

describe("phase 8.1.3C auth input contrast", () => {
  afterEach(async () => {
    vi.unstubAllGlobals();
    cleanup();
    await i18n.changeLanguage("en");
    document.documentElement.dir = "ltr";
  });

  it("keeps typed display name value accessible on the normal registration page", async () => {
    const user = userEvent.setup();
    vi.stubGlobal("fetch", vi.fn(async () => json({})));
    render(
      <MemoryRouter>
        <FeedbackProvider>
          <AuthProvider>
            <RegisterPage />
          </AuthProvider>
        </FeedbackProvider>
      </MemoryRouter>,
    );

    const displayName = screen.getByLabelText(enAuth.fullName);
    expect(displayName).toHaveProperty("type", "text");
    await user.type(displayName, "Mohammad Haydar");
    expect(screen.getByDisplayValue("Mohammad Haydar")).toBeTruthy();
    expect(screen.getByLabelText(enAuth.confirmPassword)).toBeTruthy();
  });

  it("keeps typed display name value accessible on invitation registration", async () => {
    const user = userEvent.setup();
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const path = pathOf(input);
        if (path === "/invite/token-abc") {
          return json(preview);
        }
        return json({ error: "missing" }, 404);
      }),
    );

    render(
      <MemoryRouter initialEntries={["/invite/token-abc"]}>
        <AuthProvider>
          <TenantDirectoryProvider>
            <App />
          </TenantDirectoryProvider>
        </AuthProvider>
      </MemoryRouter>,
    );

    const emailField = await screen.findByLabelText(enInvitations.invitedEmail);
    expect(emailField).toHaveProperty("readOnly", true);
    expect(screen.getByDisplayValue("ali@company.com")).toBeTruthy();

    const displayName = screen.getByLabelText(enAuth.displayName);
    expect(displayName).toHaveProperty("type", "text");
    await user.type(displayName, "Test User");
    expect(screen.getByDisplayValue("Test User")).toBeTruthy();
  });

  it.each([
    ["ar", arAuth.fullName, "محمد"],
    ["ku", kuAuth.fullName, "تاقیکردنەوە"],
  ])("accepts localized display names on registration (%s)", async (locale, label, value) => {
    await i18n.changeLanguage(locale);
    document.documentElement.dir = locale === "ar" || locale === "ku" ? "rtl" : "ltr";
    const user = userEvent.setup();
    vi.stubGlobal("fetch", vi.fn(async () => json({})));
    render(
      <MemoryRouter>
        <FeedbackProvider>
          <AuthProvider>
            <RegisterPage />
          </AuthProvider>
        </FeedbackProvider>
      </MemoryRouter>,
    );

    const displayName = screen.getByLabelText(label);
    await user.type(displayName, value);
    expect(screen.getByDisplayValue(value)).toBeTruthy();
  });
});
