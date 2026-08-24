import { ApiError } from "./errors";
import type { ApiErrorBody } from "./types";

type UnauthorizedHandler = () => void;

let unauthorizedHandler: UnauthorizedHandler | null = null;

export function setUnauthorizedHandler(handler: UnauthorizedHandler | null): void {
  unauthorizedHandler = handler;
}

export type RequestOptions = RequestInit & {
  token?: string | null;
  skipUnauthorizedHandler?: boolean;
};

function apiBaseUrl(): string {
  const configured = import.meta.env.VITE_API_BASE_URL?.trim();
  if (!configured) {
    return "";
  }
  return configured.replace(/\/$/, "");
}

export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { token, skipUnauthorizedHandler, headers, ...init } = options;
  const requestHeaders = new Headers(headers);
  if (init.body !== undefined && !requestHeaders.has("Content-Type")) {
    requestHeaders.set("Content-Type", "application/json");
  }
  if (token) {
    requestHeaders.set("Authorization", `Bearer ${token}`);
  }

  let response: Response;
  try {
    response = await fetch(`${apiBaseUrl()}${path}`, { ...init, headers: requestHeaders });
  } catch {
    throw new ApiError(0, "network");
  }

  if (response.status === 401) {
    if (!skipUnauthorizedHandler) {
      unauthorizedHandler?.();
    }
    const code = await readErrorCode(response, "unauthenticated");
    throw new ApiError(401, code);
  }

  if (!response.ok) {
    const fallback = response.status === 403 ? "forbidden" : "request_failed";
    throw new ApiError(response.status, await readErrorCode(response, fallback));
  }

  if (response.status === 204) {
    return undefined as T;
  }

  const text = await response.text();
  if (!text) {
    return undefined as T;
  }
  return JSON.parse(text) as T;
}

async function readErrorCode(response: Response, fallback: string): Promise<string> {
  try {
    const body = (await response.json()) as ApiErrorBody;
    return body.error || fallback;
  } catch {
    return fallback;
  }
}
