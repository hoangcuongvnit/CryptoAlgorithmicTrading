export function StatCard({ title, value, subtitle, icon, colorClass = 'text-gray-800', bgClass = '' }) {
  return (
    <div
      className={`rounded-xl p-4 ${bgClass}`}
      style={{
        background: bgClass ? undefined : '#ffffff',
        border: '1px solid #dbe4ef',
        boxShadow: '0 8px 30px rgba(15, 23, 42, 0.08)',
      }}
    >
      <div className="flex items-start justify-between">
        <div>
          <p className="text-sm font-medium" style={{ color: '#475569' }}>{title}</p>
          <p className={`text-2xl font-bold mt-1 ${colorClass}`}>{value ?? '—'}</p>
          {subtitle && <p className="text-xs mt-1" style={{ color: '#94a3b8' }}>{subtitle}</p>}
        </div>
        {icon && <span className="text-3xl">{icon}</span>}
      </div>
    </div>
  )
}
