import ChangePill from '../ChangePill.jsx'
import { IconCrypto } from '../../icons/Icons.jsx'
import Stat from './Stat.jsx'
import './CryptoBodies.css'

// formate un prix dans la devise de coingecko (usd, eur, btc...)
function formatMoney(value, currency, options = {}) {
  const digits = Math.abs(value) >= 1 ? 2 : 6
  try {
    return value.toLocaleString('en-US', {
      style: 'currency',
      currency: currency.toUpperCase(),
      maximumFractionDigits: digits,
      ...options,
    })
  } catch {
    // devise inconnue de Intl, on l'affiche en suffixe
    return `${value.toLocaleString('en-US', { maximumFractionDigits: digits })} ${currency.toUpperCase()}`
  }
}

// ex 1.2T, 350B
function formatCompact(value, currency) {
  return formatMoney(value, currency, { notation: 'compact', maximumFractionDigits: 1 })
}

// bitcoin -> Bitcoin
function coinName(coin) {
  return coin.charAt(0).toUpperCase() + coin.slice(1).replace(/-/g, ' ')
}

function Change({ percent, suffix = '' }) {
  if (percent == null) return null
  return (
    <ChangePill positive={percent >= 0}>
      {percent >= 0 ? '+' : ''}
      {percent.toFixed(2)}%{suffix}
    </ChangePill>
  )
}

// what a change in percent was worth, e.g. +5% on 105 is +5
function absoluteChange(price, percent) {
  return price - price / (1 + percent / 100)
}

function signed(value, currency) {
  return `${value >= 0 ? '+' : '−'}${formatMoney(Math.abs(value), currency)}`
}

// data = { coin, currency, price, changePercent24h, marketCap, lastUpdatedAt }
export function CoinPriceBody({ data }) {
  const change = data.changePercent24h == null ? null : absoluteChange(data.price, data.changePercent24h)

  return (
    <div className="coin-now">
      <div className="coin-now__hero">
        <div className="crypto-body__coin">
          <IconCrypto size={18} />
          <p className="crypto-body__pair">
            {coinName(data.coin)} / {data.currency.toUpperCase()}
          </p>
        </div>

        <p className="widget-body__big">{formatMoney(data.price, data.currency)}</p>

        <Change percent={data.changePercent24h} suffix=" 24h" />
      </div>

      {(change != null || data.marketCap != null) && (
        <div className="widget-body__stats widget-body__stats--adaptive coin-now__stats">
          {change != null && <Stat label="24h change">{signed(change, data.currency)}</Stat>}
          {data.marketCap != null && <Stat label="Market cap">{formatCompact(data.marketCap, data.currency)}</Stat>}
        </div>
      )}
    </div>
  )
}

const CHART_WIDTH = 280
const CHART_HEIGHT = 70

// transforme les points en path svg, x = temps, y = prix
function sparklinePath(points, low, high) {
  const first = points[0].timestampMs
  const span = points[points.length - 1].timestampMs - first || 1
  const range = high - low || 1

  return points
    .map((point, index) => {
      const x = ((point.timestampMs - first) / span) * CHART_WIDTH
      const y = CHART_HEIGHT - ((point.price - low) / range) * CHART_HEIGHT
      return `${index === 0 ? 'M' : 'L'}${x.toFixed(1)},${y.toFixed(1)}`
    })
    .join(' ')
}

// data = { coin, currency, days, first, last, low, high, changePercent, points: [{ timestampMs, price }] }
export function PriceHistoryBody({ data }) {
  const positive = data.changePercent >= 0
  const line = data.points.length > 1 ? sparklinePath(data.points, data.low, data.high) : null

  return (
    <div className="coin-history">
      <div className="crypto-body__header coin-history__head">
        <div className="crypto-body__coin">
          <IconCrypto size={18} />
          <p className="crypto-body__pair">
            {coinName(data.coin)} · {data.days}d
          </p>
        </div>
        <Change percent={data.changePercent} />
      </div>

      <p className="crypto-body__price coin-history__price">{formatMoney(data.last, data.currency)}</p>

      {line && (
        <div className="coin-history__chart">
          <svg
            className={`crypto-body__chart ${positive ? 'crypto-body__chart--up' : 'crypto-body__chart--down'}`}
            viewBox={`0 -4 ${CHART_WIDTH} ${CHART_HEIGHT + 8}`}
            preserveAspectRatio="none"
            aria-hidden="true"
          >
            <path
              d={`${line} L${CHART_WIDTH},${CHART_HEIGHT + 4} L0,${CHART_HEIGHT + 4} Z`}
              className="crypto-body__chart-area"
            />
            <path d={line} className="crypto-body__chart-line" vectorEffect="non-scaling-stroke" />
          </svg>
          {/* scale and time axis, shown once the chart is big enough to be read */}
          <span className="coin-history__axis coin-history__axis--high">{formatMoney(data.high, data.currency)}</span>
          <span className="coin-history__axis coin-history__axis--low">{formatMoney(data.low, data.currency)}</span>
          <span className="coin-history__axis coin-history__axis--start">{data.days}d ago</span>
          <span className="coin-history__axis coin-history__axis--end">now</span>
        </div>
      )}

      <div className="widget-body__stats widget-body__stats--adaptive coin-history__stats">
        <Stat label="Open">{formatMoney(data.first, data.currency)}</Stat>
        <Stat label="Low">{formatMoney(data.low, data.currency)}</Stat>
        <Stat label="High">{formatMoney(data.high, data.currency)}</Stat>
      </div>
    </div>
  )
}
