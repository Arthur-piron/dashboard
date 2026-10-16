import { useState } from 'react'
import WidgetCard from './WidgetCard.jsx'
import WidgetBody from './bodies/WidgetBody.jsx'
import ErrorBoundary from '../ErrorBoundary.jsx'
import { IconPencil, IconRefresh, IconTrash } from '../icons/Icons.jsx'
import { formatRefreshRate } from '../../widgetConfig.js'
import './DashboardWidget.css'

// refresh is always there, edit and remove only while the dashboard is in edit mode
function WidgetActions({ title, editing, onRefresh, onEdit, onDelete }) {
  const [refreshing, setRefreshing] = useState(false)

  async function refresh() {
    setRefreshing(true)
    try {
      await onRefresh()
    } finally {
      setRefreshing(false)
    }
  }

  return (
    <>
      <button
        type="button"
        className={`widget-card__tool${refreshing ? ' widget-card__tool--spinning' : ''}`}
        aria-label={`Refresh ${title}`}
        title="Refresh now"
        disabled={refreshing}
        onClick={refresh}
      >
        <IconRefresh size={16} />
      </button>
      {editing && (
        <>
          <button type="button" className="widget-card__tool" aria-label={`Edit ${title}`} title="Edit" onClick={onEdit}>
            <IconPencil size={16} />
          </button>
          <button
            type="button"
            className="widget-card__tool widget-card__tool--danger"
            aria-label={`Remove ${title}`}
            title="Remove"
            onClick={onDelete}
          >
            <IconTrash size={16} />
          </button>
        </>
      )}
    </>
  )
}

// one instance on the dashboard: card, live data and its actions
function DashboardWidget({
  instance,
  widget,
  result,
  style,
  cardRef,
  dragging,
  resizing,
  editing,
  dragArea,
  dragHandle,
  resizeHandle,
  onRefresh,
  onEdit,
  onDelete,
}) {
  const title = widget?.name ?? instance.widgetKey
  const classes = [
    dragging && 'widget-card--dragging',
    resizing && 'widget-card--resizing',
    editing && 'widget-card--editing',
  ]
    .filter(Boolean)
    .join(' ')

  return (
    <WidgetCard
      title={title}
      subtitle={`Every ${formatRefreshRate(instance.refreshRateSeconds)}`}
      className={classes}
      style={style}
      cardRef={cardRef}
      dragArea={dragArea}
      dragHandle={dragHandle}
      resizeHandle={resizeHandle}
      actions={
        <WidgetActions title={title} editing={editing} onRefresh={onRefresh} onEdit={onEdit} onDelete={onDelete} />
      }
    >
      <ErrorBoundary
        fallback={<p className="widget-body__note widget-body__note--error">This widget could not be displayed.</p>}
      >
        {/* the size the body layouts query: a card changes layout as it grows, it does not zoom */}
        <div className="widget-frame">
          <WidgetBody result={result} />
        </div>
      </ErrorBoundary>
    </WidgetCard>
  )
}

export default DashboardWidget
