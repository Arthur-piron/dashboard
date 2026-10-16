import { useState } from 'react'
import { Link, Navigate, useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { api, setToken, useToken } from './api.js'
import OidcButtons from './components/OidcButtons.jsx'
import './App.css'

const LOGIN_ERRORS = {
  401: 'Invalid email or password.',
  403: 'Confirm your email first, the link is in your inbox.',
}

const PROVIDER_NAMES = { google: 'Google', microsoft: 'Microsoft' }

// the api sends a failed google / microsoft sign-in back here with ?oidc=failed&reason=…
function oidcError(search) {
  if (search.get('oidc') !== 'failed') return ''
  const name = PROVIDER_NAMES[search.get('provider')] ?? 'that provider'
  switch (search.get('reason')) {
    case 'denied':
      return 'Sign-in was cancelled.'
    case 'provider_error':
      return `${name} refused the sign-in request. Its app registration needs fixing, so tell whoever runs Dashboard.`
    case 'invalid_state':
      return 'That sign-in expired or was finished in another browser. Try again.'
    case 'no_email':
      return `${name} did not share an email address, and your account needs one.`
    case 'email_in_use':
      return 'An account already uses this email. Log in with your password.'
    case 'email_not_confirmed':
      return 'An account with this email is waiting for confirmation. Open the link in your inbox, then try again.'
    case 'account_failed':
      return 'Your account could not be created. Try again in a moment.'
    default:
      return `Signing in with ${name} did not work. Try again in a moment.`
  }
}

// page /login : envoie email + mot de passe, stocke le jwt et renvoie vers le dashboard
function Login() {
  const navigate = useNavigate()
  const location = useLocation()
  const token = useToken()
  const [searchParams] = useSearchParams()
  // back to the page that sent us here, if any
  const from = location.state?.from?.pathname ?? '/'
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState(() => oidcError(searchParams))
  const [isSubmitting, setIsSubmitting] = useState(false)

  // POST /auth/login, then setToken stores it under auth-token
  async function handleSubmit(event) {
    event.preventDefault()
    setError('')
    setIsSubmitting(true)

    try {
      const session = await api.post('/api/auth/login', { email, password })
      setToken(session.token)
      navigate(from, { replace: true })
    } catch (error) {
      setError(LOGIN_ERRORS[error.status] ?? error.message)
    } finally {
      setIsSubmitting(false)
    }
  }

  if (token) return <Navigate to={from} replace />

  return (
    <div className="app-shell">
      <main className="auth">
        <div className="auth__card glass">
          <img className="auth__logo" src="/favicon.svg" alt="" />
          <div className="auth__header">
            <h1 className="auth__title">Log in</h1>
            <p className="dashboard__greeting-subtitle">Welcome back to your dashboard.</p>
          </div>

          <form className="auth__form" onSubmit={handleSubmit}>
            <input
              className="field"
              type="email"
              placeholder="Email"
              autoComplete="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
            />
            <input
              className="field"
              type="password"
              placeholder="Password"
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
            />
            {error && <p className="auth__error" role="alert">{error}</p>}
            <button type="submit" className="button button--primary" disabled={isSubmitting}>
              {isSubmitting ? 'Logging in…' : 'Log in'}
            </button>
          </form>

          <OidcButtons returnTo={from} />

          <p className="auth__footer">
            New here? <Link to="/register">Create an account</Link>
          </p>
        </div>
      </main>
    </div>
  )
}

export default Login
