export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  readonly existingName?: string;
  readonly existingKey?: string;

  constructor(status: number, code: string, options?: { existingName?: string; existingKey?: string }) {
    super(code);
    this.name = "ApiError";
    this.status = status;
    this.code = code;
    this.existingName = options?.existingName;
    this.existingKey = options?.existingKey;
  }
}

export function isApiError(error: unknown): error is ApiError {
  return error instanceof ApiError;
}

export function translationKeyForApiError(error: unknown): string {
  if (error instanceof ApiError) {
    return `errors.${error.code}`;
  }
  return "errors.network";
}
