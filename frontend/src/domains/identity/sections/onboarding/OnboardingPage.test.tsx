import { render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

const navigation = vi.hoisted(() => ({ navigateToLegacyPath: vi.fn(() => Promise.resolve()) }))

vi.mock('../../../../shared/navigation/legacy-navigation', () => navigation)

import { OnboardingPage } from './OnboardingPage'

describe('OnboardingPage', () => {
  it('preserves the Host redirect to the getting-started guide', async () => {
    render(<OnboardingPage />)

    expect(screen.getByRole('status')).toHaveTextContent('Opening the getting started guide')
    await waitFor(() => {
      expect(navigation.navigateToLegacyPath).toHaveBeenCalledWith('/wiki/getting-started')
    })
  })
})
