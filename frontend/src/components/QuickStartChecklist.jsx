import { useTranslation } from 'react-i18next'

const ITEM_KEYS = [
  { key: 'readFlow',      action: null          },
  { key: 'checkRules',    action: 'safety'      },
  { key: 'reviewSignals', action: 'trading'     },
  { key: 'browseHistory', action: 'events'      },
  { key: 'viewOverview',  action: 'overview'    },
  { key: 'contactOncall', action: null          },
]

const ICONS = {
  readFlow:      '⏱️',
  checkRules:    '🛡️',
  reviewSignals: '💹',
  browseHistory: '📋',
  viewOverview:  '📊',
  contactOncall: '🆘',
}

export function QuickStartChecklist({ onNavigate }) {
  const { t } = useTranslation('guidance')

  return (
    <div className="rounded-xl p-5" style={{ background: '#ffffff', border: '1px solid #dbe4ef', boxShadow: '0 8px 30px rgba(15,23,42,0.08)' }}>
      <h2 className="text-lg font-semibold mb-1" style={{ color: '#0f172a' }}>{t('checklist.title')}</h2>
      <p className="text-sm text-gray-500 mb-4">{t('checklist.subtitle')}</p>

      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
        {ITEM_KEYS.map(({ key, action }) => (
          <div
            key={key}
            className="rounded-xl p-4 flex flex-col gap-2"
            style={{ background: '#f8fafc', border: '1px solid #e2e8f0' }}
          >
            <div className="flex items-start gap-2">
              <span className="text-xl shrink-0 mt-0.5">{ICONS[key]}</span>
              <span className="text-sm font-semibold text-gray-800 leading-tight">
                {t(`checklist.items.${key}.title`)}
              </span>
            </div>
            <p className="text-xs text-gray-500 leading-snug flex-1">
              {t(`checklist.items.${key}.desc`)}
            </p>
            {action && (
              <button
                onClick={() => onNavigate(action)}
                className="mt-1 text-xs font-medium px-3 py-1.5 rounded-lg self-start transition-opacity hover:opacity-80"
                style={{ background: '#2f6fed', color: '#ffffff' }}
              >
                {t(`checklist.items.${key}.action`)} →
              </button>
            )}
          </div>
        ))}
      </div>
    </div>
  )
}
