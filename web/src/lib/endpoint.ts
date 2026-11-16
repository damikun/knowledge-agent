export type HttpMethod = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';

export type QueryValue = string | number | boolean | null | undefined;

export type Query = Record<string, QueryValue>;

export type ParamNames<P extends string> = P extends `${string}{${infer Name}}${infer Rest}` ? Name | ParamNames<Rest> : never;

export type PathParams<P extends string> = Record<ParamNames<P>, string | number>;

export type ParamsOf<P extends string> = [ParamNames<P>] extends [never] ? { params?: undefined } : { params: PathParams<P> };

export interface Endpoint<P extends string = string> {
  readonly method: HttpMethod;
  readonly path: P;
}

export function endpoint<P extends string>(method: HttpMethod, path: P): Endpoint<P> {
  return { method, path };
}

export function queryString(query: Query | undefined): string {
  if (!query) {
    return '';
  }
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== null && value !== '') {
      params.set(key, String(value));
    }
  }
  const text = params.toString().replace(/%2C/gi, ',');
  return text ? `?${text}` : '';
}

export function pathOf<P extends string>(target: Endpoint<P>, params?: PathParams<P>, query?: Query): string {
  const values = (params ?? {}) as Record<string, string | number>;
  const path = target.path.replace(/\{(\w+)\}/g, (_, name: string) => {
    const value = values[name];
    if (value === undefined) {
      throw new Error(`Missing path parameter "${name}" for ${target.method} ${target.path}.`);
    }
    return encodeURIComponent(String(value));
  });
  return `${path}${queryString(query)}`;
}
