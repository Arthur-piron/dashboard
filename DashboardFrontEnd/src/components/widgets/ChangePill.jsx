import { IconTrendUp, IconTrendDown } from '../icons/Icons.jsx'
import './ChangePill.css'

// badge +/- en vert ou rouge, utilise par les widgets crypto
function ChangePill({ positive, children }) {
  return (
    <div className={`change-pill ${positive ? 'change-pill--up' : 'change-pill--down'}`}>
      {positive ? <IconTrendUp size={11} /> : <IconTrendDown size={11} />}
      <p>{children}</p>
    </div>
  )
}

export default ChangePill
