import { isRouteErrorResponse, useRouteError } from 'react-router-dom'

import { HttpError } from '../../api/http-client'

function errorMessage(error: unknown): string {
  if (error instanceof HttpError) {
    return error.message
  }

  if (isRouteErrorResponse(error)) {
    return `${String(error.status)} ${error.statusText}`
  }

  if (error instanceof Error) {
    return error.message
  }

  return 'The page could not be loaded.'
}

export function RouteErrorPage() {
  const error = useRouteError()
  const sessionExpired = error instanceof HttpError && error.status === 401

  return (
    <main className="route-error">
      <div className="route-error__card">
        <p className="eyebrow">PlaceContext</p>
        <h1>Something interrupted the workspace.</h1>
        <p>{errorMessage(error)}</p>
        <a className="button button--primary" href={sessionExpired ? '/locked' : '/app/'}>
          {sessionExpired ? 'Sign in again' : 'Try again'}
        </a>
      </div>
    </main>
  )
}
