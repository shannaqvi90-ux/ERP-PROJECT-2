import { requestLanguage } from "./i18n";

export type FieldError = { code: string; message: string };

/** A problem response (RFC 9457) from the API, with its stable code and localized title. */
export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  readonly fieldErrors: Record<string, FieldError[]>;
  readonly body: Record<string, unknown>;

  constructor(status: number, body: Record<string, unknown>) {
    const title = typeof body.title === "string" ? body.title : `HTTP ${status}`;
    super(title);
    this.status = status;
    this.code = typeof body.code === "string" ? body.code : `http.${status}`;
    this.fieldErrors = (body.errors as Record<string, FieldError[]> | undefined) ?? {};
    this.body = body;
  }
}

/**
 * Calls the API with the session cookie. Unsafe methods carry the X-Erp-Request header the API
 * requires (CSRF defence); every call asks for messages in the screen's language.
 */
export async function api<T>(method: "GET" | "POST" | "PUT" | "DELETE", path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = {
    Accept: "application/json",
    "Accept-Language": requestLanguage(),
  };
  if (method !== "GET") headers["X-Erp-Request"] = "1";
  if (body !== undefined) headers["Content-Type"] = "application/json";

  const response = await fetch(path, {
    method,
    headers,
    credentials: "same-origin",
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (response.status === 204) return undefined as T;
  const text = await response.text();
  let parsed: unknown = undefined;
  if (text.length > 0) {
    try {
      parsed = JSON.parse(text);
    } catch {
      parsed = { title: text };
    }
  }
  if (!response.ok) {
    throw new ApiError(response.status, (parsed as Record<string, unknown>) ?? {});
  }
  return parsed as T;
}
