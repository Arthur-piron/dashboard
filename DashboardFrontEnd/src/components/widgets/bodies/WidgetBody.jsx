import { CurrentWeatherBody, ForecastBody } from './WeatherBodies.jsx'
import { CoinPriceBody, PriceHistoryBody } from './CryptoBodies.jsx'
import { ApodBody, MarsRoverBody } from './NasaBodies.jsx'
import { DuolingoProfileBody, DuolingoCourseBody } from './DuolingoBodies.jsx'
import { GitHubProfileBody, GitHubReposBody, GitHubActivityBody } from './GitHubBodies.jsx'
import './WidgetBody.css'

// rendu dedie par widgetKey
const BODIES = {
  'weather.current': CurrentWeatherBody,
  'weather.forecast': ForecastBody,
  'crypto.price': CoinPriceBody,
  'crypto.history': PriceHistoryBody,
  'nasa.apod': ApodBody,
  'nasa.mars': MarsRoverBody,
  'duolingo.profile': DuolingoProfileBody,
  'duolingo.course': DuolingoCourseBody,
  'github.profile': GitHubProfileBody,
  'github.repos': GitHubReposBody,
  'github.activity': GitHubActivityBody,
}

// heure du dernier fetch, ex 14:05
function formatTime(iso) {
  return new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
}

// affiche l'interieur d'un widget a partir d'un WidgetResult de GET /widget-instances/{id}/data
// result = { instanceId, widgetKey, status: 'pending' | 'ok' | 'error', fetchedAt, nextRefreshAt, data, error }
function WidgetBody({ result }) {
  if (!result || (result.status === 'pending' && result.data == null)) {
    return (
      <div className="widget-body widget-body--pending" aria-busy="true" aria-label="Loading">
        <span className="widget-body__skeleton widget-body__skeleton--big" />
        <span className="widget-body__skeleton" />
        <span className="widget-body__skeleton widget-body__skeleton--short" />
      </div>
    )
  }

  // en erreur le back renvoie les dernieres donnees connues, on les garde affichees
  if (result.data == null) {
    return (
      <p className="widget-body__note widget-body__note--error">
        {result.error ?? 'Something went wrong.'}
      </p>
    )
  }

  const Body = BODIES[result.widgetKey]
  const stale = result.status === 'error'

  return (
    <div className="widget-body">
      {stale && <p className="widget-body__note widget-body__note--error">{result.error}</p>}

      <div className={`widget-body__data${stale ? ' widget-body__data--stale' : ''}`}>
        {Body ? (
          <Body data={result.data} />
        ) : (
          <p className="widget-body__note">No display for this widget yet.</p>
        )}
      </div>

      {result.fetchedAt && (
        <p className="widget-body__footer">
          {stale ? 'Last updated' : 'Updated'} {formatTime(result.fetchedAt)}
        </p>
      )}
    </div>
  )
}

export default WidgetBody
