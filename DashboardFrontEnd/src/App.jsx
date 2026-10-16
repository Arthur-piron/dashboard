import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Link, useOutletContext } from 'react-router-dom'
import DashboardGrid from './components/DashboardGrid.jsx'
import WidgetDialog from './components/WidgetDialog.jsx'
import ConfirmDialog from './components/ConfirmDialog.jsx'
import Notice from './components/Notice.jsx'
import { IconPencil, IconPlus } from './components/icons/Icons.jsx'
import { api } from './api.js'
import { useCurrentUser } from './useCurrentUser.js'
import { describeConfig } from './widgetConfig.js'
import { bottomRow } from './widgetLayout.js'
import './App.css'

// the server owns refreshing; this only re-reads its cache, so the rate is about feeling live
const POLL_MS = 15_000
// a new or reconfigured widget is fetched by the server right away, read it back soon after
const SOON_MS = 2_500

function greeting() {
  const hour = new Date().getHours()
  if (hour < 12) return 'Good morning'
  if (hour < 18) return 'Good afternoon'
  return 'Good evening'
}

function samePosition(a, b) {
  return a.x === b.x && a.y === b.y && a.width === b.width && a.height === b.height
}

function matches(instance, widget, query) {
  const haystack = [widget?.name, instance.widgetKey, instance.serviceKey, describeConfig(instance.config)]
    .join(' ')
    .toLowerCase()
  return haystack.includes(query.toLowerCase())
}

function App() {
  const { query } = useOutletContext()
  const { user } = useCurrentUser()
  const [instances, setInstances] = useState(null)
  const [results, setResults] = useState({})
  const [catalogue, setCatalogue] = useState({ widgets: [], services: [] })
  const [loadError, setLoadError] = useState('')
  const [notice, setNotice] = useState(null)
  const [dialog, setDialog] = useState(null)
  const [editing, setEditing] = useState(false)
  const soonTimer = useRef(null)

  const widgetsByKey = useMemo(
    () => Object.fromEntries(catalogue.widgets.map((w) => [w.key, w])),
    [catalogue.widgets],
  )

  const loadInstances = useCallback(() => {
    return api
      .get('/api/widget-instances')
      .then((list) => {
        setInstances(list)
        setLoadError('')
      })
      .catch((error) => {
        if (error.status !== 401) setLoadError(error.message)
      })
  }, [])

  const refreshResults = useCallback(() => {
    api
      .get('/api/widget-instances/data')
      .then((list) => setResults(Object.fromEntries(list.map((r) => [r.instanceId, r]))))
      // a failed poll keeps the last data on screen, the next one will retry
      .catch(() => {})
  }, [])

  useEffect(() => {
    loadInstances()
    Promise.all([api.get('/api/widgets'), api.get('/api/services')])
      .then(([widgets, services]) => setCatalogue({ widgets, services }))
      .catch(() => {})
  }, [loadInstances])

  // one poll for every widget, paused while the tab is hidden
  useEffect(() => {
    let timer = null
    function start() {
      refreshResults()
      timer = setInterval(refreshResults, POLL_MS)
    }
    function onVisibility() {
      clearInterval(timer)
      if (document.visibilityState === 'visible') start()
    }
    start()
    document.addEventListener('visibilitychange', onVisibility)
    return () => {
      clearInterval(timer)
      clearTimeout(soonTimer.current)
      document.removeEventListener('visibilitychange', onVisibility)
    }
  }, [refreshResults])

  function refreshSoon() {
    clearTimeout(soonTimer.current)
    soonTimer.current = setTimeout(refreshResults, SOON_MS)
  }

  function handleSaved(saved) {
    setInstances((current) => {
      const exists = current.some((i) => i.id === saved.id)
      return exists ? current.map((i) => (i.id === saved.id ? saved : i)) : [...current, saved]
    })
    // the old payload no longer matches the new config
    setResults((current) => ({ ...current, [saved.id]: undefined }))
    setDialog(null)
    refreshSoon()
  }

  // the server fetches right away and answers with the fresh result
  async function handleRefresh(instance) {
    try {
      const result = await api.post(`/api/widget-instances/${instance.id}/refresh`)
      setResults((current) => ({ ...current, [instance.id]: result }))
    } catch (error) {
      if (error.status === 401) return
      setNotice({ tone: 'error', text: `This widget could not be refreshed: ${error.message}` })
    }
  }

  async function handleDelete(instance) {
    try {
      await api.delete(`/api/widget-instances/${instance.id}`)
    } catch (error) {
      // 404: already gone, which is what we wanted
      if (error.status !== 404) throw error
    }
    setInstances((current) => {
      const next = current.filter((i) => i.id !== instance.id)
      if (next.length === 0) setEditing(false)
      return next
    })
  }

  // layout = { [id]: { x, y, w, h } } in 12-column cells, only what moved is saved
  async function commitLayout(layout) {
    const positions = Object.fromEntries(
      Object.entries(layout).map(([id, r]) => [id, { x: r.x, y: r.y, width: r.w, height: r.h }]),
    )
    const changed = instances.filter((i) => positions[i.id] && !samePosition(i.position, positions[i.id]))
    if (changed.length === 0) return

    setInstances((current) =>
      current.map((i) => (positions[i.id] ? { ...i, position: positions[i.id] } : i)),
    )
    try {
      await Promise.all(
        changed.map((i) => api.patch(`/api/widget-instances/${i.id}/position`, positions[i.id])),
      )
    } catch (error) {
      if (error.status === 401) return
      setNotice({ tone: 'error', text: 'Your layout could not be saved, it has been reloaded.' })
      loadInstances()
    }
  }

  const visibleIds = useMemo(() => {
    if (!query || !instances) return null
    return new Set(instances.filter((i) => matches(i, widgetsByKey[i.widgetKey], query)).map((i) => i.id))
  }, [instances, query, widgetsByKey])

  const loading = instances === null && !loadError
  const empty = instances?.length === 0
  const noMatch = visibleIds?.size === 0 && !empty

  return (
    <main className="dashboard">
      <div className="dashboard__header">
        <div className="dashboard__greeting">
          <h1 className="dashboard__greeting-title">
            {user ? `${greeting()}, ${user.username}` : greeting()}
          </h1>
          <p className="dashboard__greeting-subtitle">
            {instances?.length
              ? 'Drag a widget by its handle to rearrange your dashboard.'
              : "Here's what's happening across your workspace today."}
          </p>
        </div>
        {!empty && instances && (
          <div className="dashboard__actions">
            <button
              type="button"
              className={`button${editing ? ' button--primary' : ''}`}
              aria-pressed={editing}
              onClick={() => setEditing((current) => !current)}
            >
              {!editing && <IconPencil size={16} />}
              {editing ? 'Done' : 'Edit'}
            </button>
            <button type="button" className="button button--primary" onClick={() => setDialog({ type: 'add' })}>
              <IconPlus size={16} />
              Add widget
            </button>
          </div>
        )}
      </div>

      {notice && (
        <Notice tone={notice.tone} onDismiss={() => setNotice(null)}>
          {notice.text}
        </Notice>
      )}
      {loadError && (
        <Notice tone="error">
          {loadError}{' '}
          <button type="button" className="dashboard__retry" onClick={loadInstances}>
            Retry
          </button>
        </Notice>
      )}

      {loading && (
        <div className="dashboard__skeletons" aria-busy="true" aria-label="Loading your widgets">
          <span className="dashboard__skeleton" />
          <span className="dashboard__skeleton" />
          <span className="dashboard__skeleton" />
        </div>
      )}

      {empty && (
        <div className="dashboard__empty">
          <p className="dashboard__empty-title">No widgets yet</p>
          <p className="dashboard__greeting-subtitle">
            Add your first widget, or connect a service to unlock more of them.
          </p>
          <div className="dashboard__empty-actions">
            <button type="button" className="button button--primary" onClick={() => setDialog({ type: 'add' })}>
              <IconPlus size={16} />
              Add widget
            </button>
            <Link className="button" to="/services">
              Browse services
            </Link>
          </div>
        </div>
      )}

      {noMatch && (
        <p className="dashboard__no-match">No widget matches “{query}”.</p>
      )}

      {instances && instances.length > 0 && (
        <DashboardGrid
          instances={instances}
          visibleIds={visibleIds}
          results={results}
          widgetsByKey={widgetsByKey}
          editable={!query}
          editing={editing}
          onCommit={commitLayout}
          onRefresh={handleRefresh}
          onEdit={(instance) => setDialog({ type: 'edit', instance })}
          onDelete={(instance) => setDialog({ type: 'delete', instance })}
        />
      )}

      {(dialog?.type === 'add' || dialog?.type === 'edit') && (
        <WidgetDialog
          widgets={catalogue.widgets}
          services={catalogue.services}
          instance={dialog.instance}
          nextRow={bottomRow(instances ?? [])}
          onSaved={handleSaved}
          onClose={() => setDialog(null)}
        />
      )}
      {dialog?.type === 'delete' && (
        <ConfirmDialog
          title="Remove this widget?"
          message={`${widgetsByKey[dialog.instance.widgetKey]?.name ?? dialog.instance.widgetKey} will be removed from your dashboard.`}
          confirmLabel="Remove"
          onConfirm={() => handleDelete(dialog.instance)}
          onClose={() => setDialog(null)}
        />
      )}
    </main>
  )
}

export default App
