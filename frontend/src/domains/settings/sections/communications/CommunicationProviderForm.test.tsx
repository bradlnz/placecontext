import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import type { CommunicationProviderDraft } from '../../model/communications'
import { CommunicationProviderForm } from './CommunicationProviderForm'

const draft: CommunicationProviderDraft = { channel: 'email', kind: 'postmark', name: 'Mail', enabled: true, authType: 'header', authHeaderName: 'X-Key', vaultProjectId: 'a102ed75-e94a-48fe-9826-2532d524857f', apiKeySecretName: 'POSTMARK_TOKEN', settingsJson: '{}', fromEmail: 'hello@example.com', fromName: 'Example', messageStream: 'outbound', accountSid: '', fromNumber: '', endpoint: '' }

describe('CommunicationProviderForm', () => {
  it('renders provider, Vault, and Postmark-specific controls', () => {
    render(<CommunicationProviderForm busy={false} draft={draft} editing onCancel={vi.fn()} onChange={vi.fn()} onProjectChange={vi.fn()} onSave={vi.fn()} projects={[{ id: 'a102ed75-e94a-48fe-9826-2532d524857f', name: 'Atlas' }]} secrets={[{ name: 'POSTMARK_TOKEN', createdAt: '2026-08-08T00:00:00Z' }]} />)
    expect(screen.getByRole('heading', { name: 'Edit Mail' })).toBeVisible()
    expect(screen.getByLabelText('API key secret')).toHaveValue('POSTMARK_TOKEN')
    expect(screen.getByLabelText('Message stream')).toHaveValue('outbound')
    expect(screen.getByRole('button', { name: 'Save changes' })).toBeEnabled()
  })
})
