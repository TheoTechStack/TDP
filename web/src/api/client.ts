import type {
  DisputeDetail,
  DisputeListItem,
  DisputeOutcome,
  DisputeReason,
  DisputeStatus,
  LoginResponse,
  Paged,
  Transaction,
  User,
} from './types'

/** An RFC 9457 problem returned by the API, with the stable `code` it attaches to every error. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string | undefined,
    message: string,
    readonly fieldErrors: Record<string, string[]> = {},
  ) {
    super(message)
    this.name = 'ApiError'
  }

  static async fromResponse(response: Response): Promise<ApiError> {
    let body: { title?: string; code?: string; errors?: Record<string, string[]> } = {}
    try {
      body = await response.json()
    } catch {
      // not JSON (e.g. a proxy error page)
    }
    const message =
      body.errors && Object.keys(body.errors).length > 0
        ? Object.values(body.errors).flat().join(' ')
        : body.title ?? fallbackMessage(response.status)
    return new ApiError(response.status, body.code, message, body.errors ?? {})
  }
}

function fallbackMessage(status: number): string {
  if (status === 429) return 'Too many attempts. Please wait a minute and try again.'
  if (status >= 500) return 'Something went wrong on our side. Please try again.'
  return `Request failed (${status}).`
}

type TokenProvider = () => string | null
type UnauthorizedHandler = () => void

let getToken: TokenProvider = () => null
let onUnauthorized: UnauthorizedHandler = () => {}

/** Wired up once by the auth store, so the client has no direct dependency on Pinia. */
export function configureClient(options: { getToken: TokenProvider; onUnauthorized: UnauthorizedHandler }) {
  getToken = options.getToken
  onUnauthorized = options.onUnauthorized
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' }
  const token = getToken()
  if (token) headers.Authorization = `Bearer ${token}`
  if (body !== undefined) headers['Content-Type'] = 'application/json'

  const response = await fetch(path, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  })

  if (response.status === 401 && token) {
    onUnauthorized()
  }
  if (!response.ok) {
    throw await ApiError.fromResponse(response)
  }
  return (response.status === 204 ? undefined : await response.json()) as T
}

function query(params: Record<string, string | number | undefined | null>): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') search.set(key, String(value))
  }
  const text = search.toString()
  return text ? `?${text}` : ''
}

export const api = {
  login: (email: string, password: string) => request<LoginResponse>('POST', '/api/auth/login', { email, password }),
  me: () => request<User>('GET', '/api/auth/me'),

  transactions: (params: { page?: number; pageSize?: number; search?: string }) =>
    request<Paged<Transaction>>('GET', `/api/transactions${query(params)}`),

  raiseDispute: (transactionId: string, reason: DisputeReason, description: string) =>
    request<DisputeDetail>('POST', `/api/transactions/${transactionId}/disputes`, { reason, description }),

  disputes: (params: { page?: number; pageSize?: number; status?: DisputeStatus }) =>
    request<Paged<DisputeListItem>>('GET', `/api/disputes${query(params)}`),
  dispute: (id: string) => request<DisputeDetail>('GET', `/api/disputes/${id}`),

  withdraw: (id: string) => request<DisputeDetail>('POST', `/api/disputes/${id}/withdraw`),
  startReview: (id: string) => request<DisputeDetail>('POST', `/api/disputes/${id}/review`),
  resolve: (id: string, outcome: DisputeOutcome, note: string) =>
    request<DisputeDetail>('POST', `/api/disputes/${id}/resolve`, { outcome, note }),
}
