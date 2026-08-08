import { render, screen } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { describe, expect, it } from 'vitest'

import { AuthShell } from './AuthShell'

describe('AuthShell', () => {
  it('centres anonymous route content without the workspace navigation', () => {
    const router = createMemoryRouter([
      {
        element: <AuthShell />,
        children: [{ index: true, element: <h1>Sign in content</h1> }],
      },
    ])

    render(<RouterProvider router={router} />)

    expect(screen.getByRole('main')).toContainElement(screen.getByRole('heading', { name: 'Sign in content' }))
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument()
  })
})
