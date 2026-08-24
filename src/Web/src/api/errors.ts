export class ApiError extends Error {
  readonly status: number;
  readonly code: string;

  constructor(status: number, code: string) {
    super(code);
    this.name = "ApiError";
    this.status = status;
    this.code = code;
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
