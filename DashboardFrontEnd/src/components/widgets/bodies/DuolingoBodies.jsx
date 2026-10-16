import { IconFlame, IconCrown } from '../../icons/Icons.jsx'
import Stat from './Stat.jsx'
import './DuolingoBodies.css'

// code langue -> nom lisible, ex es -> Spanish
function languageName(code) {
  if (!code) return '—'
  try {
    return new Intl.DisplayNames(['en'], { type: 'language' }).of(code) ?? code
  } catch {
    return code
  }
}

function formatNumber(value) {
  return value.toLocaleString('en-US')
}

// data = { username, name, streak, totalXp, learningLanguage, fromLanguage, courseCount }
export function DuolingoProfileBody({ data }) {
  return (
    <div className="duo">
      <div className="duo__hero">
        <p className="widget-body__subtitle">
          {data.name ? `${data.name} · ` : ''}@{data.username}
        </p>

        <div className="duolingo-body__streak">
          <span className="duolingo-body__flame">
            <IconFlame size="1em" />
          </span>
          <p className="widget-body__big">{formatNumber(data.streak)}</p>
          <p className="duolingo-body__unit">day streak</p>
        </div>
      </div>

      <div className="widget-body__stats widget-body__stats--adaptive duo__stats">
        <Stat label="Total XP">{formatNumber(data.totalXp)}</Stat>
        <Stat label="Learning">{languageName(data.learningLanguage)}</Stat>
        <Stat label="Courses">{data.courseCount}</Stat>
      </div>
    </div>
  )
}

// data = { username, title, learningLanguage, fromLanguage, xp, crowns, isCurrentCourse }
export function DuolingoCourseBody({ data }) {
  return (
    <div className="duo">
      <div className="duo__hero">
        <div className="duolingo-body__course">
          <p className="duolingo-body__title">{data.title}</p>
          {data.isCurrentCourse && <span className="duolingo-body__badge">Current</span>}
        </div>
        <p className="widget-body__subtitle">
          @{data.username} · from {languageName(data.fromLanguage)}
        </p>
      </div>

      <div className="widget-body__stats widget-body__stats--adaptive duo__stats">
        <Stat label="XP">{formatNumber(data.xp)}</Stat>
        <Stat label="Crowns">
          <span className="duolingo-body__crowns">
            <IconCrown size={15} />
            {formatNumber(data.crowns)}
          </span>
        </Stat>
      </div>
    </div>
  )
}
