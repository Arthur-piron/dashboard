import { useCallback, useEffect, useState } from 'react'
import { useOutletContext } from 'react-router-dom'
import ConfirmDialog from './components/ConfirmDialog.jsx'
import Notice from './components/Notice.jsx'
import { api } from './api.js'
import { isAdmin, useCurrentUser } from './useCurrentUser.js'
import './App.css'
import './admin.css'

function formatDate(iso) {
  return new Date(iso).toLocaleDateString([], { year: 'numeric', month: 'short', day: 'numeric' })
}

function UserRow({ account, self, busy, onToggleAdmin, onDelete }) {
  const admin = isAdmin(account)
  return (
    <li className="admin-row">
      <div className="admin-row__identity">
        <p className="admin-row__name">
          {account.username}
          {self && <span className="admin-row__you"> · you</span>}
        </p>
        <p className="admin-row__email">{account.email}</p>
      </div>

      <div className="admin-row__meta">
        {admin && <span className="admin-pill admin-pill--admin">Admin</span>}
        {!account.emailConfirmed && <span className="admin-pill admin-pill--pending">Unconfirmed</span>}
        <span className="admin-row__stat">
          {account.widgetInstances} widget{account.widgetInstances === 1 ? '' : 's'}
        </span>
        <span className="admin-row__stat">
          {account.subscriptions} connection{account.subscriptions === 1 ? '' : 's'}
        </span>
        <span className="admin-row__stat">Joined {formatDate(account.createdAt)}</span>
      </div>

      {/* the api refuses self-revoke and self-delete, so the buttons are disabled rather than failing */}
      <div className="admin-row__actions">
        <button
          type="button"
          className="button button--small"
          disabled={busy || (self && admin)}
          title={self && admin ? 'You cannot revoke your own role' : undefined}
          onClick={onToggleAdmin}
        >
          {admin ? 'Revoke admin' : 'Make admin'}
        </button>
        <button
          type="button"
          className="button button--small button--danger"
          disabled={busy || self}
          title={self ? 'You cannot delete your own account' : undefined}
          onClick={onDelete}
        >
          Delete
        </button>
      </div>
    </li>
  )
}

// page /admin: every account, with role and deletion controls
function Admin() {
  const { query } = useOutletContext()
  const { user } = useCurrentUser()
  const [users, setUsers] = useState(null)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState(null)
  const [busyId, setBusyId] = useState(null)
  const [deleting, setDeleting] = useState(null)

  const load = useCallback(() => {
    return api
      .get('/api/admin/users')
      .then((list) => {
        setUsers(list)
        setError('')
      })
      .catch((e) => {
        if (e.status !== 401) setError(e.status === 403 ? 'You are not an administrator.' : e.message)
      })
  }, [])

  useEffect(() => {
    load()
  }, [load])

  async function toggleAdmin(account) {
    setBusyId(account.id)
    try {
      if (isAdmin(account)) await api.delete(`/api/admin/users/${account.id}/admin`)
      else await api.post(`/api/admin/users/${account.id}/admin`)
      setNotice({
        tone: 'info',
        text: `${account.username} is ${isAdmin(account) ? 'no longer' : 'now'} an administrator. It takes effect on their next login.`,
      })
      await load()
    } catch (e) {
      if (e.status !== 401) setNotice({ tone: 'error', text: e.message })
    } finally {
      setBusyId(null)
    }
  }

  async function deleteUser(account) {
    try {
      await api.delete(`/api/admin/users/${account.id}`)
    } catch (e) {
      if (e.status !== 404) throw e
    }
    setUsers((current) => current.filter((u) => u.id !== account.id))
  }

  const needle = query.toLowerCase()
  const visible = (users ?? []).filter((u) => `${u.username} ${u.email}`.toLowerCase().includes(needle))

  return (
    <main className="dashboard">
      <div className="dashboard__greeting">
        <h1 className="dashboard__greeting-title">Users</h1>
        <p className="dashboard__greeting-subtitle">
          {users ? `${users.length} account${users.length === 1 ? '' : 's'}` : 'Every account on this deployment.'}
        </p>
      </div>

      {notice && (
        <Notice tone={notice.tone} onDismiss={() => setNotice(null)}>
          {notice.text}
        </Notice>
      )}
      {error && <Notice tone="error">{error}</Notice>}

      {users === null && !error && <div className="admin-list admin-list--loading" aria-busy="true" />}

      {users && visible.length === 0 && (
        <p className="dashboard__no-match">{query ? `No user matches “${query}”.` : 'No users yet.'}</p>
      )}

      {visible.length > 0 && (
        <ul className="admin-list">
          {visible.map((account) => (
            <UserRow
              key={account.id}
              account={account}
              self={account.id === user?.id}
              busy={busyId === account.id}
              onToggleAdmin={() => toggleAdmin(account)}
              onDelete={() => setDeleting(account)}
            />
          ))}
        </ul>
      )}

      {deleting && (
        <ConfirmDialog
          title={`Delete ${deleting.username}?`}
          message="Their account, widgets and connections are removed for good."
          confirmLabel="Delete"
          onConfirm={() => deleteUser(deleting)}
          onClose={() => setDeleting(null)}
        />
      )}
    </main>
  )
}

export default Admin
