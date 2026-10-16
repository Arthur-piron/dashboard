import { WeatherGlyph } from '../../icons/Icons.jsx'
import Stat from './Stat.jsx'
import './WeatherBodies.css'

// symbole de l'unite renvoyee par le back (celsius ou fahrenheit)
function unitSymbol(unit) {
  return unit === 'fahrenheit' ? '°F' : '°C'
}

// jour court a partir d'une date YYYY-MM-DD, ex Mon
function dayLabel(date, index) {
  if (index === 0) return 'Today'
  return new Date(`${date}T00:00:00`).toLocaleDateString('en-US', { weekday: 'short' })
}

// data = { city, country, temperature, unit, humidity, windSpeedKmh, weatherCode, sky, observedAt }
export function CurrentWeatherBody({ data }) {
  return (
    <div className="weather-now">
      <div className="weather-now__hero">
        <p className="widget-body__subtitle">
          {data.city}
          {data.country ? `, ${data.country}` : ''}
        </p>
        <p className="widget-body__big">
          {Math.round(data.temperature)}
          {unitSymbol(data.unit)}
        </p>
        <p className="weather-body__sky">{data.sky}</p>
      </div>

      {/* sized in css, it grows with the layout up to a cap */}
      <span className="weather-now__glyph">
        <WeatherGlyph size="1em" code={data.weatherCode} />
      </span>

      <div className="widget-body__stats widget-body__stats--adaptive weather-now__stats">
        <Stat label="Humidity">{data.humidity}%</Stat>
        <Stat label="Wind">{Math.round(data.windSpeedKmh)} km/h</Stat>
      </div>
    </div>
  )
}

// data = { city, country, unit, days: [{ date, min, max, precipitationMm, weatherCode, sky }] }
export function ForecastBody({ data }) {
  const symbol = unitSymbol(data.unit)
  // the range bars share one scale, like the ios weather app
  const low = Math.min(...data.days.map((day) => day.min))
  const span = Math.max(...data.days.map((day) => day.max)) - low || 1

  return (
    <>
      <p className="widget-body__subtitle">
        {data.city}
        {data.country ? `, ${data.country}` : ''}
      </p>

      <ul className="forecast">
        {data.days.map((day, index) => (
          <li className="forecast__day" key={day.date}>
            <span className="forecast__name">{dayLabel(day.date, index)}</span>
            <span className="forecast__glyph">
              <WeatherGlyph size="1em" code={day.weatherCode} />
            </span>
            <span className="forecast__sky" title={day.sky}>
              {day.sky}
            </span>
            <span className="forecast__rain">{day.precipitationMm > 0 ? `${day.precipitationMm} mm` : ''}</span>
            <span className="forecast__min">
              {Math.round(day.min)}
              {symbol}
            </span>
            <span className="forecast__range" aria-hidden="true">
              <span
                className="forecast__range-fill"
                style={{
                  left: `${((day.min - low) / span) * 100}%`,
                  right: `${100 - ((day.max - low) / span) * 100}%`,
                }}
              />
            </span>
            <span className="forecast__max">
              {Math.round(day.max)}
              {symbol}
            </span>
          </li>
        ))}
      </ul>
    </>
  )
}
