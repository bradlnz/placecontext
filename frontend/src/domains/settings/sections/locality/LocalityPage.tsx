import { useMutation, useQueryClient, useSuspenseQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'

import { useAppEventBus } from '../../../../app/app-event-bus'
import { localityQueryOptions } from '../../api/settings-queries'
import { saveLocality } from '../../api/settings-api'

function previewNow(timeZoneId: string): string {
  try {
    return new Intl.DateTimeFormat('en-AU', {
      timeZone: timeZoneId,
      weekday: 'short',
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
    }).format(new Date())
  } catch {
    return 'unknown timezone'
  }
}

export function LocalityPage() {
  const { data } = useSuspenseQuery(localityQueryOptions)
  const queryClient = useQueryClient()
  const eventBus = useAppEventBus()
  const [timeZoneId, setTimeZoneId] = useState(data.timeZoneId)
  const [message, setMessage] = useState<string | null>(null)
  const saveMutation = useMutation({
    mutationFn: async (value: string) => saveLocality(value, AbortSignal.timeout(30_000)),
  })

  useEffect(
    () =>
      eventBus.subscribe('settings.locality-save-requested', async ({ timeZoneId: value }) => {
        setMessage(null)
        try {
          const saved = await saveMutation.mutateAsync(value)
          setTimeZoneId(saved.timeZoneId)
          queryClient.setQueryData(localityQueryOptions.queryKey, saved)
          setMessage('Timezone saved.')
          await eventBus.publish('settings.locality-saved', { timeZoneId: saved.timeZoneId })
        } catch (error: unknown) {
          setMessage(error instanceof Error ? error.message : 'The timezone could not be saved.')
        }
      }),
    [eventBus, queryClient, saveMutation],
  )

  async function handleTimeZoneChanged(value: string): Promise<void> {
    await Promise.resolve()
    setTimeZoneId(value)
  }

  return (
    <div className="settings-page locality-page">
      <title>placecontext — Locality</title>
      <h1>Locality</h1>
      <p className="settings-intro">
        The workspace timezone drives everything time-shaped: cron schedules fire in it, and every
        timestamp the portal shows is converted to it. Agents can also set it via the{' '}
        <code>set_workspace_timezone</code> MCP tool.
      </p>
      <section className="dccard settings-form-card">
        <label className="dcfield locality-field">
          <span>
            Workspace timezone <code>IANA</code>
          </span>
          <select
            onChange={(event) => {
              void handleTimeZoneChanged(event.target.value)
            }}
            value={timeZoneId}
          >
            {data.timeZones.map((zone) => (
              <option key={zone} value={zone}>
                {zone}
              </option>
            ))}
          </select>
        </label>
        <div className="locality-now">
          Now in <code>{timeZoneId}</code>: <strong>{previewNow(timeZoneId)}</strong>
        </div>
        <div className="settings-actions">
          <button
            className="dcbtn primary"
            disabled={saveMutation.isPending}
            onClick={() =>
              void eventBus.publish('settings.locality-save-requested', { timeZoneId })
            }
            type="button"
          >
            {saveMutation.isPending ? 'Saving…' : 'Save timezone'}
          </button>
        </div>
        {message === null ? null : (
          <div className="settings-message" role="status">
            {message}
          </div>
        )}
        <div className="settings-hint">
          Existing schedules keep their already-computed next fire time until they next run; new and
          re-saved schedules use the new zone immediately.
        </div>
      </section>
    </div>
  )
}
