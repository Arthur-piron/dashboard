export const GRID_COLUMNS = 12
export const MAX_SPAN = 12

// sizes in grid spans (12 columns, rows of --grid-row height)
// max stops a card where its layout runs out of things to show: past it, it would only add empty space
const SIZES = {
  'weather.current': { default: { w: 4, h: 3 }, min: { w: 3, h: 2 }, max: { w: 6, h: 4 } },
  'weather.forecast': { default: { w: 4, h: 4 }, min: { w: 3, h: 3 }, max: { w: 12, h: 5 } },
  'crypto.price': { default: { w: 4, h: 3 }, min: { w: 3, h: 2 }, max: { w: 6, h: 4 } },
  'crypto.history': { default: { w: 4, h: 3 }, min: { w: 3, h: 3 }, max: { w: 12, h: 6 } },
  'nasa.apod': { default: { w: 4, h: 5 }, min: { w: 3, h: 3 }, max: { w: 10, h: 7 } },
  'nasa.mars': { default: { w: 6, h: 4 }, min: { w: 4, h: 3 }, max: { w: 12, h: 6 } },
  'duolingo.profile': { default: { w: 4, h: 3 }, min: { w: 3, h: 2 }, max: { w: 6, h: 4 } },
  'duolingo.course': { default: { w: 4, h: 2 }, min: { w: 3, h: 2 }, max: { w: 6, h: 3 } },
  'github.profile': { default: { w: 4, h: 3 }, min: { w: 3, h: 3 }, max: { w: 8, h: 4 } },
  'github.repos': { default: { w: 4, h: 5 }, min: { w: 3, h: 3 }, max: { w: 12, h: 8 } },
  'github.activity': { default: { w: 5, h: 5 }, min: { w: 3, h: 3 }, max: { w: 12, h: 5 } },
}
const FALLBACK = { default: { w: 4, h: 3 }, min: { w: 2, h: 2 }, max: { w: MAX_SPAN, h: MAX_SPAN } }

export function defaultSize(widgetKey) {
  return (SIZES[widgetKey] ?? FALLBACK).default
}

export function minSize(widgetKey) {
  return (SIZES[widgetKey] ?? FALLBACK).min
}

export function maxSize(widgetKey) {
  return (SIZES[widgetKey] ?? FALLBACK).max
}

export function clampSize(widgetKey, w, h) {
  const min = minSize(widgetKey)
  const max = maxSize(widgetKey)
  return {
    w: Math.min(max.w, Math.max(min.w, w)),
    h: Math.min(max.h, Math.max(min.h, h)),
  }
}

// stored sizes outside the range (e.g. the api's 1x1 default, or a card from before the caps) are shown clamped
export function sizeOf(instance) {
  return clampSize(instance.widgetKey, instance.position.width, instance.position.height)
}

// reading order of a layout: top to bottom, then left to right
export function orderOf(layout) {
  return Object.keys(layout)
    .map(Number)
    .sort((a, b) => layout[a].y - layout[b].y || layout[a].x - layout[b].x || a - b)
}

function overlaps(a, b) {
  return a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h
}

// free placement: the fixed card keeps its spot, every card it overlaps slides down just enough,
// and so on in cascade; cards that overlap nothing never move, gaps included
export function resolve(layout, fixedId = null) {
  const settled = []
  if (fixedId !== null) settled.push([fixedId, layout[fixedId]])
  for (const id of orderOf(layout)) {
    if (id === fixedId) continue
    const rect = { ...layout[id] }
    let hit
    while ((hit = settled.find(([, other]) => overlaps(rect, other)))) rect.y = hit[1].y + hit[1].h
    settled.push([id, rect])
  }
  return Object.fromEntries(settled)
}

// the stored positions as a layout, sizes clamped and overlaps (old data, concurrent adds) cleared
export function layoutOf(instances) {
  const layout = {}
  for (const instance of instances) {
    const size = sizeOf(instance)
    layout[instance.id] = {
      x: Math.max(0, Math.min(instance.position.x, GRID_COLUMNS - size.w)),
      y: Math.max(0, instance.position.y),
      ...size,
    }
  }
  return resolve(layout)
}

// first free row under every card of a layout
export function layoutBottom(layout) {
  return Object.values(layout).reduce((max, rect) => Math.max(max, rect.y + rect.h), 0)
}

// sparse auto-placement, like css grid: each item goes after the previous one, first slot that fits
export function pack(items, columns) {
  const rows = []
  const placed = {}
  let cursorRow = 0
  let cursorCol = 0

  function fits(row, col, w, h) {
    if (col + w > columns) return false
    for (let r = row; r < row + h; r++) {
      for (let c = col; c < col + w; c++) {
        if (rows[r]?.[c]) return false
      }
    }
    return true
  }

  for (const item of items) {
    const w = Math.min(item.w, columns)
    const h = item.h
    let row = cursorRow
    let col = cursorCol
    while (!fits(row, col, w, h)) {
      col++
      if (col + w > columns) {
        col = 0
        row++
      }
    }
    for (let r = row; r < row + h; r++) {
      rows[r] ??= []
      for (let c = col; c < col + w; c++) rows[r][c] = true
    }
    placed[item.id] = { x: col, y: row, w, h }
    cursorRow = row
    cursorCol = col + w
  }

  return placed
}

// narrower screens: 6 columns with half or full width cards, then a single column
export function displayItems(items, columns) {
  if (columns === GRID_COLUMNS) return items
  if (columns === 1) return items.map((item) => ({ ...item, w: 1 }))
  return items.map((item) => ({ ...item, w: item.w >= 7 ? columns : columns / 2 }))
}

// first free row under everything, where a new widget goes
export function bottomRow(instances) {
  return instances.reduce((max, i) => Math.max(max, i.position.y + sizeOf(i).h), 0)
}
