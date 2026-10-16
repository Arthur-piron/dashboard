import { useSyncExternalStore } from 'react'

const TOKEN_KEY = 'auth-token'
const listeners = new Set()

function readToken() {
  try {
    return localStorage.getItem(TOKEN_KEY)
  } catch {
    return null
  }
}

function notify() {
  listeners.forEach((listener) => listener())
}

export function getToken() {
  return readToken()
}

export function setToken(token) {
  localStorage.setItem(TOKEN_KEY, token)
  notify()
}

export function clearToken() {
  try {
    localStorage.removeItem(TOKEN_KEY)
  } catch {
    // storage unavailable, nothing to clear
  }
  notify()
}

function subscribe(listener) {
  listeners.add(listener)
  // another tab logging out must log this one out too
  const onStorage = (event) => {
    if (event.key === TOKEN_KEY) listener()
  }
  window.addEventListener('storage', onStorage)
  return () => {
    listeners.delete(listener)
    window.removeEventListener('storage', onStorage)
  }
}

// re-renders on login, logout and on a 401 anywhere in the app
export function useToken() {
  return useSyncExternalStore(subscribe, readToken)
}

export class ApiError extends Error {
  constructor(status, problem) {
    // a 5xx body can be a raw stack trace in development, never show it
    const message =
      status >= 500
        ? 'The server ran into a problem, try again in a moment.'
        : problemMessage(problem) ?? `Request failed (${status}).`
    super(message)
    this.status = status
    this.problem = problem
  }
}

function problemMessage(problem) {
  if (!problem) return null
  if (typeof problem === 'string') return problem.length <= 200 ? problem : null
  if (problem.errors) {
    const messages = Object.values(problem.errors).flat()
    if (messages.length) return messages.join(' ')
  }
  return problem.detail ?? problem.title ?? null
}

// field errors of a 400 problem document, keyed by field name
export function fieldErrors(error) {
  const errors = error?.problem?.errors
  if (!errors) return {}
  return Object.fromEntries(Object.entries(errors).map(([key, list]) => [key, list.join(' ')]))
}

export async function apiFetch(path, { method = 'GET', body, headers } = {}) {
  const token = readToken()
  const init = { method, headers: { ...headers } }
  if (token) init.headers.Authorization = `Bearer ${token}`
  if (body !== undefined) {
    init.headers['Content-Type'] = 'application/json'
    init.body = JSON.stringify(body)
  }

  let response
  try {
    response = await fetch(path, init)
  } catch {
    throw new ApiError(0, 'Could not reach the server.')
  }

  const text = await response.text()
  let payload = null
  if (text) {
    try {
      payload = JSON.parse(text)
    } catch {
      payload = text
    }
  }

  // an expired token: RequireAuth sees the cleared token and redirects to /login
  if (response.status === 401 && token) clearToken()
  if (!response.ok) throw new ApiError(response.status, payload)
  return payload
}

export const api = {
  get: (path) => apiFetch(path),
  post: (path, body) => apiFetch(path, { method: 'POST', body }),
  put: (path, body) => apiFetch(path, { method: 'PUT', body }),
  patch: (path, body) => apiFetch(path, { method: 'PATCH', body }),
  delete: (path) => apiFetch(path, { method: 'DELETE' }),
}
