import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import "../i18n";
import { AuthProvider } from "../auth/AuthProvider";
import App from "../App";
import { clearSession } from "./session";
import { getDirectionForLocale } from "../i18n/direction";
import { TenantDirectoryProvider } from "../tenancy/TenantDirectoryProvider";
import enAuth from "../locales/en/auth.json";
import arAuth from "../locales/ar/auth.json";
import kuAuth from "../locales/ku/auth.json";
import enTenants from "../locales/en/tenants.json";
import arTenants from "../locales/ar/tenants.json";
import kuTenants from "../locales/ku/tenants.json";

function renderApp(path = "/login") {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <AuthProvider>
        <TenantDirectoryProvider>
          <App />
        </TenantDirectoryProvider>
      </AuthProvider>
    </MemoryRouter>,
  );
}

describe("frontend vertical slice", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    clearSession();
    localStorage.removeItem("pts.rememberedEmail");
    cleanup();
  });

  it("sends anonymous users to login", async () => {
    renderApp("/app");
    expect(await screen.findByRole("heading", { name: enAuth.signIn })).toBeTruthy();
  });

  it("shows an invalid-credentials error without entering the app", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ error: "invalid_credentials" }), { status: 401 }),
      ),
    );
    const user = userEvent.setup();
    renderApp("/login");
    await user.type(screen.getByLabelText(enAuth.businessEmail), "a@example.test");
    await user.type(screen.getByLabelText(enAuth.password), "wrong-password");
    await user.click(screen.getAllByRole("button", { name: enAuth.signIn })[0]!);
    expect((await screen.findByRole("alert")).textContent).toContain(enAuth.errors.invalidCredentials);
    expect(screen.queryByLabelText(enTenants.selector)).toBeNull();
  });

  it("validates required credentials and exposes an accessible password toggle", async () => {
    const user = userEvent.setup();
    renderApp("/login");

    await user.click(screen.getByRole("button", { name: enAuth.signIn }));
    expect(screen.getByText(enAuth.errors.invalidEmail)).toBeTruthy();
    expect(screen.getByText(enAuth.errors.passwordRequired)).toBeTruthy();

    const password = screen.getByLabelText(enAuth.password);
    expect(password.getAttribute("type")).toBe("password");
    await user.click(screen.getByRole("button", { name: enAuth.showPassword }));
    expect(password.getAttribute("type")).toBe("text");
    expect(screen.getByRole("button", { name: enAuth.hidePassword })).toBeTruthy();
  });

  it("establishes the authenticated shell after a valid login and clears it on logout", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation(async (input: RequestInfo) => {
        const url = String(input);
        if (url.endsWith("/auth/login")) {
          return new Response(
            JSON.stringify({
              accessToken: "token-a",
              expiresAtUtc: new Date().toISOString(),
              userId: "11111111-1111-1111-1111-111111111111",
              email: "a@example.test",
              displayName: "User A",
              isPlatformAdministrator: false,
            }),
            { status: 200 },
          );
        }
        if (url.endsWith("/tenants")) {
          return new Response(JSON.stringify([]), { status: 200 });
        }
        if (url.endsWith("/invitations")) {
          return new Response(JSON.stringify([]), { status: 200 });
        }
        return new Response(JSON.stringify({ error: "missing" }), { status: 404 });
      }),
    );
    const user = userEvent.setup();
    renderApp("/login");
    await user.type(screen.getByLabelText(enAuth.businessEmail), "a@example.test");
    await user.type(screen.getByLabelText(enAuth.password), "correct-horse");
    await user.click(screen.getAllByRole("button", { name: enAuth.signIn })[0]!);
    expect(await screen.findByLabelText(enTenants.selector)).toBeTruthy();
    await user.click(screen.getAllByRole("button", { name: enAuth.signOut })[0]!);
    expect((await screen.findAllByRole("heading", { name: enAuth.signIn })).length).toBeGreaterThan(0);
  });

  it("resolves new UI strings in English, Arabic, and Kurdish", () => {
    expect(enTenants.create).toBeTruthy();
    expect(arTenants.create).toBeTruthy();
    expect(kuTenants.create).toBeTruthy();
    expect(arAuth.signIn).toBeTruthy();
    expect(kuAuth.signIn).toBeTruthy();
  });

  it("keeps Arabic and Kurdish RTL while English is LTR", () => {
    expect(getDirectionForLocale("en")).toBe("ltr");
    expect(getDirectionForLocale("ar")).toBe("rtl");
    expect(getDirectionForLocale("ku")).toBe("rtl");
  });
});
