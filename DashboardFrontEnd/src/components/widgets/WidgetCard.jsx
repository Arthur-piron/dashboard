import { IconDragHandle, IconResizeHandle } from '../icons/Icons.jsx'
import './WidgetCard.css'

// glass shell shared by every widget: header, body, optional drag and resize handles
function WidgetCard({
  title,
  subtitle,
  actions,
  dragArea,
  dragHandle,
  resizeHandle,
  className = '',
  style,
  cardRef,
  children,
  ...rest
}) {
  return (
    <article ref={cardRef} className={`widget-card ${className}`} style={style} {...rest}>
      <header className={`widget-card__header${dragArea ? ' widget-card__header--draggable' : ''}`} {...dragArea}>
        <div className="widget-card__heading">
          <p className="widget-card__title">{title}</p>
          {subtitle && <p className="widget-card__subtitle">{subtitle}</p>}
        </div>
        <div className="widget-card__tools">
          {actions}
          {dragHandle && (
            <button type="button" className="widget-card__drag-handle" {...dragHandle}>
              <IconDragHandle size={16} />
            </button>
          )}
        </div>
      </header>

      <div className="widget-card__content">{children}</div>

      {resizeHandle && (
        <button type="button" className="widget-card__resize-handle" {...resizeHandle}>
          <IconResizeHandle size={18} />
        </button>
      )}
    </article>
  )
}

export default WidgetCard
