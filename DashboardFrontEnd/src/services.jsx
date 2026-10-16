import { useCallback, useEffect, useId, useState } from 'react'
import { Link, useOutletContext, useSearchParams } from 'react-router-dom'
import Sheet from './components/Sheet.jsx'
import ConfirmDialog from './components/ConfirmDialog.jsx'
import Notice from './components/Notice.jsx'
import { ParamFields, RefreshRateField } from './components/WidgetForm.jsx'
import WidgetBody from './components/widgets/bodies/WidgetBody.jsx'
import { api } from './api.js'
import {
  DEFAULT_REFRESH_RATE,
  initialValues,
  toConfig,
  validateValues,
} from './widgetConfig.js'
import { bottomRow, defaultSize } from './widgetLayout.js'
import './App.css'
import './services.css'

const GITHUB_FAILURES = {
  denied: 'you declined the authorization on GitHub',
  invalid_request: 'GitHub sent back an incomplete answer',
  invalid_state: 'the link expired, try again',
  unknown_user: 'your account could not be found',
  exchange_failed: 'GitHub refused the authorization code',
}

// the oauth callback lands here with ?github=linked|failed, read it once
function githubNotice(search) {
  const outcome = search.get('github')
  if (outcome === 'linked') {
    const account = search.get('account')
    return { tone: 'success', text: `GitHub account ${account ? `@${account} ` : ''}linked.` }
  }
  if (outcome === 'failed') {
    const reason = GITHUB_FAILURES[search.get('reason')] ?? 'something went wrong'
    return { tone: 'error', text: `GitHub could not be linked: ${reason}.` }
  }
  return null
}

function serviceState(service, unavailable) {
  if (service.auth === 'none') return { badge: 'Ready to use', tone: 'ready' }
  if (service.auth === 'oauth' && unavailable) return { badge: 'Not available on this deployment', tone: 'muted' }
  if (service.subscribed) return { badge: 'Connected', tone: 'ready' }
  if (service.auth === 'apiKey') return { badge: 'Needs an API key', tone: 'pending' }
  return { badge: 'Link your account', tone: 'pending' }
}

function PreviewCard({ widgetKey, data }) {
  return (
    <div className="preview-card">
      <div className="preview-card__header">
        <p className="preview-card__title">Preview</p>
        <span className="preview-card__badge">
          <span className="preview-card__dot" />
          Live
        </span>
      </div>
      <div className="preview-card__body">
        <WidgetBody result={{ widgetKey, status: 'ok', data }} />
      </div>
      <details className="preview-card__raw">
        <summary>Raw response</summary>
        <pre>{JSON.stringify(data, null, 2)}</pre>
      </details>
    </div>
  )
}

function WidgetOption({ widget, open, onToggle, nextRow, onAdded }) {
  const [values, setValues] = useState(() => initialValues(widget.params))
  const [errors, setErrors] = useState({})
  const [preview, setPreview] = useState({ state: 'idle' })
  const [add, setAdd] = useState({ state: 'idle' })
  const [refreshRate, setRefreshRate] = useState(DEFAULT_REFRESH_RATE)

  function check() {
    const invalid = validateValues(widget.params, values)
    setErrors(invalid)
    return Object.keys(invalid).length === 0
  }

  // preview hits the provider directly, it is a one-off check and never polled
  async function runPreview() {
    if (!check()) return
    setPreview({ state: 'loading' })
    const query = new URLSearchParams(toConfig(widget.params, values))
    try {
      setPreview({ state: 'ok', data: await api.get(`/api/widgets/${widget.key}/preview?${query}`) })
    } catch (error) {
      if (error.status !== 401) setPreview({ state: 'error', message: error.message })
    }
  }

  async function addWidget(event) {
    event.preventDefault()
    if (!check()) return
    setAdd({ state: 'adding' })
    const size = defaultSize(widget.key)
    try {
      await api.post('/api/widget-instances', {
        widgetKey: widget.key,
        config: toConfig(widget.params, values),
        refreshRateSeconds: refreshRate,
        position: { x: 0, y: nextRow(), width: size.w, height: size.h },
      })
      setAdd({ state: 'added' })
      onAdded(size.h)
    } catch (error) {
      if (error.status !== 401) setAdd({ state: 'error', message: error.message })
    }
  }

  return (
    <li className={`widget-picker__item${open ? ' widget-picker__item--open' : ''}`}>
      <button type="button" className="widget-picker__header" aria-expanded={open} onClick={onToggle}>
        <span className="widget-picker__text">
          <span className="widget-picker__name">{widget.name}</span>
          <span className="widget-picker__description">{widget.description}</span>
        </span>
        {add.state === 'added' && <span className="preview-card__badge">Added</span>}
        <span className="widget-picker__chevron" aria-hidden="true">
          {open ? '−' : '+'}
        </span>
      </button>

      {open && (
        <form className="widget-picker__body" onSubmit={addWidget} noValidate>
          {widget.params.length === 0 ? (
            <p className="sheet__subtitle">This widget has no options.</p>
          ) : (
            <ParamFields
              params={widget.params}
              values={values}
              errors={errors}
              onChange={(name, value) => {
                setValues((current) => ({ ...current, [name]: value }))
                setErrors((current) => ({ ...current, [name]: undefined }))
                setPreview((current) => (current.state === 'ok' ? { state: 'idle' } : current))
              }}
            />
          )}

          <RefreshRateField value={refreshRate} onChange={setRefreshRate} />

          {preview.state === 'loading' && (
            <div className="preview-card preview-card--loading" aria-busy="true">
              <span className="preview-card__skeleton" />
              <span className="preview-card__skeleton preview-card__skeleton--short" />
            </div>
          )}
          {preview.state === 'error' && <p className="sheet__note sheet__note--error">{preview.message}</p>}
          {preview.state === 'ok' && <PreviewCard widgetKey={widget.key} data={preview.data} />}
          {add.state === 'error' && <p className="sheet__note sheet__note--error">{add.message}</p>}

          <div className="widget-picker__actions">
            <button
              type="button"
              className="button button--small"
              disabled={preview.state === 'loading'}
              onClick={runPreview}
            >
              {preview.state === 'loading' ? 'Loading…' : 'Preview'}
            </button>
            <button
              type="submit"
              className="button button--small button--primary"
              disabled={add.state === 'adding'}
            >
              {add.state === 'adding' ? 'Adding…' : add.state === 'added' ? 'Add another' : 'Add widget'}
            </button>
          </div>
        </form>
      )}
    </li>
  )
}

function WidgetsDialog({ service, widgets, onClose }) {
  const titleId = useId()
  const [openKey, setOpenKey] = useState(widgets[0]?.key ?? null)
  const [added, setAdded] = useState(0)
  // new widgets go under the existing ones
  const [row, setRow] = useState(null)

  useEffect(() => {
    api
      .get('/api/widget-instances')
      .then((list) => setRow(bottomRow(list)))
      .catch(() => setRow(0))
  }, [])

  return (
    <Sheet labelledBy={titleId} onClose={onClose}>
      <div className="sheet__body">
        <div className="sheet__header">
          <h2 className="sheet__title" id={titleId}>
            {service.name} widgets
          </h2>
          <p className="sheet__subtitle">
            Fill in the options, preview the result if you like, then add it to your dashboard.
          </p>
        </div>

        <ul className="widget-picker">
          {widgets.map((widget) => (
            <WidgetOption
              key={widget.key}
              widget={widget}
              open={openKey === widget.key}
              onToggle={() => setOpenKey(openKey === widget.key ? null : widget.key)}
              nextRow={() => row ?? 0}
              onAdded={(height) => {
                setAdded((count) => count + 1)
                setRow((current) => (current ?? 0) + height)
              }}
            />
          ))}
        </ul>

        <div className="sheet__actions">
          <button type="button" className="button" onClick={onClose}>
            Close
          </button>
          <Link className="button button--primary" to="/">
            {added > 0 ? `Go to dashboard (${added})` : 'Go to dashboard'}
          </Link>
        </div>
      </div>
    </Sheet>
  )
}

function ApiKeyDialog({ service, onConnected, onClose }) {
  const titleId = useId()
  const fieldId = useId()
  const prompt = service.credentialPrompt
  const [apiKey, setApiKey] = useState(prompt?.suggestedValue ?? '')
  const [status, setStatus] = useState({ state: 'idle' })

  async function submit(event) {
    event.preventDefault()
    if (!apiKey.trim()) {
      setStatus({ state: 'error', message: 'Enter a key.' })
      return
    }
    setStatus({ state: 'saving' })
    try {
      await api.post(`/api/subscriptions/${service.key}`, { apiKey: apiKey.trim() })
      onConnected()
    } catch (error) {
      if (error.status === 401) return
      // 422: the provider refused the key, the api's detail already says so
      const message = error.status === 422 && !error.problem?.detail ? `${service.name} rejected this key.` : error.message
      setStatus({ state: 'error', message })
    }
  }

  return (
    <Sheet labelledBy={titleId} onClose={onClose}>
      <form className="sheet__body" onSubmit={submit} noValidate>
        <div className="sheet__header">
          <h2 className="sheet__title" id={titleId}>
            Connect {service.name}
          </h2>
          <p className="sheet__subtitle">{service.description}</p>
        </div>

        <div className="form-field">
          <label className="form-field__label" htmlFor={fieldId}>
            {prompt?.label ?? 'API key'}
          </label>
          <input
            id={fieldId}
            className="field"
            value={apiKey}
            autoComplete="off"
            spellCheck="false"
            onChange={(e) => setApiKey(e.target.value)}
          />
          {prompt?.help && <p className="form-field__hint">{prompt.help}</p>}
        </div>

        {prompt?.signupUrl && (
          <p className="sheet__note sheet__note--info">
            Need your own key?{' '}
            <a href={prompt.signupUrl} target="_blank" rel="noreferrer">
              Get one from {service.name}
            </a>
          </p>
        )}
        {status.state === 'error' && <p className="sheet__note sheet__note--error">{status.message}</p>}

        <div className="sheet__actions">
          <button type="button" className="button" onClick={onClose}>
            Cancel
          </button>
          <button type="submit" className="button button--primary" disabled={status.state === 'saving'}>
            {status.state === 'saving' ? 'Checking…' : 'Connect'}
          </button>
        </div>
      </form>
    </Sheet>
  )
}

function ServiceCard({ service, widgets, unavailable, linking, onAddWidget, onConnect, onLink, onDisconnect }) {
  const state = serviceState(service, unavailable)
  const canAdd = widgets.some((w) => w.available)

  let primary = null
  if (service.auth === 'apiKey' && !service.subscribed) {
    primary = (
      <button type="button" className="button button--primary" onClick={onConnect}>
        Connect
      </button>
    )
  } else if (service.auth === 'oauth' && !service.subscribed) {
    primary = (
      <button type="button" className="button button--primary" disabled={unavailable || linking} onClick={onLink}>
        {linking ? 'Opening GitHub…' : 'Link your account'}
      </button>
    )
  } else if (canAdd) {
    primary = (
      <button type="button" className="button button--primary" onClick={onAddWidget}>
        Add a widget
      </button>
    )
  }

  return (
    <article className="service-card">
      <div className="service-card__heading">
        <h2 className="service-card__title">{service.name}</h2>
        <span className={`service-card__badge service-card__badge--${state.tone}`}>{state.badge}</span>
      </div>
      <p className="service-card__description">{service.description}</p>
      {widgets.length === 0 && service.auth !== 'none' && (
        <p className="service-card__note">No widgets for this service yet.</p>
      )}

      <div className="service-card__actions">
        {primary}
        {service.auth !== 'none' && service.subscribed && (
          <button type="button" className="button button--danger" onClick={onDisconnect}>
            Disconnect
          </button>
        )}
      </div>
    </article>
  )
}

// page /services: the catalogue, and connecting each service the way its auth kind requires
function Services() {
  const { query } = useOutletContext()
  const [searchParams, setSearchParams] = useSearchParams()
  const [services, setServices] = useState([])
  const [widgets, setWidgets] = useState([])
  const [status, setStatus] = useState({ state: 'loading' })
  const [notice, setNotice] = useState(() => githubNotice(searchParams))
  const [dialog, setDialog] = useState(null)
  const [unavailable, setUnavailable] = useState(new Set())
  const [linking, setLinking] = useState(null)

  const load = useCallback(() => {
    return Promise.all([api.get('/api/services'), api.get('/api/widgets')])
      .then(([serviceList, widgetList]) => {
        setServices(serviceList)
        setWidgets(widgetList)
        setStatus({ state: 'ok' })
      })
      .catch((error) => {
        if (error.status !== 401) setStatus({ state: 'error', message: error.message })
      })
  }, [])

  useEffect(() => {
    load()
  }, [load])

  // drop ?github=… so a reload does not show the banner again
  useEffect(() => {
    if (searchParams.has('github')) setSearchParams({}, { replace: true })
  }, [searchParams, setSearchParams])

  async function linkGitHub(service) {
    setLinking(service.key)
    try {
      const { url } = await api.get(`/api/services/${service.key}/oauth/authorize`)
      window.location.assign(url)
    } catch (error) {
      setLinking(null)
      if (error.status === 503) {
        setUnavailable((current) => new Set(current).add(service.key))
      } else if (error.status !== 401) {
        setNotice({ tone: 'error', text: error.message })
      }
    }
  }

  async function disconnect(service) {
    await api.delete(`/api/subscriptions/${service.key}`)
    setNotice({ tone: 'info', text: `${service.name} disconnected. Its widgets will show an error until you connect it again.` })
    await load()
  }

  const needle = query.toLowerCase()
  const visible = services.filter((s) => `${s.name} ${s.description}`.toLowerCase().includes(needle))

  return (
    <main className="dashboard">
      <div className="dashboard__greeting">
        <h1 className="dashboard__greeting-title">Services</h1>
        <p className="dashboard__greeting-subtitle">
          Connect a service to start adding its widgets to your dashboard.
        </p>
      </div>

      {notice && (
        <Notice tone={notice.tone} onDismiss={() => setNotice(null)}>
          {notice.text}
        </Notice>
      )}
      {status.state === 'error' && <Notice tone="error">{status.message}</Notice>}

      {status.state === 'loading' && (
        <div className="service-grid" aria-busy="true" aria-label="Loading services">
          <span className="service-card service-card--skeleton" />
          <span className="service-card service-card--skeleton" />
          <span className="service-card service-card--skeleton" />
        </div>
      )}

      {status.state === 'ok' && visible.length === 0 && (
        <p className="dashboard__no-match">No service matches “{query}”.</p>
      )}

      <div className="service-grid">
        {visible.map((service) => (
          <ServiceCard
            key={service.key}
            service={service}
            widgets={widgets.filter((w) => w.serviceKey === service.key)}
            unavailable={unavailable.has(service.key)}
            linking={linking === service.key}
            onAddWidget={() => setDialog({ type: 'widgets', service })}
            onConnect={() => setDialog({ type: 'apiKey', service })}
            onLink={() => linkGitHub(service)}
            onDisconnect={() => setDialog({ type: 'disconnect', service })}
          />
        ))}
      </div>

      {dialog?.type === 'widgets' && (
        <WidgetsDialog
          service={dialog.service}
          widgets={widgets.filter((w) => w.serviceKey === dialog.service.key && w.available)}
          onClose={() => setDialog(null)}
        />
      )}
      {dialog?.type === 'apiKey' && (
        <ApiKeyDialog
          service={dialog.service}
          onClose={() => setDialog(null)}
          onConnected={() => {
            setNotice({ tone: 'success', text: `${dialog.service.name} connected.` })
            setDialog(null)
            load()
          }}
        />
      )}
      {dialog?.type === 'disconnect' && (
        <ConfirmDialog
          title={`Disconnect ${dialog.service.name}?`}
          message="Your widgets stay on the dashboard but stop updating until you connect it again."
          confirmLabel="Disconnect"
          onConfirm={() => disconnect(dialog.service)}
          onClose={() => setDialog(null)}
        />
      )}
    </main>
  )
}

export default Services
