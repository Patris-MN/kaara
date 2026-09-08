import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import "../i18n";
import App from "../App";
import { AuthProvider } from "../auth/AuthProvider";
import { getDirectionForLocale } from "../i18n/direction";
import arTenants from "../locales/ar/tenants.json";
import enCommon from "../locales/en/common.json";
import enNavigation from "../locales/en/navigation.json";
import enNotifications from "../locales/en/notifications.json";
import enTenants from "../locales/en/tenants.json";
import kuTenants from "../locales/ku/tenants.json";
import { deriveAccountCapabilities } from "../test/directoryFetchHandlers";
import { readOrganizationView } from "../tenancy/organizationView";
import { TenantDirectoryProvider } from "../tenancy/TenantDirectoryProvider";
import { clearSession, writeAccessToken, writeSelectedTenantId } from "./session";

const authUser = {
  userId: "11111111-1111-1111-1111-111111111111",
  email: "a@example.test",
  displayName: "User A",
  isPlatformAdministrator: false,
};

const tenantA = {
  tenantId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  name: "Org A",
  slug: "org-a",
  role: "Owner",
  status: "Active",
  workspaceCount: 4,
  canManage: true,
};

const tenantB = {
  tenantId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  name: "Org B",
  slug: "org-b",
  role: "Member",
  status: "Active",
  workspaceCount: 0,
  canManage: false,
};

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function renderApp(path = "/app") {
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

function pathOf(input: RequestInfo | URL) {
  const raw = String(input);
  return raw.startsWith("http") ? new URL(raw).pathname : raw;
}

async function enterApp(
  fetchImpl: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>,
  path = "/app",
) {
  writeAccessToken("token-a");
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const pathName = pathOf(input);
      if (pathName === "/notifications") {
        return json({ items: [], unreadCount: 0 });
      }
      return fetchImpl(input, init);
    }),
  );
  renderApp(path);
  expect(await screen.findByLabelText(enTenants.selector)).toBeTruthy();
}

async function waitForOrganizationsDirectory() {
  await screen.findByRole("link", { name: /Org A/ });
}

async function openNewOrganizationModal(user: ReturnType<typeof userEvent.setup>) {
  await waitForOrganizationsDirectory();
  await user.click(screen.getByRole("button", { name: enTenants.newOrganization }));
}

function shell(path: string, tenants: typeof tenantA[] = [tenantA], invitations: typeof tenantA[] = []) {
  if (path.endsWith("/auth/me")) {
    return json(authUser);
  }
  if (path.endsWith("/tenants")) {
    return json(tenants);
  }
  if (path.endsWith("/invitations")) {
    return json(invitations);
  }
  if (path.endsWith("/account/capabilities")) {
    return json(deriveAccountCapabilities(tenants, invitations));
  }
  if (path === "/notifications") {
    return json({ items: [], unreadCount: 0 });
  }
  return null;
}

describe("phase 7 organization shell UX", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    clearSession();
    localStorage.clear();
    cleanup();
  });

  it("shows a list-first organizations page without a permanent create form or extra count", async () => {
    await enterApp(async (input) => {
      const handled = shell(pathOf(input), [tenantA, tenantB]);
      return handled ?? json({ error: "missing" }, 404);
    });

    expect(await screen.findByRole("heading", { name: enTenants.title })).toBeTruthy();
    expect(screen.queryByRole("heading", { name: enTenants.listWithCount.replace("{{count}}", "2") })).toBeNull();
    await waitFor(() => {
      expect(screen.getAllByText("Org A").length).toBeGreaterThan(0);
    });
    expect(screen.getAllByText("Org B").length).toBeGreaterThan(0);
    expect(screen.getByText(enTenants.roles.Owner)).toBeTruthy();
    expect(screen.getByText(enTenants.workspaceCount_other.replace("{{count}}", "4"))).toBeTruthy();
    expect(screen.getByText(enTenants.workspaceCount_zero.replace("{{count}}", "0"))).toBeTruthy();
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(screen.queryByLabelText(enTenants.name)).toBeNull();
    expect(screen.getByText(enTenants.resourceSummary_other.replace("{{count}}", "2"))).toBeTruthy();
    expect(screen.queryByRole("heading", { name: enTenants.invitationsWithCount.replace("{{count}}", "0") })).toBeNull();
    expect(screen.queryByText(enTenants.noInvitations)).toBeNull();
    const gridToggle = screen.getByRole("button", { name: enTenants.viewGrid });
    const listToggle = screen.getByRole("button", { name: enTenants.viewList });
    expect(gridToggle.getAttribute("aria-pressed")).toBe("true");
    expect(listToggle.getAttribute("aria-pressed")).toBe("false");
    expect(screen.getByRole("toolbar", { name: enTenants.resourceToolbar })).toBeTruthy();
    const toolbar = screen.getByRole("toolbar", { name: enTenants.resourceToolbar });
    expect(within(toolbar).getByRole("button", { name: enTenants.newOrganization })).toBeTruthy();
    expect(document.querySelector(".page-heading-actions")).toBeNull();
  });

  it("opens and cancels the create organization modal", async () => {
    const user = userEvent.setup();
    await enterApp(async (input) => {
      const handled = shell(pathOf(input));
      return handled ?? json({ error: "missing" }, 404);
    });

    expect(await screen.findByRole("heading", { name: enTenants.title })).toBeTruthy();
    await waitForOrganizationsDirectory();
    fireEvent.click(screen.getByRole("button", { name: enTenants.newOrganization }));
    expect(await screen.findByRole("dialog")).toBeTruthy();
    expect(document.activeElement).toBe(screen.getByLabelText(enTenants.name));
    expect(screen.queryByLabelText(enTenants.urlIdentifier)).toBeNull();
    const disclosure = screen.getByRole("button", { name: enTenants.customizeIdentifier });
    expect(disclosure.getAttribute("aria-expanded")).toBe("false");
    await user.click(disclosure);
    expect(disclosure.getAttribute("aria-expanded")).toBe("true");
    expect(screen.getByLabelText(enTenants.urlIdentifier)).toBeTruthy();
    await user.click(screen.getByRole("button", { name: enCommon.cancel }));
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("creates an organization from the name and generated identifier", async () => {
    const user = userEvent.setup();
    const tenants = [tenantA];
    let created: { name?: string; slug?: string } | undefined;

    await enterApp(async (input, init) => {
      const path = pathOf(input);
      const method = (init?.method ?? "GET").toUpperCase();
      if (path === "/tenants" && method === "POST") {
        created = JSON.parse(String(init?.body)) as typeof created;
        const next = {
          tenantId: "cccccccc-cccc-cccc-cccc-cccccccccccc",
          name: created?.name ?? "Acme Corporation",
          slug: created?.slug ?? "acme-corporation",
          role: "Owner",
          status: "Active",
          workspaceCount: 0,
          canManage: true,
        };
        tenants.push(next);
        return json(next, 201);
      }
      if (path.includes("/workspaces")) {
        return json([]);
      }
      const handled = shell(path, tenants);
      return handled ?? json({ error: "missing" }, 404);
    });

    expect(await screen.findByRole("heading", { name: enTenants.title })).toBeTruthy();
    await openNewOrganizationModal(user);
    await user.type(screen.getByLabelText(enTenants.name), "Acme Corporation");
    const submit = screen.getByRole("button", { name: enTenants.create });
    await user.click(submit);
    await waitFor(() => {
      expect(created).toMatchObject({ name: "Acme Corporation", slug: "acme-corporation" });
    });
    expect(await screen.findByText(enTenants.createdTitle)).toBeTruthy();
    expect(screen.getByText(enTenants.createdBody.replace("{{name}}", "Acme Corporation"))).toBeTruthy();
  });

  it("shows a creating state and prevents duplicate submit", async () => {
    const user = userEvent.setup();
    let resolveCreate: (() => void) | undefined;

    await enterApp(async (input, init) => {
      const path = pathOf(input);
      const method = (init?.method ?? "GET").toUpperCase();
      if (path === "/tenants" && method === "POST") {
        await new Promise<void>((resolve) => {
          resolveCreate = resolve;
        });
        return json(
          {
            tenantId: "cccccccc-cccc-cccc-cccc-cccccccccccc",
            name: "Pending Org",
            slug: "pending-org",
            role: "Owner",
            status: "Active",
            workspaceCount: 0,
            canManage: true,
          },
          201,
        );
      }
      if (path.includes("/workspaces")) {
        return json([]);
      }
      const handled = shell(path);
      return handled ?? json({ error: "missing" }, 404);
    });

    expect(await screen.findByRole("heading", { name: enTenants.title })).toBeTruthy();
    await openNewOrganizationModal(user);
    await user.type(screen.getByLabelText(enTenants.name), "Pending Org");
    const submit = screen.getByRole("button", { name: enTenants.create });
    await user.click(submit);
    const savingButton = await screen.findByRole("button", { name: enTenants.creating });
    expect(savingButton.getAttribute("aria-busy")).toBe("true");
    expect(savingButton).toHaveProperty("disabled", true);
    resolveCreate?.();
    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    });
  });

  it("keeps the create form after a server error", async () => {
    const user = userEvent.setup();
    await enterApp(async (input, init) => {
      const path = pathOf(input);
      const method = (init?.method ?? "GET").toUpperCase();
      if (path === "/tenants" && method === "POST") {
        return json({ error: "duplicate_slug" }, 409);
      }
      const handled = shell(path);
      return handled ?? json({ error: "missing" }, 404);
    });

    expect(await screen.findByRole("heading", { name: enTenants.title })).toBeTruthy();
    await openNewOrganizationModal(user);
    await user.type(screen.getByLabelText(enTenants.name), "Org A");
    await user.click(screen.getByRole("button", { name: enTenants.create }));
    expect(await screen.findByText(enTenants.errors.duplicateSlug)).toBeTruthy();
    expect((screen.getByLabelText(enTenants.name) as HTMLInputElement).value).toBe("Org A");
    expect(screen.getByRole("dialog")).toBeTruthy();
  });

  it("shows a connection toast when create fails because of network loss", async () => {
    const user = userEvent.setup();
    await enterApp(async (input, init) => {
      const path = pathOf(input);
      const method = (init?.method ?? "GET").toUpperCase();
      if (path === "/tenants" && method === "POST") {
        throw new TypeError("Failed to fetch");
      }
      const handled = shell(path);
      return handled ?? json({ error: "missing" }, 404);
    });

    expect(await screen.findByRole("heading", { name: enTenants.title })).toBeTruthy();
    await openNewOrganizationModal(user);
    await user.type(screen.getByLabelText(enTenants.name), "Org A");
    await user.click(screen.getByRole("button", { name: enTenants.create }));
    expect(await screen.findByRole("alert")).toBeTruthy();
    expect(screen.getByText(enCommon.feedback.connectionTitle)).toBeTruthy();
    expect(screen.getByRole("dialog")).toBeTruthy();
    expect((screen.getByLabelText(enTenants.name) as HTMLInputElement).value).toBe("Org A");
  });

  it("explains unavailable navigation before an organization is selected", async () => {
    await enterApp(async (input) => {
      const handled = shell(pathOf(input), []);
      return handled ?? json({ error: "missing" }, 404);
    });

    expect(await screen.findByText(enTenants.onboarding.welcomeTitle)).toBeTruthy();
    expect(screen.getByRole("combobox", { name: enTenants.selector })).toHaveProperty("value", "");
    expect(screen.queryByText(enNavigation.selectOrganizationFirst)).toBeNull();
    expect(
      screen.getByLabelText(`${enNavigation.workspaces}. ${enNavigation.selectOrganizationFirst}`),
    ).toBeTruthy();
    expect(screen.queryByRole("link", { name: enNavigation.workspaces })).toBeNull();
  });

  it("shows the active organization in the switcher and enables workspace navigation", async () => {
    writeSelectedTenantId(authUser.userId, tenantA.tenantId);
    await enterApp(async (input) => {
      const path = pathOf(input);
      const handled = shell(path, [tenantA, tenantB]);
      if (handled) {
        return handled;
      }
      if (path.includes("/workspaces")) {
        return json([]);
      }
      return json({ error: "missing" }, 404);
    });

    expect(await screen.findByRole("combobox", { name: enTenants.selector })).toHaveProperty(
      "value",
      tenantA.tenantId,
    );
    expect(screen.getByRole("link", { name: enNavigation.workspaces })).toBeTruthy();
    expect(screen.getByText(enTenants.current)).toBeTruthy();
  });

  it("localizes organization UX strings and keeps RTL", () => {
    expect(enTenants.newOrganization).toBeTruthy();
    expect(arTenants.newOrganization).toBeTruthy();
    expect(kuTenants.newOrganization).toBeTruthy();
    expect(arTenants.current).toBeTruthy();
    expect(kuTenants.urlIdentifier).toBeTruthy();
    expect(enTenants.loadFailed).toBeTruthy();
    expect(arTenants.loadFailed).toBeTruthy();
    expect(kuTenants.loadFailedBody).toBeTruthy();
    expect(enTenants.viewGrid).toBeTruthy();
    expect(enTenants.resourceSummary_other).toContain("organizations");
    expect(arTenants.resourceSummary_few).toBeTruthy();
    expect(kuTenants.resourceToolbar).toBeTruthy();
    expect(arTenants.editOrganization).toBeTruthy();
    expect(kuTenants.saveChanges).toBeTruthy();
    expect(enTenants.createdTitle).toBeTruthy();
    expect(arTenants.updatedTitle).toBeTruthy();
    expect(kuTenants.creating).toBeTruthy();
    expect(enCommon.feedback.dismiss).toBeTruthy();
    expect(enTenants.workspaceCount_one.replace("{{count}}", "1")).toBe("1 Workspace");
    expect(getDirectionForLocale("en")).toBe("ltr");
    expect(getDirectionForLocale("ar")).toBe("rtl");
    expect(getDirectionForLocale("ku")).toBe("rtl");
  });

  it("creates an organization from an Arabic name without asking for an identifier", async () => {
    const user = userEvent.setup();
    let created: { name?: string; slug?: string } | undefined;

    await enterApp(async (input, init) => {
      const path = pathOf(input);
      const method = (init?.method ?? "GET").toUpperCase();
      if (path === "/tenants" && method === "POST") {
        created = JSON.parse(String(init?.body)) as typeof created;
        return json(
          {
            tenantId: "cccccccc-cccc-cccc-cccc-cccccccccccc",
            name: created?.name ?? "شركة أكمي",
            slug: created?.slug ?? "organization-fallback",
            role: "Owner",
            status: "Active",
          },
          201,
        );
      }
      if (path.includes("/workspaces")) {
        return json([]);
      }
      const handled = shell(path);
      return handled ?? json({ error: "missing" }, 404);
    });

    expect(await screen.findByRole("heading", { name: enTenants.title })).toBeTruthy();
    await openNewOrganizationModal(user);
    expect(document.activeElement).toBe(screen.getByLabelText(enTenants.name));
    await user.type(screen.getByLabelText(enTenants.name), "شركة أكمي");
    expect(screen.queryByLabelText(enTenants.urlIdentifier)).toBeNull();
    await user.click(screen.getByRole("button", { name: enTenants.create }));
    await waitFor(() => {
      expect(created?.name).toBe("شركة أكمي");
      expect(created?.slug).toMatch(/^organization-[a-f0-9]{8}$/);
    });
  });

  it("closes the create organization modal with Escape", async () => {
    const user = userEvent.setup();
    await enterApp(async (input) => {
      const handled = shell(pathOf(input));
      return handled ?? json({ error: "missing" }, 404);
    });

    expect(await screen.findByRole("heading", { name: enTenants.title })).toBeTruthy();
    await openNewOrganizationModal(user);
    expect(await screen.findByRole("dialog")).toBeTruthy();
    await user.keyboard("{Escape}");
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("keeps pending invitations off the organizations page and surfaces them in attention", async () => {
    const user = userEvent.setup();
    const invitation = {
      tenantId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
      name: "Invited Org",
      slug: "invited-org",
      role: "Member",
      status: "Invited",
      workspaceCount: 0,
      canManage: false,
    };
    await enterApp(async (input) => {
      const handled = shell(pathOf(input), [tenantA], [invitation]);
      return handled ?? json({ error: "missing" }, 404);
    });

    expect(await screen.findByRole("heading", { name: enTenants.title })).toBeTruthy();
    expect(screen.queryByText("Invited Org")).toBeNull();
    expect(screen.queryByRole("heading", { name: enTenants.invitationsWithCount.replace("{{count}}", "1") })).toBeNull();
    expect(screen.queryByText(enTenants.noInvitations)).toBeNull();

    await user.click(
      screen.getByRole("button", {
        name: enNotifications.attentionWithInvitations.replace("{{count}}", "1"),
      }),
    );
    expect(screen.getByText(enNotifications.pendingInvitations)).toBeTruthy();
    expect(screen.getByText("Invited Org")).toBeTruthy();
    expect(screen.getByText(enTenants.invitedRole.replace("{{role}}", enTenants.roles.Member))).toBeTruthy();
    expect(screen.getByRole("button", { name: enTenants.accept })).toBeTruthy();
    expect(screen.queryByRole("button", { name: /decline/i })).toBeNull();
  });

  it("switches between grid and list without changing organization data", async () => {
    const user = userEvent.setup();
    await enterApp(async (input) => {
      const handled = shell(pathOf(input), [tenantA, tenantB]);
      return handled ?? json({ error: "missing" }, 404);
    });

    expect(await screen.findByRole("heading", { name: enTenants.title })).toBeTruthy();
    expect(document.querySelector(".org-grid")).toBeTruthy();
    expect(document.querySelector(".org-list")).toBeNull();
    expect(screen.getByRole("button", { name: enTenants.viewGrid }).getAttribute("aria-pressed")).toBe("true");
    await user.click(screen.getByRole("button", { name: enTenants.viewList }));
    expect(document.querySelector(".org-list")).toBeTruthy();
    expect(document.querySelector(".org-grid")).toBeNull();
    expect(screen.getByRole("button", { name: enTenants.viewList }).getAttribute("aria-pressed")).toBe("true");
    expect(screen.getByRole("button", { name: enTenants.viewGrid }).getAttribute("aria-pressed")).toBe("false");
    expect(screen.getByText(enTenants.resourceSummary_other.replace("{{count}}", "2"))).toBeTruthy();
    expect(screen.getAllByText("Org A").length).toBeGreaterThan(0);
    expect(readOrganizationView(authUser.userId)).toBe("list");
  });

  it("shows edit only when the server grants management", async () => {
    const user = userEvent.setup();
    let updated: { name?: string } | undefined;
    await enterApp(async (input, init) => {
      const path = pathOf(input);
      const method = (init?.method ?? "GET").toUpperCase();
      if (path === `/tenants/${tenantA.tenantId}` && method === "PUT") {
        updated = JSON.parse(String(init?.body)) as typeof updated;
        return json({ tenantId: tenantA.tenantId, name: updated?.name ?? tenantA.name, slug: tenantA.slug });
      }
      const handled = shell(path, [tenantA, tenantB]);
      return handled ?? json({ error: "missing" }, 404);
    });

    expect(await screen.findByRole("heading", { name: enTenants.title })).toBeTruthy();
    await waitForOrganizationsDirectory();
    expect(screen.getByRole("button", { name: enTenants.editOrganization })).toBeTruthy();
    expect(screen.getAllByRole("button", { name: enTenants.editOrganization })).toHaveLength(1);
    await user.click(screen.getByRole("button", { name: enTenants.editOrganization }));
    expect(await screen.findByRole("dialog", { name: enTenants.editOrganization })).toBeTruthy();
    const nameField = screen.getByLabelText(enTenants.name) as HTMLInputElement;
    expect(document.activeElement).toBe(nameField);
    await user.clear(nameField);
    await user.type(nameField, "Renamed Org");
    const saveButton = screen.getByRole("button", { name: enTenants.saveChanges });
    await user.click(saveButton);
    await waitFor(() => {
      expect(updated).toMatchObject({ name: "Renamed Org" });
    });
    expect(await screen.findByText(enTenants.updatedTitle)).toBeTruthy();
    expect(screen.getByText(enTenants.updatedBody.replace("{{name}}", "Renamed Org"))).toBeTruthy();
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("shows a saving state and keeps edit values when save fails", async () => {
    const user = userEvent.setup();
    let resolveSave: (() => void) | undefined;
    await enterApp(async (input, init) => {
      const path = pathOf(input);
      const method = (init?.method ?? "GET").toUpperCase();
      if (path === `/tenants/${tenantA.tenantId}` && method === "PUT") {
        await new Promise<void>((resolve) => {
          resolveSave = resolve;
        });
        return json({ error: "request_failed" }, 500);
      }
      const handled = shell(path, [tenantA, tenantB]);
      return handled ?? json({ error: "missing" }, 404);
    });

    expect(await screen.findByRole("heading", { name: enTenants.title })).toBeTruthy();
    await waitForOrganizationsDirectory();
    await user.click(screen.getByRole("button", { name: enTenants.editOrganization }));
    const nameField = screen.getByLabelText(enTenants.name) as HTMLInputElement;
    await user.clear(nameField);
    await user.type(nameField, "Still Here");
    await user.click(screen.getByRole("button", { name: enTenants.saveChanges }));
    expect(await screen.findByRole("button", { name: enTenants.saving })).toBeTruthy();
    resolveSave?.();
    expect(await screen.findByRole("alert")).toBeTruthy();
    expect(screen.getByText(enTenants.saveFailedTitle)).toBeTruthy();
    expect(screen.getByRole("dialog")).toBeTruthy();
    expect(nameField.value).toBe("Still Here");
  });

  it("shows a friendly permission toast when edit is forbidden", async () => {
    const user = userEvent.setup();
    await enterApp(async (input, init) => {
      const path = pathOf(input);
      const method = (init?.method ?? "GET").toUpperCase();
      if (path === `/tenants/${tenantA.tenantId}` && method === "PUT") {
        return json({ error: "tenant_update_forbidden" }, 403);
      }
      const handled = shell(path, [tenantA, tenantB]);
      return handled ?? json({ error: "missing" }, 404);
    });

    expect(await screen.findByRole("heading", { name: enTenants.title })).toBeTruthy();
    await waitForOrganizationsDirectory();
    await user.click(screen.getByRole("button", { name: enTenants.editOrganization }));
    await user.click(screen.getByRole("button", { name: enTenants.saveChanges }));
    expect(await screen.findByText(enTenants.permissionTitle)).toBeTruthy();
    expect(screen.getByText(enTenants.permissionBody)).toBeTruthy();
    expect(screen.getByRole("dialog")).toBeTruthy();
  });
});
