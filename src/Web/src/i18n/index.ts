import i18n from "i18next";
import LanguageDetector from "i18next-browser-languagedetector";
import { initReactI18next } from "react-i18next";

import enAuth from "../locales/en/auth.json";
import enCommon from "../locales/en/common.json";
import enMembers from "../locales/en/members.json";
import enNavigation from "../locales/en/navigation.json";
import enProjects from "../locales/en/projects.json";
import enNotifications from "../locales/en/notifications.json";
import enTasks from "../locales/en/tasks.json";
import enTenants from "../locales/en/tenants.json";
import enWorkspaces from "../locales/en/workspaces.json";
import arAuth from "../locales/ar/auth.json";
import arCommon from "../locales/ar/common.json";
import arMembers from "../locales/ar/members.json";
import arNavigation from "../locales/ar/navigation.json";
import arProjects from "../locales/ar/projects.json";
import arNotifications from "../locales/ar/notifications.json";
import arTasks from "../locales/ar/tasks.json";
import arTenants from "../locales/ar/tenants.json";
import arWorkspaces from "../locales/ar/workspaces.json";
import kuAuth from "../locales/ku/auth.json";
import kuCommon from "../locales/ku/common.json";
import kuMembers from "../locales/ku/members.json";
import kuNavigation from "../locales/ku/navigation.json";
import kuProjects from "../locales/ku/projects.json";
import kuNotifications from "../locales/ku/notifications.json";
import kuTasks from "../locales/ku/tasks.json";
import kuTenants from "../locales/ku/tenants.json";
import kuWorkspaces from "../locales/ku/workspaces.json";

import { DEFAULT_LOCALE, NAMESPACES, SUPPORTED_LOCALES } from "./config";

const resources = {
  en: {
    common: enCommon,
    navigation: enNavigation,
    auth: enAuth,
    tenants: enTenants,
    workspaces: enWorkspaces,
    projects: enProjects,
    members: enMembers,
    tasks: enTasks,
    notifications: enNotifications,
  },
  ar: {
    common: arCommon,
    navigation: arNavigation,
    auth: arAuth,
    tenants: arTenants,
    workspaces: arWorkspaces,
    projects: arProjects,
    members: arMembers,
    tasks: arTasks,
    notifications: arNotifications,
  },
  ku: {
    common: kuCommon,
    navigation: kuNavigation,
    auth: kuAuth,
    tenants: kuTenants,
    workspaces: kuWorkspaces,
    projects: kuProjects,
    members: kuMembers,
    tasks: kuTasks,
    notifications: kuNotifications,
  },
} as const;

void i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources,
    supportedLngs: SUPPORTED_LOCALES,
    fallbackLng: DEFAULT_LOCALE,
    defaultNS: "common",
    ns: NAMESPACES,
    interpolation: { escapeValue: false },
    detection: {
      order: ["localStorage", "navigator"],
      caches: ["localStorage"],
    },
  });

export default i18n;
