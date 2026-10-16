import { useId, useState } from 'react'
import Sheet from './Sheet.jsx'

// ios-style destructive confirmation; onConfirm may be async and may throw to show an error
function ConfirmDialog({ title, message, confirmLabel, onConfirm, onClose }) {
  const titleId = useId()
  const [state, setState] = useState({ busy: false, error: '' })

  async function confirm() {
    setState({ busy: true, error: '' })
    try {
      await onConfirm()
      onClose()
    } catch (error) {
      setState({ busy: false, error: error.message || 'Something went wrong.' })
    }
  }

  return (
    <Sheet size="sm" labelledBy={titleId} onClose={onClose}>
      <div className="sheet__body">
        <div className="sheet__header">
          <h2 className="sheet__title" id={titleId}>
            {title}
          </h2>
          {message && <p className="sheet__subtitle">{message}</p>}
        </div>
        {state.error && <p className="sheet__note sheet__note--error">{state.error}</p>}
        <div className="sheet__actions">
          <button type="button" className="button" onClick={onClose} autoFocus>
            Cancel
          </button>
          <button
            type="button"
            className="button button--danger-solid"
            disabled={state.busy}
            onClick={confirm}
          >
            {state.busy ? 'Working…' : confirmLabel}
          </button>
        </div>
      </div>
    </Sheet>
  )
}

export default ConfirmDialog
