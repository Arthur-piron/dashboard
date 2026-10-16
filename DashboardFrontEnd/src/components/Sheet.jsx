import { useEffect, useRef } from 'react'
import './Sheet.css'

// modal glass sheet: centered on desktop, docked to the bottom on phones
// showModal gives focus trapping, escape to close and inert background for free
function Sheet({ onClose, labelledBy, size = 'md', children }) {
  const ref = useRef(null)

  useEffect(() => {
    const dialog = ref.current
    const previous = document.activeElement
    dialog?.showModal()
    return () => {
      if (previous instanceof HTMLElement) previous.focus()
    }
  }, [])

  return (
    <dialog
      ref={ref}
      className={`sheet sheet--${size}`}
      aria-labelledby={labelledBy}
      onClose={onClose}
      onClick={(event) => {
        // a click on the backdrop lands on the dialog itself, never on its content
        if (event.target === ref.current) ref.current.close()
      }}
    >
      {children}
    </dialog>
  )
}

export default Sheet
