import { useTranslation } from 'react-i18next'
import { SignalCard } from '../components/SignalCard.jsx'
import { StatCard } from '../components/StatCard.jsx'
import { useRiskConfig, useOrders, useTradingStats, useOpenPositions } from '../hooks/useDashboard.js'
import { formatPrice } from '../utils/indicators.js'
import { useSettings } from '../context/SettingsContext.jsx'
import { formatTime } from '../utils/dateFormat.js'

const DEFAULT_SYMBOLS = ['BTCUSDT', 'ETHUSDT', 'BNBUSDT', 'SOLUSDT', 'XRPUSDT']

function PnLValue({ value }) {
  if (value == null) return <span className="text-gray-400">—</span>
  const n = Number(value)
  const color = n >= 0 ? 'text-green-600' : 'text-red-500'
  const sign = n >= 0 ? '+' : ''
  return <span className={`font-semibold ${color}`}>{sign}${n.toFixed(2)}</span>
}

const TABLE_STYLE = { border: '1px solid #dbe4ef', boxShadow: '0 2px 8px rgba(15,23,42,0.05)' }

function TableHeader({ children }) {
  return (
    <th className="text-left py-2.5 px-4 text-xs font-semibold text-gray-500 uppercase tracking-wide whitespace-nowrap">
      {children}
    </th>
  )
}

function PositionRow({ pos }) {
  const { t } = useTranslation('trading')
  const pnl = Number(pos.unrealizedPnL ?? 0)
  const roe = Number(pos.roe ?? 0)
  const pnlColor = pnl >= 0 ? 'text-green-600' : 'text-red-500'
  const sign = pnl >= 0 ? '+' : ''

  return (
    <tr className="border-b border-gray-100 hover:bg-blue-50/30 transition-colors">
      <td className="py-3 px-4">
        <div className="flex items-center gap-2">
          <span className="font-semibold text-gray-800">{pos.symbol?.replace('USDT', '/USDT')}</span>
          <span className="text-xs font-medium px-1.5 py-0.5 rounded bg-green-100 text-green-700">
            {t('position.long')}
          </span>
        </div>
      </td>
      <td className="py-3 px-4 text-sm text-gray-700 tabular-nums">{Number(pos.quantity).toFixed(6)}</td>
      <td className="py-3 px-4 text-sm text-gray-700 tabular-nums">${formatPrice(pos.entryPrice)}</td>
      <td className="py-3 px-4 text-sm text-gray-700 tabular-nums">${formatPrice(pos.currentPrice)}</td>
      <td className={`py-3 px-4 text-sm font-semibold tabular-nums ${pnlColor}`}>
        {sign}${pnl.toFixed(2)}
        <span className="ml-1 text-xs font-normal opacity-75">({sign}{roe.toFixed(2)}%)</span>
      </td>
    </tr>
  )
}

function OrderRow({ order }) {
  const { t } = useTranslation('trading')
  const { systemTimezone } = useSettings()
  const isSuccess = order.success ?? order.filledQty > 0
  const side = (order.side ?? '').toLowerCase()
  const hasPnL = order.realizedPnL != null
  const pnl = Number(order.realizedPnL ?? 0)
  const isClosed = (order.status ?? '').toUpperCase() === 'CLOSED'

  return (
    <tr className="border-b border-gray-100 hover:bg-blue-50/30 transition-colors">
      <td className="py-3 px-4">
        <div className="flex items-center gap-2">
          <span className={`inline-block w-2 h-2 rounded-full flex-shrink-0 ${isSuccess ? 'bg-green-400' : 'bg-red-400'}`} />
          <span className="font-semibold text-gray-800">{order.symbol?.replace('USDT', '/USDT')}</span>
          <span className={`text-xs font-medium px-1.5 py-0.5 rounded ${
            side === 'buy' ? 'bg-green-100 text-green-700' : 'bg-red-100 text-red-700'
          }`}>
            {(order.side ?? '').toUpperCase()}
          </span>
        </div>
      </td>
      <td className="py-3 px-4 text-sm text-gray-700 tabular-nums">${formatPrice(order.filledPrice ?? order.entryPrice)}</td>
      <td className="py-3 px-4 text-sm text-gray-700 tabular-nums">{Number(order.filledQty ?? order.quantity ?? 0).toFixed(6)}</td>
      <td className="py-3 px-4 text-sm">
        {hasPnL && isClosed && <PnLValue value={pnl} />}
        {!hasPnL && isSuccess && (
          <span className="text-xs px-1.5 py-0.5 rounded bg-blue-50 text-blue-600">{t('order.open')}</span>
        )}
        {!isSuccess && (
          <span className="text-xs text-red-500 truncate" title={order.errorMessage}>{t('order.failed')}</span>
        )}
      </td>
      <td className="py-3 px-4 text-xs text-gray-400 whitespace-nowrap tabular-nums">
        {formatTime(order.createdAt, systemTimezone)}
      </td>
    </tr>
  )
}

export function TradingPage() {
  const { t } = useTranslation('trading')
  const { data: config } = useRiskConfig()
  const { data: orders, loading: ordersLoading } = useOrders()
  const { data: stats } = useTradingStats()
  const { data: positions } = useOpenPositions()

  const symbols = config?.allowedSymbols?.length > 0 ? config.allowedSymbols : DEFAULT_SYMBOLS

  const pnlColor = (stats?.totalPnL ?? 0) >= 0 ? 'text-green-600' : 'text-red-500'
  const pnlSign = (stats?.totalPnL ?? 0) >= 0 ? '+' : ''

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-bold" style={{ color: '#0f172a' }}>{t('title')}</h1>

      {/* Trading Stats */}
      {stats && (
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
          <StatCard
            title={t('stats.totalPnL')}
            value={`${pnlSign}$${Number(stats.totalPnL ?? 0).toFixed(2)}`}
            colorClass={pnlColor}
            subtitle={t('stats.realized')}
            icon="💰"
          />
          <StatCard
            title={t('stats.winRate')}
            value={`${(Number(stats.winRate ?? 0) * 100).toFixed(1)}%`}
            subtitle={`${stats.winTrades ?? 0}W / ${stats.lossTrades ?? 0}L`}
            colorClass="text-blue-600"
            icon="🎯"
          />
          <StatCard
            title={t('stats.maxDrawdown')}
            value={`${(Number(stats.maxDrawdown ?? 0) * 100).toFixed(2)}%`}
            colorClass={Number(stats.maxDrawdown ?? 0) > 0.1 ? 'text-red-500' : 'text-gray-700'}
            icon="📉"
          />
          <StatCard
            title={t('stats.totalTrades')}
            value={stats.totalTrades ?? 0}
            subtitle={t('stats.realized')}
            colorClass="text-purple-600"
            icon="📊"
          />
        </div>
      )}

      {/* Open Positions */}
      <div>
        <h2 className="text-lg font-semibold mb-3" style={{ color: '#0f172a' }}>
          {t('openPositions')}
          {positions?.length > 0 && (
            <span className="ml-2 text-sm font-normal bg-blue-100 text-blue-700 px-2 py-0.5 rounded-full">
              {positions.length}
            </span>
          )}
        </h2>
        {!positions || positions.length === 0 ? (
          <div className="text-center py-6 text-gray-400 rounded-lg" style={{ border: '1px dashed #dbe4ef' }}>
            <p className="text-2xl mb-1">📭</p>
            <p className="text-sm">{t('position.none')}</p>
          </div>
        ) : (
          <div className="overflow-x-auto rounded-xl" style={TABLE_STYLE}>
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-gray-200 bg-gray-50/80">
                  <TableHeader>{t('col.symbol')}</TableHeader>
                  <TableHeader>{t('position.qty')}</TableHeader>
                  <TableHeader>{t('position.entry')}</TableHeader>
                  <TableHeader>{t('position.current')}</TableHeader>
                  <TableHeader>P&L / ROE</TableHeader>
                </tr>
              </thead>
              <tbody className="bg-white">
                {positions.map((pos, i) => (
                  <PositionRow key={pos.symbol ?? i} pos={pos} />
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Signal Cards */}
      <div>
        <h2 className="text-lg font-semibold mb-2" style={{ color: '#0f172a' }}>{t('signals')}</h2>
        <p className="text-sm text-gray-500 mb-3">{t('subtitle')}</p>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
          {symbols.map(sym => (
            <SignalCard key={sym} symbol={sym} />
          ))}
        </div>
      </div>

      {/* Recent Orders */}
      <div>
        <h2 className="text-lg font-semibold mb-3" style={{ color: '#0f172a' }}>{t('recentOrders')}</h2>
        {ordersLoading && !orders && (
          <div className="space-y-2">
            {[1, 2, 3].map(i => (
              <div key={i} className="h-14 bg-gray-100 rounded-lg animate-pulse" />
            ))}
          </div>
        )}
        {orders?.length === 0 && (
          <div className="text-center py-10 text-gray-400">
            <p className="text-4xl mb-2">📭</p>
            <p>{t('order.noOrders')}</p>
          </div>
        )}
        {orders && orders.length > 0 && (
          <div className="overflow-x-auto rounded-xl" style={TABLE_STYLE}>
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-gray-200 bg-gray-50/80">
                  <TableHeader>{t('col.symbol')}</TableHeader>
                  <TableHeader>{t('order.price')}</TableHeader>
                  <TableHeader>{t('order.qty')}</TableHeader>
                  <TableHeader>P&L</TableHeader>
                  <TableHeader>{t('col.time')}</TableHeader>
                </tr>
              </thead>
              <tbody className="bg-white">
                {orders.map((order, i) => (
                  <OrderRow key={order.orderId ?? i} order={order} />
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  )
}
