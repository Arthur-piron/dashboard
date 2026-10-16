import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from './api.js'
import './App.css'

function linkParams() {
  const search = new URLSearchParams(window.location.search)
  const userId = search.get('userId')
  const token = search.get('token')
  return userId && token ? { userId, token } : null
}

const INCOMPLETE = 'This link is incomplete. Open the link from your email again.'

// page /confirm: validates the account with the userId and token from the email link
export default function Confirm() {
  const [params] = useState(linkParams)
  const [status, setStatus] = useState(params ? 'loading' : 'error')
  const [message, setMessage] = useState(params ? '' : INCOMPLETE)
  // strict mode mounts twice and the token is single-use
  const done = useRef(false)

  useEffect(() => {
    if (done.current || !params) return
    done.current = true

    api
      .get(`/api/auth/confirm?${new URLSearchParams(params)}`)
      .then(() => setStatus('success'))
      .catch((error) => {
        setStatus('error')
        setMessage(error.status === 0 ? error.message : 'This link is invalid or has expired.')
      })
  }, [params])

  return (
    <div className="app-shell">
      <main className="auth">
        <div className="auth__card glass">
          <img className="auth__logo" src="/favicon.svg" alt="" />
          <div className="auth__header">
            <h1 className="auth__title">
              {status === 'loading' && 'Confirming…'}
              {status === 'success' && 'Email confirmed'}
              {status === 'error' && 'Confirmation failed'}
            </h1>
            {status === 'success' && (
              <p className="dashboard__greeting-subtitle">Your account is ready, you can now log in.</p>
            )}
            {status === 'error' && (
              <p className="auth__status auth__status--error" role="alert">
                {message}
              </p>
            )}
          </div>
          {status !== 'loading' && (
            <Link className="button button--primary" to="/login">
              Go to login
            </Link>
          )}
        </div>
      </main>
    </div>
  )
}
