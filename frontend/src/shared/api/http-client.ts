import type { ZodType } from 'zod'

export class HttpError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.name = 'HttpError'
    this.status = status
  }
}

interface GetJsonOptions<TResponse> {
  path: string
  schema: ZodType<TResponse>
  signal: AbortSignal
}

interface WriteJsonOptions<TRequest, TResponse> {
  path: string
  body: TRequest
  schema: ZodType<TResponse>
  signal: AbortSignal
}

async function parseJsonResponse<TResponse>(
  response: Response,
  schema: ZodType<TResponse>,
): Promise<TResponse> {
  if (response.status === 401) {
    throw new HttpError(response.status, 'Your PlaceContext session has expired.')
  }

  if (!response.ok) {
    throw new HttpError(response.status, `Request failed with status ${String(response.status)}.`)
  }

  const body: unknown = await response.json()
  return schema.parse(body)
}

export async function getJson<TResponse>({
  path,
  schema,
  signal,
}: GetJsonOptions<TResponse>): Promise<TResponse> {
  const response = await fetch(path, {
    method: 'GET',
    credentials: 'same-origin',
    headers: { Accept: 'application/json' },
    signal,
  })

  return parseJsonResponse(response, schema)
}

export async function postJson<TRequest, TResponse>({
  path,
  body,
  schema,
  signal,
}: WriteJsonOptions<TRequest, TResponse>): Promise<TResponse> {
  const response = await fetch(path, {
    method: 'POST',
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
      'X-Requested-With': 'XMLHttpRequest',
    },
    body: JSON.stringify(body),
    signal,
  })

  return parseJsonResponse(response, schema)
}

export async function putJson<TRequest, TResponse>({
  path,
  body,
  schema,
  signal,
}: WriteJsonOptions<TRequest, TResponse>): Promise<TResponse> {
  const response = await fetch(path, {
    method: 'PUT',
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
      'X-Requested-With': 'XMLHttpRequest',
    },
    body: JSON.stringify(body),
    signal,
  })

  return parseJsonResponse(response, schema)
}

export async function deleteJson<TResponse>({
  path,
  schema,
  signal,
}: GetJsonOptions<TResponse>): Promise<TResponse> {
  const response = await fetch(path, {
    method: 'DELETE',
    credentials: 'same-origin',
    headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
    signal,
  })
  return parseJsonResponse(response, schema)
}

export async function deleteRequest(path: string, signal: AbortSignal): Promise<void> {
  const response = await fetch(path, {
    method: 'DELETE',
    credentials: 'same-origin',
    headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
    signal,
  })
  if (response.status === 401) throw new HttpError(response.status, 'Your PlaceContext session has expired.')
  if (!response.ok) throw new HttpError(response.status, `Request failed with status ${String(response.status)}.`)
}
