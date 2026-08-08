import { useState } from 'react'

interface CustomerPortalAccountsProps {
  busy: boolean
  enabled: boolean
  message: string | null
  onInvite: (email: string, role: string) => Promise<boolean>
  onToggle: (enabled: boolean) => Promise<void>
}

export function CustomerPortalAccounts({
  busy,
  enabled,
  message,
  onInvite,
  onToggle,
}: CustomerPortalAccountsProps) {
  const [email, setEmail] = useState('')
  const [role, setRole] = useState('member')

  async function invite(): Promise<void> {
    if (await onInvite(email.trim(), role)) setEmail('')
  }

  return (
    <section className="dccard access-card">
      <h2>Customer portal accounts</h2>
      <p>
        Allow this customer tenant to use its isolated Rails portal. Portal users are separate from
        Placecontext workspace members.
      </p>
      <label className="access-toggle">
        <input
          checked={enabled}
          disabled={busy}
          onChange={(event) => void onToggle(event.target.checked)}
          type="checkbox"
        />
        <span>Enable customer portal accounts</span>
      </label>
      {message === null ? null : (
        <div className="settings-hint" role="status">
          {message}
        </div>
      )}
      {enabled ? (
        <div className="access-invite-row">
          <label className="dcfield">
            <span>Customer email</span>
            <input
              onChange={(event) => {
                setEmail(event.target.value)
              }}
              placeholder="customer@example.com"
              type="email"
              value={email}
            />
          </label>
          <label className="dcfield">
            <span>Portal role</span>
            <select
              onChange={(event) => {
                setRole(event.target.value)
              }}
              value={role}
            >
              <option value="member">Member</option>
              <option value="manager">Manager</option>
              <option value="admin">Admin</option>
            </select>
          </label>
          <button
            className="dcbtn primary"
            disabled={busy}
            onClick={() => void invite()}
            type="button"
          >
            {busy ? 'Inviting…' : 'Invite portal user'}
          </button>
        </div>
      ) : null}
    </section>
  )
}
