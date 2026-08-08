import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'

import { identityContextQueryKey } from '../../api/identity-query-options'
import { SetupPage } from './SetupPage'

function renderSetup(configured: boolean, entry = '/setup') {
  const queryClient = new QueryClient()
  queryClient.setQueryData(identityContextQueryKey, {
    configured,
    antiforgeryFieldName: '__RequestVerificationToken',
    antiforgeryToken: 'secure-token',
  })

  return render(
    <MemoryRouter initialEntries={[entry]}>
      <QueryClientProvider client={queryClient}>
        <SetupPage />
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

describe('SetupPage', () => {
  it('renders the first-admin form and native PlaceContext action', () => {
    const { container } = renderSetup(false, '/setup?error=Try%20again')

    expect(screen.getByRole('heading', { name: 'Set up this workspace' })).toBeVisible()
    expect(screen.getByRole('alert')).toHaveTextContent('Try again')
    expect(screen.getByLabelText(/admin email/i)).toBeRequired()
    expect(screen.getByLabelText(/^password/i)).toHaveAttribute('minlength', '12')
    expect(container.querySelector('form')).toHaveAttribute('action', '/auth/setup')
  })

  it('fails closed to sign-in when the workspace is configured', () => {
    renderSetup(true)

    expect(screen.getByRole('heading', { name: 'Already set up' })).toBeVisible()
    expect(screen.getByRole('link', { name: 'Go to sign in' })).toHaveAttribute('href', '/login')
  })
})
