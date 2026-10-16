import { useEffect, useState } from 'react'
import { api } from '../api.js'
import { IconGoogle, IconMicrosoft } from './icons/Icons.jsx'
import './OidcButtons.css'

const ICONS = { google: IconGoogle, microsoft: IconMicrosoft }

// where to land after the provider sends the browser back, read by /auth/callback
export const OIDC_RETURN_KEY = 'oidc-return'

// "Continue with Google / Microsoft", only for the providers this deployment has configured
function OidcButtons({ returnTo = '/' }) {
  const [providers, setProviders] = useState([])
  const [pending, setPending] = useState(null)
  const [error, setError] = useState('')

  useEffect(() => {
    let cancelled = false
    api
      .get('/api/auth/oidc/providers')
      .then((list) => {
        if (!cancelled) setProviders(list ?? [])
      })
      .catch(() => {
        // no buttons is the right fallback: password sign-in still works
      })
    return () => {
      cancelled = true
    }
  }, [])

  // the url comes from the api because it carries a sealed state and sets the binding cookie
  async function start(provider) {
    setError('')
    setPending(provider.key)
    try {
      const { url } = await api.get(`/api/auth/oidc/${provider.key}/authorize`)
      try {
        sessionStorage.setItem(OIDC_RETURN_KEY, returnTo)
      } catch {
        // storage unavailable, the dashboard is a fine landing page
      }
      window.location.assign(url)
    } catch (error) {
      setError(error.message)
      setPending(null)
    }
  }

  if (providers.length === 0) return null

  return (
    <div className="oidc">
      <p className="oidc__divider">
        <span>or</span>
      </p>
      {providers.map((provider) => {
        const Icon = ICONS[provider.key]
        return (
          <button
            key={provider.key}
            type="button"
            className="button oidc__button"
            onClick={() => start(provider)}
            disabled={pending !== null}
          >
            {Icon && <Icon />}
            {pending === provider.key ? 'Redirecting…' : `Continue with ${provider.name}`}
          </button>
        )
      })}
      {error && (
        <p className="auth__error" role="alert">
          {error}
        </p>
      )}
    </div>
  )
}

export default OidcButtons
