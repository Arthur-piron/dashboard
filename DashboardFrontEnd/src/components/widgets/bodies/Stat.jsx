// petit bloc libelle / valeur reutilise par les widgets
function Stat({ label, children }) {
  return (
    <div>
      <p className="widget-body__stat-label">{label}</p>
      <p className="widget-body__stat-value">{children}</p>
    </div>
  )
}

export default Stat
