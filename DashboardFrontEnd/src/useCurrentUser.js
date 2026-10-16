import { useEffect, useState } from 'react'
import { api, useToken } from './api.js'

// one /auth/me request per token, shared by the taskbar and every page
let cached = { token: null, promise: null }

function loadUser(token) {
  if (cached.token !== token) {
    cached = { token, promise: api.get('/api/auth/me').catch(() => null) }
  }
  return cached.promise
}

// { user, loading }, user is null when logged out or on error
export function useCurrentUser() {
  const token = useToken()
  const [state, setState] = useState({ token: null, user: null })

  useEffect(() => {
    if (!token) return
    let cancelled = false
    loadUser(token).then((user) => {
      if (!cancelled) setState({ token, user })
    })
    return () => {
      cancelled = true
    }
  }, [token])

  const current = token && state.token === token
  return { user: current ? state.user : null, loading: Boolean(token) && !current }
}

export function isAdmin(user) {
  return Boolean(user?.roles?.includes('Admin'))
}
