import { useTranslation } from 'react-i18next'
import { TimezonePanel } from '../components/settings/TimezonePanel.jsx'
import { TelegramPanel } from '../components/settings/TelegramPanel.jsx'
import { ExchangePanel } from '../components/settings/ExchangePanel.jsx'
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

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 items-start">
        <TimezonePanel />
        <TelegramPanel />
        <ExchangePanel />
        <RiskSettingsPanel />
        <OrderAmountLimitsPanel />
        <HouseKeeperPanel />
      </div>
    </div>
  )
}
