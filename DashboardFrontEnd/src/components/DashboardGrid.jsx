import { useEffect, useLayoutEffect, useMemo, useRef, useState, useSyncExternalStore } from 'react'
import DashboardWidget from './widgets/DashboardWidget.jsx'
import {
  GRID_COLUMNS,
  clampSize,
  displayItems,
  layoutBottom,
  layoutOf,
  orderOf,
  pack,
  resolve,
} from '../widgetLayout.js'
import './DashboardGrid.css'

const TABLET = '(max-width: 900px)'
const PHONE = '(max-width: 600px)'
// pixels the pointer must travel before a press becomes a drag, so clicks stay clicks
const DRAG_THRESHOLD = 5
// empty rows offered under the last card while dragging, so a card can be dropped lower
const SPARE_ROWS = 3
const SCROLL_EDGE = 80
const SCROLL_SPEED = 18
const SPRING = 'cubic-bezier(0.2, 0.9, 0.25, 1.1)'

function subscribeColumns(callback) {
  const queries = [window.matchMedia(TABLET), window.matchMedia(PHONE)]
  queries.forEach((q) => q.addEventListener('change', callback))
  return () => queries.forEach((q) => q.removeEventListener('change', callback))
}

function readColumns() {
  if (window.matchMedia(PHONE).matches) return 1
  if (window.matchMedia(TABLET).matches) return 6
  return GRID_COLUMNS
}

function prefersReducedMotion() {
  return window.matchMedia('(prefers-reduced-motion: reduce)').matches
}

function clamp(value, min, max) {
  return Math.min(max, Math.max(min, value))
}

// a card's slot in viewport coordinates; offsets ignore transforms, so running animations do not lie
function slotRect(card, gridRect) {
  return {
    left: gridRect.left + card.offsetLeft,
    top: gridRect.top + card.offsetTop,
    width: card.offsetWidth,
    height: card.offsetHeight,
  }
}

// the dragged card stays under the pointer, measured from the slot it currently occupies
function followPointer(s, card, grid) {
  if (!card || !grid) return
  const slot = slotRect(card, grid.getBoundingClientRect())
  const dx = s.pointerX - s.grabX - slot.left
  const dy = s.pointerY - s.grabY - slot.top
  card.style.transform = `translate3d(${dx}px, ${dy}px, 0) scale(1.03)`
}

function sameRect(a, b) {
  return a.x === b.x && a.y === b.y && a.w === b.w && a.h === b.h
}

// the 12-column grid: every card sits where the user dropped it, overlapped cards slide down
// nothing is sent while the pointer moves, onCommit runs once with the whole layout when released
// narrower screens stack the cards in reading order and leave the arrangement to the desktop
function DashboardGrid({
  instances,
  visibleIds,
  results,
  widgetsByKey,
  editable,
  editing,
  onCommit,
  onRefresh,
  onEdit,
  onDelete,
}) {
  const columns = useSyncExternalStore(subscribeColumns, readColumns)
  const gridRef = useRef(null)
  const cardRefs = useRef(new Map())
  // where every card was on screen just before a layout change, for the flip animation
  const snapshot = useRef(null)
  const session = useRef(null)
  const [draft, setDraft] = useState(null)
  const [active, setActive] = useState(null)

  const stored = useMemo(() => layoutOf(instances), [instances])
  const layout = draft ?? stored
  const byId = new Map(instances.map((i) => [i.id, i]))
  const free = columns === GRID_COLUMNS && !visibleIds
  const canArrange = editable && free

  // free mode shows the layout as is; filtered or narrow views pack the cards in reading order
  const shown = orderOf(layout).filter((id) => byId.has(id) && (!visibleIds || visibleIds.has(id)))
  const placed = free
    ? layout
    : pack(
        displayItems(
          shown.map((id) => ({ id, w: layout[id].w, h: layout[id].h })),
          columns,
        ),
        columns,
      )
  const rows = Math.max(1, free ? layoutBottom(layout) + (active ? SPARE_ROWS : 0) : 0)
  const layoutKey = JSON.stringify(placed)
  // the dom keeps a stable order: moving a node would drop the pointer and replay its entrance
  const domOrder = [...shown].sort((a, b) => a - b)

  function takeSnapshot() {
    const rects = new Map()
    for (const [id, el] of cardRefs.current) {
      const r = el.getBoundingClientRect()
      rects.set(id, { left: r.left, top: r.top })
    }
    snapshot.current = rects
    // a change that moved nothing never runs the effect, do not replay this snapshot later
    setTimeout(() => {
      if (snapshot.current === rects) snapshot.current = null
    }, 200)
  }

  // other cards glide from where they were on screen, even if they were mid-animation
  useLayoutEffect(() => {
    const previous = snapshot.current
    snapshot.current = null
    const s = session.current
    if (s?.dragging && s.mode === 'move') followPointer(s, cardRefs.current.get(s.id), gridRef.current)
    if (!previous || prefersReducedMotion()) return

    for (const [id, el] of cardRefs.current) {
      if (s?.dragging && s.mode === 'move' && id === s.id) continue
      const before = previous.get(id)
      if (!before) continue
      el.getAnimations().forEach((animation) => animation.cancel())
      const after = el.getBoundingClientRect()
      const dx = before.left - after.left
      const dy = before.top - after.top
      if (Math.abs(dx) < 1 && Math.abs(dy) < 1) continue
      el.animate([{ transform: `translate(${dx}px, ${dy}px)` }, { transform: 'none' }], {
        duration: 320,
        easing: SPRING,
      })
    }
  }, [layoutKey])

  // a drag in progress when the grid unmounts must not leave listeners behind
  useEffect(() => {
    const current = session
    return () => current.current?.detach()
  }, [])

  function update(next) {
    takeSnapshot()
    setDraft(next)
  }

  function begin(event, id, mode, fromHandle) {
    if (!canArrange || event.button !== 0 || session.current) return
    // the header is a mouse convenience; on touch it must keep scrolling the page
    if (!fromHandle && (event.pointerType === 'touch' || event.target.closest('button, a'))) return
    // no text selection, no focus jump
    event.preventDefault()

    const listeners = {
      pointermove: onPointerMove,
      pointerup: onPointerUp,
      pointercancel: onPointerCancel,
      keydown: onKeyDown,
      // alt-tab mid-drag: the pointerup never comes, drop the card where it is
      blur: onBlur,
    }
    for (const [type, listener] of Object.entries(listeners)) window.addEventListener(type, listener)

    session.current = {
      id,
      mode,
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      startScroll: window.scrollY,
      pointerX: event.clientX,
      pointerY: event.clientY,
      dragging: false,
      frame: 0,
      detach() {
        for (const [type, listener] of Object.entries(listeners)) window.removeEventListener(type, listener)
        cancelAnimationFrame(this.frame)
        document.documentElement.classList.remove('is-dragging-widget', 'is-resizing-widget')
      },
    }
  }

  function start() {
    const s = session.current
    const card = cardRefs.current.get(s.id)
    const grid = gridRef.current
    const slot = slotRect(card, grid.getBoundingClientRect())
    const style = getComputedStyle(grid)
    const gap = parseFloat(style.columnGap) || 0

    Object.assign(s, {
      dragging: true,
      // every step is computed from the layout at grab time, so cards pushed aside come back
      // when the dragged one moves on
      base: stored,
      layout: stored,
      grabX: s.startX - slot.left,
      grabY: s.startY - slot.top,
      colStep: (grid.clientWidth - gap * (columns - 1)) / columns + gap,
      rowStep: (parseFloat(style.gridAutoRows) || 88) + (parseFloat(style.rowGap) || 0),
    })
    card.getAnimations().forEach((animation) => animation.cancel())
    document.documentElement.classList.add(s.mode === 'move' ? 'is-dragging-widget' : 'is-resizing-widget')
    setDraft(stored)
    setActive({ id: s.id, mode: s.mode })
    s.frame = requestAnimationFrame(tick)
  }

  function onPointerMove(event) {
    const s = session.current
    if (!s || event.pointerId !== s.pointerId) return
    s.pointerX = event.clientX
    s.pointerY = event.clientY
    if (!s.dragging && Math.hypot(event.clientX - s.startX, event.clientY - s.startY) >= DRAG_THRESHOLD) {
      start()
    }
  }

  // all the work happens once per frame, from the last known pointer position
  function tick() {
    const s = session.current
    if (!s?.dragging) return
    autoScroll(s)
    if (s.mode === 'move') {
      followPointer(s, cardRefs.current.get(s.id), gridRef.current)
      moveTo(s)
    } else {
      resizeTo(s)
    }
    s.frame = requestAnimationFrame(tick)
  }

  function autoScroll(s) {
    let delta = 0
    if (s.pointerY < SCROLL_EDGE) delta = -SCROLL_SPEED * (1 - s.pointerY / SCROLL_EDGE)
    else if (s.pointerY > window.innerHeight - SCROLL_EDGE) {
      delta = SCROLL_SPEED * (1 - (window.innerHeight - s.pointerY) / SCROLL_EDGE)
    }
    if (delta) window.scrollBy(0, delta)
  }

  // the cell under the card's top-left corner, wherever the pointer grabbed it
  function moveTo(s) {
    const gridRect = gridRef.current.getBoundingClientRect()
    const rect = s.base[s.id]
    const x = clamp(Math.round((s.pointerX - s.grabX - gridRect.left) / s.colStep), 0, GRID_COLUMNS - rect.w)
    const y = Math.max(0, Math.round((s.pointerY - s.grabY - gridRect.top) / s.rowStep))
    place(s, { ...rect, x, y })
  }

  function resizeTo(s) {
    const scrolled = window.scrollY - s.startScroll
    const rect = s.base[s.id]
    const size = clampSize(
      byId.get(s.id).widgetKey,
      rect.w + Math.round((s.pointerX - s.startX) / s.colStep),
      rect.h + Math.round((s.pointerY + scrolled - s.startY) / s.rowStep),
    )
    // growing past the right edge is not possible, the card would leave the grid
    place(s, { ...rect, w: Math.min(size.w, GRID_COLUMNS - rect.x), h: size.h })
  }

  function place(s, rect) {
    if (sameRect(rect, s.layout[s.id])) return
    s.layout = resolve({ ...s.base, [s.id]: rect }, s.id)
    update(s.layout)
  }

  function stop() {
    const s = session.current
    session.current = null
    s?.detach()
    return s
  }

  // the lifted card lands in its slot instead of teleporting
  function settle(s) {
    const card = cardRefs.current.get(s.id)
    if (!card || s.mode !== 'move') return
    const from = card.style.transform
    card.style.transform = ''
    if (from && !prefersReducedMotion()) {
      card.animate([{ transform: from }, { transform: 'none' }], { duration: 280, easing: SPRING })
    }
  }

  function finish(commit) {
    const s = stop()
    if (!s?.dragging) return
    settle(s)
    if (commit) onCommit(s.layout)
    // cancelling puts the others back where they were
    else takeSnapshot()
    setDraft(null)
    setActive(null)
  }

  function onPointerUp(event) {
    if (event.pointerId === session.current?.pointerId) finish(true)
  }

  function onPointerCancel(event) {
    if (event.pointerId === session.current?.pointerId) finish(false)
  }

  function onBlur() {
    finish(true)
  }

  function onKeyDown(event) {
    if (event.key === 'Escape') finish(false)
  }

  // keyboard alternative: arrows move the card one cell, or change its size by one cell
  function keyMove(event, id) {
    const delta = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] }[event.key]
    if (!delta || !canArrange) return
    event.preventDefault()
    const rect = stored[id]
    takeSnapshot()
    onCommit(
      resolve(
        {
          ...stored,
          [id]: { ...rect, x: clamp(rect.x + delta[0], 0, GRID_COLUMNS - rect.w), y: Math.max(0, rect.y + delta[1]) },
        },
        id,
      ),
    )
  }

  function keyResize(event, id) {
    const delta = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] }[event.key]
    if (!delta || !canArrange) return
    event.preventDefault()
    const rect = stored[id]
    const size = clampSize(byId.get(id).widgetKey, rect.w + delta[0], rect.h + delta[1])
    takeSnapshot()
    onCommit(resolve({ ...stored, [id]: { ...rect, w: Math.min(size.w, GRID_COLUMNS - rect.x), h: size.h } }, id))
  }

  const ghost = active?.mode === 'move' ? placed[active.id] : null

  return (
    <div
      className={`widget-grid${free ? ' widget-grid--free' : ''}${active ? ' widget-grid--active' : ''}`}
      ref={gridRef}
      style={{ '--grid-columns': columns, '--grid-rows': free ? rows : undefined }}
    >
      {/* the slot the dragged card will land in */}
      {ghost && (
        <div
          className="widget-grid__placeholder"
          aria-hidden="true"
          style={{ gridColumn: `${ghost.x + 1} / span ${ghost.w}`, gridRow: `${ghost.y + 1} / span ${ghost.h}` }}
        />
      )}

      {domOrder.map((id) => {
        const instance = byId.get(id)
        const p = placed[id]
        const title = widgetsByKey[instance.widgetKey]?.name ?? instance.widgetKey
        return (
          <DashboardWidget
            key={id}
            instance={instance}
            widget={widgetsByKey[instance.widgetKey]}
            result={results[id]}
            style={{
              gridColumn: `${p.x + 1} / span ${p.w}`,
              gridRow: `${p.y + 1} / span ${p.h}`,
            }}
            cardRef={(el) => {
              if (el) cardRefs.current.set(id, el)
              else cardRefs.current.delete(id)
            }}
            dragging={active?.id === id && active.mode === 'move'}
            resizing={active?.id === id && active.mode === 'resize'}
            editing={editing}
            dragArea={canArrange ? { onPointerDown: (e) => begin(e, id, 'move', false) } : null}
            dragHandle={
              canArrange && {
                'aria-label': `Move ${title}, use the arrow keys to place it`,
                onPointerDown: (e) => begin(e, id, 'move', true),
                onKeyDown: (e) => keyMove(e, id),
              }
            }
            resizeHandle={
              canArrange && {
                'aria-label': `Resize ${title}, use the arrow keys to change its size`,
                onPointerDown: (e) => begin(e, id, 'resize', true),
                onKeyDown: (e) => keyResize(e, id),
              }
            }
            onRefresh={() => onRefresh(instance)}
            onEdit={() => onEdit(instance)}
            onDelete={() => onDelete(instance)}
          />
        )
      })}
    </div>
  )
}

export default DashboardGrid
