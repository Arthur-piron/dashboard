import { Component } from 'react'
import './ErrorBoundary.css'

// keeps one bad payload from white-screening the app
class ErrorBoundary extends Component {
  state = { error: null }

  static getDerivedStateFromError(error) {
    return { error }
  }

  componentDidCatch(error) {
    console.error(error)
  }

  render() {
    if (!this.state.error) return this.props.children
    if (this.props.fallback) return this.props.fallback

    return (
      <div className="error-boundary" role="alert">
        <p className="error-boundary__title">Something went wrong</p>
        <p className="error-boundary__text">This part of the page could not be displayed.</p>
        <button type="button" className="button" onClick={() => this.setState({ error: null })}>
          Try again
        </button>
      </div>
    )
  }
}

export default ErrorBoundary
