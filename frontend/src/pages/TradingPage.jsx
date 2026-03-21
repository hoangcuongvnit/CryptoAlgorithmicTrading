import { useTranslation } from 'react-i18next'
import { SignalCard } from '../components/SignalCard.jsx'
import { useRiskConfig, useOrders } from '../hooks/useDashboard.js'
import { formatPrice } from '../utils/indicators.js'

const DEFAULT_SYMBOLS = ['BTCUSDT', 'ETHUSDT', 'BNBUSDT', 'SOLUSDT', 'XRPUSDT']

function OrderRow({ order }) {
  const { t } = useTranslation('trading')
  const isSuccess = order.success ?? order.filledQty > 0
  const isPaper = order.isPaperTrade
  const side = order.side?.toLowerCase()

  return (
    <div className="flex items-center gap-3 p-3 rounded-lg transition-colors" style={{ background: '#ffffff', border: '1px solid #dbe4ef', boxShadow: '0 2px 8px rgba(15, 23, 42, 0.05)' }}>
      <div className="flex-shrink-0">
        <span className={`inline-block w-2 h-2 rounded-full ${isSuccess ? 'bg-green-400' : 'bg-red-400'}`} />
      </div>
      <div className="flex-1 grid grid-cols-2 sm:grid-cols-4 gap-2 text-sm">
        <div>
          <span className="font-semibold text-gray-800">{order.symbol?.replace('USDT', '/USDT')}</span>
          <span className={`ml-2 text-xs font-medium px-1.5 py-0.5 rounded ${
            side === 'buy' ? 'bg-green-100 text-green-700' : 'bg-red-100 text-red-700'
          }`}>
            {order.side?.toUpperCase()}
          </span>
        </div>
        <div className="text-gray-600">
          <span className="text-xs text-gray-400">{t('order.price')} </span>
          <span className="font-medium">${formatPrice(order.filledPrice || order.entryPrice)}</span>
        </div>
        <div className="text-gray-600">
          <span className="text-xs text-gray-400">{t('order.qty')} </span>
          <span className="font-medium">{Number(order.filledQty || order.quantity).toFixed(6)}</span>
        </div>
        <div className="flex items-center gap-2">
          {isPaper && <span className="text-xs bg-blue-100 text-blue-600 px-1.5 py-0.5 rounded">{t('order.paper')}</span>}
          {!isSuccess && order.errorMessage && (
            <span className="text-xs text-red-500 truncate" title={order.errorMessage}>❌ {order.errorMessage}</span>
          )}
          {isSuccess && <span className="text-xs text-green-600">{t('order.filled')}</span>}
        </div>
      </div>
      <div className="flex-shrink-0 text-xs text-gray-400">
        {order.createdAt ? new Date(order.createdAt).toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit', hour12: false, timeZone: 'UTC' }) : ''}
      </div>
    </div>
  )
}

export function TradingPage() {
  const { t } = useTranslation('trading')
  const { data: config } = useRiskConfig()
  const { data: orders, loading: ordersLoading } = useOrders()

  const symbols = config?.allowedSymbols?.length > 0 ? config.allowedSymbols : DEFAULT_SYMBOLS

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-bold" style={{ color: '#0f172a' }}>{t('title')}</h1>

      {/* Signal Cards */}
      <div>
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
          <div className="space-y-2">
            {orders.map((order, i) => (
              <OrderRow key={order.orderId || i} order={order} />
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
