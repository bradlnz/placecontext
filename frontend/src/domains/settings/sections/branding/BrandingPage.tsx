import { useMutation, useQueryClient, useSuspenseQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'

import { useAppEventBus } from '../../../../app/app-event-bus'
import { brandingQueryOptions } from '../../api/settings-queries'
import { resetBranding, saveBranding } from '../../api/settings-api'
import type { BrandingSettings } from '../../model/settings'

const DEFAULT_COLORS = {
  bgColor: '#0a0c0e',
  panelColor: '#0d1013',
  textColor: '#e6edf3',
  accentColor: '#43d675',
} as const

async function readLogo(file: File): Promise<string> {
  if (file.size > 200 * 1024) throw new Error('Logo too large — keep it under 200 KB.')
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.addEventListener('load', () => {
      if (typeof reader.result === 'string') resolve(reader.result)
      else reject(new Error('The logo could not be read.'))
    })
    reader.addEventListener('error', () => {
      reject(reader.error ?? new Error('The logo could not be read.'))
    })
    reader.readAsDataURL(file)
  })
}

export function BrandingPage() {
  const { data } = useSuspenseQuery(brandingQueryOptions)
  const queryClient = useQueryClient()
  const eventBus = useAppEventBus()
  const [branding, setBranding] = useState<BrandingSettings>(data)
  const [message, setMessage] = useState<string | null>(null)
  const saveMutation = useMutation({
    mutationFn: async (value: BrandingSettings) => saveBranding(value, AbortSignal.timeout(30_000)),
  })
  const resetMutation = useMutation({
    mutationFn: async () => resetBranding(AbortSignal.timeout(30_000)),
  })

  useEffect(() => {
    const unsubscribeSave = eventBus.subscribe(
      'settings.branding-save-requested',
      async ({ branding: value }) => {
        setMessage(null)
        try {
          const saved = await saveMutation.mutateAsync(value)
          setBranding(saved)
          queryClient.setQueryData(brandingQueryOptions.queryKey, saved)
          setMessage('Branding saved. Changes apply on the next page load.')
          await eventBus.publish('settings.branding-saved', { branding: saved })
        } catch (error: unknown) {
          setMessage(error instanceof Error ? error.message : 'Branding could not be saved.')
        }
      },
    )
    const unsubscribeReset = eventBus.subscribe('settings.branding-reset-requested', async () => {
      setMessage(null)
      try {
        const reset = await resetMutation.mutateAsync()
        setBranding(reset)
        queryClient.setQueryData(brandingQueryOptions.queryKey, reset)
        setMessage('Branding reset to the PlaceContext defaults.')
      } catch (error: unknown) {
        setMessage(error instanceof Error ? error.message : 'Branding could not be reset.')
      }
    })
    return () => {
      unsubscribeSave()
      unsubscribeReset()
    }
  }, [eventBus, queryClient, resetMutation, saveMutation])

  async function handleLogoSelected(file: File | undefined): Promise<void> {
    if (file === undefined) return
    setMessage(null)
    try {
      const logoDataUri = await readLogo(file)
      setBranding((current) => ({ ...current, logoDataUri }))
    } catch (error: unknown) {
      setMessage(error instanceof Error ? error.message : 'The logo could not be read.')
    }
  }

  async function handleNameChanged(productName: string): Promise<void> {
    await Promise.resolve()
    setBranding({ ...branding, productName: productName || null })
  }

  async function handleLogoRemoved(): Promise<void> {
    await Promise.resolve()
    setBranding({ ...branding, logoDataUri: null })
  }

  async function handleColorChanged(
    key: keyof typeof DEFAULT_COLORS,
    value: string,
  ): Promise<void> {
    await Promise.resolve()
    setBranding({ ...branding, [key]: value })
  }

  const busy = saveMutation.isPending || resetMutation.isPending

  return (
    <div className="settings-page branding-page">
      <title>placecontext — Branding</title>
      <h1>Whitelabel branding</h1>
      <p className="settings-intro">
        Make the portal yours — a name, a logo, and the shell colors. Applies to everyone in this
        workspace; leave a field empty to keep the default.
      </p>
      <section className="dccard settings-form-card">
        <div className="settings-field-grid two-columns">
          <label className="dcfield">
            <span>Product name</span>
            <input
              onChange={(event) => {
                void handleNameChanged(event.target.value)
              }}
              placeholder="placecontext"
              value={branding.productName ?? ''}
            />
          </label>
          <label className="dcfield">
            <span>
              Logo <code>png/svg, ≤200 KB</code>
            </span>
            <input
              accept="image/png,image/svg+xml,image/jpeg,image/webp"
              onChange={(event) => void handleLogoSelected(event.target.files?.[0])}
              type="file"
            />
          </label>
        </div>
        {branding.logoDataUri === null ? null : (
          <div className="branding-logo-row">
            <img alt="logo" src={branding.logoDataUri} />
            <button
              className="dcbtn"
              onClick={() => {
                void handleLogoRemoved()
              }}
              type="button"
            >
              remove logo
            </button>
          </div>
        )}
        <div className="settings-field-grid color-grid">
          {(
            [
              ['Background', 'bgColor'],
              ['Panels & cards', 'panelColor'],
              ['Text', 'textColor'],
              ['Accent', 'accentColor'],
            ] as const
          ).map(([label, key]) => (
            <label className="dcfield" key={key}>
              <span>{label}</span>
              <input
                aria-label={label}
                onChange={(event) => {
                  void handleColorChanged(key, event.target.value)
                }}
                type="color"
                value={branding[key] ?? DEFAULT_COLORS[key]}
              />
            </label>
          ))}
        </div>
        <div className="settings-actions">
          <button
            className="dcbtn primary"
            disabled={busy}
            onClick={() => void eventBus.publish('settings.branding-save-requested', { branding })}
            type="button"
          >
            {saveMutation.isPending ? 'Saving…' : 'Save branding'}
          </button>
          <button
            className="dcbtn"
            disabled={busy}
            onClick={() => void eventBus.publish('settings.branding-reset-requested', {})}
            type="button"
          >
            Reset to default
          </button>
        </div>
        {message === null ? null : (
          <div className="settings-message" role="status">
            {message}
          </div>
        )}
        <div className="settings-hint">
          Changes apply on the next page load. Colors override the dark theme&apos;s shell
          variables.
        </div>
      </section>
    </div>
  )
}
