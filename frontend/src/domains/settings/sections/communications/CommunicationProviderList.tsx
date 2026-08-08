import { useState } from 'react'

import type { CommunicationAction } from '../../events/settings-events'
import type { CommunicationProvider } from '../../model/communications'

interface CommunicationProviderListProps {
  busy: boolean
  providers: CommunicationProvider[]
  onAction: (action: CommunicationAction) => Promise<void>
  onEdit: (provider: CommunicationProvider) => Promise<void>
}

export function CommunicationProviderList({
  busy,
  providers,
  onAction,
  onEdit,
}: CommunicationProviderListProps) {
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null)
  const [testProviderId, setTestProviderId] = useState<string | null>(null)
  const [testRecipient, setTestRecipient] = useState('')

  async function toggleTest(providerId: string): Promise<void> {
    await Promise.resolve()
    setConfirmDeleteId(null)
    setTestProviderId((current) => (current === providerId ? null : providerId))
    setTestRecipient('')
  }

  if (providers.length === 0)
    return <div className="empty-note">No communication providers configured.</div>

  return (
    <>
      {providers.map((provider) => (
        <div key={provider.id}>
          <article className={`provider-row${provider.enabled ? '' : ' disabled'}`}>
            <div className="provider-info">
              <strong>{provider.name}</strong>
              <span className="channel-badge">{provider.channel === 'sms' ? 'SMS' : 'Email'}</span>
              <span className="kind-badge">
                {provider.kind === 'sendgrid'
                  ? 'SendGrid'
                  : provider.kind === 'twilio'
                    ? 'Twilio'
                    : 'Postmark'}
              </span>
              {provider.isDefault ? <span className="flag-badge">Default</span> : null}
              {provider.useForTwoFactor ? <span className="flag-badge">2FA</span> : null}
              <label>
                <input
                  checked={provider.enabled}
                  disabled={busy}
                  onChange={(event) =>
                    void onAction({
                      kind: 'toggle-enabled',
                      providerId: provider.id,
                      enabled: event.target.checked,
                    })
                  }
                  type="checkbox"
                />{' '}
                Enabled
              </label>
            </div>
            <div className="provider-actions">
              {provider.isDefault ? null : (
                <button
                  className="dcbtn"
                  disabled={busy}
                  onClick={() => void onAction({ kind: 'default', providerId: provider.id })}
                  type="button"
                >
                  Make default
                </button>
              )}
              <button
                className="dcbtn"
                disabled={busy}
                onClick={() =>
                  void onAction({
                    kind: 'two-factor',
                    providerId: provider.id,
                    enabled: !provider.useForTwoFactor,
                  })
                }
                type="button"
              >
                {provider.useForTwoFactor ? 'Remove 2FA' : 'Use for 2FA'}
              </button>
              <button className="dcbtn" onClick={() => void toggleTest(provider.id)} type="button">
                Test
              </button>
              <button className="dcbtn" onClick={() => void onEdit(provider)} type="button">
                Edit
              </button>
              {confirmDeleteId === provider.id ? (
                <>
                  <button
                    className="dcbtn danger"
                    disabled={busy}
                    onClick={() => void onAction({ kind: 'delete', providerId: provider.id })}
                    type="button"
                  >
                    Confirm
                  </button>
                  <button
                    className="dcbtn"
                    onClick={() => {
                      setConfirmDeleteId(null)
                    }}
                    type="button"
                  >
                    Keep
                  </button>
                </>
              ) : (
                <button
                  className="dcbtn danger"
                  onClick={() => {
                    setConfirmDeleteId(provider.id)
                  }}
                  type="button"
                >
                  Delete
                </button>
              )}
            </div>
          </article>
          {testProviderId === provider.id ? (
            <div className="provider-test-row">
              <label className="dcfield">
                <span>Test recipient</span>
                <input
                  onChange={(event) => {
                    setTestRecipient(event.target.value)
                  }}
                  placeholder={
                    provider.channel === 'email' ? 'recipient@example.com' : '+15551234567'
                  }
                  value={testRecipient}
                />
              </label>
              <button
                className="dcbtn primary"
                disabled={busy || testRecipient.trim() === ''}
                onClick={() =>
                  void onAction({
                    kind: 'test',
                    providerId: provider.id,
                    recipient: testRecipient.trim(),
                  })
                }
                type="button"
              >
                Send test
              </button>
              <button
                className="dcbtn"
                onClick={() => {
                  setTestProviderId(null)
                }}
                type="button"
              >
                Cancel
              </button>
            </div>
          ) : null}
        </div>
      ))}
    </>
  )
}
