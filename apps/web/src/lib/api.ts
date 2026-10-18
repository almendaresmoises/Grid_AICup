export class ApiError extends Error {
  status?: number
  code?: string
  details?: unknown

  constructor(message: string, status?: number, code?: string, details?: unknown) {
    super(message)
    this.name = "ApiError"
    this.status = status
    this.code = code
    this.details = details
  }
}

const tokenKey = "grid_token"
const apiBase = (import.meta.env.VITE_API_URL ?? "").replace(/\/$/, "")

function resolveUrl(path: string) {
  if (!apiBase || path.startsWith("http")) return path
  return `${apiBase}${path}`
}

export function getToken() {
  return sessionStorage.getItem(tokenKey)
}

export function setToken(token: string) {
  sessionStorage.setItem(tokenKey, token)
}

export function clearToken() {
  sessionStorage.removeItem(tokenKey)
}

export async function login(username: string, password: string) {
  const body = new URLSearchParams({
    grant_type: "password",
    client_id: "Grid_App",
    username,
    password,
    scope: "profile email roles Grid",
  })
  const response = await fetch(resolveUrl("/connect/token"), {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body,
  })
  const payload = await response.json().catch(() => ({}))
  if (!response.ok) {
    throw new ApiError(
      payload.error_description || payload.error || "Sign-in failed"
    )
  }
  setToken(payload.access_token)
  return payload.access_token as string
}

export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers)
  headers.set("Accept", "application/json")
  if (
    init.body &&
    !(init.body instanceof FormData) &&
    !headers.has("Content-Type")
  ) {
    headers.set("Content-Type", "application/json")
  }
  const token = getToken()
  if (token) {
    headers.set("Authorization", `Bearer ${token}`)
  }

  const response = await fetch(resolveUrl(path), { ...init, headers })
  if (response.status === 401) {
    clearToken()
    if (!window.location.pathname.startsWith("/login")) {
      window.location.assign("/login")
    }
    throw new ApiError("Sign in again")
  }

  if (response.status === 204) {
    return undefined as T
  }

  const text = await response.text()
  const payload = text ? JSON.parse(text) : null
  if (!response.ok) {
    const message =
      payload?.error?.message ||
      payload?.error_description ||
      payload?.message ||
      text ||
      "Request failed"
    const code = payload?.error?.code || payload?.code
    const details = payload?.error?.details || payload?.details
    throw new ApiError(message, response.status, code, details)
  }
  return payload as T
}
