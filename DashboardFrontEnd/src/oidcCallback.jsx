import { useEffect, useState } from 'react'
import { Navigate, useNavigate } from 'react-router-dom'
import { setToken } from './api.js'
import { OIDC_RETURN_KEY } from './components/OidcButtons.jsx'

// the api sends the browser here with #token=…&expiresAt=…: a fragment never reaches a server
function readSession() {
  const fragment = new URLSearchParams(window.location.hash.slice(1))
  const token = fragment.get('token')
  return token ? { token } : null
}

// only a path of this app, so a tampered value cannot send the user elsewhere
function returnPath() {
  let stored = null
  try {
    stored = sessionStorage.getItem(OIDC_RETURN_KEY)
    sessionStorage.removeItem(OIDC_RETURN_KEY)
  } catch {
    // storage unavailable
  }
  return stored?.startsWith('/') && !stored.startsWith('//') ? stored : '/'
}

// page /auth/callback : stores the jwt from a google / microsoft sign-in, then leaves
export default function OidcCallback() {
  const navigate = useNavigate()
  const [session] = useState(readSession)

  useEffect(() => {
    if (!session) return
    setToken(session.token)
    // replace drops the entry holding the token from the history
    navigate(returnPath(), { replace: true })
  }, [session, navigate])

  if (!session) return <Navigate to="/login" replace />
  return null
}
