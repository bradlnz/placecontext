import { useQueryClient, useSuspenseQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'

import { useAppEventBus } from '../../../../app/app-event-bus'
import { communicationsQueryOptions } from '../../api/communications-query'
import { createCommunicationProvider, deleteCommunicationProvider, fetchCommunicationSecrets, sendCommunicationProviderTest, setCommunicationProviderTwoFactor, setDefaultCommunicationProvider, updateCommunicationProvider } from '../../api/communications-api'
import type { CommunicationProvider, CommunicationProviderDraft, CommunicationProviderInput, CommunicationSecret } from '../../model/communications'
import { CommunicationProviderForm } from './CommunicationProviderForm'
import { CommunicationProviderList } from './CommunicationProviderList'

function emptyDraft(): CommunicationProviderDraft {
  return { channel: 'email', kind: 'postmark', name: '', enabled: true, authType: 'none', authHeaderName: null, vaultProjectId: null, apiKeySecretName: null, settingsJson: '{}', fromEmail: '', fromName: '', messageStream: 'outbound', accountSid: '', fromNumber: '', endpoint: '' }
}

function readSetting(json: string, key: string): string {
  try {
    const parsed: unknown = JSON.parse(json)
    if (typeof parsed !== 'object' || parsed === null) return ''
    const value = Reflect.get(parsed, key)
    return typeof value === 'string' ? value : ''
  } catch { return '' }
}

function providerDraft(provider: CommunicationProvider): CommunicationProviderDraft {
  return { channel: provider.channel, kind: provider.kind, name: provider.name, enabled: provider.enabled, authType: provider.authType, authHeaderName: provider.authHeaderName, vaultProjectId: provider.vaultProjectId, apiKeySecretName: provider.apiKeySecretName, settingsJson: provider.settingsJson, fromEmail: readSetting(provider.settingsJson, 'fromEmail'), fromName: readSetting(provider.settingsJson, 'fromName'), messageStream: readSetting(provider.settingsJson, 'messageStream') || 'outbound', accountSid: readSetting(provider.settingsJson, 'accountSid'), fromNumber: readSetting(provider.settingsJson, 'fromNumber'), endpoint: readSetting(provider.settingsJson, 'endpoint') }
}

function providerInput(draft: CommunicationProviderDraft): CommunicationProviderInput {
  const settings = draft.kind === 'twilio'
    ? { accountSid: draft.accountSid.trim(), fromNumber: draft.fromNumber.trim(), ...(draft.endpoint.trim() === '' ? {} : { endpoint: draft.endpoint.trim() }) }
    : { fromEmail: draft.fromEmail.trim(), fromName: draft.fromName.trim(), ...(draft.kind === 'postmark' ? { messageStream: draft.messageStream.trim() || 'outbound' } : {}), ...(draft.endpoint.trim() === '' ? {} : { endpoint: draft.endpoint.trim() }) }
  return { channel: draft.channel, kind: draft.kind, name: draft.name.trim(), enabled: draft.enabled, authType: draft.authType, authHeaderName: draft.authType === 'header' ? draft.authHeaderName : null, vaultProjectId: draft.authType === 'none' ? null : draft.vaultProjectId, apiKeySecretName: draft.authType === 'none' ? null : draft.apiKeySecretName, settingsJson: JSON.stringify(settings) }
}

export function CommunicationsPage() {
  const { data } = useSuspenseQuery(communicationsQueryOptions)
  const queryClient = useQueryClient()
  const eventBus = useAppEventBus()
  const [showForm, setShowForm] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [draft, setDraft] = useState(emptyDraft)
  const [secrets, setSecrets] = useState<CommunicationSecret[]>([])
  const [message, setMessage] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    const changed = async (providerId: string, success: string): Promise<void> => {
      setMessage(success)
      await queryClient.invalidateQueries({ queryKey: communicationsQueryOptions.queryKey })
      await eventBus.publish('settings.communication-changed', { providerId })
    }
    const unsubscribeSave = eventBus.subscribe('settings.communication-save-requested', async ({ providerId, input }) => {
      setMessage(null); setBusy(true)
      try {
        const saved = providerId === null
          ? await createCommunicationProvider(input, AbortSignal.timeout(30_000))
          : await updateCommunicationProvider(providerId, input, AbortSignal.timeout(30_000))
        setShowForm(false); setEditingId(null)
        await changed(saved.id, providerId === null ? 'Provider added.' : 'Provider updated.')
      } catch (error: unknown) { setMessage(error instanceof Error ? error.message : 'Provider could not be saved.') }
      finally { setBusy(false) }
    })
    const unsubscribeAction = eventBus.subscribe('settings.communication-action-requested', async (action) => {
      setMessage(null); setBusy(true)
      try {
        if (action.kind === 'delete') { await deleteCommunicationProvider(action.providerId, AbortSignal.timeout(30_000)); await changed(action.providerId, 'Provider deleted.'); return }
        if (action.kind === 'default') { await setDefaultCommunicationProvider(action.providerId, AbortSignal.timeout(30_000)); await changed(action.providerId, 'Default provider updated.'); return }
        if (action.kind === 'two-factor') { await setCommunicationProviderTwoFactor(action.providerId, action.enabled, AbortSignal.timeout(30_000)); await changed(action.providerId, action.enabled ? 'Provider now delivers two-factor codes.' : 'Provider removed from two-factor delivery.'); return }
        if (action.kind === 'test') { const provider = await sendCommunicationProviderTest(action.providerId, action.recipient, AbortSignal.timeout(30_000)); setMessage(`Test message sent via ${provider}.`); return }
        const provider = data.providers.find(({ id }) => id === action.providerId)
        if (provider === undefined) throw new Error('Provider not found.')
        await updateCommunicationProvider(provider.id, { ...providerInput(providerDraft(provider)), enabled: action.enabled }, AbortSignal.timeout(30_000))
        await changed(provider.id, `Provider '${provider.name}' ${action.enabled ? 'enabled' : 'disabled'}.`)
      } catch (error: unknown) { setMessage(error instanceof Error ? error.message : 'Provider action failed.') }
      finally { setBusy(false) }
    })
    return () => { unsubscribeSave(); unsubscribeAction() }
  }, [data.providers, eventBus, queryClient])

  async function openAdd(): Promise<void> { await Promise.resolve(); setEditingId(null); setDraft(emptyDraft()); setSecrets([]); setShowForm(true) }
  async function closeForm(): Promise<void> { await Promise.resolve(); setShowForm(false) }
  async function changeDraft(value: CommunicationProviderDraft): Promise<void> { await Promise.resolve(); setDraft(value) }
  async function selectProject(projectId: string | null): Promise<void> {
    setDraft((current) => ({ ...current, vaultProjectId: projectId, apiKeySecretName: null }))
    setSecrets(projectId === null ? [] : await fetchCommunicationSecrets(projectId, AbortSignal.timeout(30_000)))
  }
  async function editProvider(provider: CommunicationProvider): Promise<void> {
    const value = providerDraft(provider); setDraft(value); setEditingId(provider.id); setShowForm(true)
    setSecrets(value.vaultProjectId === null ? [] : await fetchCommunicationSecrets(value.vaultProjectId, AbortSignal.timeout(30_000)))
  }
  async function saveDraft(): Promise<void> { await eventBus.publish('settings.communication-save-requested', { providerId: editingId, input: providerInput(draft) }) }

  return <div className="settings-page communications-page"><title>placecontext — Communications</title><header className="settings-page-head"><div><span className="settings-kicker">Workspace integrations</span><h1>Communications</h1><p>Connect providers for workflows and sign-in codes. Credentials are referenced from project Vaults and resolved only when a message is sent.</p></div><button className="dcbtn primary" onClick={() => void openAdd()} type="button">+ Add provider</button></header>{message === null ? null : <div className="settings-message" role="status">{message}</div>}<section className="dccard provider-card"><CommunicationProviderList busy={busy} onAction={async (action) => eventBus.publish('settings.communication-action-requested', action)} onEdit={editProvider} providers={data.providers} /></section>{showForm ? <CommunicationProviderForm busy={busy} draft={draft} editing={editingId !== null} onCancel={closeForm} onChange={changeDraft} onProjectChange={selectProject} onSave={saveDraft} projects={data.projects} secrets={secrets} /> : null}</div>
}
