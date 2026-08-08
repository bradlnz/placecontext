import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { describe, expect, it } from 'vitest'

import { SettingsLayout } from './SettingsLayout'

describe('SettingsLayout', () => {
  it('renders every Host settings section and toggles mobile navigation asynchronously', async () => {
    const user = userEvent.setup()
    const router = createMemoryRouter([
      {
        element: <SettingsLayout />,
        children: [{ index: true, element: <p>Settings content</p> }],
      },
    ])
    render(<RouterProvider router={router} />)

    expect(screen.getByRole('navigation', { name: 'Settings sections' })).toBeVisible()
    expect(screen.getByRole('link', { name: 'Backup' })).toHaveAttribute('href', '/settings/backup')
    expect(screen.getByRole('link', { name: 'Communications' })).toHaveAttribute('href', '/settings/communications')

    const toggle = screen.getByRole('button', { name: /settings sections/i })
    await user.click(toggle)
    expect(toggle).toHaveAttribute('aria-expanded', 'true')
  })
})
