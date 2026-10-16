// icones de la maquette figma en svg inline, currentColor suit la couleur css du parent
// Crypto et WeatherGlyph gardent leurs couleurs en dur (jaune / orange systeme ios), ce sont des illustrations
import { useId } from 'react'

// loupe de la barre de recherche
export function IconSearch({ size = 16 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" aria-hidden="true">
      <circle cx="7" cy="7" r="4.2" stroke="currentColor" strokeWidth="1.6" />
      <path d="M13 13L18 18" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" />
    </svg>
  )
}

// fleche du bouton retour
export function IconArrowLeft({ size = 16 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" aria-hidden="true">
      <path d="M16 10H4M9 5L4 10L9 15" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}

// soleil du theme toggle
export function IconSun({ size = 20 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20.4 20.4" fill="none" aria-hidden="true">
      <circle cx="10.2" cy="10.2" r="4" fill="currentColor" />
      <path
        d="M17.2 10.2H19.7M15.15 15.15L16.92 16.92M10.2 17.2V19.7M5.25 15.15L3.48 16.92M3.2 10.2H0.7M5.25 5.25L3.48 3.48M10.2 3.2V0.7M15.15 5.25L16.92 3.48"
        stroke="currentColor"
        strokeWidth="1.4"
        strokeLinecap="round"
      />
    </svg>
  )
}

// lune du theme toggle
export function IconMoon({ size = 20 }) {
  // id unique par instance, le toggle affiche deux lunes
  const maskId = useId()

  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" aria-hidden="true">
      {/* decoupe par masque, transparente sur n'importe quel fond (verre, piste du switch) */}
      <mask id={maskId}>
        <rect width="20" height="20" fill="#ffffff" />
        <circle cx="13.5" cy="7.5" r="6.5" fill="#000000" />
      </mask>
      <circle cx="9.5" cy="9.5" r="6.5" fill="currentColor" mask={`url(#${maskId})`} />
    </svg>
  )
}

// poignee de deplacement des widgets
export function IconDragHandle({ size = 16 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" aria-hidden="true">
      <circle cx="8.1" cy="5.6" r="1.1" fill="currentColor" />
      <circle cx="8.1" cy="10.1" r="1.1" fill="currentColor" />
      <circle cx="8.1" cy="14.6" r="1.1" fill="currentColor" />
      <circle cx="13.1" cy="5.6" r="1.1" fill="currentColor" />
      <circle cx="13.1" cy="10.1" r="1.1" fill="currentColor" />
      <circle cx="13.1" cy="14.6" r="1.1" fill="currentColor" />
    </svg>
  )
}

// poignee de redimensionnement des widgets
export function IconResizeHandle({ size = 18 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 19.8 19.8" fill="none" aria-hidden="true" opacity="0.4">
      <path
        d="M17 19L19 17M13 19L19 13M9 19L19 9"
        stroke="currentColor"
        strokeWidth="1.6"
        strokeLinecap="round"
      />
    </svg>
  )
}

// fleche de hausse du ChangePill
export function IconTrendUp({ size = 11 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" aria-hidden="true">
      <path d="M3 14L14 5M9 5H14V10" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}

// fleche de baisse du ChangePill
export function IconTrendDown({ size = 11 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" aria-hidden="true">
      <path d="M3 6L14 15M9 15H14V10" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}

// logo des widgets crypto
export function IconCrypto({ size = 18 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" aria-hidden="true">
      <circle cx="7.5" cy="10.5" r="5.5" fill="#FF9F0A" />
      <circle cx="12.5" cy="7.5" r="4.75" fill="#FFCC00" stroke="var(--color-bg-surface)" strokeWidth="1.5" />
    </svg>
  )
}

// type d'icone meteo selon le code wmo renvoye par open-meteo (voir OpenMeteoClient.DescribeCode cote back)
function weatherKind(code) {
  if (code === 0 || code === 1) return 'clear'
  if (code === 2) return 'partly'
  if (code === 3) return 'cloudy'
  if (code === 45 || code === 48) return 'fog'
  if (code >= 51 && code <= 57) return 'drizzle'
  if ((code >= 61 && code <= 67) || (code >= 80 && code <= 82)) return 'rain'
  if ((code >= 71 && code <= 77) || code === 85 || code === 86) return 'snow'
  if (code >= 95 && code <= 99) return 'thunder'
  return 'cloudy'
}

// soleil avec rayons, centre (cx, cy)
function Sun({ cx, cy, r }) {
  const rays = [0, 45, 90, 135, 180, 225, 270, 315].map((angle) => {
    const rad = (angle * Math.PI) / 180
    return (
      <line
        key={angle}
        x1={cx + Math.cos(rad) * (r + 3)}
        y1={cy + Math.sin(rad) * (r + 3)}
        x2={cx + Math.cos(rad) * (r + 6)}
        y2={cy + Math.sin(rad) * (r + 6)}
      />
    )
  })

  return (
    <g>
      <g stroke="#FFCC00" strokeWidth="2.5" strokeLinecap="round">{rays}</g>
      <circle cx={cx} cy={cy} r={r} fill="#FFCC00" />
    </g>
  )
}

// nuage fait de trois cercles et d'une base, fond plat en y = 37
function Cloud({ fill, transform }) {
  return (
    <g fill={fill} transform={transform}>
      <circle cx="16" cy="29" r="8" />
      <circle cx="26" cy="23" r="11" />
      <circle cx="35" cy="30" r="7" />
      <rect x="16" y="29" width="19" height="8" />
    </g>
  )
}

// illustration meteo facon app meteo ios, choisie selon le code wmo
export function WeatherGlyph({ size = 48, code }) {
  const id = useId()
  const kind = weatherKind(code)
  const light = `url(#${id}-light)`
  const dark = `url(#${id}-dark)`
  // nuage remonte pour laisser la place a la pluie / neige / eclair
  const raised = 'translate(0 -6)'

  return (
    <svg width={size} height={size} viewBox="0 0 48 48" fill="none" aria-hidden="true">
      <defs>
        <linearGradient id={`${id}-light`} x1="24" y1="12" x2="24" y2="37" gradientUnits="userSpaceOnUse">
          <stop offset="0" stopColor="#F4F6F9" />
          <stop offset="1" stopColor="#C4CAD4" />
        </linearGradient>
        <linearGradient id={`${id}-dark`} x1="24" y1="12" x2="24" y2="37" gradientUnits="userSpaceOnUse">
          <stop offset="0" stopColor="#B4BAC4" />
          <stop offset="1" stopColor="#868C97" />
        </linearGradient>
      </defs>

      {kind === 'clear' && <Sun cx={24} cy={24} r={10} />}

      {kind === 'partly' && (
        <>
          <Sun cx={16} cy={16} r={7} />
          <Cloud fill={light} transform="translate(4 4)" />
        </>
      )}

      {kind === 'cloudy' && (
        <>
          <Cloud fill={dark} transform="translate(6 -6) scale(0.85)" />
          <Cloud fill={light} transform="translate(-2 2)" />
        </>
      )}

      {kind === 'fog' && (
        <>
          <Cloud fill={light} transform={raised} />
          <g stroke="#AEB4BE" strokeWidth="2.5" strokeLinecap="round">
            <line x1="12" y1="37" x2="36" y2="37" />
            <line x1="16" y1="43" x2="32" y2="43" />
          </g>
        </>
      )}

      {kind === 'drizzle' && (
        <>
          <Cloud fill={light} transform={raised} />
          <g fill="#0A84FF">
            <circle cx="17" cy="38" r="1.6" />
            <circle cx="25" cy="41" r="1.6" />
            <circle cx="33" cy="38" r="1.6" />
          </g>
        </>
      )}

      {kind === 'rain' && (
        <>
          <Cloud fill={light} transform={raised} />
          <g stroke="#0A84FF" strokeWidth="2.5" strokeLinecap="round">
            <line x1="18" y1="36" x2="15" y2="44" />
            <line x1="26" y1="36" x2="23" y2="44" />
            <line x1="34" y1="36" x2="31" y2="44" />
          </g>
        </>
      )}

      {kind === 'snow' && (
        <>
          <Cloud fill={light} transform={raised} />
          <g fill="#5AC8FA">
            <circle cx="16" cy="38" r="2" />
            <circle cx="24" cy="43" r="2" />
            <circle cx="32" cy="38" r="2" />
          </g>
        </>
      )}

      {kind === 'thunder' && (
        <>
          <Cloud fill={dark} transform={raised} />
          <path d="M26 30L19 39.5H24L21.5 46.5L30 36H24.5L27.5 30Z" fill="#FFCC00" strokeLinejoin="round" />
        </>
      )}
    </svg>
  )
}

// flamme facon sf symbols, remplace l'emoji 🔥 pour un rendu identique sur tous les os
export function IconFlame({ size = 20 }) {
  const gradientId = useId()

  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" aria-hidden="true">
      <defs>
        <linearGradient id={gradientId} x1="10" y1="1.5" x2="10" y2="18.5" gradientUnits="userSpaceOnUse">
          <stop offset="0" stopColor="#FF3B30" />
          <stop offset="1" stopColor="#FF9500" />
        </linearGradient>
      </defs>
      <path
        d="M10 1.5C10.6 4.4 14.8 6.6 15.6 10.8C16.3 14.6 13.6 18.5 10 18.5C6.4 18.5 3.9 15.6 4.3 12.3C4.6 9.9 6 8.6 7 7.4C7.2 9 7.9 10 8.8 10.4C8.3 7.2 9.3 4 10 1.5Z"
        fill={`url(#${gradientId})`}
      />
      <path
        d="M10.2 11C10.6 12.4 12.6 13.3 12.6 15.1C12.6 16.6 11.4 17.6 10 17.6C8.6 17.6 7.5 16.6 7.5 15.2C7.5 13.6 9.4 12.6 10.2 11Z"
        fill="#FFD60A"
      />
    </svg>
  )
}

// couronne facon sf symbols, remplace l'emoji 👑
export function IconCrown({ size = 16 }) {
  const gradientId = useId()

  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" aria-hidden="true">
      <defs>
        <linearGradient id={gradientId} x1="10" y1="3" x2="10" y2="17.5" gradientUnits="userSpaceOnUse">
          <stop offset="0" stopColor="#FFD60A" />
          <stop offset="1" stopColor="#FF9F0A" />
        </linearGradient>
      </defs>
      <g fill={`url(#${gradientId})`}>
        <path d="M3 6.5L6.8 9.6L10 4L13.2 9.6L17 6.5L15.6 14.5H4.4L3 6.5Z" />
        <circle cx="3" cy="6.2" r="1.3" />
        <circle cx="10" cy="3.6" r="1.3" />
        <circle cx="17" cy="6.2" r="1.3" />
        <rect x="4.4" y="15.6" width="11.2" height="1.9" rx="0.95" />
      </g>
    </svg>
  )
}

// tab bar: dashboard, four rounded tiles
export function IconGrid({ size = 22 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 22 22" fill="none" aria-hidden="true">
      <rect x="2.5" y="2.5" width="7.5" height="7.5" rx="2.2" fill="currentColor" />
      <rect x="12" y="2.5" width="7.5" height="7.5" rx="2.2" fill="currentColor" opacity="0.55" />
      <rect x="2.5" y="12" width="7.5" height="7.5" rx="2.2" fill="currentColor" opacity="0.55" />
      <rect x="12" y="12" width="7.5" height="7.5" rx="2.2" fill="currentColor" />
    </svg>
  )
}

// tab bar: services, a plug
export function IconPlug({ size = 22 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 22 22" fill="none" aria-hidden="true">
      <path d="M8 2.5V6.5M14 2.5V6.5" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
      <path
        d="M5.5 6.5H16.5V9.5C16.5 12.54 14.04 15 11 15C7.96 15 5.5 12.54 5.5 9.5V6.5Z"
        fill="currentColor"
      />
      <path d="M11 15V19.5" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
    </svg>
  )
}

// tab bar: admin, a shield
export function IconShield({ size = 22 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 22 22" fill="none" aria-hidden="true">
      <path
        d="M11 2.5L18 5.2V10.4C18 14.6 15.1 18.1 11 19.5C6.9 18.1 4 14.6 4 10.4V5.2L11 2.5Z"
        fill="currentColor"
      />
    </svg>
  )
}

export function IconPlus({ size = 16 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" aria-hidden="true">
      <path d="M10 4V16M4 10H16" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
    </svg>
  )
}

export function IconRefresh({ size = 16 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" aria-hidden="true">
      <path
        d="M16 10A6 6 0 1 1 13.5 5.1M16 3.5V7H12.5"
        stroke="currentColor"
        strokeWidth="1.8"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  )
}

export function IconPencil({ size = 16 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" aria-hidden="true">
      <path
        d="M12.5 4.5L15.5 7.5M4 16L4.6 12.9L13.3 4.2A1.5 1.5 0 0 1 15.4 4.2L15.8 4.6A1.5 1.5 0 0 1 15.8 6.7L7.1 15.4L4 16Z"
        stroke="currentColor"
        strokeWidth="1.6"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  )
}

export function IconTrash({ size = 16 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" aria-hidden="true">
      <path
        d="M4 6H16M8 6V4.5A1 1 0 0 1 9 3.5H11A1 1 0 0 1 12 4.5V6M5.5 6L6.2 15.6A1 1 0 0 0 7.2 16.5H12.8A1 1 0 0 0 13.8 15.6L14.5 6"
        stroke="currentColor"
        strokeWidth="1.6"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  )
}

export function IconClose({ size = 14 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" aria-hidden="true">
      <path d="M5 5L15 15M15 5L5 15" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
    </svg>
  )
}

export function IconChevronRight({ size = 14 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" aria-hidden="true">
      <path d="M8 4L14 10L8 16" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}

// github bodies: star, fork, location pin and building, sf symbols style
export function IconStar({ size = 13 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
      <path d="M8 1.2L10.1 5.5L14.8 6.2L11.4 9.5L12.2 14.2L8 12L3.8 14.2L4.6 9.5L1.2 6.2L5.9 5.5L8 1.2Z" />
    </svg>
  )
}

export function IconFork({ size = 13 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6" aria-hidden="true">
      <circle cx="4" cy="3.2" r="1.7" />
      <circle cx="12" cy="3.2" r="1.7" />
      <circle cx="8" cy="12.8" r="1.7" />
      <path d="M4 4.9V6.2C4 7.3 4.9 8.2 6 8.2H10C11.1 8.2 12 7.3 12 6.2V4.9M8 8.2V11.1" strokeLinecap="round" />
    </svg>
  )
}

export function IconPin({ size = 13 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
      <path d="M8 1C5.2 1 3 3.2 3 6C3 9.6 8 15 8 15C8 15 13 9.6 13 6C13 3.2 10.8 1 8 1ZM8 8C6.9 8 6 7.1 6 6C6 4.9 6.9 4 8 4C9.1 4 10 4.9 10 6C10 7.1 9.1 8 8 8Z" />
    </svg>
  )
}

export function IconBuilding({ size = 13 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5" aria-hidden="true">
      <rect x="3" y="1.8" width="10" height="12.4" rx="1.4" />
      <path d="M6 5H7M9 5H10M6 8H7M9 8H10M7 14.2V11.4H9V14.2" strokeLinecap="round" />
    </svg>
  )
}

// brand marks for the sign-in buttons, in their own colours rather than currentColor
export function IconGoogle({ size = 18 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 48 48" aria-hidden="true">
      <path
        fill="#FFC107"
        d="M43.6 20.5H42V20H24v8h11.3C33.7 32.7 29.3 36 24 36c-6.6 0-12-5.4-12-12s5.4-12 12-12c3.1 0 5.8 1.2 7.9 3.1l5.7-5.7C34 6.1 29.3 4 24 4 12.9 4 4 12.9 4 24s8.9 20 20 20 20-8.9 20-20c0-1.2-.1-2.4-.4-3.5z"
      />
      <path
        fill="#FF3D00"
        d="M6.3 14.7l6.6 4.8C14.7 15.1 19 12 24 12c3.1 0 5.8 1.2 7.9 3.1l5.7-5.7C34 6.1 29.3 4 24 4 16.3 4 9.7 8.3 6.3 14.7z"
      />
      <path
        fill="#4CAF50"
        d="M24 44c5.2 0 9.9-2 13.4-5.2l-6.2-5.2C29.2 35.1 26.7 36 24 36c-5.2 0-9.6-3.3-11.3-7.9l-6.5 5C9.5 39.6 16.2 44 24 44z"
      />
      <path
        fill="#1976D2"
        d="M43.6 20.5H42V20H24v8h11.3c-.8 2.2-2.2 4.2-4.1 5.6l6.2 5.2C37 39.2 44 34 44 24c0-1.2-.1-2.4-.4-3.5z"
      />
    </svg>
  )
}

export function IconMicrosoft({ size = 18 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 21 21" aria-hidden="true">
      <rect x="1" y="1" width="9" height="9" fill="#F25022" />
      <rect x="11" y="1" width="9" height="9" fill="#7FBA00" />
      <rect x="1" y="11" width="9" height="9" fill="#00A4EF" />
      <rect x="11" y="11" width="9" height="9" fill="#FFB900" />
    </svg>
  )
}
