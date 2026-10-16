import { useId, useState } from 'react'
import { Link } from 'react-router-dom'
import Sheet from './Sheet.jsx'
import { ParamFields, RefreshRateField } from './WidgetForm.jsx'
import { IconChevronRight } from './icons/Icons.jsx'
import { api, fieldErrors } from '../api.js'
import {
  DEFAULT_REFRESH_RATE,
  initialValues,
  toConfig,
  validateValues,
} from '../widgetConfig.js'
import { defaultSize } from '../widgetLayout.js'
import './WidgetDialog.css'

// the server may key config errors as "city" or "config.city"
function errorsByParam(params, error) {
  const raw = fieldErrors(error)
  const byParam = {}
  for (const [key, message] of Object.entries(raw)) {
    const param = params.find((p) => key.toLowerCase().endsWith(p.name.toLowerCase()))
    if (param) byParam[param.name] = message
  }
  return byParam
}

function WidgetPicker({ widgets, services, onPick }) {
  const groups = services
    .map((service) => ({ service, widgets: widgets.filter((w) => w.serviceKey === service.key) }))
    .filter((group) => group.widgets.length > 0)

  return groups.map(({ service, widgets: list }) => {
    const locked = list.every((w) => !w.available)
    return (
      <section className="widget-list" key={service.key} aria-label={service.name}>
        <div className="widget-list__heading">
          <h3 className="widget-list__service">{service.name}</h3>
          {locked && (
            <Link className="widget-list__connect" to="/services">
              Connect first
            </Link>
          )}
        </div>
        <ul className="widget-list__items">
          {list.map((widget) => (
            <li key={widget.key}>
              <button
                type="button"
                className="widget-list__item"
                disabled={!widget.available}
                onClick={() => onPick(widget)}
              >
                <span className="widget-list__text">
                  <span className="widget-list__name">{widget.name}</span>
                  <span className="widget-list__description">{widget.description}</span>
                </span>
                <IconChevronRight size={14} />
              </button>
            </li>
          ))}
        </ul>
      </section>
    )
  })
}

// add (instance absent) or reconfigure (instance given) a widget
function WidgetDialog({ widgets, services, instance, nextRow = 0, onSaved, onClose }) {
  const titleId = useId()
  const editing = Boolean(instance)
  const [widget, setWidget] = useState(() =>
    editing ? widgets.find((w) => w.key === instance.widgetKey) ?? null : null,
  )
  const [values, setValues] = useState(() =>
    widget ? initialValues(widget.params, instance?.config) : {},
  )
  const [refreshRate, setRefreshRate] = useState(instance?.refreshRateSeconds ?? DEFAULT_REFRESH_RATE)
  const [errors, setErrors] = useState({})
  const [status, setStatus] = useState({ state: 'idle' })
  const serviceName = services.find((s) => s.key === widget?.serviceKey)?.name

  function pick(next) {
    setWidget(next)
    setValues(initialValues(next.params))
    setErrors({})
    setStatus({ state: 'idle' })
  }

  async function submit(event) {
    event.preventDefault()
    const invalid = validateValues(widget.params, values)
    setErrors(invalid)
    if (Object.keys(invalid).length) return

    setStatus({ state: 'saving' })
    const config = toConfig(widget.params, values)
    try {
      let saved
      if (editing) {
        saved = await api.put(`/api/widget-instances/${instance.id}`, {
          config,
          refreshRateSeconds: refreshRate,
        })
      } else {
        const size = defaultSize(widget.key)
        saved = await api.post('/api/widget-instances', {
          widgetKey: widget.key,
          config,
          refreshRateSeconds: refreshRate,
          position: { x: 0, y: nextRow, width: size.w, height: size.h },
        })
      }
      onSaved(saved)
    } catch (error) {
      if (error.status === 401) return
      if (error.status === 403) {
        setStatus({ state: 'forbidden' })
        return
      }
      setErrors(errorsByParam(widget.params, error))
      setStatus({ state: 'error', message: error.message })
    }
  }

  return (
    <Sheet labelledBy={titleId} onClose={onClose}>
      {!widget ? (
        <div className="sheet__body">
          <div className="sheet__header">
            <h2 className="sheet__title" id={titleId}>
              Add a widget
            </h2>
            <p className="sheet__subtitle">Pick what you want to see on your dashboard.</p>
          </div>
          <WidgetPicker widgets={widgets} services={services} onPick={pick} />
          <div className="sheet__actions">
            <button type="button" className="button" onClick={onClose}>
              Cancel
            </button>
          </div>
        </div>
      ) : (
        <form className="sheet__body" onSubmit={submit} noValidate>
          <div className="sheet__header">
            {!editing && (
              <button type="button" className="sheet__back" onClick={() => setWidget(null)}>
                <IconChevronRight size={14} />
                Widgets
              </button>
            )}
            <h2 className="sheet__title" id={titleId}>
              {editing ? `Edit ${widget.name}` : widget.name}
            </h2>
            <p className="sheet__subtitle">
              {serviceName ? `${serviceName} · ` : ''}
              {widget.description}
            </p>
          </div>

          <ParamFields
            params={widget.params}
            values={values}
            errors={errors}
            onChange={(name, value) => {
              setValues((current) => ({ ...current, [name]: value }))
              setErrors((current) => ({ ...current, [name]: undefined }))
            }}
          />
          <RefreshRateField value={refreshRate} onChange={setRefreshRate} />

          {status.state === 'error' && <p className="sheet__note sheet__note--error">{status.message}</p>}
          {status.state === 'forbidden' && (
            <p className="sheet__note sheet__note--error">
              Connect {serviceName ?? 'this service'} before adding its widgets.{' '}
              <Link to="/services">Open Services</Link>
            </p>
          )}

          <div className="sheet__actions">
            <button type="button" className="button" onClick={onClose}>
              Cancel
            </button>
            <button type="submit" className="button button--primary" disabled={status.state === 'saving'}>
              {status.state === 'saving' ? 'Saving…' : editing ? 'Save' : 'Add to dashboard'}
            </button>
          </div>
        </form>
      )}
    </Sheet>
  )
}

export default WidgetDialog
