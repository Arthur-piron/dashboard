import { IconClose } from './icons/Icons.jsx'
import './Notice.css'

function Notice({ tone = 'info', children, onDismiss }) {
  return (
    <div className={`notice notice--${tone}`} role={tone === 'error' ? 'alert' : 'status'}>
      <p className="notice__text">{children}</p>
      {onDismiss && (
        <button type="button" className="notice__dismiss" aria-label="Dismiss" onClick={onDismiss}>
          <IconClose size={12} />
        </button>
      )}
    </div>
  )
}

export default Notice
