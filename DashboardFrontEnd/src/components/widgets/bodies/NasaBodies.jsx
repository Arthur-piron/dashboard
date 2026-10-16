import { useState } from 'react'
import './NasaBodies.css'

// data = { date, title, explanation, mediaType, url, hdUrl, copyright }
export function ApodBody({ data }) {
  const [expanded, setExpanded] = useState(false)

  return (
    <div className={`apod${expanded ? ' apod--expanded' : ''}`}>
      {data.mediaType === 'image' ? (
        <a className="nasa-body__media apod__media" href={data.hdUrl ?? data.url} target="_blank" rel="noreferrer">
          <img src={data.url} alt={data.title} loading="lazy" />
        </a>
      ) : (
        // apod peut etre une video youtube / vimeo, url est alors une url d'embed
        <div className="nasa-body__media apod__media">
          <iframe src={data.url} title={data.title} allowFullScreen />
        </div>
      )}

      <div className="apod__text">
        <div>
          <p className="nasa-body__title">{data.title}</p>
          <p className="widget-body__subtitle">
            {data.date}
            {data.copyright ? ` · © ${data.copyright.trim()}` : ''}
          </p>
        </div>

        <p className="nasa-body__text apod__explanation">{data.explanation}</p>
        <button type="button" className="nasa-body__more apod__more" onClick={() => setExpanded((value) => !value)}>
          {expanded ? 'Show less' : 'Read more'}
        </button>
      </div>
    </div>
  )
}

// column counts for each card shape, css picks one: a strip, two rows, three rows, two columns
function photoColumns(count) {
  return {
    '--cols-strip': count,
    '--cols-block': Math.ceil(count / 2),
    '--cols-square': Math.ceil(count / 3),
    '--cols-column': Math.min(2, count),
  }
}

// data = { rover, status, earthDate, sol, photos: [{ id, camera, imageUrl }] }
export function MarsRoverBody({ data }) {
  return (
    <div className="mars">
      <div className="nasa-body__rover">
        <p className="nasa-body__title">{data.rover}</p>
        <span className={`nasa-body__status nasa-body__status--${data.status}`}>{data.status}</span>
      </div>
      <p className="widget-body__subtitle">
        Sol {data.sol} · {data.earthDate}
      </p>

      {data.photos.length === 0 ? (
        <p className="widget-body__note">No photos for this day.</p>
      ) : (
        <div className="nasa-body__photos" style={photoColumns(data.photos.length)}>
          {data.photos.map((photo) => (
            <a className="nasa-body__photo" key={photo.id} href={photo.imageUrl} target="_blank" rel="noreferrer">
              <img src={photo.imageUrl} alt={`${data.rover} - ${photo.camera}`} loading="lazy" />
              <span className="nasa-body__camera">{photo.camera}</span>
            </a>
          ))}
        </div>
      )}
    </div>
  )
}
