import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import type { CommunicationProvider } from '../../model/communications'
import { CommunicationProviderList } from './CommunicationProviderList'

export const providerFixture: CommunicationProvider = { id: '158fdb23-5c46-4777-b0bb-d78ff91b8754', channel: 'email', kind: 'postmark', name: 'Transactional email', enabled: true, isDefault: true, useForTwoFactor: true, authType: 'header', authHeaderName: 'X-Postmark-Server-Token', vaultProjectId: null, apiKeySecretName: null, settingsJson: '{}', createdAt: '2026-08-08T00:00:00Z', updatedAt: '2026-08-08T00:00:00Z' }

describe('CommunicationProviderList', () => {
  it('renders provider flags and asynchronously opens test delivery controls', async () => {
    const user = userEvent.setup()
    render(<CommunicationProviderList busy={false} onAction={vi.fn()} onEdit={vi.fn()} providers={[providerFixture]} />)
    expect(screen.getByText('Default')).toBeVisible(); expect(screen.getByText('2FA')).toBeVisible()
    await user.click(screen.getByRole('button', { name: 'Test' }))
    expect(screen.getByLabelText('Test recipient')).toHaveAttribute('placeholder', 'recipient@example.com')
    expect(screen.getByRole('button', { name: 'Send test' })).toBeDisabled()
  })
})
