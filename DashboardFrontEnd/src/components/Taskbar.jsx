import { useEffect, useRef, useState } from 'react'
import { NavLink, useLocation, useNavigate } from 'react-router-dom'
import {
  IconSearch,
  IconSun,
  IconMoon,
  IconGrid,
  IconPlug,
  IconShield,
  IconClose,
} from './icons/Icons.jsx'
import { clearToken } from '../api.js'
import { isAdmin, useCurrentUser } from '../useCurrentUser.js'
import './Taskbar.css'

function Avatar({ initials, shape = 'circle', size = 36 }) {
  return (
    <div
      className={`avatar avatar--${shape}`}
      style={{ width: size, height: size, fontSize: size * 0.36 }}
    >
      <span>{initials}</span>
    </div>
  )
}

const SEARCH_PLACEHOLDERS = {
  '/': 'Search widgets',
  '/services': 'Search services',
  '/admin': 'Search users',
}

function SearchBar({ query, onQueryChange }) {
  const { pathname } = useLocation()
  const inputRef = useRef(null)
  // narrow screens collapse the field to a button that expands over the toolbar
  const [expanded, setExpanded] = useState(false)
  const placeholder = SEARCH_PLACEHOLDERS[pathname] ?? 'Search'

  function expand() {
    setExpanded(true)
    requestAnimationFrame(() => inputRef.current?.focus())
  }

  function cancel() {
    onQueryChange('')
    setExpanded(false)
  }

  return (
    <div className={`search${expanded ? ' search--expanded' : ''}`}>
      <button type="button" className="search__trigger" aria-label={placeholder} onClick={expand}>
        <IconSearch size={16} />
        {query && <span className="search__dot" />}
      </button>

      <label className="search-bar">
        <IconSearch size={16} />
        <input
          ref={inputRef}
          className="search-bar__input"
          type="search"
          placeholder={placeholder}
          aria-label={placeholder}
          value={query}
          onChange={(e) => onQueryChange(e.target.value)}
          onKeyDown={(e) => e.key === 'Escape' && cancel()}
        />
        {query && (
          <button
            type="button"
            className="search-bar__clear"
            aria-label="Clear search"
            onClick={() => {
              onQueryChange('')
              inputRef.current?.focus()
            }}
          >
            <IconClose size={10} />
          </button>
        )}
      </label>

      <button type="button" className="search__cancel" onClick={cancel}>
        Cancel
      </button>
    </div>
  )
}

function ThemeToggle({ theme, onToggleTheme }) {
  const isDark = theme === 'dark'

  return (
    <button
      type="button"
      className="theme-toggle"
      role="switch"
      aria-checked={isDark}
      aria-label="Dark mode"
      onClick={onToggleTheme}
    >
      <IconSun size={13} />
      <IconMoon size={13} />
      <span className={`theme-toggle__knob${isDark ? ' theme-toggle__knob--dark' : ''}`}>
        {isDark ? <IconMoon size={14} /> : <IconSun size={14} />}
      </span>
    </button>
  )
}

function initialsOf(username) {
  const words = username.trim().split(/[\s._-]+/).filter(Boolean)
  const letters = words.length > 1 ? words[0][0] + words[1][0] : username.slice(0, 2)
  return letters.toUpperCase()
}

function UserMenu({ user }) {
  const navigate = useNavigate()
  const [open, setOpen] = useState(false)
  const rootRef = useRef(null)

  useEffect(() => {
    if (!open) return
    function onPointerDown(event) {
      if (!rootRef.current?.contains(event.target)) setOpen(false)
    }
    function onKeyDown(event) {
      if (event.key === 'Escape') setOpen(false)
    }
    document.addEventListener('pointerdown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('pointerdown', onPointerDown)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [open])

  function logout() {
    clearToken()
    navigate('/login', { replace: true })
  }

  return (
    <div className="user-menu" ref={rootRef}>
      <button
        type="button"
        className="user-button"
        aria-label="Account"
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen((current) => !current)}
      >
        <Avatar initials={user?.username ? initialsOf(user.username) : '·'} />
      </button>

      {open && (
        <div className="user-menu__popover" role="menu">
          <div className="user-menu__identity">
            <Avatar initials={user?.username ? initialsOf(user.username) : '·'} size={44} />
            <div className="user-menu__names">
              <p className="user-menu__name">{user?.username ?? 'Signed in'}</p>
              {user?.email && <p className="user-menu__email">{user.email}</p>}
            </div>
          </div>
          {isAdmin(user) && <span className="user-menu__badge">Administrator</span>}
          <button type="button" role="menuitem" className="user-menu__item user-menu__item--danger" onClick={logout}>
            Log out
          </button>
        </div>
      )}
    </div>
  )
}

function navItems(user) {
  const items = [
    { to: '/', label: 'Dashboard', icon: IconGrid, end: true },
    { to: '/services', label: 'Services', icon: IconPlug },
  ]
  if (isAdmin(user)) items.push({ to: '/admin', label: 'Admin', icon: IconShield })
  return items
}

function Taskbar({ theme, onToggleTheme, query, onQueryChange }) {
  const { user } = useCurrentUser()
  const items = navItems(user)

  return (
    <>
      <header className="toolbar">
        <div className="toolbar__brand">
          <Avatar initials="D" shape="square" size={36} />
          <p className="toolbar__title">Dashboard</p>
        </div>

        <nav className="segmented" aria-label="Main">
          {items.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              className={({ isActive }) => `segmented__item${isActive ? ' segmented__item--active' : ''}`}
            >
              {item.label}
            </NavLink>
          ))}
        </nav>

        <div className="toolbar__actions">
          <SearchBar query={query} onQueryChange={onQueryChange} />
          <ThemeToggle theme={theme} onToggleTheme={onToggleTheme} />
          <UserMenu user={user} />
        </div>
      </header>

      {/* phones get an ios tab bar instead of the segmented control */}
      <nav className="tab-bar" aria-label="Main">
        {items.map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            end={item.end}
            className={({ isActive }) => `tab-bar__item${isActive ? ' tab-bar__item--active' : ''}`}
          >
            <item.icon size={22} />
            <span>{item.label}</span>
          </NavLink>
        ))}
      </nav>
    </>
  )
}

export default Taskbar
