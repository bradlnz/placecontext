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

interface DeleteJsonOptions<TRequest, TResponse> extends GetJsonOptions<TResponse> {
  body: TRequest
}

async function parseJsonResponse<TResponse>(
  response: Response,
  schema: ZodType<TResponse>,
): Promise<TResponse> {
  if (response.status === 401) {
    throw new HttpError(response.status, 'Your PlaceContext session has expired.')
  }

  if (!response.ok) {
    throw new HttpError(response.status, await readErrorMessage(response))
  }

  const body: unknown = await response.json()
  return schema.parse(body)
}

async function readErrorMessage(response: Response): Promise<string> {
  try {
    const body: unknown = await response.json()
    if (typeof body === 'object' && body !== null) {
      const error: unknown = (body as Record<string, unknown>).error
      if (typeof error === 'string' && error.trim() !== '') return error
    }
  } catch {
    // The endpoint returned no JSON error contract; use the stable status fallback.
  }
  return `Request failed with status ${String(response.status)}.`
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

export async function postRequest(path: string, body: unknown, signal: AbortSignal): Promise<void> {
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
  if (response.status === 401)
    throw new HttpError(response.status, 'Your PlaceContext session has expired.')
  if (!response.ok) throw new HttpError(response.status, await readErrorMessage(response))
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
    headers: {
      Accept: 'application/json',
      'X-Requested-With': 'XMLHttpRequest',
    },
    signal,
  })
  return parseJsonResponse(response, schema)
}

export async function deleteRequest(path: string, signal: AbortSignal): Promise<void> {
  const response = await fetch(path, {
    method: 'DELETE',
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
      'X-Requested-With': 'XMLHttpRequest',
    },
    signal,
  })
  if (response.status === 401)
    throw new HttpError(response.status, 'Your PlaceContext session has expired.')
  if (!response.ok) throw new HttpError(response.status, await readErrorMessage(response))
}

export async function deleteJsonWithBody<TRequest, TResponse>({
  path,
  body,
  schema,
  signal,
}: DeleteJsonOptions<TRequest, TResponse>): Promise<TResponse> {
  const response = await fetch(path, {
    method: 'DELETE',
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

export async function putRequest(path: string, body: unknown, signal: AbortSignal): Promise<void> {
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
  if (response.status === 401)
    throw new HttpError(response.status, 'Your PlaceContext session has expired.')
  if (!response.ok) throw new HttpError(response.status, await readErrorMessage(response))
}
