export type Guid = string;
export type IsoDateTime = string;

export type JsonValue = string | number | boolean | null | JsonValue[] | JsonObject;
export interface JsonObject {
  [key: string]: JsonValue;
}

/** RFC 9457 problem details as returned by ASP.NET Core. */
export interface ProblemDetails {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
  /** Present on validation problems: field name to messages. */
  errors?: Record<string, string[]> | null;
  [extension: string]: unknown;
}
