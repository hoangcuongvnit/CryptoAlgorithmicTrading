export function StatCard({ title, value, subtitle, icon, colorClass = 'text-gray-800', bgClass = 'bg-white' }) {
  return (
    <div className={`rounded-xl shadow-sm border border-gray-100 p-4 ${bgClass}`}>
      <div className="flex items-start justify-between">
        <div>
          <p className="text-sm text-gray-500 font-medium">{title}</p>
          <p className={`text-2xl font-bold mt-1 ${colorClass}`}>{value ?? '—'}</p>
          {subtitle && <p className="text-xs text-gray-400 mt-1">{subtitle}</p>}
        </div>
        {icon && <span className="text-3xl">{icon}</span>}
      </div>
    </div>
  )
}
