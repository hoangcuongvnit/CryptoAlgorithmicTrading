import { useTranslation } from 'react-i18next'
import { TimezonePanel } from '../components/settings/TimezonePanel.jsx'
import { TelegramPanel } from '../components/settings/TelegramPanel.jsx'
import { ExchangePanel } from '../components/settings/ExchangePanel.jsx'
import { TradingModePanel } from '../components/settings/TradingModePanel.jsx'
import { RiskSettingsPanel } from '../components/settings/RiskSettingsPanel.jsx'
import { OrderAmountLimitsPanel } from '../components/settings/OrderAmountLimitsPanel.jsx'
import { HouseKeeperPanel } from '../components/settings/HouseKeeperPanel.jsx'

export function SettingsPage() {
  const { t } = useTranslation('settings')

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold" style={{ color: '#0f172a' }}>{t('title')}</h1>
        <p className="text-sm text-gray-500 mt-1">{t('subtitle')}</p>
      </div>

      {/* Row 1: Timezone + Telegram */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 items-start">
        <TimezonePanel />
        <TelegramPanel />
      </div>

      {/* Row 2: Exchange Configuration (full width) */}
      <ExchangePanel />

      {/* Row 3: Trading Mode + Risk Management */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 items-start">
        <TradingModePanel />
        <RiskSettingsPanel />
      </div>

      {/* Row 4: Order Amount Limits (full width) */}
      <OrderAmountLimitsPanel />

      {/* Row 5: HouseKeeper (full width) */}
      <HouseKeeperPanel />
    </div>
  )
}
