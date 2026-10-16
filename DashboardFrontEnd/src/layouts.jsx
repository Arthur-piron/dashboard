import { useState } from 'react'
import { Navigate, Outlet, useLocation } from 'react-router-dom'
import Taskbar from './components/Taskbar.jsx'
import ErrorBoundary from './components/ErrorBoundary.jsx'
import { useToken } from './api.js'
import { useTheme } from './useTheme.js'
import { isAdmin, useCurrentUser } from './useCurrentUser.js'

// every logged-in page: redirect before rendering, so a logged-out user never sees a broken page
export function AppLayout() {
  const token = useToken()
  const location = useLocation()
  const { theme, toggleTheme } = useTheme()
  const [search, setSearch] = useState({ path: location.pathname, query: '' })
  // the search belongs to the page it was typed on, leaving the page clears it
  if (search.path !== location.pathname) setSearch({ path: location.pathname, query: '' })
  const query = search.path === location.pathname ? search.query : ''

  if (!token) return <Navigate to="/login" replace state={{ from: location }} />

  return (
    <div className="app-shell">
      <Taskbar
        theme={theme}
        onToggleTheme={toggleTheme}
        query={query}
        onQueryChange={(value) => setSearch({ path: location.pathname, query: value })}
      />
      <ErrorBoundary key={location.pathname}>
        <Outlet context={{ query }} />
      </ErrorBoundary>
    </div>
  )
}

export function AuthLayout() {
  useTheme()
  return <Outlet />
}

export function RequireAdmin({ children }) {
  const { user, loading } = useCurrentUser()
  if (loading) return null
  if (!isAdmin(user)) return <Navigate to="/" replace />
  return children
}
