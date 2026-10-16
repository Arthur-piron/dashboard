import { useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from './api.js'
import OidcButtons from './components/OidcButtons.jsx'
import './App.css'

// page /register : cree le compte puis demande de confirmer par e-mail
function Register() {
  const [email, setEmail] = useState('')
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [isRegistered, setIsRegistered] = useState(false)

  // POST /auth/register, affiche la premiere erreur de validation du back
  async function handleSubmit(event) {
    event.preventDefault()
    setError('')
    setIsSubmitting(true)

    try {
      await api.post('/api/auth/register', { email, username, password })
      setIsRegistered(true)
    } catch (error) {
      const firstError = error.problem?.errors && Object.values(error.problem.errors)[0]?.[0]
      setError(firstError ?? error.message)
    } finally {
      setIsSubmitting(false)
    }
  }

  // compte cree, en attente du clic sur le lien de confirmation
  if (isRegistered) {
    return (
      <div className="app-shell">
        <main className="auth">
          <div className="auth__card glass">
            <img className="auth__logo" src="/favicon.svg" alt="" />
            <div className="auth__header">
              <h1 className="auth__title">Check your email</h1>
              <p className="dashboard__greeting-subtitle">
                We sent a confirmation link to {email}. Click it before logging in.
              </p>
            </div>
            <Link className="button button--primary" to="/login">
              Go to login
            </Link>
          </div>
        </main>
      </div>
    )
  }

  return (
    <div className="app-shell">
      <main className="auth">
        <div className="auth__card glass">
          <img className="auth__logo" src="/favicon.svg" alt="" />
          <div className="auth__header">
            <h1 className="auth__title">Register</h1>
            <p className="dashboard__greeting-subtitle">Create an account to build your dashboard.</p>
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
              type="text"
              placeholder="Username"
              autoComplete="username"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              required
            />
            <input
              className="field"
              type="password"
              placeholder="Password"
              autoComplete="new-password"
              aria-describedby="password-rules"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
            />
            <p className="form-hint" id="password-rules">
              At least 8 characters, with an upper case letter, a lower case letter and a digit.
            </p>
            {error && <p className="auth__error" role="alert">{error}</p>}
            <button type="submit" className="button button--primary" disabled={isSubmitting}>
              {isSubmitting ? 'Registering…' : 'Register'}
            </button>
          </form>

          <OidcButtons />

          <p className="auth__footer">
            Already have an account? <Link to="/login">Log in</Link>
          </p>
        </div>
      </main>
    </div>
  )
}

export default Register
