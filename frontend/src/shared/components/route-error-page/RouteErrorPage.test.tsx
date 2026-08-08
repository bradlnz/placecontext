import { render, screen } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { describe, expect, it } from 'vitest'

import { HttpError } from '../../api/http-client'
import { RouteErrorPage } from './RouteErrorPage'

function renderRouteError(error: Error) {
  const router = createMemoryRouter(
    [
      {
        path: '/',
        loader: () => {
          throw error
        },
        errorElement: <RouteErrorPage />,
      },
    ],
    { initialEntries: ['/'] },
  )

  return render(<RouterProvider router={router} />)
}

describe('RouteErrorPage', () => {
  it('directs an expired session to the locked page', async () => {
    renderRouteError(new HttpError(401, 'Your PlaceContext session has expired.'))

    expect(await screen.findByText('Your PlaceContext session has expired.')).toBeVisible()
    expect(screen.getByRole('link', { name: 'Sign in again' })).toHaveAttribute('href', '/locked')
  })

  it('offers a retry for a general route error', async () => {
    renderRouteError(new Error('Network unavailable'))

    expect(await screen.findByText('Network unavailable')).toBeVisible()
    expect(screen.getByRole('link', { name: 'Try again' })).toHaveAttribute('href', '/app/')
  })
})
