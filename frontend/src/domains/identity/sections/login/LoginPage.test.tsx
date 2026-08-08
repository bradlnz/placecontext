import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'

import { identityContextQueryKey } from '../../api/identity-query-options'
import { LoginPage } from './LoginPage'

describe('LoginPage', () => {
  it('replicates the Host login form with antiforgery and redirect state', () => {
    const queryClient = new QueryClient()
    queryClient.setQueryData(identityContextQueryKey, {
      configured: true,
      antiforgeryFieldName: '__RequestVerificationToken',
      antiforgeryToken: 'secure-token',
    })

    const { container } = render(
      <MemoryRouter
        initialEntries={[
          '/login?error=Invalid%20credentials&returnUrl=%2Fcluster&email=ada%40example.com',
        ]}
      >
        <QueryClientProvider client={queryClient}>
          <LoginPage />
        </QueryClientProvider>
      </MemoryRouter>,
    )

    expect(screen.getByRole('heading', { name: 'Sign in' })).toBeVisible()
    expect(screen.getByRole('alert')).toHaveTextContent('Invalid credentials')
    expect(screen.getByRole('textbox', { name: 'Email' })).toHaveValue('ada@example.com')
    const form = container.querySelector('form')
    expect(form).toHaveAttribute('action', '/auth/login')
    expect(container.querySelector('input[name="__RequestVerificationToken"]')).toHaveValue(
      'secure-token',
    )
    expect(container.querySelector('input[name="returnUrl"]')).toHaveValue('/cluster')
  })
})
