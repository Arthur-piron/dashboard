import { useId } from 'react'
import { REFRESH_PRESETS, formatRefreshRate, labelOf } from '../widgetConfig.js'
import './Sheet.css'

function ParamField({ param, value, error, onChange }) {
  const id = useId()
  const hintId = `${id}-hint`

  return (
    <div className="form-field">
      <label className="form-field__label" htmlFor={id}>
        {labelOf(param.name)}
        {!param.required && <span className="form-field__optional"> · optional</span>}
      </label>

      {param.options ? (
        <select
          id={id}
          className="field"
          value={value}
          required={param.required}
          aria-invalid={Boolean(error)}
          aria-describedby={hintId}
          onChange={(e) => onChange(e.target.value)}
        >
          {!param.required && <option value="">Default</option>}
          {param.required && value === '' && <option value="">Choose…</option>}
          {param.options.map((option) => (
            <option key={option} value={option}>
              {option}
            </option>
          ))}
        </select>
      ) : (
        <input
          id={id}
          className="field"
          type={param.type === 'integer' ? 'number' : 'text'}
          inputMode={param.type === 'integer' ? 'numeric' : undefined}
          min={param.min ?? undefined}
          max={param.max ?? undefined}
          value={value}
          required={param.required}
          aria-invalid={Boolean(error)}
          aria-describedby={hintId}
          autoComplete="off"
          onChange={(e) => onChange(e.target.value)}
        />
      )}

      {error ? (
        <p className="form-field__error" id={hintId}>
          {error}
        </p>
      ) : (
        param.description && (
          <p className="form-field__hint" id={hintId}>
            {param.description}
          </p>
        )
      )}
    </div>
  )
}

// one field per declared widget parameter
export function ParamFields({ params, values, errors = {}, onChange }) {
  return params.map((param) => (
    <ParamField
      key={param.name}
      param={param}
      value={values[param.name] ?? ''}
      error={errors[param.name]}
      onChange={(value) => onChange(param.name, value)}
    />
  ))
}

export function RefreshRateField({ value, onChange }) {
  const presets = REFRESH_PRESETS.some((p) => p.seconds === value)
    ? REFRESH_PRESETS
    : [...REFRESH_PRESETS, { seconds: value, label: formatRefreshRate(value) }].sort(
        (a, b) => a.seconds - b.seconds,
      )

  return (
    <div className="form-field" role="group" aria-label="Refresh every">
      <span className="form-field__label">Refresh every</span>
      <div className="chips">
        {presets.map((preset) => (
          <button
            key={preset.seconds}
            type="button"
            className="chips__item"
            aria-pressed={preset.seconds === value}
            onClick={() => onChange(preset.seconds)}
          >
            {preset.label}
          </button>
        ))}
      </div>
    </div>
  )
}
