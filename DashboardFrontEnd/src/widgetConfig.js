export const REFRESH_LIMITS = { min: 10, max: 86400 }

export const REFRESH_PRESETS = [
  { seconds: 30, label: '30 s' },
  { seconds: 60, label: '1 min' },
  { seconds: 300, label: '5 min' },
  { seconds: 900, label: '15 min' },
  { seconds: 1800, label: '30 min' },
  { seconds: 3600, label: '1 h' },
  { seconds: 21600, label: '6 h' },
  { seconds: 86400, label: '24 h' },
]

export const DEFAULT_REFRESH_RATE = 300

export function formatRefreshRate(seconds) {
  const preset = REFRESH_PRESETS.find((p) => p.seconds === seconds)
  if (preset) return preset.label
  if (seconds % 3600 === 0) return `${seconds / 3600} h`
  if (seconds % 60 === 0) return `${seconds / 60} min`
  return `${seconds} s`
}

// form values are strings, prefilled from an existing config when editing
export function initialValues(params, config = {}) {
  return Object.fromEntries(
    params.map((param) => [param.name, config[param.name] != null ? String(config[param.name]) : '']),
  )
}

// optional empty fields are omitted rather than sent as ""
export function toConfig(params, values) {
  return Object.fromEntries(
    params
      .filter((param) => (values[param.name] ?? '').trim() !== '')
      .map((param) => {
        const raw = values[param.name].trim()
        return [param.name, param.type === 'integer' ? Number(raw) : raw]
      }),
  )
}

// same rules as the server, so the user is not round-tripped for a typo
export function validateValues(params, values) {
  const errors = {}
  for (const param of params) {
    const raw = (values[param.name] ?? '').trim()
    if (raw === '') {
      if (param.required) errors[param.name] = 'Required.'
      continue
    }
    if (param.type === 'integer') {
      const number = Number(raw)
      if (!Number.isInteger(number)) errors[param.name] = 'Must be a whole number.'
      else if (param.min != null && number < param.min) errors[param.name] = `At least ${param.min}.`
      else if (param.max != null && number > param.max) errors[param.name] = `At most ${param.max}.`
    }
  }
  return errors
}

// "city" -> "City", "learningLanguage" -> "Learning language"
export function labelOf(name) {
  const words = name.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase()
  return words.charAt(0).toUpperCase() + words.slice(1)
}

// a short summary of a config for card subtitles and search
export function describeConfig(config) {
  return Object.values(config ?? {})
    .map((value) => String(value))
    .join(' · ')
}
