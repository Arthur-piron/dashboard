import { IconBuilding, IconFork, IconPin, IconStar } from '../../icons/Icons.jsx'
import Stat from './Stat.jsx'
import './GitHubBodies.css'

// github's own colours for the most common languages, the rest fall back to grey
const LANGUAGE_COLORS = {
  JavaScript: '#f1e05a',
  TypeScript: '#3178c6',
  Python: '#3572a5',
  Java: '#b07219',
  'C#': '#178600',
  'C++': '#f34b7d',
  C: '#555555',
  Go: '#00add8',
  Rust: '#dea584',
  Ruby: '#701516',
  PHP: '#4f5d95',
  Swift: '#f05138',
  Kotlin: '#a97bff',
  Dart: '#00b4ab',
  Shell: '#89e051',
  HTML: '#e34c26',
  CSS: '#663399',
  Vue: '#41b883',
  Haskell: '#5e5086',
  Lua: '#000080',
}

function formatNumber(value) {
  return value.toLocaleString('en-US', { notation: value >= 10000 ? 'compact' : 'standard' })
}

// e.g. 5m, 3h, 2d, then a plain date past a month
function timeAgo(iso) {
  if (!iso) return null
  const seconds = Math.max(0, (Date.now() - new Date(iso).getTime()) / 1000)
  if (seconds < 60) return 'now'
  if (seconds < 3600) return `${Math.floor(seconds / 60)}m`
  if (seconds < 86400) return `${Math.floor(seconds / 3600)}h`
  if (seconds < 86400 * 30) return `${Math.floor(seconds / 86400)}d`
  return new Date(iso).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
}

// data = { login, name, avatarUrl, htmlUrl, publicRepos, publicGists, followers, following, detail,
//          bio?, company?, location?, createdAt? }
export function GitHubProfileBody({ data }) {
  const full = data.detail === 'full'
  const meta = full
    ? [
        data.company && { icon: <IconBuilding />, text: data.company },
        data.location && { icon: <IconPin />, text: data.location },
      ].filter(Boolean)
    : []

  return (
    <div className="gh-profile">
      <div className="gh-profile__about">
        <a className="github-body__identity" href={data.htmlUrl} target="_blank" rel="noreferrer">
          {/* sized in css, it grows with the card up to a cap */}
          <span className="github-body__avatar">
            {data.avatarUrl ? <img src={data.avatarUrl} alt="" loading="lazy" /> : data.login.charAt(0).toUpperCase()}
          </span>
          <span className="github-body__names">
            <span className="github-body__name">{data.name || data.login}</span>
            <span className="widget-body__subtitle">@{data.login}</span>
          </span>
        </a>

        {full && data.bio && <p className="github-body__bio">{data.bio}</p>}

        {meta.length > 0 && (
          <ul className="github-body__meta">
            {meta.map((item) => (
              <li key={item.text}>
                {item.icon}
                <span>{item.text}</span>
              </li>
            ))}
          </ul>
        )}

        {full && data.createdAt && (
          <p className="widget-body__subtitle github-body__joined">
            Joined {new Date(data.createdAt).toLocaleDateString('en-US', { month: 'long', year: 'numeric' })}
          </p>
        )}
      </div>

      <div className="widget-body__stats widget-body__stats--adaptive gh-profile__stats">
        <Stat label="Repos">{formatNumber(data.publicRepos)}</Stat>
        <Stat label="Followers">{formatNumber(data.followers)}</Stat>
        <Stat label="Following">{formatNumber(data.following)}</Stat>
        <Stat label="Gists">{formatNumber(data.publicGists)}</Stat>
      </div>
    </div>
  )
}

const SORT_LABELS = {
  pushed: 'recently pushed',
  updated: 'recently updated',
  created: 'newest',
  full_name: 'by name',
}

// data = { login, sort, count, repos: [{ name, fullName, description, language, stars, forks, htmlUrl, pushedAt, isFork }] }
export function GitHubReposBody({ data }) {
  if (data.repos.length === 0) {
    return <p className="widget-body__note">@{data.login} has no public repositories yet.</p>
  }

  return (
    <>
      <p className="widget-body__subtitle">
        @{data.login} · {SORT_LABELS[data.sort] ?? data.sort}
      </p>

      <ul className="github-body__list">
        {data.repos.map((repo) => (
          <li key={repo.fullName}>
            <a className="github-body__repo" href={repo.htmlUrl} target="_blank" rel="noreferrer">
              <span className="github-body__repo-head">
                <span className="github-body__repo-name">{repo.name}</span>
                {repo.isFork && <span className="github-body__tag">Fork</span>}
              </span>
              {repo.description && <span className="github-body__repo-description">{repo.description}</span>}
              <span className="github-body__repo-meta">
                {repo.language && (
                  <span className="github-body__language">
                    <span
                      className="github-body__language-dot"
                      style={{ background: LANGUAGE_COLORS[repo.language] ?? 'var(--color-avatar)' }}
                    />
                    {repo.language}
                  </span>
                )}
                <span className="github-body__count" aria-label={`${repo.stars} stars`}>
                  <IconStar />
                  {formatNumber(repo.stars)}
                </span>
                <span className="github-body__count" aria-label={`${repo.forks} forks`}>
                  <IconFork />
                  {formatNumber(repo.forks)}
                </span>
                {repo.pushedAt && <span className="github-body__when">{timeAgo(repo.pushedAt)}</span>}
              </span>
            </a>
          </li>
        ))}
      </ul>
    </>
  )
}

// e.g. 2026-10-08 -> T
function weekday(date) {
  return new Date(`${date}T00:00:00`).toLocaleDateString('en-US', { weekday: 'narrow' })
}

// owner/repo -> repo, the owner is almost always the linked account
function shortRepo(fullName) {
  return fullName.split('/').pop()
}

// data = { login, days, commits, perDay: [{ date, commits }], events: [{ id, type, repo, summary, commits, createdAt }] }
export function GitHubActivityBody({ data }) {
  const peak = Math.max(1, ...data.perDay.map((day) => day.commits))
  // labels stay readable up to two weeks, past that the bars are too thin for them
  const labelled = data.perDay.length <= 14

  return (
    <div className="gh-activity">
      <div className="github-body__total gh-activity__total">
        <p className="widget-body__big">{formatNumber(data.commits)}</p>
        <p className="github-body__unit">
          {data.commits === 1 ? 'commit' : 'commits'} · {data.days === 1 ? 'today' : `last ${data.days} days`}
        </p>
      </div>

      <div
        className="github-body__chart gh-activity__chart"
        role="img"
        aria-label={`Commits per day over the last ${data.days} days`}
      >
        {data.perDay.map((day) => (
          <div key={day.date} className="github-body__bar-slot" title={`${day.date}: ${day.commits} commits`}>
            <span className="github-body__bar-count">{day.commits > 0 ? day.commits : ''}</span>
            {/* the bar's height is a share of this track, labels around it keep their own room */}
            <span className="github-body__bar-track">
              <span
                className={`github-body__bar${day.commits === 0 ? ' github-body__bar--empty' : ''}`}
                style={{ height: `${Math.max(4, (day.commits / peak) * 100)}%` }}
              />
            </span>
            {labelled && <span className="github-body__bar-label">{weekday(day.date)}</span>}
          </div>
        ))}
      </div>

      {data.events.length === 0 ? (
        <p className="widget-body__note gh-activity__events">No public activity over this period.</p>
      ) : (
        <ul className="github-body__events gh-activity__events">
          {data.events.map((event) => (
            <li key={event.id} className="github-body__event">
              <span className="github-body__event-dot" />
              <span className="github-body__event-text">
                <span className="github-body__event-repo">{shortRepo(event.repo)}</span> {event.summary}
              </span>
              <span className="github-body__when">{timeAgo(event.createdAt)}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
