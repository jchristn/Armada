/**
 * Pure logic of the API Explorer (pages/ApiExplorer.tsx), shared with the mobile app: OpenAPI operation listing and
 * filtering, schema examples for request bodies, parameter defaults, response formatting, code snippets, and request
 * history replay matching. No browser globals.
 */
export const RESPONSE_TABS = ['preview', 'body', 'headers', 'code'] as const;
export const CODE_TABS = ['curl', 'fetch', 'csharp'] as const;

export type ResponseTab = typeof RESPONSE_TABS[number];
export type CodeTab = typeof CODE_TABS[number];

export type JsonValue = string | number | boolean | null | JsonValue[] | { [key: string]: JsonValue };

export interface OpenApiSpec {
  paths?: Record<string, OpenApiPathItem>;
  components?: {
    schemas?: Record<string, OpenApiSchema>;
  };
}

export interface OpenApiPathItem extends Record<string, OpenApiOperationSource | OpenApiParameter[] | undefined> {
  parameters?: OpenApiParameter[];
}

export type OpenApiPathItemValue = OpenApiOperationSource | OpenApiParameter[] | undefined;

export interface OpenApiOperationSource {
  operationId?: string;
  summary?: string;
  description?: string;
  tags?: string[];
  parameters?: OpenApiParameter[];
  requestBody?: {
    content?: Record<string, { schema?: OpenApiSchema }>;
  };
  responses?: Record<string, unknown>;
}

export interface OpenApiParameter {
  name: string;
  in: 'path' | 'query' | 'header' | string;
  required?: boolean;
  description?: string;
  example?: unknown;
  schema?: OpenApiSchema;
}

export interface OpenApiSchema {
  $ref?: string;
  type?: string;
  format?: string;
  example?: JsonValue;
  default?: JsonValue;
  enum?: JsonValue[];
  properties?: Record<string, OpenApiSchema>;
  items?: OpenApiSchema;
  allOf?: OpenApiSchema[];
  oneOf?: OpenApiSchema[];
  anyOf?: OpenApiSchema[];
}

export interface ExplorerOperation {
  id: string;
  method: string;
  path: string;
  summary: string;
  description: string;
  tag: string;
  parameters: OpenApiParameter[];
  requestBody: { schema?: OpenApiSchema } | null;
  requestBodyContentType: string;
}

export interface ExplorerRequestPreview {
  method: string;
  url: string;
  headers: Record<string, string>;
  body: string;
  contentType: string;
}

export interface ExplorerResponse {
  ok: boolean;
  status: number;
  statusText: string;
  durationMs: number;
  headers: Record<string, string>;
  contentType: string;
  body: string;
  preview: unknown;
  sizeBytes: number;
  code: Record<CodeTab, string>;
}

export interface ReplayRequest {
  operationId?: string;
  method: string;
  route: string;
  routeTemplate?: string | null;
  pathValues: Record<string, string | null>;
  queryValues: Record<string, string | null>;
  headerValues: Record<string, string | null>;
  bodyValue: string;
}

export function isOperationSource(value: OpenApiPathItemValue): value is OpenApiOperationSource {
  if (!value || Array.isArray(value)) return false;
  return true;
}

export function resolveSchema(schema: OpenApiSchema | undefined | null, spec: OpenApiSpec | null, seen = new Set<string>()): OpenApiSchema | null {
  if (!schema) return null;
  if (schema.$ref) {
    const refName = schema.$ref.split('/').pop();
    if (!refName || seen.has(refName)) return null;
    seen.add(refName);
    return resolveSchema(spec?.components?.schemas?.[refName], spec, seen);
  }
  if (schema.allOf?.length) {
    return schema.allOf.reduce<OpenApiSchema>((merged, item) => {
      const resolved = resolveSchema(item, spec, new Set(seen));
      return {
        ...merged,
        ...resolved,
        properties: { ...(merged.properties || {}), ...(resolved?.properties || {}) },
      };
    }, {});
  }
  if (schema.oneOf?.length) return resolveSchema(schema.oneOf[0], spec, seen);
  if (schema.anyOf?.length) return resolveSchema(schema.anyOf[0], spec, seen);
  return schema;
}

export function schemaExample(schema: OpenApiSchema | undefined | null, spec: OpenApiSpec | null): JsonValue {
  const resolved = resolveSchema(schema, spec);
  if (!resolved) return {};
  if (resolved.example !== undefined) return resolved.example;
  if (resolved.default !== undefined) return resolved.default;

  switch (resolved.type) {
    case 'object': {
      const value: Record<string, JsonValue> = {};
      Object.entries(resolved.properties || {}).forEach(([key, childSchema]) => {
        value[key] = schemaExample(childSchema, spec);
      });
      return value;
    }
    case 'array':
      return resolved.items ? [schemaExample(resolved.items, spec)] : [];
    case 'integer':
    case 'number':
      return 0;
    case 'boolean':
      return false;
    case 'string':
      if (resolved.enum?.length) return resolved.enum[0];
      if (resolved.format === 'date-time') return new Date().toISOString();
      return '';
    default:
      return {};
  }
}

export function parameterInitialValue(parameter: OpenApiParameter, spec: OpenApiSpec | null) {
  if (parameter.example !== undefined) return String(parameter.example);
  const resolved = resolveSchema(parameter.schema, spec);
  if (resolved?.default !== undefined) return String(resolved.default);
  if (resolved?.enum?.length) return String(resolved.enum[0]);
  return '';
}

export function prettifyContent(body: string, contentType: string) {
  if (!body) return '(empty)';
  if (contentType.includes('application/json')) {
    try {
      return JSON.stringify(JSON.parse(body), null, 2);
    } catch {
      return body;
    }
  }
  return body;
}

export function generateCodeSnippets(request: ExplorerRequestPreview): Record<CodeTab, string> {
  const headerLines = Object.entries(request.headers || {});
  const curlHeaders = headerLines.map(([key, value]) => `-H "${key}: ${value}"`).join(' \\\n  ');
  const fetchHeaders = headerLines.length > 0
    ? `,\n  headers: ${JSON.stringify(request.headers, null, 2).replace(/\n/g, '\n  ')}`
    : '';
  const csharpHeaders = headerLines
    .map(([key, value]) => `request.Headers.TryAddWithoutValidation("${key}", "${value}");`)
    .join('\n');
  const body = request.body ? prettifyContent(request.body, request.contentType || 'application/json') : '';
  const curlBody = request.body ? ` \\\n  --data '${body.replace(/'/g, "'\\''")}'` : '';
  const fetchBody = request.body ? `,\n  body: ${JSON.stringify(body)}` : '';
  const csharpBody = request.body
    ? `request.Content = new StringContent(${JSON.stringify(body)}, Encoding.UTF8, "${request.contentType || 'application/json'}");`
    : '';

  return {
    curl: `curl -X ${request.method.toUpperCase()} "${request.url}"${curlHeaders ? ` \\\n  ${curlHeaders}` : ''}${curlBody}`,
    fetch: `const response = await fetch(${JSON.stringify(request.url)}, {\n  method: ${JSON.stringify(request.method.toUpperCase())}${fetchHeaders}${fetchBody}\n});\n\nconst data = await response.text();`,
    csharp: `using System.Net.Http;\nusing System.Text;\n\nusing var client = new HttpClient();\nusing var request = new HttpRequestMessage(HttpMethod.${request.method.charAt(0).toUpperCase() + request.method.slice(1).toLowerCase()}, ${JSON.stringify(request.url)});\n${csharpHeaders}${csharpBody ? `\n${csharpBody}` : ''}\nusing var response = await client.SendAsync(request);\nvar body = await response.Content.ReadAsStringAsync();`,
  };
}

export function getOperationSubtext(operation: ExplorerOperation) {
  const summary = operation.summary?.trim();
  const description = operation.description?.trim();
  const methodPath = `${operation.method.toUpperCase()} ${operation.path}`;
  if (description && description !== summary && description !== methodPath && description !== operation.path) return description;
  if (summary && summary !== methodPath && summary !== operation.path) return summary;
  return '';
}

export function getResponseText(response: ExplorerResponse | null, responseTab: ResponseTab, codeTab: CodeTab) {
  if (!response) return '';
  if (responseTab === 'headers') return JSON.stringify(response.headers, null, 2);
  if (responseTab === 'code') return response.code[codeTab];
  if (responseTab === 'preview') {
    return typeof response.preview === 'string' ? response.preview : JSON.stringify(response.preview, null, 2);
  }
  return prettifyContent(response.body, response.contentType);
}

export function buildPathRegex(template: string) {
  const keys: string[] = [];
  const pattern = template.replace(/[.*+?^${}()|[\]\\]/g, '\\$&').replace(/\\\{([^}]+)\\\}/g, (_match, key: string) => {
    keys.push(key);
    return '([^/]+)';
  });
  return { regex: new RegExp(`^${pattern}$`), keys };
}

export function resolveReplayPathValues(template: string, route: string, initialValues?: Record<string, string | null>) {
  const { regex, keys } = buildPathRegex(template);
  const match = regex.exec(route);
  const pathValues: Record<string, string | null> = { ...(initialValues || {}) };
  if (!match) return pathValues;

  keys.forEach((key, index) => {
    if (!(key in pathValues) || pathValues[key] == null || pathValues[key] === '') {
      pathValues[key] = decodeURIComponent(match[index + 1]);
    }
  });

  return pathValues;
}

export function findOperationForReplay(operations: ExplorerOperation[], replay: ReplayRequest) {
  for (const operation of operations) {
    if (operation.method.toLowerCase() !== replay.method.toLowerCase()) continue;
    if (replay.routeTemplate && replay.routeTemplate === operation.path) {
      return {
        operationId: operation.id,
        pathValues: resolveReplayPathValues(operation.path, replay.route, replay.pathValues),
      };
    }

    const pathValues = resolveReplayPathValues(operation.path, replay.route, replay.pathValues);
    if (Object.keys(pathValues).length === 0 && operation.path !== replay.route) continue;

    return {
      operationId: operation.id,
      pathValues,
    };
  }
  return null;
}

/** Every operation in an OpenAPI document (get, post, put, delete, patch, head), with path-level parameters merged. */
export function buildExplorerOperations(spec: OpenApiSpec | null): ExplorerOperation[] {
  if (!spec?.paths) return [];
  return Object.entries(spec.paths).flatMap(([path, pathItem]) =>
    Object.entries(pathItem)
      .flatMap(([method, operation]) => {
        if (!['get', 'post', 'put', 'delete', 'patch', 'head'].includes(method)) return [];
        if (!isOperationSource(operation)) return [];

        const pathLevelParameters = Array.isArray(pathItem.parameters) ? pathItem.parameters : [];
        const mergedParameters = [...pathLevelParameters, ...(operation.parameters || [])];
        const parameters = mergedParameters.filter((parameter, index) => {
          const current = `${parameter.in}:${parameter.name}`;
          return mergedParameters.findIndex((item) => `${item.in}:${item.name}` === current) === index;
        });
        const requestBodyContent = operation.requestBody?.content || {};
        const preferredContentType = requestBodyContent['application/json']
          ? 'application/json'
          : Object.keys(requestBodyContent)[0] || '';

        return [{
          id: operation.operationId || `${method}:${path}`,
          method,
          path,
          summary: operation.summary || `${method.toUpperCase()} ${path}`,
          description: operation.description || '',
          tag: operation.tags?.[0] || 'General',
          parameters,
          requestBody: requestBodyContent[preferredContentType] || null,
          requestBodyContentType: preferredContentType,
        }];
      }),
  );
}

/** The category picker's options: 'All' then each tag once, in document order. */
export function explorerTags(operations: ExplorerOperation[]): string[] {
  return ['All', ...new Set(operations.map((operation) => operation.tag))];
}

/** Operations in a category ('All' for every one) whose summary, path, or tag contains the filter text. */
export function filterExplorerOperations(operations: ExplorerOperation[], selectedTag: string, operationFilter: string): ExplorerOperation[] {
  return operations.filter((operation) => {
    const matchesTag = selectedTag === 'All' || operation.tag === selectedTag;
    const haystack = `${operation.summary} ${operation.path} ${operation.tag}`.toLowerCase();
    const matchesFilter = !operationFilter || haystack.includes(operationFilter.toLowerCase());
    return matchesTag && matchesFilter;
  });
}

/** Header parameters the request builder offers (the session token and Content-Type are set by the explorer). */
export function editableHeaderParameter(parameter: OpenApiParameter): boolean {
  return parameter.in === 'header' && !['x-token', 'authorization', 'content-type'].includes(parameter.name.toLowerCase());
}
